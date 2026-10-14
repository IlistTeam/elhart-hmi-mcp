using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace FStudioMcp {
 internal static partial class VisibleHost {
  [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr handle,int command);
  [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")] static extern bool IsIconic(IntPtr handle);
  [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr handle);
  /// <summary>显示已有工作台画布；后台启动尚未创建窗口句柄时，以不激活方式完成首次显示。</summary>
  static object ShowCanvasWithoutActivation() {
   RequireProject();
   var window=Application.Current.Windows.Cast<Window>().Single(w=>w.GetType().FullName=="ICSharpCode.SharpDevelop.Gui.WpfWorkbench");
   var before=GetForegroundWindow();var handle=new WindowInteropHelper(window).Handle;
   bool initialized=handle==IntPtr.Zero;
   if(initialized){
    // 使用已存在的 WPF 工作台对象完成首次布局，不构造第二个工作台，也不抢占焦点。
    bool activate=window.ShowActivated;
    try{window.ShowActivated=false;window.Show();handle=new WindowInteropHelper(window).Handle;}
    finally{window.ShowActivated=activate;}
   }
   if(handle==IntPtr.Zero)throw new InvalidOperationException("Native canvas window handle initialization failed");
   bool minimized=IsIconic(handle);
   // 已显示过的窗口用 SW_SHOWNOACTIVATE 恢复尺寸，保持当前应用焦点。
   if(minimized||!IsWindowVisible(handle))ShowWindow(handle,4);
   return Obj("visible",IsWindowVisible(handle),"minimized",IsIconic(handle),"was_minimized",minimized,"handle_initialized",initialized,"foreground_preserved",before==GetForegroundWindow(),"settings_operation_dispatched",false,"mouse_input_used",false);
  }
  sealed class DrawingDraft {public object Model, ViewModel, Tool, Page, CommittedView;}
  static readonly Dictionary<string,DrawingDraft> drawingDrafts=new Dictionary<string,DrawingDraft>();
  static object CallNative(object target,string name,params object[] args){
   var methods=target.GetType().GetMethods(BindingFlags.Public|BindingFlags.Instance).Where(m=>m.Name==name&&!m.ContainsGenericParameters&&m.GetParameters().Length==args.Length&&m.GetParameters().Select((p,i)=>args[i]==null||p.ParameterType.IsInstanceOfType(args[i])).All(x=>x)).ToArray();
   if(methods.Length!=1)throw new InvalidOperationException("Ambiguous native call: "+target.GetType().FullName+"."+name);
   return methods[0].Invoke(target,args);
  }
  static object DesignerView(){
   RequireProject();var view=Member(Resolve("Flexem.Studio.Workbench.IWorkbench"),"ActiveViewContent");
   if(view==null||view.GetType().FullName!="Flexem.Studio.HMI.Window.HMIWindowViewContent")throw new InvalidOperationException("An HMI page must be open in the designer");
   return view;
  }
  static object DesignerService(object context,string type){return CallNative(Member(context,"Services"),"GetService",Native(type));}
  static void NativeSet(object target,string property,object value){var p=target.GetType().GetProperty(property);if(p==null||p.GetSetMethod()==null)throw new ArgumentException("Native property is not publicly writable: "+property);p.SetValue(target,ConvertArg(value,p.PropertyType),null);}
  static MethodInfo ToolHook(object tool,string name,int count){return tool.GetType().GetMethods(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic).SingleOrDefault(m=>m.Name==name&&m.GetParameters().Length==count);}
  /// <summary>补齐后台草稿的配方子模型；原生工具默认值省略这些数据，选定配方后会在保存时空引用。</summary>
  /// <param name="model">已关联当前项目、尚未加入画布的原生模型。</param>
  /// <exception cref="InvalidOperationException">项目未配置配方所需的显示语言。</exception>
  static void InitializeDrawingModel(object model){
   if(model.GetType().FullName!="Flexem.Studio.Components.Parts.RecipePart.RecipeDisplayInfo")return;
   var setting=Member(model,"RecipeSetting");
   var languages=Member(PageServices(),"LanguageConfigurationService");
   var first=((IEnumerable)Member(languages,"Configurations")).Cast<object>().FirstOrDefault();
   if(first==null)throw new InvalidOperationException("Recipe drawing requires a configured project language");
   // 配置项显式实现接口的 Id，不能按具体类型的公共属性读取。
   var languageContract=Native("Flexem.Studio.Hmi.Configurations.Language.IReadonlyLanguageConfigItem");
   int languageId=Convert.ToInt32(languageContract.GetProperty("Id").GetValue(first,null));
   int format=Convert.ToInt32(Member(setting,"DisplayFormat"));
   // 与原生属性编辑器采用相同的构造函数，仅初始化缺失字段，不覆盖已有配方配置。
   const string prefix="Flexem.Studio.Hmi.Models.Components.Parts.RecipePart.DataConfig.";
   if(Member(setting,"RecipeCommonSetting")==null)NativeSet(setting,"RecipeCommonSetting",Activator.CreateInstance(Native(prefix+"RecipeCommonSettingData"),new object[]{languageId,format}));
   if(Member(setting,"RecipeItemSettingList")==null)NativeSet(setting,"RecipeItemSettingList",Activator.CreateInstance(Native(prefix+"RecipeItemSettingDataList"),new object[]{languages,format}));
  }
  /// <summary>用原生工具创建完整草稿；静态文字批量通路直接初始化语言数据，避免无窗口的属性面板留下全局状态。</summary>
  static DrawingDraft CreateDrawingDraft(Dictionary<string,object>a,object page,object context,bool batch=false){
    var type=Native(S(a,"tool"));
    if(type.Namespace!="Flexem.Studio.GraphicsDesigner.Tools"||type.IsAbstract||type.ContainsGenericParameters||!type.Name.EndsWith("Tool"))throw new ArgumentException("Expected a concrete native drawing tool");
    // 此版本窗口选择器的 Child_Fetch 在载入 PagingAddress 前调用 OnFetching/Init，触发空引用。
    // 在创建草稿前拒绝，避免把无法再次打开的控件写入用户工程；未来版本不套用此限制。
    if(type.Name=="WindowSelectorTool"&&type.Assembly.GetName().Version.ToString()=="3.0.15685.0")
     throw new NotSupportedException("NATIVE_UNSUPPORTED: WindowSelectorTool in FStudio 3.0.15685.0 accesses PagingAddress before deserialization and prevents project reopening; draft creation was refused to preserve the project");
    var tool=Activator.CreateInstance(type);
    // 工具钩子需要当前设计上下文，但 Activate 会打开属性面板，因此只注入已有上下文。
    for(var baseType=type;baseType!=null;baseType=baseType.BaseType)foreach(var field in baseType.GetFields(BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public|BindingFlags.DeclaredOnly)){
     if(field.FieldType.FullName=="ICSharpCode.WpfDesign.DesignContext")field.SetValue(tool,context);
     if(field.FieldType.FullName=="ICSharpCode.WpfDesign.IDesignPanel")field.SetValue(tool,Member(Member(context,"Services"),"DesignPanel"));
    }
    if(a.ContainsKey("tool_properties"))foreach(var p in (Dictionary<string,object>)a["tool_properties"])NativeSet(tool,p.Key,p.Value);
    var create=ToolHook(tool,"CreateModel",0);if(create==null)throw new ArgumentException("Native tool has no model creation hook");
    var model=create.Invoke(tool,new object[0]);
    object vm;
    try{
     var project=Member(Resolve("Flexem.Studio.Hmi.Project.IHmiProjectService"),"CurrentProject");
     if(!(bool)Member(model,"IsReferenced"))CallNative(model,"EnableReference",project);
     InitializeDrawingModel(model);
     var createVm=ToolHook(tool,"CreateViewModel",1);
     // 基础几何模型自行初始化属性，无需调用属性面板工厂。
     bool basicShape=new[]{"LineTool","PolyLineTool","PolygonTool","RectangleTool","EllipseTool","ArcTool","SectorTool","SectorRingTool"}.Contains(tool.GetType().Name);
     // 批量文字在 ConfigureBatchText 中使用原生语言/字体工厂补全；省去面板视图模型的文本库扫描与事件订阅。
     vm=basicShape||(batch&&type.Name=="StaticTextTool")?null:createVm==null?Native("Flexem.Studio.GraphicsDesigner.PropertyViewModelFactory").GetMethod("GetViewModel",BindingFlags.Public|BindingFlags.Static).Invoke(null,new object[]{model}):createVm.Invoke(tool,new object[]{model});
     // SettingFirstCreateComponentTool 的默认 CreateViewModel 返回 null；原生 ShowSetting 此时回退到工厂。
     // 批量数值输入等工具的 BeforeShowSetting 依赖真正的属性视图模型；旧 prepare 不调用此钩子，保留其原有返回语义。
     if(batch&&vm==null&&!basicShape&&type.Name!="StaticTextTool")vm=Native("Flexem.Studio.GraphicsDesigner.PropertyViewModelFactory").GetMethod("GetViewModel",BindingFlags.Public|BindingFlags.Static).Invoke(null,new object[]{model});
    }catch(Exception error){
     // 草稿尚未登记，客户端无法显式丢弃；此处释放失败初始化建立的项目引用。
     try{if((bool)Member(model,"IsReferenced"))CallNative(model,"DisableReference");}
     catch(Exception cleanup){throw new AggregateException("Native draft initialization and reference cleanup failed",error,cleanup);}
     throw;
    }
   return new DrawingDraft{Model=model,ViewModel=vm,Tool=tool,Page=page};
  }
  static object ConfigureLocalAddress(Dictionary<string,object>a,object view) {
   var address=Ref(a);string type=address.GetType().FullName;
   bool legacy=type=="Flexem.Studio.Models.HMI.AddressInfo";
   if(!legacy&&type!="Flexem.Studio.Hmi.Models.Address.AddressData")throw new ArgumentException("Expected an AddressInfo or AddressData handle");
   var scope=OwningScope(S(a,"ref"));var draft=scope as DrawingDraft;
   if(scope==null||(draft==null&&!Object.ReferenceEquals(scope,view))||(draft!=null&&!Object.ReferenceEquals(draft.Page,Member(view,"Model"))))throw new ArgumentException("Address must belong to the active page or its draft");
   object supplied=a["address"];
   if(supplied==null||supplied is bool||supplied is string)throw new ArgumentException("address must be an unsigned integer");
   double number=Convert.ToDouble(supplied);
   if(Double.IsNaN(number)||Double.IsInfinity(number)||number<0||number>UInt32.MaxValue||number!=Math.Truncate(number))throw new ArgumentException("address must be an unsigned integer");
   var raw=legacy?address:Member(address,"Raw");
   if(raw==null)throw new ArgumentException("Address Raw is not initialized");
   foreach(var target in new[]{address,raw}.Distinct())foreach(var flag in new[]{"IsLabelAddress","UseIndex","UseStationAddress","UseStationReference","UseTagByteReference","UseOffset","UseWordRegisterAsBit"}){
    var property=target.GetType().GetProperty(flag);
    if(property!=null&&property.PropertyType==typeof(bool)&&(bool)property.GetValue(target,null))throw new ArgumentException("Configure local address requires a direct address; advanced option is active: "+flag);
   }
   string kind=Convert.ToString(Member(address,"AddressType"));
   if(kind!="Bit"&&kind!="Word")throw new ArgumentException("Unsupported native address type: "+kind);
   var devices=Member(PageServices(),"DeviceService");
   var source=kind=="Bit"?PageStatic("Flexem.Studio.Hmi.Models.Address.AddressData","CreateBit",devices):PageStatic("Flexem.Studio.Hmi.Models.Address.AddressData","CreateWord",devices,Member(raw,"DataType"),true);
   var sourceRaw=Member(source,"Raw");var register=Member(sourceRaw,"RegisterId");
   var described=(Dictionary<string,object>)DescribeOwned(raw,scope);string rawRef=S(described,"$ref");
   var edits=new[]{Obj("ref",rawRef,"member","DeviceId","value",Member(sourceRaw,"DeviceId")),Obj("ref",rawRef,"member","RegisterId","value",legacy?Member(register,"Id"):register),Obj("ref",rawRef,"member","MainAddress","value",Convert.ToUInt32(number))};
   var changed=(Dictionary<string,object>)SetModelProperties(Obj("changes",edits));
   // Legacy AddressInfo keeps persisted display text and CSLA validity derived
   // from Raw. A prior AddressType/IsCombinedBits change can leave those values
   // stale even though DeviceId/RegisterId/MainAddress are already correct.
   if(legacy){CallNative(address,"UpdateAbbreviation");CallNative(address,"UpdateDeviceAbbreviation");CallNative(address,"CheckRules");}
   var actualRegister=Member(raw,"RegisterId");
   return Obj("model",DescribeOwned(address,scope),"address_type",kind,"device_id",Convert.ToString(Member(raw,"DeviceId")),"register_id",legacy?actualRegister:Member(actualRegister,"Id"),"address",Member(raw,"MainAddress"),"data_type",Convert.ToString(Member(raw,"DataType")),"native_undo_transaction",changed["native_undo_transaction"],"persistence","save_project_to_persist","hardware_io",false);
  }
  static object ConfigureBitSwitch(Dictionary<string,object>a,object view) {
   var model=Ref(a);
   if(model.GetType().FullName!="Flexem.Studio.Components.Parts.SwitchPart.SwitchInfo")throw new ArgumentException("Expected a native SwitchInfo model");
   var scope=OwningScope(S(a,"ref"));var draft=scope as DrawingDraft;
   if(scope==null||(draft==null&&!Object.ReferenceEquals(scope,view))||(draft!=null&&!Object.ReferenceEquals(draft.Page,Member(view,"Model"))))throw new ArgumentException("Switch must belong to the current page or one of its drafts");
   string mode=S(a,"action");if(!new[]{"On","Off","Switch"}.Contains(mode))throw new ArgumentException("action must be On, Off or Switch");
   object rawValue=a["address"];if(rawValue==null||rawValue is bool||rawValue is string)throw new ArgumentException("address must be an unsigned integer");
   double number=Convert.ToDouble(rawValue);if(Double.IsNaN(number)||Double.IsInfinity(number)||number<0||number>UInt32.MaxValue||Math.Truncate(number)!=number)throw new ArgumentException("address must be an unsigned integer");
   var settings=Member(model,"SwitchSettings");var actions=Member(settings,"ActionList");
   if(actions==null)throw new InvalidOperationException("Switch action list is not initialized");
   if(((IEnumerable)actions).Cast<object>().Any())throw new InvalidOperationException("This initializer requires an empty action list; existing switch actions were preserved");
   var devices=Member(PageServices(),"DeviceService");
   var address=PageStatic("Flexem.Studio.Hmi.Models.Address.AddressData","CreateBit",devices);
   var raw=Member(address,"Raw");NativeSet(raw,"MainAddress",Convert.ToUInt32(number));
   var action=Activator.CreateInstance(Native("Flexem.Studio.Hmi.Models.Components.Parts.SwitchNew.BitSwitchActions.BitSwitchActionData"));
   NativeSet(action,"ExecutionCondition","Action");NativeSet(action,"Action","KeyDown");NativeSet(action,"Function","Bit");NativeSet(action,"FuncType",mode);NativeSet(action,"PulseWidth",2);NativeSet(action,"Address",address);
   var owner=OwningView(S(a,"ref"));object group=owner==null?null:BeginModelChange(owner,"MCP: bit switch action");
   try{NativeSet(settings,"UseSwitch",true);CallNative(actions,"Add",action);if(group!=null)CallNative(group,"Commit");}
   catch{if(group!=null)CallNative(group,"Abort");throw;}
   return Obj("model",DescribeOwned(model,scope),"action",DescribeOwned(action,scope),"function",mode,"trigger","KeyDown","local_device_id",Member(raw,"DeviceId"),"register",DescribeOwned(Member(raw,"RegisterId"),scope),"address",Member(raw,"MainAddress"),"action_count",((IEnumerable)actions).Cast<object>().Count(),"native_undo_transaction",group!=null,"persistence","save_project_to_persist","execution_mode",ExecutionMode);
  }
  static object ConfigureBitIndicator(Dictionary<string,object>a,object view){
   var model=Ref(a);if(model.GetType().FullName!="Flexem.Studio.Components.Parts.SwitchPart.SwitchInfo")throw new ArgumentException("Expected SwitchInfo");
   var scope=OwningScope(S(a,"ref"));var draft=scope as DrawingDraft;
   if(scope==null||(draft==null&&!Object.ReferenceEquals(scope,view))||(draft!=null&&!Object.ReferenceEquals(draft.Page,Member(view,"Model"))))throw new ArgumentException("Indicator must belong to the active page or its draft");
   var indicator=Member(model,"Indicator");if(Member(indicator,"Usage")!=null)throw new ArgumentException("Indicator is already configured; existing conditions were preserved");
   double index=Convert.ToDouble(a["address"]);if(a["address"] is bool||index<0||index>UInt32.MaxValue||Math.Truncate(index)!=index)throw new ArgumentException("Invalid local bit address");
   string off=S(a,"off_text"),on=S(a,"on_text");if(String.IsNullOrWhiteSpace(off)||String.IsNullOrWhiteSpace(on))throw new ArgumentException("Both state labels are required");
   var statuses=((IEnumerable)Member(Member(model,"Tag"),"Statuses")).Cast<object>().ToArray();if(statuses.Length!=2)throw new NotSupportedException("Expected two native tag states");
   var devices=Member(PageServices(),"DeviceService");var usageType=Native("Flexem.Studio.Hmi.Models.Components.Parts.Indicator.IndicatorUsage.IndicatorUsageData");
   var constructor=usageType.GetConstructors().Single(c=>c.GetParameters().Length==1&&c.GetParameters()[0].ParameterType.FullName=="Flexem.Studio.Hmi.Device.IDeviceService");
   var usage=constructor.Invoke(new[]{devices});NativeSet(usage,"DisplayType","RegisterMode");
   var control=Member(usage,"RegisterControl");NativeSet(control,"ControlType","BitRegister");
   var bit=Member(control,"Bit");var address=PageStatic("Flexem.Studio.Hmi.Models.Address.AddressData","CreateBit",devices);
   NativeSet(Member(address,"Raw"),"MainAddress",Convert.ToUInt32(index));NativeSet(bit,"Address",address);NativeSet(bit,"IsSameAsSwitchAddress",false);
   var bits=((IEnumerable)Member(bit,"Statuses")).Cast<object>().ToArray();if(bits.Length!=2)throw new NotSupportedException("Expected two native bit states");
   NativeSet(bits[0],"IsNegativeLogic",false);NativeSet(bits[1],"IsNegativeLogic",true);
   var owner=OwningView(S(a,"ref"));object group=owner==null?null:BeginModelChange(owner,"MCP: bit indicator states");
   try{
    NativeSet(indicator,"Usage",usage);NativeSet(indicator,"UseIndicator",true);NativeSet(Member(model,"Tag"),"UsingType","Tag");
    for(int i=0;i<2;i++)foreach(var language in (IEnumerable)Member(statuses[i],"Items"))NativeSet(language,"Text",i==0?off:on);
    if(group!=null)CallNative(group,"Commit");
   }catch{if(group!=null)CallNative(group,"Abort");throw;}
   return Obj("model",DescribeOwned(model,scope),"address",index,"state_count",bits.Length,"negative_logic",bits.Select(x=>Member(x,"IsNegativeLogic")).ToArray(),"labels",new[]{off,on},"register",Describe(Member(Member(address,"Raw"),"RegisterId")),"native_undo_transaction",group!=null,"runtime","not_executed");
  }
  static object ImportNativePicture(Dictionary<string,object>a,object view){
   var model=Ref(a);if(model.GetType().FullName!="Flexem.Studio.Components.Graphicses.StaticPictureInfo")throw new ArgumentException("Expected StaticPictureInfo");
   var scope=OwningScope(S(a,"ref"));var draft=scope as DrawingDraft;
   if(scope==null||(draft==null&&!Object.ReferenceEquals(scope,view))||(draft!=null&&!Object.ReferenceEquals(draft.Page,Member(view,"Model"))))throw new ArgumentException("Picture must belong to the active page or its draft");
   string file=Path.GetFullPath(S(a,"file"));Within(file);
   byte[] data=Convert.FromBase64String(S(a,"data"));if(data.Length==0||data.Length>8*1024*1024)throw new ArgumentException("Invalid picture byte length");
   if(!File.Exists(file)||!File.ReadAllBytes(file).SequenceEqual(data))throw new ArgumentException("Imported file bytes do not match the request");
   var normal=Member(model,"NormalInfo");if(!String.IsNullOrEmpty(Convert.ToString(Member(normal,"ImageImportPath")))||Convert.ToString(Member(normal,"PictureOrigin"))!="None")throw new ArgumentException("Picture already has a source; preserve it");
   var load=normal.GetType().GetMethod("LoadFromFile",new[]{typeof(string),typeof(byte[])});
   if(load==null)throw new NotSupportedException("Native picture LoadFromFile contract is missing");
   var owner=OwningView(S(a,"ref"));object group=owner==null?null:BeginModelChange(owner,"MCP: native picture import");
   try{load.Invoke(normal,new object[]{file,data});if(Convert.ToString(Member(normal,"PictureOrigin"))!="File")throw new InvalidOperationException("Native import did not set PictureOrigin=File");if(group!=null)CallNative(group,"Commit");}
   catch{if(group!=null)CallNative(group,"Abort");throw;}
   return Obj("picture_origin",Convert.ToString(Member(normal,"PictureOrigin")),"file",Member(normal,"ImageImportPath"),"bytes",data.Length,"native_undo_transaction",group!=null,"xml_repair_required",false,"runtime","not_executed");
  }
  static object ConfigureNavigation(Dictionary<string,object>a,object view){
   var model=Ref(a);if(model.GetType().FullName!="Flexem.Studio.Components.Parts.SwitchPart.SwitchInfo")throw new ArgumentException("Expected SwitchInfo");
   var scope=OwningScope(S(a,"ref"));var draft=scope as DrawingDraft;
   if(scope==null||(draft==null&&!Object.ReferenceEquals(scope,view))||(draft!=null&&!Object.ReferenceEquals(draft.Page,Member(view,"Model"))))throw new ArgumentException("Button must belong to the active page or its draft");
   Guid uuid;if(!Guid.TryParse(S(a,"target_uuid"),out uuid))throw new ArgumentException("Invalid target page UUID");
   var target=CallNative(Member(PageWindowService(),"WindowManager"),"GetWindow",uuid);if(target==null)throw new ArgumentException("Target page does not exist");RequireBasicPage(Member(target,"Settings"));
   var settings=Member(model,"SwitchSettings");var actions=Member(settings,"ActionList");if(((IEnumerable)actions).Cast<object>().Any())throw new ArgumentException("Existing actions were preserved; configure an empty button");
   string text=S(a,"text");if(String.IsNullOrWhiteSpace(text))throw new ArgumentException("Navigation label is required");
   var dto=Activator.CreateInstance(Native("Flexem.Studio.Components.Parts.SwitchNew.WindowOperSwitchActionDto"));
   NativeSet(dto,"ExecutionCondition","Action");NativeSet(dto,"Action","KeyDown");NativeSet(dto,"Function","Window");NativeSet(dto,"FuncType","Switch");NativeSet(dto,"Window",uuid);NativeSet(dto,"IsAutoPopupPasswordWindow",false);
   var action=Activator.CreateInstance(Native("Flexem.Studio.Hmi.Models.Components.Parts.SwitchNew.WindowOperSwitchActions.WindowOperSwitchActionData"),new[]{dto});
   var tag=Member(model,"Tag");var statuses=((IEnumerable)Member(tag,"Statuses")).Cast<object>().ToArray();
   var owner=OwningView(S(a,"ref"));object group=owner==null?null:BeginModelChange(owner,"MCP: page navigation");
   try{NativeSet(settings,"UseSwitch",true);CallNative(actions,"Add",action);NativeSet(tag,"UsingType","Tag");foreach(var state in statuses)foreach(var language in (IEnumerable)Member(state,"Items"))NativeSet(language,"Text",text);if(group!=null)CallNative(group,"Commit");}
   catch{if(group!=null)CallNative(group,"Abort");throw;}
   return Obj("target_uuid",uuid.ToString(),"action",DescribeOwned(action,scope),"function","Window","mode","Switch","trigger","KeyDown","text",text,"native_undo_transaction",group!=null,"runtime","not_executed");
  }
  static object CanvasHistory(Dictionary<string,object>a,object view){
   string op=S(a,"op");var session=Member(view,"UndoRedoSession");
   var undo=((IEnumerable)CallNative(session,"GetUndoTextList")).Cast<object>().Select(Convert.ToString).ToArray();
   var redo=((IEnumerable)CallNative(session,"GetRedoTextList")).Cast<object>().Select(Convert.ToString).ToArray();
   if(op!="drawing_history"){
    if(drawingDrafts.Count!=0)throw new InvalidOperationException("Commit or discard drawing drafts before undo/redo");
    if(op=="drawing_undo"&&undo.Length==0||op=="drawing_redo"&&redo.Length==0)throw new InvalidOperationException("No native history entry is available");
    CallNative(session,op=="drawing_undo"?"Undo":"Redo");
    undo=((IEnumerable)CallNative(session,"GetUndoTextList")).Cast<object>().Select(Convert.ToString).ToArray();
    redo=((IEnumerable)CallNative(session,"GetRedoTextList")).Cast<object>().Select(Convert.ToString).ToArray();
   }
   return Obj("undo",undo,"redo",redo,"page_uuid",PageUuid(Member(view,"Model")).ToString(),"dirty",Member(view,"IsDirty"),"execution_mode",ExecutionMode);
  }
  static object ConfigureTrendSampling(Dictionary<string,object>a,object view){
   var model=Ref(a);if(model.GetType().GetProperty("SamplingChannel")==null)throw new ArgumentException("Expected a sampling-backed curve model");
   var scope=OwningScope(S(a,"ref"));var draft=scope as DrawingDraft;
   if(scope==null||(draft==null&&!Object.ReferenceEquals(scope,view))||(draft!=null&&!Object.ReferenceEquals(draft.Page,Member(view,"Model"))))throw new ArgumentException("Trend must belong to the active page or draft");
   var trendProperty=model.GetType().GetProperty("TrendChartNormalInfo");
   var diskProperty=model.GetType().GetProperty("NormalInfo");
   var normalObject=trendProperty==null?(diskProperty==null?null:diskProperty.GetValue(model,null)):trendProperty.GetValue(model,null);
   if(normalObject==null||normalObject.GetType().GetProperty("PauseAddress")==null)throw new ArgumentException("Expected a trend or disk curve model");
   var pause=Member(normalObject,"PauseAddress");
   if(pause==null||(Guid)Member(pause,"DeviceId")==Guid.Empty||Convert.ToUInt32(Member(pause,"RegisterId"))==0)throw new ArgumentException("Configure the curve PauseAddress before binding sampling; use fstudio_address_configure_local");
   Guid id;if(!Guid.TryParse(S(a,"sample_id"),out id))throw new ArgumentException("Invalid sample_id");
   var collection=Member(Member(Member(CurrentProject(),"HMIConfigInfo"),"DataSampleConfigInfo"),"DataSampleInfos");
   var sample=((IEnumerable)collection).Cast<object>().SingleOrDefault(x=>(Guid)Member(x,"Id")==id);if(sample==null)throw new ArgumentException("Sampling group does not exist");
   if(!(bool)Member(Member(sample,"PropertyInfo"),"UseSerialAddress"))throw new ArgumentException("This binding requires serial sampling channels");
   var channels=((IEnumerable)Member(Member(sample,"ChannelConfigInfo"),"ChannelInfos")).Cast<object>().ToArray();if(channels.Length==0)throw new ArgumentException("Sampling group has no channels");
   var sampling=Member(model,"SamplingChannel");var old=Member(sampling,"Configures");if(old!=null&&((IEnumerable)old).Cast<object>().Any())throw new ArgumentException("Trend already has channel configuration; refusing to overwrite");
   var configs=Activator.CreateInstance(Native("Flexem.Studio.Hmi.Models.Components.Parts.SamplingChannel.SamplingChannelConfigureDataList"));
   foreach(var channel in channels){
    var setting=Activator.CreateInstance(Native("Flexem.Studio.Components.Parts.SamplingChannel.SamplingChannelSettingDto"));
    foreach(string bound in new[]{"MinValue","MaxValue"}){var variable=Activator.CreateInstance(Native("Flexem.Studio.Components.Common.VariableDto"));NativeSet(variable,"VariableType",0);NativeSet(variable,"Constant",bound=="MinValue"?0:100);NativeSet(setting,bound,variable);}
    var dto=Activator.CreateInstance(Native("Flexem.Studio.Components.Parts.SamplingChannel.SamplingChannelConfigureDto"));NativeSet(dto,"Id",Member(channel,"ChannelId"));NativeSet(dto,"IsUse",true);NativeSet(dto,"ChannelSetting",setting);
    var data=Activator.CreateInstance(Native("Flexem.Studio.Hmi.Models.Components.Parts.SamplingChannel.SamplingChannelConfigureData"),new[]{dto});CallNative(configs,"Add",data);
   }
   var handle=(Dictionary<string,object>)DescribeOwned(sampling,scope);var configHandle=DescribeOwned(configs,scope);var normal=(Dictionary<string,object>)DescribeOwned(normalObject,scope);
   var changes=new List<Dictionary<string,object>>{Obj("ref",S(handle,"$ref"),"member","SampleId","value",id.ToString()),Obj("ref",S(handle,"$ref"),"member","Configures","value",configHandle)};
   if(normalObject.GetType().GetProperty("DisplayType")!=null)changes.Add(Obj("ref",S(normal,"$ref"),"member","DisplayType","value","Immediate"));
   var scaleProperty=model.GetType().GetProperty("TrendChartScaleInfo");
   var scaleObject=scaleProperty==null?null:scaleProperty.GetValue(model,null);
   if(scaleObject!=null&&scaleObject.GetType().GetProperty("ChannelValue")!=null){var scale=(Dictionary<string,object>)DescribeOwned(scaleObject,scope);changes.Add(Obj("ref",S(scale,"$ref"),"member","ChannelValue","value",Member(channels[0],"ChannelId")));}
   var result=SetModelProperties(Obj("changes",changes.ToArray()));return Obj("curve_type",model.GetType().FullName,"sample_id",id.ToString(),"channels",channels.Length,"properties",result,"runtime","not_executed");
  }
  static object RemoveCanvasGraphic(Dictionary<string,object>a,object view,object page){
   var item=Ref(a);
   var collection=Member(page,"Graphicses");
   var before=((IEnumerable)collection).Cast<object>().ToArray();
   int index=Array.FindIndex(before,x=>Object.ReferenceEquals(x,item));
   if(index<0)throw new ArgumentException("Graphic handle is not an element of the active page canvas");
   var group=BeginModelChange(view,"MCP: remove canvas graphic");
   try{
    CallNative(collection,"Remove",item);
    var after=((IEnumerable)collection).Cast<object>().ToArray();
    var expected=before.Where(x=>!Object.ReferenceEquals(x,item)).ToArray();
    if(after.Length!=expected.Length||after.Where((x,i)=>!Object.ReferenceEquals(x,expected[i])).Any())throw new InvalidOperationException("Native removal changed unexpected collection entries");
    CallNative(group,"Commit");
   }catch(Exception error){try{CallNative(group,"Abort");}catch(Exception rollback){throw new AggregateException("Graphic removal and rollback failed",error,rollback);}throw;}
   return Obj("removed",true,"index",index,"remaining_count",((IEnumerable)collection).Cast<object>().Count(),
    "graphic_type",item.GetType().FullName,"comment",Member(item,"Comment"),"page_uuid",PageUuid(page).ToString(),
    "dirty",Member(view,"IsDirty"),"native_undo_transaction",true,"persistence","save_project_to_persist","runtime","not_executed");
  }
  /// <summary>在当前原生页面管理绘图草稿和画布修改；插入使用设计器事务，失败时回滚。</summary>
  /// <param name="a">已校验的操作名称、模型句柄、工具参数或插入边界。</param>
  /// <returns>草稿句柄或实际操作结果；修改后的画布需另行保存项目。</returns>
  /// <exception cref="InvalidOperationException">页面、草稿或原生模板状态不满足操作条件。</exception>
  static object DrawingOperation(Dictionary<string,object>a){
   var op=S(a,"op");var view=DesignerView();var page=Member(view,"Model");var context=Member(view,"DesignContext");
   if(op=="drawing_snapshot")return DrawingSnapshot(a,view,page);
   if(op=="drawing_create_many")return DrawingCreateMany(a,view,page,context);
   if(op=="drawing_show_canvas")return ShowCanvasWithoutActivation();
   if(op=="drawing_state")return Obj("project",ProjectFile(),"page",DescribeOwned(page,view),"graphics",DescribeOwned(Member(page,"Graphicses"),view),"view",Describe(view),"dirty",Member(view,"IsDirty"),"draft_count",drawingDrafts.Count,"drafts",drawingDrafts.Select(d=>Obj("draft_id",d.Key,"model",Describe(d.Value.Model))).ToArray(),"execution_mode","background_with_live_canvas");
   if(op=="drawing_configure_bit_switch")return ConfigureBitSwitch(a,view);
   if(op=="drawing_configure_bit_indicator")return ConfigureBitIndicator(a,view);
   if(op=="drawing_configure_navigation")return ConfigureNavigation(a,view);
   if(op=="drawing_import_image")return ImportNativePicture(a,view);
   if(op=="drawing_history"||op=="drawing_undo"||op=="drawing_redo")return CanvasHistory(a,view);
   if(op=="drawing_configure_local_address")return ConfigureLocalAddress(a,view);
   if(op=="drawing_configure_trend")return ConfigureTrendSampling(a,view);
   if(op=="drawing_remove_graphic")return RemoveCanvasGraphic(a,view,page);
   if(op=="drawing_prepare"){
    var createdDraft=CreateDrawingDraft(a,page,context);
    var id=Guid.NewGuid().ToString("N");drawingDrafts[id]=createdDraft;
    return Obj("draft_id",id,"model",DescribeOwned(createdDraft.Model,createdDraft),"view_model",DescribeOwned(createdDraft.ViewModel,createdDraft),"tool",Describe(createdDraft.Tool),"canvas_changed",false);
   }
   DrawingDraft draft;string draftId=S(a,"draft_id");if(!drawingDrafts.TryGetValue(draftId,out draft))throw new ArgumentException("Unknown or expired drawing draft");
   if(!Object.ReferenceEquals(draft.Page,page))throw new InvalidOperationException("Draft belongs to a different page; reopen its original page");
   // 插入失败的原生回滚可能已释放引用；仅释放仍有效的引用，确保失败草稿也能清理。
   if(op=="drawing_discard"){if((bool)Member(draft.Model,"IsReferenced"))CallNative(draft.Model,"DisableReference");drawingDrafts.Remove(draftId);ClearScopeHandles(draft);return Obj("discarded",true,"canvas_changed",false);}
   if(op!="drawing_commit")throw new ArgumentException("Unknown drawing operation");
   // 草稿可能早于其他创建请求，同类型编号在正式登记前重新核对，避免旧草稿与新批次编号相撞。
   AllocateBatchComponentIds(new List<BatchWidget>{new BatchWidget{Draft=draft}},context);
   var position=Member(draft.Model,"Position");
   foreach(var p in new[]{"X","Y","Width","Height"}){double value=Convert.ToDouble(a[p.ToLowerInvariant()]);if(Double.IsNaN(value)||Double.IsInfinity(value)||value<0||(p=="Width"||p=="Height")&&value==0)throw new ArgumentException("Invalid widget bounds");NativeSet(position,p,value);}
   // Modern components derive their display name from type/id. Comment is their
   // public, persisted user label; never write a protected name field directly.
   NativeSet(draft.Model,"Comment",S(a,"name"));
   var setName=draft.Model.GetType().GetMethod("SetDisplayName",new Type[]{typeof(string)});
   if(setName!=null)setName.Invoke(draft.Model,new object[]{S(a,"name")});
   var extensionManager=Member(Member(context,"Services"),"ExtensionManager");
   var viewType=(Type)Member(draft.Model,"ViewType");var component=CallNative(extensionManager,"CreateInstanceWithCustomInstanceFactory",viewType,null);
   var item=CallNative(DesignerService(context,"ICSharpCode.WpfDesign.IComponentService"),"RegisterComponentForDesigner",component);
   var group=CallNative(item,"OpenGroup","MCP: "+S(a,"name"));object placement=null;Dictionary<string,object> committedResult=null;
   try{
    CallNative(extensionManager,"ApplyDefaultInitializers",item);
    // Sliding 的绑定器立即访问模板子控件；原生鼠标绘制会先应用模板，后台创建也必须完成此步骤。
    if(viewType.FullName=="Flexem.Studio.GraphicsDesigner.Presentation.Shapes.Sliding"){
     var element=(FrameworkElement)component;
     // 尚未插入视觉树的控件无法继承页面资源，先从当前原生画布解析同类型样式。
     var canvas=Member(Member(context,"RootItem"),"View") as FrameworkElement;
     if(element.Style==null&&canvas!=null)element.Style=canvas.TryFindResource(viewType) as Style;
     element.ApplyTemplate();
     if(new[]{"BackgroundGalleryViewer","SliderGalleryViewer","Scale"}.Any(name=>Member(component,name)==null))
      throw new InvalidOperationException("Native Sliding view template did not initialize its required parts");
    }
    var registry=DesignerService(context,"Flexem.Studio.GraphicsDesign.IRootGraphicRegistry");
    CallNative(registry,"RegisterRootGraphic",draft.Model,item);
    var root=Member(context,"RootItem");var items=Array.CreateInstance(Native("ICSharpCode.WpfDesign.DesignItem"),1);items.SetValue(item,0);
    var rect=new Rect(Convert.ToDouble(a["x"]),Convert.ToDouble(a["y"]),Convert.ToDouble(a["width"]),Convert.ToDouble(a["height"]));
    var addItem=Native("ICSharpCode.WpfDesign.PlacementType").GetField("AddItem").GetValue(null);
    placement=Native("ICSharpCode.WpfDesign.PlacementOperation").GetMethod("TryStartInsertNewComponents").Invoke(null,new object[]{root,items,new Rect[]{rect},addItem});
    if(placement==null)throw new InvalidOperationException("Native designer rejected the insertion bounds");
    CallNative(placement,"Commit");
    // 在仍可回滚时完成句柄分配与原生回读，避免创建成功后因句柄上限或 getter 报错而返回失败。
    committedResult=Obj("draft_id",draftId,"model",DescribeOwned(draft.Model,draft),"design_item",DescribeOwned(item,draft),"name",S(a,"name"),"name_storage","native_Comment","display_name",Member(draft.Model,"DisplayName"),"canvas_changed",true,"dirty",null,"project",ProjectFile());
    CallNative(group,"Commit");
   }catch(Exception error){
    // placement.Commit 之后组提交仍可能失败；已完成的 placement 不能再次 Abort，否则会掩盖首错并跳过外层回滚。
    var failures=new List<Exception>{error};
    if(placement!=null)try{if(!(bool)Member(placement,"IsCommitted")&&!(bool)Member(placement,"IsAborted"))CallNative(placement,"Abort");}catch(Exception rollback){failures.Add(rollback);}
    try{CallNative(group,"Abort");}catch(Exception rollback){failures.Add(rollback);}
    if(failures.Count>1)throw new AggregateException("Native insertion failed and rollback was incomplete",failures);
    throw;
   }
   // 不选择或定位控件；画布通过原生绑定更新，不打开属性窗口或抢占焦点。
   draft.CommittedView=view;
   drawingDrafts.Remove(draftId);
   foreach(var id in refViews.Where(x=>Object.ReferenceEquals(x.Value,draft)).Select(x=>x.Key).ToArray())refViews[id]=view;
   try{committedResult["dirty"]=Member(view,"IsDirty");}
   catch(Exception diagnostic){committedResult["warnings"]=new[]{"创建已提交，但未能读取页面修改状态："+diagnostic.Message};}
   return committedResult;
  }
 }
}
