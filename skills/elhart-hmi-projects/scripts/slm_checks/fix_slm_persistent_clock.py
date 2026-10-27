"""Repair the known SLM example offline, preserving native XML QNames.

Use a saved, closed copy. Does not change addresses used by controls or the
100 ms window timers; reserves four formerly unused local words LW1084..1087.
"""
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from native_xml import NS, direct, load, save, set_text

OLD_DECL = 'static long last=0,sample_second=0;static int sample_slot=-1;int tick,refresh,slot,i;'
NEW_DECL = 'unsigned long seconds,previous;int clock_valid,tick,refresh,slot,i;'
OLD_CLOCK = '''gettimeofday(&tv,0);slot=tv.tv_usec/100000;
 tick=tv.tv_sec!=last;last=tv.tv_sec;
 refresh=tv.tv_sec!=sample_second||slot!=sample_slot;
 if(refresh){sample_second=tv.tv_sec;sample_slot=slot;}'''
NEW_CLOCK = '''gettimeofday(&tv,0);slot=tv.tv_usec/100000;seconds=(unsigned long)tv.tv_sec;
 /* Macro-local static data may be recreated on every HMI invocation.
    Keep the shared clock in reserved local words, not in the macro image.
    84=clock format marker; 85/86=last seconds low/high; 87=last 100 ms slot. */
 clock_valid=w[MAGIC]==17341&&w[84]==21324;
 previous=(unsigned long)w[85]|((unsigned long)w[86]<<16);
 tick=clock_valid&&seconds>previous;
 refresh=!clock_valid||seconds!=previous||slot!=w[87];'''
OLD_PUBLISH = '''elapsed_tenths(w,slot);
 SetMem(w,@DEMO_WORDS@,0,sizeof(w));'''
NEW_PUBLISH = '''elapsed_tenths(w,slot);
 /* logic() can initialise/clear the block, so publish the clock afterwards.
    A backwards wall-clock change rebases without adding a spurious second. */
 w[84]=21324;w[85]=(unsigned short)seconds;w[86]=(unsigned short)(seconds>>16);w[87]=(unsigned short)slot;
 SetMem(w,@DEMO_WORDS@,0,sizeof(w));'''


def main(project):
    root = Path(project).resolve(strict=True)
    path = root/'slm_demo.c'
    source = path.read_text(encoding='utf8')
    if 'clock_valid=w[MAGIC]==17341&&w[84]==21324;' in source:
        raise ValueError('Clock already repaired; do not apply twice')
    for old in (OLD_DECL, OLD_CLOCK, OLD_PUBLISH):
        if source.count(old) != 1:
            raise ValueError('Source differs from the supported SLM example')
    macros = list((root/'HMI/Macro').glob('*.cfg'))
    if len(macros) != 1:
        raise ValueError('Expected exactly one example macro')
    doc = load(macros[0])
    source_node = direct(doc.documentElement, 'SourceCode')
    original = ''.join(n.data for n in source_node.childNodes if n.nodeType in (n.TEXT_NODE, n.CDATA_SECTION_NODE))
    if original.replace('\r\n', '\n') != source:
        raise ValueError('Native SourceCode and slm_demo.c differ')
    # Refuse a conflict with native component addresses before reserving words.
    for frame in (root/'HMI/Frame').rglob('*.wcfg'):
        frame_doc = load(frame)
        for node in frame_doc.getElementsByTagNameNS(NS, 'MainAddress'):
            text = ''.join(n.data for n in node.childNodes if n.nodeType == n.TEXT_NODE)
            if text in {'1084', '1085', '1086', '1087'}:
                raise ValueError('Reserved clock word is used by '+str(frame))
    fixed = source.replace(OLD_DECL, NEW_DECL).replace(OLD_CLOCK, NEW_CLOCK).replace(OLD_PUBLISH, NEW_PUBLISH)
    # Check the expanded source before changing any project file.
    expanded_path = root/'demo_expanded.c'
    expanded = expanded_path.read_text(encoding='utf8')
    expanded_old = OLD_PUBLISH.replace('@DEMO_WORDS@', 'macroID, "0"')
    expanded_new = NEW_PUBLISH.replace('@DEMO_WORDS@', 'macroID, "0"')
    for old in (OLD_DECL, OLD_CLOCK, expanded_old):
        if expanded.count(old) != 1:
            raise ValueError('Expanded source differs from the supported example')
    fixed_expanded = expanded.replace(OLD_DECL, NEW_DECL).replace(OLD_CLOCK, NEW_CLOCK).replace(expanded_old, expanded_new)
    set_text(source_node, fixed)
    set_text(direct(doc.documentElement, 'NeedRebuild'), 'true')
    save(macros[0], doc)
    path.write_text(fixed, encoding='utf8')
    expanded_path.write_text(fixed_expanded, encoding='utf8')
    print('Repaired persistent SLM clock; reserved LW1084..1087; native macro marked for rebuild.')


if __name__ == '__main__':
    main(sys.argv[1])
