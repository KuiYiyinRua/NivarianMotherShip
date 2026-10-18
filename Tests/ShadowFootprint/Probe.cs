using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using NivarianCommander;
using RimWorld;
using UnityEngine;
using Verse;

namespace NMC.ShadowFootprintProbe
{
    public sealed class Probe : GameComponent
    {
        int stage, frames; bool done; Map map; Building_NutrientEnergyTower tower; Pawn pawn;
        static string Root => Path.GetDirectoryName(GenFilePaths.SaveDataFolderPath);
        public Probe(Game game) { }
        static void Need(bool ok, string name)
        {
            File.AppendAllText(Path.Combine(Root,"evidence.txt"),(ok?"PASS ":"FAIL ")+name+"\n");
            if(!ok) throw new Exception(name);
        }
        static bool Oracle(CellRect rect, IntVec3 cell, float radius)
            => rect.Cells.Any(origin => cell.InHorDistOf(origin,radius));
        void Buffer(double amount)
        {
            AccessTools.Field(typeof(Building_NutrientEnergyTower),"containedNutrition").SetValue(tower,amount);
            tower.GetComp<CompPowerPlant_NutrientTower>().UpdateDesiredPowerOutput();
        }
        void Geometry(CellRect rect, float radius)
        {
            var type=typeof(CommanderCloak).Assembly.GetType("NivarianCommander.ShadowCloakRange",true);
            var contains=(Func<CellRect,IntVec3,float,bool>)Delegate.CreateDelegate(typeof(Func<CellRect,IntVec3,float,bool>),type.GetMethod("Contains"));
            var cells=(Func<CellRect,Map,float,List<IntVec3>>)Delegate.CreateDelegate(typeof(Func<CellRect,Map,float,List<IntVec3>>),type.GetMethod("Cells"));
            foreach(var footprint in new[]{rect,new CellRect(0,0,2,2),new CellRect(map.Size.x-2,map.Size.z-2,2,2)})
            {
                var shown=cells(footprint,map,radius);
                var expected=footprint.ExpandedBy(9).Cells.Where(c=>c.InBounds(map)&&Oracle(footprint,c,radius)).ToHashSet();
                Need(expected.SetEquals(shown)&&shown.Distinct().Count()==shown.Count,"overlay union matches four circles without duplicates at "+footprint);
                Need(footprint.ExpandedBy(9).Cells.All(c=>contains(footprint,c,radius)==Oracle(footprint,c,radius)),"geometry matches independent four-origin oracle at "+footprint);
            }
            Need(cells(rect,map,0).Count==0&&!contains(rect,pawn.Position,0),"zero radius has no coverage");
        }
        public override void GameComponentUpdate()
        {
            if(done||!GenCommandLine.CommandLineArgPassed("nmc-shadow-footprint-probe")||Current.ProgramState!=ProgramState.Playing||LongEventHandler.AnyEventNowOrWaiting)return;
            try
            {
                if(stage==0)
                {
                    Find.TickManager.Pause(); map=Find.CurrentMap; var at=map.Center+new IntVec3(30,0,20);
                    foreach(var c in CellRect.CenteredOn(at,18).ClipInsideMap(map))
                    {foreach(var t in c.GetThingList(map).ToArray())if(!(t is Pawn))t.Destroy();map.terrainGrid.SetTerrain(c,TerrainDefOf.Concrete);map.roofGrid.SetRoof(c,null);map.fogGrid.Unfog(c);}
                    var research=ResearchProjectDef.Named("Nivarian_MothershipSupportInterface");
                    ((Dictionary<ResearchProjectDef,float>)AccessTools.Field(typeof(ResearchManager),"progress").GetValue(Find.ResearchManager))[research]=research.baseCost;
                    tower=(Building_NutrientEnergyTower)ThingMaker.MakeThing(ThingDef.Named("NMC_ShadowEnergyTower"));
                    tower.SetFaction(Faction.OfPlayer);GenSpawn.Spawn(tower,at,map);Buffer(3);
                    Need(tower.IsOperating,"fueled tower operating precondition");
                    pawn=map.mapPawns.FreeColonists.First();var rect=tower.OccupiedRect();float radius=tower.GetComp<CompCommanderStructure>().Props.cloakRadius;
                    Need(rect.Area==4&&radius==7f,"four occupied cells and seven-cell radius retained");
                    var added=rect.ExpandedBy(8).Cells.Where(c=>Oracle(rect,c,radius)&&!c.InHorDistOf(tower.Position,radius)).ToArray();
                    Need(added.Length>0,"fixture includes cells missed by the old single-origin circle");
                    pawn.Position=added.First();
                    Need(CommanderCloak.Covered(pawn),"new footprint-only boundary cell is actually cloaked");
                    int checkedCells=0;
                    foreach(var c in rect.ExpandedBy(9))
                    {pawn.Position=c;if(CommanderCloak.Covered(pawn)!=Oracle(rect,c,radius))throw new Exception("Coverage mismatch "+c);checkedCells++;}
                    Need(checkedCells==400,"actual cloak query matches four-circle union across all 400 cells");
                    Geometry(rect,radius);
                    pawn.Position=added.First();
                    Buffer(0);Need(!CommanderCloak.Covered(pawn),"starvation disables expanded coverage");
                    Buffer(3);Need(CommanderCloak.Covered(pawn),"refueling restores expanded coverage");
                    tower.GetComp<CompFlickable>().SwitchIsOn=false;Need(!CommanderCloak.Covered(pawn),"switch off disables expanded coverage");
                    tower.GetComp<CompFlickable>().SwitchIsOn=true;Buffer(3);
                    tower.GetComp<CompBreakdownable>().DoBreakdown();Need(!CommanderCloak.Covered(pawn),"breakdown disables expanded coverage");
                    tower.GetComp<CompBreakdownable>().Notify_Repaired();Buffer(3);
                    Need(!CommanderCloak.Covered(tower),"tower remains immune to its own cloak");
                    CommanderMod.Settings.showShortcut=false;
                    Find.Selector.ClearSelection();Find.Selector.Select(tower);
                    Find.CameraDriver.JumpToCurrentMapLoc((at+new IntVec3(0,0,-2)).ToVector3Shifted());Find.CameraDriver.SetRootSize(14f);
                    stage=1;frames=0;
                }
                else if(stage==1&&++frames==60){ScreenCapture.CaptureScreenshot(Path.Combine(Root,"selected-shadow-range.png"));stage=2;frames=0;}
                else if(stage==2&&++frames==60)
                {
                    Need(File.Exists(Path.Combine(Root,"selected-shadow-range.png")),"selected tower render captured");
                    File.WriteAllText(Path.Combine(Root,"complete"),"PASS");done=true;Application.Quit();
                }
            }
            catch(Exception e){done=true;File.WriteAllText(Path.Combine(Root,"failed"),e.ToString());Application.Quit();}
        }
    }
}
