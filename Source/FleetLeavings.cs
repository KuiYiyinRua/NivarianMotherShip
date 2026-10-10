using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace NivarianCommander
{
    // Native GenLeaving creates and places stacks before its final notification.
    // Give temporary stacks their batch identity before native placement/merge.
    // Scope only this ship's native death; nested unrelated deaths get no owner.
    [HarmonyPatch(typeof(GenLeaving), nameof(GenLeaving.DoLeavingsFor), new[] {
        typeof(Thing), typeof(Map), typeof(DestroyMode), typeof(CellRect),
        typeof(Predicate<IntVec3>), typeof(List<Thing>) })]
    public static class FleetLeavingsScope
    {
        [ThreadStatic] public static FleetBatch Active;
        static void Prefix(Thing diedThing, DestroyMode mode, out FleetBatch __state)
        {
            __state = Active;
            Active = diedThing is FleetShip ship && !ship.retiring &&
                (mode == DestroyMode.KillFinalize || mode == DestroyMode.KillFinalizeLeavingsOnly)
                ? CommanderFleets.Current.Get(ship.batchId) : null;
        }
        static Exception Finalizer(FleetBatch __state, Exception __exception)
        { Active = __state; return __exception; }
    }
    [HarmonyPatch(typeof(ThingMaker), nameof(ThingMaker.MakeThing), new[] { typeof(ThingDef), typeof(ThingDef) })]
    public static class FleetLeavingsIdentity
    {
        static void Postfix(Thing __result)
        {
            if (FleetLeavingsScope.Active != null && __result?.def.category == ThingCategory.Item)
                CommanderFleets.Current.Track(FleetLeavingsScope.Active, __result);
        }
    }
}
