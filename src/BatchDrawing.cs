using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Windows;

namespace FStudioMcp {
 internal static partial class VisibleHost {
  /// <summary>批量请求经过验证后的声明。保留输入边界，实际边界始终由原生模型回读。</summary>
  sealed class BatchWidget {
   internal Dictionary<string,object> Input;
   internal string Name;
   internal Rect Requested;
   internal DrawingDraft Draft;
   internal object Item,Component,SettingsHandler;
   internal bool ToolSettingsOpen,SettingsOpen;
  }

  /// <summary>两个批量接口只操作当前业务页；UUID 是并发切页时的前置条件，不会自动切页。</summary>
  static void RequireBatchPage(Dictionary<string,object>a,object page){
   RequireBasicPage(page);
   if(a.ContainsKey("page_uuid")){
    Guid expected;
    if(!(a["page_uuid"] is string)||!Guid.TryParse((string)a["page_uuid"],out expected))throw new ArgumentException("page_uuid must be a UUID string");
    if(expected!=PageUuid(page))throw new InvalidOperationException("Active page does not match page_uuid; no canvas changes were made");
   }
  }

  /// <summary>拒绝布尔、数字字符串、无穷与分数索引，避免 Convert 默认取整使分页丢对象。</summary>
  static double BatchNumber(object value,string name,double minimum,double maximum,bool integer){
   if(value==null||value is bool||value is string||!Scalar(value.GetType()))throw new ArgumentException(name+" must be a number");
   double number=Convert.ToDouble(value,CultureInfo.InvariantCulture);
   if(Double.IsNaN(number)||Double.IsInfinity(number)||number<minimum||number>maximum||(integer&&number!=Math.Truncate(number)))throw new ArgumentException("Invalid "+name);
   return number;
  }
  static object[] BatchArray(Dictionary<string,object>a,string name,int maximum,bool required){
   object raw;
   if(!a.TryGetValue(name,out raw)){if(required)throw new ArgumentException(name+" is required");return new object[0];}
   var sequence=raw as IEnumerable;
   if(sequence==null||raw is string||raw is IDictionary)throw new ArgumentException(name+" must be an array");
   var values=sequence.Cast<object>().Take(maximum+1).ToArray();
   if(values.Length>maximum)throw new ArgumentException(name+" permits at most "+maximum+" entries");
   return values;
  }
  static Dictionary<string,object> BatchObject(object value,string name){
   var result=value as Dictionary<string,object>;
   if(result==null)throw new ArgumentException(name+" must be an object");
   return result;
  }
  static string BatchString(Dictionary<string,object>a,string name,int maximum){
   object value;
   if(!a.TryGetValue(name,out value)||!(value is string)||String.IsNullOrWhiteSpace((string)value)||((string)value).Length>maximum||((string)value).Any(Char.IsControl))throw new ArgumentException(name+" must be a nonempty string without control characters");
   return (string)value;
  }
  static Rect DrawingBounds(object model){
   var p=Member(model,"Position");
   return new Rect(Convert.ToDouble(Member(p,"X")),Convert.ToDouble(Member(p,"Y")),Convert.ToDouble(Member(p,"Width")),Convert.ToDouble(Member(p,"Height")));
  }
  static Dictionary<string,object> BoundsResult(Rect r){return Obj("x",r.X,"y",r.Y,"width",r.Width,"height",r.Height);}
  static void SetDrawingBounds(object model,Rect r){
   var p=Member(model,"Position");
   NativeSet(p,"X",r.X);NativeSet(p,"Y",r.Y);NativeSet(p,"Width",r.Width);NativeSet(p,"Height",r.Height);
  }
  static void SetDrawingName(object model,string name){
   NativeSet(model,"Comment",name);
   var setName=model.GetType().GetMethod("SetDisplayName",new[]{typeof(string)});
   if(setName!=null)setName.Invoke(model,new object[]{name});
  }

