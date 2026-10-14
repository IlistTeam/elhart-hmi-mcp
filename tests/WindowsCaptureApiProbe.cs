using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;

// Self-contained API diagnostic: counts frames of its own generated window
// and that window's monitor without reading pixels or writing screenshots.
// It never substitutes for a Computer Use screenshot or UI acceptance.
class CaptureApiProbe {
 [ComImport, Guid("3628e81b-3cac-4c60-b7f4-23ce0e0c3356"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
 interface IGraphicsCaptureItemInterop {
  [PreserveSig] int CreateForWindow(IntPtr window,ref Guid iid,out IntPtr item);
  [PreserveSig] int CreateForMonitor(IntPtr monitor,ref Guid iid,out IntPtr item);
 }
 [DllImport("combase.dll")] static extern int RoInitialize(uint mode);
 [DllImport("combase.dll",CharSet=CharSet.Unicode)] static extern int WindowsCreateString(string value,uint length,out IntPtr handle);
 [DllImport("combase.dll")] static extern int WindowsDeleteString(IntPtr handle);
 [DllImport("combase.dll")] static extern int RoGetActivationFactory(IntPtr name,ref Guid iid,out IntPtr factory);
 [DllImport("d3d11.dll")] static extern int D3D11CreateDevice(IntPtr adapter,uint type,IntPtr software,uint flags,IntPtr levels,uint levelCount,uint sdk,out IntPtr device,out uint level,out IntPtr context);
 [DllImport("d3d11.dll")] static extern int CreateDirect3D11DeviceFromDXGIDevice(IntPtr device,out IntPtr inspectable);
 [DllImport("user32.dll")] static extern IntPtr MonitorFromWindow(IntPtr window,uint flags);
 class ProbeForm:Form { protected override bool ShowWithoutActivation {get{return true;}} }
 static IDirect3DDevice Device(){
  IntPtr native=IntPtr.Zero,context=IntPtr.Zero,dxgi=IntPtr.Zero,inspectable=IntPtr.Zero;
  try {uint level;Marshal.ThrowExceptionForHR(D3D11CreateDevice(IntPtr.Zero,1,IntPtr.Zero,0x20,IntPtr.Zero,0,7,out native,out level,out context));
   var iid=new Guid("54ec77fa-1377-44e6-8c32-88fd5f44c84c");Marshal.ThrowExceptionForHR(Marshal.QueryInterface(native,ref iid,out dxgi));
   Marshal.ThrowExceptionForHR(CreateDirect3D11DeviceFromDXGIDevice(dxgi,out inspectable));
   return (IDirect3DDevice)Marshal.GetObjectForIUnknown(inspectable);
  }finally{foreach(var p in new[]{inspectable,dxgi,context,native})if(p!=IntPtr.Zero)Marshal.Release(p);}
 }
 static GraphicsCaptureItem Item(IntPtr ownWindow,bool monitor){
  IntPtr name=IntPtr.Zero,factory=IntPtr.Zero,item=IntPtr.Zero;
  try {string type="Windows.Graphics.Capture.GraphicsCaptureItem";Marshal.ThrowExceptionForHR(WindowsCreateString(type,(uint)type.Length,out name));
   var iid=typeof(IGraphicsCaptureItemInterop).GUID;Marshal.ThrowExceptionForHR(RoGetActivationFactory(name,ref iid,out factory));
   var interop=(IGraphicsCaptureItemInterop)Marshal.GetObjectForIUnknown(factory);
   var itemIid=new Guid("79c3f95b-31f7-4ec2-a464-632ef5d30760");
   Marshal.ThrowExceptionForHR(monitor?interop.CreateForMonitor(MonitorFromWindow(ownWindow,2),ref itemIid,out item):interop.CreateForWindow(ownWindow,ref itemIid,out item));
   return (GraphicsCaptureItem)Marshal.GetObjectForIUnknown(item);
  }finally{if(item!=IntPtr.Zero)Marshal.Release(item);if(factory!=IntPtr.Zero)Marshal.Release(factory);if(name!=IntPtr.Zero)WindowsDeleteString(name);}
 }
 [STAThread] static void Main(){
  var results=new List<object>();ProbeForm form=null;IDirect3DDevice device=null;
  try{RoInitialize(0);Application.EnableVisualStyles();
   form=new ProbeForm{Text="Capture API diagnostic (own window)",ClientSize=new Size(240,140),StartPosition=FormStartPosition.CenterScreen};
   form.Show();Application.DoEvents();device=Device();
   foreach(bool monitor in new[]{false,true})foreach(bool freeThreaded in new[]{false,true})foreach(bool animated in new[]{false,true}){
    var item=Item(form.Handle,monitor);
    int events=0,frames=0,ticks=0;string error=null;
    using(var pool=freeThreaded?Direct3D11CaptureFramePool.CreateFreeThreaded(device,DirectXPixelFormat.B8G8R8A8UIntNormalized,2,item.Size):Direct3D11CaptureFramePool.Create(device,DirectXPixelFormat.B8G8R8A8UIntNormalized,2,item.Size))
    using(var session=pool.CreateCaptureSession(item)){
     pool.FrameArrived+=(sender,args)=>Interlocked.Increment(ref events);
     session.StartCapture();var watch=Stopwatch.StartNew();
     try{while(watch.ElapsedMilliseconds<3000){
      if(animated)form.BackColor=(ticks++%2==0)?Color.DarkBlue:Color.DarkOrange;
      Application.DoEvents();using(var frame=pool.TryGetNextFrame()){if(frame!=null)frames++;}
      Thread.Sleep(30);
     }}catch(Exception e){error=e.GetType().Name+": "+e.Message+" HRESULT="+e.HResult.ToString("X8");}
    }
    results.Add(new {monitor=monitor,free_threaded=freeThreaded,animated=animated,events=events,frames=frames,error=error});
   }
   Console.WriteLine(new JavaScriptSerializer().Serialize(new{supported=GraphicsCaptureSession.IsSupported(),scope="window_and_monitor_frame_counts_only_no_pixels_read_or_saved",results=results}));
  }catch(Exception e){Console.WriteLine(new JavaScriptSerializer().Serialize(new{error=e.ToString()}));Environment.ExitCode=1;}
  finally{if(device!=null)device.Dispose();if(form!=null){form.Close();form.Dispose();}}
 }
}
