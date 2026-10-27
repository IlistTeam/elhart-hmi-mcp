"""Extract the native example or check XML/QName integrity without rewriting."""
from pathlib import Path,PurePosixPath
import argparse,json,re,zipfile,sys
from native_xml import load,qname_types

def starter(args):
    destination=Path(args.out).resolve()
    if destination.exists():raise ValueError('Output already exists; choose a new folder')
    if not re.fullmatch(r'[A-Za-z][A-Za-z0-9_-]{0,63}',args.name):raise ValueError('Use an ASCII project name, starting with a letter, max64 chars')
    if args.name.upper() in {'CON','PRN','AUX','NUL',*(f'COM{i}' for i in range(1,10)),*(f'LPT{i}' for i in range(1,10))}:raise ValueError('Reserved Windows file name')
    archive=Path(__file__).resolve().parents[1]/'assets/slm-ecp10-starter.zip'
    with zipfile.ZipFile(archive) as z:
        for entry in z.infolist():
            p=PurePosixPath(entry.filename)
            if p.is_absolute() or '..' in p.parts or '\\' in entry.filename or ':' in entry.filename:raise ValueError('Unsafe archive path')
            if not (destination/p).resolve().is_relative_to(destination):raise ValueError('Archive path outside output')
        destination.mkdir(parents=True);z.extractall(destination)
    project=destination/'SLM_Powder_Loader_ECP10.fsprj'
    renamed=project.with_name(args.name+'.fsprj')
    if renamed!=project:project.rename(renamed)
    print(json.dumps({'project':str(renamed),'profile':'ECP10','size':[1024,768],'mode':'SLM internal-memory demo','compiled':False},ensure_ascii=False))

def check(args):
    root=Path(args.project).resolve()
    if not (root/'HMI/Frame').is_dir():raise ValueError('Missing HMI/Frame directory')
    files=[p for top in [root/'HMI',root/'Global',root/'Settings'] if top.exists() for p in top.rglob('*') if p.is_file() and p.suffix.lower() in {'.cfg','.xml','.wcfg','.fsvg'}]
    files+=list(root.glob('*.fsprj'))
    if not files:raise ValueError('No native XML files')
    bad=[];types=0
    for path in sorted(files):
        try:types+=len(qname_types(load(path)))
        except Exception as exc:bad.append({'file':str(path.relative_to(root)),'error':str(exc)})
    print(json.dumps({'files':len(files),'qname_types':types,'failures':bad},ensure_ascii=False,indent=2))
    if bad:raise SystemExit(1)

def main():
    parser=argparse.ArgumentParser();subs=parser.add_subparsers(dest='command',required=True)
    s=subs.add_parser('starter');s.add_argument('--out',required=True);s.add_argument('--name',default='HMI_Project');s.set_defaults(run=starter)
    s=subs.add_parser('xml-check');s.add_argument('--project',required=True);s.set_defaults(run=check)
    args=parser.parse_args();args.run(args)

if __name__=='__main__':
    sys.stdout.reconfigure(encoding='utf-8')
    main()
