using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

// 假工具只有类型和公开参数。构造器故意抛错，以证明整批输入验证不创建原生对象。
namespace Flexem.Studio.GraphicsDesigner.Tools {
 public sealed class ContractTestTool {
  public ContractTestTool(){throw new InvalidOperationException("Validation must not construct a tool");}
  public bool Enabled {get;set;}
 }
 public sealed class StaticTextTool {
  public StaticTextTool(){throw new InvalidOperationException("Validation must not construct a text tool");}
 }
}
// 编号生成只依赖这三个原生接口的约定。显式更新接口用于防止测试误接受受保护 setter 的直接写入。
namespace Flexem.Studio.GraphicsDesign {
 public interface IComponent {int ComponentId {get;}}
 public interface IUpdatableComponent {void SetComponentId(int id);}
 public interface IGraphicsProvider {IEnumerable Graphics {get;}}
}
public class ContractComponent : Flexem.Studio.GraphicsDesign.IComponent,Flexem.Studio.GraphicsDesign.IUpdatableComponent {
 public int ComponentId {get;private set;} public int Updates {get;private set;}
 public ContractComponent(int id){ComponentId=id;}
 void Flexem.Studio.GraphicsDesign.IUpdatableComponent.SetComponentId(int id){ComponentId=id;Updates++;}
}
public sealed class OtherContractComponent:ContractComponent {public OtherContractComponent(int id):base(id){}}
public sealed class ContractProvider:Flexem.Studio.GraphicsDesign.IGraphicsProvider {public IEnumerable Graphics {get;set;}}
public sealed class ContractServices {
 public object Provider;
 public object GetService(Type type){if(type==typeof(Flexem.Studio.GraphicsDesign.IGraphicsProvider))return Provider;throw new ArgumentException("Unexpected service");}
}
public sealed class ContractContext {public ContractServices Services {get;set;}}

public sealed class ContractPosition {
 public double X {get;set;} public double Y {get;set;} public double Width {get;set;} public double Height {get;set;}
}
public sealed class ContractGraphic {
 public string Comment {get;set;} public Guid UniqueId {get;set;} public ContractPosition Position {get;set;}
}
public sealed class ContractPage {
 public Guid UniqueId {get;set;} public string Name {get;set;} public string Type {get;set;} public int Id {get;set;}
 public int Width {get;set;} public int Height {get;set;} public string ConfigFileName {get;set;}
 public List<ContractGraphic> Graphicses {get;set;}
}
public sealed class ContractView {public bool IsDirty {get;set;}}

// 模拟两种原生工厂重载；接口重载故意拒绝，以验证宿主精确选择 FontInfo 类型。
public interface IContractFont {}
public sealed class ContractFont:IContractFont {
 public string FontType {get;set;} public double FontSize {get;set;} public string FontColor {get;set;}
 public string FontName {get;set;} public bool IsBold {get;set;}
 public ContractFont Clone(){return (ContractFont)MemberwiseClone();}
}
public sealed class ContractLanguage {public int SN {get;set;} public ContractFont FontInfo {get;set;}}
public sealed class ContractFontStyle {public bool UseFontStyle {get;set;} public ContractFont Font {get;set;}}
namespace Flexem.Studio.Components.Graphicses {
 public sealed class StaticTextContentInfo {
  public int LanguageSN {get;set;} public string LabelContent {get;set;} public string UsingType {get;set;}
  public ContractFontStyle FontStyleInfo {get;set;}
  public static StaticTextContentInfo CreateAsChild(int languageId,IContractFont font){throw new Exception("An exact native font overload is required");}
  public static StaticTextContentInfo CreateAsChild(int languageId,ContractFont font){return new StaticTextContentInfo{LanguageSN=languageId,FontStyleInfo=new ContractFontStyle{UseFontStyle=true,Font=font.Clone()}};}
 }
}
public sealed class ContractTextNormal {
 public string UsingType {get;set;} public int LanguageSN {get;set;}
 public List<Flexem.Studio.Components.Graphicses.StaticTextContentInfo> StaticTextContentInfos {get;set;}
}

