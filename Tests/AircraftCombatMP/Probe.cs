using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using HarmonyLib;
using NivarianCommander;
using Multiplayer.API;
using Multiplayer.Client;
using Multiplayer.Common;
using Multiplayer.Common.Networking.Packet;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.AI.Group;
using Mp = Multiplayer.Client.Multiplayer;

namespace NMC.AircraftCombatMPProbe
{
    public sealed class State : GameComponent
    {
        public Building_NivarianAircraft carrier, storm;
        public List<Building_NivarianAircraft> enemies = new List<Building_NivarianAircraft>();
        public List<Thing> targets = new List<Thing>();
        public List<bool> crossed = new List<bool>(), touched = new List<bool>();
        public Pawn observer;
        public int phase = -1, checkedPhase = -1, start, phaseStart, executionTick;
        public int shots, stormShots, damage, shotStart, stormStart, damageStart, routeCycles, routeStart;
        public bool sealedMountain;
        public State(Game game) { }
        public override void GameComponentUpdate() { if (Driver.Enabled) Driver.Update(); }
        public override void GameComponentTick() { if (Driver.Enabled) Driver.Tick(); }
        public override void ExposeData()
        {
            Scribe_References.Look(ref carrier, "testCarrier"); Scribe_References.Look(ref storm, "testStorm");
            Scribe_References.Look(ref observer, "testObserver");
            Scribe_Collections.Look(ref enemies, "testEnemies", LookMode.Reference);
            Scribe_Collections.Look(ref targets, "testTargets", LookMode.Reference);
            Scribe_Collections.Look(ref crossed, "testCrossed", LookMode.Value);
            Scribe_Collections.Look(ref touched, "testTouched", LookMode.Value);
            Scribe_Values.Look(ref phase, "testPhase", -1); Scribe_Values.Look(ref checkedPhase, "testChecked", -1);
            Scribe_Values.Look(ref start, "testStart"); Scribe_Values.Look(ref phaseStart, "testPhaseStart");
            Scribe_Values.Look(ref executionTick, "testExecution"); Scribe_Values.Look(ref shots, "testShots");
            Scribe_Values.Look(ref stormShots, "testStormShots"); Scribe_Values.Look(ref damage, "testDamage");
            Scribe_Values.Look(ref shotStart, "testShotStart"); Scribe_Values.Look(ref stormStart, "testStormStart");
            Scribe_Values.Look(ref damageStart, "testDamageStart"); Scribe_Values.Look(ref routeCycles, "testRouteCycles");
            Scribe_Values.Look(ref routeStart, "testRouteStart"); Scribe_Values.Look(ref sealedMountain, "testSealed");
        }
    }

