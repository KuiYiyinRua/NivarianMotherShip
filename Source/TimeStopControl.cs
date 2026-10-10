using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Nivarian.NivarianDrones;
using Nivarian.NivarianDrones.NivarianDroneComps;
using Nivarian_Race.Code.Comps.ThingComps;
using RimWorld;
using Verse;

namespace NivarianCommander
{
    public static class TimeStopCombat
    {
        // Pure, map-local cadence: exactly one approved non-Pawn combat progress
        // tick per 100 local ticks. Pawn actions are stopped by independent stun.
        public static bool Progress(Thing source)
        {
            if (ProjectionRegistry.BlocksFire(source) || !ProjectionResources.CanFire(source)) return false;
            if (!TimeStopMap.Controlled(source)) return VoidCrystalCombat.Progress(source);
            return !(source is Pawn) && source.Map.GetComponent<TimeStopMap>().remaining % 100 == 0
                && VoidCrystalCombat.Progress(source);
        }
        public static bool TurretStunned(bool native, Building_TurretGun turret)
            => native || VoidCrystalMap.Affected(turret) || !Progress(turret);
        public static bool BurstProgress(bool bursting, Verb verb)
            => bursting && (verb.Caster is Pawn ? VoidCrystalCombat.VerbProgress(verb) : Progress(verb.Caster));
        public static bool CanFire(Verb verb) => !ProjectionRegistry.BlocksFire(verb.Caster) && ProjectionResources.CanFire(verb)
            && !TimeStopMap.Controlled(verb.Caster) && VoidCrystalCombat.VerbProgress(verb);
    }

    [HarmonyPatch(typeof(StunHandler), "get_Stunned")]
    public static class TimeStopPawnStun
    {
        static void Postfix(StunHandler __instance, ref bool __result)
        {
            if (__instance.parent is Pawn && NativeMonolithStun.Primary(__instance.parent) == __instance
                && TimeStopMap.Controlled(__instance.parent)) __result = true;
        }
    }
    [HarmonyPatch(typeof(StunHandler), "get_StunTicksLeft")]
    public static class TimeStopPawnStunDuration
    {
        static void Postfix(StunHandler __instance, ref int __result)
        {
            if (__instance.parent is Pawn && NativeMonolithStun.Primary(__instance.parent) == __instance
                && TimeStopMap.Controlled(__instance.parent)) __result = Math.Max(2, __result);
        }
    }

