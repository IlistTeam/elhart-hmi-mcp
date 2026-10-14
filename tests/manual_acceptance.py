"""Opt-in native session for operator-assisted simulator acceptance.

Starts one owned editor and simulator; never injects UI input. Reports native
build/launch separately from the operator's visual and interaction evidence.
"""
import json
from pathlib import Path
import re
import shutil
import sys
import time

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from server import FStudio, DEFAULT_BIN, HERE
from visible_host import VisibleHost
from build_snapshot import input_manifest
from native_session import TestEditor, refuse_live_tests, require_terminal


def main():
    sys.stdout.reconfigure(encoding='utf8')
    name, source_argument = sys.argv[1:3]
    if not re.fullmatch(r'[A-Za-z][A-Za-z0-9_-]{0,40}', name):
        raise ValueError('Use a short fresh session name')
    source = Path(source_argument).resolve(strict=True)
    service = FStudio(HERE/'projects', DEFAULT_BIN, 'elhart')
    source_directory = service.project(source)
    before = input_manifest(source_directory)
    target = service.inside(name)
    destination = HERE/'artifacts/manual-acceptance'/name
    if target.exists() or destination.exists():
        raise ValueError('Use a fresh session name; never overwrite or relaunch')
    editor = TestEditor()
    entries, report = [], {'verification': 'operator_evidence_pending'}
    try:
        refuse_live_tests(destination.parent)
        target.mkdir()
        for relative in before:
            copied = target/relative
            copied.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(source_directory/relative, copied)
        entry = target/source.name
        entries.append(entry)
        destination.mkdir(parents=True)
        addin = destination/'visible-addin'
        addin.mkdir()
        for file in ('FStudioMcp.Visible.dll', 'FStudioMcp.addin'):
            shutil.copy2(HERE/'artifacts/visible-addin'/file, addin/file)
        settings = dict(workspace=str(service.workspace),
                        state_directory=str(destination/'visible-host'),
                        bin=str(service.bin), profile='elhart',
                        log_file=str(service.profile.log_file),
                        test_session_token=editor.token)
        (addin/'host-settings.json').write_text(json.dumps(settings), encoding='utf8')
        service.visible = VisibleHost(destination/'visible-host/endpoint.json')
        report['launch'] = service.visible.start(service.bin, project=entry)
        editor.own(service.visible.started_process)
        deadline = time.monotonic()+60
        while not service.visible.endpoint.is_file():
            if time.monotonic() > deadline:
                raise TimeoutError('Endpoint pending; do not launch another editor')
            time.sleep(.25)
        report['state'] = require_terminal(service.visible, 'state', timeout=60)
        require_terminal(service.visible, 'save_project', timeout=60)
        build = require_terminal(service.visible, 'build_start', timeout=60)
        report['build'] = build
        deadline = time.monotonic()+180
        while build['status'] not in ('completed', 'failed', 'cancelled', 'ui_required'):
            if time.monotonic() > deadline:
                raise TimeoutError('Observe original native build '+build['build_id'])
            time.sleep(.25)
            build = service.visible.request('build_status', build_id=build['build_id'])
            report['build'] = build
        if build['status'] != 'completed' or not build['compiler_success']:
            raise RuntimeError('Native build did not succeed')
        report['simulator_launch'] = service.visible.request('simulation_start')
        deadline = time.monotonic()+60
        while True:
            state = service.visible.read('simulation_state')
            report['simulator_state'] = state
            if state.get('launch_error'):
                raise RuntimeError(state['launch_error'])
            if any(p['name'] == 'FSGUI' for p in state.get('new_process_candidates', [])):
                break
            if time.monotonic() > deadline:
                raise TimeoutError('Observe original simulator job; do not replay launch')
            time.sleep(.25)
        (destination/'ready.json').write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding='utf8')
        print(json.dumps(dict(ready=True, project=str(entry), pid=editor.process.pid,
                             compiler_success=True, simulator=state,
                             verification='operator_evidence_pending',
                             automatic_close_seconds=1800), ensure_ascii=False), flush=True)
        deadline = time.monotonic()+1800
        while time.monotonic() < deadline and not (destination/'stop').exists():
            if editor.process.poll() is not None:
                raise RuntimeError('Editor exited before native cleanup')
            time.sleep(.5)
        report['saved'] = require_terminal(service.visible, 'save_project', timeout=60)
    except Exception as error:
        report['error'] = str(error)
        print(json.dumps(dict(error=str(error)), ensure_ascii=False), flush=True)
    finally:
        try:
            report['cleanup'] = editor.cleanup(service.visible, entries)
        except Exception as error:
            report['cleanup'] = dict(status='failed', error=str(error))
        report['source_unchanged'] = input_manifest(source_directory) == before
        service.native.close()
        if destination.exists():
            (destination/'report.json').write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding='utf8')
        print(json.dumps(dict(cleanup=report.get('cleanup'),
                              source_unchanged=report['source_unchanged']), ensure_ascii=False), flush=True)
    if report.get('error'):
        raise RuntimeError(report['error'])


if __name__ == '__main__':
    main()
