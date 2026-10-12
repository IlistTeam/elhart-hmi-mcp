// 本文件只提供 LiveModels.cs 的外部依赖桩；编译和执行均不加载原生 FStudio 程序。
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;

namespace System.Windows {
 public class DependencyObject {}
 public class Window:DependencyObject {public string Title;public object DataContext;public bool IsVisible;public object WindowState;}
 public class Application {public static Application Current;public IEnumerable Windows;}
}
namespace ICSharpCode.Core {public interface ICommand {}}
namespace Flexem.Studio.Components.Graphicses {
 // 与真实崩溃黑名单完全相同的类型/成员名；测试要求 getter 和 setter 都不能被执行。
 public class EllipseInfo {
  public static int GetterCalls,SetterCalls;
  public object Normal {get{GetterCalls++;throw new Exception("dangerous getter executed");}set{SetterCalls++;}}
 }
}
namespace FStudioMcp {
 internal static partial class VisibleHost {
  sealed class DrawingDraft {public object CommittedView;}
  static readonly Dictionary<string,DrawingDraft> drawingDrafts=new Dictionary<string,DrawingDraft>();
  const string ExecutionMode="test";
  static void RequireProject(){}
  static object CurrentProject(){return null;}
  static string ProjectFile(){return "test.fsprj";}
  static Type Native(string name){throw new NotSupportedException(name);}
  static Dictionary<string,object> Obj(params object[] values){var result=new Dictionary<string,object>();for(int i=0;i<values.Length;i+=2)result[(string)values[i]]=values[i+1];return result;}
  static string S(Dictionary<string,object> values,string name){object value;return values.TryGetValue(name,out value)?Convert.ToString(value):"";}
  static object CallNative(object target,string method,params object[] arguments){return target.GetType().GetMethod(method).Invoke(target,arguments);}

  sealed class Position {public int Width {get;set;}public int Height {get;set;}}
  struct ValuePosition {public int Width {get;set;}}
  sealed class Graphic {
   public Position Position {get;set;}
   public Position Alias {get;set;}
   public object Nested {get;set;}
   public ValuePosition ValuePosition {get;set;}
   public IList Items {get;set;}
   public int WriteOnly {private get;set;}
   public int ReadOnly {get{return 3;}}
   public override bool Equals(object other){return other is Graphic;}
   public override int GetHashCode(){return 7;}
  }
  sealed class UiModel:System.Windows.DependencyObject {public object Child {get;set;}public int Width {get;set;}}
  sealed class UnstableSetter {
   int value;
   public int Value {get{return value;}set{this.value=value;if(value==13)throw new InvalidOperationException("test setter failure");}}
  }
  // 模拟模式 setter 重建子模型；回滚模式时也会创建新实例，不能依赖旧子对象的句柄继续写入。
  sealed class ModeGraphic {
   int mode;
   public Position Position {get;private set;}
   public ModeGraphic(){Position=new Position{Width=3};}
   public int Mode {get{return mode;}set{mode=value;Position=new Position{Width=value==0?3:30};}}
  }
  sealed class RootItem {
   public int Groups;
   public Transaction Latest;
   public Transaction OpenGroup(string name){Groups++;return Latest=new Transaction();}
  }
  sealed class Transaction {public int Commits,Aborts;public void Commit(){Commits++;}public void Abort(){Aborts++;}}
  sealed class DesignContext {public RootItem RootItem {get;set;}}
  sealed class PageView {public DesignContext DesignContext {get;set;}}
  // 索引读取不应遍历集合；该桩用抛异常的迭代器保证优化没有退回 O(n) 路径。
  sealed class IndexOnlyList:ArrayList {public override IEnumerator GetEnumerator(){throw new InvalidOperationException("Enumeration is forbidden");}}