    // Preserve base.Tick (power/refuelling/ordinary components) and all the native
    // target bookkeeping. Restrict only the already isolated turret combat branch.
    [HarmonyPatch(typeof(Building_TurretGun), "Tick")]
    public static class TimeStopTurretProgress
    {
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> source, ILGenerator generator)
        {
            // Native IL explicitly calls the protected getter declared on the
            // base Building_Turret, not a property declared on TurretGun.
            var native = AccessTools.DeclaredPropertyGetter(typeof(Building_Turret), "IsStunned");
            var gate = AccessTools.Method(typeof(TimeStopCombat), nameof(TimeStopCombat.TurretStunned));
            var reset = AccessTools.DeclaredMethod(typeof(Building_TurretGun), "ResetCurrentTarget");
            var controlled = AccessTools.Method(typeof(VoidCrystalCombat), nameof(VoidCrystalCombat.TurretResetLocked));
            int matched = 0, resets = 0;
            foreach (var code in source)
            {
                if (code.Calls(reset))
                {
                    // The native inactive/stunned ELSE clears target and warmup.
                    // A skipped S11 progress tick must preserve both. Guard only
                    // this Tick call, so other native reset paths remain intact.
                    resets++;
                    var callNative = generator.DefineLabel();
                    var afterReset = generator.DefineLabel();
                    var duplicate = new CodeInstruction(OpCodes.Dup);
                    duplicate.labels.AddRange(code.labels);
                    duplicate.blocks.AddRange(code.blocks);
                    code.labels.Clear(); code.blocks.Clear();
                    code.labels.Add(callNative);
                    yield return duplicate;
                    yield return new CodeInstruction(OpCodes.Call, controlled);
                    yield return new CodeInstruction(OpCodes.Brfalse, callNative);
                    yield return new CodeInstruction(OpCodes.Pop);
                    yield return new CodeInstruction(OpCodes.Br, afterReset);
                    yield return code;
                    var resume = new CodeInstruction(OpCodes.Nop);
                    resume.labels.Add(afterReset);
                    yield return resume;
                    continue;
                }
                yield return code;
                if (!code.Calls(native)) continue;
                matched++;
                yield return new CodeInstruction(OpCodes.Ldarg_0);
                yield return new CodeInstruction(OpCodes.Call, gate);
            }
            if (matched != 1) throw new InvalidOperationException("[NMC S11] Expected exactly one native turret combat branch, got " + matched);
            if (resets != 1) throw new InvalidOperationException("[NMC S11] Expected exactly one native turret inactive reset, got " + resets);
        }
    }

    // VerbTick also advances visual effecters. Slow its burst branch, never the
    // whole method; this leaves effecters and independent ability cooldowns live.
    [HarmonyPatch(typeof(Verb), nameof(Verb.VerbTick))]
    public static class TimeStopBurstProgress
    {
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> source, ILGenerator generator)
        {
            var codes = source.ToList();
            var state = AccessTools.Field(typeof(Verb), nameof(Verb.state));
            var gate = AccessTools.Method(typeof(TimeStopCombat), nameof(TimeStopCombat.BurstProgress));
            var shot = AccessTools.Method(typeof(Verb), "TryCastNextBurstShot");
            var canFire = AccessTools.Method(typeof(TimeStopCombat), nameof(TimeStopCombat.CanFire));
            int matched = 0, shots = 0;
            for (int i = 0; i < codes.Count; i++)
            {
                var code = codes[i];
                // Actual native IL is ldfld state, ldc.i4.1, bne.un.s.
                if (code.LoadsField(state) && i + 2 < codes.Count
                    && codes[i + 1].LoadsConstant((int)VerbState.Bursting)
                    && (codes[i + 2].opcode == OpCodes.Bne_Un || codes[i + 2].opcode == OpCodes.Bne_Un_S))
                {
                    matched++;
                    yield return code;
                    yield return codes[++i];
                    yield return new CodeInstruction(OpCodes.Ceq);
                    yield return new CodeInstruction(OpCodes.Ldarg_0);
                    yield return new CodeInstruction(OpCodes.Call, gate);
                    var branch = new CodeInstruction(codes[++i]);
                    branch.opcode = branch.opcode == OpCodes.Bne_Un_S ? OpCodes.Brfalse_S : OpCodes.Brfalse;
                    yield return branch;
                    continue;
                }
                if (code.Calls(shot))
                {
                    shots++;
                    // Skipping the native body alone still runs the original
                    // race's void shot prefix (recoil/weapon draw offset). Avoid
                    // entering that entire shot call while this source controls it.
                    var call = generator.DefineLabel();
                    var end = generator.DefineLabel();
                    var duplicate = new CodeInstruction(OpCodes.Dup);
                    duplicate.labels.AddRange(code.labels);
                    duplicate.blocks.AddRange(code.blocks);
                    code.labels.Clear(); code.blocks.Clear();
                    yield return duplicate;
                    yield return new CodeInstruction(OpCodes.Call, canFire);
                    yield return new CodeInstruction(OpCodes.Brtrue, call);
                    yield return new CodeInstruction(OpCodes.Pop);
                    yield return new CodeInstruction(OpCodes.Br, end);
                    code.labels.Add(call);
                    yield return code;
                    var resume = new CodeInstruction(OpCodes.Nop);
                    resume.labels.Add(end);
                    yield return resume;
                    continue;
                }
                yield return code;
            }
            if (matched != 1) throw new InvalidOperationException("[NMC S11] Expected exactly one native Verb burst branch, got " + matched);
            if (shots != 1) throw new InvalidOperationException("[NMC S11] Expected exactly one native Verb burst shot call, got " + shots);
        }
    }

    [HarmonyPatch]
    public static class TimeStopStartCast
    {
        static IEnumerable<MethodBase> TargetMethods()
            => AccessTools.GetDeclaredMethods(typeof(Verb)).Where(m => m.Name == nameof(Verb.TryStartCastOn));
        static bool Prefix(Verb __instance, ref bool __result)
        {
            if (!TimeStopMap.Controlled(__instance.Caster)
                && (!VoidCrystalMap.Affected(__instance.Caster) || !(__instance.Caster is Building))) return true;
            __result = false;
            return false;
        }
    }
    [HarmonyPatch(typeof(Verb), "TryCastNextBurstShot")]
    public static class TimeStopBurstShot
    { static bool Prefix(Verb __instance) => TimeStopCombat.CanFire(__instance); }
    [HarmonyPatch(typeof(Building_TurretGun), "BeginBurst")]
    public static class TimeStopTurretBeginBurst
    { static bool Prefix(Building_TurretGun __instance) => !VoidCrystalCombat.TurretResetLocked(__instance); }
    [HarmonyPatch(typeof(Building_TurretGun), nameof(Building_TurretGun.TryStartShootSomething))]
    public static class TimeStopTurretAcquire
    { static bool Prefix(Building_TurretGun __instance) => !VoidCrystalCombat.TurretResetLocked(__instance); }

    // This exact native component only handles its weapon, target/aim, recoil and
    // effecters. Battery, fuel, health, work and skill timers are separate ticks.
    [HarmonyPatch(typeof(CompAttachTurret), nameof(CompAttachTurret.CompTick))]
    public static class TimeStopAttachedWeaponProgress
    {
        static readonly AccessTools.FieldRef<CompAttachTurret, int> Cooldown
            = AccessTools.FieldRefAccess<CompAttachTurret, int>("_cooldownUntilTick");
        static readonly AccessTools.FieldRef<CompAttachTurret, int> NextIdleRotation
            = AccessTools.FieldRefAccess<CompAttachTurret, int>("_nextIdleRotationTick");
        static bool Prefix(CompAttachTurret __instance)
        {
            if (TimeStopCombat.Progress(__instance.parent)) return true;
            // Native weapon timers store absolute game deadlines. Preserve their
            // remaining combat ticks when this component's progress is skipped.
            // Expired deadlines stay expired; unrelated ability/battery ticks run.
            int now = GenTicks.TicksGame;
            if (Cooldown(__instance) > now) Cooldown(__instance)++;
            if (NextIdleRotation(__instance) > now) NextIdleRotation(__instance)++;
            return false;
        }
    }
    [HarmonyPatch(typeof(CompAttachTurret), "Fire")]
    public static class TimeStopAttachedWeaponFire
    { static bool Prefix(CompAttachTurret __instance) => !TimeStopMap.Controlled(__instance.parent) && VoidCrystalCombat.Progress(__instance.parent); }

    [HarmonyPatch]
    public static class TimeStopFlightMovement
    {
        static IEnumerable<MethodBase> TargetMethods()
            => typeof(NivarianDroneComp_MovingBase).Assembly.GetTypes()
                .Where(t => typeof(NivarianDroneComp_MovingBase).IsAssignableFrom(t))
                .Select(t => AccessTools.DeclaredMethod(t, nameof(ThingComp.CompTick), Type.EmptyTypes))
                .Where(m => m != null);
        static bool Prefix(ThingComp __instance) => !TimeStopMap.Controlled(__instance.parent) && VoidCrystalCombat.Progress(__instance.parent);
    }
    [HarmonyPatch(typeof(NivarianDroneBase), nameof(NivarianDroneBase.GoHome))]
    public static class TimeStopFlightHome
    { static bool Prefix(NivarianDroneBase __instance) => !TimeStopMap.Controlled(__instance) && VoidCrystalCombat.Progress(__instance); }

    [HarmonyPatch(typeof(HediffComp_Invisibility), "get_ForcedVisible")]
    public static class TimeStopPawnForcedVisible
    { static void Postfix(HediffComp_Invisibility __instance, ref bool __result)
        { if (TimeStopMap.Controlled(__instance.Pawn)) __result = true; } }
    [HarmonyPatch(typeof(InvisibilityUtility), nameof(InvisibilityUtility.IsPsychologicallyInvisible))]
    public static class TimeStopPawnVisibility
    {
        [HarmonyPriority(Priority.Last)]
        static void Postfix(Pawn pawn, ref bool __result)
        { if (TimeStopMap.Controlled(pawn)) __result = false; }
    }
    [HarmonyPatch(typeof(CommanderCloak), nameof(CommanderCloak.Invisible))]
    public static class TimeStopThingVisibility
    { static void Postfix(Thing target, ref bool __result)
        { if (TimeStopMap.Controlled(target)) __result = false; } }
    [HarmonyPatch(typeof(NivarianAntiCloakingHelper), nameof(NivarianAntiCloakingHelper.CanEngageInvisibleFrom))]
    public static class TimeStopDetection
    { static void Postfix(Thing source, ref bool __result)
        { if (TimeStopMap.Controlled(source)) __result = false; } }
    [HarmonyPatch]
    public static class TimeStopDroneDetection
    {
        static IEnumerable<MethodBase> TargetMethods()
            => typeof(NivarianDroneBase).Assembly.GetTypes()
                .Where(t => typeof(NivarianDroneBase).IsAssignableFrom(t))
                .Select(t => AccessTools.DeclaredPropertyGetter(t, nameof(NivarianDroneBase.CanEngageInvisibleTargets)))
                .Where(m => m != null);
        static void Postfix(NivarianDroneBase __instance, ref bool __result)
        { if (TimeStopMap.Controlled(__instance)) __result = false; }
    }
    [HarmonyPatch(typeof(NivarianDroneBase), "TickAntiCloakReveal")]
    public static class TimeStopDroneReveal
    { static bool Prefix(NivarianDroneBase __instance) => !TimeStopMap.Controlled(__instance); }

    [HarmonyPatch]
    public static class TimeStopFactionChange
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.DeclaredMethod(typeof(Thing), nameof(Thing.SetFaction), new[] { typeof(Faction), typeof(Pawn) });
            yield return AccessTools.DeclaredMethod(typeof(Pawn), nameof(Pawn.SetFaction), new[] { typeof(Faction), typeof(Pawn) });
        }
        static void Postfix() => TimeStopMap.ClearInvalid();
    }
    [HarmonyPatch(typeof(Faction), nameof(Faction.Notify_RelationKindChanged))]
    public static class TimeStopDiplomacyChange
    { static void Postfix() => TimeStopMap.ClearInvalid(); }
    [HarmonyPatch(typeof(Thing), nameof(Thing.SetFactionDirect))]
    public static class TimeStopFactionChangeDirect
    { static void Postfix() => TimeStopMap.ClearInvalid(); }
    [HarmonyPatch]
    public static class TimeStopDespawn
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.DeclaredMethod(typeof(Thing), nameof(Thing.DeSpawn), new[] { typeof(DestroyMode) });
            yield return AccessTools.DeclaredMethod(typeof(Pawn), nameof(Pawn.DeSpawn), new[] { typeof(DestroyMode) });
        }
        static void Prefix(Thing __instance) => TimeStopMap.RemoveTarget(__instance);
    }
}
