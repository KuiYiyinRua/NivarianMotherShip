using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace NivarianCommander
{
    public sealed partial class Building_NivarianAircraft
    {
        public bool IsStormShip => def.defName == "NMC_StormShip";
        public bool IsGravityFrigate => def.defName == "NMC_GravityFrigate";
        public bool HeavyFlight => IsCarrier || IsStormShip;
        public const float StormRange = 100f;
        public const int StormCooldownTicks = 180;
        public Thing stormTarget;
        public bool stormFireAtWill = true;
        int stormSearchTicks;

        internal void StormCombat()
        {
            if (FieldWarp.Pending(this) || weaponClock > 0) return;
            Thing target = StormShipActions.CanFireAt(this, stormTarget) ? stormTarget : null;
            if (stormTarget == null && stormFireAtWill)
            {
                if (stormSearchTicks > 0) { stormSearchTicks--; return; }
                // Stable tie-breaking, and bounded idle searches for the long-range weapon.
                target = Map.listerThings.AllThings.Where(t => StormShipActions.ValidAutomaticTarget(this, t))
                    .OrderBy(t => Position.DistanceToSquared(t.Position)).ThenBy(t => t.thingIDNumber).FirstOrDefault();
                if (target == null) { stormSearchTicks = 29; return; }
            }
            if (target == null) return;
            var projectile = (Projectile)ThingMaker.MakeThing(ThingDef.Named("NMC_StormOrb"));
            GenSpawn.Spawn(projectile, Position, Map);
            projectile.Launch(this, DrawPos, target, target, ProjectileHitFlags.All);
            weaponClock = StormCooldownTicks;
            volleys++;
            Map.GetComponent<StormShipVisuals>().Flash(DrawPos, false);
            DefDatabase<SoundDef>.GetNamed("NMC_StormOrbLaunch").PlayOneShot(SoundInfo.InMap(this));
        }

        void ExposeStormWeapon()
        {
            Scribe_References.Look(ref stormTarget, "nmcStormTarget");
            Scribe_Values.Look(ref stormFireAtWill, "nmcStormFireAtWill", true);
            Scribe_Values.Look(ref stormSearchTicks, "nmcStormSearchTicks");
        }
    }

    public static class StormShipActions
    {
        // Keep explicit building orders separate from automatic target acquisition.
        public static bool ValidAutomaticTarget(Building_NivarianAircraft ship, Thing target)
            => (target is Pawn || target is Building_Turret) && AircraftTargeting.Valid(ship, target, Building_NivarianAircraft.StormRange)
                && CanFireAt(ship, target);

        static bool Owned(Map map, Building_NivarianAircraft ship) => map != null && ship != null
            && ship.IsStormShip && ship.Spawned && !ship.Destroyed && ship.Map == map && ship.Faction == Faction.OfPlayer;
        public static bool ValidTarget(Building_NivarianAircraft ship, Thing target)
            => AircraftTargeting.ForcedTargetVisible(ship, target);
        public static bool CanFireAt(Building_NivarianAircraft ship, Thing target) => ValidTarget(ship, target)
            && ship.Position.DistanceToSquared(target.Position) <= Building_NivarianAircraft.StormRange * Building_NivarianAircraft.StormRange
            && GenSight.LineOfSight(ship.Position, target.Position, ship.Map, true);
        public static void SetTarget(Map map, Building_NivarianAircraft ship, Thing target)
        {
            if (!Owned(map, ship) || !ship.CombatFlight || FieldWarp.Pending(ship) || !ValidTarget(ship, target)) return;
            ship.stormTarget = target;
        }
        public static void SetFireAtWill(Map map, Building_NivarianAircraft ship, bool enabled)
        { if (Owned(map, ship)) ship.stormFireAtWill = enabled; }
        public static void ClearTarget(Map map, Building_NivarianAircraft ship)
        { if (Owned(map, ship)) ship.stormTarget = null; }
        public static IEnumerable<Gizmo> Gizmos(Building_NivarianAircraft ship)
        {
            var attack = new Command_Action {
                defaultLabel = "NMC_StormAttack".Translate(), defaultDesc = "NMC_StormAttackDesc".Translate(),
                icon = ThingDef.Named("NMC_StormOrb").uiIcon,
                action = () => Find.Targeter.BeginTargeting(new TargetingParameters {
                    canTargetPawns = true, canTargetBuildings = true, canTargetItems = true, canTargetLocations = false,
                    validator = t => ValidTarget(ship, t.Thing)
                }, t => SetTarget(ship.Map, ship, t.Thing))
            };
            if (!ship.CombatFlight || FieldWarp.Pending(ship)) attack.Disable("NMC_StormMustFly".Translate());
            yield return attack;
            yield return new Command_Toggle {
                defaultLabel = "NMC_StormFireAtWill".Translate(), defaultDesc = "NMC_StormAutoDesc".Translate(),
                icon = ThingDef.Named("NMC_StormOrb").uiIcon, isActive = () => ship.stormFireAtWill,
                toggleAction = () => SetFireAtWill(ship.Map, ship, !ship.stormFireAtWill)
            };
            if (ship.stormTarget != null)
                yield return new Command_Action {
                    defaultLabel = "NMC_StormClearTarget".Translate(), defaultDesc = "NMC_StormClearDesc".Translate(),
                    icon = ThingDef.Named("NMC_StormOrb").uiIcon, action = () => ClearTarget(ship.Map, ship)
                };
        }
    }

    // Native Projectile serializes travel and collision, including shots still in flight at save/join.
    public sealed class Projectile_StormOrb : Projectile
    {
        protected override void Impact(Thing hitThing, bool blockedByShield = false)
        {
            var map = Map;
            var cell = Position;
            var source = launcher;
            if (!blockedByShield && map != null && cell.InBounds(map))
            {
                if (hitThing != null && !hitThing.Destroyed)
                    hitThing.TakeDamage(new DamageInfo(def.projectile.damageDef, 20f, .8f,
                        (destination - origin).AngleFlat(), source, weapon: def, intendedTarget: intendedTarget.Thing));
                // Override the footprint explicitly: all nine cells, no adjacent-cell extension or falloff.
                var cells = CellRect.CenteredOn(cell, 1).Cells.Where(c => c.InBounds(map)).ToList();
                GenExplosion.DoExplosion(cell, map, 1.5f, DefDatabase<DamageDef>.GetNamed("NMC_StormBlastDamage"),
                    source, damAmount: 25, armorPenetration: .35f, projectile: def,
                    intendedTarget: intendedTarget.Thing, applyDamageToExplosionCellsNeighbors: false,
                    chanceToStartFire: 0f, damageFalloff: false, doVisualEffects: false,
                    doSoundEffects: true, overrideCells: cells);
                map.GetComponent<StormShipVisuals>().Flash(cell.ToVector3Shifted(), true);
            }
            base.Impact(hitThing, blockedByShield);
        }

        protected override void DrawAt(Vector3 drawLoc, bool flip = false)
        {
            float phase = Find.TickManager.TicksGame * 11f + thingIDNumber * 19f;
            var at = drawLoc; at.y = AltitudeLayer.Projectile.AltitudeFor();
            float size = 1.05f + .08f * Mathf.Sin(phase * Mathf.Deg2Rad);
            Graphics.DrawMesh(MeshPool.plane10, Matrix4x4.TRS(at,
                Quaternion.AngleAxis(phase, Vector3.up), new Vector3(size, 1f, size)), Graphic.MatSingle, 0);
            var direction = (destination - origin); direction.y = 0; direction.Normalize();
            StormShipVisuals.Trail(at, direction, phase);
        }
    }

    // Pure presentation: no random calls, no damage, and no simulation state depends on visibility.
    public sealed class StormShipVisuals : MapComponent
    {
        sealed class Pulse { public Vector3 position; public int born; public bool impact; }
        readonly List<Pulse> pulses = new List<Pulse>();
        static Material arc;
        static Material Arc => arc ?? (arc = SolidColorMaterials.SimpleSolidColorMaterial(new Color(.35f, .75f, 1f, .85f)));
        public StormShipVisuals(Map map) : base(map) { }
        public void Flash(Vector3 at, bool impact)
        {
            if (Find.CurrentMap != map) return;
            pulses.Add(new Pulse { position = at, born = Find.TickManager.TicksGame, impact = impact });
        }
        public static void Trail(Vector3 at, Vector3 direction, float phase)
        {
            var side = new Vector3(direction.z, 0f, -direction.x);
            for (int strand = 0; strand < 2; strand++)
            {
                Vector3 last = at;
                for (int i = 1; i <= 7; i++)
                {
                    var next = at - direction * (i * .25f) + side * (Mathf.Sin((phase + i * 91f + strand * 160f) * Mathf.Deg2Rad) * .17f);
                    GenDraw.DrawLineBetween(last, next, Arc, .035f); last = next;
                }
            }
        }
        public override void MapComponentUpdate()
            => pulses.RemoveAll(p => Find.TickManager.TicksGame - p.born > 24);
        public override void MapComponentDraw()
        {
            if (Find.CurrentMap != map) return;
            foreach (var p in pulses)
            {
                if (p.position.ToIntVec3().Fogged(map)) continue;
                float age = Mathf.Clamp01((Find.TickManager.TicksGame - p.born) / 24f);
                float radius = (p.impact ? 1.45f : .65f) * (.3f + .7f * age);
                var center = p.position; center.y = AltitudeLayer.MoteOverhead.AltitudeFor();
                for (int i = 0; i < 12; i++)
                {
                    float angle = (i * 30f + age * 70f) * Mathf.Deg2Rad;
                    var end = center + new Vector3(Mathf.Sin(angle), 0, Mathf.Cos(angle)) * radius;
                    var elbow = Vector3.Lerp(center, end, .58f) + new Vector3(Mathf.Cos(angle), 0, -Mathf.Sin(angle)) * .17f;
                    GenDraw.DrawLineBetween(center, elbow, Arc, .05f * (1f - age));
                    GenDraw.DrawLineBetween(elbow, end, Arc, .035f * (1f - age));
                }
            }
        }
        public override void MapRemoved() => pulses.Clear();
    }
}
