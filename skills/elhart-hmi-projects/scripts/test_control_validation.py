"""Reproduce silently lost button settings on a disposable native starter."""
from pathlib import Path
import argparse
import subprocess
import sys
import tempfile
from native_xml import load, save, direct, children, set_text


def optional(node, name):
    matches = children(node, name)
    return matches[0] if matches else None


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--install-root', default='C:/Program Files (x86)/KIP/ELHART HMI Soft 3.x')
    args = parser.parse_args()
    scripts = Path(__file__).resolve().parent
    powershell = Path('C:/Windows/System32/WindowsPowerShell/v1.0/powershell.exe')
    if not powershell.is_file():
        raise RuntimeError('Windows PowerShell/.NET Framework is required')
    with tempfile.TemporaryDirectory(prefix='elhart_control_regression_') as temporary:
        project = Path(temporary)/'project'
        subprocess.run([sys.executable, str(scripts/'project_tool.py'), 'starter',
                        '--out', str(project), '--name', 'ControlRegression'],
                       check=True, capture_output=True)

        def validate(success, label, message=None):
            result = subprocess.run([str(powershell), '-NoProfile', '-ExecutionPolicy', 'Bypass',
                                     '-File', str(scripts/'validate_native.ps1'),
                                     '-ProjectRoot', str(project), '-InstallRoot', args.install_root],
                                    capture_output=True, text=True, errors='replace')
            if (result.returncode == 0) != success or (message and message not in result.stdout):
                raise AssertionError(f'{label}: unexpected result\n{result.stdout}\n{result.stderr}')
            print('PASS: '+label)

        validate(True, 'unmodified native starter')
        # Select a real command button, preserving its addresses, actions and macro IDs.
        selected = None
        for file in sorted((project/'HMI/Frame/Basic').glob('*.wcfg')):
            doc = load(file)
            for graphic in children(direct(doc.documentElement, 'Graphicses'), 'GraphicsBase'):
                switch = optional(graphic, 'SwitchSettings')
                control = optional(graphic, 'ControlSettingsProperty')
                if switch and control and direct(switch, 'UseSwitch').firstChild.data == 'true':
                    selected = file, doc, control
                    break
            if selected:
                break
        if not selected:
            raise AssertionError('Starter has no command button fixture')
        file, doc, control = selected
        field = optional(control, 'IsDisableWhenNotMatch')
        if field:
            control.removeChild(field)
        else:
            field = doc.createElementNS(control.namespaceURI, 'IsDisableWhenNotMatch')
        set_text(field, 'true')
        version = direct(control, 'Version')
        if not version:
            raise AssertionError('Expected native Order=1 Version fixture')
        control.insertBefore(field, version.nextSibling)
        save(file, doc)
        validate(True, 'Order=2 disable field after Version preserves settings')
        correct = file.read_bytes()
        control.removeChild(field)
        control.insertBefore(field, direct(control, 'IsEnabled'))
        save(file, doc)
        validate(False, 'premature Order=2 field rejected', 'ControlSettingsProperty DataContract order')
        file.write_bytes(correct)
        doc = load(file)
        # A syntactically valid nil would otherwise deserialize successfully.
        for graphic in children(direct(doc.documentElement, 'Graphicses'), 'GraphicsBase'):
            control = optional(graphic, 'ControlSettingsProperty')
            if control and optional(control, 'IsDisableWhenNotMatch'):
                enable = optional(control, 'EnableType')
                if not enable:
                    enable = doc.createElementNS(control.namespaceURI, 'EnableType')
                    # The native fixture has BeforeWordAddress followed by
                    # IsAutoPopupPasswordWindow/IsEnabled (unordered members).
                    before_word = direct(control, 'BeforeWordAddress')
                    control.insertBefore(enable, before_word.nextSibling)
                set_text(enable, 'Condition')
                set_text(direct(control, 'IsEnabled'), 'true')
                visibility = direct(control, 'VisibilityInfo')
                for child in list(visibility.childNodes):
                    visibility.removeChild(child)
                visibility.setAttributeNS('http://www.w3.org/2001/XMLSchema-instance', 'i:nil', 'true')
                break
        save(file, doc)
        validate(False, 'condition with nil visibility rejected', 'Condition control requires VisibilityInfo')
        file.write_bytes(correct)
        validate(True, 'repaired control settings pass again')


if __name__ == '__main__':
    main()
