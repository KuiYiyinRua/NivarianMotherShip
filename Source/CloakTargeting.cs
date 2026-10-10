using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Nivarian_Race.Code.Comps.ThingComps;
using Nivarian.NivarianDrones.NivarianDroneTargetValidator;
using RimWorld;
using Verse;
using Verse.AI;

namespace NivarianCommander
{
    [HarmonyPatch]
    public static class CommanderCloakDroneValidator
    {
        static IEnumerable<MethodBase> TargetMethods()
            => typeof(NivarianDroneSearchBase).Assembly.GetTypes()
                .Where(t => typeof(NivarianDroneSearchBase).IsAssignableFrom(t))
                .Select(t => AccessTools.DeclaredMethod(t, nameof(NivarianDroneSearchBase.Validator),
                    new[] { typeof(LocalTargetInfo), typeof(Thing), typeof(float) })).Where(m => m != null);
        static void Postfix([HarmonyArgument(0)] LocalTargetInfo target, [HarmonyArgument(1)] Thing source, ref bool __result)
        { if (target.HasThing && CommanderCloak.HiddenFrom(target.Thing, source)) __result = false; }
    }
    [HarmonyPatch(typeof(AttackTargetFinder), nameof(AttackTargetFinder.BestAttackTarget))]
    public static class CommanderCloakScan
    {
        static void Prefix(IAttackTargetSearcher searcher, ref Predicate<Thing> validator)
        {
            var previous = validator;
            validator = target => !CommanderCloak.HiddenFrom(target, searcher.Thing) && (previous == null || previous(target));
        }
    }
    [HarmonyPatch(typeof(Building_Turret), nameof(Building_Turret.ThreatDisabled))]
    public static class CommanderCloakTurretThreat
    {
        static void Postfix(Building_Turret __instance, IAttackTargetSearcher disabledFor, ref bool __result)
        { if (CommanderCloak.HiddenFrom(__instance, disabledFor?.Thing)) __result = true; }
    }
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.ThreatDisabled))]
    public static class CommanderCloakPawnThreat
    {
        static void Postfix(Pawn __instance, IAttackTargetSearcher disabledFor, ref bool __result)
        { if (CommanderCloak.HiddenFrom(__instance, disabledFor?.Thing)) __result = true; }
    }
    [HarmonyPatch(typeof(CompAttachTurret), nameof(CompAttachTurret.TargetValid))]
    public static class CommanderCloakAttachedTarget
    {
        static void Postfix(CompAttachTurret __instance, LocalTargetInfo target, ref bool __result)
        { if (target.HasThing && CommanderCloak.HiddenFrom(target.Thing, __instance.parent)) __result = false; }
    }
    [HarmonyPatch(typeof(Verb), nameof(Verb.CanHitTargetFrom))]
    public static class CommanderCloakDirectTarget
    {
        static bool Prefix(Verb __instance, LocalTargetInfo targ, ref bool __result)
        {
            if (!targ.HasThing || !CommanderCloak.HiddenFrom(targ.Thing, __instance.Caster)) return true;
            __result = false; return false;
        }
    }
    [HarmonyPatch]
    public static class CommanderCloakStartCast
    {
        static IEnumerable<MethodBase> TargetMethods()
            => AccessTools.GetDeclaredMethods(typeof(Verb)).Where(m => m.Name == nameof(Verb.TryStartCastOn));
        static bool Prefix(Verb __instance, [HarmonyArgument(0)] LocalTargetInfo target, ref bool __result)
        {
            if (!target.HasThing || !CommanderCloak.HiddenFrom(target.Thing, __instance.Caster)) return true;
            __result = false; return false;
        }
    }
    [HarmonyPatch(typeof(Verb), "TryCastNextBurstShot")]
    public static class CommanderCloakContinuingBurst
    {
        static bool Prefix(Verb __instance)
        {
            if (!__instance.CurrentTarget.HasThing || !CommanderCloak.HiddenFrom(__instance.CurrentTarget.Thing, __instance.Caster)) return true;
            var complete = __instance.castCompleteCallback;
            __instance.Reset(); complete?.Invoke();
            return false;
        }
    }
}
