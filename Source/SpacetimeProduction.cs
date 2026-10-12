using System;
using System.Linq;
using HarmonyLib;
using Nivarian_Race.Code.Comps.ThingComps;
using RimWorld;
using UnityEngine;
using Verse;

namespace NivarianCommander
{
    [HarmonyPatch(typeof(Bill_Autonomous), nameof(Bill_Autonomous.BillTick))]
    public static class SpacetimeAutonomousBill
    {
        static void Prefix(Bill_Autonomous __instance)
        {
            var table = __instance.billStack?.billGiver as Building;
            if (!SpacetimeBuildingWork.Applies(table) || __instance.suspended
                || __instance.State != FormingState.Forming) return;
            __instance.formingTicks -= SpacetimeWaveStats.WorkMultiplier - 1;
        }
    }

    // A gestator can query the mechanitor's recipe speed stat. Suppress the pawn
    // wave part for this one nested lookup, then multiply the completed multiplier
    // once, so the gestator and its worker never compose into 100x.
    [HarmonyPatch(typeof(Bill_Mech), "WorkSpeedMultiplier", MethodType.Getter)]
    public static class SpacetimeMechGestation
    {
        static void Prefix(out int __state)
        {
            __state = SpacetimeWaveStats.SuppressDepth;
            SpacetimeWaveStats.SuppressDepth = __state + 1;
        }
        static void Postfix(Bill_Mech __instance, ref float __result, int __state)
        {
            SpacetimeWaveStats.SuppressDepth = __state;
            var table = __instance.Gestator;
            if (SpacetimeBuildingWork.Applies(table)) __result *= SpacetimeWaveStats.WorkMultiplier;
        }
        static Exception Finalizer(Exception __exception, int __state)
        {
            SpacetimeWaveStats.SuppressDepth = __state;
            return __exception;
        }
    }

    // Production timers advance once, at their own native tick entry. We never tick
    // the building extra times: that would also repeat fuel, power and other side effects.
    [HarmonyPatch(typeof(CompSpawner), "TickIntervalDelta")]
    public static class SpacetimeAutomaticProduction
    {
        static void Prefix(CompSpawner __instance, ref int interval)
        {
            var thing = __instance.parent;
            if (SpacetimeBuildingWork.Applies(thing)) interval *= SpacetimeWaveStats.WorkMultiplier;
        }
    }

    [HarmonyPatch(typeof(Building_FermentingBarrel), nameof(Building_FermentingBarrel.TickRare))]
    public static class SpacetimeFermentation
    {
        static void Prefix(Building_FermentingBarrel __instance, out float __state) => __state = __instance.Progress;
        static void Postfix(Building_FermentingBarrel __instance, float __state)
        {
            if (!SpacetimeBuildingWork.Applies(__instance)) return;
            float earned = __instance.Progress - __state;
            if (earned > 0f) __instance.Progress = Mathf.Min(1f, __state + earned * SpacetimeWaveStats.WorkMultiplier);
        }
    }

    // The source mod's dragon egg stores a real baby. Only accelerate the native
    // hatch progress call for a player/allied baby; do not advance biological age,
    // the egg's temperature sampling or its other per-tick behavior.
    [HarmonyPatch(typeof(ThingComp_DragonEggContainer), "AdvanceHatch")]
    public static class SpacetimeDragonEggHatching
    {
        static void Prefix(ThingComp_DragonEggContainer __instance, ref int ticks)
        {
            var egg = __instance.parent;
            if (!egg.Spawned || !SpacetimeWaveMap.ActiveFor(egg.Map)) return;
            if (!__instance.InnerContainer.OfType<Pawn>().Any(p => p.Faction != null &&
                (p.Faction == Faction.OfPlayer || p.Faction.RelationKindWith(Faction.OfPlayer) == FactionRelationKind.Ally))) return;
            ticks *= SpacetimeWaveStats.WorkMultiplier;
        }
    }
}
