using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FStudioMcp {
 internal static partial class VisibleHost {
  static object ConfigureWordIndicator(Dictionary<string,object>a,object view) {
   var model=Ref(a);
   if(model.GetType().FullName!="Flexem.Studio.Components.Parts.SwitchPart.SwitchInfo")throw new ArgumentException("Expected SwitchInfo");
   var scope=OwningScope(S(a,"ref"));
   if(!Object.ReferenceEquals(scope,view))throw new ArgumentException("Word indicator requires a committed component on the active page");
   var indicator=Member(model,"Indicator");var settings=Member(model,"SwitchSettings");
   if(Member(indicator,"Usage")!=null||((IEnumerable)Member(settings,"ActionList")).Cast<object>().Any())throw new ArgumentException("Existing indicator conditions and actions were preserved");
   double index=Convert.ToDouble(a["address"]);
   if(a["address"]==null||a["address"] is bool||a["address"] is string||Double.IsNaN(index)||Double.IsInfinity(index)||index<0||index>UInt32.MaxValue||index!=Math.Truncate(index))throw new ArgumentException("Invalid local word address");
   var files=((IEnumerable)a["files"]).Cast<object>().Select(x=>Path.GetFullPath(Convert.ToString(x))).ToArray();
   if(files.Length!=3)throw new ArgumentException("Supply two state images and one fallback image");
   foreach(string file in files){Within(file);if(!File.Exists(file)||new FileInfo(file).Length>8*1024*1024)throw new ArgumentException("Invalid state image file");}
   var devices=Member(PageServices(),"DeviceService");
   var usageType=Native("Flexem.Studio.Hmi.Models.Components.Parts.Indicator.IndicatorUsage.IndicatorUsageData");
   var constructor=usageType.GetConstructors().Single(c=>c.GetParameters().Length==1&&c.GetParameters()[0].ParameterType.FullName=="Flexem.Studio.Hmi.Device.IDeviceService");
   var usage=constructor.Invoke(new[]{devices});NativeSet(usage,"DisplayType","RegisterMode");
   var control=Member(usage,"RegisterControl");NativeSet(control,"ControlType","WordRegister");
   var dataType=Enum.Parse(Native("Flexem.Studio.Hmi.DataType"),"UInt16");
   var word=Activator.CreateInstance(Native("Flexem.Studio.Hmi.Models.Components.Parts.Indicator.IndicatorWord.IndicatorWordData"),new[]{devices,dataType});NativeSet(control,"Word",word);
   var address=PageStatic("Flexem.Studio.Hmi.Models.Address.AddressData","CreateWord",devices,dataType,true);
   NativeSet(Member(address,"Raw"),"MainAddress",Convert.ToUInt32(index));
   NativeSet(word,"Address",address);NativeSet(word,"IsSameAsSwitchAddress",false);
   var conditions=Member(word,"Statuses");CallNative(conditions,"Clear");
   for(int i=0;i<2;i++) {
    var status=Activator.CreateInstance(Native("Flexem.Studio.Hmi.Models.Components.Parts.Indicator.WordStatusData"),new object[]{i});
    NativeSet(status,"ConditionType","Range");var condition=Member(status,"WordCondition");
    NativeSet(condition,"ComparatorA","Equal");NativeSet(condition,"Relationship","None");
    NativeSet(Member(condition,"VariableA"),"Constant",i);CallNative(conditions,"Add",status);
   }
   NativeSet(Member(word,"InvalidInput"),"Actions","ShowErrorState");NativeSet(Member(word,"InvalidInput"),"Notify",false);
   string name="mcp_indicator_"+((Guid)Member(model,"UniqueId")).ToString("N");
   string sourceDir=Path.Combine(Path.GetDirectoryName(ProjectFile()),"HMI","McpSources");Within(sourceDir);Directory.CreateDirectory(sourceDir);
   string sourceFile=Path.Combine(sourceDir,name+".fsvg");if(File.Exists(sourceFile))throw new ArgumentException("Indicator graph source already exists; inspect the prior outcome");
   var category=Member(Member(CurrentProject(),"GraphLib"),"CurrentProjectGraphCategory");
   string libraryDir=Convert.ToString(Member(category,"Dir"));Within(libraryDir);
   if(Directory.Exists(libraryDir)&&Directory.EnumerateFiles(libraryDir,"*.fsvg").Any(x=>String.Equals(Path.GetFileNameWithoutExtension(x),name,StringComparison.OrdinalIgnoreCase)))throw new ArgumentException("Indicator graph already exists; inspect the prior outcome");
   var graph=PageStatic("Flexem.Studio.HMI.GraphInfo","CreateAsChild",sourceFile);
   NativeSet(graph,"GraphConfigDir",sourceDir);NativeSet(graph,"GraphCategoryType","CurrentProject");
   NativeSet(graph,"Name",name);NativeSet(graph,"Width",64.0);NativeSet(graph,"Height",64.0);
   var graphStates=Member(graph,"GraphStatuses");CallNative(graphStates,"Clear");
   for(int i=0;i<3;i++) {
    var state=PageStatic("Flexem.Studio.HMI.GraphStatusInfo","CreateAsChild");CallNative(state,"UpdateIndexAndName",i);
    var picture=PageStatic("Flexem.Studio.Components.Graphicses.StaticPictureInfo","CreateAsChildForGraphLib",1);
    var normal=Member(picture,"NormalInfo");CallNative(normal,"LoadFromFile",files[i],File.ReadAllBytes(files[i]));
    var metadata=Member(normal,"PictureMetadata");if(Convert.ToDouble(Member(metadata,"RawWidth"))!=64||Convert.ToDouble(Member(metadata,"RawHeight"))!=64)throw new ArgumentException("Each state image must be 64x64 pixels");
    NativeSet(Member(normal,"AlphaColor"),"IsUsePngAlphaChannel",true);
    NativeSet(Member(normal,"SizeZoom"),"IsFixLatitudeScale",true);NativeSet(Member(normal,"SizeZoom"),"IsUseOriginalSize",true);
    var position=Member(picture,"Position");NativeSet(position,"X",0.0);NativeSet(position,"Y",0.0);NativeSet(position,"Width",64.0);NativeSet(position,"Height",64.0);
    CallNative(Member(state,"Graphicses"),"Add",picture);CallNative(graphStates,"Add",state);
   }
   CallNative(graph,"SaveToFile",sourceFile);
   var library=Member(PageServices(),"ProjectGraphLibraryService");
   RequireBackgroundGraphImport(library,"Import",new[]{graph});var imported=CallNative(library,"Import",graph);
   var group=BeginModelChange(view,"MCP: word indicator and fallback");
   try {
    NativeSet(settings,"UseSwitch",false);NativeSet(indicator,"Usage",usage);NativeSet(indicator,"UseIndicator",true);
    var graphProperty=Member(model,"GraphProperty");CallNative(graphProperty,"ChangeGallery",imported);NativeSet(graphProperty,"UseGraph",true);
    NativeSet(graphProperty,"GraphWidth",64.0);NativeSet(graphProperty,"GraphHeight",64.0);
    var tag=Member(model,"Tag");NativeSet(tag,"UsingType","Tag");NativeSet(tag,"IsDecisionTagOverflow",false);
    var tags=Member(tag,"Statuses");
    while(((IEnumerable)tags).Cast<object>().Count()<3){var dto=DeepCopyPageDto(((IEnumerable)tags).Cast<object>().First());NativeSet(dto,"Id",((IEnumerable)tags).Cast<object>().Count());CallNative(tags,"Add",Activator.CreateInstance(Native("Flexem.Studio.Hmi.Models.Components.Parts.Tag.TagStatusData"),new[]{dto}));}
    if(((IEnumerable)tags).Cast<object>().Count()!=3)throw new NotSupportedException("Expected two tag states plus fallback");
    foreach(var tagState in (IEnumerable)tags)foreach(var language in (IEnumerable)Member(tagState,"Items"))NativeSet(language,"Text","");
    var bounds=Member(model,"Position");NativeSet(bounds,"Width",64.0);NativeSet(bounds,"Height",64.0);
    if(((IEnumerable)Member(graphProperty,"GraphStateInfos")).Cast<object>().Count()!=3)throw new NotSupportedException("Expected three native graph states");
    CallNative(group,"Commit");
   }catch{CallNative(group,"Abort");throw;}
   return Obj("address",index,"device_id",Member(Member(address,"Raw"),"DeviceId"),"register_id",Member(Member(Member(address,"Raw"),"RegisterId"),"Id"),"normal_states",2,"total_states",3,"invalid_input","ShowErrorState","graph_id",Member(imported,"Id"),"source_file",sourceFile,"graph_file",Member(imported,"FileName"),"width",Member(Member(model,"Position"),"Width"),"height",Member(Member(model,"Position"),"Height"),"native_undo_transaction",true,"runtime","not_executed");
  }
 }
}
