using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace NivarianCommander
{
    public static class RayActions
    {
        public static string Unavailable(Map map)
        {
            if (map == null || !Find.Maps.Contains(map)) return NmcText.Get("NMC_Text0034");
            if (map.generatorDef?.isUnderground == true) return NmcText.Get("NMC_Text0458");
            var ability = DefenseActions.Ability(5);
            var state = CommanderState.Current;
            if (state == null) return NmcText.Get("NMC_Text0432");
            if (CommanderResearch.Unavailable(ability) is string researchBlock) return researchBlock;
            if (state.Remaining(ability) > 0) return NmcText.Get("NMC_Text0035", CommanderPanel.Period(state.Remaining(ability)));
            if (NativeLedger.Balance("PowerCoin") < ability.powerCost || NativeLedger.Balance("VolatileReactive") < ability.volatileCost)
                return NmcText.Get("NMC_Text0036", ability.powerCost, ability.volatileCost);
            return null;
        }

        // The optional Multiplayer adapter synchronizes this exact execution entry point.
        public static void Start(Map map, IntVec3 requested)
        {
            string blocked = Unavailable(map);
            if (blocked != null) { Reject(blocked); return; }
            IntVec3 center = CommanderActions.ResolveCenter(map, requested);
            if (!center.IsValid) { Reject(NmcText.Get("NMC_Text0459")); return; }
            var ability = DefenseActions.Ability(5);
            if (!NativeLedger.TryPay(ability.powerCost, ability.volatileCost)) { Reject(NmcText.Get("NMC_Text0038")); return; }
            CommanderState.Current.cooldowns[ability.defName] = ability.cooldownTicks;
            int serial = ++CommanderState.Current.rayCastSerial;
            int seed = unchecked((serial * 104729) ^ (map.uniqueID * 73856093) ^
                (center.x * 19349663) ^ (center.z * 83492791));
            map.GetComponent<SolarRayMap>().Start(center, seed);
        }

        public static void Cancel(Map map)
        {
            if (map == null || !Find.Maps.Contains(map)) return;
            map.GetComponent<SolarRayMap>().CancelAll();
        }

        static void Reject(string reason)
        {
            if (CommanderState.Current != null) CommanderState.Current.requestsRejected++;
            Messages.Message(reason, MessageTypeDefOf.RejectInput, false);
        }
    }

    public sealed class RayHitRecord : IExposable
    {
        public int thingId;
        public int count;
        public RayHitRecord() { }
        public RayHitRecord(int id) { thingId = id; count = 1; }
        public void ExposeData()
        {
            Scribe_Values.Look(ref thingId, "thingId");
            Scribe_Values.Look(ref count, "count");
        }
    }

    public sealed class SolarRayBeam : IExposable
    {
        public Vector2 position;
        public Vector2 previous;
        public int lastTargetId = -1;
        public int hitEvents;
        public SolarRayBeam() { }
        public SolarRayBeam(IntVec3 center)
        {
            position = previous = new Vector2(center.x, center.z);
        }
        public void ExposeData()
        {
            Scribe_Values.Look(ref position, "position");
            Scribe_Values.Look(ref previous, "previous");
            Scribe_Values.Look(ref lastTargetId, "lastTargetId", -1);
            Scribe_Values.Look(ref hitEvents, "hitEvents");
        }
    }

    public sealed class SolarRaySalvo : IExposable
    {
        public IntVec3 center;
        public int elapsed;
        public int pulses;
        public int sequenceHash = 17;
        public int randomState;
        public List<SolarRayBeam> beams = new List<SolarRayBeam>();
        public List<RayHitRecord> hitRecords = new List<RayHitRecord>();
        public SolarRaySalvo() { }
        public SolarRaySalvo(IntVec3 center, int seed)
        {
            this.center = center; randomState = seed;
            for (int i = 0; i < 3; i++) beams.Add(new SolarRayBeam(center));
        }
        public int HitCount(int id) => hitRecords.FirstOrDefault(r => r.thingId == id)?.count ?? 0;
        public void RecordHit(int id)
        {
            var found = hitRecords.FirstOrDefault(r => r.thingId == id);
            if (found == null) hitRecords.Add(new RayHitRecord(id));
            else found.count++;
        }
        public void ExposeData()
        {
            Scribe_Values.Look(ref center, "center");
            Scribe_Values.Look(ref elapsed, "elapsed");
            Scribe_Values.Look(ref pulses, "pulses");
            Scribe_Values.Look(ref sequenceHash, "sequenceHash", 17);
            Scribe_Values.Look(ref randomState, "randomState");
            Scribe_Collections.Look(ref beams, "beams", LookMode.Deep);
            Scribe_Collections.Look(ref hitRecords, "hitRecords", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                beams = beams ?? new List<SolarRayBeam>();
                hitRecords = hitRecords ?? new List<RayHitRecord>();
            }
        }
    }

    // All path choice, motion and damage happen on this map's local ticks.
    public sealed class SolarRayMap : MapComponent
    {
        public const int Duration = 480;
        public const int Interval = 15;
        public const float SearchRadius = 18f;
        public const float StepLength = 2f; // 8 cells/second at one pulse per 15 ticks.
        public const float HitHalfWidth = 1f;
        List<SolarRaySalvo> salvos = new List<SolarRaySalvo>();
        readonly List<IntVec3> legalCells = new List<IntVec3>();
        public int lastCompletedPulses;
        public int lastCompletedHash;
        SolarRayVisuals visuals;
        public SolarRayMap(Map map) : base(map) { }
        public int ActiveCount => salvos.Count;
        public IReadOnlyList<SolarRaySalvo> ActiveSalvos => salvos;

        public void Start(IntVec3 center, int seed)
        {
            var salvo = new SolarRaySalvo(center, seed);
            salvos.Add(salvo);
            Pulse(salvo);
        }

        public void CancelAll()
        {
            visuals?.Clear();
            if (salvos.Count == 0) return;
            lastCompletedPulses = salvos[salvos.Count - 1].pulses;
            lastCompletedHash = salvos[salvos.Count - 1].sequenceHash;
            salvos.Clear();
        }

        public override void ExposeData()
        {
            Scribe_Collections.Look(ref salvos, "nmcSolarRaySalvos", LookMode.Deep);
            Scribe_Values.Look(ref lastCompletedPulses, "nmcSolarRayCompletedPulses");
            Scribe_Values.Look(ref lastCompletedHash, "nmcSolarRayCompletedHash");
            if (Scribe.mode == LoadSaveMode.PostLoadInit) salvos = salvos ?? new List<SolarRaySalvo>();
        }

        public override void MapComponentTick()
        {
            for (int i = salvos.Count - 1; i >= 0; i--)
            {
                var salvo = salvos[i];
                if (++salvo.elapsed < Duration && salvo.elapsed % Interval == 0) Pulse(salvo);
                else if (salvo.elapsed >= Duration)
                {
                    lastCompletedPulses = salvo.pulses;
                    lastCompletedHash = salvo.sequenceHash;
                    salvos.RemoveAt(i);
                }
            }
            visuals?.Tick(salvos);
        }

        bool Legal(SolarRaySalvo salvo, Vector2 point)
        {
            if ((point - new Vector2(salvo.center.x, salvo.center.z)).sqrMagnitude > SearchRadius * SearchRadius) return false;
            var cell = new IntVec3(Mathf.RoundToInt(point.x), 0, Mathf.RoundToInt(point.y));
            return cell.InBounds(map) && cell.GetRoof(map)?.isThickRoof != true;
        }

        static uint Next(SolarRaySalvo salvo)
        {
            uint state = unchecked((uint)salvo.randomState);
            state = unchecked(state * 1664525u + 1013904223u);
            salvo.randomState = unchecked((int)state);
            return state;
        }

        static int RandomIndex(SolarRaySalvo salvo, int count)
        {
            uint n = (uint)count;
            uint threshold = unchecked(0u - n) % n;
            uint sample;
            do { sample = Next(salvo); } while (sample < threshold);
            return (int)(sample % n);
        }

        Pawn SelectTarget(SolarRaySalvo salvo, SolarRayBeam beam, HashSet<int> claimed)
        {
            var available = map.mapPawns.AllPawnsSpawned
                .Where(p => !p.Dead && !p.Destroyed && p.Position.InHorDistOf(salvo.center, SearchRadius) &&
                    p.Position.GetRoof(map)?.isThickRoof != true && !SupportDiplomacy.Friendly(p))
                .OrderBy(p => p.thingIDNumber).ToList();
            if (available.Count == 0) return null;
            var enemies = available.Where(p => p.HostileTo(Faction.OfPlayer)).ToList();
            if (enemies.Count > 0) available = enemies;
            // Reserve a distinct target per pulse and retain a useful crossing lane until
            // accumulated hits justify another route. Stable ID order precedes local RNG ties.
            Func<Pawn, int> score = p => (claimed.Contains(p.thingIDNumber) ? 100000 : 0) +
                salvo.HitCount(p.thingIDNumber) * 1000 +
                Mathf.RoundToInt((new Vector2(p.Position.x, p.Position.z) - beam.position).sqrMagnitude) -
                (beam.lastTargetId == p.thingIDNumber ? 3000 : 0);
            int best = available.Min(score);
            var ties = available.Where(p => score(p) == best).ToList();
            return ties[RandomIndex(salvo, ties.Count)];
        }

        Vector2 RandomLegalPoint(SolarRaySalvo salvo)
        {
            legalCells.Clear();
            foreach (IntVec3 cell in GenRadial.RadialCellsAround(salvo.center, SearchRadius, true))
                if (cell.InBounds(map) && cell.InHorDistOf(salvo.center, SearchRadius) &&
                    cell.GetRoof(map)?.isThickRoof != true) legalCells.Add(cell);
            if (legalCells.Count == 0) return new Vector2(salvo.center.x, salvo.center.z);
            var chosen = legalCells[RandomIndex(salvo, legalCells.Count)];
            return new Vector2(chosen.x, chosen.z);
        }

        Vector2 Step(SolarRaySalvo salvo, SolarRayBeam beam, Vector2 target, int beamIndex)
        {
            Vector2 toTarget = target - beam.position;
            Vector2 direction;
            if (toTarget.sqrMagnitude < .0001f)
            {
                float angle = beamIndex * Mathf.PI * 2f / 3f;
                direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            }
            else direction = toTarget.normalized;
            // Crossing a nearby target carries the beam beyond it instead of parking on the pawn.
            Vector2 intended = beam.position + direction * StepLength;
            if (toTarget.magnitude < StepLength && toTarget.sqrMagnitude >= .0001f)
                intended = target + direction * (StepLength - toTarget.magnitude);
            for (int n = 8; n >= 1; n--)
            {
                Vector2 candidate = Vector2.Lerp(beam.position, intended, n / 8f);
                if (Legal(salvo, candidate)) return candidate;
            }
            return beam.position;
        }

        static float DistanceToSegment(Vector2 point, Vector2 a, Vector2 b)
        {
            Vector2 delta = b - a;
            if (delta.sqrMagnitude < .0001f) return Vector2.Distance(point, a);
            float t = Mathf.Clamp01(Vector2.Dot(point - a, delta) / delta.sqrMagnitude);
            return Vector2.Distance(point, a + delta * t);
        }

        void HitPath(SolarRaySalvo salvo, SolarRayBeam beam)
        {
            DamageDef energy = DefDatabase<DamageDef>.GetNamed("NMC_SolarRayEnergy");
            DamageDef burn = DefDatabase<DamageDef>.GetNamed("NMC_SolarRayBurn");
            foreach (Thing thing in map.listerThings.AllThings.Where(t => t is Pawn || t is Building)
                .OrderBy(t => t.thingIDNumber).ToList())
            {
                if (!thing.Spawned || thing.Destroyed || thing.Position.GetRoof(map)?.isThickRoof == true ||
                    thing is Pawn pawn && pawn.Dead) continue;
                var point = new Vector2(thing.Position.x, thing.Position.z);
                if (DistanceToSegment(point, beam.previous, beam.position) > HitHalfWidth) continue;
                thing.TakeDamage(new DamageInfo(energy, 10f, .30f));
                if (!thing.Destroyed && thing.Spawned) thing.TakeDamage(new DamageInfo(burn, 5f, 0f));
                salvo.RecordHit(thing.thingIDNumber);
                beam.hitEvents++;
                salvo.sequenceHash = unchecked((salvo.sequenceHash * 31 + thing.thingIDNumber) * 31 + beam.hitEvents);
            }
        }

        void Pulse(SolarRaySalvo salvo)
        {
            var claimed = new HashSet<int>();
            for (int i = 0; i < salvo.beams.Count; i++)
            {
                var beam = salvo.beams[i];
                var target = SelectTarget(salvo, beam, claimed);
                beam.lastTargetId = target?.thingIDNumber ?? -1;
                if (target != null) claimed.Add(target.thingIDNumber);
                Vector2 destination = target == null ? RandomLegalPoint(salvo) :
                    new Vector2(target.Position.x, target.Position.z);
                beam.previous = beam.position;
                beam.position = Step(salvo, beam, destination, i);
                salvo.sequenceHash = unchecked((salvo.sequenceHash * 31 + i) * 31 +
                    Mathf.RoundToInt(beam.position.x * 1000f) * 31 + Mathf.RoundToInt(beam.position.y * 1000f));
                HitPath(salvo, beam);
            }
            salvo.pulses++;
        }

        public override void MapComponentUpdate()
        {
            if (Find.CurrentMap != map)
            {
                visuals?.Dispose();
                visuals = null;
                return;
            }
            if (salvos.Count == 0 && (visuals == null || !visuals.HasResidue))
            {
                visuals?.Dispose();
                visuals = null;
                return;
            }
            if (visuals == null) visuals = new SolarRayVisuals(map);
            visuals.Draw(salvos);
        }

        public override void MapRemoved() { visuals?.Dispose(); visuals = null; }
    }
}
