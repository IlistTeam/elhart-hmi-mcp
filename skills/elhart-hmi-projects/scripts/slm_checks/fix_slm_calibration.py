"""Repair the known SLM demo on a saved closed copy, retaining native QNames."""
from pathlib import Path
import sys
sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from native_xml import NS, direct, load, save, set_text


def value(node):
    return ''.join(n.data for n in node.childNodes if n.nodeType in (n.TEXT_NODE, n.CDATA_SECTION_NODE))


def main(project):
    root = Path(project).resolve(strict=True)
    source_path = root/'slm_demo.c'
    expanded_path = root/'demo_expanded.c'
    source = source_path.read_text(encoding='utf8')
    expanded = expanded_path.read_text(encoding='utf8')
    old = 'else {w[CALRESULT]=1;w[CALSTEP]=5;event(w,15,0,1);}'
    new = 'else if(w[CALSTEP]==4) {w[CALRESULT]=1;w[CALSTEP]=5;event(w,15,0,1);}'
    if source.count(old) != 1 or expanded.count(old) != 1:
        raise ValueError('Expected the known unfinished calibration guard')
    macros = list((root/'HMI/Macro').glob('*.cfg'))
    if len(macros) != 1:
        raise ValueError('Expected one example macro')
    macro = load(macros[0])
    code = direct(macro.documentElement, 'SourceCode')
    if value(code).replace('\r\n', '\n') != source:
        raise ValueError('Native macro and source differ')
    windows = []
    for path in (root/'HMI/Frame/Basic').glob('*.wcfg'):
        document = load(path)
        if value(direct(document.documentElement, 'Id')) == '5':
            windows.append((path, document))
    if len(windows) != 1:
        raise ValueError('Expected one calibration page')
    path, document = windows[0]
    captions = [node for node in document.getElementsByTagNameNS(NS, 'Text')
                if value(node) == 'Тест аварии O₂']
    if len(captions) != 1:
        raise ValueError('Expected the known missing-glyph caption')
    set_text(captions[0], 'Тест аварии O2')
    fixed = source.replace(old, new)
    set_text(code, fixed)
    set_text(direct(macro.documentElement, 'NeedRebuild'), 'true')
    save(path, document)
    save(macros[0], macro)
    source_path.write_text(fixed, encoding='utf8')
    expanded_path.write_text(expanded.replace(old, new), encoding='utf8')
    print('Calibration completion is idempotent; O2 caption uses supported characters; macro needs rebuild.')


if __name__ == '__main__':
    main(sys.argv[1])
