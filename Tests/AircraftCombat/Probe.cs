using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using HarmonyLib;
using NivarianCommander;
using RimWorld;
using UnityEngine;
using Verse;

namespace NMC.AircraftCombatProbe
{
    public sealed class Probe : GameComponent
    {
        static bool Enabled => GenCommandLine.CommandLineArgPassed("nmc-aircraft-combat");
        static string Root => Path.GetDirectoryName(GenFilePaths.SaveDataFolderPath);
        static readonly MethodInfo ShipTick = AccessTools.Method(typeof(Building_NivarianAircraft), "Tick");
        static readonly MethodInfo FighterTick = AccessTools.Method(typeof(CarrierInterceptor), "Tick");
        static readonly MethodInfo Move = AccessTools.Method(typeof(Building_NivarianAircraft), "MoveFlight");
        static readonly FieldInfo Intended = AccessTools.Field(typeof(Projectile), "intendedTarget");
        Map map; IntVec3 center; bool done, started, loaded; int frames;
        Building_NivarianAircraft carrier, storm, savedShip;
        Thing a, b, decoy;
        readonly List<IntVec3> roofs = new List<IntVec3>();
        public Probe(Game game) { }
        static void Record(string text) => File.AppendAllText(Path.Combine(Root, "evidence.txt"), text + "\n");
        static void Need(bool ok, string text) { Record((ok ? "PASS " : "FAIL ") + text); if (!ok) throw new Exception(text); }
        static void Invoke(MethodInfo method, object instance, params object[] args) => method.Invoke(instance, args);
        static Thing Spawn(string name, Map map, IntVec3 cell, Faction faction)
        {
            var def = ThingDef.Named(name);
            var thing = ThingMaker.MakeThing(def, def.MadeFromStuff ? ThingDefOf.Steel : null);
            if (def.CanHaveFaction) thing.SetFaction(faction);
            return GenSpawn.Spawn(thing, cell, map);
        }
        Building_NivarianAircraft Ship(string name, IntVec3 at, bool enemy = false)
        {
            var ship = (Building_NivarianAircraft)Spawn(name, map, at, enemy ? Faction.OfAncientsHostile : Faction.OfPlayer);
            ship.RefuelableComp.Refuel(10000);
            ship.manufactureEnabled = false;
            ship.flightState = AircraftFlightState.Flying;
            ship.destination = at; ship.flightPosition = at.ToVector3Shifted();
            ship.hostileAutopilot = enemy;
            return ship;
        }
        void Roof(IntVec3 at, RoofDef roof)
        { map.roofGrid.SetRoof(at, roof); if (roof != null) roofs.Add(at); }
        void ClearRoofs() { foreach (var c in roofs) map.roofGrid.SetRoof(c, null); roofs.Clear(); }
        void Setup()
        {
            map = Find.CurrentMap; center = map.Center; Find.TickManager.Pause();
            Record("GAME " + VersionControl.CurrentVersionStringWithRev + " MODS " + string.Join(",", LoadedModManager.RunningMods.Select(m => m.PackageId)));
            var asm = typeof(Building_NivarianAircraft).Assembly;
            Record("CORE " + asm.Location + " SHA256=" + BitConverter.ToString(SHA256.Create().ComputeHash(File.ReadAllBytes(asm.Location))).Replace("-", ""));
            foreach (var monolith in map.listerThings.AllThings.OfType<Building_VoidMonolith>().ToArray())
            {
                var keep = new IntVec3(12, 0, 12);
                foreach (var c in GenAdj.OccupiedRect(keep, monolith.Rotation, monolith.def.size))
                {
                    foreach (var t in c.GetThingList(map).ToArray())
                        if (t != monolith && !(t is Pawn)) { if (t.def.destroyable) t.Destroy(DestroyMode.Vanish); else t.DeSpawn(); }
                    map.terrainGrid.SetTerrain(c, TerrainDefOf.Concrete);
                }
                monolith.Position = keep;
            }
            foreach (var c in CellRect.WholeMap(map)) map.roofGrid.SetRoof(c, null);
            foreach (var c in CellRect.CenteredOn(center, 70).ClipInsideMap(map))
            {
                foreach (var t in c.GetThingList(map).ToArray())
                    if (!(t is Pawn)) { if (t.def.destroyable) t.Destroy(DestroyMode.Vanish); else t.DeSpawn(); }
                map.terrainGrid.SetTerrain(c, TerrainDefOf.Concrete); map.fogGrid.Unfog(c);
            }
            carrier = Ship("NMC_Carrier", center + new IntVec3(-30, 0, -30));
            storm = Ship("NMC_StormShip", center + new IntVec3(-30, 0, -45));
            a = Spawn("Wall", map, carrier.Position + new IntVec3(30,0,0), Faction.OfPlayer);
            b = Spawn("Wall", map, carrier.Position + new IntVec3(32,0,12), Faction.OfPlayer);
            decoy = Spawn("Turret_MiniTurret", map, carrier.Position + new IntVec3(12,0,-10), Faction.OfAncientsHostile);
            for (int i = 0; i < 4; i++) {
                var fighter = (CarrierInterceptor)ThingMaker.MakeThing(ThingDef.Named("NMC_CarrierInterceptor"));
                fighter.carrier = carrier; fighter.SetFaction(carrier.Faction); carrier.hangar.TryAdd(fighter);
            }
            Need(CarrierAttackActions.Gizmos(carrier).OfType<Command_Action>().Any(c => c.defaultLabel == "NMC_CarrierAttack".Translate()), "carrier exposes focus-fire command");
            carrier.flightState = AircraftFlightState.Grounded;
            CarrierAttackActions.SetTarget(map, carrier, a);
            Need(carrier.carrierTarget == null, "grounded carrier rejects attack orders");
            Need(CarrierAttackActions.Gizmos(carrier).First().Disabled, "grounded attack gizmo disabled");
            carrier.flightState = AircraftFlightState.Flying;
            CarrierAttackActions.SetTarget(map, carrier, carrier);
            Need(carrier.carrierTarget == null, "cannot target own carrier");
            CarrierAttackActions.SetTarget(null, carrier, a);
            Need(carrier.carrierTarget == null, "invalid source map cannot mutate orders");
            CarrierAttackActions.SetFireAtWill(map, carrier, false);
        }
        int SimulateSquadron(int ticks, Thing expected)
        {
            int shots = 0;
            for (int i = 0; i < ticks; i++)
            {
                Invoke(ShipTick, carrier);
                foreach (var fighter in carrier.deployed.ToArray()) if (fighter.Spawned) Invoke(FighterTick, fighter);
                foreach (var projectile in map.listerThings.AllThings.OfType<Projectile>().ToArray())
                {
                    if (projectile.Launcher is CarrierInterceptor)
                    {
                        if (expected != null) NeedSilent(((LocalTargetInfo)Intended.GetValue(projectile)).Thing == expected, "all real interceptor rounds target the designated object");
                        shots++;
                    }
                    projectile.Destroy(DestroyMode.Vanish);
                }
            }
            return shots;
        }
        static void NeedSilent(bool ok, string text) { if (!ok) Need(false, text); }
        void Combat()
        {
            for (int round = 0; round < 3; round++)
            {
                Thing goal = round % 2 == 0 ? a : b;
                CarrierAttackActions.SetTarget(map, carrier, goal);
                int shots = SimulateSquadron(round == 0 ? 420 : 160, goal);
                Need(carrier.deployed.Count == 4 && carrier.deployed.All(f => f.target == goal), "all four launched fighters focus selected target round=" + round);
                Need(shots > 0, "real projectile launch uses designated target despite closer hostile turret round=" + round + " shots=" + shots);
            }
            CarrierAttackActions.ClearTarget(map, carrier);
            SimulateSquadron(1, null);
            Need(carrier.deployed.All(f => f.returning), "clear target with automatic sorties off recalls fighters");
            CarrierAttackActions.SetTarget(map, carrier, b);
            SimulateSquadron(1, b);
            Need(carrier.deployed.All(f => !f.returning && f.target == b), "new order redirects airborne returning fighters immediately");
            b.Position = carrier.Position + new IntVec3(95,0,0);
            SimulateSquadron(1, null);
            Need(carrier.carrierTarget == b && CarrierAttackActions.LaunchTarget(carrier) == null && carrier.deployed.All(f => f.returning), "out-of-leash target stays locked without firing at a decoy");
            b.Position = carrier.Position + new IntVec3(40,0,10);
            SimulateSquadron(700, b);
            Need(carrier.deployed.Count == 4 && carrier.deployed.All(f => f.target == b), "returning target relaunches squadron while automatic sorties are off");
            AircraftActions.Recall(map, carrier); SimulateSquadron(700, null);
            Need(carrier.carrierTarget == null && !carrier.carrierFireAtWill && carrier.hangar.Count == 4 && carrier.deployed.Count == 0, "recall clears order and keeps recovered fighters in hangar");
            CarrierAttackActions.SetFireAtWill(map, carrier, true);
            Need(CarrierAttackActions.LaunchTarget(carrier) == decoy, "automatic sorties resume hostile target selection");
            CarrierAttackActions.SetTarget(map, carrier, a); a.Destroy(DestroyMode.Vanish); SimulateSquadron(1, null);
            Need(carrier.carrierTarget == null, "destroyed target clears persistent order");
            CarrierAttackActions.SetFireAtWill(map, carrier, false);
            CarrierAttackActions.SetTarget(map, carrier, b);
            StormShipActions.SetFireAtWill(map, storm, false);
            StormShipActions.SetTarget(map, storm, b);
            Invoke(ShipTick, storm);
            var orb = map.listerThings.AllThings.OfType<Projectile>().Single(p => p.Launcher == storm);
            Need(((LocalTargetInfo)Intended.GetValue(orb)).Thing == b && storm.volleys == 1, "storm ship fires actual orb at explicit building with automatic fire off");
            orb.Destroy(DestroyMode.Vanish);
            StormShipActions.SetFireAtWill(map, storm, true);
            b.Position = storm.Position + new IntVec3(110,0,0); storm.weaponClock = 0;
            Invoke(ShipTick, storm);
            Need(storm.stormTarget == b && storm.volleys == 1, "storm retains out-of-range lock and does not switch to closer enemy");
            b.Position = carrier.Position + new IntVec3(40,0,10);
            Need(StormShipActions.CanFireAt(storm, b), "storm can resume the same target after it re-enters range");
        }
        void Navigation()
        {
            var origin = center + new IntVec3(-22,0,25); var goal = center + new IntVec3(22,0,25);
            foreach (string name in new[] { "NMC_Carrier", "NMC_GravityFrigate", "NMC_StormShip", "NMC_SageShip", "NMC_WarpPrism" })
            {
                var ship = Ship(name, origin, true);
                for (int x = -2; x <= 2; x++) for (int z = 10; z <= 40; z++) Roof(center + new IntVec3(x,0,z), RoofDefOf.RoofRockThick);
                var route = AircraftFlightNavigation.FindRoute(ship, goal);
                Need(route.Count > 2 && route.All(c => AircraftFlightNavigation.ClearFootprint(ship, c)), name + " route detours around thick roof with full footprint clearance");
                Need(route.SequenceEqual(AircraftFlightNavigation.FindRoute(ship, goal)), name + " route tie-breaking is deterministic");
                ship.destination = goal;
                int ticks = 0;
                while ((ship.flightPosition - goal.ToVector3Shifted()).sqrMagnitude > .0001f && ticks++ < 2600)
                {
                    var previous = ship.flightPosition; Invoke(Move, ship, new object[] { null });
                    NeedSilent(AircraftFlightNavigation.ClearFootprint(ship, ship.Position), name + " must never enter thick roof");
                    NeedSilent((ship.flightPosition - previous).sqrMagnitude <= .084f, name + " must not teleport");
                }
                Need(ticks < 2600, name + " actual MoveFlight reaches goal safely ticks=" + ticks);
                ClearRoofs(); ship.Destroy(DestroyMode.Vanish);
            }
            savedShip = Ship("NMC_Carrier", origin, true);
            var edge = GenAdj.OccupiedRect(origin, savedShip.Rotation, savedShip.def.size).Cells.First(c => c != origin);
            Roof(edge, RoofDefOf.RoofRockThick);
            Need(!AircraftFlightNavigation.ClearFootprint(savedShip, origin) && AircraftFlightNavigation.InBounds(savedShip, origin), "roof touching hull edge adds cost but remains flyable");
            ClearRoofs(); Roof(edge, RoofDefOf.RoofRockThin);
            Need(AircraftFlightNavigation.ClearFootprint(savedShip, origin), "thin rock roof remains traversable in flight");
            ClearRoofs();
            Roof(origin + new IntVec3(3,0,-3), RoofDefOf.RoofRockThick);
            Need(!AircraftFlightNavigation.ClearSegment(savedShip, origin.ToVector3Shifted(), (origin + new IntVec3(5,0,5)).ToVector3Shifted()), "diagonal corner overlap is detected for route cost preference");
            ClearRoofs();
            for (int z = 0; z < map.Size.z; z++) Roof(new IntVec3(center.x,0,z), RoofDefOf.RoofRockThick);
            var mountainRoute = AircraftFlightNavigation.FindRoute(savedShip, goal);
            Need(mountainRoute.Count > 0 && mountainRoute.Any(c => !AircraftFlightNavigation.ClearFootprint(savedShip, c)), "sealed mountain barrier remains traversable when no open detour exists");
            savedShip.destination = goal; var before = savedShip.flightPosition;
            bool crossedRock = false; int crossingTicks = 0;
            while ((savedShip.flightPosition - goal.ToVector3Shifted()).sqrMagnitude > .0001f && crossingTicks++ < 2000)
            {
                Invoke(Move, savedShip, new object[] { null });
                crossedRock |= !AircraftFlightNavigation.ClearFootprint(savedShip, savedShip.Position);
            }
            Need(crossingTicks < 2000 && crossedRock, "enemy carrier actually crosses unavoidable mountain and reaches destination");
            savedShip.Position = origin; savedShip.flightPosition = origin.ToVector3Shifted();
            Invoke(AccessTools.Method(savedShip.GetType(), "ResetCombatRoute"), savedShip);
            for (int z = goal.z - 1; z <= goal.z + 1; z++) map.roofGrid.SetRoof(new IntVec3(center.x,0,z), null);
            var narrowRoute = AircraftFlightNavigation.FindRoute(savedShip, goal);
            Need(narrowRoute.Count > 0 && narrowRoute.Any(c => !AircraftFlightNavigation.ClearFootprint(savedShip, c)), "narrow gap still counts hull overlap as mountain travel instead of forbidding it");
            var player = Ship("NMC_StormShip", origin + new IntVec3(0,0,15));
            player.destination = goal + new IntVec3(0,0,15);
            bool playerCrossed = false;
            for (int i = 0; i < 850; i++) { Invoke(Move, player, new object[] { null }); playerCrossed |= !AircraftFlightNavigation.ClearFootprint(player, player.Position); }
            Need(playerCrossed && player.Position == player.destination, "player movement retains direct mountain crossing");
            player.Destroy(DestroyMode.Vanish);
            ClearRoofs();
            // Start a real detour, then persist its partially consumed route.
            for (int z = 10; z <= 40; z++) Roof(center + new IntVec3(0,0,z), RoofDefOf.RoofRockThick);
            for (int i = 0; i < 90; i++) Invoke(Move, savedShip, new object[] { null });
            Need(savedShip.flightPosition != before && AircraftFlightNavigation.ClearFootprint(savedShip, savedShip.Position), "route prefers open detour after obstruction changes");
            // AI standing goals must be legal too, not just movement waypoints.
            var pilot = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfAncientsHostile);
            savedShip.TransporterComp.innerContainer.TryAdd(pilot); savedShip.combatPilot = pilot;
            savedShip.combatRetargetWait = 0;
            AircraftHostileControl.Tick(savedShip);
            Need(AircraftFlightNavigation.ClearFootprint(savedShip, savedShip.destination), "hostile carrier selects a safe combat station");
            var blocker = savedShip.Position + new IntVec3(3,0,0);
            Roof(blocker, RoofDefOf.RoofRockThick);
            for (int i = 0; i < 80; i++)
            {
                Invoke(Move, savedShip, new object[] { null });
                NeedSilent(AircraftFlightNavigation.InBounds(savedShip, savedShip.Position), "route refresh must retain whole hull inside map bounds");
            }
            Need(true, "periodic route refresh handles changed roof costs without blocking flight");
            Find.Selector.ClearSelection(); Find.Selector.Select(carrier);
            Find.CameraDriver.JumpToCurrentMapLoc(carrier.Position);
        }
        string Snapshot()
        {
            var ships = map.listerThings.AllThings.OfType<Building_NivarianAircraft>().OrderBy(s => s.thingIDNumber);
            return string.Join("\n", ships.Select(s => s.thingIDNumber + ":" + s.carrierTarget?.thingIDNumber + ":" + s.stormTarget?.thingIDNumber
                + ":" + s.carrierFireAtWill + ":" + s.hangar.Count + ":" + string.Join(",", s.deployed.Select(f => f.thingIDNumber).OrderBy(id => id))
                + ":" + AccessTools.Field(s.GetType(), "combatRouteGoal").GetValue(s)
                + ":" + AccessTools.Field(s.GetType(), "combatRouteIndex").GetValue(s)
                + ":" + AccessTools.Field(s.GetType(), "combatRouteWait").GetValue(s)
                + ":" + string.Join(",", (List<IntVec3>)AccessTools.Field(s.GetType(), "combatRoute").GetValue(s))));
        }
        public override void LoadedGame()
        {
            if (!Enabled || !File.Exists(Path.Combine(Root, "snapshot.txt"))) return;
            Find.TickManager.Pause(); loaded = true; map = Find.CurrentMap;
            File.WriteAllText(Path.Combine(Root, "loaded-callback.txt"), Snapshot());
        }
        void Fail(Exception e) { done = true; Record(e.ToString()); File.WriteAllText(Path.Combine(Root, "failed"), e.ToString()); Application.Quit(); }
        public override void GameComponentUpdate()
        {
            if (!Enabled || done || Current.ProgramState != ProgramState.Playing || LongEventHandler.AnyEventNowOrWaiting) return;
            try
            {
                Find.TickManager.Pause();
                if (loaded)
                {
                    map = Find.CurrentMap;
                    File.WriteAllText(Path.Combine(Root, "loaded-update.txt"), Snapshot());
                    Need(Snapshot() == File.ReadAllText(Path.Combine(Root,"snapshot.txt")), "save/load restores target references, sortie mode, squadron and exact route progress");
                    done = true; File.WriteAllText(Path.Combine(Root,"complete"), "PASS"); Application.Quit(); return;
                }
                if (!started) { started = true; Setup(); Combat(); Navigation(); }
                if (++frames == 15) ScreenCapture.CaptureScreenshot(Path.Combine(Root, "carrier-orders.png"));
                if (frames < 30) return;
                File.WriteAllText(Path.Combine(Root,"snapshot.txt"), Snapshot());
                GameDataSaveLoader.SaveGame("NMC-AircraftCombat");
                done = true; GameDataSaveLoader.LoadGame("NMC-AircraftCombat");
            }
            catch (Exception e) { Fail(e); }
        }
    }
}
