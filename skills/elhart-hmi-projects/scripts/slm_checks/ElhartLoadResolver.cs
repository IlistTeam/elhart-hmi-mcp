using System;
using System.Collections.Generic;
using Flexem.Infrastructure;
using Flexem.Studio.Hmi.Project;
// Test-only resource text adapter. All action/address models use native loaders.
public sealed class ElhartLoadStringParser : Flexem.Services.IStringParser {
 public string Parse(string value){return value;}
 public string Parse(string value,Flexem.Services.StringTagPair[] pairs){return value;}
}
public sealed class ElhartLoadProjectService : IHmiProjectService {
 public IHmiProject CurrentProject {get{return null;}}
 public bool IsProjectClosing {get{return false;}}
 public event EventHandler<HmiProjectEventArgs> ProjectLoaded {add{} remove{}}
 public event EventHandler<HmiProjectEventArgs> ProjectClosed {add{} remove{}}
 public bool IsProjectOpened(string a,string b){return false;}
 public void LoadProject(string a,string b){}
}
public sealed class ElhartLoadResolver : IDependencyResolver {
 public void Dispose(){}
 public readonly Dictionary<Type,object> Values=new Dictionary<Type,object>();
 public void Register<T>(T value){Values[typeof(T)]=value;}
 public void Inject<T>(T value){}
 public object Resolve(Type type,string name){
  object value;if(Values.TryGetValue(type,out value))return value;
  throw new InvalidOperationException("Missing test service: "+type.FullName);
 }
 public T Resolve<T>(Type type){return (T)Resolve(type,null);}
 public T Resolve<T>(Type type,string name){return (T)Resolve(type,name);}
 public T Resolve<T>(){return (T)Resolve(typeof(T),null);}
 public T Resolve<T>(string name){return (T)Resolve(typeof(T),name);}
 public T Resolve<T>(object[] args){return Resolve<T>();}
 public T Resolve<T>(KeyValuePair<string,object>[] args){return Resolve<T>();}
 public IEnumerable<T> ResolveAll<T>(){return new T[]{Resolve<T>()};}
 public bool IsRegistered(Type type,string name){return Values.ContainsKey(type);}
 public bool IsRegistered<T>(string name){return IsRegistered(typeof(T),name);}
}
