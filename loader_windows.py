"""FSTools 2.1.9.1 controls, scoped exclusively to an owned private desktop."""
import ctypes
from ctypes import wintypes as W
import time


def loader_processes():
    """Observe all FSTools instances, including workers orphaned by a crashed runner."""
    kernel=ctypes.WinDLL('kernel32',use_last_error=True)
    class Entry(ctypes.Structure):
        _fields_=[('dwSize',W.DWORD),('cntUsage',W.DWORD),('pid',W.DWORD),
                  ('heap',ctypes.c_size_t),('module',W.DWORD),('threads',W.DWORD),
                  ('parent_pid',W.DWORD),('priority',W.LONG),('flags',W.DWORD),('exe',W.WCHAR*260)]
    kernel.CreateToolhelp32Snapshot.argtypes=[W.DWORD,W.DWORD]
    kernel.CreateToolhelp32Snapshot.restype=W.HANDLE
    kernel.Process32FirstW.argtypes=[W.HANDLE,ctypes.POINTER(Entry)]
    kernel.Process32NextW.argtypes=[W.HANDLE,ctypes.POINTER(Entry)]
    kernel.CloseHandle.argtypes=[W.HANDLE]
    snapshot=kernel.CreateToolhelp32Snapshot(2,0)
    if snapshot==ctypes.c_void_p(-1).value:raise ctypes.WinError(ctypes.get_last_error())
    result=[]
    try:
        entry=Entry();entry.dwSize=ctypes.sizeof(entry)
        present=kernel.Process32FirstW(snapshot,ctypes.byref(entry))
        while present:
            if entry.exe.casefold()=='fstools.exe':result.append(dict(pid=entry.pid,parent_pid=entry.parent_pid))
            present=kernel.Process32NextW(snapshot,ctypes.byref(entry))
        if ctypes.get_last_error()!=18:raise ctypes.WinError(ctypes.get_last_error())
    finally:kernel.CloseHandle(snapshot)
    return result


def usb_devices():
    setup=ctypes.WinDLL('setupapi',use_last_error=True)
    class Info(ctypes.Structure):
        _fields_=[('cbSize',W.DWORD),('ClassGuid',ctypes.c_byte*16),('DevInst',W.DWORD),('Reserved',ctypes.c_size_t)]
    setup.SetupDiGetClassDevsW.argtypes=[ctypes.c_void_p,W.LPCWSTR,W.HWND,W.DWORD]
    setup.SetupDiGetClassDevsW.restype=W.HANDLE
    setup.SetupDiEnumDeviceInfo.argtypes=[W.HANDLE,W.DWORD,ctypes.POINTER(Info)]
    setup.SetupDiGetDeviceInstanceIdW.argtypes=[W.HANDLE,ctypes.POINTER(Info),W.LPWSTR,W.DWORD,ctypes.POINTER(W.DWORD)]
    setup.SetupDiDestroyDeviceInfoList.argtypes=[W.HANDLE]
    handle=setup.SetupDiGetClassDevsW(None,'USB',None,6)
    if handle==ctypes.c_void_p(-1).value:raise ctypes.WinError(ctypes.get_last_error())
    result=[]
    try:
        index=0
        while True:
            info=Info();info.cbSize=ctypes.sizeof(info)
            if not setup.SetupDiEnumDeviceInfo(handle,index,ctypes.byref(info)):
                if ctypes.get_last_error()==259:break
                raise ctypes.WinError(ctypes.get_last_error())
            index+=1;buf=ctypes.create_unicode_buffer(1024)
            if not setup.SetupDiGetDeviceInstanceIdW(handle,ctypes.byref(info),buf,len(buf),None):
                raise ctypes.WinError(ctypes.get_last_error())
            if buf.value.upper().startswith('USB\\VID_0528&PID_A4A8\\'):result.append(buf.value)
    finally:setup.SetupDiDestroyDeviceInfoList(handle)
    return sorted(result)


