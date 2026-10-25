using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace NivarianCommander
{
    public sealed partial class Building_NivarianAircraft
    {
        // Route state affects movement, so it must survive saves and MP snapshots.
        List<IntVec3> combatRoute = new List<IntVec3>();
        IntVec3 combatRouteGoal = IntVec3.Invalid;
        int combatRouteIndex, combatRouteWait;

        void ResetCombatRoute()
        {
            combatRoute.Clear(); combatRouteGoal = IntVec3.Invalid;
            combatRouteIndex = combatRouteWait = 0;
        }

        IntVec3 CombatWaypoint(IntVec3 goal)
        {
            if (!goal.IsValid) return IntVec3.Invalid;
            if (goal == Position) { ResetCombatRoute(); return goal; }
            if (combatRouteWait > 0) combatRouteWait--;
            if (combatRouteWait == 0)
            {
                combatRoute = AircraftFlightNavigation.FindRoute(this, goal);
                combatRouteGoal = goal; combatRouteIndex = 0; combatRouteWait = 60;
            }
            while (combatRouteIndex < combatRoute.Count
                && (combatRoute[combatRouteIndex].ToVector3Shifted() - flightPosition).sqrMagnitude < .0001f)
                combatRouteIndex++;
            return combatRouteIndex < combatRoute.Count ? combatRoute[combatRouteIndex] : IntVec3.Invalid;
        }

        void ExposeCombatRoute()
        {
            Scribe_Collections.Look(ref combatRoute, "nmcCombatRoute", LookMode.Value);
            Scribe_Values.Look(ref combatRouteGoal, "nmcCombatRouteGoal", IntVec3.Invalid);
            Scribe_Values.Look(ref combatRouteIndex, "nmcCombatRouteIndex");
            Scribe_Values.Look(ref combatRouteWait, "nmcCombatRouteWait");
            if (Scribe.mode == LoadSaveMode.PostLoadInit && combatRoute == null) combatRoute = new List<IntVec3>();
        }
    }

    public static class AircraftFlightNavigation
    {
        // Mountains are flyable, but an open-air step is strongly preferred.
        public const int ThickRoofCost = 40;
        public static bool InBounds(Building_NivarianAircraft ship, IntVec3 center)
        {
            if (ship?.Map == null || !center.IsValid) return false;
            foreach (var cell in GenAdj.OccupiedRect(center, ship.Rotation, ship.def.size))
                if (!cell.InBounds(ship.Map)) return false;
            return true;
        }

        public static bool ClearFootprint(Building_NivarianAircraft ship, IntVec3 center)
        {
            if (ship?.Map == null || !center.IsValid) return false;
            foreach (var cell in GenAdj.OccupiedRect(center, ship.Rotation, ship.def.size))
                if (!cell.InBounds(ship.Map) || cell.GetRoof(ship.Map)?.isThickRoof == true) return false;
            return true;
        }

        // Supercover traversal checks every crossed grid cell, including both sides of a corner.
        // Use the real sub-cell origin, so large/fast hulls cannot clip a mountain between waypoints.
        public static bool ClearSegment(Building_NivarianAircraft ship, Vector3 from, Vector3 to)
        {
            var cell = from.ToIntVec3(); var end = to.ToIntVec3();
            if (!ClearFootprint(ship, cell) || !ClearFootprint(ship, end)) return false;
            float dx = to.x - from.x, dz = to.z - from.z;
            int sx = Math.Sign(dx), sz = Math.Sign(dz);
            float stepX = sx == 0 ? float.PositiveInfinity : 1f / Mathf.Abs(dx);
            float stepZ = sz == 0 ? float.PositiveInfinity : 1f / Mathf.Abs(dz);
            float crossX = sx == 0 ? float.PositiveInfinity : (sx > 0 ? cell.x + 1 - from.x : from.x - cell.x) * stepX;
            float crossZ = sz == 0 ? float.PositiveInfinity : (sz > 0 ? cell.z + 1 - from.z : from.z - cell.z) * stepZ;
            while (cell != end)
            {
                if (cell.x == end.x) crossX = float.PositiveInfinity;
                if (cell.z == end.z) crossZ = float.PositiveInfinity;
                if (Mathf.Abs(crossX - crossZ) < .00001f)
                {
                    if (!ClearFootprint(ship, cell + new IntVec3(sx, 0, 0))
                        || !ClearFootprint(ship, cell + new IntVec3(0, 0, sz))) return false;
                    cell += new IntVec3(sx, 0, sz); crossX += stepX; crossZ += stepZ;
                }
                else if (crossX < crossZ) { cell.x += sx; crossX += stepX; }
                else { cell.z += sz; crossZ += stepZ; }
                if (!ClearFootprint(ship, cell)) return false;
            }
            return true;
        }

        public static IntVec3 SafeDestination(Building_NivarianAircraft ship, IntVec3 desired)
        {
            if (ClearFootprint(ship, desired)) return desired;
            foreach (var cell in GenRadial.RadialCellsAround(desired, 16f, true))
                if (ClearFootprint(ship, cell)) return cell;
            return InBounds(ship, desired) ? desired : ship.Position;
        }

        struct OpenCell : IComparable<OpenCell>
        {
            public int index, cost, estimate;
            public int CompareTo(OpenCell other)
            {
                int order = estimate.CompareTo(other.estimate);
                if (order == 0) order = index.CompareTo(other.index);
                return order != 0 ? order : cost.CompareTo(other.cost);
            }
        }
        static readonly IntVec3[] Neighbors = {
            new IntVec3(0, 0, 1), new IntVec3(1, 0, 0), new IntVec3(0, 0, -1), new IntVec3(-1, 0, 0)
        };
        static int Distance(IntVec3 a, IntVec3 b) => Math.Abs(a.x - b.x) + Math.Abs(a.z - b.z);

        public static List<IntVec3> FindRoute(Building_NivarianAircraft ship, IntVec3 goal)
        {
            var route = new List<IntVec3>();
            if (!InBounds(ship, ship.Position) || !InBounds(ship, goal)) return route;
            if (ClearSegment(ship, ship.flightPosition, goal.ToVector3Shifted())) { route.Add(goal); return route; }
            int width = ship.Map.Size.x, area = width * ship.Map.Size.z;
            var costs = new int[area]; var parents = new int[area]; var passable = new byte[area];
            for (int i = 0; i < area; i++) costs[i] = int.MaxValue;
            int start = ship.Position.z * width + ship.Position.x, finish = goal.z * width + goal.x;
            costs[start] = 0;
            var open = new SortedSet<OpenCell> { new OpenCell { index = start, estimate = Distance(ship.Position, goal) } };
            while (open.Count > 0)
            {
                var current = open.Min; open.Remove(current);
                if (current.cost != costs[current.index]) continue;
                if (current.index == finish)
                {
                    for (int index = finish; index != start; index = parents[index])
                        route.Add(new IntVec3(index % width, 0, index / width));
                    // Center the hull before taking the first cardinal step.
                    route.Add(ship.Position); route.Reverse(); return route;
                }
                var cell = new IntVec3(current.index % width, 0, current.index / width);
                foreach (var offset in Neighbors)
                {
                    var next = cell + offset;
                    if (!next.InBounds(ship.Map)) continue;
                    int index = next.z * width + next.x;
                    if (passable[index] == 0) passable[index] = !InBounds(ship, next) ? (byte)3
                        : ClearFootprint(ship, next) ? (byte)1 : (byte)2;
                    if (passable[index] == 3) continue;
                    int cost = current.cost + (passable[index] == 2 ? ThickRoofCost : 1);
                    if (cost >= costs[index]) continue;
                    costs[index] = cost; parents[index] = current.index;
                    open.Add(new OpenCell { index = index, cost = costs[index], estimate = costs[index] + Distance(next, goal) });
                }
            }
            // Only map bounds are impassable; even a sealed mountain has a costly route.
            return route;
        }
    }
}
