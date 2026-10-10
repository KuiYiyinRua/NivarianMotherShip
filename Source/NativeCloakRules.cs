using System;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace NivarianCommander
{
    // The approved rule applies to vanilla invisibility globally. Keep the public external
    // DisruptInvisibility entry, fire/downed/foam/platform checks and their recovery windows.
    [HarmonyPatch(typeof(HediffComp_Invisibility), nameof(HediffComp_Invisibility.Notify_PawnPostApplyDamage))]
    public static class NativeCloakDamageRule
    {
        static bool Prefix() => false;
    }

    [HarmonyPatch(typeof(HediffComp_Invisibility), nameof(HediffComp_Invisibility.CompPostTick))]
    public static class NativeCloakRecoveryRule
    {
        static readonly FieldInfo WasForced = AccessTools.Field(typeof(HediffComp_Invisibility), "wasForcedVisibleLastTick");
        static readonly MethodInfo Forced = AccessTools.PropertyGetter(typeof(HediffComp_Invisibility), "ForcedVisible");
        static readonly MethodInfo ShouldBeVisible = AccessTools.PropertyGetter(typeof(HediffComp_Invisibility), "ShouldBeVisible");
        static void Prefix(HediffComp_Invisibility __instance, out bool __state)
        {
            __state = (bool)WasForced.GetValue(__instance)
                && !(bool)Forced.Invoke(__instance, null)
                && !(bool)ShouldBeVisible.Invoke(__instance, null);
        }
        static void Postfix(HediffComp_Invisibility __instance, bool __state)
        {
            if (!__state) return;
            var pawn = __instance.Pawn;
            // Native tick just ended a forced-visible exception. Remove only the recovery fade;
            // voluntary BecomeVisible/BecomeInvisible transitions keep their native behavior.
            pawn.mindState.lastBecameInvisibleTick = Find.TickManager.TicksGame - Math.Max(1, __instance.Props.fadeDurationTicks);
            if (pawn.Spawned) pawn.Map.attackTargetsCache.UpdateTarget(pawn);
            pawn.Drawer?.renderer?.SetAllGraphicsDirty();
        }
    }
}
