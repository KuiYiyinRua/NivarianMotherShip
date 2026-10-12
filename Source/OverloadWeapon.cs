using System;
using System.Linq;
using LumiParticle.Data;
using LumiParticle.Effects;
using Nivarian.NivarianDrones;
using RimWorld;
using UnityEngine;
using Verse;

namespace NivarianCommander
{
    public static class OverloadWeapon
    {
        public const int IntervalTicks = 30;
        public const float DamageAmount = 10f;
        // Prism Catawand's real beam resource; Bullet_PrismBeam is only a dummy def.
        // Keep this local: the staff's own colors, damage scaling and hediff are unchanged.
        static readonly LumiLaserLineData PrismBeam = new LumiLaserLineData
        {
            prefabName = "line_psyBeam", fadeIn = .1f, hold = .2f, fadeOut = .3f,
            overrideColor = true,
            startColor = new Color(.2f, .75f, 1f, 1f),
            endColor = new Color(.1f, .45f, 1f, 1f)
        };

        public static void Tick(DefenseRecord record, Map map)
        {
            if (ProjectionRegistry.BlocksFire(record.target) || !ProjectionResources.CanFireSupport(record.target)) return;
            if (record.weaponWait > 0 && --record.weaponWait > 0) return;
            Thing source = record.target;
            // Stable target selection; only damage resolution uses simulation randomness.
            var enemy = map.listerThings.AllThings.Where(t => t.Spawned && !t.Destroyed && t != source
                && (t is Pawn || t is Building || t is NivarianDroneBase)
                && (!(t is Pawn p) || !p.DeadOrDowned)
                && t.HostileTo(source.Faction ?? Faction.OfPlayer)
                && !SupportDiplomacy.Friendly(t)
                && !CommanderCloak.HiddenFrom(t, source)
                && t.Position.InHorDistOf(source.Position, 58f)
                && GenSight.LineOfSight(source.Position, t.Position, map, skipFirstCell: true))
                .OrderBy(t => t.Position.DistanceToSquared(source.Position)).ThenBy(t => t.thingIDNumber).FirstOrDefault();
            if (enemy == null) { record.weaponWait = 15; return; }

            // Damage direction is based on simulation cells, never render interpolation.
            float angle = (enemy.Position - source.Position).ToVector3().AngleFlat();
            record.weaponWait = IntervalTicks; record.weaponShots++;
            // Render before a lethal hit can despawn the target; all render reads are isolated.
            ShowBeam(map, source, enemy);
            enemy.TakeDamage(new DamageInfo(DefDatabase<DamageDef>.GetNamed("NMC_OverloadEnergy"),
                DamageAmount, .25f, angle, instigator: source));
        }

        static void ShowBeam(Map map, Thing source, Thing target)
        {
            if (Find.CurrentMap != map) return;
            // Cosmetic prefab creation must not consume the map's simulation RNG.
            Rand.PushState();
            try
            {
                Vector3 start = source.DrawPos, end = target.DrawPos;
                Vector3 direction = end - start;
                if (direction.sqrMagnitude > 1f)
                {
                    direction.Normalize();
                    start += direction * .6f;
                    end -= direction * .4f;
                }
                if (LumiParticleEffects.SpawnLine(map, start, end, PrismBeam) == null)
                    Log.ErrorOnce("[NMC] Could not create the Prism Catawand overload beam.", 9401201);
            }
            catch (Exception ex)
            {
                Log.ErrorOnce("[NMC] Overload beam visual failed: " + ex, 9401202);
            }
            finally { Rand.PopState(); }
        }
    }
}
