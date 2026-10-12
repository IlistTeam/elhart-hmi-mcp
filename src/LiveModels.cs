using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows;

namespace FStudioMcp {
 internal static partial class VisibleHost {
  static readonly Dictionary<string,object> refs=new Dictionary<string,object>();
  static readonly Dictionary<string,object> refViews=new Dictionary<string,object>();
  /// <summary>模型可能重写 Equals；句柄必须按实例区分，避免两个内容相同的控件共用句柄。</summary>
  sealed class ModelReferenceComparer:IEqualityComparer<object> {
   public new bool Equals(object left,object right){return Object.ReferenceEquals(left,right);}
   public int GetHashCode(object value){return RuntimeHelpers.GetHashCode(value);}
  }
  static readonly Dictionary<object,string> reverseRefs=new Dictionary<object,string>(new ModelReferenceComparer());
  // 只缓存反射元数据，不缓存属性值；原生模型的 Position 等子对象可能随编辑而更换。
  // 原生编译器错误回调也会读取元数据，使用并发字典；原生 getter 始终在缓存锁外执行。
  static readonly ConcurrentDictionary<Tuple<Type,string>,PropertyInfo> modelProperties=new ConcurrentDictionary<Tuple<Type,string>,PropertyInfo>();
  static void ClearRefs(){refs.Clear();reverseRefs.Clear();refViews.Clear();drawingDrafts.Clear();modelProperties.Clear();}
  /// <summary>释放临时草稿的全部句柄和反向索引；成功提交后应再以页面归属重新描述模型。</summary>
  internal static void ClearScopeHandles(object scope){
   foreach(var id in refViews.Where(x=>Object.ReferenceEquals(x.Value,scope)).Select(x=>x.Key).ToArray()){
    object model;if(refs.TryGetValue(id,out model)){refs.Remove(id);reverseRefs.Remove(model);}
    refViews.Remove(id);
   }
  }
  // Vendor getters that overflow the managed stack. A StackOverflowException is not catchable by any try/catch on
  // either side, so the host only survives if the call is refused before GetValue. Reproduced twice on 2026-09-08
  // at 15:02:23 and 15:06:17 (Application Error 1000, FStudio.exe 3.0.15685.0, exception 0xc00000fd, faulting
  // module unknown) by reading EllipseInfo.Normal, which the design surface also never needs: NormalInfo works.
  static readonly HashSet<string> StackOverflowGetters=new HashSet<string>{
   "Flexem.Studio.Components.Graphicses.EllipseInfo.Normal"};
  /// <summary>获取公开、无索引的属性元数据；成功结果按类型和名称缓存，未知名称不占用缓存。</summary>
  static PropertyInfo ModelProperty(Type type,string name){
   if(String.IsNullOrWhiteSpace(name))throw new ArgumentException("Property name must not be empty");
   var key=Tuple.Create(type,name);PropertyInfo property;
   if(!modelProperties.TryGetValue(key,out property)){
    property=type.GetProperty(name,BindingFlags.Public|BindingFlags.Instance);
    if(property==null||property.GetIndexParameters().Length!=0)throw new ArgumentException("Unknown non-indexed property: "+type.FullName+"."+name);
    modelProperties[key]=property;
   }
   return property;
  }
  /// <summary>在真正执行 getter 前检查公开性与原生崩溃黑名单；批量写入的预读也必须经过这里。</summary>
  static void RequireReadableModelProperty(Type t,PropertyInfo p){
   if(p.GetGetMethod()==null)throw new ArgumentException("Property is not publicly readable: "+p.Name);
   var n=p.Name;
   var declaring=p.DeclaringType??t;
   if(StackOverflowGetters.Contains(t.FullName+"."+n)||StackOverflowGetters.Contains(declaring.FullName+"."+n))
    throw new InvalidOperationException("Refused: the native getter "+declaring.FullName+"."+n+" is a known stack-overflow landmine that terminates the FStudio process and cannot be caught. This member is unreadable by design; read its documented equivalent instead.");
  }
  static object Member(object o,string n){
   if(o==null)return null;
   var p=ModelProperty(o.GetType(),n);RequireReadableModelProperty(o.GetType(),p);
   return p.GetValue(o,null);
  }
  /// <summary>点路径最多 512 字符、16 段 ASCII 属性名；不接受索引或表达式，集合索引仍通过独立参数指定。</summary>
  static string[] ModelMemberSegments(string path){
   if(String.IsNullOrWhiteSpace(path))throw new ArgumentException("Member path must not be empty");
   var segments=path.Split('.');
   if(segments.Any(s=>String.IsNullOrWhiteSpace(s)))throw new ArgumentException("Member path contains an empty property: "+path);
   if(path.Length>512||!System.Text.RegularExpressions.Regex.IsMatch(path,@"\A[A-Za-z_][A-Za-z0-9_]*(\.[A-Za-z_][A-Za-z0-9_]*){0,15}\z"))
    throw new ArgumentException("Expected a property path with 1..16 ASCII identifier segments and at most 512 characters");
   return segments;
  }
  /// <summary>安全读取点路径；每一级都重新取值，并在空中间节点处报告完整路径及失败段。</summary>
  internal static object ResolveMemberPath(object root,string path){
   object current=root;
   foreach(var segment in ModelMemberSegments(path)){
    if(current==null)throw new ArgumentException("Member path '"+path+"' reaches null before '"+segment+"'");
    current=Member(current,segment);
   }
   return current;
  }
  /// <summary>导航中已知的页面/草稿归属必须一致，防止通过关联属性改到另一页面。</summary>
  static void RequireModelPathScope(object target,object scope,string path){
   string id;
   if(scope==null||!reverseRefs.TryGetValue(target,out id))return;
   var knownScope=OwningScope(id);
   if(knownScope!=null&&!Object.ReferenceEquals(knownScope,scope))throw new ArgumentException("Member path crosses page or draft scope: "+path);
  }
  /// <summary>
  /// 解析点路径写入的最终宿主与属性。根对象、每个中间对象和最终宿主均执行后台限制，
  /// 防止经由普通模型关联属性修改 UI。值类型节点会被装箱复制，故拒绝这类无法可靠回写的路径。
  /// </summary>
  internal static PropertyInfo ResolvePropertyTarget(object root,string path,out object target){
   return ResolvePropertyTarget(root,path,out target,null);
  }
  /// <summary>需要批量预检时记录完整父对象链，包括独立子句柄的根，以识别跨句柄的父子替换冲突。</summary>
  static PropertyInfo ResolvePropertyTarget(object root,string path,out object target,List<object> owners){
   var segments=ModelMemberSegments(path);target=root;string rootId;
   var scope=root!=null&&reverseRefs.TryGetValue(root,out rootId)?OwningScope(rootId):null;
   for(int i=0;i<segments.Length;i++){
    if(target==null)throw new ArgumentException("Member path '"+path+"' reaches null before '"+segments[i]+"'");
    RequireBackgroundModelTarget(target);RequireModelPathScope(target,scope,path);
    if(target.GetType().IsValueType)throw new ArgumentException("Member path cannot write through a value-type copy: "+path);
    if(owners!=null)owners.Add(target);
    var property=ModelProperty(target.GetType(),segments[i]);RequireReadableModelProperty(target.GetType(),property);
    if(i==segments.Length-1){
     if(property.GetSetMethod()==null)throw new ArgumentException("Property is not publicly writable: "+path);
     return property;
    }
    target=property.GetValue(target,null);
   }
   throw new ArgumentException("Member path must not be empty");
  }
  static bool Scalar(Type t){t=Nullable.GetUnderlyingType(t)??t;return t.IsPrimitive||t.IsEnum||t==typeof(string)||t==typeof(decimal)||t==typeof(Guid)||t==typeof(DateTime)||t==typeof(TimeSpan);}
  static object Describe(object o){
   if(o==null)return null;var t=o.GetType();
   if(Scalar(t)){if(t.IsEnum||t==typeof(Guid)||t==typeof(DateTime)||t==typeof(TimeSpan))return Convert.ToString(o,CultureInfo.InvariantCulture);if(o is double&&(double.IsNaN((double)o)||double.IsInfinity((double)o)))return o.ToString();return o;}
   string id;
   if(!reverseRefs.TryGetValue(o,out id)){if(refs.Count>=20000)throw new InvalidOperationException("Too many model handles; reopen project");id=Guid.NewGuid().ToString("N");refs[id]=o;reverseRefs[o]=id;}
   if(t.FullName=="Flexem.Studio.HMI.Window.HMIWindowViewContent")refViews[id]=o;
   return Obj("$ref",id,"type",t.FullName,"enumerable",o is IEnumerable);
  }
  static object DescribeOwned(object o,object view){var result=Describe(o);var d=result as Dictionary<string,object>;if(view!=null&&d!=null&&d.ContainsKey("$ref"))refViews[S(d,"$ref")]=view;return result;}
  static object OwningScope(string id){object scope;return refViews.TryGetValue(id,out scope)?scope:null;}
  static object OwningView(string id){var scope=OwningScope(id);var draft=scope as DrawingDraft;return draft==null?scope:draft.CommittedView;}
  static object Ref(Dictionary<string,object>a){object o;if(!refs.TryGetValue(S(a,"ref"),out o))throw new ArgumentException("Unknown or expired model reference");return o;}
  static object Resolve(string name){var type=Native(name);var ioc=Native("Flexem.Infrastructure.IoC");return ioc.GetMethods().Single(m=>m.Name=="Resolve"&&m.IsGenericMethodDefinition&&m.GetParameters().Length==0).MakeGenericMethod(type).Invoke(null,new object[0]);}
  static object LiveState(){
   var windows=Application.Current==null?new Window[0]:Application.Current.Windows.Cast<Window>().ToArray();
   var hmi=CurrentProject()==null?null:Member(Resolve("Flexem.Studio.Hmi.Project.IHmiProjectService"),"CurrentProject");
   return Obj("project",ProjectFile(),"project_model",Describe(CurrentProject()),"hmi_model",Describe(hmi),"workbench",Describe(Resolve("Flexem.Studio.Workbench.IWorkbench")),"windows",windows.Select(w=>Obj("title",w.Title,"type",w.GetType().FullName,"window_model",Describe(w),"data_context",Describe(w.DataContext),"visible",w.IsVisible,"window_state",w.WindowState.ToString())).ToArray(),"visible_log",false,"execution_mode",ExecutionMode);
  }
  static object Output(){
   var t=Native("Flexem.Studio.Pads.Output.OutputPad");var field=t.GetField("_instance",BindingFlags.NonPublic|BindingFlags.Static);
   var view=field==null?null:field.GetValue(null);
   if(view==null)return Obj("project",ProjectFile(),"categories",new object[0],"status","not_created");
   var categories=(IEnumerable)Member(view,"OutputCategories");
   return Obj("project",ProjectFile(),"categories",categories.Cast<object>().Select(c=>Obj("category",Member(c,"Category"),"display_name",Member(c,"DisplayCategory"),"text",Member(c,"Text"))).ToArray());
  }
  static object ConvertArg(object v,Type t){
   if(v==null){if(t.IsValueType&&Nullable.GetUnderlyingType(t)==null)throw new ArgumentException("Null is not allowed for "+t.FullName);return null;}
   var d=v as Dictionary<string,object>;if(d!=null&&d.ContainsKey("$ref")){object r;if(!refs.TryGetValue(S(d,"$ref"),out r)||!t.IsInstanceOfType(r))throw new ArgumentException("Incompatible model reference");return r;}
   t=Nullable.GetUnderlyingType(t)??t;
   if(t==typeof(byte[])&&v is string)return Convert.FromBase64String((string)v);
   if(t==typeof(Type)&&d!=null&&d.ContainsKey("$type"))return Native(S(d,"$type"));
   if(t.IsInstanceOfType(v))return v;
   if(t.IsEnum)return Enum.Parse(t,Convert.ToString(v),false);
   if(t==typeof(Guid))return Guid.Parse(Convert.ToString(v));
   if(t.IsArray){var values=(IEnumerable)v;var items=values.Cast<object>().ToArray();var arr=Array.CreateInstance(t.GetElementType(),items.Length);for(int i=0;i<items.Length;i++)arr.SetValue(ConvertArg(items[i],t.GetElementType()),i);return arr;}
   if(t.IsGenericType&&v is IEnumerable&&!(v is string)){
    var g=t.GetGenericTypeDefinition();
    if(g==typeof(IEnumerable<>)||g==typeof(ICollection<>)||g==typeof(IList<>)||g==typeof(List<>)){
     var itemType=t.GetGenericArguments()[0];var list=(IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(itemType));
     foreach(var item in (IEnumerable)v)list.Add(ConvertArg(item,itemType));return list;
    }
   }
   if(Scalar(t))return Convert.ChangeType(v,t,CultureInfo.InvariantCulture);
   throw new ArgumentException("Complex arguments must use a compatible $ref: "+t.FullName);
  }
  static object MethodEntry(MethodInfo m){return Obj("name",m.Name,"signature",m.ToString(),"return_type",m.ReturnType.FullName,"parameters",m.GetParameters().Select(p=>Obj("name",p.Name,"type",p.ParameterType.FullName,"optional",p.IsOptional)).ToArray());}
  static void RequireBackgroundModelTarget(object target) {
   if(target is DependencyObject||target is System.Windows.Input.ICommand||target is ICSharpCode.Core.ICommand)
    throw new InvalidOperationException("BACKGROUND_ONLY: UI objects and interactive commands cannot be changed or invoked through model APIs.");
   if(target==null)return;
   var type=target.GetType();
   if(type.Namespace=="Flexem.Studio.GraphicsDesigner.Tools"||type.GetInterfaces().Any(i=>new[]{"Flexem.Studio.Workbench.IWorkbench","Flexem.Studio.Workbench.IViewContent","ICSharpCode.SharpDevelop.Gui.IViewContent","ICSharpCode.SharpDevelop.Gui.IWorkbenchWindow","ICSharpCode.SharpDevelop.Gui.IWorkbench","Caliburn.Micro.IWindowManager","Flexem.Studio.Workbench.IAsynchronousWaitDialog"}.Contains(i.FullName)))
    throw new InvalidOperationException("BACKGROUND_ONLY: Designer tools and workbench UI are inspect-only through model APIs.");
  }
  /// <summary>阻止会启动交互窗口的模型方法；图库导入还需在参数转换后检查重名。</summary>
  static void RequireBackgroundMethod(object target,string method) {
   RequireBackgroundModelTarget(target);
   string[] interactive={"Activate","Show","ShowDialog","ShowWindow","ShowSettings","ShowSetting","Edit","Execute","Run","Focus","BringToFront","SelectGraphics","StartDrag","DoDragDrop","OpenOrActiveWindow","CreateWindow","CreateWindowWithoutOpen","DeleteWindow","Close","CloseAsync","TryClose","Deactivate","Hide","CloseWindow","OpenHMIWindow","LocateSettings","ImportOrSelectStatusVector","BatchAddGraphs","OpenGraph"};
   if(interactive.Contains(method)||new[]{"Show","Activate","Focus","Bring","SelectGraphic"}.Any(prefix=>method.StartsWith(prefix,StringComparison.OrdinalIgnoreCase))||method.IndexOf("Dialog",StringComparison.OrdinalIgnoreCase)>=0||method.IndexOf("Mouse",StringComparison.OrdinalIgnoreCase)>=0||method.IndexOf("Drag",StringComparison.OrdinalIgnoreCase)>=0)
    throw new InvalidOperationException("BACKGROUND_ONLY: Interactive method "+method+" is disabled; use the dedicated background API.");
  }
  /// <summary>图库导入仅允许显式、无重名的来源，避免选择或冲突对话框阻塞后台队列。</summary>
  static void RequireBackgroundGraphImport(object target,string method,object[] arguments){
   if(target==null||target.GetType().FullName!="Flexem.Studio.HMI.ProjectGraphLibraryService"||method!="Import")return;
   if(arguments.Length==0)throw new InvalidOperationException("BACKGROUND_ONLY: Import requires an explicit source graph; the graph-selection dialog is disabled");
   if(arguments[0]==null)throw new ArgumentException("Source graph is required");
   var category=Member(Member(CurrentProject(),"GraphLib"),"CurrentProjectGraphCategory");
   string directory=Convert.ToString(Member(category,"Dir")),name=Convert.ToString(Member(arguments[0],"Name"));
   // 原生按文件节点存储图库；检测磁盘文件可覆盖已保存但尚未加载到集合的重名资源。
   if(System.IO.Directory.Exists(directory)&&System.IO.Directory.EnumerateFiles(directory,"*.fsvg").Any(p=>String.Equals(System.IO.Path.GetFileNameWithoutExtension(p),name,StringComparison.OrdinalIgnoreCase)))
    throw new InvalidOperationException("BACKGROUND_ONLY: A graph named '"+name+"' already exists; reuse its model handle instead of opening the native conflict dialog");
  }
  /// <summary>按精确签名转换参数并调用已通过后台约束的方法，返回归属当前会话的结果句柄。</summary>
  static object Invoke(MethodInfo[] methods,object target,Dictionary<string,object>a){
   var name=S(a,"method");var args=a.ContainsKey("arguments")?((IEnumerable)a["arguments"]).Cast<object>().ToArray():new object[0];
   var choices=methods.Where(m=>m.Name==name&&!m.ContainsGenericParameters&&m.GetParameters().Length==args.Length&&( !a.ContainsKey("signature")||m.ToString()==S(a,"signature"))).ToArray();
   if(choices.Length!=1)throw new ArgumentException("Method is missing or ambiguous; use exact signature from inspect_object");
   var method=choices[0];RequireBackgroundMethod(target,name);var pars=method.GetParameters();
   var converted=args.Select((x,i)=>ConvertArg(x,pars[i].ParameterType)).ToArray();
   RequireBackgroundGraphImport(target,name,converted);
   return DescribeOwned(method.Invoke(target,converted),a.ContainsKey("ref")?OwningScope(S(a,"ref")):null);
  }
  sealed class ModelPropertyChange {internal string Ref,Member;internal object Target,Scope,Before,After;internal PropertyInfo Property;internal object[] PathOwners;}
  // The designer owns the paired WPF/CSLA transaction. Beginning the CSLA
  // session directly would nest another transaction when geometry updates the view.
  static object BeginModelChange(object view,string name){return CallNative(Member(Member(view,"DesignContext"),"RootItem"),"OpenGroup",name);}
  /// <summary>校验数组形状并限制批量大小，避免空请求、字符串枚举或超大输入进入原生模型。</summary>
  static object[] ModelBatchEntries(Dictionary<string,object> arguments,string name){
   object input;
   if(!arguments.TryGetValue(name,out input)||input==null||input is string||input is IDictionary||!(input is IEnumerable))throw new ArgumentException(name+" must be a non-empty array");
   var entries=((IEnumerable)input).Cast<object>().Take(101).ToArray();
   if(entries.Length==0||entries.Length>100)throw new ArgumentException(name+" must contain between 1 and 100 entries");
   return entries;
  }
  /// <summary>先完成路径、归属、重复项和值转换检查，再开始事务；输入错误不会造成部分写入。</summary>
  static object SetModelProperties(Dictionary<string,object> a) {
   var input=ModelBatchEntries(a,"changes");var changes=new List<ModelPropertyChange>();
   foreach(var entry in input){
    var change=entry as Dictionary<string,object>;if(change==null||!change.ContainsKey("ref")||!change.ContainsKey("member")||!change.ContainsKey("value"))throw new ArgumentException("Each change must contain ref, member and value");
    var root=Ref(change);object target;var name=S(change,"member");var owners=new List<object>();
    var property=ResolvePropertyTarget(root,name,out target,owners);
    var id=S(change,"ref");var scope=OwningScope(id);
    if(scope==null)throw new ArgumentException("Batch properties require a page or draft scope; obtain handles from drawing_state or drawing_prepare");
    // 不按请求字符串判重：Position.Width 与单独 Position 句柄的 Width 可能指向同一属性。
    if(changes.Any(c=>Object.ReferenceEquals(c.Target,target)&&c.Property.Equals(property)))throw new ArgumentException("Duplicate resolved property in batch: "+name);
    if(changes.Count>0&&!Object.ReferenceEquals(changes[0].Scope,scope))throw new ArgumentException("All batch properties must belong to the same page or draft");
    var before=Member(target,property.Name);
    // 两个方向都在写入前拒绝：替换父属性后，独立旧子句柄仍能解析，单靠执行前重读不足以识别脱离模型的写入。
    // 执行阶段仍保留重新解析，用于发现模式 setter 等无法从属性关系预知的子对象重建。
    if(changes.Any(c=>(c.Before!=null&&owners.Any(o=>Object.ReferenceEquals(o,c.Before)))||(before!=null&&c.PathOwners.Any(o=>Object.ReferenceEquals(o,before)))))
     throw new ArgumentException("Parent/child replacement conflict in batch: "+name);
    changes.Add(new ModelPropertyChange{Ref=id,Member=name,Target=target,Scope=scope,Property=property,Before=before,After=ConvertArg(change["value"],property.PropertyType),PathOwners=owners.ToArray()});
   }
   var owner=OwningView(changes[0].Ref);object transaction=null;int applied=0;
   try{
    if(owner!=null)transaction=BeginModelChange(owner,"MCP: properties");
    foreach(var c in changes){
     // 前面的 setter 可能替换父对象，或切换模式后重建子模型；预校验时的叶节点此时可能已经脱离模型。
     // 每次执行前重新解析原句柄与路径，只允许仍指向同一宿主和属性的修改，避免对旧对象静默写入并返回成功。
     object currentTarget;var currentProperty=ResolvePropertyTarget(Ref(Obj("ref",c.Ref)),c.Member,out currentTarget);
     if(!Object.ReferenceEquals(currentTarget,c.Target)||!currentProperty.Equals(c.Property))
      throw new InvalidOperationException("Member path target changed during batch: "+c.Member+"; split parent or mode changes into a separate batch and read the updated model before retrying");
     applied++;c.Property.SetValue(c.Target,c.After,null);
    }
    if(transaction!=null)CallNative(transaction,"Commit");
   }catch(Exception error){
    var failures=new List<Exception>{error};
    if(transaction!=null){try{CallNative(transaction,"Abort");}catch(Exception rollback){failures.Add(rollback);}}
    else for(int i=applied-1;i>=0;i--){try{changes[i].Property.SetValue(changes[i].Target,changes[i].Before,null);}catch(Exception rollback){failures.Add(rollback);}}
    if(failures.Count>1)throw new AggregateException("Property change failed and rollback was incomplete; inspect the model before retrying",failures);
    throw;
   }
   return Obj("results",changes.Select(c=>Obj("ref",c.Ref,"member",c.Member,"value",DescribeOwned(Member(c.Target,c.Property.Name),c.Scope))).ToArray(),"native_undo_transaction",transaction!=null,"persistence","native_model_changed_save_project_to_persist");
  }
  /// <summary>为多个控件展开相同属性设置，总量上限仍为 100；由统一写入入口保证单事务与完整预校验。</summary>
  static object ApplyModelProperties(Dictionary<string,object> a){
   var ids=ModelBatchEntries(a,"refs");var properties=ModelBatchEntries(a,"properties");
   if(ids.Length*properties.Length>100)throw new ArgumentException("Expanded batch must not exceed 100 property changes");
   var changes=new List<object>();
   foreach(var id in ids){
    if(!(id is string)||String.IsNullOrWhiteSpace((string)id))throw new ArgumentException("Each refs entry must be a model reference string");
    foreach(var entry in properties){
     var property=entry as Dictionary<string,object>;
     if(property==null||!property.ContainsKey("member")||!property.ContainsKey("value"))throw new ArgumentException("Each property must contain member and value");
     changes.Add(Obj("ref",id,"member",property["member"],"value",property["value"]));
    }
   }
   return SetModelProperties(Obj("changes",changes.ToArray()));
  }
  /// <summary>单次读取沿用原返回结构；点路径不创建无用中间句柄，列表读取优先用 IList 的直接索引。</summary>
  static Dictionary<string,object> ReadModelMember(Dictionary<string,object> a,bool expandCollections=true){
   var root=Ref(a);object value=a.ContainsKey("member")?ResolveMemberPath(root,S(a,"member")):root;
   if(a.ContainsKey("index")){
    int index=Convert.ToInt32(a["index"]);if(index<0)throw new ArgumentException("index must not be negative");
    var enumerable=value as IEnumerable;if(enumerable==null)throw new ArgumentException("Value is not enumerable");
    var list=value as IList;
    if(list!=null){if(index>=list.Count)throw new ArgumentException("index is outside the collection");value=list[index];}
    else value=enumerable.Cast<object>().ElementAt(index);
   }
   int offset=a.ContainsKey("offset")?Convert.ToInt32(a["offset"]):0;
   if(offset<0)throw new ArgumentException("offset must not be negative");
   var owner=OwningScope(S(a,"ref"));
   if(expandCollections&&value is IEnumerable&&!(value is string))return Obj("value",DescribeOwned(value,owner),"items",((IEnumerable)value).Cast<object>().Skip(offset).Take(100).Select(x=>DescribeOwned(x,owner)).ToArray(),"offset",offset);
   return Obj("value",DescribeOwned(value,owner));
  }
  /// <summary>批量读取已知属性默认只描述集合句柄；显式 index/offset 或直接读取集合句柄时才枚举，并保留条目归属。</summary>
  static object ReadModelMembers(Dictionary<string,object> a){
   var reads=ModelBatchEntries(a,"reads");var results=new List<object>();
   foreach(var entry in reads){
    var read=entry as Dictionary<string,object>;
    if(read==null||!read.ContainsKey("ref"))throw new ArgumentException("Each read must contain ref");
    bool expand=read.ContainsKey("index")||read.ContainsKey("offset")||!read.ContainsKey("member");
    var result=ReadModelMember(read,expand);result["ref"]=S(read,"ref");
    if(read.ContainsKey("member"))result["member"]=S(read,"member");
    if(read.ContainsKey("index"))result["index"]=read["index"];
    results.Add(result);
   }
   return Obj("results",results.ToArray());
  }
  static object ModelOperation(Dictionary<string,object>a){
   RequireProject();var op=S(a,"op");
   if(op=="set_members")return SetModelProperties(a);
   if(op=="apply_members")return ApplyModelProperties(a);
   if(op=="read_members")return ReadModelMembers(a);
   if(op=="native_factory"){
    var t=Native(S(a,"type"));if(t.Namespace==null||!t.Namespace.StartsWith("Flexem.Studio."))throw new ArgumentException("Factory type must be a native FStudio model");
    var m=S(a,"method");if(m!="CreateAsChild"&&m!="Create"&&m!="CreateNew")throw new ArgumentException("Only native model creation factories are exposed");
    return Obj("value",Invoke(t.GetMethods(BindingFlags.Public|BindingFlags.Static),null,a));
   }
   var o=Ref(a);var type=o.GetType();
   if(op=="inspect_object")return Obj("object",Describe(o),"properties",type.GetProperties(BindingFlags.Public|BindingFlags.Instance).Where(p=>p.GetIndexParameters().Length==0).Select(p=>Obj("name",p.Name,"type",p.PropertyType.FullName,"readable",p.CanRead,"writable",p.GetSetMethod()!=null,"enum_values",(Nullable.GetUnderlyingType(p.PropertyType)??p.PropertyType).IsEnum?Enum.GetNames(Nullable.GetUnderlyingType(p.PropertyType)??p.PropertyType):null)).ToArray(),"methods",type.GetMethods(BindingFlags.Public|BindingFlags.Instance).Where(m=>!m.IsSpecialName&&m.DeclaringType!=typeof(object)&&!m.ContainsGenericParameters).Select(MethodEntry).ToArray());
   if(op=="read_member")return ReadModelMember(a);
   if(op=="set_member"){
    object target;var p=ResolvePropertyTarget(o,S(a,"member"),out target);
    var owner=OwningView(S(a,"ref"));object transaction=null;
    try{
     if(owner!=null)transaction=BeginModelChange(owner,"MCP: "+p.Name);
     p.SetValue(target,ConvertArg(a["value"],p.PropertyType),null);
     if(transaction!=null)CallNative(transaction,"Commit");
    }catch{if(transaction!=null)CallNative(transaction,"Abort");throw;}
    return Obj("value",DescribeOwned(Member(target,p.Name),OwningScope(S(a,"ref"))),"native_undo_transaction",transaction!=null,"persistence","native_model_changed_save_project_to_persist");
   }
   if(op=="invoke_method")return Obj("value",Invoke(type.GetMethods(BindingFlags.Public|BindingFlags.Instance).Where(m=>m.DeclaringType!=typeof(object)&&!m.IsSpecialName).ToArray(),o,a));
   throw new ArgumentException("Unknown model operation");
  }
 }
}
