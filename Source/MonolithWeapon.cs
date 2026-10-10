using System.Collections.Generic;
using System.Linq;
using Nivarian.NivarianDrones;
using RimWorld;
using UnityEngine;
using Verse;

namespace NivarianCommander
{
    public sealed class CompProperties_MonolithWeapon : CompProperties
    {
        public float range = 60;
        public int intervalTicks = 120, maximumTargets = 10, stunTicks = 60;
        public GraphicData turretGraphic;
        // Normalized pixel pivot, measured from the upper-left of the texture.
        public Vector2 turretPivot = new Vector2(.5f, .5f);
        public float turretMuzzleOffset = 1.25f;
        public CompProperties_MonolithWeapon() { compClass = typeof(CompMonolithWeapon); }
    }
    public sealed class CompMonolithWeapon : ThingComp
    {
        public int weaponWait, roundsFired, targetsHit, beamTicks;
        public bool waveCadenceActive;
        List<Thing> lastTargets = new List<Thing>();
        // Local rendering cache only; never serialized or used to choose a target.
        MonolithTurretVisuals turretVisuals;
        internal MonolithTurretVisuals TurretVisuals => turretVisuals ?? (turretVisuals = new MonolithTurretVisuals());
        public IReadOnlyList<Thing> LastTargets => lastTargets;
        public CompProperties_MonolithWeapon Props => (CompProperties_MonolithWeapon)props;
        public bool Powered => parent.GetComp<CompPowerTrader>()?.PowerOn == true;
        public override void CompTick()
        {
            if (beamTicks > 0) beamTicks--;
            if (ProjectionRegistry.BlocksFire(parent) || !ProjectionResources.CanFire(parent)) return;
            lastTargets.RemoveAll(t => t == null || t.Destroyed || !t.Spawned || t.Map != parent.Map);
            bool waveActive = SpacetimeWaveMap.ActiveFor(parent.Map);
            if (waveActive != waveCadenceActive)
            {
                weaponWait = waveActive ? Mathf.CeilToInt(weaponWait / 5f)
                    : Mathf.Min(Props.intervalTicks, weaponWait * 5);
                waveCadenceActive = waveActive;
            }
            if (weaponWait > 0) weaponWait--;
            if (!Powered || weaponWait > 0 || !SupportDiplomacy.Friendly(parent)) return;
            var selected = parent.Map.listerThings.AllThings.Where(ValidTarget)
                .OrderBy(t => t.Position.DistanceToSquared(parent.Position)).ThenBy(t => t.thingIDNumber)
                .Take(Props.maximumTargets).ToList();
            if (selected.Count == 0) return;
            // One simulation tick owns the whole pulse; no delayed per-target volley or ammunition.
            lastTargets = selected; beamTicks = 12; roundsFired++;
            weaponWait = waveActive ? Mathf.Max(1, Props.intervalTicks / 5) : Props.intervalTicks;
            parent.Map.GetComponent<MonolithPrismVisuals>().Show(this);
            foreach (var target in selected)
            {
                target.TakeDamage(new DamageInfo(DefDatabase<DamageDef>.GetNamed("NMC_MonolithEnergy"),
                    3f, 0f, instigator: parent));
                if (!target.Destroyed && target.Spawned)
                    CommanderControl.Stun(target, Props.stunTicks, parent);
                targetsHit++;
            }
            lastTargets.RemoveAll(t => t.Destroyed || !t.Spawned || t.Map != parent.Map);
        }
        bool ValidTarget(Thing target)
        {
            if (target == parent || target.Destroyed || !target.Spawned || target.Map != parent.Map
                || (!(target is Pawn) && !(target is Building) && !(target is NivarianDroneBase))
                || (target is Pawn p && p.Dead) || CommanderCloak.HiddenFrom(target, parent)
                || !target.HostileTo(parent.Faction ?? Faction.OfPlayer) || SupportDiplomacy.Friendly(target)) return false;
            return target.Position.InHorDistOf(parent.Position, Props.range)
                && GenSight.LineOfSight(parent.Position, target.Position, parent.Map, skipFirstCell: true);
        }
        public override void PostDraw()
        {
            if (!parent.Spawned || parent.Position.Fogged(parent.Map)) return;
            TurretVisuals.Draw(this);
            if (beamTicks > 0) parent.Map.GetComponent<MonolithPrismVisuals>().Show(this);
        }
        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref weaponWait, "nmcMonolithWeaponWait");
            Scribe_Values.Look(ref waveCadenceActive, "nmcMonolithWaveCadenceActive");
            Scribe_Values.Look(ref roundsFired, "nmcMonolithRoundsFired");
            Scribe_Values.Look(ref targetsHit, "nmcMonolithTargetsHit");
            Scribe_Values.Look(ref beamTicks, "nmcMonolithBeamTicks");
            Scribe_Collections.Look(ref lastTargets, "nmcMonolithLastTargets", LookMode.Reference);
            if (Scribe.mode == LoadSaveMode.PostLoadInit) lastTargets = lastTargets ?? new List<Thing>();
        }
        public override string CompInspectStringExtra()
            => NmcText.Get("NMC_Text0287", (Powered ? NmcText.Get("NMC_Text0283") : NmcText.Get("NMC_Text0284")), (SpacetimeWaveMap.ActiveFor(parent.Map) ? NmcText.Get("NMC_Text0285") : NmcText.Get("NMC_Text0286")));
    }
}