class LoaderControls:
    def __init__(self, process):
        self.proc=process;self.u=process.user
        self.u.GetDlgCtrlID.argtypes=[W.HWND];self.u.GetDlgCtrlID.restype=ctypes.c_int
        self.u.IsWindowEnabled.argtypes=[W.HWND]
        self.u.SendMessageTimeoutW.argtypes=[W.HWND,W.UINT,ctypes.c_size_t,ctypes.c_ssize_t,W.UINT,W.UINT,ctypes.POINTER(ctypes.c_size_t)]
        self.u.SendMessageTimeoutW.restype=ctypes.c_ssize_t
        self.u.PostMessageW.argtypes=[W.HWND,W.UINT,ctypes.c_size_t,ctypes.c_ssize_t]
        self.page=None;self.main=None;self.controls={}

    def send(self,hwnd,msg,wparam=0,lparam=0):
        result=ctypes.c_size_t()
        if not self.u.SendMessageTimeoutW(hwnd,msg,wparam,lparam,2,2000,ctypes.byref(result)):
            raise RuntimeError('Owned FSTools control did not respond')
        return result.value

    def text(self,h):
        buf=ctypes.create_unicode_buffer(8192)
        self.send(h,13,len(buf),ctypes.addressof(buf))
        return buf.value

    def attach(self,timeout=10):
        deadline=time.monotonic()+timeout
        while time.monotonic()<deadline:
            try:windows=self.proc.windows()
            except OSError as error:
                if error.winerror!=0:raise
                # EnumDesktopWindows can return FALSE/no error before the first window exists.
                windows=[]
            mains=[w for w in windows if w['title']=='Tools v2.1.9' and w['window_class']=='#32770']
            if len(mains)==1:
                self.main=mains[0]['hwnd'];rows=[]
                cb=ctypes.WINFUNCTYPE(W.BOOL,W.HWND,W.LPARAM)
                @cb
                def collect(h,l):
                    kind=ctypes.create_unicode_buffer(256);self.u.GetClassNameW(h,kind,len(kind))
                    rows.append((h,self.u.GetDlgCtrlID(h),kind.value));return True
                self.u.EnumChildWindows.argtypes=[W.HWND,cb,W.LPARAM]
                self.u.EnumChildWindows(self.main,collect,0)
                buttons=[r for r in rows if r[1]==1012 and r[2]=='Button']
                if len(buttons)==1:
                    self.u.GetParent.argtypes=[W.HWND];self.u.GetParent.restype=W.HWND
                    self.page=self.u.GetParent(buttons[0][0])
                    for h,id,kind in rows:
                        if self.u.GetParent(h)==self.page:self.controls[id]=(h,kind)
                    self.require(1012,'Button','Download');self.require(1082,'Button','Keep current IP')
                    self.require(1002,'msctls_progress32');self.require(1016,'ListBox')
                    return
            if self.proc.poll() is not None:raise RuntimeError('FSTools exited before initialization')
            time.sleep(.2)
        raise RuntimeError('FSTools control contract not found')

    def require(self,id,kind,text=None):
        h,k=self.controls[id]
        if k!=kind or (text is not None and self.text(h)!=text):raise RuntimeError('Unsupported FSTools control contract')
        return h

    def check(self,id,value):
        h=self.require(id,'Button');self.send(h,0xf1,int(value))
        if self.send(h,0xf0)!=int(value):raise RuntimeError('Cannot verify loader option '+str(id))

    def configure(self,project):
        # Explicitly clear all destructive and runtime-data replacement choices.
        for id in (1011,1013,1014,1015,1017,1075,1080,1081,1123,1077,1018):self.check(id,False)
        self.check(1082,True);self.check(1003,True);self.check(1004,False)
        self.check(1005,True);self.check(1006,False)
        self.send(self.page,0x111,1003,self.require(1003,'Button'))
        self.send(self.page,0x111,1005,self.require(1005,'Button'))
        h=self.require(1009,'Edit');buf=ctypes.create_unicode_buffer(str(project))
        self.send(h,12,0,ctypes.addressof(buf))
        if self.text(h)!=str(project):raise RuntimeError('Project path readback failed')
        if not self.u.IsWindowEnabled(self.require(1012,'Button')):raise RuntimeError('Download is disabled')
        return dict(keep_current_ip=True,transport='usb',force=False,delete_data=False,project_path=self.text(h))

    def start(self):
        # Asynchronous WM_COMMAND avoids blocking on a native modal dialog.
        if not self.u.PostMessageW(self.page,0x111,1012,self.require(1012,'Button')):
            raise ctypes.WinError(ctypes.get_last_error())

    def observe(self):
        dialogs=[w for w in self.proc.windows() if w['window_class']=='#32770' and w['hwnd']!=self.main and w['visible']]
        messages=[]
        h=self.require(1016,'ListBox');count=self.send(h,0x18b)
        if count>10000:raise RuntimeError('Invalid loader journal length')
        for index in range(count):
            length=self.send(h,0x18a,index)
            if length>8190:raise RuntimeError('Invalid loader journal entry')
            buf=ctypes.create_unicode_buffer(length+1);self.send(h,0x189,index,ctypes.addressof(buf));messages.append(buf.value)
        progress=self.send(self.require(1002,'msctls_progress32'),0x408)
        return dict(progress=progress,messages=messages,dialogs=dialogs)

    def close(self):
        if self.main:self.u.PostMessageW(self.main,0x10,0,0)