  /// <summary>一次遍历当前页顶层图元，返回紧凑几何快照；筛选身份按全集合判断，分页不隐藏同名冲突。</summary>
  /// <remarks>不合成公共页，不展开组。index 只是本次集合位置，不能代替 UUID 或触摸层级。</remarks>
  static object DrawingSnapshot(Dictionary<string,object>a,object view,object page){
   var watch=Stopwatch.StartNew();RequireBatchPage(a,page);
   int offset=a.ContainsKey("offset")?(int)BatchNumber(a["offset"],"offset",0,Int32.MaxValue,true):0;
   int limit=a.ContainsKey("limit")?(int)BatchNumber(a["limit"],"limit",1,100,true):100;
   string[] names=null;
   if(a.ContainsKey("names")){
    names=BatchArray(a,"names",100,true).Select(x=>BatchString(Obj("name",x),"name",200)).ToArray();
    if(names.Length==0||names.Distinct(StringComparer.Ordinal).Count()!=names.Length)throw new ArgumentException("names must contain 1..100 distinct names");
   }
   var all=((IEnumerable)Member(page,"Graphicses")).Cast<object>().Select((m,i)=>new {Model=m,Index=i,Name=Convert.ToString(Member(m,"Comment"))}).ToArray();
   var wanted=names==null?null:new HashSet<string>(names,StringComparer.Ordinal);
   var matched=all.Where(x=>wanted==null||wanted.Contains(x.Name)).ToArray();
   var counts=matched.GroupBy(x=>x.Name,StringComparer.Ordinal).ToDictionary(g=>g.Key,g=>g.Count(),StringComparer.Ordinal);
   var objects=matched.Skip(offset).Take(limit).Select(x=>Obj("model",DescribeOwned(x.Model,view),"name",x.Name,"uuid",Convert.ToString(Member(x.Model,"UniqueId")),"type",x.Model.GetType().FullName,"bounds",BoundsResult(DrawingBounds(x.Model)),"index",x.Index)).ToArray();
   int consumed=offset+objects.Length;
   return Obj("page_uuid",PageUuid(page).ToString(),"page",Obj("uuid",PageUuid(page).ToString(),"id",Member(page,"Id"),"name",Member(page,"Name"),"type",Convert.ToString(Member(page,"Type")),"width",Member(page,"Width"),"height",Member(page,"Height")),
    "items",objects,"total_count",all.Length,"scope","active_page_top_level_only","matched_count",matched.Length,"offset",offset,"limit",limit,"next_offset",consumed<matched.Length?(object)consumed:null,"complete",consumed>=matched.Length,
    "missing_names",names==null?new string[0]:names.Where(n=>!counts.ContainsKey(n)).ToArray(),"duplicate_names",counts.Where(x=>x.Value>1).Select(x=>x.Key).ToArray(),"filtered",names!=null,"includes_inherited",false,"expands_groups",false,"dirty",Member(view,"IsDirty"),"elapsed_ms",watch.Elapsed.TotalMilliseconds);
  }

