"""Verified build handoff to an owned FSTools process. No editor endpoint required."""
from pathlib import Path
import hashlib
import json
import re
import shutil
import subprocess
import sys
import time
import uuid
import xml.etree.ElementTree as ET
from build_snapshot import input_manifest
from isolated_build_manager import launch_lock
from loader_windows import usb_devices,loader_processes

# Exact installed binary whose control and argument contract was inspected.
LOADER_SHA256 = 'f11efe167a80a67bd1d84966c97157c6da61f2549726e77174d3a6ed4d0759df'
TERMINAL={'completed','dry_run_completed','failed','ui_required','outcome_unknown','cleanup_failed'}


def read(path):return json.loads(Path(path).read_text(encoding='utf-8-sig'))


def digest(path):
    with Path(path).open('rb') as stream:return hashlib.file_digest(stream,'sha256').hexdigest()


class PanelDownloadManager:
    def __init__(self,root,workspace,bin_dir):
        self.root=Path(root).resolve();self.workspace=Path(workspace).resolve();self.bin=Path(bin_dir).resolve()
        self.jobs=self.root/'artifacts/download-jobs';self.jobs.mkdir(parents=True,exist_ok=True)

    def directory(self,id):
        if not isinstance(id,str) or not re.fullmatch('[0-9a-f]{32}',id):raise ValueError('Invalid job identifier')
        path=self.jobs/id
        if path.is_symlink() or not path.is_dir():raise ValueError('Unknown download job')
        return path

    def preflight(self,build_id,device_id):
        if not isinstance(build_id,str) or not re.fullmatch('[0-9a-f]{32}',build_id):raise ValueError('Invalid build_id')
        job=self.root/'artifacts/isolated-jobs'/build_id
        if job.is_symlink():raise ValueError('Build directory is a reparse point')
        state=read(job/'status.json');snapshot=read(job/'snapshot.json');outputs=read(job/'outputs.json')
        if state.get('status')!='completed' or state.get('compiler_success') is not True or state.get('cleanup_complete') is not True:
            raise ValueError('A completed successful isolated build with cleanup is required')
        project=Path(snapshot['source']).resolve(strict=True)
        if project.suffix.lower()!='.fsprj' or not project.is_relative_to(self.workspace) or Path(state['project']).resolve()!=project:
            raise ValueError('Build project does not belong to this workspace')
        if input_manifest(project.parent)!=snapshot['inputs']:raise ValueError('Saved project changed since build; rebuild first')
        folder=job/'outputs'
        files=input_manifest(folder)
        if str(folder.resolve()).casefold()!=str(Path(outputs['directory']).resolve()).casefold() or files!=outputs['files']:
            raise ValueError('Build outputs changed')
        for name in ('fshmi.bin','fshmi.logo'):
            if not files.get(name,{}).get('size'):raise ValueError('Missing compiled '+name)
        connection=ET.parse(project.parent/'HMI/System/ConnectInfo.cfg').getroot()
        # Installed ECP-10 model uses HMI ID 2050. Reject other targets in this pilot.
        if connection.findtext('{http://schemas.flexem.com/fs/model/1}ID')!='2050':
            raise ValueError('This implementation supports ECP-10 / F010 / ID2050 only')
        loader=self.bin/'FSTools.exe'
        if digest(loader)!=LOADER_SHA256:raise ValueError('Unsupported FSTools binary; inspect its contract first')
        devices=usb_devices()
        if len(devices)!=1 or devices[0].casefold()!=device_id.casefold():
            raise ValueError('Expected exactly one present VID0528/PIDA4A8 USB device matching device_id')
        existing=loader_processes()
        if existing:raise RuntimeError('Existing FSTools requires observation; it will not be reused or closed: '+str(existing))
        return dict(build_id=build_id,project=str(project),device_id=devices[0],transport='usb',model='ECP-10',
                    device_verification='present_usb_instance_only; native product/cpu checks occur on transfer',
                    bin_sha256=files['fshmi.bin']['sha256'],loader_sha256=LOADER_SHA256,
                    keep_current_ip=True,inputs=snapshot['inputs'],outputs=files,loader=str(loader),output_directory=str(folder.resolve()))

    def devices(self):return dict(transport='usb',devices=usb_devices(),verification='Windows present device instances; no panel communication')

    def status(self,download_id):
        directory=self.directory(download_id);request=read(directory/'request.json')
        if Path(request['workspace']).resolve()!=self.workspace:raise ValueError('Job belongs to another workspace')
        return self._status(directory)

    def _status(self,directory):
        # Atomic publication can briefly collide with an open reader on Windows.
        # A failed observation must never hide the identifier or invent a terminal state.
        queued=dict(download_id=directory.name,status='queued',cleanup_complete=False,hardware_success=None)
        deadline=time.monotonic()+2
        while True:
            try:return read(directory/'status.json')
            except FileNotFoundError:return queued
            except OSError as error:
                if time.monotonic()>=deadline:return dict(queued,status_read_error=str(error))
                time.sleep(.05)

    def start(self,build_id,device_id,*,dry_run=True):
        if type(dry_run) is not bool:raise ValueError('dry_run must be boolean')
        # Same mutex across installations. Do not queue concurrent transfers.
        with launch_lock('elhart-panel-download-global'):
            for directory in self.jobs.iterdir():
                if directory.is_dir() and re.fullmatch('[0-9a-f]{32}',directory.name):
                    # All workspace jobs share the hardware guard. A completed job in
                    # another workspace is harmless; a pending one must still block.
                    state=self._status(directory)
                    if state['status'] not in TERMINAL or not state.get('cleanup_complete') or state.get('recovery_required'):
                        raise RuntimeError('Previous download requires observation/recovery: '+directory.name)
            verified=self.preflight(build_id,device_id)
            id=uuid.uuid4().hex;directory=self.jobs/id;directory.mkdir()
            request=dict(root=str(self.root),workspace=str(self.workspace),bin=str(self.bin),build_id=build_id,device_id=device_id,dry_run=dry_run)
            (directory/'request.json').write_text(json.dumps(request),encoding='utf8')
            # Stage only verified sources/results. Never let FSTools write to the editor project.
            stage=directory/'stage';stage.mkdir()
            spawned=False;tracking_error=None
            try:
                project=Path(verified['project'])
                for relative in verified['inputs']:
                    target=stage/relative;target.parent.mkdir(parents=True,exist_ok=True);shutil.copyfile(project.parent/relative,target)
                if input_manifest(stage)!=verified['inputs']:raise RuntimeError('Inputs changed while staging')
                shutil.copytree(verified['output_directory'],stage/'Bin')
                if input_manifest(stage/'Bin')!=verified['outputs']:raise RuntimeError('Outputs changed while staging')
                (directory/'verified.json').write_text(json.dumps(verified),encoding='utf8')
                with (directory/'runner.log').open('wb') as log:
                    proc=subprocess.Popen([sys.executable,'-X','utf8',str(self.root/'panel_download_job.py'),str(directory/'request.json')],
                        cwd=self.root,stdin=subprocess.DEVNULL,stdout=log,stderr=log,creationflags=subprocess.CREATE_NO_WINDOW)
                    spawned=True
                try:(directory/'runner.json').write_text(json.dumps(dict(pid=proc.pid)),encoding='utf8')
                except OSError as error:tracking_error=str(error)
            except Exception as error:
                if not spawned:
                    (directory/'status.json').write_text(json.dumps(dict(download_id=id,status='failed',error=str(error),cleanup_complete=True,hardware_success=None,transmission_started=False)),encoding='utf8')
                else:
                    # Once Popen succeeds the worker owns the result, even if launcher
                    # bookkeeping/handle cleanup fails. Always return its download_id.
                    return dict(self._status(directory),launch_observation_error=str(error))
                raise
            result=self._status(directory)
            return dict(result,runner_tracking_error=tracking_error) if tracking_error else result

