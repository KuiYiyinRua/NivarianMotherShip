using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace NivarianCommander
{
    public sealed partial class Building_NivarianAircraft
    {
        public bool IsSageShip => def.defName == "NMC_SageShip";
        public const float SageRange = 12f;
        public bool sageAttackMode;
        public Thing sageTarget;

        internal void SageCombat()
        {
            if (!sageAttackMode || FieldWarp.Pending(this) || weaponClock > 0 || FuelLevel < 1f) return;
            Thing target = SageShipActions.ValidTarget(this, sageTarget) ? sageTarget : null;
            if (target == null)
            {
                int nearest = int.MaxValue;
                foreach (var candidate in Map.listerThings.AllThings)
                {
                    if (!SageShipActions.ValidAutomaticTarget(this, candidate)) continue;
                    int distance = Position.DistanceToSquared(candidate.Position);
                    if (distance < nearest || distance == nearest && (target == null || candidate.thingIDNumber < target.thingIDNumber))
                    { target = candidate; nearest = distance; }
                }
            }
            if (target == null) return;
            // One debit owns the whole simultaneous volley, including after a battery upgrade.
            RefuelableComp.ConsumeFuel(1f);
            weaponClock = 30; beamTicks = 24; volleys++; beamTarget = target;
            beamEnd = target.Position.ToVector3Shifted();
            Map.GetComponent<AircraftPrismVisuals>().Show(this);
            DefDatabase<SoundDef>.GetNamed("Nivarian_LaserFire").PlayOneShot(SoundInfo.InMap(this));
            var damage = DefDatabase<DamageDef>.GetNamed("NMC_SageBeamDamage");
            for (int i = 0; i < 2 && !target.Destroyed; i++)
                target.TakeDamage(new DamageInfo(damage, 6f, .3f, (target.Position - Position).ToVector3().AngleFlat(), this));
        }

        internal void SageReveal()
        {
            if ((GenTicks.TicksGame + thingIDNumber) % 30 != 0) return;
            foreach (var pawn in Map.mapPawns.AllPawnsSpawned)
            {
                if (pawn.Dead || !SageDetection.InRange(this, pawn)) continue;
                // Query suppression already makes covered units visible. Inspect sources directly
                // so their native fade, target cache and recovery timers are updated as well.
                CommanderCloak.EnsurePawnSource(pawn);
                foreach (var hediff in pawn.health.hediffSet.hediffs)
                {
                    var invisibility = hediff.TryGetComp<HediffComp_Invisibility>();
                    if (invisibility != null) invisibility.DisruptInvisibility();
                }
            }
        }

        void ExposeSageWeapon()
        {
            Scribe_Values.Look(ref sageAttackMode, "nmcSageAttackMode", false);
            Scribe_References.Look(ref sageTarget, "nmcSageTarget");
        }
    }

    public static class SageShipActions
    {
        // Buildings remain available to explicit orders, but are not automatic targets.
        public static bool ValidAutomaticTarget(Building_NivarianAircraft ship, Thing target)
            => (target is Pawn || target is Building_Turret) && ValidTarget(ship, target);

        static bool Owned(Map map, Building_NivarianAircraft ship) => map != null && ship != null
            && ship.IsSageShip && ship.Spawned && !ship.Destroyed && ship.Map == map && ship.Faction == Faction.OfPlayer;
        public static bool ValidTarget(Building_NivarianAircraft ship, Thing target) => ship != null && ship.Spawned
            && target != null && (target is Pawn || target.def.category == ThingCategory.Building || target is Verse.AI.IAttackTarget)
            && (target is Pawn || target.def.useHitPoints) && !AircraftTargeting.Airborne(target)
            && AircraftTargeting.Valid(ship, target, Building_NivarianAircraft.SageRange)
            && GenSight.LineOfSight(ship.Position, target.Position, ship.Map, true);
        public static void SetAttackMode(Map map, Building_NivarianAircraft ship, bool enabled)
        {
            if (!Owned(map, ship)) return;
            ship.sageAttackMode = enabled;
            if (!enabled) { ship.sageTarget = null; ship.beamTicks = 0; ship.beamTarget = null; }
        }
        public static void SetTarget(Map map, Building_NivarianAircraft ship, Thing target)
        {
            if (Owned(map, ship) && ship.CombatFlight && ship.sageAttackMode && !FieldWarp.Pending(ship) && ValidTarget(ship, target))
                ship.sageTarget = target;
        }
        public static IEnumerable<Gizmo> Gizmos(Building_NivarianAircraft ship)
        {
            yield return new Command_Toggle {
                defaultLabel = "NMC_SageAttackMode".Translate(), defaultDesc = "NMC_SageAttackModeDesc".Translate(),
                icon = ship.def.uiIcon, isActive = () => ship.sageAttackMode,
                toggleAction = () => SetAttackMode(ship.Map, ship, !ship.sageAttackMode)
            };
            var attack = new Command_Action {
                defaultLabel = "NMC_SageTarget".Translate(), defaultDesc = "NMC_SageTargetDesc".Translate(), icon = ship.def.uiIcon,
                action = () => Find.Targeter.BeginTargeting(new TargetingParameters {
                    canTargetPawns = true, canTargetBuildings = true, canTargetItems = true, canTargetLocations = false,
                    validator = t => ValidTarget(ship, t.Thing)
                }, t => SetTarget(ship.Map, ship, t.Thing))
            };
            if (!ship.CombatFlight || !ship.sageAttackMode || FieldWarp.Pending(ship)) attack.Disable("NMC_SageMustAttack".Translate());
            yield return attack;
        }
    }

    public static class SageDetection
    {
        public const float Radius = 50f;
        public static bool InRange(Building_NivarianAircraft ship, Thing target) => ship != null && ship.Spawned && !ship.Destroyed
            && target != null && target.Spawned && !target.Destroyed && target.Map == ship.Map && target.HostileTo(ship)
            && (!SupportDiplomacy.Friendly(ship) || !SupportDiplomacy.Friendly(target))
            && ship.Position.DistanceToSquared(target.Position) <= Radius * Radius;
        // Read live sources; no saved caches or local UI state can extend detection across maps.
        public static bool Covers(Thing target)
        {
            if (target == null || !target.Spawned || target.Destroyed) return false;
            var def = DefDatabase<ThingDef>.GetNamedSilentFail("NMC_SageShip");
            if (def == null) return false;
            foreach (var source in target.Map.listerThings.ThingsOfDef(def))
                if (source is Building_NivarianAircraft ship && InRange(ship, target)) return true;
            return false;
        }
    }
    [HarmonyPatch(typeof(HediffComp_Invisibility), "get_ForcedVisible")]
    public static class SageForcedVisibility
    { static void Postfix(HediffComp_Invisibility __instance, ref bool __result)
        { if (SageDetection.Covers(__instance.Pawn)) __result = true; } }
    [HarmonyPatch(typeof(InvisibilityUtility), nameof(InvisibilityUtility.IsPsychologicallyInvisible))]
    public static class SagePawnVisibility
    {
        [HarmonyPriority(Priority.Last)]
        static void Postfix(Pawn pawn, ref bool __result) { if (SageDetection.Covers(pawn)) __result = false; }
    }
    [HarmonyPatch(typeof(CommanderCloak), nameof(CommanderCloak.Invisible))]
    public static class SageThingVisibility
    { static void Postfix(Thing target, ref bool __result) { if (SageDetection.Covers(target)) __result = false; } }
    [HarmonyPatch(typeof(TravellingTransporters), "get_TraveledPctStepPerTick")]
    public static class SageWorldSpeed
    { static void Postfix(TravellingTransporters __instance, ref float __result)
        { if (__instance.def.defName == "NMC_SageShipFlying") __result *= 1.25f; } }
}