/// <summary>直接调用已编译宿主的真实验证和快照函数；使用内存页，不启动 FStudio、不读写工程。</summary>
public static class DrawingContractsHarness {
 static Type Host;
 static int Passed;
 static Dictionary<string,object> D(params object[] pairs){var d=new Dictionary<string,object>();for(int i=0;i<pairs.Length;i+=2)d[(string)pairs[i]]=pairs[i+1];return d;}
 static object Invoke(string name,params object[] args){
  try{return Host.GetMethod(name,BindingFlags.Static|BindingFlags.NonPublic|BindingFlags.Public).Invoke(null,args);}
  catch(TargetInvocationException e){throw e.InnerException;}
 }
 static void Check(bool condition,string message){if(!condition)throw new Exception(message);Passed++;}
 static void Reject(Action run,string message){try{run();}catch(ArgumentException){Passed++;return;}catch(InvalidOperationException){Passed++;return;}catch(NotSupportedException){Passed++;return;}throw new Exception("Expected refusal: "+message);}
 static Dictionary<string,object> Widget(string name){return D("name",name,"tool","Flexem.Studio.GraphicsDesigner.Tools.ContractTestTool","x",12,"y",24,"width",90,"height",42);}
 static ContractGraphic Graphic(string name,int x){return new ContractGraphic{Comment=name,UniqueId=Guid.NewGuid(),Position=new ContractPosition{X=x,Y=20,Width=80,Height=30}};}
 static object[] Items(object value){return ((IEnumerable)value).Cast<object>().ToArray();}
 static Dictionary<string,object> Snapshot(Dictionary<string,object> request,ContractView view,ContractPage page){return (Dictionary<string,object>)Invoke("DrawingSnapshot",request,view,page);}
 static void TestComponentIds(){
  var widgetType=Host.GetNestedType("BatchWidget",BindingFlags.NonPublic);var draftType=Host.GetNestedType("DrawingDraft",BindingFlags.NonPublic);
  var widgets=(IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(widgetType));
  var first=new ContractComponent(2);var second=new ContractComponent(2);var other=new OtherContractComponent(2);
  foreach(var model in new ContractComponent[]{first,second,other}){
   var draft=Activator.CreateInstance(draftType,true);draftType.GetField("Model").SetValue(draft,model);
   var widget=Activator.CreateInstance(widgetType,true);widgetType.GetField("Draft",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(widget,draft);widgets.Add(widget);
  }
  var context=new ContractContext{Services=new ContractServices{Provider=new ContractProvider{Graphics=new object[]{new ContractComponent(1),new ContractComponent(3),new ContractComponent(7)}}}};
  Invoke("AllocateBatchComponentIds",widgets,context);
  Check(first.ComponentId==2&&second.ComponentId==4,"Batch ids must skip both existing ids and earlier draft ids");
  Check(other.ComponentId==2,"Native id allocation groups by exact model type");
  Check(first.Updates==0&&second.Updates==1&&other.Updates==0,"Only collisions should call the explicit native update interface");
 }
 static void TestTextLanguages(){
  var languages=new[]{new ContractLanguage{SN=1,FontInfo=new ContractFont{FontType="Vector",FontName="语言一"}},new ContractLanguage{SN=2,FontInfo=new ContractFont{FontType="Vector",FontName="语言二"}}};
  var existing=Flexem.Studio.Components.Graphicses.StaticTextContentInfo.CreateAsChild(1,languages[0].FontInfo);
  var retained=Flexem.Studio.Components.Graphicses.StaticTextContentInfo.CreateAsChild(3,languages[0].FontInfo);
  var normal=new ContractTextNormal{UsingType="Label",StaticTextContentInfos=new List<Flexem.Studio.Components.Graphicses.StaticTextContentInfo>{existing,retained}};
  var request=D("text","目标值","text_style",D("font_size",18,"color","#FFE0EDF8","bold",true,"font_name","Microsoft YaHei"));
  Invoke("ConfigureBatchTextLanguages",normal,languages.Cast<object>().ToArray(),2,request);
  Check(normal.StaticTextContentInfos.Count==3&&normal.StaticTextContentInfos.Any(x=>x.LanguageSN==2),"Every configured project language must receive a native text item");
  Check(Object.ReferenceEquals(normal.StaticTextContentInfos[0],existing)&&Object.ReferenceEquals(normal.StaticTextContentInfos[1],retained),"Previously initialized native language items must be retained");
  Check(normal.LanguageSN==2&&normal.StaticTextContentInfos.All(x=>x.LabelContent=="目标值"&&x.UsingType=="Label"),"Text must apply to every initialized language while preview follows the active language");
  Check(normal.StaticTextContentInfos.All(x=>!x.FontStyleInfo.UseFontStyle&&x.FontStyleInfo.Font.FontType=="Graphic"),"Explicit text styles must retain the published Graphic font behavior for every language");
  Check(normal.StaticTextContentInfos.All(x=>x.FontStyleInfo.Font.FontSize==18&&x.FontStyleInfo.Font.FontColor=="#FFE0EDF8"&&x.FontStyleInfo.Font.IsBold&&x.FontStyleInfo.Font.FontName=="Microsoft YaHei"),"Every initialized language must receive the requested native font style");
  Check(languages.All(x=>x.FontInfo.FontType=="Vector")&&!Object.ReferenceEquals(normal.StaticTextContentInfos.Last().FontStyleInfo.Font,languages[1].FontInfo),"Text configuration must not mutate or share project language fonts");
  Reject(()=>Invoke("ConfigureBatchTextLanguages",normal,languages.Cast<object>().ToArray(),4,request),"Unconfigured active language");
  normal.UsingType="TextLib";Reject(()=>Invoke("ConfigureBatchTextLanguages",normal,languages.Cast<object>().ToArray(),2,request),"Text properties cannot configure a text-library mode");
 }

 public static int Main(string[] args){
  try{
   if(args.Length!=2)throw new ArgumentException("Usage: DrawingContractsHarness.exe <host.dll> <workspace>");
   Host=Assembly.LoadFrom(Path.GetFullPath(args[0])).GetType("FStudioMcp.VisibleHost",true);
   string root=Path.GetFullPath(args[1]).TrimEnd('\\')+"\\";
   Host.GetField("workspace",BindingFlags.Static|BindingFlags.NonPublic).SetValue(null,root);
   var serializer=Invoke("Json");
   var largePayload="{\"data\":\""+new String('A',12*1024*1024)+"\"}";
   var decoded=(IDictionary)serializer.GetType().GetMethod("DeserializeObject").Invoke(serializer,new object[]{largePayload});
   Check(((string)decoded["data"]).Length==12*1024*1024,"Pipe JSON must fit base64 of an allowed 8 MiB image plus metadata");
   // No dispatcher or native project exists in this harness. A status read
   // must complete directly even when a native UI launch cannot complete.
   var simulation=(Dictionary<string,object>)Invoke("Handle",D("op","simulation_state"));
   Check(Convert.ToString(simulation["status"])=="not_started"&&!simulation.ContainsKey("job_id"),"Simulation status must not queue an unbounded UI job");
   Host.GetField("simulationPhase",BindingFlags.Static|BindingFlags.NonPublic).SetValue(null,"native_call_running");
   simulation=(Dictionary<string,object>)Invoke("Handle",D("op","simulation_state"));
   Check(Convert.ToString(simulation["status"])=="native_call_running"&&simulation["native_is_simulating"]==null,"A pending native call must be observable without native getters");
   Check(!(bool)simulation["online"]&&!(bool)simulation["process_ownership_verified"],"Status must preserve offline mode and avoid claiming process ownership");
   Host.GetField("simulationPhase",BindingFlags.Static|BindingFlags.NonPublic).SetValue(null,"not_started");
   var buildRunType=Host.GetNestedType("BackgroundBuildRun",BindingFlags.NonPublic);
   foreach(bool compilerResult in new[]{true,false}){
    var run=Activator.CreateInstance(buildRunType,true);
    buildRunType.GetField("NativeResult",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(run,compilerResult);
    Invoke("FinalizeBackgroundBuild",run,null,false);
    Check(Convert.ToString(buildRunType.GetField("State",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(run))==(compilerResult?"completed":"failed"),"Task completion must preserve actual compiler success/failure");
   }
   var pendingBuild=Activator.CreateInstance(buildRunType,true);
   buildRunType.GetField("Id",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(pendingBuild,"contract-running");
   buildRunType.GetField("Project",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(pendingBuild,Path.Combine(root,"contract.fsprj"));
   buildRunType.GetField("State",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(pendingBuild,"running");
   ((IDictionary)Host.GetField("buildRuns",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null)).Add("contract-running",pendingBuild);
   Host.GetField("activeBuild",BindingFlags.Static|BindingFlags.NonPublic).SetValue(null,pendingBuild);
   var buildStatus=(Dictionary<string,object>)Invoke("Handle",D("op","build_status","build_id","contract-running"));
   Check(Convert.ToString(buildStatus["status"])=="running"&&!buildStatus.ContainsKey("job_id"),"Build status must not wait for a busy UI dispatcher");
   var cancelled=(Dictionary<string,object>)Invoke("Handle",D("op","build_cancel","build_id","contract-running"));
   Check((bool)cancelled["cancel_requested"],"Cancellation must be acknowledged while the dispatcher is unavailable");
   Invoke("FinalizeBackgroundBuild",pendingBuild,null,true);
   Check(Convert.ToString(buildRunType.GetField("State",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(pendingBuild))=="cancelled","Cancellation must retain its terminal state");
   TestComponentIds();
   TestTextLanguages();
   var page=new ContractPage{UniqueId=Guid.NewGuid(),Name="测试页",Type="Basic",Id=1,Width=800,Height=480,ConfigFileName=Path.Combine(root,"contract-page.xml"),Graphicses=new List<ContractGraphic>()};
   var view=new ContractView();
   page.Graphicses.AddRange(new[]{Graphic("same",1),Graphic("middle",2),Graphic("same",3),Graphic("last",4)});
   var first=Snapshot(D("limit",2),view,page);
   Check(Convert.ToInt32(first["total_count"])==4&&Items(first["items"]).Length==2,"First page must contain two of four objects");
   Check(Convert.ToString(first["scope"])=="active_page_top_level_only","Scope must match the installed layout checker contract");
   Check(Convert.ToInt32(first["next_offset"])==2&&!(bool)first["complete"],"First page must retain a continuation");
   var duplicateRows=Items(first["duplicate_names"]).Cast<Dictionary<string,object>>().ToArray();
   Check(duplicateRows.Length==1&&Convert.ToString(duplicateRows[0]["name"])=="same"&&Convert.ToInt32(duplicateRows[0]["count"])==2&&Convert.ToInt32(first["duplicate_name_count"])==1,"A duplicate on another page must retain the published name/count structure and total");
   var second=Snapshot(D("offset",2,"limit",2),view,page);
   Check(second["next_offset"]==null&&(bool)second["complete"],"Last page must complete");
   var rows=Items(second["items"]).Cast<Dictionary<string,object>>().ToArray();
   Check(Convert.ToInt32(rows[0]["index"])==2&&Convert.ToInt32(rows[1]["index"])==3,"Indexes must remain original collection indexes");
   var filtered=Snapshot(D("names",new object[]{"same","missing"},"limit",1),view,page);
   Check(Convert.ToInt32(filtered["matched_count"])==2&&Items(filtered["missing_names"]).SequenceEqual(new object[]{"missing"}),"Filtered identity diagnostics must use all matches");
   Check((bool)filtered["filtered"]&&!(bool)filtered["includes_inherited"]&&!(bool)filtered["expands_groups"],"Scope limitations must be explicit");
   Check(!(bool)filtered["common_composition_included"]&&!(bool)filtered["nested_children_included"],"Published common and nested composition flags must remain available");
   // 只读名称查询需覆盖旧工程已有的长名称和换行，不应继承新建控件的命名约束。
   page.Graphicses[1].Comment=new String('x',512);
   Check(Convert.ToInt32(Snapshot(D("names",new object[]{page.Graphicses[1].Comment}),view,page)["matched_count"])==1,"Snapshot must accept the published 512-character name limit");
   Reject(()=>Snapshot(D("names",new object[]{new String('x',513)}),view,page),"Snapshot name exceeds 512 characters");
   page.Graphicses[1].Comment=" \r\n ";
   Check(Convert.ToInt32(Snapshot(D("names",new object[]{page.Graphicses[1].Comment}),view,page)["matched_count"])==1,"Snapshot must match existing whitespace and control-character names exactly");
   page.Graphicses[1].Comment="middle";
   var crowded=new ContractPage{UniqueId=Guid.NewGuid(),Name="重复名称页",Type="Basic",Id=2,Width=800,Height=480,Graphicses=Enumerable.Range(0,101).SelectMany(i=>new[]{Graphic("dup"+i,1),Graphic("dup"+i,2)}).ToList()};
   var duplicateLimit=Snapshot(D("limit",1),view,crowded);
   Check(Convert.ToInt32(duplicateLimit["duplicate_name_count"])==101&&Items(duplicateLimit["duplicate_names"]).Length==100,"Duplicate diagnostics must retain the full count while bounding the returned list");
   var model=(Dictionary<string,object>)rows[0]["model"];
   var reference=Invoke("Ref",D("ref",model["$ref"]));
   Check(Object.ReferenceEquals(reference,page.Graphicses[2]),"Returned handle must resolve to the exact object");
   Invoke("ClearScopeHandles",view);
   Reject(()=>Invoke("Ref",D("ref",model["$ref"])),"Scope cleanup must invalidate handles");
   Reject(()=>Snapshot(D("page_uuid",Guid.NewGuid().ToString()),view,page),"Wrong active page");
   Reject(()=>Snapshot(D("offset",0.5),view,page),"Fractional offset");
   Reject(()=>Snapshot(D("limit",true),view,page),"Boolean limit");
   Reject(()=>Snapshot(D("names",new object[]{"same","same"}),view,page),"Repeated filter names");
   page.Id=29000;Check(Convert.ToInt32(Snapshot(D(),view,page)["total_count"])==4,"Reserved pages remain readable");
   Reject(()=>Invoke("DrawingCreateMany",D(),view,page,null),"Reserved page creation");page.Id=1;
   page.Type="Common";Check(Convert.ToInt32(Snapshot(D(),view,page)["total_count"])==4,"Common pages remain readable");
   Reject(()=>Invoke("DrawingCreateMany",D(),view,page,null),"Common page creation");page.Type="Basic";
   var good=Widget("new");
   var batch=Invoke("ValidateBatchSpecifications",D("widgets",new object[]{good}),page);
   Check(Items(batch).Length==1,"Valid declaration should succeed without constructing tool");
   Reject(()=>Invoke("ValidateBatchSpecifications",D("widgets",new object[]{Widget("same")}),page),"Existing name");
   Reject(()=>Invoke("ValidateBatchSpecifications",D("widgets",new object[]{Widget("new"),Widget("new")}),page),"Duplicate batch name");
   foreach(object width in new object[]{0,-1,0.5,16385,Double.NaN,Double.PositiveInfinity,true,"20"}){
    var bad=Widget("bad");bad["width"]=width;Reject(()=>Invoke("ValidateBatchSpecifications",D("widgets",new object[]{bad}),page),"Invalid width");
   }
   var forbidden=Widget("bad");forbidden["properties"]=new object[]{D("member","Position.Width","value",100)};
   Reject(()=>Invoke("ValidateBatchSpecifications",D("widgets",new object[]{forbidden}),page),"Hidden geometry override");
   forbidden["properties"]=new object[]{D("member","Parent.Comment","value","changed")};
   Reject(()=>Invoke("ValidateBatchSpecifications",D("widgets",new object[]{forbidden}),page),"Parent traversal");
   forbidden["properties"]=new object[]{D("member","IsReferenced","value",false)};
   Reject(()=>Invoke("ValidateBatchSpecifications",D("widgets",new object[]{forbidden}),page),"Reference lifecycle override");
   forbidden["properties"]=new object[]{D("member","Settings.Font","value",D("$ref","shared"))};
   Reject(()=>Invoke("ValidateBatchSpecifications",D("widgets",new object[]{forbidden}),page),"Shared reference assignment");
   var addresses=Widget("bad");addresses["local_addresses"]=new object[]{D("member","Settings.Address","address",-1)};
   Reject(()=>Invoke("ValidateBatchSpecifications",D("widgets",new object[]{addresses}),page),"Unsigned address");
   var text=Widget("text");text["tool"]="Flexem.Studio.GraphicsDesigner.Tools.StaticTextTool";text["text"]="目标值";text["text_style"]=D("font_size",18,"color","#FFE0EDF8","bold",true,"font_name","Microsoft YaHei");
   Check(Items(Invoke("ValidateBatchSpecifications",D("widgets",new object[]{text}),page)).Length==1,"Native text style contract");
   text["text_style"]=D("color","blue");Reject(()=>Invoke("ValidateBatchSpecifications",D("widgets",new object[]{text}),page),"Exact ARGB color");
   text["text_style"]=D("alignment","Center");Reject(()=>Invoke("ValidateBatchSpecifications",D("widgets",new object[]{text}),page),"Unsupported style");
   var large=Enumerable.Range(0,21).Select(i=>{var w=Widget("many"+i);w["properties"]=Enumerable.Range(0,60).Select(j=>(object)D("member","Setting"+j,"value",j)).ToArray();return (object)w;}).ToArray();
   Reject(()=>Invoke("ValidateBatchSpecifications",D("widgets",large),page),"Expanded property budget");
   Check(page.Graphicses.Count==4&&!view.IsDirty,"Read/validation checks must leave the memory canvas unchanged");
   Console.WriteLine("Drawing contract checks passed: "+Passed);return 0;
  }catch(Exception error){Console.Error.WriteLine(error);return 1;}
 }
}