    [StaticConstructorOnStartup]
    public static class Driver
    {
        public static readonly bool Enabled = GenCommandLine.CommandLineArgPassed("nmc-aircraft-mp");
        static readonly bool Client = GenCommandLine.CommandLineArgPassed("nmc-client"), Seed = GenCommandLine.CommandLineArgPassed("nmc-seed");
        static readonly string Root = Path.GetDirectoryName(GenFilePaths.SaveDataFolderPath);
        static readonly int Port = Arg("nmc-port", 31579), Duration = Arg("nmc-ticks", 20000);
        static readonly FieldInfo TargetAction = AccessTools.Field(typeof(Targeter), "action");
        static readonly FieldInfo Route = AccessTools.Field(typeof(Building_NivarianAircraft), "combatRoute");
        static readonly FieldInfo RouteIndex = AccessTools.Field(typeof(Building_NivarianAircraft), "combatRouteIndex");
        static readonly FieldInfo RouteWait = AccessTools.Field(typeof(Building_NivarianAircraft), "combatRouteWait");
        static readonly MethodInfo ResetRoute = AccessTools.Method(typeof(Building_NivarianAircraft), "ResetCombatRoute");
        static readonly FieldInfo DraftTimer = AccessTools.Field(typeof(Pawn_DraftController), "autoUndrafter");
        static ISyncMethod prepare, check;
        static bool loadRequested, hosted, ready, finished, freezeSent;
        static int dispatched = -99, checkSent = -99, prepareSent = -99, checkpoint;
        static State S => Current.Game?.GetComponent<State>();
        static Map A => S.carrier.Map;
        static int Now => Mp.AsyncWorldTime.worldTicks;
        static string Peer => Client ? "client" : "host";
        static string At(string name) => Path.Combine(Root, name);
        static int Arg(string name, int fallback) => GenCommandLine.TryGetCommandLineArg(name, out string v) ? int.Parse(v) : fallback;
        static string N(float value) => value.ToString("R", CultureInfo.InvariantCulture);
        static string Vec(Vector3 v) => N(v.x) + "," + N(v.z);
        static string Id(Thing t) => t?.ThingID ?? "null";
        static string Hash(string file) { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(file))).Replace("-", ""); }
        static void Need(bool ok, string reason) { if (!ok) throw new Exception(reason); }
        static void Write(string file, string value) { File.WriteAllText(At(file), value); Log.Message("NMCMP " + file + " " + value); }
        static void Receipt(string value) { Log.Message("NMCMP " + value); File.AppendAllText(At(Peer + ".receipts"), value + "\n"); }
        static Driver()
        {
            if (!Enabled) return;
            // Isolated endurance targets must survive real storm impacts and burst overlap.
            // Only this flag-gated probe changes their durability; weapon behavior is native.
            foreach (string name in new[] { "Wall", "Turret_MiniTurret" })
                ThingDef.Named(name).statBases.Single(s => s.stat == StatDefOf.MaxHitPoints).value = 100000;
            var harmony = new Harmony("local.nmc.aircraft.mp.probe");
            harmony.Patch(AccessTools.Method(typeof(Storyteller), nameof(Storyteller.StorytellerTick)), prefix: new HarmonyMethod(typeof(Driver), nameof(AllowStoryteller)));
            harmony.Patch(AccessTools.Method(typeof(Root_Entry), "Update"), postfix: new HarmonyMethod(typeof(Driver), nameof(Update)));
            harmony.Patch(AccessTools.Method(typeof(WorldGenerator), "GenerateWorld"), prefix: new HarmonyMethod(typeof(Driver), nameof(PushSeed)), finalizer: new HarmonyMethod(typeof(Driver), nameof(PopSeed)));
            harmony.Patch(AccessTools.Method(typeof(Projectile), "Launch", new[] { typeof(Thing), typeof(Vector3), typeof(LocalTargetInfo), typeof(LocalTargetInfo), typeof(ProjectileHitFlags), typeof(bool), typeof(Thing), typeof(ThingDef) }),
                postfix: new HarmonyMethod(typeof(Driver), nameof(Launched)));
            harmony.Patch(AccessTools.Method(typeof(Thing), nameof(Thing.TakeDamage)), postfix: new HarmonyMethod(typeof(Driver), nameof(Damaged)));
            harmony.Patch(AccessTools.Method(typeof(Pawn), nameof(Pawn.Kill)), prefix: new HarmonyMethod(typeof(Driver), nameof(PawnKilled)));
            var map = new SyncType(typeof(Map)) { contextMap = true };
            prepare = MP.RegisterSyncMethod(typeof(Driver), nameof(Prepare), new SyncType[] { map });
            check = MP.RegisterSyncMethod(typeof(Driver), nameof(Check), new SyncType[] { map });
            Need(TargetAction != null && Route != null && RouteIndex != null && RouteWait != null && ResetRoute != null && DraftTimer != null, "reflection target missing");
            var core = typeof(Building_NivarianAircraft).Assembly;
            string hash = Hash(File.ReadAllText(At("core.path")).Trim());
            Need(hash == File.ReadAllText(At("core.sha256")).Trim(), "candidate hash mismatch");
            Need(core.ManifestModule.ModuleVersionId.ToString() == File.ReadAllText(At("core.mvid")).Trim(), "INVALID_STALE_BINARY");
            Log.Message("NMCMP BOOT peer=" + Peer + " run=" + Path.GetFileName(Root) + " core=" + hash + " version=" + core.GetName().Version
                + " mvid=" + core.ManifestModule.ModuleVersionId + " config=" + Hash(Path.Combine(GenFilePaths.ConfigFolderPath, "ModsConfig.xml"))
                + " seed=" + File.ReadAllText(At("seed.sha256")).Trim());
            LongEventHandler.ExecuteWhenFinished(() =>
            {
                foreach (var type in new[] { typeof(CarrierAttackActions), typeof(StormShipActions) })
                    foreach (string name in new[] { "SetTarget", "ClearTarget", "SetFireAtWill" }) VerifySync(type, name);
                VerifySync(typeof(AircraftActions), "Recall");
                foreach (var type in new[] { typeof(CarrierAttackActions), typeof(StormShipActions) })
                    foreach (string name in new[] { "SetTarget", "ClearTarget", "SetFireAtWill" })
                        harmony.Patch(AccessTools.Method(type, name), postfix: new HarmonyMethod(typeof(Driver), nameof(Executed)));
                harmony.Patch(AccessTools.Method(typeof(AircraftActions), "Recall"), postfix: new HarmonyMethod(typeof(Driver), nameof(Executed)));
                var adapter = AppDomain.CurrentDomain.GetAssemblies().Single(a => a.GetName().Name == "NivarianCommander.StormShip.Multiplayer");
                string adapterHash = Hash(File.ReadAllText(At("adapter.path")).Trim());
                Need(adapterHash == File.ReadAllText(At("adapter.sha256")).Trim(), "adapter hash mismatch");
                Need(adapter.ManifestModule.ModuleVersionId.ToString() == File.ReadAllText(At("adapter.mvid")).Trim(), "INVALID_STALE_ADAPTER");
                Log.Message("NMCMP ADAPTER sha256=" + adapterHash + " mvid=" + adapter.ManifestModule.ModuleVersionId);
                Log.Message("NMCMP REQUIRED_TARGETS resolved=7 nativeCombatSync=7 missing=0");
            });
        }
        static void VerifySync(Type type, string name)
        {
            var info = Harmony.GetPatchInfo(AccessTools.Method(type, name));
            Need(info != null && info.Transpilers.Any(p => p.owner.Contains("multiplayer")), "native sync absent: " + type.Name + "." + name);
        }
        static void Executed(MethodBase __originalMethod, Building_NivarianAircraft ship)
        {
            if (MP.IsInMultiplayer && !MP.InInterface && S?.start > 0)
                Receipt("EXECUTED phase=" + S.phase + " tick=" + Now + " method=" + __originalMethod.DeclaringType.Name + "." + __originalMethod.Name
                    + " ship=" + Id(ship) + " target=" + Id(ship.IsCarrier ? ship.carrierTarget : ship.stormTarget));
        }
        static void PushSeed() => Rand.PushState(261025);
        static void PopSeed() => Rand.PopState();
        // Preserve the controlled battle; incidental raids/visitors invalidate its target geometry.
        static bool AllowStoryteller() => !Enabled;
        static T Spawn<T>(string name, IntVec3 cell, Faction faction) where T : Thing
        {
            var def = ThingDef.Named(name); var t = (T)ThingMaker.MakeThing(def, def.MadeFromStuff ? ThingDefOf.Steel : null);
            t.SetFaction(faction); return (T)GenSpawn.Spawn(t, cell, Find.CurrentMap);
        }
        static Building_NivarianAircraft Ship(string name, IntVec3 at, bool enemy)
        {
            var ship = Spawn<Building_NivarianAircraft>(name, at, enemy ? Faction.OfAncientsHostile : Faction.OfPlayer);
            ship.RefuelableComp.Refuel(10000); ship.manufactureEnabled = false;
            ship.carrierFireAtWill = false; ship.stormFireAtWill = false;
            ship.flightState = AircraftFlightState.Flying; ship.destination = at; ship.flightPosition = at.ToVector3Shifted();
            if (enemy)
            {
                var pilot = PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Colonist, Faction.OfAncientsHostile, forceGenerateNewPawn: true, canGeneratePawnRelations: false));
                ship.TransporterComp.innerContainer.TryAdd(pilot); ship.combatPilot = pilot;
            }
            return ship;
        }
        static void Setup()
        {
            var map = Find.CurrentMap;
            foreach (var p in map.mapPawns.AllPawnsSpawned.ToArray())
            { p.GetLord()?.Notify_PawnLost(p, PawnLostCondition.ExitedMap); p.DeSpawn(); Find.WorldPawns.PassToWorld(p, PawnDiscardDecideMode.KeepForever); }
            foreach (var p in Find.WorldPawns.AllPawnsAliveOrDead.ToArray()) p.relations?.ClearAllRelations();
            foreach (var t in map.listerThings.AllThings.ToArray()) if (t.def.destroyable) t.Destroy();
            foreach (var c in map.AllCells) { map.roofGrid.SetRoof(c, null); map.terrainGrid.SetTerrain(c, TerrainDefOf.Concrete); }
            map.fogGrid.ClearAllFog();
            S.carrier = Ship("NMC_Carrier", new IntVec3(40, 0, 40), false);
            S.storm = Ship("NMC_StormShip", new IntVec3(40, 0, 23), false);
            S.targets.Add(Spawn<Building>("Wall", new IntVec3(76, 0, 40), Faction.OfPlayer));
            S.targets.Add(Spawn<Building>("Wall", new IntVec3(76, 0, 53), Faction.OfPlayer));
            S.targets.Add(Spawn<Building>("Turret_MiniTurret", new IntVec3(54, 0, 62), Faction.OfAncientsHostile));
            S.targets.Add(Spawn<Building>("Turret_MiniTurret", new IntVec3(220, 0, 175), Faction.OfPlayer));
            for (int i = 0; i < 4; i++)
            {
                var f = (CarrierInterceptor)ThingMaker.MakeThing(ThingDef.Named("NMC_CarrierInterceptor"));
                f.carrier = S.carrier; f.SetFaction(Faction.OfPlayer); Need(S.carrier.hangar.TryAdd(f), "hangar fixture rejected");
            }
            foreach (string name in new[] { "NMC_Carrier", "NMC_GravityFrigate", "NMC_StormShip", "NMC_SageShip", "NMC_WarpPrism" })
            { S.enemies.Add(Ship(name, new IntVec3(100, 0, 155 + S.enemies.Count * 10), true)); S.crossed.Add(false); S.touched.Add(false); }
            S.observer = PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Colonist, Faction.OfPlayer, forceGenerateNewPawn: true, canGeneratePawnRelations: false));
            GenSpawn.Spawn(S.observer, new IntVec3(25, 0, 25), map); S.observer.drafter.Drafted = true;
            Find.TickManager.Pause(); GameDataSaveLoader.SaveGame("AircraftMPSeed");
            Write("seed.complete", "fixture saved carrier=4 fighters enemies=5 targets=4 seed=" + Find.World.info.seedString);
        }
        static void Launched(Projectile __instance, Thing launcher, LocalTargetInfo intendedTarget)
        {
            if (!Enabled || !MP.IsInMultiplayer || S?.start == 0) return;
            try
            {
                if (launcher is CarrierInterceptor fighter && fighter.carrier == S.carrier)
                {
                    S.shots++;
                    if (S.carrier.carrierTarget != null) Need(intendedTarget.Thing == S.carrier.carrierTarget, "fighter fired at wrong forced target");
                    if (S.shots <= 12 || S.shots % 300 == 0) Receipt("SHOT tick=" + Now + " count=" + S.shots + " fighter=" + fighter.ThingID + " target=" + Id(intendedTarget.Thing));
                }
                if (launcher == S.storm)
                { S.stormShots++; if (S.storm.stormTarget != null) Need(intendedTarget.Thing == S.storm.stormTarget, "storm wrong forced target"); }
            }
            catch (Exception e) { Fail(e); }
        }
        static void Damaged(Thing __instance, DamageWorker.DamageResult __result)
        { if (Enabled && MP.IsInMultiplayer && S?.targets != null && S.targets.Contains(__instance) && __result.totalDamageDealt > 0) S.damage++; }
        static void PawnKilled(Pawn __instance, DamageInfo? dinfo, Hediff exactCulprit)
        {
            if (!Enabled || !MP.IsInMultiplayer || S == null) return;
            if (__instance == S.observer || S.enemies.Any(s => s.combatPilot == __instance))
                Receipt("CREW_DEATH tick=" + Now + " pawn=" + Id(__instance) + " damage=" + dinfo + " culprit=" + exactCulprit
                    + " temperature=" + N(__instance.AmbientTemperature) + " hediffs=" + string.Join(",", __instance.health.hediffSet.hediffs.Select(h => h.def.defName + ":" + N(h.Severity))));
        }
        static void StartRoute(bool sealedMountain)
        {
            S.sealedMountain = sealedMountain; S.routeStart = Now;
            // Separate the native AI target from the player squadron's battle area.
            S.targets[3].Position = new IntVec3(210, 0, 195);
            for (int x = 123; x <= 127; x++) for (int z = 0; z < A.Size.z; z++)
                A.roofGrid.SetRoof(new IntVec3(x, 0, z), sealedMountain || z >= 145 && z <= 205 ? RoofDefOf.RoofRockThick : null);
            for (int i = 0; i < S.enemies.Count; i++)
            {
                var ship = S.enemies[i]; var at = new IntVec3(100, 0, 185 + i * 5);
                ship.Position = at; ship.flightPosition = at.ToVector3Shifted(); ship.destination = at;
                ship.hostileAutopilot = true; ship.flightState = AircraftFlightState.Flying; ship.combatRetargetWait = 0;
                ResetRoute.Invoke(ship, null); S.crossed[i] = S.touched[i] = false;
            }
            Receipt("ROUTE_START tick=" + Now + " sealed=" + sealedMountain + " cycle=" + S.routeCycles);
        }
        public static void Prepare(Map map)
        {
            S.phase++; S.phaseStart = S.executionTick = Now; if (S.start == 0) S.start = Now;
            if (S.phase == 0 && S.targets.Count == 4)
            {
                Receipt("FIXTURE_CLIMATE original=" + N(A.mapTemperature.OutdoorTemp) + " controlled=21");
                A.Biome.constantOutdoorTemperature = 21f;
                // Keep the storm impact area away from fighters circling the carrier targets.
                S.targets.Add(Spawn<Building>("Wall", new IntVec3(76, 0, 12), Faction.OfPlayer));
                S.targets.Add(Spawn<Building>("Wall", new IntVec3(90, 0, 23), Faction.OfPlayer));
            }
            S.shotStart = S.shots; S.stormStart = S.stormShots; S.damageStart = S.damage;
            if (S.phase % 8 == 7) StartRoute(S.phase / 8 == 1);
            Receipt("PREPARE phase=" + S.phase + " tick=" + Now);
        }
        static void Toggle(Building_NivarianAircraft ship, bool value, bool carrier)
        {
            if ((carrier ? ship.carrierFireAtWill : ship.stormFireAtWill) == value) return;
            var commands = carrier ? CarrierAttackActions.Gizmos(ship) : StormShipActions.Gizmos(ship);
            commands.OfType<Command_Toggle>().Single().toggleAction();
        }
        static void Focus(Building_NivarianAircraft ship, Thing target, bool carrier)
        {
            var commands = carrier ? CarrierAttackActions.Gizmos(ship) : StormShipActions.Gizmos(ship);
            var cmd = commands.OfType<Command_Action>().First(); Need(!cmd.Disabled, "focus gizmo disabled");
            var previous = carrier ? ship.carrierTarget : ship.stormTarget;
            cmd.action(); Find.Targeter.StopTargeting();
            Need((carrier ? ship.carrierTarget : ship.stormTarget) == previous, "cancel mutated target");
            cmd.action(); var callback = (Action<LocalTargetInfo>)TargetAction.GetValue(Find.Targeter);
            Need(callback != null, "target callback missing"); callback(new LocalTargetInfo(target)); Find.Targeter.StopTargeting();
        }
        static void Clear(Building_NivarianAircraft ship, bool carrier)
        {
            var commands = carrier ? CarrierAttackActions.Gizmos(ship) : StormShipActions.Gizmos(ship);
            commands.OfType<Command_Action>().Single(c => c.defaultLabel == "NMC_StormClearTarget".Translate()).action();
        }
        static void Dispatch()
        {
            int action = S.phase % 8;
            if (action == 0 || action == 1 || action == 3)
            {
                Toggle(S.carrier, false, true); Toggle(S.storm, false, false);
                var target = S.targets[action == 1 ? 1 : 0]; Focus(S.carrier, target, true); Focus(S.storm, S.targets[action == 1 ? 5 : 4], false);
            }
            if (action == 2) { Clear(S.carrier, true); Clear(S.storm, false); }
            if (action == 4) AircraftActions.Gizmos(S.carrier).OfType<Command_Action>().Single(c => c.defaultLabel == "NMC_Text0016".Translate()).action();
            if (action == 5) { Toggle(S.carrier, true, true); Toggle(S.storm, true, false); }
            if (action == 6) { Toggle(S.carrier, false, true); Toggle(S.storm, false, false); Clear(S.storm, false); }
            if (action == 7) { Focus(S.carrier, S.targets[0], true); Focus(S.storm, S.targets[4], false); }
            Log.Message("NMCMP DISPATCH phase=" + S.phase + " peer=client path=real-gizmo-targeter-callback");
        }
        static bool Settled()
        {
            int a = S.phase % 8; if (Now - S.phaseStart < (a == 7 ? 1800 : 650)) return false;
            if (a == 7) return S.crossed.All(v => v);
            if (a == 0 || a == 1 || a == 3)
            { var target = S.targets[a == 1 ? 1 : 0]; return S.carrier.deployed.Count == 4 && S.carrier.deployed.All(f => f.target == target && !f.returning) && S.shots > S.shotStart && S.stormShots > S.stormStart; }
            if (a == 5) return S.carrier.deployed.Count == 4 && S.carrier.deployed.All(f => f.target == S.targets[2] && !f.returning) && S.shots > S.shotStart;
            return S.carrier.hangar.Count == 4 && S.carrier.deployed.Count == 0;
        }
        static void CheckRoute()
        {
            for (int i = 0; i < S.enemies.Count; i++)
            {
                var ship = S.enemies[i]; Need(S.crossed[i], "enemy did not cross barrier: " + ship.def.defName);
                Need(S.touched[i] == S.sealedMountain, "mountain soft preference incorrect: " + ship.def.defName + " sealed=" + S.sealedMountain + " touched=" + S.touched[i]);
                Need(ship.combatTarget == S.targets[3], "native AI target mismatch");
                Need(AircraftFlightNavigation.ClearFootprint(ship, ship.destination), "native AI station/orbit ended under roof");
            }
        }
        public static void Check(Map map)
        {
            Need(Settled(), "phase not settled"); int a = S.phase % 8;
            if (a == 0 || a == 1 || a == 3)
            {
                var target = S.targets[a == 1 ? 1 : 0];
                Need(S.carrier.carrierTarget == target && S.storm.stormTarget == S.targets[a == 1 ? 5 : 4], "forced order mismatch");
                Need(!S.carrier.carrierFireAtWill && !S.storm.stormFireAtWill, "manual attack requires auto off");
                Need(S.damage > S.damageStart, "real projectiles did not damage fixture targets");
            }
            if (a == 2 || a == 4 || a == 6) Need(S.carrier.carrierTarget == null && !S.carrier.carrierFireAtWill, "clear/recall left stale attack order");
            if (a == 5) Need(S.carrier.carrierTarget == null && S.carrier.carrierFireAtWill, "automatic sorties failed");
            if (a == 7) { CheckRoute(); S.routeCycles++; }
            S.checkedPhase = S.phase; Receipt("ASSERT phase=" + S.phase + " tick=" + Now + " PASS state=" + StateText());
        }
        static string ShipText(Building_NivarianAircraft t) => Id(t) + ":" + Vec(t.flightPosition) + ":" + t.destination + ":" + N(t.heading)
            + ":" + Id(t.carrierTarget) + ":" + t.carrierFireAtWill + ":" + Id(t.stormTarget) + ":" + t.stormFireAtWill
            + ":" + Id(t.combatTarget) + ":" + t.combatRetargetWait + ":" + N(t.combatOrbitPhase) + ":" + t.weaponClock + ":" + t.launchClock
            + ":" + RouteIndex.GetValue(t) + ":" + RouteWait.GetValue(t) + ":" + string.Join(",", (List<IntVec3>)Route.GetValue(t));
        static string StateText() => "shots=" + S.shots + ":" + S.stormShots + " damage=" + S.damage + " routes=" + S.routeCycles + ":" + S.sealedMountain
            + " observer=" + Id(S.observer) + ":" + S.observer.Position + ":" + S.observer.Drafted + ":" + S.observer.Dead
            + " carrier=" + ShipText(S.carrier) + " storm=" + ShipText(S.storm)
            + " hangar=" + string.Join(",", S.carrier.hangar.Cast<Thing>().Select(Id))
            + " fighters=" + string.Join(";", S.carrier.deployed.OrderBy(f => f.thingIDNumber).Select(f => Id(f) + ":" + Id(f.target) + ":" + Vec(f.realPosition) + ":" + N(f.orbitPhase) + ":" + f.returning + ":" + f.fireWait + ":" + f.burstLeft))
            + " enemies=" + string.Join(";", S.enemies.Select(ShipText)) + " crossed=" + string.Join(",", S.crossed) + " touched=" + string.Join(",", S.touched)
            + " hp=" + string.Join(",", S.targets.Select(t => Id(t) + ":" + t.HitPoints));
        static string RandText(ulong value) => value + "[" + (uint)value + "," + (uint)(value >> 32) + "]";
        static string Snapshot() => "world=" + Now + ":" + RandText(Mp.AsyncWorldTime.randState)
            + " maps=" + string.Join(";", Find.Maps.OrderBy(m => m.uniqueID).Select(m => m.uniqueID + ":" + m.AsyncTime().mapTicks + ":" + RandText(m.AsyncTime().randState)))
            + " phase=" + S.phase + " " + StateText();
        public static void Tick()
        {
            if (!MP.IsInMultiplayer || S?.carrier == null || S.start == 0 || finished) return;
            try
            {
                foreach (var target in S.targets) { Need(!target.Destroyed && target.Spawned, "fixture target lost " + Id(target) + " hp=" + target.HitPoints + " tick=" + Now); target.HitPoints = target.MaxHitPoints; }
                for (int i = 0; i < S.enemies.Count; i++) if (S.enemies[i].hostileAutopilot)
                {
                    var ship = S.enemies[i]; S.crossed[i] |= ship.Position.x >= 138;
                    S.touched[i] |= !AircraftFlightNavigation.ClearFootprint(ship, ship.Position);
                    Need(S.sealedMountain || !S.touched[i], "enemy entered avoidable thick roof: " + ship.def.defName);
                }
                if (Now % 250 == 0)
                {
                    // Vanilla automatically undrafts an idle observer after 10,000 ticks.
                    // Keep this noncombat fixture pawn from taking repair jobs in the impact area.
                    Need(S.observer.Spawned && S.observer.Position == new IntVec3(25, 0, 25), "fixture observer left station");
                    S.observer.drafter.Drafted = true;
                    ((AutoUndrafter)DraftTimer.GetValue(S.observer.drafter)).Notify_Drafted();
                    foreach (var ship in S.enemies.Concat(new[] { S.carrier, S.storm })) ship.RefuelableComp.Refuel(10000);
                    foreach (var pawn in S.enemies.Select(s => s.combatPilot).Concat(new[] { S.observer }))
                    {
                        Need(pawn != null && !pawn.Dead && pawn.needs != null, "fixture crew lost " + Id(pawn));
                        foreach (var need in pawn.needs.AllNeeds) need.CurLevel = need.MaxLevel;
                    }
                    File.AppendAllText(At(Peer + ".states"), Snapshot() + "\n");
                }
                if (S.checkedPhase == 23 && Now - S.routeStart >= 4000)
                { CheckRoute(); S.routeCycles++; StartRoute(!S.sealedMountain); }
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
                { loadRequested = true; GameDataSaveLoader.LoadGame("AircraftMPSeed"); return; }
                if (!Client && !hosted && Current.ProgramState == ProgramState.Playing)
                {
                    hosted = true;
                    if (Seed) { Setup(); finished = true; Application.Quit(); return; }
                    Need(S?.carrier != null && S.enemies.Count == 5 && S.targets.Count == 4, "invalid seed fixture");
                    Need(HostWindow.HostProgrammatically(new ServerSettings { gameName = "NMC Aircraft regression", direct = true, directAddress = "127.0.0.1:" + Port,
                        lan = false, steam = false, multifaction = false, asyncTime = false, syncConfigs = false, pauseOnJoin = false,
                        pauseOnLetter = PauseOnLetter.Never, autoJoinPoint = 0, autosaveInterval = 0, debugMode = false,
                        desyncTraces = GenCommandLine.CommandLineArgPassed("nmc-trace") }), "hosting API rejected"); return;
                }
                if (Current.ProgramState != ProgramState.Playing || !MP.IsInMultiplayer || Mp.Client.State != ConnectionStateEnum.ClientPlaying || TickPatch.Simulating) return;
                Need(!Mp.session.desynced, "desynced=True");
                if (!Client && !ready && !TickPatch.serverFrozen) { Mp.Client.Send(new ClientFreezePacket(true)); return; }
                if (Client && !ready) { if (!TickPatch.Frozen) return; ready = true; Write("client.ready", Snapshot()); }
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
                        Need(S.checkedPhase == 23 && S.routeCycles >= 3 && S.shots > 100 && S.stormShots > 9 && S.damage > 30, "incomplete targeted coverage");
                        Write(Peer + ".final", Snapshot()); Write(Peer + ".complete", "PASS desynced=False ticks=" + (Now - S.start) + " shots=" + S.shots + " stormShots=" + S.stormShots + " damage=" + S.damage + " routeCycles=" + S.routeCycles); finished = true;
                    }
                    return;
                }
                if (Client) { S.carrier.GetInspectString(); S.storm.GetInspectString(); foreach (var ship in S.enemies) ship.GetInspectString(); }
                if (Now >= checkpoint) { checkpoint = Now + 10000; Log.Message("NMCMP PROGRESS shared=" + (S.start == 0 ? 0 : Now - S.start) + " world=" + Now + " phase=" + S.phase + " players=2 maps=" + Find.Maps.Count + " desynced=False"); }
                if (S.checkedPhase == 23)
                { if (!Client && Now - S.start >= Duration && !freezeSent) { freezeSent = true; Mp.Client.Send(new ClientFreezePacket(true)); } return; }
                Need(S.phase < 0 || Now - S.phaseStart < 6000, "action timeout phase=" + S.phase);
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
