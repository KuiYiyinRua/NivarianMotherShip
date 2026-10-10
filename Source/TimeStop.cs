using System;
using System.Collections.Generic;
using System.Linq;
using Nivarian.NivarianDrones;
using Nivarian_Race.Code.Comps.ThingComps;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace NivarianCommander
{
    // One cast-time snapshot per colony, with an independent map-local lifetime.
    public sealed class TimeStopMap : MapComponent
    {
        public const int Duration = 1800;
        public int remaining;
        public int castsReceived;
        public int lastCompletedElapsed;
        List<Thing> targets = new List<Thing>();
        static Material marker;
        public TimeStopMap(Map map) : base(map) { }
        public bool Active => remaining > 0;
        public IReadOnlyList<Thing> Targets => targets;

        public static bool Eligible(Thing thing, Map owner)
            => thing != null && thing.Spawned && !thing.Destroyed && thing.Map == owner
                && !(thing is Pawn pawn && pawn.Dead) && thing.HostileTo(Faction.OfPlayer)
                && !SupportDiplomacy.Friendly(thing)
                && (thing is Pawn || thing is NivarianDroneBase || thing is Building_Turret
                    || thing.TryGetComp<CompAttachTurret>() != null);

        public static bool Controlled(Thing thing)
        {
            if (thing?.Map == null || !Eligible(thing, thing.Map)) return false;
            var state = thing.Map.GetComponent<TimeStopMap>();
            return state != null && state.Active && state.targets.Contains(thing);
        }

        public void Start(IEnumerable<Thing> snapshot)
        {
            targets = snapshot.Where(t => Eligible(t, map)).Distinct()
                .OrderBy(t => t.thingIDNumber).ToList();
            remaining = Duration;
            lastCompletedElapsed = 0;
            castsReceived++;
            foreach (var target in targets) Refresh(target);
        }
        public void Cancel()
        {
            if (Active) lastCompletedElapsed = Duration - remaining;
            remaining = 0;
            var previous = targets.ToArray();
            targets.Clear();
            foreach (var target in previous) Refresh(target);
        }
        // Faction/despawn adapters will call this even when the source map is paused.
        // Once removed, a later enemy faction never re-adds it to this snapshot.
        public static void RemoveTarget(Thing thing)
        {
            if (thing == null || Current.Game == null) return;
            foreach (var owner in Find.Maps.OrderBy(m => m.uniqueID))
                if (owner.GetComponent<TimeStopMap>().targets.Remove(thing)) Refresh(thing);
        }
        public static void ClearInvalid()
        {
            if (Current.Game == null) return;
            foreach (var owner in Find.Maps.OrderBy(m => m.uniqueID))
                owner.GetComponent<TimeStopMap>().RemoveInvalid();
        }
        void RemoveInvalid()
        {
            foreach (var target in targets.Where(t => !Eligible(t, map)).ToArray())
            { targets.Remove(target); Refresh(target); }
        }
        static void Refresh(Thing target)
        {
            if (target == null || target.Destroyed || !target.Spawned) return;
            if (target is IAttackTarget attackTarget) target.Map.attackTargetsCache.UpdateTarget(attackTarget);
            if (target is Pawn pawn)
            {
                // Native Stunned/FullBodyBusy pauses PatherTick without discarding its path.
                // StopDead here strands Goto jobs after time resumes.
                pawn.Drawer?.renderer?.SetAllGraphicsDirty();
            }
        }
        public override void MapComponentTick()
        {
            if (!Active) return;
            RemoveInvalid();
            remaining--;
            if (remaining == 0)
            {
                lastCompletedElapsed = Duration;
                var previous = targets.ToArray();
                targets.Clear();
                foreach (var target in previous) Refresh(target);
            }
        }
        public override void MapComponentUpdate()
        {
            if (!Active || Find.CurrentMap != map) return;
            if (marker == null) marker = SolidColorMaterials.SimpleSolidColorMaterial(new Color(.55f, .94f, 1f, .65f), true);
            // Pure drawing: no effect clocks, RNG calls, target acquisition or writes.
            foreach (var target in targets)
                if (Controlled(target)) GenDraw.DrawCircleOutline(target.DrawPos, .65f, marker);
        }
        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref remaining, "nmcTimeStopRemainingLocalTicks");
            Scribe_Values.Look(ref castsReceived, "nmcTimeStopCastsReceived");
            Scribe_Values.Look(ref lastCompletedElapsed, "nmcTimeStopLastCompletedElapsed");
            Scribe_Collections.Look(ref targets, "nmcTimeStopSnapshot", LookMode.Reference);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                targets = targets ?? new List<Thing>();
                targets.RemoveAll(t => t == null);
                remaining = Math.Max(0, Math.Min(Duration, remaining));
                if (!Active) targets.Clear();
            }
        }
    }

    public static class TimeStopActions
    {
        public static string Unavailable()
        {
            var state = CommanderState.Current;
            var def = DefenseActions.Ability(11);
            if (state == null || !Find.Maps.Any(m => m.IsPlayerHome)) return NmcText.Get("NMC_Text0461");
            if (CommanderResearch.Unavailable(def) is string researchBlock) return researchBlock;
            if (state.Remaining(def) > 0) return NmcText.Get("NMC_Text0035", CommanderPanel.Period(state.Remaining(def)));
            if (NativeLedger.Balance("PowerCoin") < def.powerCost || NativeLedger.Balance("VolatileReactive") < def.volatileCost)
                return NmcText.Get("NMC_Text0036", def.powerCost, def.volatileCost);
            return null;
        }
        // Registered once in world context; no local GUI targeter or camera data.
        public static void Start()
        {
            string blocked = Unavailable();
            if (blocked != null) { Reject(blocked); return; }
            var snapshots = Find.Maps.Where(m => m.IsPlayerHome).OrderBy(m => m.uniqueID)
                .Select(m => new { map = m, targets = m.listerThings.AllThings
                    .Where(t => TimeStopMap.Eligible(t, m)).OrderBy(t => t.thingIDNumber).ToList() }).ToList();
            var def = DefenseActions.Ability(11);
            if (!NativeLedger.TryPay(def.powerCost, def.volatileCost)) { Reject(NmcText.Get("NMC_Text0038")); return; }
            CommanderState.Current.cooldowns[def.defName] = def.cooldownTicks;
            foreach (var snapshot in snapshots) snapshot.map.GetComponent<TimeStopMap>().Start(snapshot.targets);
        }
        public static void Cancel()
        {
            foreach (var map in Find.Maps.OrderBy(m => m.uniqueID)) map.GetComponent<TimeStopMap>().Cancel();
        }
        static void Reject(string reason)
        {
            if (CommanderState.Current != null) CommanderState.Current.requestsRejected++;
            Messages.Message(reason, MessageTypeDefOf.RejectInput, false);
        }
    }
}
