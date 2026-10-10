using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace NivarianCommander
{
    public static class ProjectionPacking
    {
        public static string Rejection => NmcText.Get("NMC_Text0424");
        public static bool Blocked(Thing thing) => ProjectionRegistry.IsProjection(thing?.GetInnerIfMinified());
    }

    [HarmonyPatch(typeof(Building), nameof(Building.GetGizmos))]
    public static class ProjectionPackingGizmos
    {
        static void Postfix(Building __instance, ref IEnumerable<Gizmo> __result)
        {
            if (ProjectionPacking.Blocked(__instance))
                __result = __result.Where(g => !(g is Designator_Uninstall) && !(g is Designator_Install));
        }
    }

    [HarmonyPatch(typeof(Designator_Uninstall), nameof(Designator_Uninstall.CanDesignateThing))]
    public static class ProjectionUninstallDesignation
    {
        static bool Prefix(Thing t, ref AcceptanceReport __result)
        {
            if (!ProjectionPacking.Blocked(t)) return true;
            __result = new AcceptanceReport(ProjectionPacking.Rejection); return false;
        }
    }
    [HarmonyPatch(typeof(Designator_Uninstall), nameof(Designator_Uninstall.DesignateThing))]
    public static class ProjectionUninstallDirectDesignation
    { static bool Prefix(Thing t) => !ProjectionPacking.Blocked(t); }

    [HarmonyPatch(typeof(Designator_Install), nameof(Designator_Install.CanDesignateCell))]
    public static class ProjectionReinstallDesignation
    {
        static bool Prefix(ref AcceptanceReport __result)
        {
            if (!ProjectionPacking.Blocked(Find.Selector.SingleSelectedThing)) return true;
            __result = new AcceptanceReport(ProjectionPacking.Rejection); return false;
        }
    }
    [HarmonyPatch(typeof(Designator_Install), nameof(Designator_Install.DesignateSingleCell))]
    public static class ProjectionReinstallDirectDesignation
    { static bool Prefix() => !ProjectionPacking.Blocked(Find.Selector.SingleSelectedThing); }

    [HarmonyPatch(typeof(WorkGiver_Uninstall), nameof(WorkGiver_Uninstall.HasJobOnThing))]
    public static class ProjectionUninstallWork
    {
        static bool Prefix(Thing t, ref bool __result)
        { if (!ProjectionPacking.Blocked(t)) return true; __result = false; return false; }
    }
    [HarmonyPatch(typeof(WorkGiver_RemoveBuilding), nameof(WorkGiver_RemoveBuilding.JobOnThing))]
    public static class ProjectionUninstallJob
    {
        static bool Prefix(WorkGiver_RemoveBuilding __instance, Thing t, ref Job __result)
        {
            if (!(__instance is WorkGiver_Uninstall) || !ProjectionPacking.Blocked(t)) return true;
            __result = null; return false;
        }
    }
    [HarmonyPatch(typeof(JobDriver_RemoveBuilding), nameof(JobDriver_RemoveBuilding.TryMakePreToilReservations))]
    public static class ProjectionUninstallReservation
    {
        static bool Prefix(JobDriver_RemoveBuilding __instance, ref bool __result)
        {
            if (!(__instance is JobDriver_Uninstall) || !ProjectionPacking.Blocked(__instance.job?.targetA.Thing)) return true;
            __result = false; return false;
        }
    }
    [HarmonyPatch(typeof(JobDriver_Uninstall), "FinishedRemoving")]
    public static class ProjectionUninstallCompletion
    { static bool Prefix(JobDriver_Uninstall __instance) => !ProjectionPacking.Blocked(__instance.job?.targetA.Thing); }

    [HarmonyPatch(typeof(MinifyUtility), nameof(MinifyUtility.MakeMinified))]
    public static class ProjectionNoMinifiedWrapper
    {
        static bool Prefix(Thing thing, ref MinifiedThing __result)
        { if (!ProjectionPacking.Blocked(thing)) return true; __result = null; return false; }
    }
    [HarmonyPatch(typeof(MinifyUtility), nameof(MinifyUtility.TryMakeMinified))]
    public static class ProjectionNoTryMinify
    {
        static bool Prefix(Thing thing, ref Thing __result)
        { if (!ProjectionPacking.Blocked(thing)) return true; __result = thing; return false; }
    }
    [HarmonyPatch(typeof(MinifyUtility), nameof(MinifyUtility.Uninstall))]
    public static class ProjectionNoDirectUninstall
    {
        // Guard the outer operation as well: native Uninstall otherwise passes
        // a rejected/null MakeMinified result to GenPlace and selection APIs.
        static bool Prefix(Thing th, ref MinifiedThing __result)
        { if (!ProjectionPacking.Blocked(th)) return true; __result = null; return false; }
    }

    [HarmonyPatch(typeof(CompPowerTrader), nameof(CompPowerTrader.CompInspectStringExtra))]
    public static class ProjectionSourcePowerInspection
    {
        static bool Prefix(CompPowerTrader __instance, ref string __result)
        {
            var link = ProjectionRegistry.Projected(__instance.parent);
            if (link == null) return true;
            string status = !ProjectionRegistry.ValidSource(link) ? NmcText.Get("NMC_Text0425")
                : link.remaining > 3600 ? NmcText.Get("NMC_Text0426")
                : (link.source.TryGetComp<CompPowerTrader>()?.PowerOn ?? true) ? NmcText.Get("NMC_Text0427") : NmcText.Get("NMC_Text0428");
            __result = NmcText.Get("NMC_Text0430", (link.source?.LabelCap.ToString() ?? NmcText.Get("NMC_Text0041")), (link.sourceMap?.Parent?.LabelCap.ToString() ?? NmcText.Get("NMC_Text0429")), status);
            return false;
        }
    }

    [HarmonyPatch(typeof(CompPowerTrader), "UpdateOverlays")]
    public static class ProjectionPowerOverlay
    {
        sealed class VisualState { public int value = -1; }
        static readonly ConditionalWeakTable<Thing, VisualState> states = new ConditionalWeakTable<Thing, VisualState>();
        static readonly System.Reflection.MethodInfo NativeUpdate = AccessTools.Method(typeof(CompPowerTrader), "UpdateOverlays");
        static bool Prefix(CompPowerTrader __instance)
        {
            var link = ProjectionRegistry.Projected(__instance.parent);
            // Forming is an intentional weapon gate, not an unconnected source.
            return link == null || link.remaining <= 3600;
        }
        public static void Refresh(ProjectionLink link)
        {
            var comp = link.projection.TryGetComp<CompPowerTrader>();
            if (comp == null) return;
            int value = link.remaining > 3600 ? 0 : comp.PowerOn ? 1 : 2;
            var state = states.GetValue(link.projection, _ => new VisualState());
            if (state.value == value) return;
            state.value = value;
            // UI-only refresh when forwarded power changes. Native overlays use
            // the forwarded getter; no source tick, stock, or Rand is mutated.
            NativeUpdate.Invoke(comp, null);
        }
    }
}
