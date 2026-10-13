using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.AI;

namespace NivarianCommander
{
    public static class AircraftStarshipUpgrade
    {
        public static bool Available => BatteryDef != null;
        public static ThingDef BatteryDef => DefDatabase<ThingDef>.GetNamedSilentFail("Milira_StarshipBattery")
            ?? DefDatabase<ThingDef>.GetNamedSilentFail("UnstablePowerCell")
            ?? DefDatabase<ThingDef>.GetNamedSilentFail("VanometricPowerCell");
        // Public def-based interface: optional mods can supply further equivalents.
        public static int BatteryUnits(ThingDef def)
        {
            if (def == null) return 0;
            if (def.defName == "Milira_StarshipBattery" || def.defName == "UnstablePowerCell") return 1;
            if (def.defName == "VanometricPowerCell") return 2;
            return Math.Max(0, def.GetModExtension<StarshipBatteryEquivalent>()?.units ?? 0);
        }
        public static int DeliveryCount(Building_NivarianAircraft ship, Thing battery)
        {
            int units = BatteryUnits(battery?.GetInnerIfMinified()?.def);
            if (units <= 0 || ship == null || battery == null) return 0;
            int remaining = ship.StarshipBatteryCost - ship.starshipBatteriesDelivered;
            return remaining <= 0 ? 0 : Math.Min(battery.stackCount, 1 + (remaining - 1) / units);
        }
        public static bool NeedsDelivery(Building_NivarianAircraft ship)
            => Available && ship != null && ship.Spawned && !ship.Destroyed && !ship.Airborne
                && ship.Faction == Faction.OfPlayer && ship.starshipUpgradeRequested && !ship.starshipBatteryInstalled
                && ship.starshipBatteriesDelivered < ship.StarshipBatteryCost;

        // UI executors are registered by the optional MP adapter.
        public static void Request(Map map, Building_NivarianAircraft ship)
        {
            if (!Available || map == null || ship == null || !ship.Spawned || ship.Destroyed || ship.Map != map
                || ship.Faction != Faction.OfPlayer || ship.Airborne || ship.starshipBatteryInstalled) return;
            ship.starshipUpgradeRequested = true;
        }
        public static void Cancel(Map map, Building_NivarianAircraft ship)
        {
            if (map == null || ship == null || !ship.Spawned || ship.Destroyed || ship.Map != map
                || ship.Faction != Faction.OfPlayer || ship.starshipBatteryInstalled || !ship.starshipUpgradeRequested) return;
            if (!RestoreLegacyInputs(ship)) return;
            ship.starshipUpgradeRequested = false; // Outstanding hauling jobs fail before depositing.
            // A failed drop stays owned and saved; another click retries only the remainder.
            ship.starshipBatteryInputs.TryDropAll(ship.Position, map, ThingPlaceMode.Near);
            ship.starshipBatteriesDelivered = 0;
            foreach (var input in ship.starshipBatteryInputs)
                ship.starshipBatteriesDelivered += input.stackCount * BatteryUnits(input.GetInnerIfMinified().def);
            if (ship.starshipBatteryInputs.Count > 0) ship.starshipUpgradeRequested = true;
        }
        static bool RestoreLegacyInputs(Building_NivarianAircraft ship)
        {
            // Account for legacy credit even when new equivalents were also delivered.
            int recorded = ship.starshipBatteryInputs.Sum(t => t.stackCount * BatteryUnits(t.GetInnerIfMinified().def));
            int missing = ship.starshipBatteriesDelivered - recorded;
            if (missing > 0)
            {
                var def = DefDatabase<ThingDef>.GetNamedSilentFail("Milira_StarshipBattery")
                    ?? DefDatabase<ThingDef>.GetNamedSilentFail("UnstablePowerCell");
                if (def == null) return false; // Preserve the old credit if its def is unavailable.
                for (int i = 0; i < missing; i++)
                {
                    Thing refund = ThingMaker.MakeThing(def);
                    if (refund is Building) refund = refund.MakeMinified();
                    ship.starshipBatteryInputs.TryAdd(refund, false);
                }
            }
            return true;
        }
        public static Gizmo UpgradeGizmo(Building_NivarianAircraft ship)
        {
            var command = new Command_Action {
                defaultLabel = ship.starshipBatteryInstalled ? NmcText.Get("NMC_Text0025") : ship.starshipUpgradeRequested
                    ? NmcText.Get("NMC_Text0752", ship.starshipBatteriesDelivered, ship.StarshipBatteryCost) : NmcText.Get("NMC_Text0027"),
                defaultDesc = NmcText.Get("NMC_Text0028", ship.StarshipBatteryCost, ship.TakeoffFuelCost),
                icon = BatteryDef.uiIcon,
                action = () => { if (ship.starshipUpgradeRequested) Cancel(ship.Map, ship); else Request(ship.Map, ship); }
            };
            if (ship.starshipBatteryInstalled) command.Disable(NmcText.Get("NMC_Text0029"));
            else if (ship.Airborne && !ship.starshipUpgradeRequested) command.Disable(NmcText.Get("NMC_Text0031"));
            return command;
        }
        public static int Deliver(Building_NivarianAircraft ship, Thing battery)
        {
            if (!NeedsDelivery(ship) || battery == null || battery.Destroyed || battery is Building && battery.Spawned) return 0;
            int count = DeliveryCount(ship, battery);
            if (count <= 0) return 0;
            if (!RestoreLegacyInputs(ship)) return 0;
            int units = BatteryUnits(battery.GetInnerIfMinified().def);
            // Keep the actual input (including minified payload HP) until completion/cancel.
            int added = ship.starshipBatteryInputs.TryAddOrTransfer(battery, count, false);
            if (added <= 0) return 0;
            ship.starshipBatteriesDelivered += added * units;
            if (ship.starshipBatteriesDelivered >= ship.StarshipBatteryCost)
            {
                ship.starshipBatteryInstalled = true;
                ship.starshipUpgradeRequested = false;
                ship.fuelClock = 0;
                ship.starshipBatteriesDelivered = ship.StarshipBatteryCost;
                ship.starshipBatteryInputs.ClearAndDestroyContents();
                Messages.Message(NmcText.Get("NMC_Text0032", ship.LabelCap), ship, MessageTypeDefOf.PositiveEvent, false);
            }
            return added;
        }
    }

