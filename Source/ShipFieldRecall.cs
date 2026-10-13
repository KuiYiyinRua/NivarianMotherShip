using System;
using HarmonyLib;
using Nivarian.NivarianDrones.NivarianDroneComps;
using RimWorld;
using UnityEngine;
using Verse;

namespace NivarianCommander
{
    public static class ShipFieldRecallActions
    {
        internal static bool InFlight(Thing ship)
            => ship is FleetShip || ship is Building_NivarianAircraft aircraft && aircraft.Airborne;
        public static bool Eligible(Thing ship) => ship != null && ship.Spawned && !ship.Destroyed
            && ship.Faction == Faction.OfPlayer && (ship is Building_NivarianAircraft || ship is FleetShip);
        public static string Unavailable(Thing ship)
        {
            if (!FieldRecallActions.ResearchFinished) return NmcText.Get("NMC_Text0245");
            if (!Eligible(ship)) return NmcText.Get("NMC_Text0753");
            if (FieldWarp.Pending(ship)) return NmcText.Get("NMC_WarpBusy");
            if (!FieldEconomy.HasField) return NmcText.Get("NMC_Text0247");
            if (ship is Building_NivarianAircraft prism && prism.WarpMovementLocked) return NmcText.Get("NMC_WarpLocked");
            if (ship is FleetShip fleet && (!fleet.arrived || fleet.retiring
                || fleet.GetComp<NivarianDroneComp_Warp>() == null || FleetWarpState.Pending(fleet.GetComp<NivarianDroneComp_Warp>()))
                || ship is Building_NivarianAircraft aircraft && (aircraft.flightState == AircraftFlightState.TakingOff
                    || aircraft.flightState == AircraftFlightState.Landing)) return NmcText.Get("NMC_Text0754");
            return null;
        }
        public static Gizmo Gizmo(Thing ship)
        {
            var source = ship.Map;
            int revision = CommanderField.Current?.revision ?? -1;
            var command = new Command_FieldRecall {
                defaultLabel = NmcText.Get("NMC_Text0256"), defaultDesc = NmcText.Get("NMC_Text0755"),
                icon = ContentFinder<Texture2D>.Get("NMC/Supports/warp_energy_field"),
                action = () => Recall(source, ship, revision), alsoClickIfOtherInGroupClicked = true
            };
            var reason = Unavailable(ship);
            if (reason != null) command.Disable(reason);
            return command;
        }
        // One explicit source-map command. Destination is resolved from the shared field revision.
        public static void Recall(Map source, Thing ship, int expectedRevision)
        {
            string reason = Unavailable(ship);
            if (reason != null) { Reject(reason, ship); return; }
            if (source == null || !Find.Maps.Contains(source) || ship.Map != source)
            { Reject(NmcText.Get("NMC_Text0248"), ship); return; }
            var field = CommanderField.Current;
            if (field.revision != expectedRevision) { Reject(NmcText.Get("NMC_Text0249"), ship); return; }
            if (!FieldRecallActions.LandingCell(ship, field.fieldMap, field.center).IsValid)
            { Reject(NmcText.Get("NMC_Text0250"), ship); return; }
            FieldWarp.Begin(ship, field.fieldMap, field.center, FieldGroundGeometry.Radius, expectedRevision);
        }
        internal static bool CompleteRecall(Map source, Thing ship, int expectedRevision)
        {
            string reason = Unavailable(ship);
            if (reason != null) { Reject(reason, ship); return false; }
            if (source == null || !Find.Maps.Contains(source) || ship.Map != source)
            { Reject(NmcText.Get("NMC_Text0248"), ship); return false; }
            var field = CommanderField.Current;
            if (field.revision != expectedRevision) { Reject(NmcText.Get("NMC_Text0249"), ship); return false; }
            var destination = field.fieldMap;
            var landing = FieldRecallActions.LandingCell(ship, destination, field.center);
            if (!landing.IsValid) { Reject(NmcText.Get("NMC_Text0250"), ship); return false; }
            var origin = ship.Position;
            if (source == destination && origin == landing) { Reject(NmcText.Get("NMC_Text0251"), ship); return false; }
            var rotation = ship.Rotation;
            var aircraft = ship as Building_NivarianAircraft;
            var flight = aircraft?.flightState ?? AircraftFlightState.Grounded;
            float heading = aircraft?.heading ?? 0f;
            try
            {
                if (source == destination) { ship.Position = landing; ResetFlight(ship, landing, flight, heading); }
                else
                {
                    // Aircraft.DeSpawn recovers deployed fighters into its own saved hangar.
                    // WillReplace is the native transporter path that retains its cargo.
                    // Re-register this live hull without restarting its TransportShip job queue.
                    var transporter = aircraft?.TransporterComp as CompAircraftTransporter;
                    if (transporter != null) transporter.fieldRecallTransfer = true;
                    try { ship.DeSpawn(DestroyMode.WillReplace); }
                    finally { if (transporter != null) transporter.fieldRecallTransfer = false; }
                    ProjectionContext.Run(destination, () => {
                        SpawnTransferred(ship, landing, destination, rotation, flight, heading);
                        FieldRecallContext.AdjustShipPassengers(ship, source, destination);
                        ResetFlight(ship, landing, flight, heading);
                    });
                }
                Messages.Message(NmcText.Get("NMC_Text0253", ship.LabelCap), ship, MessageTypeDefOf.PositiveEvent, false);
                return true;
            }
            catch (Exception error)
            {
                if (!ship.Spawned && !ship.Destroyed && Find.Maps.Contains(source))
                    ProjectionContext.Run(source, () => { SpawnTransferred(ship, origin, source, rotation, flight, heading); ResetFlight(ship, origin, flight, heading); });
                Log.Error("[NMC] Ship field recall failed for " + ship.ThingID + ": " + error);
                Reject(NmcText.Get("NMC_Text0255"), ship);
                return false;
            }
        }
        static void SpawnTransferred(Thing ship, IntVec3 at, Map map, Rot4 rotation, AircraftFlightState flight, float heading)
        {
            // DeSpawn resets an aircraft to Grounded. Restore flight before any
            // destination spawn callbacks observe it, and never clear the ground below it.
            if (ship is Building_NivarianAircraft aircraft)
            { aircraft.flightState = flight; aircraft.flightPosition = at.ToVector3Shifted(); aircraft.heading = heading; }
            AirborneFieldRecallSpawn.Spawn(ship, at, map, rotation);
        }
        static void ResetFlight(Thing ship, IntVec3 at, AircraftFlightState flight, float heading)
        {
            if (ship is Building_NivarianAircraft aircraft)
            {
                aircraft.RecallAll(true);
                aircraft.flightState = flight; aircraft.flightPosition = at.ToVector3Shifted();
                aircraft.heading = heading; aircraft.destination = at;
                aircraft.beamTarget = aircraft.combatTarget = null; aircraft.beamTicks = aircraft.volleys = 0;
            }
            else if (ship is FleetShip fleet)
            {
                fleet.Home = new LocalTargetInfo(at);
                var moving = fleet.GetComp<NivarianDroneComp_MovingBase>();
                moving.ClearBehavior(); moving.PhysicPosition = at.ToVector3Shifted(); moving.Velocity = Vector3.zero;
                foreach (var gun in fleet.GetAllCompAttachTurrets())
                { gun.CurTarget = LocalTargetInfo.Invalid; gun.AttackVerb?.Reset(); }
            }
        }
        static void Reject(string reason, Thing ship) => Messages.Message(reason, ship, MessageTypeDefOf.RejectInput, false);
    }

