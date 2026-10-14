"""Opt-in native integration against a new test project, never a user project."""
import argparse
from datetime import datetime, timezone
import hashlib
import json
from pathlib import Path
import sys
import shutil
import time
sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from server import FStudio, DEFAULT_BIN, HERE
from visible_host import VisibleHost
from build_snapshot import input_manifest


def complete(host, operation, **arguments):
    result = host.request(operation, **arguments)
    deadline = time.monotonic() + 90
    while 'job_id' in result:
        if result['status'] == 'completed':
            return result['result']
        if result['status'] == 'failed':
            raise RuntimeError(result['error'])
        if time.monotonic() > deadline:
            raise TimeoutError(f'Operation outcome pending; inspect job {result["job_id"]}')
        time.sleep(.1)
        result = host.request('job', job_id=result['job_id'])
    return result


def main():
    sys.stdout.reconfigure(encoding='utf8')
    parser = argparse.ArgumentParser()
    parser.add_argument('--name', required=True)
    parser.add_argument('--bin', type=Path, default=DEFAULT_BIN)
    parser.add_argument('--build', action='store_true')
    parser.add_argument('--widgets', action='store_true')
    parser.add_argument('--simulate', action='store_true')
    parser.add_argument('--from-project', type=Path)
    args = parser.parse_args()
    report = dict(started_utc=datetime.now(timezone.utc).isoformat(), scope='native_editor_smoke')
    destination = HERE/'artifacts/elhart-smoke'/args.name
    destination.mkdir(parents=True, exist_ok=True)
    service = FStudio(HERE/'projects', args.bin, 'elhart')
    try:
        # Each smoke run gets its own plugin and endpoint. Never restart a user's editor.
        addin = destination/'visible-addin'
        addin.mkdir(exist_ok=True)
        shutil.copy2(HERE/'artifacts/FStudioMcp.Visible.dll', addin/'FStudioMcp.Visible.dll')
        shutil.copy2(HERE/'artifacts/visible-addin/FStudioMcp.addin', addin/'FStudioMcp.addin')
        settings = dict(workspace=str(service.workspace), state_directory=str(destination/'visible-host'),
                        bin=str(service.bin), profile='elhart', log_file=str(service.profile.log_file))
        (addin/'host-settings.json').write_text(json.dumps(settings), encoding='utf8')
        service.visible = VisibleHost(destination/'visible-host/endpoint.json')
        report['bridge'] = service.call('fstudio_health', {})
        service.profile.require_supported(report['bridge']['dto_version'])
        report['models'] = service.list_models()
        report['dll_sha256'] = {name: hashlib.sha256((args.bin/name).read_bytes()).hexdigest()
                                for name in ('Flexem.Studio.Dtos.dll', 'Flexem.Studio.Build.dll',
                                             'Flexem.Studio.Core.dll', 'FStudio.exe')}
        cloned_entry=None
        if args.from_project:
            source=service.project(args.from_project.resolve())
            target=service.inside(args.name)
            if target.exists():raise ValueError('Test destination already exists')
            before=input_manifest(source)
            target.mkdir()
            for relative in before:
                copied=target/relative;copied.parent.mkdir(parents=True,exist_ok=True)
                shutil.copyfile(source/relative,copied)
            assert before==input_manifest(source), 'Source changed while copying'
            entry=next(target.glob('*.fsprj'))
            cloned_entry=target/(args.name+'.fsprj');entry.rename(cloned_entry)
            report['created']=dict(project=str(cloned_entry),copied_saved_native_project=True,source_unchanged=True)
        report['launch'] = service.visible_start(project=cloned_entry)
        deadline = time.monotonic() + 60
        while not service.visible.endpoint.is_file():
            if time.monotonic() > deadline:
                raise TimeoutError('Plugin endpoint did not appear; inspect editor and native log')
            time.sleep(.25)
        report['host'] = service.visible.request('health')
        assert report['host']['runtime_profile'] == 'elhart'
        if cloned_entry is None:
            report['created'] = service.visible_create_project(args.name, 'F010')
            job = report['created']
            deadline = time.monotonic() + 90
            while job['status'] not in ('completed','failed'):
                if time.monotonic() > deadline:
                    raise TimeoutError(f'Create outcome pending; inspect job {job["job_id"]}')
                time.sleep(.1)
                job = service.visible.request('job', job_id=job['job_id'])
            if job['status'] == 'failed':
                raise RuntimeError(job['error'])
            report['created'] = job['result']
        else:
            report['opened_copy']=complete(service.visible,'open_project',file=str(cloned_entry))
        report['state'] = complete(service.visible, 'state')
        report['pages'] = complete(service.visible, 'pages_list')
        if args.widgets:
            tool = 'Flexem.Studio.GraphicsDesigner.Tools.'
            def numeric(name, operation, address, x):
                return dict(name=name, tool=tool+'NumericCharTool',
                            tool_properties=dict(OperationAttrib=operation), x=x, y=200, width=170, height=60,
                            properties=[dict(member='NormalData.DataType', value='UInt16'),
                                        dict(member='NormalData.IntegerValue',value=5),
                                        dict(member='NormalData.DecimalValue',value=0),
                                        dict(member='NormalData.NumericLimit.UseLimit',value=True),
                                        dict(member='NormalData.NumericLimit.MinLimit.IsConstant',value=True),
                                        dict(member='NormalData.NumericLimit.MinLimit.Constant',value=0),
                                        dict(member='NormalData.NumericLimit.MaxLimit.IsConstant',value=True),
                                        dict(member='NormalData.NumericLimit.MaxLimit.Constant',value=1000)],
                            local_addresses=[dict(member='NormalData.ReadAddress',address=address)])
            widgets = [dict(name='main.title', tool=tool+'StaticTextTool', text='Проверка ELHART HMI MCP',
                            text_style=dict(font_size=28,color='#FF143D59',font_name='Arial',bold=True),
                            x=40,y=30,width=700,height=60),
                       dict(name='main.note', tool=tool+'StaticTextTool',
                            text='Тестовый экран ECP-10: значения и действия на внутренней памяти',
                            text_style=dict(font_size=18,font_name='Arial'),x=40,y=110,width=900,height=50),
                       numeric('main.value','NumericDisplay',100,40),
                       numeric('main.input','NumericInput',101,300),
                       dict(name='main.unit',tool=tool+'StaticTextTool',text='мбар',
                            text_style=dict(font_size=22,font_name='Arial'),x=218,y=205,width=75,height=50),
                       dict(name='main.circle',tool=tool+'EllipseTool',x=560,y=200,width=60,height=60),
                       dict(name='main.start',tool=tool+'SwitchTool',x=40,y=330,width=200,height=75)]
            if cloned_entry is None:
                report['widgets_created'] = service._wait_job(service.canvas_create_many(widgets), timeout=90)
            snapshot = complete(service.visible,'drawing_snapshot')
            report['layout_before_save'] = snapshot
            button = next(x for x in snapshot['items'] if x['name']=='main.start')
            if cloned_entry is None:
                report['button_action'] = complete(service.visible,'drawing_configure_bit_switch',
                                                   ref=button['model']['$ref'],action='Switch',address=20)
                report['button_indicator'] = complete(service.visible,'drawing_configure_bit_indicator',
                    ref=button['model']['$ref'],address=20,off_text='Пуск',on_text='Работает')
            report['button_properties'] = complete(service.visible,'inspect_object',ref=button['model']['$ref'])
            if cloned_entry is None:
                report['second_page'] = complete(service.visible,'page_create',name='Вторая страница')
        report['saved'] = complete(service.visible, 'save_project')
        entry = report['saved']['project']
        report['closed'] = complete(service.visible, 'close_project')
        report['reopened'] = complete(service.visible, 'open_project', file=entry)
        if args.widgets:
            snapshot = complete(service.visible,'drawing_snapshot')
            report['layout_after_reopen'] = snapshot
            assert {x['name'] for x in snapshot['items']} == {x['name'] for x in widgets}
            old_ref = button['model']['$ref']
            try:
                complete(service.visible, 'read_member',ref=old_ref,member='Comment')
            except RuntimeError as error:
                assert 'expired' in str(error)
                report['old_handle_rejected'] = True
            else:
                raise AssertionError('Stale model reference survived reopening')
        if args.build:
            result = service.builds.start(Path(entry))
            report['build'] = result
            print(json.dumps(dict(build_id=result['build_id'],status=result['status'])), flush=True)
            deadline = time.monotonic() + 180
            while result['status'] not in ('completed','failed','cancelled','ui_required','cleanup_failed'):
                if time.monotonic() > deadline:
                    raise TimeoutError('Build pending; inspect build_id '+result['build_id'])
                time.sleep(.5)
                result = service.builds.status(result['build_id'])
            report['build'] = result
            assert result.get('compiler_success') and result.get('cleanup_complete'), result
        if args.simulate:
            report['simulator_launch'] = complete(service.visible, 'simulation_start')
            report['simulator_state'] = complete(service.visible, 'simulation_state')
        report['success'] = True
    except Exception as error:
        report['success'] = False
        report['error'] = str(error)
        raise
    finally:
        service.native.close()
        (destination/'report.json').write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding='utf8')
        print(json.dumps(dict(success=report.get('success'), error=report.get('error'),
                              report=str(destination/'report.json'), project=report.get('saved')), ensure_ascii=False))


if __name__ == '__main__':
    main()
