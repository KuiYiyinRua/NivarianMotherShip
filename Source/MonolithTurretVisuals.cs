using System;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace NivarianCommander
{
    // The native building graphic prints the fixed base. This local renderer
    // adds a north-facing sprite above it, rotating around their common center.
    // It follows an existing volley, never acquires targets or gates damage.
    internal sealed class MonolithTurretVisuals
    {
        int observedRound = -1;
        Thing followedTarget;
        float angle;
        bool failed;

        static bool Visible(Thing target, Thing source)
            => target != null && !target.Destroyed && target.Spawned && target.Map == source.Map &&
                target.Position.InBounds(source.Map) && !target.Position.Fogged(source.Map);

        void UpdateAim(CompMonolithWeapon weapon)
        {
            if (observedRound != weapon.roundsFired)
            {
                observedRound = weapon.roundsFired;
                followedTarget = null;
                // Capture before the damage loop too, so a lethal primary hit
                // doesn't turn the head toward a different target in the volley.
                foreach (var target in weapon.LastTargets)
                    if (Visible(target, weapon.parent)) { followedTarget = target; break; }
            }
            if (!Visible(followedTarget, weapon.parent) || !weapon.Powered ||
                !followedTarget.Position.InHorDistOf(weapon.parent.Position, weapon.Props.range)) return;
            var direction = followedTarget.DrawPos - weapon.parent.DrawPos;
            direction.y = 0f;
            if (direction.sqrMagnitude > .0001f) angle = direction.AngleFlat();
        }

        internal Vector3 Muzzle(CompMonolithWeapon weapon)
        {
            UpdateAim(weapon);
            return weapon.parent.DrawPos + Altitudes.AltIncVect +
                new Vector3(0f, 0f, weapon.Props.turretMuzzleOffset).RotatedBy(angle);
        }

        internal void Draw(CompMonolithWeapon weapon)
        {
            if (failed || Find.CurrentMap != weapon.parent.Map || !WorldRendererUtility.DrawingMap ||
                LongEventHandler.ShouldWaitForEvent || weapon.Props.turretGraphic == null) return;
            var unityRandom = UnityEngine.Random.state;
            Rand.PushState();
            try
            {
                UpdateAim(weapon);
                var data = weapon.Props.turretGraphic;
                var at = weapon.parent.DrawPos + Altitudes.AltIncVect;
                var pivot = weapon.Props.turretPivot;
                // The authored breech is below image center. Offset the quad
                // around the axle, not the axle around the building center.
                at += new Vector3((.5f - pivot.x) * data.drawSize.x, 0f,
                    (pivot.y - .5f) * data.drawSize.y).RotatedBy(angle);
                // Unlike vanilla east-facing gun art, this texture points north;
                // no vanilla -90 degree artwork correction is appropriate.
                Graphics.DrawMesh(MeshPool.plane10,
                    Matrix4x4.TRS(at, Quaternion.AngleAxis(angle, Vector3.up),
                        new Vector3(data.drawSize.x, 1f, data.drawSize.y)),
                    data.Graphic.MatSingle, 0);
                if (DefenseShieldOverlay.Active(weapon.parent))
                {
                    at.y += .012f;
                    Graphics.DrawMesh(MeshPool.plane10, Matrix4x4.TRS(at, Quaternion.AngleAxis(angle, Vector3.up),
                        new Vector3(data.drawSize.x, 1f, data.drawSize.y)),
                        DefenseShieldOverlay.FilterMaterial(data.Graphic.MatSingle), 0);
                }
            }
            catch (Exception error)
            {
                failed = true;
                Log.ErrorOnce("[NMC] Monolith turret head visual unavailable: " + error, 9401704);
            }
            finally { UnityEngine.Random.state = unityRandom; Rand.PopState(); }
        }
    }
}
