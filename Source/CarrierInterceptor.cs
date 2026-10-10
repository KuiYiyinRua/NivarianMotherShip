using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using Nivarian_Race.Code.Patches;

namespace NivarianCommander
{
    public sealed class CarrierInterceptor : ThingWithComps, IAttackTarget, ICanHaveFactionExtraNivarian
    {
        public Building_NivarianAircraft carrier;
        public Thing target;
        public bool returning;
        public Vector3 realPosition;
        public float heading;
        public int burstLeft, fireWait, retargetWait;
        public Thing Thing => this;
        public LocalTargetInfo TargetCurrentlyAimingAt => target;
        public float TargetPriorityFactor => 1f;
        public bool ThreatDisabled(IAttackTargetSearcher searcher)
            => !Spawned || Destroyed || searcher?.CurrentEffectiveVerb?.IsMeleeAttack == true;
        public override Vector3 DrawPos => new Vector3(realPosition.x, AltitudeLayer.Skyfaller.AltitudeFor() + .02f, realPosition.z + 1.2f);
        protected override void Tick()
        {
            base.Tick();
            if (!Spawned) return;
            if (carrier == null || carrier.Destroyed || !carrier.Spawned || carrier.Map != Map)
            { Destroy(); return; }
            if (!carrier.CombatFlight || Position.DistanceToSquared(carrier.Position) > 6400) returning = true;
            if (returning)
            {
                Fly(carrier.flightPosition);
                if ((realPosition - carrier.flightPosition).sqrMagnitude < 1f) carrier.Recover(this);
                return;
            }
            if (fireWait > 0) fireWait--;
            if (retargetWait > 0) retargetWait--;
            if (!AircraftTargeting.Valid(this, target, 90f) || !AircraftTargeting.Valid(carrier, target, 80f))
            {
                target = null; burstLeft = 0;
                if (retargetWait == 0) { target = AircraftTargeting.Nearest(carrier, 60f, false); retargetWait = 30; }
            }
            if (target == null) { returning = true; return; }
            float distance = Position.DistanceTo(target.Position);
            if (distance > 13f) Fly(target.Position.ToVector3Shifted());
            else
            {
                var direction = realPosition - target.Position.ToVector3Shifted(); direction.y = 0;
                if (direction.sqrMagnitude < .01f) direction = Vector3.right;
                var tangent = new Vector3(-direction.z, 0, direction.x).normalized;
                Fly(realPosition + tangent * 4f + direction.normalized * (10f - distance) * .15f);
            }
            if (distance <= 22.9f && fireWait == 0)
            {
                if (burstLeft == 0) burstLeft = 3;
                var def = DefDatabase<ThingDef>.GetNamedSilentFail("Projectile_Gun_HeavySMG_AttackDrone")
                    ?? DefDatabase<ThingDef>.GetNamed("NMC_InterceptorFallbackRound");
                var shot = (Projectile)GenSpawn.Spawn(def, Position, Map);
                var aim = new LocalTargetInfo(target);
                shot.Launch(this, realPosition, aim, aim, ProjectileHitFlags.IntendedTarget, preventFriendlyFire: true);
                // Native drone: three rounds five ticks apart, .9 warmup + 1.22 cooldown.
                burstLeft--; fireWait = burstLeft > 0 ? 5 : 127;
            }
        }
        void Fly(Vector3 goal)
        {
            goal.y = 0; realPosition.y = 0;
            var delta = goal - realPosition;
            if (delta.sqrMagnitude > .001f) heading = Mathf.MoveTowardsAngle(heading, delta.AngleFlat(), 10f);
            var next = Vector3.MoveTowards(realPosition, goal, .25f);
            var cell = next.ToIntVec3();
            if (!cell.InBounds(Map)) { returning = true; return; }
            realPosition = next; if (Position != cell) Position = cell;
        }
        protected override void DrawAt(Vector3 drawLoc, bool flip = false)
            => Graphics.DrawMesh(MeshPool.plane10, Matrix4x4.TRS(DrawPos, Quaternion.AngleAxis(heading, Vector3.up),
                new Vector3(def.graphicData.drawSize.x, 1, def.graphicData.drawSize.y)), Graphic.MatSingle, 0);
        public override string GetInspectString() => NmcText.Get("NMC_Text0044", (carrier?.LabelCap ?? NmcText.Get("NMC_Text0041")), (returning ? NmcText.Get("NMC_Text0042") : NmcText.Get("NMC_Text0043")));
        public override void ExposeData()
        {
            base.ExposeData(); Scribe_References.Look(ref carrier, "nmcCarrier"); Scribe_References.Look(ref target, "nmcTarget");
            Scribe_Values.Look(ref returning, "nmcReturning"); Scribe_Values.Look(ref realPosition, "nmcRealPosition");
            Scribe_Values.Look(ref heading, "nmcHeading"); Scribe_Values.Look(ref burstLeft, "nmcBurstLeft");
            Scribe_Values.Look(ref fireWait, "nmcFireWait"); Scribe_Values.Look(ref retargetWait, "nmcRetargetWait");
        }
    }
}
