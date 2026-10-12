using System;
using RimWorld;
using Verse;

namespace NivarianCommander
{
    // Native turrets acquire/aim once; an established channel has its own saved,
    // map-local combat clock, so repeated turret aiming cannot break its cadence.
    public sealed class Verb_FleetRay : Verb
    {
        public Thing beamTarget;
        public int beamRemaining, pulseEvents;
        public int pulseWait, bombWait, consecutiveHits, bombEvents, flashRemaining;
        int channelVersion = 1;
        public bool Hero => caster?.def.defName == "NMC_Zoraya";
        public bool Mothership => caster?.def.defName == "NMC_TacticalMothership";
        public bool Freezing => Hero || caster?.def.defName == "NMC_VoidRay";
        public bool Channeling => beamTarget != null && beamRemaining > 0;
        public bool VisibleBeam => Channeling && (!Mothership || flashRemaining > 0);
        public int IntervalTicks => Mothership ? 60 : 30;
        public float PulseDamage => Mothership ? 9f : (Hero ? 4.8f : 2.4f) + consecutiveHits;
        public float PulsePenetration => Mothership ? .6f : Hero ? .5f : Freezing ? .4f : .3f;

        bool Valid(Thing source, Thing target)
            => source is FleetShip ship && ship.Spawned && ship.arrived && !ship.Destroyed && !ship.retiring
                && target != null && target.Spawned && !target.Destroyed && (!(target is Pawn pawn)
                    || !pawn.DeadOrDowned && (!InvisibilityUtility.IsPsychologicallyInvisible(pawn) || ship.CanEngageInvisibleTargets))
                && target.Map == source.Map && target.HostileTo(source) && !SupportDiplomacy.Friendly(target)
                && !CommanderCloak.HiddenFrom(target, source)
                && CanHitTargetFrom(source.Position, new LocalTargetInfo(target));

        protected override bool TryCastShot()
        {
            var target = currentTarget.Thing;
            if (!Valid(caster, target) || !TimeStopCombat.CanFire(this)) { ClearBeam(); return false; }
            if (beamTarget != target) { ClearBeam(); beamTarget = target; }
            beamRemaining = 1;
            FireDue();
            return true;
        }

        public void CombatTick(FleetShip ship, LocalTargetInfo target)
        {
            if (Channeling && (target.Thing != beamTarget || !Valid(ship, beamTarget))) ClearBeam();
            if (!ship.Spawned || ship.retiring || !ship.arrived || !TimeStopCombat.Progress(ship)) return;
            if (pulseWait > 0) pulseWait--;
            if (bombWait > 0) bombWait--;
            if (flashRemaining > 0) flashRemaining--;
            if (Channeling && TimeStopCombat.CanFire(this)) FireDue();
        }

        void FireDue()
        {
            // Bombs use their own clock and launch first: a lethal simultaneous
            // laser hit must not suppress a bomb already due on the same tick.
            if (Mothership && bombWait == 0)
            {
                bombWait = 120; bombEvents++;
                var projectile = (Projectile)GenSpawn.Spawn(
                    DefDatabase<ThingDef>.GetNamed("NMC_FleetMothershipBolt"), caster.Position, caster.Map);
                var target = new LocalTargetInfo(beamTarget);
                projectile.Launch(caster, caster.Position.ToVector3Shifted(), target, target,
                    ProjectileHitFlags.IntendedTarget, preventFriendlyFire: true, equipment: EquipmentSource);
            }
            if (pulseWait > 0) return;
            pulseWait = IntervalTicks; pulseEvents++; flashRemaining = 36;
            var victim = beamTarget;
            string damageName = Mothership ? "NMC_FleetMothershipLaser" : Freezing ? "NMC_FleetRayEnergy" : "NMC_FleetDestroyerEnergy";
            // Simulation uses cells, never interpolated DrawPos or camera state.
            float angle = (victim.Position - caster.Position).ToVector3().AngleFlat();
            // Capture a visible shot before a lethal hit despawns its endpoint.
            caster.Map.GetComponent<FleetPrismVisuals>().Show(this);
            victim.TakeDamage(new DamageInfo(DefDatabase<DamageDef>.GetNamed(damageName), PulseDamage,
                PulsePenetration, angle, caster, weapon: EquipmentSource?.def));
            if (!Mothership && consecutiveHits < int.MaxValue - 8) consecutiveHits++;
            if (Freezing && victim is Pawn pawn && !pawn.Dead && !pawn.Destroyed && Rand.Chance(.65f))
                // Exact Prism Catawand status application: 65%, +0.55 CryoSlow.
                HealthUtility.AdjustSeverity(pawn, DefDatabase<HediffDef>.GetNamed("Nivarian_Hediff_CryoSlow"), .55f);
            if (victim.Destroyed || !victim.Spawned || victim is Pawn dead && dead.Dead) ClearBeam(true);
        }

        public void ClearBeam(bool finishingHit = false)
        {
            beamTarget = null; beamRemaining = 0; consecutiveHits = 0; flashRemaining = 0;
            // Keep weapon clocks on a target switch, preventing rapid retargeting
            // from producing extra initial hits/bombs inside the fixed intervals.
            var visuals = caster?.Map?.GetComponent<FleetPrismVisuals>();
            if (finishingHit) visuals?.FinishHit(this); else visuals?.Forget(this);
        }
        public override void Reset() { ClearBeam(); base.Reset(); }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_References.Look(ref beamTarget, "fleetRayTarget");
            Scribe_Values.Look(ref beamRemaining, "fleetRayRemaining");
            Scribe_Values.Look(ref pulseEvents, "fleetRayPulseEvents");
            Scribe_Values.Look(ref pulseWait, "fleetRayPulseWait");
            Scribe_Values.Look(ref bombWait, "fleetBombWait");
            Scribe_Values.Look(ref consecutiveHits, "fleetRayConsecutiveHits");
            Scribe_Values.Look(ref bombEvents, "fleetBombEvents");
            Scribe_Values.Look(ref flashRemaining, "fleetRayFlashRemaining");
            Scribe_Values.Look(ref channelVersion, "fleetRayChannelVersion");
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                // Old continuous-ray saves stored the remaining 30-tick visible
                // segment but no independent pulse clock. Preserve that delay.
                if (channelVersion == 0 && beamTarget != null && beamRemaining > 0)
                    pulseWait = Math.Max(1, Math.Min(30, beamRemaining));
                channelVersion = 1;
                pulseWait = Math.Max(0, Math.Min(60, pulseWait));
                bombWait = Math.Max(0, Math.Min(120, bombWait));
                consecutiveHits = Math.Max(0, Math.Min(int.MaxValue - 8, consecutiveHits));
                if (beamTarget == null) { beamRemaining = 0; consecutiveHits = 0; }
            }
        }

        public void DrawBeam(Thing source)
        {
            if (source.Spawned && source.Map == Find.CurrentMap)
                source.Map.GetComponent<FleetPrismVisuals>().Show(this);
        }
    }
}
