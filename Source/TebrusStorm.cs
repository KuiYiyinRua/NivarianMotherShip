using System;
using System.Collections.Generic;
using System.Linq;
using Nivarian.NivarianDrones;
using RimWorld;
using Verse;
using Verse.AI;
namespace NivarianCommander
{
    public static class TebrusStorm
    {
        public const float Radius = 6f, Range = 30f, FocusCost = 35f;
        public const int Duration = 240, Interval = 60, Cooldown = 720;
        public const float DamagePerPulse = 30f, HealingPerPulse = 30f;
        public static bool CasterReady(Pawn pawn)
        {
            var marker = Hediff_LegionIdentity.For(pawn);
            return marker?.hero == true && marker.legionAbility == 14 && pawn.Spawned && !pawn.Destroyed && !pawn.Dead && !pawn.Downed
                && pawn.Map != null && !pawn.stances.stunner.Stunned && TimeStopCombat.Progress(pawn);
        }
        public static bool Legal(Thing thing, Pawn caster, Map map)
            => thing != null && caster != null && thing != caster && thing.Spawned && !thing.Destroyed && thing.Map == map
                && (thing is Pawn enemy ? !enemy.DeadOrDowned : thing is NivarianDroneBase)
                && thing.HostileTo(caster.Faction) && thing.HostileTo(Faction.OfPlayer)
                && !SupportDiplomacy.Friendly(thing) && !CommanderCloak.HiddenFrom(thing, caster);
        public static List<Thing> Targets(Pawn caster, Map map, IntVec3 center)
            => map.listerThings.AllThings.Where(t => Legal(t, caster, map) && t.Position.DistanceToSquared(center) <= Radius * Radius)
                .Distinct().OrderBy(t => t.thingIDNumber).ToList();
        static bool LivingUnit(Thing thing, Map map)
            => thing != null && thing.Spawned && !thing.Destroyed && thing.Map == map
                && (thing is Pawn pawn ? !pawn.Dead : thing is NivarianDroneBase);
        static bool FriendlyUnit(Thing thing, Pawn caster)
        {
            if (thing.HostileTo(Faction.OfPlayer) || thing is Pawn prisoner && prisoner.IsPrisoner) return false;
            var faction = thing.Faction;
            // Native drones derive from ThingWithComps, not Pawn/Building, so
            // SupportDiplomacy's narrower shield target filter cannot be used here.
            // Hired legions retain their NPC faction, which may be neutral to the player.
            return thing == caster || faction != null && (faction == Faction.OfPlayer
                || faction.RelationKindWith(Faction.OfPlayer) == FactionRelationKind.Ally || faction == caster.Faction)
                || thing is Pawn pawn && Hediff_LegionIdentity.For(pawn) != null;
        }
        static void HealUnit(Thing thing)
        {
            if (thing is Pawn pawn)
            {
                float remaining = HealingPerPulse;
                // Stable, largest-wound-first allocation of ONE budget per unit.
                // Natural Heal notifies health/capacities; diseases and missing parts are untouched.
                var injuries = pawn.health.hediffSet.hediffs.OfType<Hediff_Injury>()
                    .Where(h => h.Severity > 0f && !h.IsPermanent())
                    .OrderByDescending(h => h.Severity).ToList();
                foreach (var injury in injuries)
                {
                    if (remaining <= 0f || pawn.Dead || pawn.Destroyed) break;
                    float amount = Math.Min(remaining, injury.Severity);
                    injury.Heal(amount);
                    remaining -= amount;
                }
            }
            else if (thing is NivarianDroneBase && thing.def.useHitPoints && thing.HitPoints > 0)
                thing.HitPoints = Math.Min(thing.MaxHitPoints, thing.HitPoints + (int)HealingPerPulse);
        }
        internal static void Pulse(Pawn caster, Map map, IntVec3 center)
        {
            // Area effects cover downed and hidden units; visibility restricts AI acquisition only.
            var units = map.listerThings.AllThings.Where(t => LivingUnit(t, map)
                && t.Position.DistanceToSquared(center) <= Radius * Radius)
                .Distinct().OrderBy(t => t.thingIDNumber).ToList();
            var damage = DefDatabase<DamageDef>.GetNamed("NMC_TebrusStormDamage");
            foreach (var unit in units)
            {
                if (!LivingUnit(unit, map) || unit.Position.DistanceToSquared(center) > Radius * Radius) continue;
                if (FriendlyUnit(unit, caster)) HealUnit(unit);
                else if (unit != caster && unit.HostileTo(Faction.OfPlayer))
                    unit.TakeDamage(new DamageInfo(damage, DamagePerPulse, 0f, -1f, caster));
            }
        }
        public static bool CanExecute(Ability ability, IntVec3 cell)
        {
            var pawn = ability.pawn; var marker = Hediff_LegionIdentity.For(pawn);
            return CasterReady(pawn) && marker.focusBackend != LegionFocusBackend.Unselected
                && ability.def.defName == LegionFocus.AbilityName(marker) && ability.CanCast
                && LegionFocus.Amount(pawn) + .0001f >= FocusCost && cell.InBounds(pawn.Map)
                && cell.DistanceToSquared(pawn.Position) <= Range * Range
                && Targets(pawn, pawn.Map, cell).Count >= 3;
        }
        public static IntVec3 BestCell(Pawn pawn)
        {
            if (!CasterReady(pawn)) return IntVec3.Invalid;
            var possible = pawn.Map.listerThings.AllThings.Where(t => Legal(t, pawn, pawn.Map)
                && t.Position.DistanceToSquared(pawn.Position) <= (Range + Radius) * (Range + Radius)).Distinct().ToList();
            if (possible.Count < 3) return IntVec3.Invalid;
            int bestCount = 2, bestDistance = int.MaxValue; var best = IntVec3.Invalid;
            // Consider every legal range cell, including centers between units.
            // Enumeration order never decides equal coverage ties.
            foreach (var cell in GenRadial.RadialCellsAround(pawn.Position, Range, true))
            {
                if (!cell.InBounds(pawn.Map)) continue;
                int count = possible.Count(t => t.Position.DistanceToSquared(cell) <= Radius * Radius);
                int distance = cell.DistanceToSquared(pawn.Position);
                if (count > bestCount || count == bestCount && count >= 3
                    && (distance < bestDistance || distance == bestDistance && (!best.IsValid || cell.z < best.z || cell.z == best.z && cell.x < best.x)))
                { best = cell; bestCount = count; bestDistance = distance; }
            }
            return best;
        }
        public static Job CastJob(Pawn pawn)
        {
            if (!CasterReady(pawn) || LegionFocus.Amount(pawn) < FocusCost) return null;
            var marker = Hediff_LegionIdentity.For(pawn);
            var ability = pawn.abilities?.GetAbility(DefDatabase<AbilityDef>.GetNamed(LegionFocus.AbilityName(marker)));
            if (ability == null || !ability.CanCast) return null;
            var cell = BestCell(pawn);
            return cell.IsValid && CanExecute(ability, cell) ? ability.GetJob(new LocalTargetInfo(cell), LocalTargetInfo.Invalid) : null;
        }
    }
    public sealed class Ability_TebrusStormNative : Psycast
    {
        public Ability_TebrusStormNative(Pawn pawn) : base(pawn) { }
        public Ability_TebrusStormNative(Pawn pawn, AbilityDef def) : base(pawn, def) { }
        public override AcceptanceReport CanCast => pawn.psychicEntropy != null && Hediff_LegionIdentity.For(pawn)?.focusBackend == LegionFocusBackend.NativePsyfocus ? base.CanCast : false;
        public override bool Activate(LocalTargetInfo target, LocalTargetInfo dest)
            => TebrusStorm.CanExecute(this, target.Cell) && base.Activate(target, dest);
    }
    public sealed class Ability_TebrusStormFallback : Ability
    {
        public Ability_TebrusStormFallback(Pawn pawn) : base(pawn) { }
        public Ability_TebrusStormFallback(Pawn pawn, AbilityDef def) : base(pawn, def) { }
        public override AcceptanceReport CanCast => Hediff_LegionIdentity.For(pawn)?.focusBackend == LegionFocusBackend.Fallback && LegionFocus.Amount(pawn) >= TebrusStorm.FocusCost ? base.CanCast : false;
        public override bool Activate(LocalTargetInfo target, LocalTargetInfo dest)
        {
            if (!TebrusStorm.CanExecute(this, target.Cell)) return false;
            Hediff_LegionIdentity.For(pawn).fallbackFocus -= TebrusStorm.FocusCost;
            return base.Activate(target, dest);
        }
    }
    public sealed class CompProperties_TebrusStorm : CompProperties_AbilityEffect
    { public CompProperties_TebrusStorm() { compClass = typeof(CompAbilityEffect_TebrusStorm); } }
    public sealed class CompAbilityEffect_TebrusStorm : CompAbilityEffect
    {
        public override bool Valid(LocalTargetInfo target, bool throwMessages = false)
            => base.Valid(target, throwMessages) && TebrusStorm.CanExecute(parent, target.Cell);
        public override bool CanApplyOn(LocalTargetInfo target, LocalTargetInfo dest)
            => TebrusStorm.CanExecute(parent, target.Cell);
        public override void Apply(LocalTargetInfo target, LocalTargetInfo dest)
        {
            base.Apply(target, dest);
            // Activation already revalidated before the native/fallback payment.
            parent.pawn.Map.GetComponent<TebrusStormMap>().Start(parent.pawn, target.Cell);
        }
    }
    public sealed class TebrusStormCloud : IExposable
    {
        public Pawn caster; public IntVec3 center; public int elapsed, pulses;
        public void ExposeData()
        {
            Scribe_References.Look(ref caster, "caster", true);
            Scribe_Values.Look(ref center, "center"); Scribe_Values.Look(ref elapsed, "elapsed"); Scribe_Values.Look(ref pulses, "pulses");
        }
    }
    public sealed class TebrusStormMap : MapComponent
    {
        public List<TebrusStormCloud> storms = new List<TebrusStormCloud>();
        TebrusStormVisuals visuals;
        public TebrusStormMap(Map map) : base(map) { }
        public void Start(Pawn caster, IntVec3 center) => storms.Add(new TebrusStormCloud { caster = caster, center = center });
        public void Cancel(IEnumerable<Pawn> pawns)
        {
            var members = new HashSet<Pawn>(pawns);
            storms.RemoveAll(s => members.Contains(s.caster));
            visuals?.Cancel(members);
        }
        public override void ExposeData()
        {
            Scribe_Collections.Look(ref storms, "nmcTebrusStorms", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                storms = storms ?? new List<TebrusStormCloud>();
                // Legacy six-second storms keep elapsed time, without replaying old pulses.
                storms.RemoveAll(s => s == null || s.elapsed >= TebrusStorm.Duration);
            }
        }
        public override void MapRemoved()
        { storms.Clear(); visuals?.Dispose(); visuals = null; }
        public override void MapComponentTick()
        {
            visuals?.Tick();
            foreach (var cloud in storms.ToList())
            {
                if (cloud.caster == null || cloud.caster.Destroyed || cloud.elapsed >= TebrusStorm.Duration)
                { storms.Remove(cloud); continue; }
                cloud.elapsed++;
                if (cloud.elapsed % TebrusStorm.Interval == 0)
                {
                    cloud.pulses++;
                    TebrusStorm.Pulse(cloud.caster, map, cloud.center);
                }
                if (cloud.elapsed >= TebrusStorm.Duration)
                { visuals?.Finish(cloud); storms.Remove(cloud); }
            }
        }
        public override void MapComponentUpdate()
        {
            if (!TebrusStormVisuals.CanView(map) || storms.Count == 0 && visuals?.HasResidue != true)
            {
                visuals?.Dispose(); visuals = null; return;
            }
            if (visuals == null || visuals.Disposed) visuals = new TebrusStormVisuals(map);
            visuals.Draw(storms);
        }
    }
}
