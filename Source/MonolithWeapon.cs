using System.Collections.Generic;
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
        List<Thing> selectedTargets = new List<Thing>();
        static List<ThingDef> extraTargetDefs;
        CompPowerTrader power;
        // Local rendering cache only; never serialized or used to choose a target.
        MonolithTurretVisuals turretVisuals;
        internal MonolithTurretVisuals TurretVisuals => turretVisuals ?? (turretVisuals = new MonolithTurretVisuals());
        public IReadOnlyList<Thing> LastTargets => lastTargets;
        public CompProperties_MonolithWeapon Props => (CompProperties_MonolithWeapon)props;
        public bool Powered => (power ?? (power = parent.GetComp<CompPowerTrader>()))?.PowerOn == true;
        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            power = parent.GetComp<CompPowerTrader>();
        }
        public override void CompTick()
        {
            if (beamTicks > 0) beamTicks--;
            if (ProjectionRegistry.BlocksFire(parent) || !ProjectionResources.CanFire(parent)) return;
            RemoveInvalidLastTargets();
            bool waveActive = SpacetimeTurretTimers.ActiveFor(parent);
            if (waveActive != waveCadenceActive)
            {
                weaponWait = waveActive ? Mathf.CeilToInt(weaponWait / (float)SpacetimeWaveStats.TurretMultiplier)
                    : Mathf.Min(Props.intervalTicks, weaponWait * SpacetimeWaveStats.TurretMultiplier);
                waveCadenceActive = waveActive;
            }
            if (weaponWait > 0) weaponWait--;
            if (weaponWait > 0 || !Powered || !SupportDiplomacy.Friendly(parent)) return;
            var selected = SelectTargets();
            if (selected.Count == 0) return;
            // One simulation tick owns the whole pulse; no delayed per-target volley or ammunition.
            selectedTargets = lastTargets;
            lastTargets = selected; beamTicks = 12; roundsFired++;
            weaponWait = waveActive ? Mathf.Max(1, Mathf.CeilToInt(Props.intervalTicks / (float)SpacetimeWaveStats.TurretMultiplier)) : Props.intervalTicks;
            parent.Map.GetComponent<MonolithPrismVisuals>().Show(this);
            foreach (var target in selected)
            {
                target.TakeDamage(new DamageInfo(DefDatabase<DamageDef>.GetNamed("NMC_MonolithEnergy"),
                    3f, 0f, instigator: parent));
                if (!target.Destroyed && target.Spawned)
                    CommanderControl.Stun(target, Props.stunTicks, parent);
                targetsHit++;
            }
            RemoveInvalidLastTargets();
        }
        void RemoveInvalidLastTargets()
        {
            for (int i = lastTargets.Count - 1; i >= 0; i--)
            {
                var target = lastTargets[i];
                if (target == null || target.Destroyed || !target.Spawned || target.Map != parent.Map)
                    lastTargets.RemoveAt(i);
            }
        }
        // Use the game's live membership lists, including non-Pawn drones and
        // natural/custom buildings omitted by BuildingArtificial. No scan timer:
        // a new or newly hostile target can still be acquired on the next tick.
        List<Thing> SelectTargets()
        {
            selectedTargets.Clear();
            if (Props.maximumTargets <= 0) return selectedTargets;
            if (extraTargetDefs == null)
            {
                extraTargetDefs = new List<ThingDef>();
                foreach (var def in DefDatabase<ThingDef>.AllDefsListForReading)
                    if (def.thingClass != null
                        && (typeof(Pawn).IsAssignableFrom(def.thingClass)
                            || typeof(Building).IsAssignableFrom(def.thingClass)
                            || typeof(NivarianDroneBase).IsAssignableFrom(def.thingClass))
                        && !ThingRequestGroup.Pawn.Includes(def)
                        && !ThingRequestGroup.BuildingArtificial.Includes(def))
                        extraTargetDefs.Add(def);
            }
            var lister = parent.Map.listerThings;
            ConsiderTargets(lister.ThingsInGroup(ThingRequestGroup.Pawn));
            ConsiderTargets(lister.ThingsInGroup(ThingRequestGroup.BuildingArtificial));
            for (int i = 0; i < extraTargetDefs.Count; i++)
                ConsiderTargets(lister.ThingsOfDef(extraTargetDefs[i]));
            return selectedTargets;
        }
        void ConsiderTargets(List<Thing> candidates)
        {
            var origin = parent.Position;
            int maximum = Props.maximumTargets;
            for (int i = 0; i < candidates.Count; i++)
            {
                var target = candidates[i];
                if (!target.Position.InHorDistOf(origin, Props.range)) continue;
                int distance = target.Position.DistanceToSquared(origin);
                int at = selectedTargets.Count;
                // Only the nearest maximumTargets matter. Reject worse entries
                // before cloak/LOS checks; ties retain the original thing-ID order.
                while (at > 0)
                {
                    var previous = selectedTargets[at - 1];
                    int previousDistance = previous.Position.DistanceToSquared(origin);
                    if (previousDistance < distance || previousDistance == distance
                        && previous.thingIDNumber <= target.thingIDNumber) break;
                    at--;
                }
                if (at >= maximum || !ValidTarget(target)) continue;
                selectedTargets.Insert(at, target);
                if (selectedTargets.Count > maximum) selectedTargets.RemoveAt(maximum);
            }
        }
        bool ValidTarget(Thing target)
        {
            if (target == parent || target.Destroyed || !target.Spawned || target.Map != parent.Map
                || (!(target is Pawn) && !(target is Building) && !(target is NivarianDroneBase))
                || !target.Position.InHorDistOf(parent.Position, Props.range)
                || (target is Pawn p && p.DeadOrDowned)
                || !target.HostileTo(parent.Faction ?? Faction.OfPlayer) || SupportDiplomacy.Friendly(target)) return false;
            return !CommanderCloak.HiddenFrom(target, parent)
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
            => NmcText.Get("NMC_Text0287", (Powered ? NmcText.Get("NMC_Text0283") : NmcText.Get("NMC_Text0284")), (SpacetimeTurretTimers.ActiveFor(parent) ? NmcText.Get("NMC_Text0285") : NmcText.Get("NMC_Text0286")));
    }
}
