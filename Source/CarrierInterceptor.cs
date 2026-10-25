using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.Sound;
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
        public float orbitPhase;
        public int burstLeft, fireWait, retargetWait;
        public const int LaunchDuration = 30;
        public int launchTicks;
        public Vector3 launchDirection;
        public Thing Thing => this;
        public LocalTargetInfo TargetCurrentlyAimingAt => target;
        public float TargetPriorityFactor => 1f;
        public bool ThreatDisabled(IAttackTargetSearcher searcher)
            => !Spawned || Destroyed || searcher?.CurrentEffectiveVerb?.IsMeleeAttack == true;
        public override Vector3 DrawPos => new Vector3(realPosition.x, AltitudeLayer.Skyfaller.AltitudeFor() + .02f, realPosition.z + 1.2f);
        internal void BeginLaunch(Building_NivarianAircraft home, float side)
        {
            // Bind the home/target and physical origin before the first spawn draw,
            // as the native DroneHub does, then accelerate clear of the flight deck.
            carrier = home; target = null; returning = false;
            orbitPhase = (thingIDNumber % 360) * Mathf.Deg2Rad;
            var rotation = Quaternion.AngleAxis(home.heading, Vector3.up);
            realPosition = home.flightPosition + rotation * new Vector3(side * 1.35f, 0f, .65f);
            if (!realPosition.ToIntVec3().InBounds(home.Map)) realPosition = home.flightPosition;
            heading = home.heading + side * 25f;
            launchDirection = Quaternion.AngleAxis(heading, Vector3.up) * Vector3.forward;
            launchTicks = LaunchDuration; burstLeft = fireWait = retargetWait = 0;
        }
        protected override void Tick()
        {
            base.Tick();
            if (!Spawned) return;
            if (carrier == null || carrier.Destroyed || !carrier.Spawned || carrier.Map != Map)
            { Destroy(); return; }
            if (!carrier.CombatFlight || FieldWarp.Pending(carrier) || Position.DistanceToSquared(carrier.Position) > 6400) returning = true;
            if (returning)
            {
                launchTicks = 0;
                Fly(carrier.flightPosition);
                if ((realPosition - carrier.flightPosition).sqrMagnitude < 1f) carrier.Recover(this);
                return;
            }
            if (launchTicks > 0)
            {
                float progress = 1f - launchTicks / (float)LaunchDuration;
                var next = realPosition + launchDirection * Mathf.Lerp(.06f, .25f, progress);
                if (!next.ToIntVec3().InBounds(Map)) { launchTicks = 0; returning = true; return; }
                realPosition = next; Position = next.ToIntVec3(); launchTicks--;
                return;
            }
            if (fireWait > 0) fireWait--;
            if (retargetWait > 0) retargetWait--;
            bool forced = carrier.carrierTarget != null;
            bool validTarget = AircraftTargeting.Valid(this, target, 90f)
                && AircraftTargeting.Valid(carrier, target, 80f);
            if (forced)
            {
                var nextTarget = CarrierAttackActions.InRange(carrier, carrier.carrierTarget, 80f)
                    ? carrier.carrierTarget : null;
                if (nextTarget != target) burstLeft = 0;
                target = nextTarget;
            }
            else if (!carrier.carrierFireAtWill || FieldWarp.Pending(carrier))
            { target = null; burstLeft = 0; }
            else if (retargetWait == 0 || !validTarget)
            {
                // Each aircraft chooses from its own position. The home range is
                // only a leash, and ties remain ordered by durable thing IDs.
                var nextTarget = AircraftTargeting.Nearest(this, 60f, false,
                    rangeOrigin: carrier, originRange: 70f);
                if (nextTarget != target) burstLeft = 0;
                target = nextTarget; retargetWait = 30;
            }
            if (target == null) { returning = true; return; }
            float distance = Position.DistanceTo(target.Position);
            if (distance > 13f) Fly(target.Position.ToVector3Shifted());
            else
            {
                // A lemniscate has two lobes and crosses at the target. Each
                // aircraft owns and saves its phase; no render clock or RNG.
                orbitPhase = Mathf.Repeat(orbitPhase + .018f, Mathf.PI * 2f);
                var offset = new Vector3(9f * Mathf.Sin(orbitPhase), 0f,
                    5f * Mathf.Sin(2f * orbitPhase));
                var rotation = Quaternion.AngleAxis(thingIDNumber % 180, Vector3.up);
                var goal = target.Position.ToVector3Shifted() + rotation * offset;
                goal.x = Mathf.Clamp(goal.x, .5f, Map.Size.x - .5f);
                goal.z = Mathf.Clamp(goal.z, .5f, Map.Size.z - .5f);
                Fly(goal);
            }
            if (distance <= 22.9f && fireWait == 0)
            {
                if (burstLeft == 0) burstLeft = 3;
                var def = DefDatabase<ThingDef>.GetNamedSilentFail("Projectile_Gun_HeavySMG_AttackDrone")
                    ?? DefDatabase<ThingDef>.GetNamed("NMC_InterceptorFallbackRound");
                var shot = (Projectile)GenSpawn.Spawn(def, Position, Map);
                var aim = new LocalTargetInfo(target);
                shot.Launch(this, realPosition, aim, aim, ProjectileHitFlags.IntendedTarget, preventFriendlyFire: true);
                // Same shot sound as the base mod Nivarian_FrontierRifle.
                DefDatabase<SoundDef>.GetNamed("Shot_BoltActionRifle").PlayOneShot(SoundInfo.InMap(this));
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
            Scribe_Values.Look(ref orbitPhase, "nmcOrbitPhase");
            Scribe_Values.Look(ref heading, "nmcHeading"); Scribe_Values.Look(ref burstLeft, "nmcBurstLeft");
            Scribe_Values.Look(ref fireWait, "nmcFireWait"); Scribe_Values.Look(ref retargetWait, "nmcRetargetWait");
            Scribe_Values.Look(ref launchTicks, "nmcLaunchTicks"); Scribe_Values.Look(ref launchDirection, "nmcLaunchDirection");
        }
    }
}