  /// <summary>整批先检查输入结构、工具类型、名称、数量和标量值；此阶段不创建模型、不登记组件。</summary>
  static List<BatchWidget> ValidateBatchSpecifications(Dictionary<string,object>a,object page){
   if(a.ContainsKey("presets"))throw new ArgumentException("Host expects expanded widgets; presets must be expanded by the MCP server");
   var inputs=BatchArray(a,"widgets",30,true);if(inputs.Length==0)throw new ArgumentException("widgets must contain 1..30 entries");
   var names=new HashSet<string>(((IEnumerable)Member(page,"Graphicses")).Cast<object>().Select(x=>Convert.ToString(Member(x,"Comment"))),StringComparer.Ordinal);
   var result=new List<BatchWidget>();int propertyCount=0;
   foreach(var raw in inputs){
    var spec=BatchObject(raw,"widget");
    var allowed=new[]{"name","tool","tool_properties","x","y","width","height","properties","local_addresses","text","text_style"};
    if(spec.Keys.Any(k=>!allowed.Contains(k)))throw new ArgumentException("Unknown widget field: "+spec.Keys.First(k=>!allowed.Contains(k)));
    string name=BatchString(spec,"name",200);
    if(!names.Add(name))throw new ArgumentException("Duplicate or existing widget name: "+name+"; creation is not upsert");
    string toolName=BatchString(spec,"tool",256);var type=Native(toolName);
    if(type.Namespace!="Flexem.Studio.GraphicsDesigner.Tools"||type.IsAbstract||type.ContainsGenericParameters||!type.Name.EndsWith("Tool")||type.GetConstructor(Type.EmptyTypes)==null)throw new ArgumentException("Expected a concrete native drawing tool: "+toolName);
    if(type.Name=="WindowSelectorTool"&&type.Assembly.GetName().Version.ToString()=="3.0.15685.0")throw new NotSupportedException("NATIVE_UNSUPPORTED: WindowSelectorTool cannot safely reopen in FStudio 3.0.15685.0");
    var coordinates=new double[4];var coordinateNames=new[]{"x","y","width","height"};
    for(int i=0;i<4;i++){
     object value;if(!spec.TryGetValue(coordinateNames[i],out value))throw new ArgumentException(coordinateNames[i]+" is required for "+name);
     coordinates[i]=BatchNumber(value,coordinateNames[i],i<2?0:1,16384,false);
    }
    if(spec.ContainsKey("tool_properties")){
     var properties=BatchObject(spec["tool_properties"],"tool_properties");if(properties.Count>20)throw new ArgumentException("At most 20 tool_properties are allowed");
     foreach(var pair in properties){
      var p=type.GetProperty(pair.Key,BindingFlags.Public|BindingFlags.Instance);
      if(p==null||p.GetSetMethod()==null||p.GetIndexParameters().Length!=0||!Scalar(p.PropertyType))throw new ArgumentException("Unsupported tool property: "+pair.Key);
      RequireBatchScalar(pair.Value,pair.Key);ConvertArg(pair.Value,p.PropertyType);
     }
    }
    var paths=new HashSet<string>(StringComparer.Ordinal);
    foreach(var entry in BatchArray(spec,"properties",60,false)){
     var change=BatchObject(entry,"property");string path=BatchString(change,"member",512);RequireBatchPath(path);
     if(change.Count!=2||!change.ContainsKey("value"))throw new ArgumentException("Each property requires only member and value");
     if(!paths.Add(path))throw new ArgumentException("Duplicate property path: "+path);
     RequireBatchScalar(change["value"],path);propertyCount++;
    }
    paths.Clear();
    foreach(var entry in BatchArray(spec,"local_addresses",8,false)){
     var address=BatchObject(entry,"local address");string path=BatchString(address,"member",512);RequireBatchPath(path);
     if(address.Count!=2||!address.ContainsKey("address"))throw new ArgumentException("Each local address requires only member and address");
     if(!paths.Add(path))throw new ArgumentException("Duplicate address path: "+path);
     BatchNumber(address["address"],"address",0,UInt32.MaxValue,true);
    }
    // 原生标签编译规则不允许全部语言内容为空；声明式批量创建在任何模型修改前拒绝。
    if(type.Name=="StaticTextTool"&&(!spec.ContainsKey("text")||!(spec["text"] is string)||String.IsNullOrWhiteSpace((string)spec["text"])))throw new ArgumentException("StaticTextTool requires nonempty text; use canvas_prepare for staged configuration");
    if(spec.ContainsKey("text")||spec.ContainsKey("text_style")){
     if(type.Name!="StaticTextTool")throw new ArgumentException("text and text_style require StaticTextTool");
     if(spec.ContainsKey("text")&&(!(spec["text"] is string)||((string)spec["text"]).Length>4096))throw new ArgumentException("text must be a string of at most 4096 characters");
     if(spec.ContainsKey("text_style"))ValidateBatchTextStyle(BatchObject(spec["text_style"],"text_style"));
    }
    result.Add(new BatchWidget{Input=spec,Name=name,Requested=new Rect(coordinates[0],coordinates[1],coordinates[2],coordinates[3])});
   }
   if(propertyCount>1200)throw new ArgumentException("At most 1200 expanded properties are allowed per batch");
   return result;
  }
  static void RequireBatchScalar(object value,string name){
   if(value!=null&&!Scalar(value.GetType()))throw new ArgumentException("Batch property "+name+" requires a scalar value; shared model handles are not supported");
   if(value is double&&(Double.IsNaN((double)value)||Double.IsInfinity((double)value)))throw new ArgumentException("Non-finite value: "+name);
  }
  /// <summary>声明中禁止覆盖身份与几何，也不允许沿 Parent/服务路径修改其他对象。</summary>
  static void RequireBatchPath(string path){
   var segments=path.Split('.');
   if(segments.Length>16||segments.Any(s=>!Regex.IsMatch(s,"^[A-Za-z_][A-Za-z0-9_]*$")))throw new ArgumentException("Expected a plain property path: "+path);
   var forbidden=new[]{"Position","Comment","Name","DisplayName","ComponentId","UniqueId","Id","Parent","Project","ProjectContext","Services","View","ViewModel"};
   if(segments.Any(s=>forbidden.Contains(s)))throw new ArgumentException("Identity, geometry and ownership paths cannot be set in batch properties: "+path);
  }
  static void ValidateBatchTextStyle(Dictionary<string,object>style){
   foreach(var pair in style){
    if(pair.Key=="font_size")BatchNumber(pair.Value,"font_size",8,120,false);
    else if(pair.Key=="color"){if(!(pair.Value is string)||!Regex.IsMatch((string)pair.Value,"^#[0-9A-Fa-f]{8}$"))throw new ArgumentException("color must be #AARRGGBB");}
    else if(pair.Key=="bold"){if(!(pair.Value is bool))throw new ArgumentException("bold must be boolean");}
    else if(pair.Key=="font_name")BatchString(style,"font_name",200);
    else throw new ArgumentException("Unsupported text_style field: "+pair.Key);
   }
  }

