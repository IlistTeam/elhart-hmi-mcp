"""Opt-in connection, project-switch and uncertain-write recovery test."""
import json
from pathlib import Path
import shutil
import sys
import time
sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
from server import FStudio,DEFAULT_BIN,HERE
from visible_host import VisibleHost
from build_snapshot import input_manifest
from native_session import TestEditor,refuse_live_tests,require_terminal


def main():
    sys.stdout.reconfigure(encoding='utf8')
    name=sys.argv[1];source=Path(sys.argv[2]).resolve(strict=True)
    service=FStudio(HERE/'projects',DEFAULT_BIN,'elhart');editor=TestEditor()
    destination=HERE/'artifacts/elhart-smoke'/name;entries=[];report={}
    try:
        refuse_live_tests(destination.parent)
        if destination.exists():raise ValueError('Use a fresh test name')
        source_dir=service.project(source);before=input_manifest(source_dir)
        for suffix in ('A','B'):
            target=service.inside(name+suffix)
            if target.exists():raise ValueError('Test destination exists')
            target.mkdir()
            for relative in before:
                copied=target/relative;copied.parent.mkdir(parents=True,exist_ok=True);shutil.copy2(source_dir/relative,copied)
            entry=next(target.glob('*.fsprj'));renamed=target/(target.name+'.fsprj');entry.rename(renamed);entries.append(renamed)
        destination.mkdir();addin=destination/'visible-addin';addin.mkdir()
        for file in ('FStudioMcp.Visible.dll','FStudioMcp.addin'):
            shutil.copy2(HERE/'artifacts/visible-addin'/file,addin/file)
        settings=dict(workspace=str(service.workspace),state_directory=str(destination/'visible-host'),
            bin=str(service.bin),profile='elhart',log_file=str(service.profile.log_file),test_session_token=editor.token)
        (addin/'host-settings.json').write_text(json.dumps(settings),encoding='utf8')
        service.visible=VisibleHost(destination/'visible-host/endpoint.json')
        report['launch']=service.visible.start(service.bin,project=entries[0]);editor.own(service.visible.started_process)
        deadline=time.monotonic()+60
        while not service.visible.endpoint.is_file():
            if time.monotonic()>deadline:raise TimeoutError('Endpoint pending')
            time.sleep(.25)
        report['health']=service.visible.request('health')
        state=require_terminal(service.visible,'state',timeout=60)
        report['contracts']=service.visible.request('health')['native_contracts']
        assert report['contracts']['compatible'],report['contracts']['errors']
        old_project_ref=state['project_model']['$ref']
        first=require_terminal(service.visible,'drawing_snapshot',timeout=60)
        old_graphic_ref=first['items'][0]['model']['$ref']
        report['switched']=require_terminal(service.visible,'open_project',timeout=60,file=str(entries[1]))
        switched=require_terminal(service.visible,'state',timeout=60)
        assert Path(switched['project']).resolve()==entries[1].resolve()
        assert switched['project_model']['$ref']!=old_project_ref
        for stale in (old_project_ref,old_graphic_ref):
            try:require_terminal(service.visible,'inspect_object',ref=stale)
            except RuntimeError as error:assert 'expired' in str(error),str(error)
            else:raise AssertionError('A reference survived switching projects')
        fresh=require_terminal(service.visible,'drawing_snapshot',timeout=60)
        unit=next(x for x in fresh['items'] if x['name']=='main.unit')
        report['project_switch_invalidated_refs']=True
        # The read observation budget is five seconds; the mutation runs once
        # for six seconds. A new client recovers its existing job and model.
        pending=service.visible.read('test_session_delayed_set',token=editor.token,pid=editor.process.pid,
            project=str(entries[1]),ref=unit['model']['$ref'],value=234)
        assert pending['status'] in ('running','queued'),pending
        job_id=pending['job_id'];report['observation_pending']=pending
        recovered=VisibleHost(service.visible.endpoint)
        deadline=time.monotonic()+30
        while True:
            job=recovered.request('job',job_id=job_id)
            if job['status']=='failed':raise RuntimeError(job['error'])
            if job['status']=='completed':break
            if time.monotonic()>deadline:raise TimeoutError('Original mutation job remains pending '+job_id)
            time.sleep(.05)
        snapshot=require_terminal(recovered,'drawing_snapshot',timeout=60,names=['main.unit'])
        assert snapshot['items'][0]['bounds']['x']==234
        assert snapshot['items'][0]['uuid']==unit['uuid']
        assert sum('开始 test_session_delayed_set' in line for line in (destination/'visible-host/visible-host.log').read_text(encoding='utf-8-sig').splitlines())==1
        report['recovered_original_job']=job_id;report['write_repeated']=False
        # Starting against the same endpoint reuses the PID and leaves the
        # current B project unchanged, even when another initial file is given.
        reuse=recovered.start(service.bin,project=entries[0]);assert reuse['pid']==editor.process.pid
        assert Path(require_terminal(recovered,'state',timeout=60)['project']).resolve()==entries[1].resolve()
        report['endpoint_reuse_preserved_project']=True
        report['saved']=require_terminal(recovered,'save_project',timeout=60)
        assert input_manifest(source_dir)==before
        report.update(success=True,source_unchanged=True)
    except Exception as error:report.update(success=False,error=str(error));raise
    finally:
        try:report['cleanup']=editor.cleanup(service.visible,entries)
        except Exception as error:report['cleanup']={'status':'failed','error':str(error)};report['success']=False
        service.native.close()
        if destination.exists():(destination/'report.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf8')
        print(json.dumps({k:report.get(k) for k in ('success','error','cleanup')},ensure_ascii=False))
    if not report['success']:raise RuntimeError('Connection acceptance failed')


if __name__=='__main__':main()