  static string Handle(object value,object scope){return S((Dictionary<string,object>)DescribeOwned(value,scope),"$ref");}
  static Graphic NewGraphic(int width){return new Graphic{Position=new Position{Width=width}};}
  static Dictionary<string,object> Read(string id,string member){return ReadModelMember(Obj("ref",id,"member",member));}
  static Dictionary<string,object> Edit(string id,string member,object value){return Obj("ref",id,"member",member,"value",value);}
  static void Check(bool condition,string message){if(!condition)throw new Exception(message);}
  static void Fails(Action action,string expected){
   try{action();}catch(Exception error){Check(error.ToString().IndexOf(expected,StringComparison.OrdinalIgnoreCase)>=0,"Wrong failure: "+error);return;}
   throw new Exception("Expected failure containing: "+expected);
  }
  static void Run(string name,Action test){ClearRefs();test();Console.WriteLine("PASS "+name);}
  public static int Main(){
   try{
    Run("point path reads current child",delegate{
     var graphic=NewGraphic(12);var id=Handle(graphic,new DrawingDraft());
     Check((int)Read(id,"Position.Width")["value"]==12,"initial width");
     graphic.Position=new Position{Width=44};
     Check((int)Read(id,"Position.Width")["value"]==44,"dynamic child must not be cached");
     Check(refs.Count==1,"point paths must avoid intermediate handles");
    });
    Run("read many preserves order and page ownership",delegate{
     var scope=new DrawingDraft();var graphic=NewGraphic(8);graphic.Items=new ArrayList{new Position{Width=1},new Position{Width=2},new Position{Width=3}};
     var id=Handle(graphic,scope);
     var result=(Dictionary<string,object>)ModelOperation(Obj("op","read_members","reads",new object[]{Obj("ref",id,"member","Position.Width"),Obj("ref",id,"member","Items","offset",1)}));
     var results=(object[])result["results"];Check(results.Length==2,"read count");
     Check((int)((Dictionary<string,object>)results[0])["value"]==8,"read order");
     var items=(object[])((Dictionary<string,object>)results[1])["items"];Check(items.Length==2,"offset count");
     Check(Object.ReferenceEquals(OwningScope(S((Dictionary<string,object>)items[0],"$ref")),scope),"item scope");
    });
    Run("known collection reads stay lazy unless traversal is explicit",delegate{
     // 迭代器会抛错，因此此处能成功返回句柄便证明默认批量属性读取没有枚举集合。
     var scope=new DrawingDraft();var graphic=NewGraphic(8);graphic.Items=new IndexOnlyList{4,5,6};var id=Handle(graphic,scope);
     var result=(Dictionary<string,object>)ReadModelMembers(Obj("reads",new object[]{Obj("ref",id,"member","Items"),Obj("ref",id,"member","Items","index",1)}));
     var rows=((object[])result["results"]).Cast<Dictionary<string,object>>().ToArray();
     Check(!rows[0].ContainsKey("items")&&!rows[0].ContainsKey("offset"),"known collection was expanded implicitly");
     var collection=(Dictionary<string,object>)rows[0]["value"];
     Check((bool)collection["enumerable"]&&Object.ReferenceEquals(Ref(Obj("ref",collection["$ref"])),graphic.Items),"collection handle changed identity");
     Check((int)rows[1]["value"]==5,"explicit batch indexing lost direct list access");
     graphic.Items=new ArrayList{4,5,6};var listId=Handle(graphic.Items,scope);
     result=(Dictionary<string,object>)ReadModelMembers(Obj("reads",new object[]{Obj("ref",id,"member","Items","offset",1),Obj("ref",listId)}));
     rows=((object[])result["results"]).Cast<Dictionary<string,object>>().ToArray();
     Check(((object[])rows[0]["items"]).Length==2&&((object[])rows[1]["items"]).Length==3,"explicit paging or collection handle did not expand");
     Check(((object[])ReadModelMember(Obj("ref",id,"member","Items"))["items"]).Length==3,"single-property collection behavior changed");
    });
    Run("handles use reference identity",delegate{
     var a=NewGraphic(1);var b=NewGraphic(1);Check(a.Equals(b),"test equality assumption");
     string aid=Handle(a,null),bid=Handle(b,null);Check(aid!=bid,"equal objects must have different handles");
     Check(Handle(a,null)==aid,"same object must reuse its handle");
    });
    Run("clear invalidates handles and reverse cache",delegate{
     var graphic=NewGraphic(4);string id=Handle(graphic,null);Read(id,"Position.Width");ClearRefs();
     Check(refs.Count==0&&reverseRefs.Count==0&&modelProperties.Count==0,"all lookup caches cleared");
     Fails(delegate{Read(id,"Position.Width");},"expired");Check(Handle(graphic,null)!=id,"new session handle");
    });
    Run("single setter accepts point paths",delegate{
     var graphic=NewGraphic(2);var id=Handle(graphic,null);
     ModelOperation(Obj("op","set_member","ref",id,"member","Position.Width","value",20));Check(graphic.Position.Width==20,"single path setter");
    });
    Run("page edits share one native transaction",delegate{
     var root=new RootItem();var page=new PageView{DesignContext=new DesignContext{RootItem=root}};
     var graphic=NewGraphic(3);var id=Handle(graphic,page);
     var result=(Dictionary<string,object>)SetModelProperties(Obj("changes",new object[]{Edit(id,"Position.Width",30),Edit(id,"Position.Height",15)}));
     Check(graphic.Position.Width==30&&graphic.Position.Height==15,"page changes");
     Check(root.Groups==1&&root.Latest.Commits==1&&root.Latest.Aborts==0,"transaction counts");Check((bool)result["native_undo_transaction"],"transaction flag");
    });
    Run("alias handle duplicates rejected before writes",delegate{
     var scope=new DrawingDraft();var graphic=NewGraphic(3);var id=Handle(graphic,scope);var child=Handle(graphic.Position,scope);
     Fails(delegate{SetModelProperties(Obj("changes",new object[]{Edit(id,"Position.Width",10),Edit(child,"Width",20)}));},"Duplicate resolved");Check(graphic.Position.Width==3,"duplicate batch mutated");
    });
    Run("alias paths rejected before writes",delegate{
     var graphic=NewGraphic(3);graphic.Alias=graphic.Position;var id=Handle(graphic,new DrawingDraft());
     Fails(delegate{SetModelProperties(Obj("changes",new object[]{Edit(id,"Position.Width",10),Edit(id,"Alias.Width",20)}));},"Duplicate resolved");Check(graphic.Position.Width==3,"alias batch mutated");
    });
    Run("cross page batches rejected",delegate{
     var a=NewGraphic(1);var b=NewGraphic(2);var aid=Handle(a,new DrawingDraft());var bid=Handle(b,new DrawingDraft());
     Fails(delegate{SetModelProperties(Obj("changes",new object[]{Edit(aid,"Position.Width",4),Edit(bid,"Position.Width",5)}));},"same page or draft");Check(a.Position.Width==1&&b.Position.Width==2,"cross-page batch mutated");
    });
    Run("known cross page paths rejected",delegate{
     var graphic=NewGraphic(2);var id=Handle(graphic,new DrawingDraft());Handle(graphic.Position,new DrawingDraft());
     Fails(delegate{SetModelProperties(Obj("changes",new object[]{Edit(id,"Position.Width",4)}));},"crosses page or draft");Check(graphic.Position.Width==2,"cross-page path mutated");
    });
    Run("UI root intermediate and final targets protected",delegate{
     var graphic=NewGraphic(2);var ui=new UiModel{Child=graphic};var scope=new DrawingDraft();var uiId=Handle(ui,scope);var id=Handle(graphic,scope);graphic.Nested=ui;
     Fails(delegate{SetModelProperties(Obj("changes",new object[]{Edit(uiId,"Child.Position.Width",9)}));},"BACKGROUND_ONLY");
     Fails(delegate{SetModelProperties(Obj("changes",new object[]{Edit(id,"Nested.Child.Position.Width",9)}));},"BACKGROUND_ONLY");
     Fails(delegate{SetModelProperties(Obj("changes",new object[]{Edit(id,"Nested.Width",9)}));},"BACKGROUND_ONLY");Check(graphic.Position.Width==2&&ui.Width==0,"UI write bypass");
    });
    Run("dangerous getters refused on reads and writes",delegate{
     var danger=new Flexem.Studio.Components.Graphicses.EllipseInfo();var scope=new DrawingDraft();var id=Handle(danger,scope);var graphic=NewGraphic(2);graphic.Nested=danger;var outer=Handle(graphic,scope);
     Fails(delegate{Read(id,"Normal");},"stack-overflow");Fails(delegate{Read(outer,"Nested.Normal");},"stack-overflow");
     Fails(delegate{SetModelProperties(Obj("changes",new object[]{Edit(id,"Normal",null)}));},"stack-overflow");
     Check(Flexem.Studio.Components.Graphicses.EllipseInfo.GetterCalls==0&&Flexem.Studio.Components.Graphicses.EllipseInfo.SetterCalls==0,"dangerous accessor executed");
    });
    Run("draft setter failure restores failed and prior values",delegate{
     var scope=new DrawingDraft();var first=new UnstableSetter{Value=1};var second=new UnstableSetter{Value=2};var aid=Handle(first,scope);var bid=Handle(second,scope);
     Fails(delegate{SetModelProperties(Obj("changes",new object[]{Edit(aid,"Value",6),Edit(bid,"Value",13)}));},"test setter failure");Check(first.Value==1&&second.Value==2,"draft rollback incomplete");
    });
    Run("conversion errors fail before the first write",delegate{
     var graphic=NewGraphic(3);var id=Handle(graphic,new DrawingDraft());
     Fails(delegate{SetModelProperties(Obj("changes",new object[]{Edit(id,"Position.Width",20),Edit(id,"Position.Height","abc")}));},"FormatException");Check(graphic.Position.Width==3,"conversion failure mutated first property");
    });
    Run("apply many shares settings and enforces expansion limit",delegate{
     var scope=new DrawingDraft();var a=NewGraphic(1);var b=NewGraphic(2);var aid=Handle(a,scope);var bid=Handle(b,scope);
     var result=(Dictionary<string,object>)ModelOperation(Obj("op","apply_members","refs",new object[]{aid,bid},"properties",new object[]{Obj("member","Position.Width","value",12),Obj("member","Position.Height","value",24)}));
     Check(((object[])result["results"]).Length==4&&a.Position.Width==12&&b.Position.Height==24,"apply expansion result");
     Fails(delegate{ApplyModelProperties(Obj("refs",Enumerable.Repeat<object>(aid,51).ToArray(),"properties",new object[]{Obj("member","Position.Width","value",3),Obj("member","Position.Height","value",3)}));},"100");Check(a.Position.Width==12,"over-limit mutated");
    });
    Run("invalid empty and private paths fail clearly",delegate{
     var graphic=NewGraphic(3);var id=Handle(graphic,new DrawingDraft());
     Fails(delegate{Read(id,"Position..Width");},"empty property");Fails(delegate{Read(id,"Nested.Width");},"reaches null");Fails(delegate{Read(id,"Position.Missing");},"Unknown");Fails(delegate{Read(id,"");},"empty");
     Fails(delegate{Read(id,"WriteOnly");},"not publicly readable");Fails(delegate{SetModelProperties(Obj("changes",new object[]{Edit(id,"ReadOnly",8)}));},"not publicly writable");
    });
    Run("property paths enforce published length depth and identifier rules",delegate{
     Check(ModelMemberSegments(new string('A',512)).Length==1,"512-character path boundary rejected");
     Check(ModelMemberSegments(String.Join(".",Enumerable.Repeat("_A0",16).ToArray())).Length==16,"16-segment path boundary rejected");
     var graphic=NewGraphic(3);graphic.Nested=graphic;var id=Handle(graphic,new DrawingDraft());
     foreach(var path in new[]{new string('A',513),String.Join(".",Enumerable.Repeat("Nested",17).ToArray()),"Position[0]","Position.宽度","Position.0Width","Position.Width()"}){
      Fails(delegate{Read(id,path);},"Expected a property path");
      Fails(delegate{SetModelProperties(Obj("changes",new object[]{Edit(id,path,9)}));},"Expected a property path");
     }
     Check(graphic.Position.Width==3,"invalid path validation changed the model");
    });
    Run("value type path writes refused",delegate{
     var graphic=NewGraphic(2);var id=Handle(graphic,new DrawingDraft());
     Fails(delegate{SetModelProperties(Obj("changes",new object[]{Edit(id,"ValuePosition.Width",9)}));},"value-type copy");Check(graphic.ValuePosition.Width==0,"value path mutated");
    });
    Run("list indexing avoids enumeration and validates bounds",delegate{
     var values=new IndexOnlyList{4,5,6};var id=Handle(values,new DrawingDraft());
     Check((int)ReadModelMember(Obj("ref",id,"index",2))["value"]==6,"direct list index");
     Fails(delegate{ReadModelMember(Obj("ref",id,"index",3));},"outside");Fails(delegate{ReadModelMember(Obj("ref",id,"index",-1));},"negative");
     Fails(delegate{ReadModelMember(Obj("ref",id,"offset",-1));},"negative");
    });
    Run("batch shape and read count limits",delegate{
     var graphic=NewGraphic(2);var id=Handle(graphic,new DrawingDraft());var request=Obj("ref",id,"member","Position.Width");
     var result=(Dictionary<string,object>)ReadModelMembers(Obj("reads",Enumerable.Repeat<object>(request,100).ToArray()));Check(((object[])result["results"]).Length==100,"100 reads supported");
     Fails(delegate{ReadModelMembers(Obj("reads",Enumerable.Repeat<object>(request,101).ToArray()));},"100");Fails(delegate{ReadModelMembers(Obj("reads",new object[0]));},"1 and 100");
     Fails(delegate{ReadModelMembers(Obj("reads","invalid"));},"array");Fails(delegate{SetModelProperties(Obj("changes",new object[]{Obj("ref",id,"member","Position.Width")}));},"value");
    });
    Run("batch writes require an owned page or draft",delegate{
     var graphic=NewGraphic(3);var id=Handle(graphic,null);Fails(delegate{SetModelProperties(Obj("changes",new object[]{Edit(id,"Position.Width",8)}));},"page or draft scope");Check(graphic.Position.Width==3,"unowned batch mutated");
    });
    Run("parent replacement rejects dependent paths before writing",delegate{
     var scope=new DrawingDraft();var graphic=NewGraphic(3);var original=graphic.Position;var replacement=new Position{Width=7};var id=Handle(graphic,scope);var replacementId=Handle(replacement,scope);
     Fails(delegate{SetModelProperties(Obj("changes",new object[]{Edit(id,"Position",Obj("$ref",replacementId)),Edit(id,"Position.Width",99)}));},"Parent/child replacement conflict");
     Check(Object.ReferenceEquals(graphic.Position,original),"parent replacement preflight changed original relation");
     Check(original.Width==3&&replacement.Width==7,"detached or replacement child mutated");
    });
    Run("parent replacement rejects independent old child handles in either order",delegate{
     // 旧子句柄不会随父属性替换而失效；必须在事务开始前通过对象链关系拒绝，而不是等重解析路径发现变化。
     var transactionRoot=new RootItem();var scope=new PageView{DesignContext=new DesignContext{RootItem=transactionRoot}};
     var graphic=NewGraphic(3);var original=graphic.Position;var replacement=new Position{Width=7};var id=Handle(graphic,scope);
     var childId=Handle(original,scope);var replacementId=Handle(replacement,scope);
     var parentWrite=Edit(id,"Position",Obj("$ref",replacementId));var childWrite=Edit(childId,"Width",99);
     foreach(var changes in new[]{new object[]{parentWrite,childWrite},new object[]{childWrite,parentWrite}})
      Fails(delegate{SetModelProperties(Obj("changes",changes));},"Parent/child replacement conflict");
     Check(transactionRoot.Groups==0,"parent/child conflict started a transaction");
     Check(Object.ReferenceEquals(graphic.Position,original)&&original.Width==3&&replacement.Width==7,"parent/child conflict mutated a model");
    });
    Run("mode rebuild invalidates later path and restores model state",delegate{
     var scope=new DrawingDraft();var graphic=new ModeGraphic();var original=graphic.Position;var id=Handle(graphic,scope);
     Fails(delegate{SetModelProperties(Obj("changes",new object[]{Edit(id,"Mode",1),Edit(id,"Position.Width",99)}));},"target changed during batch");
     Check(graphic.Mode==0&&graphic.Position!=null&&graphic.Position.Width==3,"mode rollback did not restore current model state");
     Check(original.Width==3,"detached preflight child mutated");
     Check(!Object.ReferenceEquals(graphic.Position,original),"test setter must rebuild children during rollback");
    });
    Run("repeated handle lookup avoids scanning",delegate{
     object last=null;for(int i=0;i<5000;i++){last=NewGraphic(i);Handle(last,null);}string expected=Handle(last,null);
     var timer=Stopwatch.StartNew();for(int i=0;i<2000;i++)Check(refs.FirstOrDefault(p=>Object.ReferenceEquals(p.Value,last)).Key==expected,"baseline lookup");timer.Stop();double baseline=timer.Elapsed.TotalMilliseconds;
     timer.Restart();for(int i=0;i<2000;i++)Check(Handle(last,null)==expected,"cached lookup");timer.Stop();
     Console.WriteLine("BENCH 5000 handles / 2000 last-object lookups: linear="+baseline.ToString("F2")+"ms reverse="+timer.Elapsed.TotalMilliseconds.ToString("F2")+"ms");
    });
    Console.WriteLine("PASS 26 model access scenarios");return 0;
   }catch(Exception error){Console.Error.WriteLine(error);return 1;}
  }
 }
}
