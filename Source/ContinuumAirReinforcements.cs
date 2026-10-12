using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace NivarianCommander
{
    public static class ContinuumAirReinforcements
    {
        public static int FrigateCount(int stage) => stage == 8 ? 4 : stage >= 6 && stage <= 7 ? 2 : 0;
        public static int CarrierCount(int stage) => stage == 8 ? 2 : stage == 7 ? 1 : 0;

        public static bool TrySpawn(ContinuumBattlefield state, Map map)
        {
            int frigates = FrigateCount(state.stage), carriers = CarrierCount(state.stage);
            if (frigates + carriers == 0) return true;
            var frigateDef = DefDatabase<ThingDef>.GetNamedSilentFail("NMC_GravityFrigate");
            var carrierDef = DefDatabase<ThingDef>.GetNamedSilentFail("NMC_Carrier");
            // Shuttle defs are optional when no supporting expansion is enabled.
            if (frigateDef == null || carriers > 0 && carrierDef == null) return true;
            var faction = map.ParentFaction ?? Faction.OfAncientsHostile;
            var cells = new List<IntVec3>();
            var reserved = new HashSet<IntVec3>();
            // Reserve all footprints before creating anything. A blocked arrival
            // can be retried without partially spawning and duplicating a fleet.
            for (int i = 0; i < frigates + carriers; i++)
            {
                var def = i < frigates ? frigateDef : carrierDef;
                float angle = i * Mathf.PI * 2f / (frigates + carriers);
                var anchor = new IntVec3(Mathf.RoundToInt(map.Center.x + Mathf.Cos(angle) * (map.Size.x / 2 - 10)),
                    0, Mathf.RoundToInt(map.Center.z + Mathf.Sin(angle) * (map.Size.z / 2 - 10)));
                var cell = GenRadial.RadialCellsAround(anchor, 40f, true).Concat(CellRect.WholeMap(map))
                    .Where(c => ClearFootprint(map, def, c, reserved)).DefaultIfEmpty(IntVec3.Invalid).First();
                if (!cell.IsValid) return false;
                cells.Add(cell);
                foreach (var c in GenAdj.OccupiedRect(cell, Rot4.North, def.size)) reserved.Add(c);
            }
            for (int i = 0; i < cells.Count; i++)
            {
                var ship = (Building_NivarianAircraft)ThingMaker.MakeThing(i < frigates ? frigateDef : carrierDef);
                ship.SetFaction(faction);
                ship.hostileAutopilot = true;
                ship.combatOrbitPhase = i * Mathf.PI * 2f / Math.Max(1, frigates);
                ship.autoFillDragonSteel = false;
                ship.dragonSteel = ship.IsCarrier ? 500 : 0;
                // Refuel(float) applies the difficulty multiplier; these are
                // exact onboard fuel amounts, independent of difficulty.
                ship.RefuelableComp.ConsumeFuel(ship.RefuelableComp.Fuel);
                ship.RefuelableComp.Refuel((ship.IsCarrier ? 8000f : 600f) / ship.RefuelableComp.Props.FuelMultiplierCurrentDifficulty);
                GenSpawn.Spawn(ship, cells[i], map, Rot4.North);
                var pilot = ContinuumSiteGenerator.MakeGuards(state.stage, 1, faction)[0];
                GenSpawn.Spawn(pilot, ship.Position, map);
                pilot.DeSpawn();
                ship.TransporterComp.innerContainer.TryAdd(pilot);
                // A spawned shuttle deep-saves passengers in its native cargo.
                // World registration here would save the same pawn twice.
                ship.combatPilot = pilot;
                state.defenders.Add(ship);
                state.defenders.Add(pilot);
                ship.BeginTakeoff();
            }
            return true;
        }

        static bool ClearFootprint(Map map, ThingDef def, IntVec3 center, HashSet<IntVec3> reserved)
        {
            foreach (var c in GenAdj.OccupiedRect(center, Rot4.North, def.size))
            {
                if (!c.InBounds(map) || reserved.Contains(c) || c.Roofed(map)) return false;
                foreach (var thing in c.GetThingList(map))
                    if (thing is Pawn || thing is Skyfaller || thing.def.category == ThingCategory.Building) return false;
            }
            return true;
        }
    }

    public static class AircraftHostileControl
    {
        public const float CarrierRange = 60f;
        public const float CarrierStandOff = 58f;
        public const float FrigateOrbitRadius = 11f;

        public static bool Tick(Building_NivarianAircraft ship)
        {
            if (ship.combatPilot == null || ship.combatPilot.DeadOrDowned
                || !ship.TransporterComp.innerContainer.Contains(ship.combatPilot))
            {
                ship.combatTarget = null;
                ship.RecallAll(true);
                if (ship.Airborne) ship.EmergencyLand();
                return false;
            }
            if (!ship.Airborne) ship.BeginTakeoff();
            if (!ship.CombatFlight) return true;
            float searchRange = ship.Map.Size.LengthHorizontal;
            if (ship.combatRetargetWait > 0) ship.combatRetargetWait--;
            if (ship.combatRetargetWait == 0 || !AircraftTargeting.Valid(ship, ship.combatTarget, searchRange))
            {
                ship.combatTarget = AircraftTargeting.Nearest(ship, searchRange, false);
                ship.combatRetargetWait = 30;
            }
            var target = ship.combatTarget;
            if (target == null) { ship.destination = ship.Position; return true; }
            if (ship.IsCarrier)
            {
                // Hold still inside the launch limit; reposition only when a
                // moving target exits the narrow outer-range band.
                float distance = ship.flightPosition.ToIntVec3().DistanceTo(target.Position);
                if (distance >= CarrierStandOff - 1.5f && distance <= CarrierRange - .5f)
                    ship.destination = ship.Position;
                else if (ship.combatRetargetWait == 30)
                    ship.destination = CarrierStation(ship, target);
            }
            else
            {
                ship.combatOrbitPhase = Mathf.Repeat(ship.combatOrbitPhase + .018f, Mathf.PI * 2f);
                var offset = new Vector3(Mathf.Cos(ship.combatOrbitPhase), 0f, Mathf.Sin(ship.combatOrbitPhase)) * FrigateOrbitRadius;
                ship.destination = InsideMap(ship, target.Position.ToVector3Shifted() + offset);
            }
            return true;
        }

        static IntVec3 CarrierStation(Building_NivarianAircraft ship, Thing target)
        {
            var origin = target.Position.ToVector3Shifted();
            var away = ship.flightPosition - origin;
            float angle = away.sqrMagnitude > .01f ? Mathf.Atan2(away.z, away.x) : ship.thingIDNumber * .7f;
            IntVec3 best = ship.Position;
            float bestScore = float.MaxValue;
            for (int i = 0; i < 32; i++)
            {
                float a = angle + i * Mathf.PI * 2f / 32f;
                var goal = InsideMap(ship, origin + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * CarrierStandOff);
                // Prefer an intact outer-range ring, then the closest station.
                float score = Mathf.Abs(goal.DistanceTo(target.Position) - CarrierStandOff) * 10000f
                    + goal.DistanceToSquared(ship.Position);
                if (score < bestScore) { bestScore = score; best = goal; }
            }
            return best;
        }

        static IntVec3 InsideMap(Building_NivarianAircraft ship, Vector3 goal)
        {
            int margin = Math.Max(ship.def.size.x, ship.def.size.z) / 2 + 1;
            return new IntVec3(Mathf.Clamp(Mathf.FloorToInt(goal.x), margin, ship.Map.Size.x - margin - 1),
                0, Mathf.Clamp(Mathf.FloorToInt(goal.z), margin, ship.Map.Size.z - margin - 1));
        }
    }
}
