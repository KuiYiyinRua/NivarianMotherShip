using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using NivarianCommander;
using Nivarian_Race.Code.Defs;
using RimWorld;
using UnityEngine;
using Verse;

namespace NMC.RimWorld17Abilities
{
    public sealed class Probe : GameComponent
    {
        static bool Enabled => GenCommandLine.CommandLineArgPassed("nmc17-abilities");
        static string Root => Path.GetDirectoryName(GenFilePaths.SaveDataFolderPath);
        Map map; IntVec3 center; int step, started, frames; bool initialized, done, reloaded;
        readonly List<Building_NivarianAircraft> ships = new List<Building_NivarianAircraft>();
        static int blackHoleFrames;
        int pausedElapsed, renderedBeforeLoad, renderedAfterCancel;
        static void CountBlackHole() { blackHoleFrames++; }
        public Probe(Game game) { }
        static void Record(string s) => File.AppendAllText(Path.Combine(Root, "evidence.txt"), s + "\n");
        static void Need(bool v, string s) { Record((v ? "PASS " : "FAIL ") + s); if (!v) throw new Exception(s); }
        public override void LoadedGame()
        {
            if (!Enabled || !File.Exists(Path.Combine(Root, "reload"))) return;
            reloaded = true;
            Find.TickManager.Pause();
            try {
                Need(CommanderState.Current.cooldowns["NMC_S01"] == 1234, "saved field cooldown restored exactly before ticking");
                pausedElapsed=int.Parse(File.ReadAllText(Path.Combine(Root,"reload")));
                Need(Find.CurrentMap.GetComponent<BlackHoleMap>().active?.elapsed==pausedElapsed,"active black hole restored at exact elapsed tick");
                renderedBeforeLoad=blackHoleFrames;
            }
            catch(Exception e) { Fail(e); }
        }
        static Thing Spawn(string name, Map map, IntVec3 at)
        {
            var def=ThingDef.Named(name);
            var thing=ThingMaker.MakeThing(def, def.MadeFromStuff ? ThingDefOf.Steel : null);
            thing.SetFaction(Faction.OfPlayer); GenSpawn.Spawn(thing,at,map); return thing;
        }
        void Clear(IntVec3 at, int radius)
        {
            foreach(var c in CellRect.CenteredOn(at,radius).ClipInsideMap(map))
            {
                foreach(var t in c.GetThingList(map).ToArray()) if(!(t is Pawn))
                { if(t.def.destroyable)t.Destroy(DestroyMode.Vanish);else t.DeSpawn(); }
                map.terrainGrid.SetTerrain(c,TerrainDefOf.Concrete); map.roofGrid.SetRoof(c,null); map.fogGrid.Unfog(c);
            }
        }
        void Fail(Exception e) { done=true; Record(e.ToString()); File.WriteAllText(Path.Combine(Root,"failed"),e.ToString()); Application.Quit(); }
        public override void GameComponentUpdate()
        {
            if(!Enabled||done||Current.ProgramState!=ProgramState.Playing||LongEventHandler.AnyEventNowOrWaiting)return;
            try
            {
                if(reloaded)
                {
                    frames++;Find.TickManager.Pause();
                    if(frames==1)Need(Find.CurrentMap.listerThings.AllThings.OfType<Building_NivarianAircraft>().Count(s=>s.Airborne)==5,"all five airborne ship types restored");
                    if(frames==30){
                        Need(Find.CurrentMap.GetComponent<BlackHoleMap>().active?.elapsed==pausedElapsed,"black hole animation remains frozen while paused after load");
                        Need(blackHoleFrames>renderedBeforeLoad+10,"black hole screen passes rebuilt after load");
                        ScreenCapture.CaptureScreenshot(Path.Combine(Root,"blackhole-after-load.png"));
                    }
                    if(frames==45)BlackHoleActions.Cancel(Find.CurrentMap);
                    if(frames==50)renderedAfterCancel=blackHoleFrames;
                    if(frames<70)return;
                    Need(blackHoleFrames==renderedAfterCancel,"cancel retires black hole screen passes");
                    File.WriteAllText(Path.Combine(Root,"complete"),"PASS");done=true;Application.Quit();return;
                }
                if(!initialized)
                {
                    initialized=true; map=Find.CurrentMap; center=map.Center;
                    Need(CommanderMod.Initialized,"all migration hooks initialized");
                    new Harmony("nmc17.abilities.render").Patch(AccessTools.Method(typeof(BlackHoleScreenEffect), "BuildFrame"),
                        prefix: new HarmonyMethod(typeof(Probe), nameof(CountBlackHole)));
                    Need(CommanderProgression.CampaignEnabled,"bundled 1.7 military content enables campaign");
                    var locked=CommanderResearch.For(DefenseActions.Ability(19));
                    Need(CommanderProgression.ResearchBlocked(locked)!=null,"mission gate remains active");
                    var queue=AccessTools.TypeByName("Nivarian_Race.Code.Features.UplinkResearch.UplinkResearchQueueComponent");
                    var manager=AccessTools.Property(queue,"Current").GetValue(null);
                    Need(((string)AccessTools.Method(queue,"WaitingReason").Invoke(manager,new object[]{locked})).Length>0,"new research queue explains mission lock");
                    ContinuumCampaign.Current.completedStage=8;
                    var progress=(Dictionary<ResearchProjectDef,float>)AccessTools.Field(typeof(ResearchManager),"progress").GetValue(Find.ResearchManager);
                    foreach(var research in DefDatabase<ResearchProjectDef>.AllDefsListForReading) progress[research]=research.baseCost;
                    foreach(var ability in DefDatabase<CommanderAbilityDef>.AllDefsListForReading)
                        CommanderResearch.For(ability).GetModExtension<CommanderResearchExtension>().mothershipLevel=0;
                    foreach(string key in new[]{"PowerCoin","VolatileReactive"})NativeLedger.Component.CurrencyHold[DefDatabase<NivarianMotherShipRequestCurrencyDef>.GetNamed(key)]=1000000;
                    foreach(var pawn in map.mapPawns.FreeColonistsSpawned) pawn.Position=center+new IntVec3(40,0,40);
                    Clear(center,32);CommanderMod.Settings.showShortcut=false;Find.CameraDriver.SetRootSize(24f);
                    started=Find.TickManager.TicksGame-180;
                }
                Find.TickManager.CurTimeSpeed=TimeSpeed.Normal;
                int elapsed=Find.TickManager.TicksGame-started;
                if(step>0&&step<=19&&elapsed>60&&frames++==0)ScreenCapture.CaptureScreenshot(Path.Combine(Root,"support-"+step.ToString("00")+".png"));
                if(elapsed<150)return;
                if(step<19)
                {
                    if(step==10)Need(blackHoleFrames>0,"black hole postprocessing really rendered");
                    BlackHoleActions.Cancel(map);RayActions.Cancel(map);PurificationActions.Cancel(map);TimeStopActions.Cancel();VoidCrystalActions.Cancel(map);
                    CommanderState.Current.cooldowns.Clear();
                    int number=++step; IntVec3 at=center+new IntVec3((number%3-1)*17,0,((number/3)%3-1)*17);
                    if(number==8||number==17||number==13)Clear(at,10);
                    int p=NativeLedger.Balance("PowerCoin"), rejected=CommanderState.Current.requestsRejected;
                    Find.CameraDriver.JumpToCurrentMapLoc(at.ToVector3Shifted());
                    switch(number)
                    {
                        case 1: DefenseActions.DeployField(map,at);break;
                        case 2: CommanderActions.Fire(map,at);break;
                        case 3: DefenseActions.Shield(map);break;
                        case 4: SolarActions.Start(map,at);break;
                        case 5: RayActions.Start(map,at);break;
                        case 6: SpacetimeWaveActions.Start();break;
                        case 7: PurificationActions.Start(map,at);break;
                        case 8: StructureActions.DeployCore(map,at,8);break;
                        case 9: GhostActions.Deploy(map,at);break;
                        case 10: BlackHoleActions.Start(map,at);break;
                        case 11: TimeStopActions.Start();break;
                        case 12: CommanderState.Current.overloadCharges=3; DefenseActions.Overload(map,Spawn("NMC_NutrientEnergyTower",map,at));break;
                        case 13: FleetActions.Start(map,at);break;
                        case 14: TebrusLegionActions.Start(map,at);break;
                        case 15: RayFleetActions.Start(map,at);break;
                        case 16: SeydasLegionActions.Start(map,at);break;
                        case 17: StructureActions.DeployCore(map,at,17);break;
                        case 18: VoidCrystalActions.Start(map,at);break;
                        case 19:
                            var source=Spawn("NMC_HyperdimensionalMonolith",map,center+new IntVec3(-30,0,-30));
                            ProjectionActions.Register(map,source,true);ProjectionActions.Start(map,at);break;
                    }
                    Need(CommanderState.Current.requestsRejected==rejected,"support "+number+" accepted by real executor");
                    Need(NativeLedger.Balance("PowerCoin")<p,"support "+number+" paid through native ledger");
                    if(number==10)Need(map.GetComponent<BlackHoleMap>().Active,"black hole simulation active");
                    started=Find.TickManager.TicksGame;frames=0;return;
                }
                if(step==19)
                {
                    Clear(center+new IntVec3(0,0,48),34);
                    string[] defs={"NMC_Carrier","NMC_GravityFrigate","NMC_StormShip","NMC_SageShip","NMC_WarpPrism"};
                    for(int i=0;i<defs.Length;i++)
                    {
                        var ship=(Building_NivarianAircraft)Spawn(defs[i],map,center+new IntVec3((i-2)*12,0,48));
                        ship.TryGetComp<CompRefuelable>()?.Refuel(10000); ships.Add(ship); AircraftActions.Takeoff(map,ship);
                    }
                    step++;started=Find.TickManager.TicksGame;return;
                }
                if(step==20){
                    if(elapsed<500)return;
                    foreach(var ship in ships)Need(ship.Airborne,"native takeoff "+ship.def.defName);
                    CommanderState.Current.cooldowns.Clear();BlackHoleActions.Start(map,center);
                    Find.CameraDriver.JumpToCurrentMapLoc(center.ToVector3Shifted());
                    step++;started=Find.TickManager.TicksGame;return;
                }
                if(elapsed<90)return;
                Find.TickManager.Pause();CommanderState.Current.cooldowns["NMC_S01"]=1234;
                GameDataSaveLoader.SaveGame("NMC17-Abilities");File.WriteAllText(Path.Combine(Root,"reload"),map.GetComponent<BlackHoleMap>().active.elapsed.ToString());
                done=true;GameDataSaveLoader.LoadGame("NMC17-Abilities");
            }
            catch(Exception e){Fail(e);}
        }
    }
}
