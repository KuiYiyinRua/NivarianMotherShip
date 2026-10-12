using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace NivarianCommander
{
    public static class CarrierDragonSteelSupply
    {
        public const int Capacity = 1000;
        public static ThingDef Material => DefDatabase<ThingDef>.GetNamedSilentFail("Nivarian_DragonfrostSteel");
        public static JobDef FillJob => DefDatabase<JobDef>.GetNamedSilentFail("NMC_FillCarrierDragonSteel");
        static bool Owned(Map map, Building_NivarianAircraft ship)
            => map != null && ship != null && ship.Spawned && !ship.Destroyed && ship.Map == map
                && ship.IsCarrier && ship.Faction == Faction.OfPlayer;
        public static int Needed(Building_NivarianAircraft ship)
            => Math.Max(0, Math.Min(Capacity, ship.dragonSteelTarget) - ship.dragonSteel);
        public static bool NeedsDelivery(Building_NivarianAircraft ship, bool forced = false)
            => ship != null && Owned(ship.Map, ship) && !ship.Airborne
                && (forced || ship.autoFillDragonSteel) && Needed(ship) > 0 && Material != null;
        // Only these UI outcomes are synchronized. Drag previews remain local.
        public static void SetTarget(Map map, Building_NivarianAircraft ship, int amount)
        { if (Owned(map, ship)) ship.dragonSteelTarget = Math.Max(0, Math.Min(Capacity, amount)); }
        public static void SetAutoFill(Map map, Building_NivarianAircraft ship, bool enabled)
        { if (Owned(map, ship)) ship.autoFillDragonSteel = enabled; }
        public static int Deliver(Building_NivarianAircraft ship, Thing steel, bool forced = false)
        {
            if (!NeedsDelivery(ship, forced) || steel == null || steel.Destroyed || steel.def != Material) return 0;
            int amount = Math.Min(Needed(ship), steel.stackCount);
            if (amount <= 0) return 0;
            steel.SplitOff(amount).Destroy(); ship.dragonSteel += amount;
            return amount;
        }
    }

    public sealed class Gizmo_CarrierDragonSteel : Gizmo_Slider
    {
        readonly Building_NivarianAircraft ship;
        bool dragging, commitDrag;
        internal bool IsDragging => dragging;
        public Gizmo_CarrierDragonSteel(Building_NivarianAircraft ship) { this.ship = ship; }
        protected override float Width => 180f;
        protected override float Target
        {
            get => ship.dragonSteelTarget / (float)CarrierDragonSteelSupply.Capacity;
            set
            {
                if (!commitDrag) return;
                commitDrag = false;
                CarrierDragonSteelSupply.SetTarget(ship.Map, ship,
                    Mathf.RoundToInt(value * CarrierDragonSteelSupply.Capacity));
            }
        }
        protected override bool DraggingBar
        {
            get => dragging;
            set { commitDrag = dragging && !value; dragging = value; }
        }
        protected override float ValuePercent => ship.dragonSteel / (float)CarrierDragonSteelSupply.Capacity;
        protected override string Title => NmcText.Get("NMC_CarrierSteelTitle");
        protected override string BarLabel => ship.dragonSteel + " / " + CarrierDragonSteelSupply.Capacity;
        protected override bool IsDraggable => true;
        protected override int Increments => 100;
        protected override Color BarColor => new Color(.27f, .54f, .73f);
        protected override string GetTooltip() => NmcText.Get("NMC_CarrierSteelTooltip", ship.dragonSteelTarget,
            Building_NivarianAircraft.FighterSteelCost)
            + (ship.Airborne ? "\n\n" + NmcText.Get("NMC_CarrierSteelAirborne") : "");
        protected override void DrawHeader(Rect headerRect, ref bool mouseOverElement)
        {
            headerRect.xMax -= 24f;
            var toggle = new Rect(headerRect.xMax, headerRect.y, 24f, 24f);
            if (CarrierDragonSteelSupply.Material != null) GUI.DrawTexture(toggle, CarrierDragonSteelSupply.Material.uiIcon);
            GUI.DrawTexture(new Rect(toggle.center.x, toggle.y, 12f, 12f),
                ship.autoFillDragonSteel ? Widgets.CheckboxOnTex : Widgets.CheckboxOffTex);
            if (Widgets.ButtonInvisible(toggle))
                CarrierDragonSteelSupply.SetAutoFill(ship.Map, ship, !ship.autoFillDragonSteel);
            if (Mouse.IsOver(toggle))
            {
                Widgets.DrawHighlight(toggle);
                TooltipHandler.TipRegion(toggle, NmcText.Get("NMC_CarrierSteelAutoFill"));
                mouseOverElement = true;
            }
            base.DrawHeader(headerRect, ref mouseOverElement);
        }
    }

    public sealed class WorkGiver_FillCarrierDragonSteel : WorkGiver_Scanner
    {
        public override ThingRequest PotentialWorkThingRequest => ThingRequest.ForGroup(ThingRequestGroup.BuildingArtificial);
        public override IEnumerable<Thing> PotentialWorkThingsGlobal(Pawn pawn)
        {
            var carrierDef = DefDatabase<ThingDef>.GetNamedSilentFail("NMC_Carrier");
            if (carrierDef == null) yield break;
            foreach (var ship in pawn.Map.listerThings.ThingsOfDef(carrierDef)) yield return ship;
        }
        public override PathEndMode PathEndMode => PathEndMode.Touch;
        public override bool HasJobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            if (!(t is Building_NivarianAircraft ship) || !CarrierDragonSteelSupply.NeedsDelivery(ship, forced)
                || pawn.Faction != ship.Faction || ship.IsForbidden(pawn) || ship.Fogged() || ship.IsBurning()
                || !pawn.CanReserveAndReach(ship, PathEndMode.Touch, pawn.NormalMaxDanger(), 1, -1, null, forced)
                || pawn.Map.designationManager.DesignationOn(ship, DesignationDefOf.Deconstruct) != null) return false;
            if (FindSteel(pawn, forced) != null) return true;
            JobFailReason.Is(NmcText.Get("NMC_CarrierSteelMissing"));
            return false;
        }
        public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            if (!HasJobOnThing(pawn, t, forced)) return null;
            var ship = (Building_NivarianAircraft)t;
            var steel = FindSteel(pawn, forced);
            if (steel == null) return null;
            var job = JobMaker.MakeJob(CarrierDragonSteelSupply.FillJob, ship, steel);
            job.count = Math.Min(steel.stackCount, CarrierDragonSteelSupply.Needed(ship));
            return job;
        }
        static Thing FindSteel(Pawn pawn, bool forced)
            => GenClosest.ClosestThingReachable(pawn.Position, pawn.Map, ThingRequest.ForDef(CarrierDragonSteelSupply.Material),
                PathEndMode.ClosestTouch, TraverseParms.For(pawn), 9999f,
                t => !t.IsForbidden(pawn) && pawn.CanReserve(t, 1, -1, null, forced));
    }

    public sealed class JobDriver_FillCarrierDragonSteel : JobDriver
    {
        Building_NivarianAircraft Ship => job.targetA.Thing as Building_NivarianAircraft;
        public override bool TryMakePreToilReservations(bool errorOnFailed)
            => pawn.Reserve(job.targetA, job, 1, -1, null, errorOnFailed)
                && pawn.Reserve(job.targetB, job, 1, -1, null, errorOnFailed);
        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedNullOrForbidden(TargetIndex.A);
            this.FailOn(() => !CarrierDragonSteelSupply.NeedsDelivery(Ship, job.playerForced));
            yield return Toils_General.DoAtomic(() => job.count = Math.Min(job.count, CarrierDragonSteelSupply.Needed(Ship)));
            yield return Toils_Goto.GotoThing(TargetIndex.B, PathEndMode.ClosestTouch)
                .FailOnDespawnedNullOrForbidden(TargetIndex.B).FailOnSomeonePhysicallyInteracting(TargetIndex.B);
            yield return Toils_Haul.StartCarryThing(TargetIndex.B).FailOnDestroyedNullOrForbidden(TargetIndex.B);
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
            yield return Toils_General.Wait(240).FailOnCannotTouch(TargetIndex.A, PathEndMode.Touch)
                .FailOnDestroyedNullOrForbidden(TargetIndex.B).WithProgressBarToilDelay(TargetIndex.A);
            yield return Toils_General.DoAtomic(() => CarrierDragonSteelSupply.Deliver(Ship, pawn.carryTracker.CarriedThing, job.playerForced));
        }
    }
}
