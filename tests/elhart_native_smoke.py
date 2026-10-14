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
        report['launch'] = service.visible_start()
        deadline = time.monotonic() + 60
        while not service.visible.endpoint.is_file():
            if time.monotonic() > deadline:
                raise TimeoutError('Plugin endpoint did not appear; inspect editor and native log')
            time.sleep(.25)
        report['host'] = service.visible.request('health')
        assert report['host']['runtime_profile'] == 'elhart'
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
        report['state'] = complete(service.visible, 'state')
        report['pages'] = complete(service.visible, 'pages_list')
        report['saved'] = complete(service.visible, 'save_project')
        entry = report['saved']['project']
        report['closed'] = complete(service.visible, 'close_project')
        report['reopened'] = complete(service.visible, 'open_project', file=entry)
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
