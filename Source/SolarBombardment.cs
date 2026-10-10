using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace NivarianCommander
{
    public static class SolarActions
    {
        public static string Unavailable(Map map)
        {
            if (map == null || !Find.Maps.Contains(map)) return NmcText.Get("NMC_Text0034");
            if (map.generatorDef?.isUnderground == true) return NmcText.Get("NMC_Text0455");
            var ability = DefenseActions.Ability(4);
            var state = CommanderState.Current;
            if (state == null) return NmcText.Get("NMC_Text0432");
            if (CommanderResearch.Unavailable(ability) is string researchBlock) return researchBlock;
            if (state.Remaining(ability) > 0) return NmcText.Get("NMC_Text0035", CommanderPanel.Period(state.Remaining(ability)));
            if (NativeLedger.Balance("PowerCoin") < ability.powerCost || NativeLedger.Balance("VolatileReactive") < ability.volatileCost)
                return NmcText.Get("NMC_Text0036", ability.powerCost, ability.volatileCost);
            return null;
        }

        public static void Start(Map map, IntVec3 requested)
        {
            string blocked = Unavailable(map);
            if (blocked != null) { Reject(blocked); return; }
            IntVec3 center = CommanderActions.ResolveCenter(map, requested);
            if (!center.IsValid) { Reject(NmcText.Get("NMC_Text0456")); return; }
            var ability = DefenseActions.Ability(4);
            if (!NativeLedger.TryPay(ability.powerCost, ability.volatileCost)) { Reject(NmcText.Get("NMC_Text0038")); return; }
            CommanderState.Current.cooldowns[ability.defName] = ability.cooldownTicks;
            int serial = ++CommanderState.Current.solarCastSerial;
            int seed = unchecked(((serial * 73856093) ^ (map.uniqueID * 19349663) ^
                (center.x * 83492791) ^ (center.z * 104729)));
            map.GetComponent<SolarBombardmentMap>().Start(center, seed);
        }

        static void Reject(string reason)
        {
            if (CommanderState.Current != null) CommanderState.Current.requestsRejected++;
            Messages.Message(reason, MessageTypeDefOf.RejectInput, false);
        }
    }

    public sealed class SolarSalvo : IExposable
    {
        public IntVec3 center;
        public int elapsed;
        public int fired;
        public int sequenceHash = 17;
        public int randomState;
        public SolarSalvo() { }
        public SolarSalvo(IntVec3 center, int seed) { this.center = center; randomState = seed; }
        public void ExposeData()
        {
            Scribe_Values.Look(ref center, "center");
            Scribe_Values.Look(ref elapsed, "elapsed");
            Scribe_Values.Look(ref fired, "fired");
            Scribe_Values.Look(ref sequenceHash, "sequenceHash", 17);
            Scribe_Values.Look(ref randomState, "randomState");
        }
    }

    public sealed class SolarImpact : IExposable
    {
        public IntVec3 cell;
        public int remaining = 32;
        public SolarImpact() { }
        public SolarImpact(IntVec3 cell) { this.cell = cell; }
        public void ExposeData()
        {
            Scribe_Values.Look(ref cell, "cell");
            Scribe_Values.Look(ref remaining, "remaining", 32);
        }
    }

    // The effect advances only when its own map ticks. No render path consumes Rand.
    public sealed class SolarBombardmentMap : MapComponent
    {
        public const int ShotCount = 300;
        public const int Duration = 1800;
        public const int Interval = 6;
        readonly List<IntVec3> legalCells = new List<IntVec3>();
        List<SolarSalvo> salvos = new List<SolarSalvo>();
        List<SolarImpact> impacts = new List<SolarImpact>();
        public int lastCompletedShots;
        public int lastCompletedHash;
        static Material beamOuter, beamCore, impactRing;

        public SolarBombardmentMap(Map map) : base(map) { }
        public int ActiveCount => salvos.Count;
        public int FiredCount { get { int n = 0; foreach (var salvo in salvos) n += salvo.fired; return n; } }
        public IReadOnlyList<SolarSalvo> ActiveSalvos => salvos;

        public void Start(IntVec3 center, int seed)
        {
            var salvo = new SolarSalvo(center, seed);
            salvos.Add(salvo);
            if (!FireNext(salvo)) salvos.Remove(salvo);
        }

        public override void ExposeData()
        {
            Scribe_Collections.Look(ref salvos, "nmcSolarSalvos", LookMode.Deep);
            Scribe_Collections.Look(ref impacts, "nmcSolarImpacts", LookMode.Deep);
            Scribe_Values.Look(ref lastCompletedShots, "nmcSolarLastCompletedShots");
            Scribe_Values.Look(ref lastCompletedHash, "nmcSolarLastCompletedHash");
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                salvos = salvos ?? new List<SolarSalvo>();
                impacts = impacts ?? new List<SolarImpact>();
            }
        }

        public override void MapComponentTick()
        {
            for (int i = impacts.Count - 1; i >= 0; i--) if (--impacts[i].remaining <= 0) impacts.RemoveAt(i);
            for (int i = salvos.Count - 1; i >= 0; i--)
            {
                var salvo = salvos[i];
                if (++salvo.elapsed < Duration && salvo.elapsed % Interval == 0 && !FireNext(salvo)) Complete(i);
                else if (salvo.elapsed >= Duration) Complete(i);
            }
        }

        void Complete(int index)
        {
            lastCompletedShots = salvos[index].fired;
            lastCompletedHash = salvos[index].sequenceHash;
            salvos.RemoveAt(index);
        }

        bool FireNext(SolarSalvo salvo)
        {
            if (salvo.fired >= ShotCount) return true;
            legalCells.Clear();
            foreach (IntVec3 cell in GenRadial.RadialCellsAround(salvo.center, 13f, true))
                if (cell.InBounds(map) && cell.InHorDistOf(salvo.center, 13f) && cell.GetRoof(map)?.isThickRoof != true)
                    legalCells.Add(cell);
            if (legalCells.Count == 0)
            {
                Messages.Message(NmcText.Get("NMC_Text0457"), MessageTypeDefOf.RejectInput, false);
                return false;
            }
            // A full-period local generator makes later impacts independent of AI/render/load Rand usage.
            // Rejection sampling removes modulo bias so each currently legal cell is equally likely.
            uint state = unchecked((uint)salvo.randomState);
            uint count = (uint)legalCells.Count;
            uint threshold = unchecked(0u - count) % count;
            uint sample;
            do { state = unchecked(state * 1664525u + 1013904223u); sample = state; }
            while (sample < threshold);
            salvo.randomState = unchecked((int)state);
            IntVec3 impact = legalCells[(int)(sample % count)];
            salvo.fired++;
            salvo.sequenceHash = unchecked((salvo.sequenceHash * 31 + impact.x) * 31 + impact.z);
            impacts.Add(new SolarImpact(impact));
            var damage = DefDatabase<DamageDef>.GetNamed("NMC_SolarBombEnergy");
            GenExplosion.DoExplosion(impact, map, 5f, damage, null,
                damAmount: damage.defaultDamage, armorPenetration: .30f, chanceToStartFire: 0f, damageFalloff: false,
                applyDamageToExplosionCellsNeighbors: false);
            return true;
        }

        public override void MapComponentUpdate()
        {
            if (Find.CurrentMap != map || (salvos.Count == 0 && impacts.Count == 0)) return;
            if (beamOuter == null)
            {
                beamOuter = SolidColorMaterials.SimpleSolidColorMaterial(new Color(.13f, .65f, .9f, .32f), true);
                beamCore = SolidColorMaterials.SimpleSolidColorMaterial(new Color(1f, .9f, .52f, .9f), true);
                impactRing = SolidColorMaterials.SimpleSolidColorMaterial(new Color(.4f, .88f, 1f, .8f), true);
            }
            foreach (var salvo in salvos) GenDraw.DrawRadiusRing(salvo.center, 13f, new Color(.22f, .77f, .95f, .4f));
            foreach (var pulse in impacts)
            {
                float fade = Mathf.Clamp01(pulse.remaining / 12f);
                var at = pulse.cell.ToVector3Shifted(); at.y = AltitudeLayer.MetaOverlays.AltitudeFor();
                var beam = at + new Vector3(0, 0, 13);
                Graphics.DrawMesh(MeshPool.plane10, Matrix4x4.TRS(beam, Quaternion.identity, new Vector3(.8f * fade, 1, 26)), beamOuter, 0);
                beam.y += .01f;
                Graphics.DrawMesh(MeshPool.plane10, Matrix4x4.TRS(beam, Quaternion.identity, new Vector3(.16f * fade, 1, 26)), beamCore, 0);
                GenDraw.DrawCircleOutline(at, 5f * (1f - pulse.remaining / 48f), impactRing);
            }
        }
    }

    public sealed class DamageWorker_SolarBombEnergy : DamageWorker_AddInjury
    {
        public override void ExplosionAffectCell(Explosion explosion, IntVec3 cell, List<Thing> damagedThings, List<Thing> ignoredThings, bool canThrowMotes)
        {
            if (!cell.InBounds(explosion.Map) || cell.GetRoof(explosion.Map)?.isThickRoof == true) return;
            int original = explosion.damAmount;
            try
            {
                // The old 60-to-30 radial curve becomes flat when capped at 30.
                // Keep any already lower base amount; never raise it to the cap.
                explosion.damAmount = Mathf.Min(original, def.defaultDamage);
                base.ExplosionAffectCell(explosion, cell, damagedThings, ignoredThings, canThrowMotes);
            }
            finally { explosion.damAmount = original; }
        }
    }
}
