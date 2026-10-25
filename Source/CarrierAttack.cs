using System.Collections.Generic;
using RimWorld;
using Verse;

namespace NivarianCommander
{
    public sealed partial class Building_NivarianAircraft
    {
        public Thing carrierTarget;
        public bool carrierFireAtWill = true;

        internal void ExposeCarrierAttack()
        {
            Scribe_References.Look(ref carrierTarget, "nmcCarrierTarget");
            Scribe_Values.Look(ref carrierFireAtWill, "nmcCarrierFireAtWill", true);
        }

        internal void ValidateAttackOrders()
        {
            if (carrierTarget != null && !AircraftTargeting.ForcedTargetExists(this, carrierTarget)) carrierTarget = null;
            if (stormTarget != null && !AircraftTargeting.ForcedTargetExists(this, stormTarget)) stormTarget = null;
        }
    }

    public static class CarrierAttackActions
    {
        static bool Owned(Map map, Building_NivarianAircraft ship) => map != null && ship != null
            && ship.IsCarrier && ship.Spawned && !ship.Destroyed && ship.Map == map && ship.Faction == Faction.OfPlayer;

        public static bool ValidTarget(Building_NivarianAircraft ship, Thing target)
            => AircraftTargeting.ForcedTargetVisible(ship, target);

        public static Thing LaunchTarget(Building_NivarianAircraft ship)
        {
            if (FieldWarp.Pending(ship)) return null;
            if (ship.carrierTarget != null)
                return InRange(ship, ship.carrierTarget, AircraftHostileControl.CarrierRange) ? ship.carrierTarget : null;
            return ship.carrierFireAtWill ? AircraftTargeting.Nearest(ship, AircraftHostileControl.CarrierRange, false) : null;
        }

        public static bool InRange(Building_NivarianAircraft ship, Thing target, float range)
            => ValidTarget(ship, target) && ship.Position.DistanceToSquared(target.Position) <= range * range;

        public static void SetTarget(Map map, Building_NivarianAircraft ship, Thing target)
        {
            if (!Owned(map, ship) || !ship.CombatFlight || FieldWarp.Pending(ship) || !ValidTarget(ship, target)) return;
            ship.carrierTarget = target;
            foreach (var fighter in ship.deployed)
            {
                if (fighter == null || !fighter.Spawned || fighter.Destroyed || fighter.Map != map) continue;
                fighter.target = null;
                fighter.burstLeft = fighter.retargetWait = 0;
                // A new order can redirect returning aircraft while they are still inside the leash.
                if (fighter.Position.DistanceToSquared(ship.Position) <= 6400 && InRange(ship, target, 80f))
                    fighter.returning = false;
            }
        }

        public static void ClearTarget(Map map, Building_NivarianAircraft ship)
        {
            if (!Owned(map, ship)) return;
            ship.carrierTarget = null;
            foreach (var fighter in ship.deployed)
            {
                if (fighter == null || fighter.Destroyed) continue;
                fighter.target = null;
                fighter.burstLeft = fighter.retargetWait = 0;
            }
        }

        public static void SetFireAtWill(Map map, Building_NivarianAircraft ship, bool enabled)
        { if (Owned(map, ship)) ship.carrierFireAtWill = enabled; }

        public static IEnumerable<Gizmo> Gizmos(Building_NivarianAircraft ship)
        {
            var attack = new Command_Action {
                defaultLabel = "NMC_CarrierAttack".Translate(), defaultDesc = "NMC_CarrierAttackDesc".Translate(),
                icon = ship.def.uiIcon,
                action = () => Find.Targeter.BeginTargeting(new TargetingParameters {
                    canTargetPawns = true, canTargetBuildings = true, canTargetItems = true, canTargetLocations = false,
                    validator = t => ValidTarget(ship, t.Thing)
                }, t => SetTarget(ship.Map, ship, t.Thing))
            };
            if (!ship.CombatFlight || FieldWarp.Pending(ship)) attack.Disable("NMC_CarrierMustFly".Translate());
            yield return attack;
            yield return new Command_Toggle {
                defaultLabel = "NMC_CarrierFireAtWill".Translate(), defaultDesc = "NMC_CarrierAutoDesc".Translate(),
                icon = ship.def.uiIcon, isActive = () => ship.carrierFireAtWill,
                toggleAction = () => SetFireAtWill(ship.Map, ship, !ship.carrierFireAtWill)
            };
            if (ship.carrierTarget != null)
                yield return new Command_Action {
                    defaultLabel = "NMC_StormClearTarget".Translate(), defaultDesc = "NMC_CarrierClearDesc".Translate(),
                    icon = ship.def.uiIcon, action = () => ClearTarget(ship.Map, ship)
                };
        }
    }
}
