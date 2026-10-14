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
  static readonly object simulationGate=new object();
  static string simulationPhase="not_started";
  static bool? simulationNativeFlag;
  static DateTime? simulationNativeObservedUtc;

  sealed class SimulationWindowManager : RealProxy {
   readonly object original;
   internal SimulationWindowManager(Type contract):base(contract){original=Resolve(contract.FullName);}
   public override IMessage Invoke(IMessage message) {
    var call=(IMethodCallMessage)message;
    try{
     if(call.MethodName=="ShowDialog"&&call.ArgCount>0&&call.Args[0]!=null&&call.Args[0].GetType().FullName=="Flexem.Studio.Build.Views.ClearRecordViewModel"){
      foreach(string flag in new[]{"IsClearRw","IsClearRecipe","IsClearHistoryData","IsClearSpecialReg"})NativeSet(call.Args[0],flag,false);
      lock(simulationGate)simulationRecordsPreserved=true;
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

  static object SimulationState() {
   // This runs on the pipe thread: never read native models or dispatch to UI.
   // Simulate may hold its UI job until the simulator closes. Native flags are
   // explicitly dated observations, not proof of a currently running process.
   lock(simulationGate) {
    var candidates=new List<object>();
    if(simulationProject!=null)foreach(string name in new[]{"FCS","FSGUI"})
     foreach(var process in Process.GetProcessesByName(name))using(process)
      if(!previousSimulatorProcesses.Contains(process.Id))candidates.Add(Obj("pid",process.Id,"name",name));
    return Obj("status",simulationPhase,"project",simulationProject,"native_is_simulating",simulationNativeFlag,
     "native_observed_utc",simulationNativeObservedUtc.HasValue?simulationNativeObservedUtc.Value.ToString("o"):null,
     "online",false,"records_preserved",simulationRecordsPreserved,"launch_error",simulationLaunchError,"started_utc",simulationProject==null?null:simulationStartedUtc.ToString("o"),
     "new_process_candidates",candidates.ToArray(),"process_ownership_verified",false,
     "verification","native_launch_only_not_visual_or_interaction_acceptance");
   }
  }

  static object SimulationOperation(Dictionary<string,object>a) {
   RequireProject();
   if(S(a,"op")!="simulation_start")throw new ArgumentException("Unknown simulation operation");
   var operation=simulationOperation??(simulationOperation=CreateSimulationOperation());
   if((bool)Member(operation,"IsSimulating"))throw new InvalidOperationException("A simulator is already running in this host; inspect simulation_state");
   if(DirtyViewCount()!=0)throw new InvalidOperationException("Save all project views before starting the simulator");
   string file=ProjectFile();if(!File.Exists(file))throw new FileNotFoundException("Save the project first",file);
   NativeSet(operation,"IsOnLine",false);
   // The native progress dialog requires an owner that has actually been shown.
   ShowCanvasWithoutActivation();
   lock(simulationGate) {
    previousSimulatorProcesses=new HashSet<int>();
    foreach(string name in new[]{"FCS","FSGUI"})foreach(var process in Process.GetProcessesByName(name))using(process)previousSimulatorProcesses.Add(process.Id);
    simulationProject=file;simulationStartedUtc=DateTime.UtcNow;
    simulationLaunchError=null;simulationRecordsPreserved=false;
    simulationPhase="native_call_running";
    simulationNativeFlag=(bool)Member(operation,"IsSimulating");simulationNativeObservedUtc=DateTime.UtcNow;
   }
   var contract=Native("Flexem.Studio.Build.Operations.ISimulateOperation");
   var nativeFile=Activator.CreateInstance(Native("Flexem.FileName"),new object[]{file});
   try{contract.GetMethod("Simulate").Invoke(operation,new[]{nativeFile});}
   catch(Exception error){lock(simulationGate){simulationLaunchError=error.ToString();simulationPhase="failed";}throw;}
   finally{lock(simulationGate){simulationNativeFlag=(bool)Member(operation,"IsSimulating");simulationNativeObservedUtc=DateTime.UtcNow;if(simulationPhase!="failed")simulationPhase="native_call_returned";}}
   return SimulationState();
  }
 }
}