  /// <summary>以原生当前语言建立可编辑文字项；复用原生字体数据，其他语言内容不被覆盖。</summary>
  static void ConfigureBatchText(BatchWidget widget){
   // 批量文字省去了属性面板，仍需显式补全当前语言条目，再写入已校验的非空文字。
   if(widget.Draft.Tool.GetType().Name!="StaticTextTool")return;
   var normal=Member(widget.Draft.Model,"NormalInfo");var services=PageServices();
   int languageId=Convert.ToInt32(Member(Member(services,"LanguageService"),"CurrentLanguageId"));
   var languages=Member(Member(CurrentProject(),"HMIConfigInfo"),"LanguageConfigInfo");
   var language=((IEnumerable)Member(languages,"LanguageInfos")).Cast<object>().SingleOrDefault(x=>Convert.ToInt32(Member(x,"SN"))==languageId);
   if(language==null)throw new InvalidOperationException("Current native language is not configured");
   var contents=Member(normal,"StaticTextContentInfos");
   var content=((IEnumerable)contents).Cast<object>().SingleOrDefault(x=>Convert.ToInt32(Member(x,"LanguageSN"))==languageId);
   if(content==null){
    // CreateAsChild 还重载了 IReadonlyFont 参数；精确使用本语言 FontInfo 类型，避免两个兼容重载产生歧义。
    var nativeFont=Member(language,"FontInfo");
    var factory=Native("Flexem.Studio.Components.Graphicses.StaticTextContentInfo").GetMethod("CreateAsChild",new[]{typeof(int),nativeFont.GetType()});
    if(factory==null)throw new InvalidOperationException("Native language-font factory is unavailable");
    content=factory.Invoke(null,new[]{(object)languageId,nativeFont});CallNative(contents,"Add",content);
   }
   if(widget.Input.ContainsKey("text")||widget.Input.ContainsKey("text_style")){
    if(Convert.ToString(Member(normal,"UsingType"))!="Label")throw new ArgumentException("text and text_style cannot be combined with a non-Label static text mode");
   }
   NativeSet(normal,"LanguageSN",languageId);NativeSet(content,"UsingType",Member(normal,"UsingType"));
   if(widget.Input.ContainsKey("text"))NativeSet(content,"LabelContent",widget.Input["text"]);
   if(widget.Input.ContainsKey("text_style")){
    var fontStyle=Member(content,"FontStyleInfo");var font=Member(fontStyle,"Font");
    if(font==null)throw new InvalidOperationException("Native text font is not initialized");
    // 直接样式应使用本条目的字体；否则已选字体样式会优先覆盖用户声明。
    NativeSet(fontStyle,"UseFontStyle",false);
    var mapping=new Dictionary<string,string>{{"font_size","FontSize"},{"color","FontColor"},{"bold","IsBold"},{"font_name","FontName"}};
    foreach(var pair in (Dictionary<string,object>)widget.Input["text_style"])NativeSet(font,mapping[pair.Key],pair.Value);
   }
  }

