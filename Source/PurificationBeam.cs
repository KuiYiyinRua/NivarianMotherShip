using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace NivarianCommander
{
    public static class PurificationActions
    {
        public static string Unavailable(Map map)
        {
            if (map == null || !Find.Maps.Contains(map)) return NmcText.Get("NMC_Text0034");
            if (map.generatorDef?.isUnderground == true) return NmcText.Get("NMC_Text0431");
            var ability = DefenseActions.Ability(7);
            var state = CommanderState.Current;
            if (state == null) return NmcText.Get("NMC_Text0432");
            if (CommanderResearch.Unavailable(ability) is string researchBlock) return researchBlock;
            if (state.Remaining(ability) > 0) return NmcText.Get("NMC_Text0035", CommanderPanel.Period(state.Remaining(ability)));
            if (NativeLedger.Balance("PowerCoin") < ability.powerCost || NativeLedger.Balance("VolatileReactive") < ability.volatileCost)
                return NmcText.Get("NMC_Text0036", ability.powerCost, ability.volatileCost);
            return null;
        }

        // The MP adapter synchronizes only these command entries; targeting UI stays local.
        public static void Start(Map map, IntVec3 requested)
        {
            string blocked = Unavailable(map);
            if (blocked != null) { Reject(blocked); return; }
            IntVec3 center = CommanderActions.ResolveCenter(map, requested);
            if (!center.IsValid) { Reject(NmcText.Get("NMC_Text0433")); return; }
            var ability = DefenseActions.Ability(7);
            if (!NativeLedger.TryPay(ability.powerCost, ability.volatileCost)) { Reject(NmcText.Get("NMC_Text0038")); return; }
            CommanderState.Current.cooldowns[ability.defName] = ability.cooldownTicks;
            int serial = ++CommanderState.Current.purificationCastSerial;
            map.GetComponent<PurificationBeamMap>().Start(center, serial);
        }

        public static void Draft(Map map, int serial, bool drafted)
        {
            if (map == null || !Find.Maps.Contains(map)) return;
            map.GetComponent<PurificationBeamMap>().SetDraft(serial, drafted);
        }

        public static void Move(Map map, int serial, IntVec3 destination)
        {
            if (map == null || !Find.Maps.Contains(map) || !destination.InBounds(map)) return;
            map.GetComponent<PurificationBeamMap>().SetDestination(serial, destination);
        }

        public static void Cancel(Map map)
        {
            if (map == null || !Find.Maps.Contains(map)) return;
            map.GetComponent<PurificationBeamMap>().CancelAll();
        }

        static void Reject(string reason)
        {
            if (CommanderState.Current != null) CommanderState.Current.requestsRejected++;
            Messages.Message(reason, MessageTypeDefOf.RejectInput, false);
        }
    }

    public sealed class PurificationBeam : IExposable
    {
        public int serial;
        public Vector2 position;
        public Vector2 destination;
        public bool drafted;
        public int elapsed;
        public int pulses;
        public int targetId = -1;
        public int hitEvents;
        public int sequenceHash = 17;
        public PurificationBeam() { }
        public PurificationBeam(IntVec3 center, int castSerial)
        {
            serial = castSerial;
            position = destination = new Vector2(center.x, center.z);
        }
        public void ExposeData()
        {
            Scribe_Values.Look(ref serial, "serial");
            Scribe_Values.Look(ref position, "position");
            Scribe_Values.Look(ref destination, "destination");
            Scribe_Values.Look(ref drafted, "drafted");
            Scribe_Values.Look(ref elapsed, "elapsed");
            Scribe_Values.Look(ref pulses, "pulses");
            Scribe_Values.Look(ref targetId, "targetId", -1);
            Scribe_Values.Look(ref hitEvents, "hitEvents");
            Scribe_Values.Look(ref sequenceHash, "sequenceHash", 17);
        }
    }

    // The beam is a saved map effect, not a pawn. It advances only on this map's local ticks.
    public sealed class PurificationBeamMap : MapComponent
    {
        public const int Duration = 1200;
        public const int DamageInterval = 30;
        public const float SearchRadius = 40f;
        public const float DamageRadius = 4f;
        public const float MovePerTick = .05f; // 3 cells per simulated second.
        List<PurificationBeam> beams = new List<PurificationBeam>();
        public int lastCompletedPulses;
        public int lastCompletedHash;
        PurificationBeamVisuals visuals;
        public PurificationBeamMap(Map map) : base(map) { }
        public IReadOnlyList<PurificationBeam> ActiveBeams => beams;
        public int ActiveCount => beams.Count;

        public void Start(IntVec3 center, int serial)
        {
            var beam = new PurificationBeam(center, serial);
            beams.Add(beam);
            // A cast visibly starts at the chosen point. Automatic target loss is checked
            // on the next local tick, so a paused map can still accept a draft command.
            DamagePulse(beam);
        }

        public void SetDraft(int serial, bool drafted)
        {
            var beam = beams.FirstOrDefault(b => b.serial == serial);
            if (beam == null) return;
            beam.drafted = drafted;
            if (drafted) { beam.destination = beam.position; beam.targetId = -1; }
        }

        public void SetDestination(int serial, IntVec3 cell)
        {
            var beam = beams.FirstOrDefault(b => b.serial == serial);
            if (beam == null || !beam.drafted) return;
            beam.destination = new Vector2(cell.x, cell.z);
        }

        public void CancelAll()
        {
            visuals?.Clear();
            if (beams.Count == 0) return;
            lastCompletedPulses = beams[beams.Count - 1].pulses;
            lastCompletedHash = beams[beams.Count - 1].sequenceHash;
            beams.Clear();
        }

        public override void ExposeData()
        {
            Scribe_Collections.Look(ref beams, "nmcPurificationBeams", LookMode.Deep);
            Scribe_Values.Look(ref lastCompletedPulses, "nmcPurificationCompletedPulses");
            Scribe_Values.Look(ref lastCompletedHash, "nmcPurificationCompletedHash");
            if (Scribe.mode == LoadSaveMode.PostLoadInit) beams = beams ?? new List<PurificationBeam>();
        }

        Pawn FindEnemy(PurificationBeam beam)
        {
            return map.mapPawns.AllPawnsSpawned
                .Where(p => !p.DeadOrDowned && !p.Destroyed && p.HostileTo(Faction.OfPlayer) &&
                    p.Position.GetRoof(map)?.isThickRoof != true &&
                    (new Vector2(p.Position.x, p.Position.z) - beam.position).sqrMagnitude <= SearchRadius * SearchRadius)
                .OrderBy(p => (new Vector2(p.Position.x, p.Position.z) - beam.position).sqrMagnitude)
                .ThenBy(p => p.thingIDNumber).FirstOrDefault();
        }

        void CompleteAt(int index)
        {
            var beam = beams[index];
            visuals?.Finish(beam);
            lastCompletedPulses = beam.pulses;
            lastCompletedHash = beam.sequenceHash;
            beams.RemoveAt(index);
        }

        public override void MapComponentTick()
        {
            for (int i = beams.Count - 1; i >= 0; i--)
            {
                var beam = beams[i];
                if (++beam.elapsed >= Duration) { CompleteAt(i); continue; }
                if (!beam.drafted)
                {
                    var target = FindEnemy(beam);
                    if (target == null) { CompleteAt(i); continue; }
                    beam.targetId = target.thingIDNumber;
                    beam.destination = new Vector2(target.Position.x, target.Position.z);
                }
                Vector2 direction = beam.destination - beam.position;
                if (direction.sqrMagnitude > MovePerTick * MovePerTick) beam.position += direction.normalized * MovePerTick;
                else beam.position = beam.destination;
                beam.sequenceHash = unchecked((beam.sequenceHash * 31 + Mathf.RoundToInt(beam.position.x * 1000f)) * 31 +
                    Mathf.RoundToInt(beam.position.y * 1000f));
                if (beam.elapsed % DamageInterval == 0) DamagePulse(beam);
            }
            visuals?.Tick(beams);
        }

        void DamagePulse(PurificationBeam beam)
        {
            DamageDef energy = DefDatabase<DamageDef>.GetNamed("NMC_PurificationEnergy");
            DamageDef burn = DefDatabase<DamageDef>.GetNamed("NMC_PurificationBurn");
            foreach (Thing thing in map.listerThings.AllThings.Where(t => t is Pawn || t is Building)
                .OrderBy(t => t.thingIDNumber).ToList())
            {
                if (!thing.Spawned || thing.Destroyed || thing.Position.GetRoof(map)?.isThickRoof == true ||
                    thing is Pawn pawn && pawn.Dead) continue;
                Vector2 point = new Vector2(thing.Position.x, thing.Position.z);
                if ((point - beam.position).sqrMagnitude > DamageRadius * DamageRadius) continue;
                thing.TakeDamage(new DamageInfo(energy, 25f, .40f));
                if (!thing.Destroyed && thing.Spawned) thing.TakeDamage(new DamageInfo(burn, 10f, 0f));
                beam.hitEvents++;
                beam.sequenceHash = unchecked((beam.sequenceHash * 31 + thing.thingIDNumber) * 31 + beam.hitEvents);
            }
            beam.pulses++;
        }

        public override void MapComponentUpdate()
        {
            if (!PurificationBeamVisuals.CanView(map))
            {
                visuals?.Dispose();
                visuals = null;
                return;
            }
            if (visuals?.Disposed == true) visuals = null;
            if (beams.Count == 0 && (visuals == null || !visuals.HasResidue))
            {
                visuals?.Dispose();
                visuals = null;
                return;
            }
            if (visuals == null) visuals = new PurificationBeamVisuals(map);
            visuals.Draw(beams);
            foreach (var beam in beams)
            {
                // Keep the existing manual-move command guide as a UI overlay.
                if (!beam.drafted) continue;
                Vector3 at = new Vector3(beam.position.x + .5f, AltitudeLayer.MetaOverlays.AltitudeFor(), beam.position.y + .5f);
                GenDraw.DrawLineBetween(at, new Vector3(beam.destination.x + .5f, at.y,
                    beam.destination.y + .5f), SimpleColor.Cyan, .09f);
            }
        }

        public override void MapRemoved() { visuals?.Dispose(); visuals = null; }
    }
}
