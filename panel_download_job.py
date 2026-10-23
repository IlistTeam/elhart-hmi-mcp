"""Durable download worker. dry_run never posts the native Download command."""
import json
from contextlib import ExitStack
from pathlib import Path
import sys
import time
from datetime import datetime,timezone
from desktop_process import DesktopProcess
from isolated_build_manager import launch_lock
from panel_download import PanelDownloadManager,read
from loader_windows import LoaderControls,usb_devices
from build_snapshot import input_manifest


def run(request_path,process_factory=DesktopProcess,controls_factory=LoaderControls):
    path=Path(request_path).resolve();directory=path.parent;request=read(path)
    state=dict(download_id=directory.name,status='validating',hardware_success=None,transmission_started=False,cleanup_complete=False,dry_run=request['dry_run'])
    proc=None;controls=None;guards=ExitStack()
    def publish(**changes):
        state.update(changes);state['updated_utc']=datetime.now(timezone.utc).isoformat()
        temp=directory/'status.tmp';temp.write_text(json.dumps(state,ensure_ascii=False,indent=2),encoding='utf8')
        deadline=time.monotonic()+3
        while True:
            try:temp.replace(directory/'status.json');return
            except PermissionError:
                if time.monotonic()>deadline:raise
                time.sleep(.05)
    try:
        guards.enter_context(launch_lock('elhart-panel-download-global'))
        publish()
        manager=PanelDownloadManager(request['root'],request['workspace'],request['bin'])
        verified=manager.preflight(request['build_id'],request['device_id'])
        if verified!=read(directory/'verified.json'):raise RuntimeError('Preflight changed before launch')
        stage=directory/'stage'
        if input_manifest(stage)!=verified['inputs'] or input_manifest(stage/'Bin')!=verified['outputs']:
            raise RuntimeError('Staged files changed before launch')
        publish(status='initializing',project=verified['project'],build_id=verified['build_id'],device_id=verified['device_id'],bin_sha256=verified['bin_sha256'])
        proc=process_factory(verified['loader'],['-D',str(stage/'Bin'),'-c','en','-o','119'],cwd=stage)
        publish(worker_pid=proc.pid,desktop=proc.name)
        controls=controls_factory(proc);controls.attach()
        options=controls.configure(stage/'Bin')
        observed=controls.observe()
        if observed['dialogs']:raise RuntimeError('Native initialization requires user input: '+str(observed['dialogs']))
        publish(status='ready',options=options,**observed)
        if request['dry_run']:
            publish(status='dry_run_completed',hardware_success=None)
        else:
            # Revalidate the exact present USB instance immediately before dispatch.
            if [d.casefold() for d in usb_devices()]!=[verified['device_id'].casefold()]:raise RuntimeError('USB target changed')
            # Publish BEFORE dispatch so interruption can never be reported safe to retry.
            publish(status='transferring',transmission_started=True)
            controls.start();deadline=time.monotonic()+600
            while True:
                observed=controls.observe();publish(**observed)
                text=[t.strip() for t in observed['messages']]
                text += [t.strip() for d in observed['dialogs'] for t in d.get('child_text',[])]
                if 'Download Success!' in text:
                    publish(status='completed',hardware_success=True);break
                if 'Download failed!' in text:
                    publish(status='failed',hardware_success=False,recovery_required=True);break
                if observed['dialogs']:
                    publish(status='ui_required',recovery_required=True,error='Native dialog was not acknowledged automatically');break
                if proc.poll() is not None or time.monotonic()>deadline:
                    publish(status='outcome_unknown',recovery_required=True,error='No explicit native completion before exit/timeout');break
                time.sleep(.25)
    except BaseException as error:
        publish(status='outcome_unknown' if state['transmission_started'] else 'failed',error=str(error),recovery_required=state['transmission_started'])
    finally:
        try:
            if controls:controls.close()
            if proc:
                deadline=time.monotonic()+3
                while proc.poll() is None and time.monotonic()<deadline:time.sleep(.1)
                proc.close()
            publish(cleanup_complete=True)
        except BaseException as error:publish(status='cleanup_failed',cleanup_complete=False,cleanup_error=str(error),recovery_required=True)
        guards.close()
    return state


if __name__=='__main__':run(sys.argv[1])
