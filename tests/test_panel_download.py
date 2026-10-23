import contextlib
import hashlib
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import Mock,patch
import panel_download as dl
import panel_download_job as worker
from build_snapshot import input_manifest


class PreflightTests(unittest.TestCase):
    def setUp(self):
        self.temp=tempfile.TemporaryDirectory();self.addCleanup(self.temp.cleanup)
        self.root=Path(self.temp.name);self.project=self.root/'projects/p';self.project.mkdir(parents=True)
        (self.project/'p.fsprj').write_text('<FsProject/>')
        system=self.project/'HMI/System';system.mkdir(parents=True)
        (system/'ConnectInfo.cfg').write_text('<ConnectInfo xmlns="http://schemas.flexem.com/fs/model/1"><ID>2050</ID></ConnectInfo>')
        self.id='a'*32;self.job=self.root/'artifacts/isolated-jobs'/self.id;self.job.mkdir(parents=True)
        self.outputs=self.job/'outputs';self.outputs.mkdir()
        for name in ('fshmi.bin','fshmi.logo'):(self.outputs/name).write_bytes(b'compiled')
        self.bin=self.root/'bin';self.bin.mkdir();(self.bin/'FSTools.exe').write_bytes(b'test binary')
        self.write('status.json',dict(status='completed',compiler_success=True,cleanup_complete=True,project=str(self.project/'p.fsprj')))
        self.write('snapshot.json',dict(source=str(self.project/'p.fsprj'),inputs=input_manifest(self.project)))
        self.write('outputs.json',dict(directory=str(self.outputs),files=input_manifest(self.outputs)))
        self.manager=dl.PanelDownloadManager(self.root,self.root/'projects',self.bin)
        self.device='USB\\VID_0528&PID_A4A8\\TEST'
        self.hashpatch=patch.object(dl,'LOADER_SHA256',dl.digest(self.bin/'FSTools.exe'));self.hashpatch.start();self.addCleanup(self.hashpatch.stop)
        self.usbpatch=patch.object(dl,'usb_devices',return_value=[self.device]);self.usb=self.usbpatch.start();self.addCleanup(self.usbpatch.stop)
        self.processpatch=patch.object(dl,'loader_processes',return_value=[]);self.processes=self.processpatch.start();self.addCleanup(self.processpatch.stop)

    def write(self,name,value):(self.job/name).write_text(json.dumps(value))
    def check(self):return self.manager.preflight(self.id,self.device)
    def test_valid_build(self):self.assertTrue(self.check()['keep_current_ip'])
    def test_changed_saved_inputs(self):
        (self.project/'p.fsprj').write_text('changed')
        with self.assertRaisesRegex(ValueError,'changed since build'):self.check()
    def test_changed_outputs(self):
        (self.outputs/'fshmi.bin').write_bytes(b'changed')
        with self.assertRaisesRegex(ValueError,'outputs changed'):self.check()
    def test_failed_build(self):
        self.write('status.json',dict(status='failed'))
        with self.assertRaisesRegex(ValueError,'successful'):self.check()
    def test_multiple_devices(self):
        self.usb.return_value=[self.device,self.device+'2']
        with self.assertRaisesRegex(ValueError,'exactly one'):self.check()
    def test_wrong_device(self):
        self.usb.return_value=[self.device+'2']
        with self.assertRaises(ValueError):self.check()
    def test_unknown_binary(self):
        (self.bin/'FSTools.exe').write_bytes(b'new version')
        with self.assertRaisesRegex(ValueError,'Unsupported'):self.check()
    def test_wrong_model(self):
        path=self.project/'HMI/System/ConnectInfo.cfg';path.write_text(path.read_text().replace('2050','2051'))
        self.write('snapshot.json',dict(source=str(self.project/'p.fsprj'),inputs=input_manifest(self.project)))
        with self.assertRaisesRegex(ValueError,'ECP-10'):self.check()
    def test_outside_workspace(self):
        self.manager.workspace=self.root/'other'
        with self.assertRaisesRegex(ValueError,'workspace'):self.check()
    def test_invalid_id(self):
        with self.assertRaises(ValueError):self.manager.preflight('../escape',self.device)
    def test_existing_loader_rejected_without_touching_it(self):
        self.processes.return_value=[dict(pid=99,parent_pid=88)]
        with self.assertRaisesRegex(RuntimeError,'Existing FSTools'):self.check()

    def start(self):
        with patch.object(dl,'launch_lock',return_value=contextlib.nullcontext()),patch.object(dl.subprocess,'Popen',return_value=Mock(pid=123)) as spawn:
            return self.manager.start(self.id,self.device),spawn

    def test_completed_job_other_workspace_does_not_block(self):
        prior=self.manager.jobs/('b'*32);prior.mkdir()
        (prior/'request.json').write_text(json.dumps(dict(workspace=str(self.root/'another-workspace'))))
        (prior/'status.json').write_text(json.dumps(dict(status='dry_run_completed',cleanup_complete=True)))
        result,spawn=self.start()
        self.assertEqual(result['status'],'queued');spawn.assert_called_once()

    def test_pending_job_other_workspace_still_blocks(self):
        prior=self.manager.jobs/('b'*32);prior.mkdir()
        (prior/'request.json').write_text(json.dumps(dict(workspace=str(self.root/'another-workspace'))))
        (prior/'status.json').write_text(json.dumps(dict(status='transferring',cleanup_complete=False)))
        with self.assertRaisesRegex(RuntimeError,'Previous download'):self.start()

    def test_pid_record_failure_never_claims_worker_finished(self):
        original=Path.write_text
        def write(path,*args,**kwargs):
            if path.name=='runner.json':raise PermissionError('simulated PID record failure')
            return original(path,*args,**kwargs)
        with patch.object(Path,'write_text',write):result,spawn=self.start()
        spawn.assert_called_once();self.assertIn('runner_tracking_error',result)
        self.assertFalse(result['cleanup_complete']);self.assertEqual(result['status'],'queued')
        self.assertFalse((self.manager.directory(result['download_id'])/'status.json').exists())

    def test_spawn_failure_is_safe_terminal_failure(self):
        with patch.object(dl,'launch_lock',return_value=contextlib.nullcontext()),patch.object(dl.subprocess,'Popen',side_effect=OSError('spawn failed')):
            with self.assertRaisesRegex(OSError,'spawn failed'):self.manager.start(self.id,self.device)
        states=[dl.read(p/'status.json') for p in self.manager.jobs.iterdir()]
        self.assertEqual(len(states),1);self.assertEqual(states[0]['status'],'failed')
        self.assertFalse(states[0]['transmission_started']);self.assertTrue(states[0]['cleanup_complete'])

    def test_transient_status_read_failure_is_retried(self):
        directory=self.manager.jobs/('c'*32);directory.mkdir()
        expected=dict(status='transferring',cleanup_complete=False)
        with patch.object(dl,'read',side_effect=[PermissionError('sharing violation'),expected]) as reader:
            self.assertEqual(self.manager._status(directory),expected)
        self.assertEqual(reader.call_count,2)

    def test_permanent_status_read_failure_is_nonterminal(self):
        directory=self.manager.jobs/('c'*32);directory.mkdir()
        with patch.object(dl,'read',side_effect=PermissionError('denied')),patch.object(dl.time,'monotonic',side_effect=[0,3]):
            result=self.manager._status(directory)
        self.assertEqual(result['download_id'],directory.name);self.assertIn('status_read_error',result)
        self.assertFalse(result['cleanup_complete'])


