using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using NivarianCommander;
using Nivarian_Race.Code.Comps.BuildingComps;
using Nivarian_Race.Code.Defs;
using Nivarian_Race.Code.NivarianBuilding;
using RimWorld;
using UnityEngine;
using Verse;
namespace NMC.NativeConstructionProbe
{
 public sealed class Probe : GameComponent
 {
  int stage, frames, start; Map map; IntVec3 center; Thing[] sites; CompSelfBuilding[] builds; bool done;
  static string Root => Path.GetDirectoryName(GenFilePaths.SaveDataFolderPath);
  public Probe(Game game){}
  static void Record(string s)=>File.AppendAllText(Path.Combine(Root,"evidence.txt"),s+"\n");
  static void Need(bool v,string s){Record((v?"PASS ":"FAIL ")+s);if(!v)throw new Exception(s);}
  static void Shot(string name)=>ScreenCapture.CaptureScreenshot(Path.Combine(Root,name+".png"));
  public override void GameComponentUpdate()
  {
   if(done||!GenCommandLine.CommandLineArgPassed("nmc-native-construction-probe")||Current.ProgramState!=ProgramState.Playing||LongEventHandler.AnyEventNowOrWaiting)return;
   try
   {
    if(stage==0)
    {
     Find.TickManager.Pause();map=Find.CurrentMap;center=map.Center+new IntVec3(30,0,20);
     foreach(var c in CellRect.CenteredOn(center,17))
     { foreach(var t in c.GetThingList(map).ToArray())if(!(t is Pawn)){if(t.def.destroyable)t.Destroy();else t.DeSpawn();} map.terrainGrid.SetTerrain(c,TerrainDefOf.Concrete);map.roofGrid.SetRoof(c,null);map.fogGrid.Unfog(c); }
     foreach(string key in new[]{"PowerCoin","VolatileReactive"})NativeLedger.Component.CurrencyHold[DefDatabase<NivarianMotherShipRequestCurrencyDef>.GetNamed(key)]=100000;
     sites=new Thing[2];builds=new CompSelfBuilding[2];int[] numbers={8,17};
     for(int i=0;i<2;i++)
     {
      var ability=DefenseActions.Ability(numbers[i]);var research=CommanderResearch.For(ability);
      ((Dictionary<ResearchProjectDef,float>)AccessTools.Field(typeof(ResearchManager),"progress").GetValue(Find.ResearchManager))[research]=research.baseCost;
      // Isolate deployment/construction from progression prerequisites in this test world.
      research.GetModExtension<CommanderResearchExtension>().mothershipLevel=0;
      string blocked=StructureActions.Unavailable(map,numbers[i]);var coreDef=StructureActions.CoreFor(numbers[i]);var props=coreDef?.GetCompProperties<CompProperties_SelfBuilding>();
      Record("DEPLOY "+numbers[i]+" blocked="+blocked+" researchFinished="+research.IsFinished+" core="+coreDef?.defName+" class="+coreDef?.thingClass+" nativeProps="+(props!=null)+" work="+props?.workAmount+" cost="+props?.costList?.Count+" size="+coreDef?.Size+" targetSize="+props?.buildingDef?.Size);
      Need(blocked==null,"native construction configuration accepted "+numbers[i]);
      var cell=center+new IntVec3(i==0?-6:6,0,-4);int before=NativeLedger.Balance("PowerCoin");
      StructureActions.DeployCore(map,cell,numbers[i]);sites[i]=map.listerThings.ThingsOfDef(StructureActions.CoreFor(numbers[i])).Single();builds[i]=sites[i].TryGetComp<CompSelfBuilding>();
      Need(sites[i] is Building_SelBuilding && builds[i]!=null && sites[i].TryGetComp<CompCommanderConstruction>()==null,"deployment uses native construct and native construction component "+numbers[i]);
      Need(before-NativeLedger.Balance("PowerCoin")==ability.powerCost,"deployment charges original support price once "+numbers[i]);
      Need(!builds[i].BuildingStarted && builds[i].GetNeededAmount(ThingDefOf.Steel)==(i==0?150:300),"native ingredient request and waiting state "+numbers[i]);
     }
     Need(ThingDef.Named("NMC_ShadowCore").GetCompProperties<CompProperties_CommanderConstruction>()!=null,"legacy in-progress core definition retained");
     Need(CommanderCloak.Immune(sites[0]),"native shadow construct retains cloak immunity");
     var relay=ThingMaker.MakeThing(ThingDef.Named("NMC_ContinuumRelay"));relay.SetFaction(Faction.OfAncientsHostile);GenSpawn.Spawn(relay,center+new IntVec3(-5,0,7),map);
     var power=ThingMaker.MakeThing(ThingDef.Named("NMC_ContinuumPowerCore"));power.SetFaction(Faction.OfAncientsHostile);GenSpawn.Spawn(power,center+new IntVec3(5,0,7),map);
     Need(relay.def.graphicData.texPath.EndsWith("continuum_communications_core")&&power.def.graphicData.texPath.EndsWith("continuum_power_core"),"both base cores use distinct Continuum pylon sprites");
     Need(relay.MaxHitPoints==450&&power.MaxHitPoints==350&&power.def.GetCompProperties<CompProperties_Power>().PowerConsumption==-12000,"base core durability and power output preserved");
     Need(relay.Graphic.MatSingle.mainTexture.width>0&&power.Graphic.MatSingle.mainTexture.width>0,"both transparent sprites loaded by game");
     CommanderMod.Settings.showShortcut=false;
     Find.CameraDriver.JumpToCurrentMapLoc((center+new IntVec3(0,0,1)).ToVector3Shifted());
     Find.CameraDriver.SetRootSize(14f);stage=1;frames=0;
    }
    else if(stage==1&&++frames==60){Shot("waiting-constructs-and-cores");stage=2;frames=0;}
    else if(stage==2&&++frames==30)
    {
     for(int i=0;i<2;i++)foreach(var cost in builds[i].Props.costList)
     {var item=ThingMaker.MakeThing(cost.thingDef);item.stackCount=cost.count;Need(builds[i].ContainerComp.GetIngredientsContainer().TryAdd(item),"material delivered "+i+" "+cost.thingDef.defName);}
     start=Find.TickManager.TicksGame;Find.TickManager.CurTimeSpeed=TimeSpeed.Superfast;stage=3;
    }
    else if(stage==3&&builds[1].Progress>.25f)
    {
     Need(builds.All(c=>c.BuildingStarted)&&!sites[0].Destroyed,"native tick starts both construction animations");
     Shot("native-printing");stage=4;
    }
    else if(stage==4&&sites.All(s=>s.Destroyed))
    {
     Find.TickManager.Pause();
     for(int i=0;i<2;i++)Need(map.listerThings.ThingsOfDef(builds[i].Props.buildingDef).Count==1,"native completion creates exactly one original finished structure "+i);
     Need(Find.TickManager.TicksGame-start>=2700,"native work duration preserved at base speed");
     Shot("completed-buildings-and-cores");GameDataSaveLoader.SaveGame("NativeConstruction-Complete");stage=5;frames=0;
    }
    else if(stage==5&&++frames>60){Need(File.Exists(Path.Combine(Root,"native-printing.png")),"in-game render evidence captured");File.WriteAllText(Path.Combine(Root,"complete"),"PASS");done=true;Application.Quit();}
    if(stage>=3&&stage<5&&Find.TickManager.TicksGame-start>8000)throw new Exception("Native construction did not complete before tick deadline");
   }
   catch(Exception e){done=true;Record(e.ToString());File.WriteAllText(Path.Combine(Root,"failed"),e.ToString());Application.Quit();}
  }
 }
}
