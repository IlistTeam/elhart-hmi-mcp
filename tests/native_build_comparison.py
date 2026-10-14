"""Opt-in comparison of native in-editor and isolated saved-project builds."""
import json
from pathlib import Path
import shutil
import sqlite3
import sys
import time
sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
from server import FStudio,DEFAULT_BIN,HERE
from visible_host import VisibleHost
from build_snapshot import input_manifest
from native_session import TestEditor,refuse_live_tests,require_terminal


def project_database(path):
    """Read project data separately from compiler cache rows and their IDs."""
    with sqlite3.connect(path.resolve().as_uri()+'?mode=ro',uri=True) as connection:
        tables=[r[0] for r in connection.execute("select name from sqlite_master where type='table'")]
        result={}
        for table in tables:
            if table.startswith('BuildCache'):continue
            rows=connection.execute('select * from "'+table.replace('"','""')+'"').fetchall()
            if table=='sqlite_sequence':rows=[r for r in rows if not r[0].startswith('BuildCache')]
            result[table]=sorted(rows,key=repr)
        return result


def main():
    sys.stdout.reconfigure(encoding='utf8')
    name=sys.argv[1];source=Path(sys.argv[2]).resolve(strict=True)
    service=FStudio(HERE/'projects',DEFAULT_BIN,'elhart');editor=TestEditor()
    destination=HERE/'artifacts/elhart-smoke'/name;entries=[];report={}
    try:
        refuse_live_tests(destination.parent)
        if destination.exists():raise ValueError('Use a fresh test name')
        source_dir=service.project(source);before=input_manifest(source_dir)
        target=service.inside(name)
        if target.exists():raise ValueError('Test destination exists')
        target.mkdir()
        for relative in before:
            copied=target/relative;copied.parent.mkdir(parents=True,exist_ok=True)
            shutil.copy2(source_dir/relative,copied)
        entry=target/source.name;entries.append(entry)
        destination.mkdir();addin=destination/'visible-addin';addin.mkdir()
        for file in ('FStudioMcp.Visible.dll','FStudioMcp.addin'):
            shutil.copy2(HERE/'artifacts/visible-addin'/file,addin/file)
        settings=dict(workspace=str(service.workspace),state_directory=str(destination/'visible-host'),
            bin=str(service.bin),profile='elhart',log_file=str(service.profile.log_file),test_session_token=editor.token)
        (addin/'host-settings.json').write_text(json.dumps(settings),encoding='utf8')
        service.visible=VisibleHost(destination/'visible-host/endpoint.json')
        report['launch']=service.visible.start(service.bin,project=entry);editor.own(service.visible.started_process)
        deadline=time.monotonic()+60
        while not service.visible.endpoint.is_file():
            if time.monotonic()>deadline:raise TimeoutError('Endpoint pending')
            time.sleep(.25)
        require_terminal(service.visible,'state',timeout=60)
        require_terminal(service.visible,'save_project',timeout=60)
        saved=input_manifest(target)
        database_before=project_database(target/'HMI/System/fs2.s3db')
        native=require_terminal(service.visible,'build_start',timeout=60)
        report['in_editor_build']=native
        deadline=time.monotonic()+180
        while native['status'] not in ('completed','failed','cancelled','ui_required'):
            if time.monotonic()>deadline:raise TimeoutError('Observe native build_id '+native['build_id'])
            time.sleep(.25)
            native=service.visible.request('build_status',build_id=native['build_id'])
            report['in_editor_build']=native
        assert native['status']=='completed' and native['compiler_success'],native
        native_outputs=input_manifest(target/'Bin')
        assert native_outputs['fshmi.bin']['size']>0
        after_native=input_manifest(target)
        report['native_input_changes']=[k for k in sorted(set(saved)|set(after_native)) if saved.get(k)!=after_native.get(k)]
        assert set(report['native_input_changes']) <= {'HMI/System/fs2.s3db'},report['native_input_changes']
        report['project_database_preserved']=project_database(target/'HMI/System/fs2.s3db')==database_before
        assert report['project_database_preserved']
        isolated=service.builds.start(entry);report['isolated_build']=isolated
        deadline=time.monotonic()+180
        while isolated['status'] not in ('completed','failed','cancelled','ui_required','cleanup_failed'):
            if time.monotonic()>deadline:raise TimeoutError('Observe isolated build_id '+isolated['build_id'])
            time.sleep(.5)
            isolated=service.builds.status(isolated['build_id']);report['isolated_build']=isolated
        assert isolated['status']=='completed' and isolated['compiler_success'] and isolated['cleanup_complete']
        assert isolated['source_unchanged'] and input_manifest(target)==after_native
        archived=isolated['outputs']['files']
        report['output_differences']={key:dict(in_editor=native_outputs.get(key),isolated=archived.get(key))
            for key in sorted(set(native_outputs)|set(archived)) if native_outputs.get(key)!=archived.get(key)}
        report['compared_files']=len(native_outputs)
        report['identical_outputs']=not report['output_differences']
        report['source_unchanged']=input_manifest(source_dir)==before
        assert report['source_unchanged']
        # A binary difference is retained, never silently normalised. If this
        # happens, repeat the native build to record in-process reproducibility.
        # This does not establish semantic equivalence of differing binaries.
        # All external resources must match exactly.
        if not report['identical_outputs']:
            assert set(report['output_differences'])=={'fshmi.bin'},report['output_differences']
            assert native_outputs['fshmi.bin']['size']==archived['fshmi.bin']['size']
            repeat=require_terminal(service.visible,'build_start',timeout=60)
            deadline=time.monotonic()+180
            while repeat['status'] not in ('completed','failed','cancelled','ui_required'):
                if time.monotonic()>deadline:raise TimeoutError('Observe native repeat '+repeat['build_id'])
                time.sleep(.25)
                repeat=service.visible.request('build_status',build_id=repeat['build_id'])
            report['in_editor_repeat']=repeat
            assert repeat['status']=='completed' and repeat['compiler_success']
            repeated=input_manifest(target/'Bin')
            changed=[k for k in sorted(set(native_outputs)|set(repeated)) if native_outputs.get(k)!=repeated.get(k)]
            report['native_repeat_output_changes']=changed
            assert set(changed)<={'fshmi.bin'},changed
            assert repeated['fshmi.bin']['size']==native_outputs['fshmi.bin']['size']
            assert project_database(target/'HMI/System/fs2.s3db')==database_before
            report['verification']='both_native_build_paths_succeeded; external_resources_identical; differing_binary_not_semantically_compared'
        else:report['verification']='byte_identical_native_and_isolated_outputs'
        report['success']=True
    except Exception as error:report.update(success=False,error=str(error));raise
    finally:
        try:report['cleanup']=editor.cleanup(service.visible,entries)
        except Exception as error:report['cleanup']={'status':'failed','error':str(error)};report['success']=False
        service.native.close()
        if destination.exists():(destination/'report.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf8')
        print(json.dumps({k:report.get(k) for k in ('success','error','compared_files','identical_outputs','cleanup')},ensure_ascii=False))
    if not report.get('success'):raise RuntimeError('Build comparison failed')


if __name__=='__main__':main()
