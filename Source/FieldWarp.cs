using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace NivarianCommander
{
    // The original, damageable Thing stays spawned throughout both four-second phases.
    // No holder, replacement pawn, invulnerability or damage interception is used.
    public sealed class FieldWarpOrder : IExposable
    {
        public Thing subject, anchor;
        public Map destination;
        public IntVec3 center;
        public float radius;
        public int revision, remaining = FieldWarp.Duration, startedTick;
        public bool primaryField, arriving;
        public void ExposeData()
        {
            Scribe_References.Look(ref subject, "subject"); Scribe_References.Look(ref anchor, "anchor");
            Scribe_References.Look(ref destination, "destination");
            Scribe_Values.Look(ref center, "center"); Scribe_Values.Look(ref radius, "radius", 8f);
            Scribe_Values.Look(ref revision, "revision"); Scribe_Values.Look(ref remaining, "remaining", FieldWarp.Duration);
            Scribe_Values.Look(ref startedTick, "startedTick"); Scribe_Values.Look(ref primaryField, "primaryField");
            Scribe_Values.Look(ref arriving, "arriving");
        }
        public bool DestinationValid
        {
            get
            {
                if (destination == null || !Find.Maps.Contains(destination)) return false;
                if (primaryField)
                {
                    var currentField = CommanderField.Current;
                    return currentField != null && currentField.Exists && currentField.revision == revision
                        && currentField.fieldMap == destination && currentField.center == center;
                }
                return anchor is Building_NivarianAircraft prism && prism.WarpFieldActive
                    && prism.Map == destination && prism.warpFieldRevision == revision && prism.Position == center;
            }
        }
    }

    public sealed class FieldWarp : MapComponent
    {
        public const int Duration = 4 * 60;
        public List<FieldWarpOrder> orders = new List<FieldWarpOrder>();
        public FieldWarp(Map map) : base(map) { }
        public static FieldWarpOrder For(Thing subject)
        {
            if (subject == null || !subject.Spawned) return null;
            var orders = subject.Map.GetComponent<FieldWarp>().orders;
            for (int i = 0; i < orders.Count; i++) if (orders[i].subject == subject) return orders[i];
            return null;
        }
        public static bool Pending(Thing subject) => For(subject) != null;
        public static void Begin(Thing subject, Map destination, IntVec3 center, float radius,
            int revision, Thing anchor = null)
        {
            if (Pending(subject)) return;
            var order = new FieldWarpOrder { subject = subject, destination = destination, center = center,
                radius = radius, revision = revision, anchor = anchor, primaryField = anchor == null,
                startedTick = GenTicks.TicksGame };
            if (!order.DestinationValid) return;
            subject.Map.GetComponent<FieldWarp>().orders.Add(order);
        }
        public override void MapComponentTick()
        {
            for (int i = orders.Count - 1; i >= 0; i--)
            {
                var order = orders[i];
                var subject = order.subject;
                if (subject == null || subject.Destroyed || !subject.Spawned || subject.Map != map
                    || subject.Faction != Faction.OfPlayer || subject is Pawn pawn && pawn.Dead
                    || !order.arriving && !order.DestinationValid)
                { orders.RemoveAt(i); continue; }
                // A command issued during this map tick must not lose its first tick.
                if (GenTicks.TicksGame == order.startedTick || --order.remaining > 0) continue;
                orders.RemoveAt(i);
                if (order.arriving) continue;
                var landing = FieldRecallActions.LandingCell(subject, order.destination, order.center, order.radius);
                if (!landing.IsValid)
                { Messages.Message(NmcText.Get("NMC_Text0250"), subject, MessageTypeDefOf.RejectInput, false); continue; }
                bool transferred = subject is Pawn colonist
                    ? FieldRecallActions.CompleteTeleport(map, colonist, order.destination, landing)
                    : ShipFieldRecallActions.CompleteRecall(map, subject, order.revision);
                if (transferred)
                    ProjectionContext.Run(subject.Map, () => subject.Map.GetComponent<FieldWarp>().orders.Add(
                        new FieldWarpOrder { subject = subject, arriving = true,
                            center = subject.Position, startedTick = GenTicks.TicksGame }));
            }
        }
        public override void MapRemoved() { orders.Clear(); }
        public override void ExposeData()
        {
            base.ExposeData(); Scribe_Collections.Look(ref orders, "nmcFieldWarpOrders", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit) orders = orders ?? new List<FieldWarpOrder>();
        }
    }
}
