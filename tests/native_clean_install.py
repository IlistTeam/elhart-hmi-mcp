"""Opt-in probe of a separately extracted and built source distribution."""
import argparse
import importlib.util
import json
from pathlib import Path
import shutil
import subprocess
import sys
import time
from native_session import TestEditor,refuse_live_tests,require_terminal


def main():
    sys.stdout.reconfigure(encoding='utf8')
    parser=argparse.ArgumentParser()
    parser.add_argument('--root',type=Path,required=True)
    parser.add_argument('--source-project',type=Path,required=True)
    parser.add_argument('--name',required=True)
    args=parser.parse_args()
    repository=Path(__file__).resolve().parents[1];root=args.root.resolve(strict=True)
    sys.path.insert(0,str(root))
    spec=importlib.util.spec_from_file_location('clean_server',root/'server.py')
    module=importlib.util.module_from_spec(spec);spec.loader.exec_module(module)
    from build_snapshot import input_manifest
    service=module.FStudio(repository/'projects',module.DEFAULT_BIN,profile='elhart')
    editor=TestEditor();report={};entry=None
    try:
        refuse_live_tests(repository/'artifacts/elhart-smoke')
        source=service.project(args.source_project.resolve());target=service.inside(args.name)
        if target.exists():raise ValueError('Probe destination exists')
        before=input_manifest(source);target.mkdir()
        for relative in before:
            copied=target/relative;copied.parent.mkdir(parents=True,exist_ok=True);shutil.copy2(source/relative,copied)
        entry=next(target.glob('*.fsprj'));renamed=target/(args.name+'.fsprj');entry.rename(renamed);entry=renamed
        settings_path=root/'artifacts/visible-addin/host-settings.json'
        settings=json.loads(settings_path.read_text(encoding='utf-8-sig'))
        settings['test_session_token']=editor.token
        settings_path.write_text(json.dumps(settings),encoding='utf8')
        report['launch']=service.visible_start(project=entry)
        if service.visible.started_process is None:raise RuntimeError('Probe must own a fresh editor')
        editor.own(service.visible.started_process)
        deadline=time.monotonic()+60
        while not service.visible.endpoint.is_file():
            if time.monotonic()>deadline:raise TimeoutError('Endpoint pending; do not restart')
            time.sleep(.25)
        report['host']=service.visible.request('health')
        assert report['host']['pid']==editor.process.pid
        report['state']=require_terminal(service.visible,'state',timeout=60)
        assert Path(report['state']['project']).resolve()==entry.resolve()
        messages=[{'jsonrpc':'2.0','id':1,'method':'initialize','params':{'protocolVersion':'2025-11-25'}},
            {'jsonrpc':'2.0','method':'notifications/initialized'},
            {'jsonrpc':'2.0','id':2,'method':'tools/call','params':{'name':'fstudio_visible_state','arguments':{}}},
            {'jsonrpc':'2.0','id':3,'method':'tools/call','params':{'name':'fstudio_canvas_snapshot','arguments':{}}}]
        run=subprocess.run([sys.executable,str(root/'server.py'),'--workspace',str(service.workspace),'--profile','elhart'],
            cwd=root,input='\n'.join(json.dumps(m) for m in messages)+'\n',capture_output=True,text=True,encoding='utf8',timeout=45)
        assert run.returncode==0,run.stderr
        rows=[json.loads(line) for line in run.stdout.splitlines()]
        for row in rows[1:]:assert not row['result']['isError'],row
        live=rows[1]['result']['structuredContent']
        if 'job_id' in live:
            # Observe an existing read job; never substitute a new request.
            live=service._wait_job(live)
        assert Path(live['project']).resolve()==entry.resolve()
        canvas=service._wait_job(rows[2]['result']['structuredContent'])
        assert any(x['name']=='main.status' for x in canvas['items'])
        report['stdio_native_canvas']=canvas
        report['saved']=require_terminal(service.visible,'save_project',timeout=60)
        assert input_manifest(source)==before,'Original source changed'
        report.update(success=True,source_unchanged=True,server_root=str(module.HERE),
            plugin_sha256=module.hashlib.sha256((root/'artifacts/FStudioMcp.Visible.dll').read_bytes()).hexdigest())
    except Exception as error:
        report.update(success=False,error=str(error));raise
    finally:
        try:report['cleanup']=editor.cleanup(service.visible,[entry] if entry else [])
        except Exception as error:report['cleanup']={'status':'failed','error':str(error)};report['success']=False
        service.native.close()
        (root.parent/'native-protocol-report.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf8')
        print(json.dumps({k:report.get(k) for k in ('success','error','cleanup','server_root')},ensure_ascii=False))
    if not report['success']:raise RuntimeError('Clean-install probe failed')


if __name__=='__main__':main()
