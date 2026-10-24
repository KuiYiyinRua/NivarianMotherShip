using System;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace NMC.ContinuumRetry17
{
 public sealed class Probe : GameComponent
 {
  static string Root => Path.GetDirectoryName(GenFilePaths.SaveDataFolderPath);
  bool done;
  object campaign;
  Type type;
  int failures;
  public Probe(Game game) { }
  static void Record(string text) => File.AppendAllText(Path.Combine(Root,"evidence.txt"),text+"\n");
  void Check(bool ok,string text) { Record((ok?"PASS ":"FAIL ")+text); if(!ok) failures++; }
  object Get(string field) => AccessTools.Field(type,field).GetValue(campaign);
  int Int(string field) => (int)Get(field);
  void Set(string field,object value) => AccessTools.Field(type,field).SetValue(campaign,value);
  object Call(string method,params object[] args) => AccessTools.Method(type,method).Invoke(campaign,args);
  void State(string label) => Record(label+" ticks="+Find.TickManager.TicksGame+" completed="+Int("completedStage")+" active="+Int("activeStage")+" retry="+Int("retryRemaining")+" attempts="+Int("attempts")+" site="+((Site)Get("activeSite"))?.ID+" quest="+((Quest)Get("activeQuest"))?.State);
  void Advance(int ticks) { for(int i=0;i<ticks;i++){Find.TickManager.DebugSetTicksGame(Find.TickManager.TicksGame+1); Call("WorldComponentTick");} }
  void Reset(int completed,int time)
  {
   if(Int("activeStage")>0) Call("Resolve",false);
   Set("completedStage",completed);Set("retryRemaining",0);Set("attempts",0);
   Find.TickManager.DebugSetTicksGame(time);
  }
  void RetryCase(string name,int completed,int time,bool externalFailure)
  {
   Reset(completed,time);
   Check((bool)Call("TrySpawn"),name+" initial site generated");
   var old=(Quest)Get("activeQuest"); var site=(Site)Get("activeSite");
   if(old==null||site==null)throw new InvalidOperationException("Initial generation failed");
   if(externalFailure)old.End(QuestEndOutcome.Fail,false,false);
   else Set("entered",true); // Native retreat boundary: an entered site no longer has a map.
   Call("WorldComponentTick");State(name+" failure");
   Check(old.State==QuestState.EndedFailed&&Int("activeStage")==0&&Int("retryRemaining")==180000,name+" failure scheduled once");
   Advance(179999);State(name+" before deadline");
   Check(Int("activeStage")==0&&Int("retryRemaining")==1,name+" waits full three days");
   Advance(1);State(name+" deadline");
   Check(Int("activeStage")==completed+1&&Get("activeQuest")!=old&&((Quest)Get("activeQuest"))?.State==QuestState.Ongoing,name+" same stage regenerates at deadline");
   Check(Int("completedStage")==completed,name+" preserves completed research stage");
   Check(site.Destroyed,name+" unloaded failed site removed");
  }
  void ColdReload(bool overdue)
  {
   Prefs.DevMode=false;
   State("cold loaded");
   Check(Int("completedStage")==4,"cold load preserves completed stage four");
   if(overdue)
   {
    // Exercise the engine's real world-component dispatch on a cold-loaded old state.
    WorldComponentUtility.WorldComponentTick(Find.World);
    WorldComponentUtility.WorldComponentTick(Find.World);
    Check(Int("activeStage")==5&&((Quest)Get("activeQuest"))?.State==QuestState.Ongoing,"cold-loaded overdue failure regenerates without another three-day wait");
   }
   else
   {
    Check(Int("activeStage")==0&&Int("retryRemaining")>0&&Int("retryRemaining")<=12345,"cold load preserves unfinished countdown");
    int remaining=Int("retryRemaining");Advance(Math.Max(0,remaining-1));
    Check(Int("activeStage")==0&&Int("retryRemaining")==1,"cold-loaded retry does not fire early");
    Find.TickManager.DebugSetTicksGame(Find.TickManager.TicksGame+1);
    WorldComponentUtility.WorldComponentTick(Find.World);
    Check(Int("activeStage")==5&&((Quest)Get("activeQuest"))?.State==QuestState.Ongoing,"cold-loaded retry regenerates on deadline through world dispatch");
   }
   var quest=(Quest)Get("activeQuest");int attempts=Int("attempts");
   Advance(500);
   Check(Get("activeQuest")==quest&&Int("attempts")==attempts,"recovered mission remains unique after subsequent ticks");
  }
  public override void GameComponentUpdate()
  {
   if(done||!GenCommandLine.CommandLineArgPassed("nmc17-continuum-retry")||Current.ProgramState!=ProgramState.Playing||LongEventHandler.AnyEventNowOrWaiting)return;
   done=true;
   try
   {
    Find.TickManager.Pause();
    var assembly=AppDomain.CurrentDomain.GetAssemblies().Single(a=>a.GetName().Name=="NivarianCommander");
    type=assembly.GetType("NivarianCommander.ContinuumCampaign",true);
    campaign=type.GetProperty("Current").GetValue(null);
    Record("GAME "+VersionControl.CurrentVersionStringWithRev+" CORE "+assembly.Location);
    Check((bool)assembly.GetType("NivarianCommander.CommanderProgression").GetProperty("CampaignEnabled").GetValue(null),"1.7 campaign enabled");
    if(GenCommandLine.CommandLineArgPassed("nmc17-retry-reload-pending")||GenCommandLine.CommandLineArgPassed("nmc17-retry-reload-overdue"))
    {
     ColdReload(GenCommandLine.CommandLineArgPassed("nmc17-retry-reload-overdue"));
     Record("RESULT failures="+failures);
     File.WriteAllText(Path.Combine(Root,failures==0?"complete":"failed"),"assertion failures="+failures);
     Application.Quit();return;
    }
    RetryCase("mature retreat",0,600000,false);
    RetryCase("mature quest failure",3,900000,true);
    RetryCase("final stage failure",7,1200000,true);
    RetryCase("early existing quest",0,60000,true);
    Reset(2,1200000);
    Check((bool)Call("TrySpawn"),"orphaned failed quest initial site generated");
    var failedQuest=(Quest)Get("activeQuest");
    failedQuest.End(QuestEndOutcome.Fail,false,false);
    Set("activeQuest",null); // Save with an unresolved/removed quest reference.
    Call("WorldComponentTick");State("orphaned failed quest observed");
    Check(Int("activeStage")==0&&Int("retryRemaining")==180000,"orphaned failed quest enters retry instead of blocking forever");
    Advance(180000);
    Check(Int("activeStage")==3&&((Quest)Get("activeQuest"))?.State==QuestState.Ongoing,"orphaned failed quest regenerates same stage");
    Reset(1,1500000);
    Check((bool)Call("TrySpawn"),"lost live reference initial site generated");
    var liveQuest=(Quest)Get("activeQuest");var liveSite=(Site)Get("activeSite");int liveAttempts=Int("attempts");
    Set("activeQuest",null);
    Call("WorldComponentTick");State("lost live reference observed");
    Check(Get("activeQuest")==liveQuest&&Get("activeSite")==liveSite&&Int("attempts")==liveAttempts&&liveQuest.State==QuestState.Ongoing,"recover existing live quest without duplicate site or retry");
    Reset(4,1800000);
    Check((bool)Call("TrySpawn"),"overdue old save initial site generated");
    ((Quest)Get("activeQuest")).End(QuestEndOutcome.Fail,false,false);
    Set("activeQuest",null);
    Find.TickManager.DebugSetTicksGame(Find.TickManager.TicksGame+180001);
    GameDataSaveLoader.SaveGame("ContinuumRetry17-Overdue");
    Call("WorldComponentTick");State("overdue recovery");
    Check(Int("activeStage")==0&&Int("retryRemaining")==0,"already elapsed retry is not reset to another three days");
    Call("WorldComponentTick");
    Check(Int("activeStage")==5&&((Quest)Get("activeQuest"))?.State==QuestState.Ongoing,"overdue old state regenerates on next world tick");
    if(Int("activeStage")>0)Call("Resolve",false);
    Set("retryRemaining",12345);
    GameDataSaveLoader.SaveGame("ContinuumRetry17-Pending");
    var save=GenFilePaths.FilePathForSavedGame("ContinuumRetry17-Pending");
    Check(File.ReadAllText(save).Contains("<nmcContinuumRetryRemaining>12345</nmcContinuumRetryRemaining>"),"pending retry serialized");
    Reset(8,2400000);Advance(2);
    Check(Int("activeStage")==0&&Int("completedStage")==8,"completed eight-stage campaign never restarts");
    Reset(0,60000);Advance(2);
    Check(Int("activeStage")==0&&Int("attempts")==0,"brand-new campaign still waits until day seven");
    Record("RESULT failures="+failures);
    File.WriteAllText(Path.Combine(Root,failures==0?"complete":"failed"),"assertion failures="+failures);
   }
   catch(Exception error){Record(error.ToString());File.WriteAllText(Path.Combine(Root,"failed"),error.ToString());}
   Application.Quit();
  }
 }
}