  /// <summary>GenerateId 扫描已登记对象而非递增计数；未登记的同批草稿必须通过原生接口补分配编号。</summary>
  static void AllocateBatchComponentIds(List<BatchWidget>widgets,object context){
   var contract=Native("Flexem.Studio.GraphicsDesign.IGraphicsProvider");var provider=DesignerService(context,contract.FullName);
   var occupied=((IEnumerable)contract.GetProperty("Graphics").GetValue(provider,null)).Cast<object>().ToList();
   var component=Native("Flexem.Studio.GraphicsDesign.IComponent");var updatable=Native("Flexem.Studio.GraphicsDesign.IUpdatableComponent");
   foreach(var widget in widgets){
    var model=widget.Draft.Model;
    if(component.IsInstanceOfType(model)){
     int id=Convert.ToInt32(component.GetProperty("ComponentId").GetValue(model,null));
     var used=new HashSet<int>(occupied.Where(x=>x.GetType()==model.GetType()&&component.IsInstanceOfType(x)).Select(x=>Convert.ToInt32(component.GetProperty("ComponentId").GetValue(x,null))));
     int available=id;while(used.Contains(available)){if(available==Int32.MaxValue)throw new InvalidOperationException("No component id is available");available++;}
     if(available!=id){if(!updatable.IsInstanceOfType(model))throw new InvalidOperationException("Native model cannot allocate a distinct batch component id");updatable.GetMethod("SetComponentId").Invoke(model,new object[]{available});}
    }
    occupied.Add(model);
   }
  }