class WorkerTests(unittest.TestCase):
    def execute(self,observation,dry_run=False,cleanup_error=False,other_loader=False):
        with tempfile.TemporaryDirectory() as temp:
            root=Path(temp);request=dict(root=str(root),workspace=str(root),bin=str(root),build_id='a'*32,device_id='test',dry_run=dry_run)
            verified=dict(inputs={},outputs={},project='p',build_id='a'*32,device_id='test',bin_sha256='hash',loader='loader')
            (root/'request.json').write_text(json.dumps(request));(root/'verified.json').write_text(json.dumps(verified))
            proc=Mock(pid=123);proc.name='private';proc.poll.return_value=0
            if cleanup_error:proc.close.side_effect=RuntimeError('cleanup failed')
            controls=Mock();controls.configure.return_value={'keep_current_ip':True}
            controls.observe.side_effect=[dict(progress=0,messages=[],dialogs=[]),observation]
            with patch.object(worker,'launch_lock',return_value=contextlib.nullcontext()),patch.object(worker.PanelDownloadManager,'preflight',return_value=verified),patch.object(worker,'input_manifest',return_value={}),patch.object(worker,'usb_devices',return_value=['test']),patch.object(worker,'loader_processes',return_value=[dict(pid=123),dict(pid=456)] if other_loader else [dict(pid=123)]):
                result=worker.run(root/'request.json',process_factory=Mock(return_value=proc),controls_factory=Mock(return_value=controls))
            return result,controls,proc
    def test_dry_run_never_dispatches(self):
        result,controls,proc=self.execute({},dry_run=True)
        controls.start.assert_not_called();proc.close.assert_called_once()
        self.assertEqual(result['status'],'dry_run_completed');self.assertFalse(result['transmission_started']);self.assertIsNone(result['hardware_success']);self.assertTrue(result['cleanup_complete'])
    def test_explicit_success(self):
        result,c,p=self.execute(dict(progress=100,messages=['Download Success!'],dialogs=[]))
        c.start.assert_called_once();self.assertTrue(result['hardware_success']);self.assertEqual(result['status'],'completed')
    def test_individual_file_success_is_insufficient(self):
        result,c,p=self.execute(dict(progress=100,messages=['Download fshmi.bin success!'],dialogs=[]))
        self.assertEqual(result['status'],'outcome_unknown');self.assertIsNone(result['hardware_success'])
    def test_native_failure_requires_recovery(self):
        result,c,p=self.execute(dict(progress=50,messages=['Download failed!'],dialogs=[]))
        self.assertEqual(result['status'],'failed');self.assertTrue(result['recovery_required'])
    def test_unknown_dialog_never_confirmed(self):
        result,c,p=self.execute(dict(progress=0,messages=[],dialogs=[dict(child_text=['Wrong model. Continue?'])]))
        self.assertEqual(result['status'],'ui_required');self.assertTrue(result['recovery_required'])
    def test_cleanup_failure_preserved(self):
        result,c,p=self.execute({},dry_run=True,cleanup_error=True)
        self.assertEqual(result['status'],'cleanup_failed');self.assertFalse(result['cleanup_complete'])
    def test_new_other_loader_prevents_dispatch(self):
        result,controls,proc=self.execute({},other_loader=True)
        controls.start.assert_not_called();self.assertFalse(result['transmission_started'])
        self.assertEqual(result['status'],'failed');self.assertTrue(result['cleanup_complete'])


if __name__=='__main__':unittest.main()