    [HarmonyPatch(typeof(GenSpawn), nameof(GenSpawn.WipeExistingThings))]
    internal static class AirborneFieldRecallSpawn
    {
        [ThreadStatic] static Thing preservedShip;
        [ThreadStatic] static Map preservedMap;
        [ThreadStatic] static IntVec3 preservedCell;
        internal static void Spawn(Thing ship, IntVec3 at, Map map, Rot4 rotation)
        {
            var previousShip = preservedShip; var previousMap = preservedMap; var previousCell = preservedCell;
            try
            {
                preservedShip = ShipFieldRecallActions.InFlight(ship) ? ship : null;
                preservedMap = map; preservedCell = at;
                GenSpawn.Spawn(ship, at, map, rotation, WipeMode.Vanish, true);
            }
            finally { preservedShip = previousShip; preservedMap = previousMap; preservedCell = previousCell; }
        }
        static bool Prefix(IntVec3 thingPos, BuildableDef thingDef, Map map)
            => preservedShip == null || thingDef != preservedShip.def || map != preservedMap || thingPos != preservedCell;
    }

    [HarmonyPatch(typeof(GenSpawn), nameof(GenSpawn.SpawnBuildingAsPossible))]
    internal static class AirborneFieldRecallLoad
    {
        static bool Prefix(Building building, Map map, bool respawningAfterLoad)
        {
            if (!respawningAfterLoad || !(building is Building_NivarianAircraft ship) || !ship.Airborne) return true;
            // Loading a hovering hull must not refund it as an overlapping ground
            // building or remove the plants beneath it. Native SpawnSetup still runs.
            AirborneFieldRecallSpawn.Spawn(ship, ship.Position, map, ship.Rotation);
            return false;
        }
    }
}
