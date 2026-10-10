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
        public static bool Available => ModsConfig.IsActive("Ancot.MiliraRace") && BatteryDef != null;
        public static ThingDef BatteryDef => DefDatabase<ThingDef>.GetNamedSilentFail("Milira_StarshipBattery");
        public static bool NeedsDelivery(Building_NivarianAircraft ship)
            => Available && ship != null && ship.Spawned && !ship.Destroyed && !ship.Airborne
                && ship.Faction == Faction.OfPlayer && ship.starshipUpgradeRequested && !ship.starshipBatteryInstalled
                && ship.starshipBatteriesDelivered < ship.StarshipBatteryCost;

        // This is the sole UI mutation and is registered by the optional MP adapter.
        public static void Request(Map map, Building_NivarianAircraft ship)
        {
            if (!Available || map == null || ship == null || !ship.Spawned || ship.Destroyed || ship.Map != map
                || ship.Faction != Faction.OfPlayer || ship.Airborne || ship.starshipBatteryInstalled) return;
            ship.starshipUpgradeRequested = true;
        }
        public static Gizmo UpgradeGizmo(Building_NivarianAircraft ship)
        {
            var command = new Command_Action {
                defaultLabel = ship.starshipBatteryInstalled ? NmcText.Get("NMC_Text0025") : ship.starshipUpgradeRequested
                    ? NmcText.Get("NMC_Text0026", ship.starshipBatteriesDelivered, ship.StarshipBatteryCost) : NmcText.Get("NMC_Text0027"),
                defaultDesc = NmcText.Get("NMC_Text0028", ship.StarshipBatteryCost, ship.TakeoffFuelCost),
                icon = BatteryDef.uiIcon,
                action = () => Request(ship.Map, ship)
            };
            if (ship.starshipBatteryInstalled) command.Disable(NmcText.Get("NMC_Text0029"));
            else if (ship.starshipUpgradeRequested) command.Disable(NmcText.Get("NMC_Text0030"));
            else if (ship.Airborne) command.Disable(NmcText.Get("NMC_Text0031"));
            return command;
        }
        public static int Deliver(Building_NivarianAircraft ship, Thing battery)
        {
            if (!NeedsDelivery(ship) || battery == null || battery.Destroyed || battery.def != BatteryDef) return 0;
            int count = Math.Min(ship.StarshipBatteryCost - ship.starshipBatteriesDelivered, battery.stackCount);
            if (count <= 0) return 0;
            battery.SplitOff(count).Destroy();
            ship.starshipBatteriesDelivered += count;
            if (ship.starshipBatteriesDelivered == ship.StarshipBatteryCost)
            {
                ship.starshipBatteryInstalled = true;
                ship.starshipUpgradeRequested = false;
                ship.fuelClock = 0;
                Messages.Message(NmcText.Get("NMC_Text0032", ship.LabelCap), ship, MessageTypeDefOf.PositiveEvent, false);
            }
            return count;
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
            job.count = Math.Min(battery.stackCount, ship.StarshipBatteryCost - ship.starshipBatteriesDelivered);
            return job;
        }
        static Thing FindBattery(Pawn pawn, Building_NivarianAircraft ship, bool forced)
            => pawn.Map.listerThings.ThingsOfDef(AircraftStarshipUpgrade.BatteryDef)
                .Where(t => !t.IsForbidden(pawn) && pawn.CanReserveAndReach(t, PathEndMode.ClosestTouch, pawn.NormalMaxDanger(), 1, -1, null, forced))
                .OrderBy(t => pawn.Position.DistanceToSquared(t.Position)).ThenBy(t => t.thingIDNumber).FirstOrDefault();
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
