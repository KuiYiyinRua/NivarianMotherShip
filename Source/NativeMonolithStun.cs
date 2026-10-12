using System;
using HarmonyLib;
using RimWorld;
using Verse;

namespace NivarianCommander
{
    // The original handler owns the target's stun. A separate original handler
    // owns this source's clock, allowing cancellation without cancelling EMP.
    public static class NativeMonolithStun
    {
        static readonly AccessTools.FieldRef<StunHandler, int> RawTicks =
            AccessTools.FieldRefAccess<StunHandler, int>("stunTicksLeft");
        static bool applyingOwned;
        public static StunHandler Primary(Thing target)
            => target is Pawn pawn ? pawn.stances?.stunner : target?.TryGetComp<CompStunnable>()?.StunHandler;
        public static MonolithStunRecord Record(Thing target)
        {
            if (target == null || Current.ProgramState != ProgramState.Playing) return null;
            if (target.Map != null)
            {
                var records = CommanderPawnIndex.For(target.Map).Control.MonolithStuns;
                for (int i = 0; i < records.Count; i++)
                    if (records[i].target == target) return records[i];
            }
            if (!target.Spawned)
                foreach (var map in Find.Maps)
                {
                    var records = CommanderPawnIndex.For(map).Control.MonolithStuns;
                    for (int i = 0; i < records.Count; i++)
                        if (records[i].target == target) return records[i];
                }
            return null;
        }
        public static StunHandler SourceClock(MonolithStunRecord record)
        {
            if (record.sourceClock == null && record.target != null)
            {
                record.sourceClock = new StunHandler(record.target);
                if (record.remaining > 0)
                    record.sourceClock.StunFor(record.remaining, null, false, Primary(record.target) == null);
            }
            return record.sourceClock;
        }
        public static void Refresh(MonolithStunRecord record, int ticks, Thing source)
        {
            var main = Primary(record.target);
            if (!record.nativeApplied && !record.pendingNative)
                record.externalRemaining = main == null ? 0 : Math.Max(0, RawTicks(main));
            SourceClock(record).StunFor(ticks, source, false, main == null);
            record.remaining = record.sourceClock.StunTicksLeft;
            record.pendingNative = true;
            record.instigator = source;
            record.firstTickPending = true;
            record.createdLocalTick = GenTicks.TicksGame;
            Find.BattleLog.Add(new BattleLogEntry_Event(record.target, RulePackDefOf.Event_Stun, source));
        }
        public static void Tick(MonolithStunRecord record)
        {
            var main = Primary(record.target);
            // CompStunnable on a rare-ticking object has no native CompTick this
            // local tick. Only that gap uses the original handler's tick adapter.
            if (record.nativeApplied && main != null && record.lastPrimaryTick != GenTicks.TicksGame)
                main.StunHandlerTick();
            var clock = SourceClock(record);
            bool bornNow = record.firstTickPending && record.createdLocalTick == GenTicks.TicksGame;
            record.firstTickPending = false;
            if (!bornNow) clock.StunHandlerTick();
            record.remaining = clock.StunTicksLeft;
            if (!record.pendingNative) return;
            record.pendingNative = false;
            if (main == null) return; // Native-handler adapter for non-stunnable flying objects.
            applyingOwned = true;
            try
            {
                // All normal thing ticks have completed. First native decrement is
                // the next local tick, independent of target registration order.
                main.StunFor(record.remaining, record.instigator, false, true, main.DisableRotation);
                record.nativeApplied = true;
            }
            finally { applyingOwned = false; }
        }
        public static void Release(MonolithStunRecord record)
        {
            var main = Primary(record.target);
            if (main == null || !record.nativeApplied) return;
            int actual = RawTicks(main);
            // Never StopStun the shared handler: that would also cancel EMP.
            // Demerge only a counter covered by the known owned source. Longer
            // unexpected external state is left intact.
            if (actual <= Math.Max(record.remaining, record.externalRemaining))
                RawTicks(main) = Math.Max(0, Math.Min(actual, record.externalRemaining));
        }
        public static void ObserveExternalApply(StunHandler handler, int ticks)
        {
            if (applyingOwned || ticks <= 0) return;
            var record = Record(handler.parent);
            if (record == null || Primary(record.target) != handler) return;
            record.externalRemaining = Math.Max(record.externalRemaining, ticks);
        }
        public static void ObserveNativeTick(StunHandler handler, int before)
        {
            var record = Record(handler.parent);
            if (record == null || Primary(record.target) != handler) return;
            record.lastPrimaryTick = GenTicks.TicksGame;
            if (before > 0) record.externalRemaining = Math.Max(0, record.externalRemaining - 1);
            if (RawTicks(handler) <= 0) record.externalRemaining = 0;
        }
        public static int Raw(StunHandler handler) => RawTicks(handler);
        public static bool Pending(StunHandler handler)
        {
            var record = Record(handler.parent);
            return record != null && record.pendingNative && record.remaining > 0
                && Primary(record.target) == handler && !SupportDiplomacy.Friendly(record.target);
        }
        public static void ObserveExternalStop(StunHandler handler)
        {
            if (applyingOwned || Primary(handler.parent) != handler) return;
            CommanderControl.RemoveTarget(handler.parent, false);
        }
    }
    [HarmonyPatch(typeof(StunHandler), nameof(StunHandler.StunFor))]
    public static class MonolithExternalNativeStun
    { static void Postfix(StunHandler __instance, int ticks) => NativeMonolithStun.ObserveExternalApply(__instance, ticks); }
    [HarmonyPatch(typeof(StunHandler), nameof(StunHandler.StunHandlerTick))]
    public static class MonolithNativeStunTick
    {
        static void Prefix(StunHandler __instance, out int __state) => __state = NativeMonolithStun.Raw(__instance);
        static void Postfix(StunHandler __instance, int __state) => NativeMonolithStun.ObserveNativeTick(__instance, __state);
    }
    [HarmonyPatch(typeof(StunHandler), nameof(StunHandler.StopStun))]
    public static class MonolithExternalNativeStop
    { static void Postfix(StunHandler __instance) => NativeMonolithStun.ObserveExternalStop(__instance); }
}