    public sealed class WorkGiver_UpgradeAircraftStarship : WorkGiver_Scanner
    {
        public override ThingRequest PotentialWorkThingRequest => ThingRequest.ForGroup(ThingRequestGroup.BuildingArtificial);
        public override PathEndMode PathEndMode => PathEndMode.Touch;
        public override bool HasJobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            if (!(t is Building_NivarianAircraft ship) || !AircraftStarshipUpgrade.NeedsDelivery(ship)
                || ship.IsForbidden(pawn) || ship.IsBurning() || !pawn.CanReserveAndReach(ship, PathEndMode.Touch, pawn.NormalMaxDanger(), 1, -1, null, forced)
                || pawn.Map.designationManager.DesignationOn(ship, DesignationDefOf.Deconstruct) != null) return false;
            if (FindBattery(pawn, ship, forced) != null) return true;
            JobFailReason.Is(NmcText.Get("NMC_Text0033"));
            return false;
        }
        public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            if (!HasJobOnThing(pawn, t, forced)) return null;
            var ship = (Building_NivarianAircraft)t;
            Thing battery = FindBattery(pawn, ship, forced);
            if (battery == null) return null;
            var job = JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("NMC_InstallStarshipBattery"), ship, battery);
            job.count = AircraftStarshipUpgrade.DeliveryCount(ship, battery);
            return job;
        }
        static Thing FindBattery(Pawn pawn, Building_NivarianAircraft ship, bool forced)
            => pawn.Map.listerThings.ThingsInGroup(ThingRequestGroup.HaulableEver)
                .Where(t => AircraftStarshipUpgrade.BatteryUnits(t.GetInnerIfMinified().def) > 0
                    && !ProjectionPacking.Blocked(t) && !t.IsForbidden(pawn)
                    && pawn.CanReserveAndReach(t, PathEndMode.ClosestTouch, pawn.NormalMaxDanger(), 1, -1, null, forced))
                .OrderBy(t => pawn.Position.DistanceToSquared(t.Position)).ThenBy(t => t.thingIDNumber).FirstOrDefault();
    }

    public sealed class StarshipBatteryEquivalent : DefModExtension
    {
        public int units = 1;
    }
    public sealed class JobDriver_InstallStarshipBattery : JobDriver
    {
        Building_NivarianAircraft Ship => job.targetA.Thing as Building_NivarianAircraft;
        public override bool TryMakePreToilReservations(bool errorOnFailed)
            => pawn.Reserve(job.targetA, job, 1, -1, null, errorOnFailed)
                && pawn.Reserve(job.targetB, job, 1, -1, null, errorOnFailed);
        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedNullOrForbidden(TargetIndex.A);
            this.FailOn(() => !AircraftStarshipUpgrade.NeedsDelivery(Ship));
            yield return Toils_Goto.GotoThing(TargetIndex.B, PathEndMode.ClosestTouch)
                .FailOnDespawnedNullOrForbidden(TargetIndex.B).FailOnSomeonePhysicallyInteracting(TargetIndex.B);
            yield return Toils_Haul.StartCarryThing(TargetIndex.B).FailOnDestroyedNullOrForbidden(TargetIndex.B);
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
            yield return Toils_General.Wait(120).FailOnCannotTouch(TargetIndex.A, PathEndMode.Touch)
                .FailOnDestroyedNullOrForbidden(TargetIndex.B).WithProgressBarToilDelay(TargetIndex.A);
            yield return Toils_General.DoAtomic(() => AircraftStarshipUpgrade.Deliver(Ship, pawn.carryTracker.CarriedThing));
        }
    }

    // Native map launches and caravan launches both call these exact methods.
    // Keep native loading, roofs, cargo, travel/arrival actions and MP launch sync.
    [HarmonyPatch(typeof(CompLaunchable), nameof(CompLaunchable.FuelNeededToLaunchAtDist))]
    internal static class AircraftStarshipWorldFuel
    {
        static bool Prefix(CompLaunchable __instance, ref float __result)
        {
            if (!(__instance.parent is Building_NivarianAircraft ship) || !ship.StarshipPowered) return true;
            __result = ship.TakeoffFuelCost;
            return false;
        }
    }
    [HarmonyPatch(typeof(CompLaunchable), nameof(CompLaunchable.MaxLaunchDistanceAtFuelLevel), new[] { typeof(float), typeof(PlanetLayer) })]
    internal static class AircraftStarshipWorldRange
    {
        static bool Prefix(CompLaunchable __instance, float fuelLevel, ref int __result)
        {
            if (!(__instance.parent is Building_NivarianAircraft ship) || !ship.StarshipPowered) return true;
            // DrawWorldRadiusRing computes radius + 1. Avoid its integer overflow;
            // this still exceeds any reachable world path and rejects no-path MaxValue.
            __result = fuelLevel >= ship.TakeoffFuelCost ? int.MaxValue - 1 : 0;
            return false;
        }
    }
}
