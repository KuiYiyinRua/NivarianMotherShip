using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Nivarian.NivarianDrones;
using Nivarian.NivarianDrones.NivarianDroneComps;
using Nivarian_Race.Code.Comps.ThingComps;
using RimWorld;
using Verse;

namespace NivarianCommander
{
    public sealed class MonolithStunRecord : IExposable
    {
        public Thing target;
        public int remaining;
        public bool firstTickPending;
        public int createdLocalTick = -1;
        public int externalRemaining;
        public bool nativeApplied, pendingNative;
        public Thing instigator;
        internal StunHandler sourceClock;
        internal int lastPrimaryTick = -1;
        public void ExposeData()
        {
            Scribe_References.Look(ref target, "target");
            Scribe_Values.Look(ref remaining, "remainingLocalTicks");
            Scribe_Values.Look(ref firstTickPending, "firstTickPending");
            Scribe_Values.Look(ref createdLocalTick, "createdLocalTick", -1);
            Scribe_Values.Look(ref externalRemaining, "externalStunTicks");
            Scribe_Values.Look(ref nativeApplied, "nativeStunApplied");
            Scribe_Values.Look(ref pendingNative, "pendingNativeStun");
            Scribe_References.Look(ref instigator, "nativeStunInstigator");
            if (Scribe.mode == LoadSaveMode.PostLoadInit && target != null)
            {
                NativeMonolithStun.SourceClock(this);
                if (!nativeApplied && NativeMonolithStun.Primary(target) != null) pendingNative = true;
            }
        }
    }
    public sealed class CommanderControl : MapComponent
    {
        List<MonolithStunRecord> monolithStuns = new List<MonolithStunRecord>();
        public CommanderControl(Map map) : base(map) { }
        public IReadOnlyList<MonolithStunRecord> MonolithStuns => monolithStuns;
        public static int Remaining(Thing target)
            => target != null && target.Spawned && !target.Destroyed && !SupportDiplomacy.Friendly(target)
                ? target.Map.GetComponent<CommanderControl>().monolithStuns.FirstOrDefault(r => r.target == target)?.remaining ?? 0 : 0;
        public static bool Stunned(Thing target) => Remaining(target) > 0;
        public static void ClearFriendly()
        {
            if (Current.ProgramState != ProgramState.Playing) return;
            foreach (var map in Find.Maps) map.GetComponent<CommanderControl>().RemoveWhere(r => r.target == null || SupportDiplomacy.Friendly(r.target), true);
        }
        void RemoveWhere(Func<MonolithStunRecord, bool> predicate, bool restore)
        {
            foreach (var record in monolithStuns.Where(predicate).ToArray())
            { if (restore) NativeMonolithStun.Release(record); monolithStuns.Remove(record); }
        }
        public static void RemoveTarget(Thing target, bool restore)
        {
            if (Current.ProgramState != ProgramState.Playing) return;
            foreach (var map in Find.Maps) map.GetComponent<CommanderControl>().RemoveWhere(r => r.target == target, restore);
        }
        public static void Stun(Thing target, int ticks, Thing source)
        {
            if (target == null || target.Destroyed || !target.Spawned || ticks <= 0 || SupportDiplomacy.Friendly(target)) return;
            // Real native stun application with source ownership for cancellation.
            var records = target.Map.GetComponent<CommanderControl>().monolithStuns;
            var record = records.FirstOrDefault(r => r.target == target);
            if (record == null) { record = new MonolithStunRecord { target = target }; records.Add(record); }
            NativeMonolithStun.Refresh(record, ticks, source);
        }
        public override void MapComponentTick()
        {
            RemoveWhere(r => r.target == null || r.target.Destroyed || !r.target.Spawned
                || r.target.Map != map || SupportDiplomacy.Friendly(r.target), true);
            foreach (var record in monolithStuns.ToArray())
            {
                NativeMonolithStun.Tick(record);
            }
            RemoveWhere(r => r.remaining <= 0, true);
        }
        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref monolithStuns, "nmcMonolithStuns", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit) monolithStuns = monolithStuns ?? new List<MonolithStunRecord>();
        }
    }
    [HarmonyPatch(typeof(StunHandler), "get_Stunned")]
    public static class CommanderNativeStunQuery
    { static void Postfix(StunHandler __instance, ref bool __result) { if (NativeMonolithStun.Pending(__instance)) __result = true; } }
    [HarmonyPatch(typeof(StunHandler), "get_StunTicksLeft")]
    public static class CommanderNativeStunDuration
    { static void Postfix(StunHandler __instance, ref int __result) { if (NativeMonolithStun.Pending(__instance)) __result = Math.Max(__result, CommanderControl.Remaining(__instance.parent)); } }
    [HarmonyPatch]
    public static class CommanderControlFactionChange
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.DeclaredMethod(typeof(Thing), nameof(Thing.SetFaction), new[] { typeof(Faction), typeof(Pawn) });
            yield return AccessTools.DeclaredMethod(typeof(Pawn), nameof(Pawn.SetFaction), new[] { typeof(Faction), typeof(Pawn) });
        }
        static void Postfix() => CommanderControl.ClearFriendly();
    }
    [HarmonyPatch(typeof(Faction), nameof(Faction.Notify_RelationKindChanged))]
    public static class CommanderControlDiplomacyChange
    { static void Postfix() => CommanderControl.ClearFriendly(); }
    [HarmonyPatch]
    public static class CommanderControlStartCast
    {
        static IEnumerable<MethodBase> TargetMethods() => AccessTools.GetDeclaredMethods(typeof(Verb)).Where(m => m.Name == nameof(Verb.TryStartCastOn));
        static bool Prefix(Verb __instance, ref bool __result)
        { if (!CommanderControl.Stunned(__instance.Caster)) return true; __result = false; return false; }
    }
    [HarmonyPatch]
    public static class CommanderDroneMovementStun
    {
        static IEnumerable<MethodBase> TargetMethods()
            => typeof(NivarianDroneComp_MovingBase).Assembly.GetTypes()
                .Where(t => typeof(NivarianDroneComp_MovingBase).IsAssignableFrom(t))
                .Select(t => AccessTools.DeclaredMethod(t, nameof(ThingComp.CompTick), Type.EmptyTypes))
                .Where(m => m != null);
        static bool Prefix(ThingComp __instance) => !CommanderControl.Stunned(__instance.parent);
    }
    [HarmonyPatch(typeof(NivarianDroneBase), nameof(NivarianDroneBase.GoHome))]
    public static class CommanderDroneHomeStun
    {
        static bool Prefix(NivarianDroneBase __instance) => !CommanderControl.Stunned(__instance);
    }
    [HarmonyPatch(typeof(CompAttachTurret), nameof(CompAttachTurret.CompTick))]
    public static class CommanderDroneTurretStun
    {
        static bool Prefix(CompAttachTurret __instance) => !CommanderControl.Stunned(__instance.parent);
    }
    [HarmonyPatch(typeof(CompAttachTurret), nameof(CompAttachTurret.Fire))]
    public static class CommanderDroneFireStun
    {
        static bool Prefix(CompAttachTurret __instance) => !CommanderControl.Stunned(__instance.parent);
    }
}
