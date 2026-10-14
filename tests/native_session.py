"""Ownership and cleanup for opt-in native tests; never attach to a user editor."""
import ctypes
from ctypes import wintypes as W
import json
from pathlib import Path
import time
import uuid


def require_terminal(host, operation, timeout=15, **arguments):
    job = host.request(operation, **arguments)
    deadline = time.monotonic() + timeout
    while 'job_id' in job:
        if job['status'] == 'completed':
            return job['result']
        if job['status'] == 'failed':
            raise RuntimeError(job['error'])
        if time.monotonic() >= deadline:
            raise TimeoutError('Outcome pending; preserve job ' + job['job_id'])
        time.sleep(.05)
        job = host.request('job', job_id=job['job_id'])
    return job


class TestEditor:
    """Own the Popen handle and a Windows job containing only this test's tree.

    Closing the job handle does NOT kill its processes. Termination is allowed
    only after the native host acknowledges saved, closed, clean project state.
    A failed cleanup is reported and leaves the editor available for recovery.
    """
    def __init__(self):
        self.token = uuid.uuid4().hex
        self.process = None
        self.job = None
        self.kernel = ctypes.WinDLL('kernel32', use_last_error=True)
        self.kernel.CreateJobObjectW.argtypes = [ctypes.c_void_p, W.LPCWSTR]
        self.kernel.CreateJobObjectW.restype = W.HANDLE
        self.kernel.AssignProcessToJobObject.argtypes = [W.HANDLE, W.HANDLE]
        self.kernel.AssignProcessToJobObject.restype = W.BOOL
        self.kernel.TerminateJobObject.argtypes = [W.HANDLE, W.UINT]
        self.kernel.TerminateJobObject.restype = W.BOOL
        self.kernel.CloseHandle.argtypes = [W.HANDLE]
        self.kernel.CreateMutexW.argtypes = [ctypes.c_void_p,W.BOOL,W.LPCWSTR]
        self.kernel.CreateMutexW.restype = W.HANDLE
        self.kernel.WaitForSingleObject.argtypes = [W.HANDLE,W.DWORD]
        self.kernel.ReleaseMutex.argtypes = [W.HANDLE]
        self.mutex = self.kernel.CreateMutexW(None,False,'Local\\ElhartMcpNativeSmoke')
        if not self.mutex:
            raise ctypes.WinError(ctypes.get_last_error())
        if self.kernel.WaitForSingleObject(self.mutex,0) not in (0,128):
            self.kernel.CloseHandle(self.mutex)
            self.mutex = None
            raise RuntimeError('Another native smoke test is running; do not start another editor')

    def own(self, process):
        if self.process is not None:
            raise RuntimeError('Test already owns an editor')
        self.process = process
        self.job = self.kernel.CreateJobObjectW(None, None)
        if not self.job or not self.kernel.AssignProcessToJobObject(self.job, int(process._handle)):
            raise ctypes.WinError(ctypes.get_last_error())

    def cleanup(self, host, projects):
        try:
            if self.process is None:
                return dict(status='not_launched')
            if self.process.poll() is not None:
                # No native save proof: do not terminate any surviving children.
                return dict(status='editor_exited',pid=self.process.pid,
                            child_cleanup_confirmed=False)
            health = host.request('health')
            if health['pid'] != self.process.pid:
                raise RuntimeError('Endpoint is not the editor owned by this test')
            result = require_terminal(host, 'test_session_shutdown',
                token=self.token, pid=self.process.pid,
                projects=[str(Path(p).resolve()) for p in projects])
            if not result.get('shutdown_scheduled') or result.get('project') is not None or result.get('dirty_views') != 0:
                raise RuntimeError('Native host did not prove safe shutdown')
            # The editor may be held open by vendor modal windows. The job
            # contains only our freshly launched editor and its descendants.
            if self.job and not self.kernel.TerminateJobObject(self.job, 0):
                raise ctypes.WinError(ctypes.get_last_error())
            self.process.wait(timeout=10)
            return dict(status='closed',pid=self.process.pid,native=result,
                        child_cleanup_confirmed=bool(self.job))
        finally:
            if self.job:
                self.kernel.CloseHandle(self.job)
                self.job = None
            if self.mutex:
                self.kernel.ReleaseMutex(self.mutex)
                self.kernel.CloseHandle(self.mutex)
                self.mutex = None


def refuse_live_tests(directory):
    """Refuse another launch if any recorded test editor is still alive.

    Pipe observation failure does not establish process death. OpenProcess and
    WaitForSingleObject confirm whether the recorded process still exists.
    """
    kernel=ctypes.WinDLL('kernel32',use_last_error=True)
    kernel.OpenProcess.argtypes=[W.DWORD,W.BOOL,W.DWORD]
    kernel.OpenProcess.restype=W.HANDLE
    kernel.WaitForSingleObject.argtypes=[W.HANDLE,W.DWORD]
    kernel.CloseHandle.argtypes=[W.HANDLE]
    for endpoint in Path(directory).glob('*/visible-host/endpoint.json'):
        pid=int(json.loads(endpoint.read_text(encoding='utf-8-sig'))['pid'])
        handle=kernel.OpenProcess(0x100000,False,pid)
        if not handle:
            if ctypes.get_last_error()==87:
                continue
            raise RuntimeError(f'Cannot establish death of previous test PID {pid}')
        try:
            if kernel.WaitForSingleObject(handle,0)==258:
                raise RuntimeError(f'Previous test PID {pid} is still alive ({endpoint}); recover it before launching another editor')
        finally:
            kernel.CloseHandle(handle)