  /// <summary>仅对新草稿配置地址；复用既有原生设备/寄存器解析和直接地址限制。</summary>
  static void ConfigureBatchWidget(BatchWidget widget,object view){
   var draft=widget.Draft;var before=ToolHook(draft.Tool,"BeforeShowSetting",1);
   if(before!=null){widget.ToolSettingsOpen=true;before.Invoke(draft.Tool,new[]{draft.ViewModel});}
   // 新草稿尚未进入页面，可以按声明顺序设置；每步重新解析路径，跟随前一模式设置重建的子对象。
   // 后续属性失败会使整批草稿被丢弃，不会留下部分画布写入。
   foreach(var entry in BatchArray(widget.Input,"properties",60,false)){
    var change=(Dictionary<string,object>)entry;object target;var property=ResolvePropertyTarget(draft.Model,S(change,"member"),out target);
    RequireBackgroundModelTarget(target);
    if(!Scalar(property.PropertyType))throw new ArgumentException("Batch properties cannot replace native child objects: "+S(change,"member"));
    property.SetValue(target,ConvertArg(change["value"],property.PropertyType),null);
   }
   foreach(var entry in BatchArray(widget.Input,"local_addresses",8,false)){
    var address=(Dictionary<string,object>)entry;var target=ResolveMemberPath(draft.Model,S(address,"member"));
    var handle=(Dictionary<string,object>)DescribeOwned(target,draft);
    if(handle==null||!handle.ContainsKey("$ref"))throw new ArgumentException("Address path did not resolve to a native address: "+S(address,"member"));
    ConfigureLocalAddress(Obj("ref",S(handle,"$ref"),"address",address["address"]),view);
   }
   ConfigureBatchText(widget);
   FinishBatchToolSettings(widget);
   SetDrawingName(draft.Model,widget.Name);SetDrawingBounds(draft.Model,widget.Requested);
  }
  static void FinishBatchToolSettings(BatchWidget widget){
   if(!widget.ToolSettingsOpen)return;
   widget.ToolSettingsOpen=false;
   var after=ToolHook(widget.Draft.Tool,"AfterShowSetting",1);if(after!=null)after.Invoke(widget.Draft.Tool,new[]{widget.Draft.ViewModel});
  }
  /// <summary>后台 Sliding 与原生鼠标绘图一样，必须在绑定器访问模板部件前应用模板。</summary>
  static void InitializeBatchView(object context,object component,Type viewType){
   if(viewType.FullName!="Flexem.Studio.GraphicsDesigner.Presentation.Shapes.Sliding")return;
   var element=(FrameworkElement)component;var canvas=Member(Member(context,"RootItem"),"View") as FrameworkElement;
   if(element.Style==null&&canvas!=null)element.Style=canvas.TryFindResource(viewType) as Style;
   element.ApplyTemplate();
   if(new[]{"BackgroundGalleryViewer","SliderGalleryViewer","Scale"}.Any(n=>Member(component,n)==null))throw new InvalidOperationException("Native Sliding view template did not initialize its required parts");
  }
  static bool BoundsDiffer(Rect a,Rect b){return Math.Abs(a.X-b.X)>0.01||Math.Abs(a.Y-b.Y)>0.01||Math.Abs(a.Width-b.Width)>0.01||Math.Abs(a.Height-b.Height)>0.01;}
  /// <summary>释放仅供批量内部地址设置使用的句柄，避免失败草稿或临时子模型长期保活。</summary>
  static void ForgetBatchDraft(DrawingDraft draft){
   ClearScopeHandles(draft);
  }

