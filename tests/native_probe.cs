// 此文件只编译到隔离回归测试的临时宿主，不属于生产插件或 MCP 工具。
using System;
using System.IO;
using System.Collections;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FStudioMcp {
 internal static partial class VisibleHost {
  /// <summary>使用与编辑器菜单相同的双层撤销会话，验证一次撤销/重做覆盖完整批次。</summary>
  static object NativeHistoryForTest(string operation){
   var view=DesignerView();var session=Member(view,"UndoRedoSession");
   if(operation=="test_undo")CallNative(session,"Undo");
   if(operation=="test_redo")CallNative(session,"Redo");
   var undo=((IEnumerable)CallNative(session,"GetUndoTextList")).Cast<object>().Select(Convert.ToString).ToArray();
   var redo=((IEnumerable)CallNative(session,"GetRedoTextList")).Cast<object>().Select(Convert.ToString).ToArray();
   var component=Native("Flexem.Studio.GraphicsDesign.IComponent");
   var ids=((IEnumerable)Member(Member(view,"Model"),"Graphicses")).Cast<object>().Where(component.IsInstanceOfType)
    .Select(model=>Obj("type",model.GetType().FullName,"id",component.GetProperty("ComponentId").GetValue(model,null))).ToArray();
   return Obj("undo",undo,"redo",redo,"component_ids",ids);
  }
  /// <summary>把测试页真实 WPF 画布渲染为 PNG，供核对文字、边界和原生控件外观。</summary>
  static object RenderCanvasForTest(string file){
   var view=DesignerView();var page=Member(view,"Model");
   var canvas=Member(Member(Member(view,"DesignContext"),"RootItem"),"View") as FrameworkElement;
   if(canvas==null)throw new InvalidOperationException("Native canvas is unavailable");
   int width=Convert.ToInt32(Member(page,"Width")),height=Convert.ToInt32(Member(page,"Height"));
   var surface=Native("Flexem.Studio.GraphicsDesign.IGraphicsViewContent").GetProperty("DesignSurface").GetValue(view,null);
   // BackgroundLayer 在本版本设计器中不是公开模型属性，仅测试探针读取其实际视觉层成员。
   var flags=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance;
   var backgroundProperty=surface.GetType().GetProperty("BackgroundLayer",flags);
   var backgroundField=surface.GetType().GetField("BackgroundLayer",flags);
   var layer=backgroundProperty!=null?backgroundProperty.GetValue(surface,null):backgroundField==null?null:backgroundField.GetValue(surface);
   var background=Member(layer,"Content") as FrameworkElement;
   if(background==null)throw new InvalidOperationException("Native background layer is unavailable");
   // 原生 WindowCanvas 背景强制透明；按编辑器真实分层先渲染背景，再渲染控件层。
   foreach(var element in new[]{background,canvas}){
    element.ApplyTemplate();element.Measure(new Size(width,height));element.Arrange(new Rect(0,0,width,height));element.UpdateLayout();
   }
   var bitmap=new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32);bitmap.Render(background);bitmap.Render(canvas);
   var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));
   using(var stream=new FileStream(file,FileMode.CreateNew,FileAccess.Write))encoder.Save(stream);
   return Obj("file",file,"width",width,"height",height,"page_uuid",PageUuid(page).ToString(),"render_width",canvas.ActualWidth,"render_height",canvas.ActualHeight,"source","native_wpf_background_and_canvas");
  }
 }
}
