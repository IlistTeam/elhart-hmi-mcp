using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace FStudioMcp {
 internal static partial class VisibleHost {
  static volatile object nativeContracts;
  static MethodInfo RequireContractMethod(Type type,string name,bool isStatic,Type returns,params string[] parameters){
   var matches=type.GetMethods(BindingFlags.Public|BindingFlags.Static|BindingFlags.Instance|BindingFlags.FlattenHierarchy)
    .Where(m=>m.Name==name&&m.IsStatic==isStatic&&!m.ContainsGenericParameters&&m.ReturnType==returns&&m.GetParameters().Select(p=>p.ParameterType.FullName).SequenceEqual(parameters)).ToArray();
   if(matches.Length!=1)throw new NotSupportedException("Native contract missing or ambiguous: "+type.FullName+"."+name+"("+String.Join(",",parameters)+") -> "+returns.FullName);
   return matches[0];
  }
  static object ProbeNativeContracts(){
   dispatcher.VerifyAccess();var checkedMethods=new List<object>();var errors=new List<string>();
   Action<string,string,bool,Type,string[]> check=(type,name,isStatic,returns,parameters)=>{
    try{var method=RequireContractMethod(Native(type),name,isStatic,returns,parameters);checkedMethods.Add(Obj("type",type,"method",method.ToString(),"assembly",method.DeclaringType.Assembly.GetName().Name));}
    catch(Exception error){errors.Add(error.Message);}
   };
   const string project="Flexem.Studio.Project.FsProjectService";
   check(project,"OpenProjectWithPath",true,typeof(void),new[]{"System.String"});
   check(project,"SaveCurrentProject",true,typeof(void),new string[0]);
   check(project,"CanCloseCurrentProject",true,typeof(bool),new string[0]);
   check(project,"CloseCurrentProject",true,typeof(bool),new[]{"System.Boolean"});
   check("Flexem.Studio.HMIWizard.HMIProjectCreator","Create",false,typeof(string),new[]{"System.String","System.String","Flexem.Studio.HMIWizard.HMIProjectInfo","Flexem.Studio.HMIWizard.ConfigStepEnum"});
   check("Flexem.Studio.Components.Graphicses.StaticPictureNormalInfo","LoadFromFile",false,typeof(void),new[]{"System.String","System.Byte[]"});
   check("Flexem.Studio.HMI.GraphInfo","CreateAsChild",true,Native("Flexem.Studio.HMI.GraphInfo"),new[]{"System.String"});
   check("Flexem.Studio.HMI.GraphInfo","SaveToFile",false,typeof(void),new[]{"System.String"});
   check("Flexem.Studio.Hmi.Models.Address.AddressData","CreateBit",true,Native("Flexem.Studio.Hmi.Models.Address.AddressData"),new[]{"Flexem.Studio.Hmi.Device.IDeviceService"});
   check("Flexem.Studio.Hmi.Models.Address.AddressData","CreateWord",true,Native("Flexem.Studio.Hmi.Models.Address.AddressData"),new[]{"Flexem.Studio.Hmi.Device.IDeviceService","Flexem.Studio.Hmi.DataType","System.Boolean"});
   check("Flexem.Studio.Build.Operations.IBuildOperation","BuildAsync",false,typeof(Task<bool>),new[]{"Flexem.FileName","Flexem.Studio.Asynchronous.IProgressMonitor",typeof(Action<>).MakeGenericType(Native("Flexem.Studio.Asynchronous.Error")).FullName});
   try{
    var compilerTypes=Native("Flexem.Studio.Build.Operations.IBuildOperation").Assembly.GetTypes().Where(t=>NativeCompilerConstructor(t)!=null).ToArray();
    if(compilerTypes.Length!=1)throw new NotSupportedException("Compiler implementation contract has "+compilerTypes.Length+" matches");
    checkedMethods.Add(Obj("type",compilerTypes[0].FullName,"constructor",NativeCompilerConstructor(compilerTypes[0]).ToString(),"assembly","Flexem.Studio.Build"));
   }catch(Exception error){errors.Add(error.Message);}
   nativeContracts=Obj("compatible",errors.Count==0,"checked",checkedMethods.ToArray(),"errors",errors.ToArray(),"verification","reflection_contracts_only_not_behavior_or_ui","captured_utc",DateTime.UtcNow.ToString("o"));
   return nativeContracts;
  }
 }
}
