"""Exercise system DTO/reference failures on a disposable native starter copy."""
from pathlib import Path
import argparse
import subprocess
import sys
import tempfile
from native_xml import load, save, direct, set_text


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--install-root', default='C:/Program Files (x86)/KIP/ELHART HMI Soft 3.x')
    args = parser.parse_args()
    scripts = Path(__file__).resolve().parent
    powershell = Path('C:/Windows/System32/WindowsPowerShell/v1.0/powershell.exe')
    if not powershell.is_file():
        raise RuntimeError('Windows PowerShell/.NET Framework is required')
    with tempfile.TemporaryDirectory(prefix='elhart_system_regression_') as temporary:
        project = Path(temporary) / 'project'
        subprocess.run([sys.executable, str(scripts/'project_tool.py'), 'starter',
                        '--out', str(project), '--name', 'SystemRegression'],
                       check=True, capture_output=True)
        cfg = project/'HMI/System/GlobalConfigInfo.cfg'
        original = cfg.read_bytes()

        def validate(expected_success, label, expected_message=None):
            result = subprocess.run([str(powershell), '-NoProfile', '-ExecutionPolicy', 'Bypass',
                                     '-File', str(scripts/'validate_native.ps1'),
                                     '-ProjectRoot', str(project), '-InstallRoot', args.install_root],
                                    capture_output=True, text=True, errors='replace')
            if (result.returncode == 0) != expected_success:
                raise AssertionError(f'{label}: unexpected result\n{result.stdout}\n{result.stderr}')
            if expected_message and expected_message not in result.stdout:
                raise AssertionError(f'{label}: expected targeted diagnostic\n{result.stdout}\n{result.stderr}')
            print('PASS: '+label)

        def mutate(member, value):
            cfg.write_bytes(original)
            doc = load(cfg)
            set_text(direct(doc.documentElement, member), value)
            save(cfg, doc)

        validate(True, 'unmodified native starter')
        mutate('InitWindow', '00000000-0000-0000-0000-000000000001')
        validate(False, 'missing initial window rejected', 'InitWindow points to a missing window')
        mutate('BacklitConservationTime', '0')
        validate(False, 'enabled backlight timeout rejected', 'requires a positive BacklitConservationTime')
        mutate('BuzzerTime', 'invalid-number')
        validate(False, 'invalid general-settings DTO rejected', 'GlobalConfigInfo.cfg')


if __name__ == '__main__':
    main()
