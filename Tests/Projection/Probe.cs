using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Nivarian.NivarianDrones;
using NivarianCommander;
using Nivarian_Race.Code.Defs;
using Nivarian_Race.Code.Helper;
using RimWorld;
using UnityEngine;
using Verse;

namespace NMC.ProjectionProbe
{
    public sealed class Probe : GameComponent
    {
        static readonly string[] HubDefs = { "Nivarian_DroneHub", "Nivarian_DroneHub_AttackerStrafing", "Nivarian_DroneHub_Recon" };
        static readonly FieldInfo Batteries = AccessTools.Field(typeof(NivarianDroneHubComp), "_batteries");
        static readonly FieldInfo Drones = AccessTools.Field(typeof(NivarianDroneHubComp), "_spawnedDrones");
        static readonly MethodInfo SpawnDrone = AccessTools.Method(typeof(NivarianDroneHubComp), "SpawnDrone");
        static readonly MethodInfo RecallDrone = AccessTools.Method(typeof(NivarianDroneHubComp), "RecallDroneImmediately");
        static readonly FieldInfo PowerOn = AccessTools.Field(typeof(CompPowerTrader), "powerOnInt");
        int stage, frames, start;
        Map map;
        IntVec3 center;
        List<Thing> sources = new List<Thing>();
        List<ProjectionLink> links;
        bool done;
        public Probe(Game game) { }
        public override void LoadedGame()
        {
            if (!GenCommandLine.CommandLineArgPassed("nmc-projection-probe") || !File.Exists(Path.Combine(Root, "reload"))) return;
            Find.TickManager.Pause();
            Record("LOADED cooldown=" + CommanderState.Current.cooldowns["NMC_S19"]
                + " remaining=" + string.Join(",", ProjectionRegistry.Current.links.Select(l => l.remaining)));
        }
        static string Root => Path.GetDirectoryName(GenFilePaths.SaveDataFolderPath);
        static void Record(string value) => File.AppendAllText(Path.Combine(Root, "evidence.txt"), value + "\n");
        static void Need(bool condition, string name)
        { Record((condition ? "PASS " : "FAIL ") + name); if (!condition) throw new Exception(name); }
        static List<NivarianGauge> Pool(NivarianDroneHubComp hub) => (List<NivarianGauge>)Batteries.GetValue(hub);
        static List<NivarianDroneBase> Flying(NivarianDroneHubComp hub) => (List<NivarianDroneBase>)Drones.GetValue(hub);
        static void Shot(string name) => ScreenCapture.CaptureScreenshot(Path.Combine(Root, name + ".png"));
        public override void GameComponentUpdate()
        {
            if (done || !GenCommandLine.CommandLineArgPassed("nmc-projection-probe")
                || Current.ProgramState != ProgramState.Playing || LongEventHandler.AnyEventNowOrWaiting) return;
            try
            {
#if RIMWORLD_1_7
                NMC.RimWorld17Fixtures.Projection17Fixture.EnsureCompute();
#endif
                if (stage == 0 && File.Exists(Path.Combine(Root, "reload")))
                {
                    Find.TickManager.Pause();
                    links = ProjectionRegistry.Current.links.ToList();
                    Record("CHECK_RELOAD cooldown=" + CommanderState.Current.cooldowns["NMC_S19"]
                        + " remaining=" + string.Join(",", links.Select(l => l.remaining)));
                    Need(links.Count == 4, "saved projection identities restored");
                    Need(CommanderState.Current.cooldowns["NMC_S19"] == 120000, "old seven-day cooldown clamped to two days on load");
                    foreach (var link in links)
                    {
                        Need(link.remaining == 3800 && ProjectionVisuals.Forming(link.projection), "saved formation timer restored " + link.source.def.defName);
                        if (!ProjectionDrones.Eligible(link.source)) continue;
                        Need(link.dronePoolTransferred, "saved battery ownership restored " + link.source.def.defName);
                        Need(Pool(link.source.TryGetComp<NivarianDroneHubComp>()).Count == 0, "source battery stays empty after load");
                        Need(Pool(link.projection.TryGetComp<NivarianDroneHubComp>()).Count == 1, "projection battery survives load");
                    }
                    var hubs = links.Where(l => ProjectionDrones.Eligible(l.source)).ToArray();
                    foreach (var link in hubs)
                    {
                        link.remaining = 3600;
                        var hub = link.projection.TryGetComp<NivarianDroneHubComp>();
                        AccessTools.Method(typeof(NivarianDroneHubComp), "EnsureDroneDefCacheInitialized").Invoke(hub, null);
                        // Restore sufficient charge only inside the fixture to
                        // exercise cleanup with a real active native drone.
                        foreach (var battery in Pool(hub)) battery.Add(battery.Size - battery.Value);
                        SpawnDrone.Invoke(hub, new object[] { LocalTargetInfo.Invalid });
                        Need(Flying(hub).Count == 1, "native drone relaunched after load");
                    }
                    var flight = hubs.Select(l => Flying(l.projection.TryGetComp<NivarianDroneHubComp>()).Single()).ToArray();
                    ProjectionActions.Cancel(hubs[0].targetMap, hubs[0].projection);
                    hubs[1].projection.Destroy(DestroyMode.KillFinalize);
                    hubs[2].remaining = 1; hubs[2].bornLocalTick = -1;
                    ProjectionRegistry.Current.Tick(hubs[2].targetMap);
                    foreach (var link in links.Where(l => !ProjectionDrones.Eligible(l.source))) ProjectionActions.Cancel(link.targetMap, link.projection);
                    Need(ProjectionRegistry.Current.links.Count == 0, "loaded projections recalled");
                    Need(flight.All(d => d.Destroyed), "recall, destruction and expiry remove native drones");
#if RIMWORLD_1_7
                    Need(Nivarian_Race.Code.Features.ComputeNetwork.GameComp_NivarianCompute.Instance.DeviceBandwidthUsed == 0,
                        "all native device bandwidth released after projection recall");
#endif
                    foreach (var link in links.Where(l => ProjectionDrones.Eligible(l.source)))
                        Need(Pool(link.source.TryGetComp<NivarianDroneHubComp>()).Count == 1, "loaded battery returned exactly once");
                    File.WriteAllText(Path.Combine(Root, "complete"), "PASS"); done = true; Application.Quit(); return;
                }
                if (stage == 0)
                {
                    Find.TickManager.Pause(); map = Find.CurrentMap; center = map.Center;
                    foreach (var c in CellRect.CenteredOn(center, 24))
                    {
                        foreach (var thing in c.GetThingList(map).ToArray()) if (!(thing is Pawn))
                        { if(thing.def.destroyable)thing.Destroy();else thing.DeSpawn(); }
                        map.terrainGrid.SetTerrain(c, TerrainDefOf.Concrete); map.roofGrid.SetRoof(c, null); map.fogGrid.Unfog(c);
                    }
                    foreach (string key in new[] { "PowerCoin", "VolatileReactive" })
                        NativeLedger.Component.CurrencyHold[DefDatabase<NivarianMotherShipRequestCurrencyDef>.GetNamed(key)] = 100000;
                    var ability = DefenseActions.Ability(19); var research = CommanderResearch.For(ability);
                    ((Dictionary<ResearchProjectDef, float>)AccessTools.Field(typeof(ResearchManager), "progress").GetValue(Find.ResearchManager))[research] = research.baseCost;
                    research.GetModExtension<CommanderResearchExtension>().mothershipLevel = 0;
                    Need(ability.cooldownTicks == 120000 && ability.powerCost == 300 && ability.volatileCost == 100, "two days and doubled P/V");
                    Need(DefenseActions.Ability(8).iconPath == "NMC/Supports/shadow_core", "original shadow panel icon");
                    Need(DefenseActions.Ability(17).iconPath == "NMC/Supports/hyperdimensional_monolith", "original monolith panel icon");
                    foreach (int number in new[] { 8, 17 })
                        Need(ContentFinder<Texture2D>.Get(DefenseActions.Ability(number).iconPath).width > 0, "original panel texture loads " + number);
                    for (int i = 0; i < 4; i++)
                    {
                        var thing = ThingMaker.MakeThing(ThingDef.Named(i < 3 ? HubDefs[i] : "NMC_HyperdimensionalMonolith"));
                        thing.SetFaction(Faction.OfPlayer); GenSpawn.Spawn(thing, center + new IntVec3(-17, 0, i * 5 - 8), map);
                        PowerOn.SetValue(thing.TryGetComp<CompPowerTrader>(), true);
                        sources.Add(thing);
                        Need(ProjectionRegistry.Eligible(thing), "eligible " + thing.def.defName);
                        if (i < 3)
                        {
                            var hub = thing.TryGetComp<NivarianDroneHubComp>();
                            AccessTools.Method(typeof(NivarianDroneHubComp), "EnsureDroneDefCacheInitialized").Invoke(hub, null);
                            AccessTools.Method(typeof(NivarianDroneHubComp), "InitBatteries").Invoke(hub, null);
                            AccessTools.Field(typeof(NivarianDroneHubComp), "batteriesInited").SetValue(hub, true);
                            foreach (var battery in Pool(hub)) battery.Add(battery.Size * .75f - battery.Value);
                            SpawnDrone.Invoke(hub, new object[] { LocalTargetInfo.Invalid });
                            Need(Flying(hub).Count == 1, "native source drone launched " + i);
                        }
                        ProjectionActions.Register(map, thing, true);
                    }
#if RIMWORLD_1_7
                    Need(Nivarian_Race.Code.Features.ComputeNetwork.GameComp_NivarianCompute.Instance.Reservations.Count() == 3,
                        "three source drones reserve native device bandwidth");
#endif
                    int p = NativeLedger.Balance("PowerCoin"), v = NativeLedger.Balance("VolatileReactive");
                    ProjectionActions.Start(map, center + new IntVec3(9, 0, 0));
                    links = ProjectionRegistry.Current.links.ToList();
                    Need(links.Count == 4, "real projection action creates all three hubs and ordinary monolith");
                    Need(p - NativeLedger.Balance("PowerCoin") == 300 && v - NativeLedger.Balance("VolatileReactive") == 100, "one exact payment");
                    Need(CommanderState.Current.cooldowns[ability.defName] == 120000, "executor reads new cooldown");
                    foreach (var link in links)
                    {
                        Need(link.projection.HitPoints == link.source.HitPoints / 2, "half HP " + link.source.def.defName);
                        Need(!ProjectionVisuals.Forming(link.source) && ProjectionVisuals.Forming(link.projection), "formation only at destination");
                        if (!ProjectionDrones.Eligible(link.source)) continue;
                        var original = link.source.TryGetComp<NivarianDroneHubComp>(); var copy = link.projection.TryGetComp<NivarianDroneHubComp>();
                        Need(copy.DroneNeedBattery, "projection battery cache initialized before first tick");
                        Need(Flying(original).Count == 0 && Pool(original).Count == 0, "source recalled and battery ownership moved");
                        Need(Math.Abs(Pool(copy).Single().Progress - .75f) < .001f, "actual flight charge transferred without refill");
                        Need(!copy.ShouldReleaseDrone() && !original.ShouldReleaseDrone(), "both launch paths blocked in warmup");
                        Need(!ProjectionDrones.CanRun(original) && !ProjectionDrones.CanRun(copy), "battery ticking blocked during warmup");
                    }
                    CommanderMod.Settings.showShortcut = false;
                    Find.CameraDriver.JumpToCurrentMapLoc(center.ToVector3Shifted()); Find.CameraDriver.SetRootSize(27f);
                    start = Find.TickManager.TicksGame; Find.TickManager.CurTimeSpeed = TimeSpeed.Normal; stage = 1;
                }
                else if (stage == 1)
                {
                    foreach (var source in sources) PowerOn.SetValue(source.TryGetComp<CompPowerTrader>(), true);
                    if (links[0].remaining < 3750 && links[0].remaining > 3600 && frames++ == 0) Shot("formation");
                    if (links.All(l => l.remaining <= 3600))
                    {
                        Find.TickManager.Pause();
                        Need(Find.TickManager.TicksGame - start >= 300, "five-second warmup elapsed");
                        foreach (var link in links.Where(l => ProjectionDrones.Eligible(l.source)))
                        {
                            var hub = link.projection.TryGetComp<NivarianDroneHubComp>();
                            Need(!ProjectionVisuals.Forming(link.projection) && ProjectionDrones.CanRun(hub), "projection activated after warmup");
                            float charge = Pool(hub).Single().Value;
                            SpawnDrone.Invoke(hub, new object[] { LocalTargetInfo.Invalid });
                            var drone = Flying(hub).Single();
                            Need(drone.Home.Thing == link.projection && drone.Faction == Faction.OfPlayer, "native projected drone bound to destination hub");
                            Record("FLIGHT " + link.source.def.defName + " poolBefore=" + charge + " flight=" + drone.DroneBatteryPower + " poolAfter=" + Pool(hub).Count);
                            Need(Math.Abs(drone.DroneBatteryPower - charge) < .01f && Pool(hub).Count == 0, "flight uses transferred battery");
                            drone.DroneBatteryPower *= .5f;
                        }
                        Shot("active-drones"); stage = 2; frames = 0;
                    }
                    if (Find.TickManager.TicksGame - start > 1000) throw new Exception("formation timer stalled");
                }
                else if (stage == 2 && ++frames == 45)
                {
                    foreach (var link in links.Where(l => ProjectionDrones.Eligible(l.source)))
                    {
                        var copyHub = link.projection.TryGetComp<NivarianDroneHubComp>(); var drone = Flying(copyHub).Single(); float charge = drone.DroneBatteryPower;
                        ProjectionActions.Cancel(map, link.projection);
                        Need(drone.Destroyed && Flying(copyHub).Count == 0, "recall removes projected drone");
                        var sourceHub = link.source.TryGetComp<NivarianDroneHubComp>();
                        Need(Pool(sourceHub).Count == 1 && Math.Abs(Pool(sourceHub)[0].Value - charge) < .01f, "remaining battery returned exactly once");
                        Need(ProjectionDrones.CanRun(sourceHub), "source resumes after recall");
                    }
                    var monolith = links.Single(l => !ProjectionDrones.Eligible(l.source));
                    int hp = monolith.source.HitPoints;
                    monolith.projection.TakeDamage(new DamageInfo(DamageDefOf.Bomb, 10, 999f));
                    Need(monolith.source.HitPoints < hp, "ordinary projection still returns actual damage to source");
                    ProjectionActions.Cancel(map, monolith.projection);
                    Need(ProjectionRegistry.Current.links.Count == 0, "all links cleared");
                    // A second real cast proves battery depletion persists, then
                    // a cold state reconstruction exercises Scribe ownership.
                    CommanderState.Current.cooldowns["NMC_S19"] = 0;
                    ProjectionActions.Start(map, center + new IntVec3(9, 0, 0));
                    links = ProjectionRegistry.Current.links.ToList();
                    Need(links.Count == 4, "second cast after recall");
                    foreach (var link in links) link.remaining = 3800;
                    CommanderState.Current.cooldowns["NMC_S19"] = 420000;
                    GameDataSaveLoader.SaveGame("Projection-Formation");
                    File.WriteAllText(Path.Combine(Root, "reload"), "1");
                    done = true;
                    GameDataSaveLoader.LoadGame("Projection-Formation");
                }
            }
            catch (Exception e)
            { done = true; Record(e.ToString()); File.WriteAllText(Path.Combine(Root, "failed"), e.ToString()); Application.Quit(); }
        }
    }
}