  /// <summary>整批草稿准备成功后以一次原生插入、一个撤销组提交；异常时逐项收集回滚失败，保留首错。</summary>
  static object DrawingCreateMany(Dictionary<string,object>a,object view,object page,object context){
   var watch=Stopwatch.StartNew();RequireBatchPage(a,page);
   var widgets=ValidateBatchSpecifications(a,page);double validated=watch.Elapsed.TotalMilliseconds;
   var original=((IEnumerable)Member(page,"Graphicses")).Cast<object>().ToArray();
   object group=null,placement=null;bool placementCommitted=false,groupCommitted=false;
   var warnings=new List<string>();var results=new List<Dictionary<string,object>>();Dictionary<string,object> response=null;double prepared=0,inserted=0;
   try{
    // 不在撤销事务内创建默认模型，遵循原生 SingleStepCreateComponentTool 的初始化边界。
    foreach(var widget in widgets)widget.Draft=CreateDrawingDraft(widget.Input,page,context,true);
    AllocateBatchComponentIds(widgets,context);
    foreach(var widget in widgets)ConfigureBatchWidget(widget,view);
    // 原生工厂负责 UUID；在写入页面前验证其结果，禁止用空值或重复 UUID 作为稳定身份。
    var identities=new HashSet<Guid>(original.Select(x=>(Guid)Member(x,"UniqueId")));
    foreach(var widget in widgets){Guid uuid=(Guid)Member(widget.Draft.Model,"UniqueId");if(uuid==Guid.Empty||!identities.Add(uuid))throw new InvalidOperationException("Native factory returned an empty or duplicate widget UUID");}
    prepared=watch.Elapsed.TotalMilliseconds;
    var root=Member(context,"RootItem");group=CallNative(root,"OpenGroup","MCP: create "+widgets.Count+" widgets");
    var extensions=Member(Member(context,"Services"),"ExtensionManager");
    var componentService=DesignerService(context,"ICSharpCode.WpfDesign.IComponentService");
    var registry=DesignerService(context,"Flexem.Studio.GraphicsDesign.IRootGraphicRegistry");
    var settings=Native("Flexem.Studio.GraphicsDesign.IHandleSettings");
    var items=Array.CreateInstance(Native("ICSharpCode.WpfDesign.DesignItem"),widgets.Count);var bounds=new Rect[widgets.Count];
    for(int i=0;i<widgets.Count;i++){
     var widget=widgets[i];var viewType=(Type)Member(widget.Draft.Model,"ViewType");
     widget.Component=CallNative(extensions,"CreateInstanceWithCustomInstanceFactory",viewType,null);
     widget.Item=CallNative(componentService,"RegisterComponentForDesigner",widget.Component);items.SetValue(widget.Item,i);
     CallNative(extensions,"ApplyDefaultInitializers",widget.Item);InitializeBatchView(context,widget.Component,viewType);
     CallNative(registry,"RegisterRootGraphic",widget.Draft.Model,widget.Item);
     var element=Member(widget.Item,"View") as FrameworkElement;var handler=element==null?null:element.DataContext;
     if(handler!=null&&settings.IsInstanceOfType(handler)){
      widget.SettingsHandler=handler;widget.SettingsOpen=true;settings.GetMethod("BeforeSettings").Invoke(handler,new[]{widget.Item});
     }
     else if(widget.Input.ContainsKey("text")||widget.Input.ContainsKey("text_style"))throw new InvalidOperationException("Native static text settings handler is unavailable; text size was not verified");
     // 注册绑定可能先按默认字体修改尺寸；最终请求边界在刷新前再次施加。
     SetDrawingBounds(widget.Draft.Model,widget.Requested);
     if(widget.SettingsOpen){settings.GetMethod("AfterSettings").Invoke(widget.SettingsHandler,new[]{widget.Item,(object)true});widget.SettingsOpen=false;}
     bounds[i]=DrawingBounds(widget.Draft.Model);
    }
    var addItem=Native("ICSharpCode.WpfDesign.PlacementType").GetField("AddItem").GetValue(null);
    placement=Native("ICSharpCode.WpfDesign.PlacementOperation").GetMethod("TryStartInsertNewComponents").Invoke(null,new object[]{root,items,bounds,addItem});
    if(placement==null)throw new InvalidOperationException("Native designer rejected batch insertion");
    CallNative(placement,"Commit");placementCommitted=true;
    var actual=((IEnumerable)Member(page,"Graphicses")).Cast<object>().ToArray();
    if(actual.Length!=original.Length+widgets.Count||original.Any(x=>!actual.Any(y=>Object.ReferenceEquals(x,y)))||widgets.Any(w=>actual.Count(x=>Object.ReferenceEquals(x,w.Draft.Model))!=1))throw new InvalidOperationException("Native batch insertion changed unexpected canvas entries");
    foreach(var widget in widgets){
     var model=widget.Draft.Model;var actualBounds=DrawingBounds(model);bool adjusted=BoundsDiffer(widget.Requested,actualBounds);
     if(Convert.ToString(Member(model,"Comment"))!=widget.Name)throw new InvalidOperationException("Native widget name did not persist: "+widget.Name);
     if(adjusted)warnings.Add(widget.Name+"：原生文字或控件规则调整了请求尺寸，请检查相邻内容间距。");
     if(actualBounds.Left<0||actualBounds.Top<0||actualBounds.Right>Convert.ToDouble(Member(page,"Width"))||actualBounds.Bottom>Convert.ToDouble(Member(page,"Height")))warnings.Add(widget.Name+"：实际边界超出页面。");
     // 句柄与所有原生回读都在 commit 前完成；句柄上限等错误必须仍可触发整个批次回滚。
     results.Add(Obj("model",DescribeOwned(model,widget.Draft),"name",widget.Name,"uuid",Convert.ToString(Member(model,"UniqueId")),"type",model.GetType().FullName,"requested_bounds",BoundsResult(widget.Requested),"actual_bounds",BoundsResult(actualBounds),"bounds_adjusted",adjusted,"index",Array.FindIndex(actual,x=>Object.ReferenceEquals(x,model))));
    }
    inserted=watch.Elapsed.TotalMilliseconds;
    response=Obj("page_uuid",PageUuid(page).ToString(),"created",results.ToArray(),"count",results.Count,"warnings",warnings.ToArray(),"native_undo_transaction",true,"placement_operations",1,"canvas_changed",true,"dirty",Member(view,"IsDirty"),"persistence","save_project_to_persist","text_scope","current_native_language");
    CallNative(group,"Commit");groupCommitted=true;
    // 只转移已经存在的模型句柄；finally 会释放仍属草稿的临时地址句柄，不会误删结果。
    for(int i=0;i<widgets.Count;i++){
     widgets[i].Draft.CommittedView=view;
     refViews[S((Dictionary<string,object>)results[i]["model"],"$ref")]=view;
    }
    // IsDirty 可能在组提交事件后才更新。诊断读取失败不能把已成功的创建伪报为失败。
    try{response["dirty"]=Member(view,"IsDirty");}
    catch(Exception diagnostic){response["dirty"]=null;warnings.Add("创建已提交，但未能读取页面修改状态："+diagnostic.Message);response["warnings"]=warnings.ToArray();}
   }catch(Exception error){
    var failures=new List<Exception>{error};
    // placement 完成后再次 Abort 会掩盖原错；每个回滚步骤独立执行，外层组始终有机会恢复。
    if(placement!=null&&!placementCommitted)try{if(!(bool)Member(placement,"IsAborted")&&!(bool)Member(placement,"IsCommitted"))CallNative(placement,"Abort");}catch(Exception rollback){failures.Add(rollback);}
    foreach(var widget in widgets.Where(w=>w.SettingsOpen))try{Native("Flexem.Studio.GraphicsDesign.IHandleSettings").GetMethod("AfterSettings").Invoke(widget.SettingsHandler,new[]{widget.Item,(object)false});widget.SettingsOpen=false;}catch(Exception rollback){failures.Add(rollback);}
    if(group!=null&&!groupCommitted)try{CallNative(group,"Abort");}catch(Exception rollback){failures.Add(rollback);}
    foreach(var widget in widgets.Where(w=>w.Draft!=null)){
     try{FinishBatchToolSettings(widget);}catch(Exception cleanup){failures.Add(cleanup);}
     try{
      // 回滚不完整时保留仍挂在画布上的引用，避免把可诊断的残留对象进一步变成无效对象。
      bool attached=((IEnumerable)Member(page,"Graphicses")).Cast<object>().Any(x=>Object.ReferenceEquals(x,widget.Draft.Model));
      if(!attached&&(bool)Member(widget.Draft.Model,"IsReferenced"))CallNative(widget.Draft.Model,"DisableReference");
     }catch(Exception cleanup){failures.Add(cleanup);}
    }
    try{var after=((IEnumerable)Member(page,"Graphicses")).Cast<object>().ToArray();if(after.Length!=original.Length||after.Where((x,i)=>!Object.ReferenceEquals(x,original[i])).Any())failures.Add(new InvalidOperationException("Canvas rollback was incomplete; inspect the current page before retrying"));}catch(Exception verify){failures.Add(verify);}
    if(failures.Count>1)throw new AggregateException("Batch creation failed and rollback or cleanup was incomplete",failures);
    throw;
   }finally{foreach(var widget in widgets.Where(w=>w.Draft!=null))ForgetBatchDraft(widget.Draft);}
   response["elapsed_ms"]=watch.Elapsed.TotalMilliseconds;
   response["timings_ms"]=Obj("validation",validated,"draft_configuration",prepared-validated,"native_insertion",inserted-prepared,"commit_and_result",watch.Elapsed.TotalMilliseconds-inserted);
   return response;
  }
 }
}
