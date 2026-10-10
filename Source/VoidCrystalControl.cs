using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace NivarianCommander
{
    public static class VoidCrystalCombat
    {
        public static bool Combat(Verb verb)
            => verb is IAbilityVerb ability ? ability.Ability?.def.hostile == true
                : verb?.verbProps?.violent == true;
        public static bool Progress(Thing actor)
        {
            if (!VoidCrystalMap.Affected(actor)) return true;
            if (actor is Building) return false;
            int clock = GenTicks.TicksGame;
            // When S11 permits only 1% of a non-Pawn's combat ticks, apply the
            // different-source 30% on that progress ordinal, not on phase zero
            // of every hundred ticks. No independent second compensation owner.
            if (!(actor is Pawn) && TimeStopMap.Controlled(actor))
                clock = (TimeStopMap.Duration - actor.Map.GetComponent<TimeStopMap>().remaining) / 100;
            return (clock + actor.thingIDNumber) % 10 < 3;
        }
        public static bool VerbProgress(Verb verb)
            => !Combat(verb) || Progress(verb.Caster);
        public static bool TurretResetLocked(Thing actor)
            => TimeStopMap.Controlled(actor) || VoidCrystalMap.Affected(actor);
    }

    [HarmonyPatch(typeof(Pawn_PathFollower), "CostToPayThisTick")]
    public static class VoidCrystalMovement
    {
        [HarmonyPriority(Priority.Last)]
        static void Postfix(Pawn ___pawn, ref float __result)
        {
            // Multiply the native movement progress after its own flying,
            // stagger and floor calculations. Don't also scale MoveSpeed or
            // path cost: doing both would apply the same crystal twice.
            if (VoidCrystalMap.Affected(___pawn)) __result *= .3f;
        }
    }

    [HarmonyPatch(typeof(Stance_Busy), nameof(Stance_Busy.StanceTick))]
    public static class VoidCrystalAttackStance
    {
        static bool Prefix(Stance_Busy __instance)
            => !(__instance is Stance_Warmup || __instance is Stance_Cooldown)
                || !VoidCrystalCombat.Combat(__instance.verb)
                || VoidCrystalCombat.Progress(__instance.stanceTracker.pawn);
    }

    [HarmonyPatch(typeof(Ability), "CooldownTick")]
    public static class VoidCrystalHostileAbilityCooldown
    {
        static readonly AccessTools.FieldRef<Ability, int> End
            = AccessTools.FieldRefAccess<Ability, int>("cooldownEndTick");
        static void Prefix(Ability __instance, bool ___inCooldown)
        {
            // AbilityTick, visual effects and all ability components continue.
            // Only native, hostile active-ability deadlines are compensated.
            // Global commander cooldowns and non-hostile abilities are separate.
            if (___inCooldown && __instance.def.hostile && End(__instance) > GenTicks.TicksGame
                && !VoidCrystalCombat.Progress(__instance.pawn)) End(__instance)++;
        }
    }
}
