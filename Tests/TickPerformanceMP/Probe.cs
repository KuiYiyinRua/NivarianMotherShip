using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using HarmonyLib;
using NivarianCommander;
using Nivarian.NivarianDrones;
using Multiplayer.API;
using Multiplayer.Client;
using Multiplayer.Common;
using Multiplayer.Common.Networking.Packet;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.AI.Group;
using Mp = Multiplayer.Client.Multiplayer;

namespace NMC.TickPerformanceMPProbe
{
    // This component and all patches are confined to the isolated probe mod.
    public sealed class State : GameComponent
    {
        public List<Building_NutrientEnergyTower> towers = new List<Building_NutrientEnergyTower>();
        public List<Building> lamps = new List<Building>();
        public List<Pawn> workers = new List<Pawn>();
        public List<Thing> targets = new List<Thing>();
        public Building monolith;
        public Pawn attackPawn;
        public int pawnHits, pawnHitsStart;
        public int phase = -1, start, phaseStart, executionTick, checkedPhase = -1, pulses, deliveries, powerStart, pulseStart;
        public State(Game game) { }
        public override void GameComponentUpdate() { if (Driver.Enabled) Driver.Update(); }
        public override void GameComponentTick() { if (Driver.Enabled) Driver.Tick(); }
        public override void ExposeData()
        {
            Scribe_Collections.Look(ref towers, "perfTowers", LookMode.Reference);
            Scribe_Collections.Look(ref lamps, "perfLamps", LookMode.Reference);
            Scribe_Collections.Look(ref workers, "perfWorkers", LookMode.Reference);
            Scribe_Collections.Look(ref targets, "perfTargets", LookMode.Reference);
            Scribe_References.Look(ref monolith, "perfMonolith");
            Scribe_References.Look(ref attackPawn, "perfAttackPawn");
            Scribe_Values.Look(ref pawnHits, "perfPawnHits"); Scribe_Values.Look(ref pawnHitsStart, "perfPawnHitsStart");
            Scribe_Values.Look(ref phase, "perfPhase", -1); Scribe_Values.Look(ref start, "perfStart");
            Scribe_Values.Look(ref phaseStart, "perfPhaseStart"); Scribe_Values.Look(ref checkedPhase, "perfChecked", -1);
            Scribe_Values.Look(ref executionTick, "perfExecutionTick");
            Scribe_Values.Look(ref pulses, "perfPulses"); Scribe_Values.Look(ref deliveries, "perfDeliveries");
            Scribe_Values.Look(ref powerStart, "perfPowerStart"); Scribe_Values.Look(ref pulseStart, "perfPulseStart");
        }
    }

