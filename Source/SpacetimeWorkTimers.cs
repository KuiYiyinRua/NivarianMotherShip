using System;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace NivarianCommander
{
    // These two vanilla jobs use a countdown that is set once when the toil starts.
    // Accelerate the countdown while the local window is active, so entering and
    // leaving the wave mid-job works without changing the initial duration twice.
    [HarmonyPatch(typeof(JobDriver), nameof(JobDriver.DriverTick))]
    public static class SpacetimeMedicalTimer
    {
        static readonly PropertyInfo CurrentToil = AccessTools.Property(typeof(JobDriver), "CurToil")
            ?? throw new MissingMemberException("JobDriver", "CurToil");
        static void Prefix(JobDriver __instance)
        {
            if (!(__instance is JobDriver_TendPatient) || __instance.pawn == null || !SupportDiplomacy.Friendly(__instance.pawn)
                || !SpacetimeWaveMap.ActiveFor(__instance.pawn.Map)) return;
            var toil = (Toil)CurrentToil.GetValue(__instance);
            if (toil?.defaultCompleteMode != ToilCompleteMode.Delay || toil.activeSkill?.Invoke() != SkillDefOf.Medicine) return;
            __instance.ticksLeftThisToil -= SpacetimeWaveStats.WorkMultiplier - 1;
        }
    }

    [HarmonyPatch(typeof(JobDriver), nameof(JobDriver.DriverTickInterval))]
    public static class SpacetimeMiningTimer
    {
        static readonly FieldInfo Countdown = AccessTools.Field(typeof(JobDriver_Mine), "ticksToPickHit")
            ?? throw new MissingFieldException("JobDriver_Mine", "ticksToPickHit");
        static void Prefix(JobDriver __instance, int delta)
        {
            if (!(__instance is JobDriver_Mine) || __instance.pawn == null || !SupportDiplomacy.Friendly(__instance.pawn)
                || !SpacetimeWaveMap.ActiveFor(__instance.pawn.Map)) return;
            int left = (int)Countdown.GetValue(__instance);
            if (left > 0) Countdown.SetValue(__instance, left - (SpacetimeWaveStats.WorkMultiplier - 1) * delta);
        }
    }
}
