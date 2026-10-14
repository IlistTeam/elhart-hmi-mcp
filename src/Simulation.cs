using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Remoting.Messaging;
using System.Runtime.Remoting.Proxies;

namespace FStudioMcp {
 internal static partial class VisibleHost {
  static string simulationProject;
  static DateTime simulationStartedUtc;
  static HashSet<int> previousSimulatorProcesses=new HashSet<int>();
  static object simulationOperation;
  static string simulationLaunchError;
  static bool simulationRecordsPreserved;

  sealed class SimulationWindowManager : RealProxy {
   readonly object original;
   internal SimulationWindowManager(Type contract):base(contract){original=Resolve(contract.FullName);}
   public override IMessage Invoke(IMessage message) {
    var call=(IMethodCallMessage)message;
    try{
     if(call.MethodName=="ShowDialog"&&call.ArgCount>0&&call.Args[0]!=null&&call.Args[0].GetType().FullName=="Flexem.Studio.Build.Views.ClearRecordViewModel"){
      foreach(string flag in new[]{"IsClearRw","IsClearRecipe","IsClearHistoryData","IsClearSpecialReg"})NativeSet(call.Args[0],flag,false);
      simulationRecordsPreserved=true;
      return new ReturnMessage((bool?)true,call.Args,call.ArgCount,call.LogicalCallContext,call);
     }
     var result=((MethodInfo)call.MethodBase).Invoke(original,call.Args);
     return new ReturnMessage(result,call.Args,call.ArgCount,call.LogicalCallContext,call);
    }catch(Exception error){return new ReturnMessage(error is TargetInvocationException&&error.InnerException!=null?error.InnerException:error,call);}
   }
  }

  static object CreateSimulationOperation() {
   var contract=Native("Flexem.Studio.Build.Operations.ISimulateOperation");
   // This editor exposes the contract but does not register it in Unity.
   string[] dependencies={"Flexem.Studio.Build.IBuildMessageService","Flexem.Studio.Workbench.IAsynchronousWaitDialog","Flexem.Services.IStringParser","Flexem.Services.IMessageService","Caliburn.Micro.IWindowManager","Flexem.Services.IFileSystem","Flexem.Studio.Hmi.Options.IBuildOptions","Flexem.Infrastructure.IDependencyResolver"};
   var candidates=contract.Assembly.GetTypes().Where(t=>!t.IsAbstract&&!t.IsInterface&&contract.IsAssignableFrom(t)).SelectMany(t=>t.GetConstructors()).Where(c=>c.GetParameters().Select(p=>p.ParameterType.FullName).SequenceEqual(dependencies)).ToArray();
   if(candidates.Length!=1)throw new NotSupportedException("Simulator constructor contract missing or ambiguous");
   return candidates[0].Invoke(dependencies.Select(name=>name=="Caliburn.Micro.IWindowManager"?(object)new SimulationWindowManager(Native(name)).GetTransparentProxy():Resolve(name)).ToArray());
  }

  static object SimulationState(object operation) {
   var processes=Process.GetProcessesByName("FCS");
   return Obj("project",simulationProject,"native_is_simulating",Member(operation,"IsSimulating"),
    "online",Member(operation,"IsOnLine"),"records_preserved",simulationRecordsPreserved,"launch_error",simulationLaunchError,"started_utc",simulationProject==null?null:simulationStartedUtc.ToString("o"),
    "new_process_candidates",simulationProject==null?new int[0]:processes.Where(p=>!previousSimulatorProcesses.Contains(p.Id)).Select(p=>p.Id).ToArray(),
    "verification","native_launch_only_not_visual_or_interaction_acceptance");
  }

  static object SimulationOperation(Dictionary<string,object>a) {
   RequireProject();
   if(S(a,"op")=="simulation_state")return simulationOperation==null?Obj("status","not_started","project",null,"online",false):SimulationState(simulationOperation);
   if(S(a,"op")!="simulation_start")throw new ArgumentException("Unknown simulation operation");
   var operation=simulationOperation??(simulationOperation=CreateSimulationOperation());
   if((bool)Member(operation,"IsSimulating"))throw new InvalidOperationException("A simulator is already running in this host; inspect simulation_state");
   if(DirtyViewCount()!=0)throw new InvalidOperationException("Save all project views before starting the simulator");
   string file=ProjectFile();if(!File.Exists(file))throw new FileNotFoundException("Save the project first",file);
   NativeSet(operation,"IsOnLine",false);
   // The native progress dialog requires an owner that has actually been shown.
   ShowCanvasWithoutActivation();
   previousSimulatorProcesses=new HashSet<int>(Process.GetProcessesByName("FCS").Select(p=>p.Id));
   simulationProject=file;simulationStartedUtc=DateTime.UtcNow;
   var contract=Native("Flexem.Studio.Build.Operations.ISimulateOperation");
   var nativeFile=Activator.CreateInstance(Native("Flexem.FileName"),new object[]{file});
   simulationLaunchError=null;simulationRecordsPreserved=false;
   try{contract.GetMethod("Simulate").Invoke(operation,new[]{nativeFile});}
   catch(Exception error){simulationLaunchError=error.ToString();throw;}
   return SimulationState(operation);
  }
 }
}
