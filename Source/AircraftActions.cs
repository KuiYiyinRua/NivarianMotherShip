using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Nivarian.NivarianDrones;
using RimWorld;
using UnityEngine;
using Verse;

namespace NivarianCommander
{
    public static class AircraftTargeting
    {
        static readonly Dictionary<Type, FieldInfo> flightFields = new Dictionary<Type, FieldInfo>();
        public static bool Airborne(Thing thing)
        {
            if (thing is Building_NivarianAircraft ship) return ship.Airborne;
            if (thing is CarrierInterceptor || thing is NivarianDroneBase) return thing.Spawned;
            if (thing is Pawn p && p.Spawned && p.Map.GetComponent<AircraftGravity>().Contains(p)) return true;
            // Optional support for the reference fighter's real public flight state.
            var type = thing.GetType();
            if (!flightFields.TryGetValue(type, out var field))
            {
                field = AccessTools.Field(type, "FlightState");
                flightFields[type] = field;
            }
            return field != null && field.FieldType.IsEnum && field.GetValue(thing)?.ToString() != "Grounded";
        }
        public static bool Valid(Thing source, Thing target, float range)
            => target != null && target != source && target.Spawned && !target.Destroyed && target.Map == source.Map
                && target.HostileTo(source) && !SupportDiplomacy.Friendly(target)
                && (!(target is Pawn p) || !p.DeadOrDowned && !InvisibilityUtility.IsPsychologicallyInvisible(p))
                && !target.Position.Fogged(source.Map) && !CommanderCloak.HiddenFrom(target, source)
                && source.Position.DistanceToSquared(target.Position) <= range * range;
        public static Thing Nearest(Thing source, float range, bool airOnly, bool groundPawnOnly = false)
        {
            Thing chosen = null; int distance = int.MaxValue;
            foreach (var thing in source.Map.listerThings.AllThings)
            {
                if (!(thing is Verse.AI.IAttackTarget) || !Valid(source, thing, range)) continue;
                bool flying = Airborne(thing);
                if (airOnly && !flying || groundPawnOnly && (!(thing is Pawn) || flying)) continue;
                int d = source.Position.DistanceToSquared(thing.Position);
                if (d < distance || d == distance && (chosen == null || thing.thingIDNumber < chosen.thingIDNumber))
                { chosen = thing; distance = d; }
            }
            return chosen;
        }
        public static bool LandingClear(Building_NivarianAircraft ship, IntVec3 at)
        {
            foreach (var cell in GenAdj.OccupiedRect(at, Rot4.North, ship.def.size))
            {
                if (!cell.InBounds(ship.Map) || cell.Roofed(ship.Map) || cell.Fogged(ship.Map)
                    || !cell.GetTerrain(ship.Map).affordances.Contains(TerrainAffordanceDefOf.Light)) return false;
                foreach (var thing in cell.GetThingList(ship.Map))
                    if (thing != ship && (thing is Pawn || thing.def.category == ThingCategory.Building
                        && thing.def.passability == Traversability.Impassable)) return false;
            }
            return true;
        }
    }
    public static class AircraftActions
    {
        static bool Owned(Map map, Building_NivarianAircraft ship)
            => map != null && ship != null && ship.Spawned && !ship.Destroyed && ship.Map == map && ship.Faction == Faction.OfPlayer;
        public static void Takeoff(Map map, Building_NivarianAircraft ship) { if (Owned(map, ship)) ship.BeginTakeoff(); }
        public static void Move(Map map, Building_NivarianAircraft ship, IntVec3 cell)
        {
            if (!Owned(map, ship) || !ship.CombatFlight || !cell.InBounds(map) || cell.Fogged(map)) return;
            // Keep the whole footprint inside the map during travel.
            if (!GenAdj.OccupiedRect(cell, Rot4.North, ship.def.size).Cells.All(c => c.InBounds(map))) return;
            ship.destination = cell;
        }
        public static void Land(Map map, Building_NivarianAircraft ship) { if (Owned(map, ship)) ship.BeginLanding(); }
        public static void Recall(Map map, Building_NivarianAircraft ship) { if (Owned(map, ship)) ship.RecallAll(false); }
        public static void Gravity(Map map, Building_NivarianAircraft ship, Thing target) { if (Owned(map, ship)) ship.UseGravity(target); }
        public static void ToggleManufacture(Map map, Building_NivarianAircraft ship)
        { if (Owned(map, ship) && ship.IsCarrier) ship.manufactureEnabled = !ship.manufactureEnabled; }
        public static void ToggleGravity(Map map, Building_NivarianAircraft ship)
        { if (Owned(map, ship) && !ship.IsCarrier) ship.automaticGravity = !ship.automaticGravity; }
        public static IEnumerable<Gizmo> Gizmos(Building_NivarianAircraft ship)
        {
            if (AircraftStarshipUpgrade.Available) yield return AircraftStarshipUpgrade.UpgradeGizmo(ship);
            if (!ship.Airborne)
            {
                var takeoff = Action(ship, NmcText.Get("NMC_Text0001"), ship.StarshipPowered
                    ? NmcText.Get("NMC_Text0002", ship.TakeoffFuelCost)
                    : NmcText.Get("NMC_Text0003"), () => Takeoff(ship.Map, ship));
                if (!ship.CanTakeoffWithFuel) takeoff.Disable(ship.StarshipPowered ? NmcText.Get("NMC_Text0004", ship.TakeoffFuelCost) : NmcText.Get("NMC_Text0005"));
                else if (ship.OccupiedRect().Cells.Any(c => c.Roofed(ship.Map))) takeoff.Disable(NmcText.Get("NMC_Text0006"));
                yield return takeoff;
            }
            else
            {
                var move = Action(ship, NmcText.Get("NMC_Text0007"), NmcText.Get("NMC_Text0008"), () => Find.Targeter.BeginTargeting(
                    new TargetingParameters { canTargetLocations = true, canTargetPawns = false, canTargetBuildings = false },
                    t => Move(ship.Map, ship, t.Cell)));
                if (!ship.CombatFlight) move.Disable(NmcText.Get("NMC_Text0009"));
                yield return move;
                var land = Action(ship, NmcText.Get("NMC_Text0010"), NmcText.Get("NMC_Text0011"), () => Land(ship.Map, ship));
                if (!AircraftTargeting.LandingClear(ship, ship.Position)) land.Disable(NmcText.Get("NMC_Text0012"));
                yield return land;
            }
            if (ship.IsCarrier)
            {
                yield return Action(ship, ship.manufactureEnabled ? NmcText.Get("NMC_Text0013") : NmcText.Get("NMC_Text0014"),
                    NmcText.Get("NMC_Text0015", Building_NivarianAircraft.FighterSteelCost, Building_NivarianAircraft.FighterCapacity),
                    () => ToggleManufacture(ship.Map, ship));
                yield return Action(ship, NmcText.Get("NMC_Text0016"), NmcText.Get("NMC_Text0017"), () => Recall(ship.Map, ship));
            }
            else
            {
                var gravity = Action(ship, NmcText.Get("NMC_Text0018"), NmcText.Get("NMC_Text0019"), () => Find.Targeter.BeginTargeting(
                    new TargetingParameters { canTargetPawns = true, canTargetLocations = false, canTargetBuildings = false,
                        validator = t => t.Thing is Pawn && AircraftTargeting.Valid(ship, t.Thing, 16f) && !AircraftTargeting.Airborne(t.Thing) },
                    t => Gravity(ship.Map, ship, t.Thing)));
                if (!ship.CombatFlight) gravity.Disable(NmcText.Get("NMC_Text0020"));
                else if (ship.gravityCooldown > 0) gravity.Disable(NmcText.Get("NMC_Text0021"));
                yield return gravity;
                yield return Action(ship, ship.automaticGravity ? NmcText.Get("NMC_Text0022") : NmcText.Get("NMC_Text0023"),
                    NmcText.Get("NMC_Text0024"), () => ToggleGravity(ship.Map, ship));
            }
        }
        static Command_Action Action(Building_NivarianAircraft ship, string label, string description, Action action)
            => new Command_Action { defaultLabel = label, defaultDesc = description, icon = ship.def.uiIcon, action = action };
    }
}