    [StaticConstructorOnStartup]
    public static class Driver
    {
        public static readonly bool Enabled = GenCommandLine.CommandLineArgPassed("nmc-tick-mp");
        static readonly bool Client = GenCommandLine.CommandLineArgPassed("nmc-client"), Seed = GenCommandLine.CommandLineArgPassed("nmc-seed");
        static readonly string Root = Path.GetDirectoryName(GenFilePaths.SaveDataFolderPath);
        static readonly int Port = Arg("nmc-port", 31573), Duration = Arg("nmc-ticks", 20000);
        static readonly FieldInfo Buffer = AccessTools.Field(typeof(Building_NutrientEnergyTower), "containedNutrition");
        static readonly FieldInfo Pending = AccessTools.Field(typeof(Building_NutrientEnergyTower), "pendingPower");
        static readonly FieldInfo Production = AccessTools.Field(typeof(Building_NutrientEnergyTower), "productionTicks");
        static readonly PropertyInfo CanReceive = AccessTools.Property(typeof(Building_NutrientEnergyTower), "CanReceiveNutrition");
        static readonly WorkGiver_FillNutrientEnergyTower Giver = new WorkGiver_FillNutrientEnergyTower();
        static ISyncMethod prepare, deliver, switchPower, empty, check;
        static bool loadRequested, hosted, ready, finished, freezeSent;
        static int dispatched = -99, checkSent = -99, prepareSent = -99, checkpoint, uiReads;
        static State S => Current.Game?.GetComponent<State>();
        static Map A => S.monolith.Map;
        static CompMonolithWeapon W => S.monolith.GetComp<CompMonolithWeapon>();
        static int Now => Mp.AsyncWorldTime.worldTicks;
        static string Peer => Client ? "client" : "host";
        static string At(string name) => Path.Combine(Root, name);
        static int Arg(string name, int fallback) => GenCommandLine.TryGetCommandLineArg(name, out string v) ? int.Parse(v) : fallback;
        static string N(double n) => n.ToString("R", CultureInfo.InvariantCulture);
        static string Hash(string file) { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(file))).Replace("-", ""); }
        static void Need(bool ok, string reason) { if (!ok) throw new Exception(reason); }
        static void DiagnoseFixture(string stage)
        {
            Log.Message("NMCMP FIXTURE " + stage + " towers=" + S.towers.Count + " monolith=" + S.monolith?.ThingID);
            foreach (var t in S.towers)
            {
                Need(t != null, "fixture tower reference is null at " + stage);
                string comps = string.Join(",", t.AllComps.Select(c => c.GetType().FullName));
                Log.Message("NMCMP FIXTURE " + t.ThingID + " spawned=" + t.Spawned + " comps=" + comps + " food=" + (t.GetDirectlyHeldThings() != null));
                Need(t.Spawned, "fixture tower not spawned after load: " + t.ThingID);
                Need(t.GetComp<CompPowerTrader>() != null, "fixture power trader absent for " + t.ThingID + " comps=" + comps);
                Need(t.GetDirectlyHeldThings() != null, "fixture food container absent " + t.ThingID);
                CheckReads(t);
            }
        }
        static void Write(string file, string value) { File.WriteAllText(At(file), value); Log.Message("NMCMP " + file + " " + value); }
        static void Receipt(string value) { Log.Message("NMCMP " + value); File.AppendAllText(At(Peer + ".receipts"), value + "\n"); }
        static Driver()
        {
            if (!Enabled) return;
            var harmony = new Harmony("local.nmc.tickperformance.mp.probe");
            harmony.Patch(AccessTools.Method(typeof(Root_Entry), "Update"), postfix: new HarmonyMethod(typeof(Driver), nameof(Update)));
            harmony.Patch(AccessTools.Method(typeof(WorldGenerator), "GenerateWorld"), prefix: new HarmonyMethod(typeof(Driver), nameof(PushSeed)), finalizer: new HarmonyMethod(typeof(Driver), nameof(PopSeed)));
            harmony.Patch(AccessTools.Method(typeof(CompMonolithWeapon), nameof(CompMonolithWeapon.CompTick)),
                prefix: new HarmonyMethod(typeof(Driver), nameof(BeforePulse)), postfix: new HarmonyMethod(typeof(Driver), nameof(AfterPulse)));
            var map = new SyncType(typeof(Map)) { contextMap = true };
            prepare = MP.RegisterSyncMethod(typeof(Driver), nameof(Prepare), new SyncType[] { map });
            deliver = MP.RegisterSyncMethod(typeof(Driver), nameof(Deliver), new SyncType[] { map, typeof(bool) });
            switchPower = MP.RegisterSyncMethod(typeof(Driver), nameof(SwitchPower), new SyncType[] { map, typeof(bool) });
            empty = MP.RegisterSyncMethod(typeof(Driver), nameof(Empty), new SyncType[] { map });
            check = MP.RegisterSyncMethod(typeof(Driver), nameof(Check), new SyncType[] { map });
            Need(Buffer != null && Pending != null && Production != null && CanReceive != null, "reflection targets missing");
            var core = typeof(CompMonolithWeapon).Assembly;
            string hash = Hash(File.ReadAllText(At("core.path")).Trim());
            Need(hash == File.ReadAllText(At("core.sha256")).Trim(), "loaded candidate mismatch");
            Need(core.ManifestModule.ModuleVersionId.ToString() == File.ReadAllText(At("core.mvid")).Trim(), "loaded module differs from frozen candidate");
            Log.Message("NMCMP BOOT peer=" + Peer + " run=" + Path.GetFileName(Root) + " core=" + hash + " version=" + core.GetName().Version
                + " mvid=" + core.ManifestModule.ModuleVersionId + " config=" + Hash(Path.Combine(GenFilePaths.ConfigFolderPath, "ModsConfig.xml"))
                + " seed=" + File.ReadAllText(At("seed.sha256")).Trim());
            LongEventHandler.ExecuteWhenFinished(() =>
            {
                foreach (string name in new[] { nameof(NutrientTowerSupply.SetTarget), nameof(NutrientTowerSupply.SetAutoFill) })
                {
                    var info = Harmony.GetPatchInfo(AccessTools.Method(typeof(NutrientTowerSupply), name));
                    Need(info != null && info.Transpilers.Any(p => p.owner.Contains("multiplayer")), "native supply sync transpiler missing: " + name);
                }
                Log.Message("NMCMP REQUIRED_TARGETS resolved=9 nativeSupplySync=2 missing=0");
            });
        }
        static void PushSeed() => Rand.PushState(261021);
        static void PopSeed() => Rand.PopState();

        static T Spawn<T>(string def, IntVec3 cell, Faction faction = null, ThingDef stuff = null) where T : Thing
        { var t = (T)ThingMaker.MakeThing(ThingDef.Named(def), stuff); if (faction != null) t.SetFaction(faction); return (T)GenSpawn.Spawn(t, cell, Find.CurrentMap); }
        static void Setup()
        {
            var map = Find.CurrentMap;
            foreach (var p in map.mapPawns.AllPawnsSpawned.ToArray())
            { p.GetLord()?.Notify_PawnLost(p, PawnLostCondition.ExitedMap); p.DeSpawn(); Find.WorldPawns.PassToWorld(p, PawnDiscardDecideMode.KeepForever); }
            foreach (var p in Find.WorldPawns.AllPawnsAliveOrDead.ToArray()) p.relations?.ClearAllRelations();
            foreach (var t in map.listerThings.AllThings.ToArray()) if (t.def.destroyable) t.Destroy();
            foreach (var c in map.AllCells) { map.roofGrid.SetRoof(c, null); map.terrainGrid.SetTerrain(c, TerrainDefOf.Concrete); }
            map.fogGrid.ClearAllFog();
            var research = ResearchProjectDef.Named("Nivarian_MothershipSupportInterface");
            ((Dictionary<ResearchProjectDef, float>)AccessTools.Field(typeof(ResearchManager), "progress").GetValue(Find.ResearchManager))[research] = research.baseCost;
            for (int i = 0; i < 2; i++)
            {
                int x = 70 + i * 35;
                var tower = Spawn<Building_NutrientEnergyTower>(i == 0 ? "NMC_NutrientEnergyTower" : "NMC_ShadowEnergyTower", new IntVec3(x, 0, 80), Faction.OfPlayer);
                S.towers.Add(tower); Buffer.SetValue(tower, 2d);
                S.lamps.Add(Spawn<Building>("StandingLamp", new IntVec3(x + 4, 0, 80), Faction.OfPlayer));
                for (int z = 82; z <= (i == 0 ? 94 : 92); z++) Spawn<Building>("PowerConduit", new IntVec3(x, 0, z), Faction.OfPlayer);
                var p = PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Colonist, Faction.OfPlayer, forceGenerateNewPawn: true, canGeneratePawnRelations: false));
                GenSpawn.Spawn(p, new IntVec3(x - 4, 0, 80), map); S.workers.Add(p);
                p.playerSettings.hostilityResponse = HostilityResponseMode.Ignore;
                p.workSettings.EnableAndInitialize(); foreach (var d in DefDatabase<WorkTypeDef>.AllDefsListForReading) p.workSettings.SetPriority(d, 0);
                p.drafter.Drafted = true;
                for (int j = 0; j < 12; j++) { var rice = Spawn<Thing>("RawRice", new IntVec3(x - 7, 0, 73 + j)); rice.stackCount = rice.def.stackLimit; }
            }
            S.monolith = Spawn<Building>("NMC_HyperdimensionalMonolith", new IntVec3(105, 0, 94), Faction.OfPlayer);
            for (int i = 0; i < 16; i++) S.targets.Add(Spawn<Building>("Sandbags", new IntVec3(97 + i % 4 * 5, 0, 112 + i / 4 * 4), Faction.OfMechanoids, ThingDefOf.Cloth));
            Find.TickManager.Pause();
            GameDataSaveLoader.SaveGame("TickMPSeed");
            Write("seed.complete", "fixture saved towers=2 workers=2 targets=" + S.targets.Count + " seed=" + Find.World.info.seedString);
        }

        static double ReferenceNutrition(Building_NutrientEnergyTower t)
        { double amount = (double)Buffer.GetValue(t); foreach (var f in t.GetDirectlyHeldThings()) if (!f.IsNotFresh()) amount += f.stackCount * (double)f.GetStatValue(StatDefOf.Nutrition); return amount; }
        static void CheckReads(Building_NutrientEnergyTower t)
        {
            double stored = ReferenceNutrition(t), needed = Math.Max(0d, Math.Min(Building_NutrientEnergyTower.Capacity, t.nutritionTarget) - stored);
            bool enabled = t.Spawned && t.Faction == Faction.OfPlayer && FlickUtility.WantsToBeOn(t);
            bool permitted = enabled && !t.IsBrokenDown() && !t.IsBurning() && ResearchProjectDef.Named("Nivarian_MothershipSupportInterface").IsFinished && NativeLedger.Component != null;
            Need(t.NutritionStored == stored && t.NutritionNeeded == needed, "nutrition getter drift");
            Need((bool)CanReceive.GetValue(t) == (enabled && needed > 0), "acceptance getter drift");
            Need(t.HaulDestinationEnabled == (t.autoFillNutrition && enabled && needed > 0 && stored < Math.Min(Building_NutrientEnergyTower.Capacity, t.nutritionTarget) * .5), "haul threshold drift");
            Need(t.OperationPermitted == permitted && t.IsOperating == (permitted && stored >= t.DailyNutrition / 60000d && t.GetComp<CompPowerTrader>().PowerOn), "operating getter drift");
        }
        static List<Thing> ReferenceTargets(CompMonolithWeapon w) => w.parent.Map.listerThings.AllThings.Where(t =>
            t != w.parent && !t.Destroyed && t.Spawned && t.Map == w.parent.Map && (t is Pawn || t is Building || t is NivarianDroneBase)
            && !(t is Pawn p && p.DeadOrDowned) && t.HostileTo(w.parent.Faction ?? Faction.OfPlayer) && !SupportDiplomacy.Friendly(t)
            && t.Position.InHorDistOf(w.parent.Position, w.Props.range) && !CommanderCloak.HiddenFrom(t, w.parent)
            && GenSight.LineOfSight(w.parent.Position, t.Position, t.Map, skipFirstCell: true))
            .OrderBy(t => t.Position.DistanceToSquared(w.parent.Position)).ThenBy(t => t.thingIDNumber).Take(w.Props.maximumTargets).ToList();
        sealed class Pulse { public int before; public List<Thing> targets; public int[] hp; }
        static void BeforePulse(CompMonolithWeapon __instance, out Pulse __state)
        {
            __state = null;
            if (!Enabled || !MP.IsInMultiplayer || S?.monolith != __instance.parent || S.start == 0) return;
            int wait = __instance.weaponWait;
            bool active = SpacetimeTurretTimers.ActiveFor(__instance.parent);
            if (active != __instance.waveCadenceActive) wait = active ? Mathf.CeilToInt(wait / (float)SpacetimeWaveStats.TurretMultiplier) : Mathf.Min(__instance.Props.intervalTicks, wait * SpacetimeWaveStats.TurretMultiplier);
            if (wait > 1 || !__instance.Powered) return;
            var targets = ReferenceTargets(__instance);
            __state = new Pulse { before = __instance.roundsFired, targets = targets, hp = targets.Select(t => t.HitPoints).ToArray() };
        }
        static void AfterPulse(CompMonolithWeapon __instance, Pulse __state)
        {
            if (__state == null) return;
            try
            {
                var expected = __state.targets;
                Need(__instance.roundsFired == __state.before + (expected.Count > 0 ? 1 : 0), "pulse count differs from reference");
                if (expected.Count == 0) return;
                Need(string.Join(",", __instance.LastTargets.Select(t => t.thingIDNumber)) == string.Join(",", expected.Where(t => !t.Destroyed && t.Spawned).Select(t => t.thingIDNumber)), "ordered target mismatch");
                for (int i = 0; i < expected.Count; i++)
                {
                    var t = expected[i];
                    if (t is Building && !t.Destroyed) Need(t.HitPoints == __state.hp[i] - 3, "pulse damage mismatch " + t.ThingID);
                    if (!t.Destroyed && t.Spawned) Need(CommanderControl.Remaining(t) == __instance.Props.stunTicks, "stun mismatch " + t.ThingID);
                    if (t is Pawn p)
                    {
                        Need(p.health.hediffSet.hediffs.Any(h => h.def.defName == "Burn"), "native pawn injury absent");
                        S.pawnHits++;
                        Receipt("PAWN_HIT tick=" + Now + " pawn=" + p.ThingID + " health=" + N(p.health.summaryHealth.SummaryHealthPercent) + " stun=" + CommanderControl.Remaining(p));
                    }
                }
                S.pulses++;
                if (S.pulses <= 8 || S.pulses % 100 == 0) Receipt("PULSE tick=" + Now + " count=" + S.pulses + " targets=" + string.Join(",", expected.Select(t => t.ThingID)));
            }
            catch (Exception e) { Fail(e); }
        }

        public static void Prepare(Map map)
        {
            S.phase++; S.phaseStart = S.executionTick = Now; if (S.start == 0) { S.start = Now; S.powerStart = NativeLedger.Balance("PowerCoin"); }
            S.pulseStart = W.roundsFired;
            int action = S.phase % 8;
            for (int i = 0; i < S.towers.Count; i++)
            {
                var t = S.towers[i]; var p = S.workers[i];
                p.jobs.StopAll(); p.pather.StopDead(); p.drafter.Drafted = true;
                if (p.carryTracker.CarriedThing != null) p.carryTracker.TryDropCarriedThing(p.Position, ThingPlaceMode.Near, out var _);
                p.Position = t.Position + new IntVec3(-4, 0, 0);
                if (action <= 2)
                { t.GetDirectlyHeldThings().ClearAndDestroyContents(); Buffer.SetValue(t, action == 2 ? (1 + S.phase / 8) * .75d : .25d); }
                if (action == 1 || action == 2 || action == 6)
                {
                    foreach (var old in map.listerThings.AllThings.Where(f => f.def.defName == "RawRice" && f.Position.InHorDistOf(t.Position, 12)).OrderBy(f => f.thingIDNumber).ToArray()) old.Destroy();
                    var stock = Spawn<Thing>("RawRice", t.Position + new IntVec3(-5, 0, 0)); stock.stackCount = stock.def.stackLimit;
                }
                CheckReads(t);
            }
            // Rotating hostile/friendly and out-of-range fixtures exercise live
            // membership lists and tie ordering without changing weapon timing.
            if (action == 7)
            {
                S.targets.Add(Spawn<Building>("Granite", new IntVec3(90 + S.phase / 8 * 2, 0, 108), Faction.OfMechanoids));
                S.pawnHitsStart = S.pawnHits;
                S.attackPawn = PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDef.Named("Muffalo"), Faction.OfMechanoids, forceGenerateNewPawn: true, canGeneratePawnRelations: false));
                GenSpawn.Spawn(S.attackPawn, new IntVec3(105, 0, 105), map);
                S.attackPawn.jobs.StartJob(JobMaker.MakeJob(JobDefOf.Wait_MaintainPosture, 2000), JobCondition.InterruptForced);
                S.targets[0].SetFaction(Faction.OfPlayer);
                var moved = S.targets[1]; moved.DeSpawn(); GenSpawn.Spawn(moved, new IntVec3(200, 0, 200), map);
                map.GetComponent<SpacetimeWaveMap>().remaining = 240;
            }
            if (action == 0 && S.phase > 0)
            {
                S.targets[0].SetFaction(Faction.OfMechanoids);
                var moved = S.targets[1]; moved.DeSpawn(); GenSpawn.Spawn(moved, new IntVec3(102, 0, 112), map);
            }
            Receipt("PREPARE phase=" + S.phase + " tick=" + Now);
        }
        public static void Deliver(Map map, bool forced)
        {
            S.executionTick = Now;
            for (int i = 0; i < S.towers.Count; i++)
            {
                var t = S.towers[i]; var p = S.workers[i];
                p.drafter.Drafted = false;
                var job = Giver.JobOnThing(p, t, forced);
                Need(job != null && job.def == JobDefOf.HaulToContainer && job.count > 0, "native delivery absent: " + i + " forced=" + forced + " reserve=" + N(t.NutritionStored));
                job.playerForced = forced;
                p.jobs.StartJob(job, JobCondition.InterruptForced, resumeCurJobAfterwards: false);
                S.deliveries++;
                Receipt("DELIVER phase=" + S.phase + " tick=" + Now + " tower=" + t.ThingID + " forced=" + forced + " food=" + job.targetA.Thing.ThingID + " count=" + job.count);
            }
        }
        public static void SwitchPower(Map map, bool on)
        { S.executionTick = Now; foreach (var t in S.towers) t.GetComp<CompFlickable>().SwitchIsOn = on; Receipt("SWITCH phase=" + S.phase + " tick=" + Now + " on=" + on); }
        public static void Empty(Map map)
        { S.executionTick = Now; foreach (var t in S.towers) { t.GetDirectlyHeldThings().ClearAndDestroyContents(); Buffer.SetValue(t, 0d); } Receipt("EMPTY phase=" + S.phase + " tick=" + Now); }
        static void Dispatch()
        {
            int action = S.phase % 8;
            if (action == 0)
                foreach (var t in S.towers)
                {
                    // Commit through the real slider setters, cancel and reopen
                    // once first. The adapter, not the probe, syncs SetTarget.
                    var g = new Gizmo_NutrientTowerNutrition(t);
                    var target = AccessTools.Property(typeof(Gizmo_NutrientTowerNutrition), "Target");
                    var dragging = AccessTools.Property(typeof(Gizmo_NutrientTowerNutrition), "DraggingBar");
                    float before = t.nutritionTarget;
                    target.SetValue(g, .9f); Need(t.nutritionTarget == before, "uncommitted slider mutated state");
                    g = new Gizmo_NutrientTowerNutrition(t); dragging.SetValue(g, true); dragging.SetValue(g, false);
                    target.SetValue(g, (1 + S.phase / 8) / 10f);
                    NutrientTowerSupply.SetAutoFill(A, t, false);
                }
            if (action == 1 || action == 6) { foreach (var t in S.towers) NutrientTowerSupply.SetAutoFill(A, t, true); deliver.DoSync(null, A, false); }
            if (action == 2) { foreach (var t in S.towers) NutrientTowerSupply.SetAutoFill(A, t, false); deliver.DoSync(null, A, true); }
            if (action == 3) switchPower.DoSync(null, A, false);
            if (action == 4) switchPower.DoSync(null, A, true);
            if (action == 5) empty.DoSync(null, A);
            Log.Message("NMCMP DISPATCH phase=" + S.phase + " peer=client");
        }
        static bool Settled()
        {
            int a = S.phase % 8;
            if (Now - S.executionTick < (a == 7 ? 700 : 300)) return false;
            bool powered = S.lamps.All(l => l.GetComp<CompPowerTrader>().PowerOn) && W.Powered;
            if (a != 3 && a != 5 && (!powered || !S.towers.All(t => t.IsOperating))) return false;
            if ((a == 3 || a == 5) && (S.lamps.Any(l => l.GetComp<CompPowerTrader>().PowerOn) || W.Powered)) return false;
            if (a == 1 || a == 2 || a == 6) return S.towers.All(t => t.NutritionStored > t.nutritionTarget - .04d);
            return true;
        }
        public static void Check(Map map)
        {
            Need(Settled(), "phase not settled");
            int action = S.phase % 8;
            foreach (var t in S.towers)
            {
                CheckReads(t);
                Need(t.nutritionTarget == 1 + S.phase / 8, "slider target command failed");
                if (action == 0) Need(!t.autoFillNutrition && !t.HaulDestinationEnabled && (bool)CanReceive.GetValue(t), "auto-off versus forced acceptance");
                if (action == 1 || action == 2 || action == 6)
                    Need(t.NutritionStored > t.nutritionTarget - .04 && t.NutritionStored <= t.nutritionTarget + .051 && !t.HaulDestinationEnabled, "delivery completion/threshold mismatch");
                if (action == 3 || action == 5)
                    Need(!t.IsOperating && t.GetComp<CompPowerTrader>().PowerOutput == 0, "disabled/starved tower still generating");
                else Need(t.IsOperating && t.GetComp<CompPowerTrader>().PowerOutput == t.RatedWatts, "tower did not resume real power generation");
            }
            if (action == 3 || action == 5)
                Need(S.lamps.All(l => !l.GetComp<CompPowerTrader>().PowerOn) && !W.Powered, "downstream circuit still powered");
            else Need(S.lamps.All(l => l.GetComp<CompPowerTrader>().PowerOn) && W.Powered, "downstream circuit did not recover");
            if (action == 7)
            {
                Need(W.roundsFired > S.pulseStart && !W.LastTargets.Contains(S.targets[0]) && !W.LastTargets.Contains(S.targets[1]), "live hostile/range acquisition failed");
                Need(!W.waveCadenceActive, "wave cooldown failed to restore");
                Need(S.pawnHits > S.pawnHitsStart, "live pawn did not receive real damage/stun");
                if (!S.attackPawn.Destroyed) S.attackPawn.Destroy();
                S.attackPawn = null;
            }
            Receipt("ASSERT phase=" + S.phase + " tick=" + Now + " PASS state=" + StateText());
            S.checkedPhase = S.phase;
        }
        static string StateText() => "shots=" + W.roundsFired + ":" + W.targetsHit + ":" + W.weaponWait + ":" + S.pulses + " pawnHits=" + S.pawnHits + " deliveries=" + S.deliveries + " P=" + NativeLedger.Balance("PowerCoin")
            + " towers=" + string.Join(";", S.towers.Select(t => t.ThingID + ":" + N((double)Buffer.GetValue(t)) + ":" + N(t.NutritionStored) + ":" + N((double)Pending.GetValue(t)) + ":" + Production.GetValue(t)
                + ":" + N(t.nutritionTarget) + ":" + t.autoFillNutrition + ":" + t.OperationPermitted + ":" + t.IsOperating + ":" + N(t.GetComp<CompPowerTrader>().PowerOutput) + ":" + t.GetComp<CompPowerTrader>().PowerOn
                + ":" + string.Join(",", t.GetDirectlyHeldThings().Select(f => f.ThingID + "x" + f.stackCount))))
            + " loads=" + string.Join(";", S.lamps.Concat(new[] { S.monolith }).Select(t => t.ThingID + ":" + t.GetComp<CompPowerTrader>().PowerOn + ":" + N(t.GetComp<CompPowerTrader>().PowerOutput)))
            + " targets=" + string.Join(";", S.targets.Select(t => t.ThingID + ":" + t.HitPoints + ":" + t.Position + ":" + t.Faction?.loadID + ":" + CommanderControl.Remaining(t)));
        static string RandText(ulong value) => value + "[" + (uint)value + "," + (uint)(value >> 32) + "]";
        static string Snapshot() => "world=" + Now + ":" + RandText(Mp.AsyncWorldTime.randState)
            + " maps=" + string.Join(";", Find.Maps.OrderBy(m => m.uniqueID).Select(m => m.uniqueID + ":" + m.AsyncTime().mapTicks + ":" + RandText(m.AsyncTime().randState)))
            + " phase=" + S.phase + " " + StateText();
        public static void Tick()
        {
            if (!MP.IsInMultiplayer || S?.monolith == null || S.start == 0 || finished) return;
            try
            {
                if (Now % 250 == 0)
                {
                    foreach (var p in S.workers) foreach (var n in p.needs.AllNeeds)
                        if (n is Need_Food || n is Need_Rest || n is Need_Mood) n.CurLevel = n.MaxLevel;
                    foreach (var t in S.towers) CheckReads(t);
                    File.AppendAllText(At(Peer + ".states"), Snapshot() + "\n");
                }
                if (Now % 2500 == 0)
                {
                    // Keep the isolated soak targets alive and food available;
                    // the weapon still applies every actual damage/stun tick.
                    foreach (var target in S.targets) { Need(!target.Destroyed, "fixture target died before maintenance"); target.HitPoints = target.MaxHitPoints; }
                    if (S.checkedPhase == 23) foreach (var p in S.workers) { p.jobs.StopAll(); p.drafter.Drafted = true; }
                    foreach (var t in S.towers)
                    {
                        var broken = t.GetComp<CompBreakdownable>(); if (broken.BrokenDown) broken.Notify_Repaired();
                        if (S.phase == 23 && S.checkedPhase == 23 && t.NutritionStored < 1.4)
                        {
                            var rice = ThingMaker.MakeThing(ThingDef.Named("RawRice")); rice.stackCount = 30;
                            Need(t.GetDirectlyHeldThings().TryAdd(rice), "soak supply rejected");
                        }
                    }
                }
            }
            catch (Exception e) { Fail(e); }
        }
        static void Fail(Exception error)
        { if (finished) return; finished = true; Log.Error("NMCMP FAILED " + error); Write(Peer + ".failed", error.ToString()); }
        public static void Update()
        {
            if (!Enabled) return;
            if (File.Exists(At("quit"))) { Application.Quit(); return; }
            if (finished || LongEventHandler.AnyEventNowOrWaiting) return;
            try
            {
                if (!Client && !Seed && !loadRequested && Current.ProgramState == ProgramState.Entry)
                { loadRequested = true; GameDataSaveLoader.LoadGame("TickMPSeed"); return; }
                if (!Client && !hosted && Current.ProgramState == ProgramState.Playing)
                {
                    hosted = true;
                    if (Seed) { Setup(); finished = true; Application.Quit(); return; }
                    Need(S?.towers?.Count == 2 && S.targets.Count == 16, "invalid seed fixture");
                    DiagnoseFixture("loaded-before-host");
                    Need(HostWindow.HostProgrammatically(new ServerSettings { gameName = "NMC Tick regression", direct = true, directAddress = "127.0.0.1:" + Port,
                        lan = false, steam = false, multifaction = false, asyncTime = false, syncConfigs = false, pauseOnJoin = false,
                        pauseOnLetter = PauseOnLetter.Never, autoJoinPoint = 0, autosaveInterval = 0, debugMode = false,
                        desyncTraces = GenCommandLine.CommandLineArgPassed("nmc-trace") }), "hosting API rejected"); return;
                }
                if (Current.ProgramState != ProgramState.Playing || !MP.IsInMultiplayer || Mp.Client.State != ConnectionStateEnum.ClientPlaying || TickPatch.Simulating) return;
                Need(!Mp.session.desynced, "desynced=True");
                if (!Client && !ready && !TickPatch.serverFrozen) { Mp.Client.Send(new ClientFreezePacket(true)); return; }
                if (Client && !ready) { if (!TickPatch.Frozen) return; DiagnoseFixture("client-loaded-frozen"); ready = true; Write("client.ready", Snapshot()); }
                if (Mp.session.players.Count != 2 || !File.Exists(At("client.ready"))) return;
                if (!Client && !ready)
                {
                    Need(Snapshot() == File.ReadAllText(At("client.ready")), "BASELINE_LOAD_DRIFT"); ready = true; Write("host.ready", Snapshot());
                    Mp.Client.Send(new ClientFreezePacket(false)); Mp.Client.SendCommand(CommandType.GlobalTimeSpeed, ScheduledCommand.Global, (byte)TimeSpeed.Ultrafast);
                }
                if (TickPatch.Frozen)
                {
                    if (freezeSent || File.Exists(At("host.final")))
                    {
                        Need(S.checkedPhase == 23 && S.pulses > 30 && S.pawnHits >= 3 && S.deliveries == 18, "incomplete targeted coverage");
                        Need(NativeLedger.Balance("PowerCoin") > S.powerStart, "no native P currency credited");
                        Write(Peer + ".final", Snapshot()); Write(Peer + ".complete", "PASS desynced=False ticks=" + (Now - S.start) + " pulses=" + S.pulses + " deliveries=" + S.deliveries + " uiReads=" + uiReads); finished = true;
                    }
                    return;
                }
                if (Client)
                {
                    // Deliberately unequal UI reads warm only this peer's caches.
                    foreach (var t in S.towers) for (int i = 0; i < 10; i++) { t.GetInspectString(); var _ = t.NutritionStored + t.NutritionNeeded; var __ = t.HaulDestinationEnabled | t.IsOperating | t.OperationPermitted; uiReads++; }
                }
                if (Now >= checkpoint) { checkpoint = Now + 10000; Log.Message("NMCMP PROGRESS shared=" + (S.start == 0 ? 0 : Now - S.start) + " world=" + Now + " phase=" + S.phase + " players=2 maps=" + Find.Maps.Count + " desynced=False"); }
                if (S.checkedPhase == 23)
                { if (!Client && Now - S.start >= Duration && !freezeSent) { freezeSent = true; Mp.Client.Send(new ClientFreezePacket(true)); } return; }
                Need(S.phase < 0 || Now - S.phaseStart < 4500, "action timeout phase=" + S.phase + " jobs=" + string.Join(",", S.workers.Select(p => p.CurJob?.ToString())));
                if (!Client) return;
                if (S.phase < 0 || S.checkedPhase == S.phase)
                { if (prepareSent != S.phase) { prepareSent = S.phase; prepare.DoSync(null, A); } return; }
                if (dispatched != S.phase) { dispatched = S.phase; Dispatch(); }
                if (Settled() && checkSent != S.phase) { checkSent = S.phase; check.DoSync(null, A); }
            }
            catch (Exception e) { Fail(e); }
        }
    }
}
