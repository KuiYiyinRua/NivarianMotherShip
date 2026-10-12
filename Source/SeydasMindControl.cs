using System;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;
namespace NivarianCommander
{
    // History lives on the converted pawn. It never holds a temporary caster
    // reference and never enters CommanderLegions' recall membership.
    public sealed class Hediff_SeydasConversion : HediffWithComps
    {
        public Faction originalFaction, convertedFaction;
        public int casterId, originMapId, localTick;
        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_References.Look(ref originalFaction, "originalFaction");
            Scribe_References.Look(ref convertedFaction, "convertedFaction");
            Scribe_Values.Look(ref casterId, "casterId"); Scribe_Values.Look(ref originMapId, "originMapId"); Scribe_Values.Look(ref localTick, "localTick");
        }
    }
    public static class SeydasMindControl
    {
        public const float Range = 20f, FocusCost = 50f;
        public const int Cooldown = 480;
        public static bool Ready(Pawn caster) => caster != null && Hediff_LegionIdentity.For(caster)?.legionAbility == 16
            && caster.Spawned && !caster.Destroyed && !caster.Dead && !caster.Downed
            && !caster.stances.stunner.Stunned && TimeStopCombat.Progress(caster);
        public static bool Legal(Pawn target, Pawn caster) => Ready(caster) && target != null && target != caster
            && target.Spawned && !target.Destroyed && !target.DeadOrDowned && target.Map == caster.Map
            && target.Position.DistanceToSquared(caster.Position) <= Range * Range
            && target.HostileTo(caster.Faction) && target.HostileTo(Faction.OfPlayer)
            && !SupportDiplomacy.Friendly(target) && !CommanderCloak.HiddenFrom(target, caster)
            && caster.Faction == LegionPurchase.NativeFaction;
        public static Pawn BestTarget(Pawn caster) => Ready(caster) ? caster.Map.mapPawns.AllPawnsSpawned
            .Where(p => Legal(p, caster)).OrderByDescending(p => p.MarketValue).ThenBy(p => p.thingIDNumber).FirstOrDefault() : null;
        public static bool CanExecute(Ability ability, LocalTargetInfo target)
        {
            var marker = Hediff_LegionIdentity.For(ability.pawn);
            return Legal(target.Pawn, ability.pawn) && marker.focusBackend != LegionFocusBackend.Unselected
                && ability.def.defName == LegionFocus.AbilityName(marker) && ability.CanCast
                && LegionFocus.Amount(ability.pawn) + .0001f >= FocusCost;
        }
        public static Job CombatJob(Pawn caster)
        {
            if (!Ready(caster)) return Wait();
            var marker = Hediff_LegionIdentity.For(caster);
            var ability = caster.abilities?.GetAbility(DefDatabase<AbilityDef>.GetNamed(LegionFocus.AbilityName(marker)));
            var target = BestTarget(caster);
            if (target != null && ability != null && CanExecute(ability, new LocalTargetInfo(target)))
                return ability.GetJob(new LocalTargetInfo(target), LocalTargetInfo.Invalid);
            var threat = caster.Map.mapPawns.AllPawnsSpawned.Where(p => Legal(p, caster)
                && p.Position.DistanceToSquared(caster.Position) <= 9f)
                .OrderBy(p => p.Position.DistanceToSquared(caster.Position)).ThenBy(p => p.thingIDNumber).FirstOrDefault();
            if (threat == null) return Wait();
            var job = JobMaker.MakeJob(JobDefOf.AttackMelee, threat); job.expiryInterval = 60; job.checkOverrideOnExpire = true; return job;
        }
        static Job Wait() { var job = JobMaker.MakeJob(JobDefOf.Wait_Combat); job.expiryInterval = 30; job.checkOverrideOnExpire = true; return job; }
        public static void Convert(Pawn caster, Pawn target)
        {
            // Activate revalidates immediately before either focus payment.
            // Single-threaded native ability effects cannot interleave here.
            if (!Legal(target, caster)) throw new InvalidOperationException("Mind-control target changed after activation validation");
            var previous = target.Faction;
            target.SetFaction(caster.Faction, caster);
            RevalidateAlliedAttacks(target, caster);
            var history = (Hediff_SeydasConversion)HediffMaker.MakeHediff(DefDatabase<HediffDef>.GetNamed("NMC_SeydasConversion"), target);
            history.originalFaction = previous; history.convertedFaction = caster.Faction;
            history.casterId = caster.thingIDNumber; history.originMapId = caster.Map.uniqueID; history.localTick = GenTicks.TicksGame;
            target.health.AddHediff(history);
            // Native SetFaction removes the old lord and clears old jobs.
            // Keep the original kind and its default NPC think tree.
            MoteMaker.ThrowText(target.DrawPos, target.Map, NmcText.Get("NMC_Text0454"));
        }
        public static int RevalidateAlliedAttacks(Pawn joined, Pawn castingPawn = null)
        {
            if (joined?.Map == null) return 0;
            int affected = 0;
            foreach (var actor in joined.Map.mapPawns.AllPawnsSpawned.OrderBy(p => p.thingIDNumber).ToArray())
            {
                // Keep deliberate player orders and mental-state hostility under native rules.
                if (actor == castingPawn || actor.IsPlayerControlled || actor.CurJob?.playerForced == true || actor.Dead || actor.Destroyed || actor != joined && actor.HostileTo(joined)) continue;
                var job = actor.CurJob;
                bool oldAttack = job != null && !job.playerForced && job.targetA.Pawn == joined
                    && (job.def == JobDefOf.AttackMelee || job.def == JobDefOf.AttackStatic
                        || job.def == JobDefOf.CastAbilityOnThing && job.ability?.def.hostile == true);
                bool oldEnemy = actor.mindState?.enemyTarget == joined;
                bool oldWarmup = actor.stances?.curStance is Stance_Warmup warm
                    && (warm.focusTarg.Pawn == joined || actor == joined && warm.focusTarg.Pawn != null && !actor.HostileTo(warm.focusTarg.Pawn))
                    && (!(warm.verb is IAbilityVerb abilityVerb) || abilityVerb.Ability?.def.hostile == true);
                if (actor != joined && !oldAttack && !oldEnemy && !oldWarmup) continue;
                var verbs = actor.VerbTracker.AllVerbs.ToList();
                if (actor.equipment != null) verbs.AddRange(actor.equipment.AllEquipmentListForReading
                    .SelectMany(t => t.GetComp<CompEquippable>()?.AllVerbs ?? Enumerable.Empty<Verb>()));
                if (actor.abilities != null) verbs.AddRange(actor.abilities.AllAbilitiesForReading
                    .Where(a => a.def.hostile).Select(a => a.verb));
                bool reset = false;
                foreach (var verb in verbs.Where(x => x != null).Distinct())
                {
                    var victim = verb.CurrentTarget.Pawn;
                    if (victim != null && !actor.HostileTo(victim) && (victim == joined || actor == joined))
                    { verb.Reset(); reset = true; }
                    else if (oldEnemy && verb.CurrentTarget.IsValid && verb.CurrentTarget.Cell == joined.Position)
                    { verb.Reset(); reset = true; }
                }
                if (oldEnemy) actor.mindState.enemyTarget = null;
                if (oldAttack) actor.jobs.EndCurrentJob(JobCondition.InterruptForced, false);
                if (oldAttack || oldWarmup || reset) actor.stances?.CancelBusyStanceSoft();
                if (oldAttack || oldEnemy || oldWarmup || reset) affected++;
            }
            return affected;
        }
    }
    public sealed class Ability_SeydasControlNative : Psycast
    {
        public Ability_SeydasControlNative(Pawn pawn) : base(pawn) { }
        public Ability_SeydasControlNative(Pawn pawn, AbilityDef def) : base(pawn, def) { }
        public override AcceptanceReport CanCast => pawn.psychicEntropy != null
            && Hediff_LegionIdentity.For(pawn)?.focusBackend == LegionFocusBackend.NativePsyfocus ? base.CanCast : false;
        public override bool Activate(LocalTargetInfo target, LocalTargetInfo dest)
            => SeydasMindControl.CanExecute(this, target) && base.Activate(target, dest);
    }
    public sealed class Ability_SeydasControlFallback : Ability
    {
        public Ability_SeydasControlFallback(Pawn pawn) : base(pawn) { }
        public Ability_SeydasControlFallback(Pawn pawn, AbilityDef def) : base(pawn, def) { }
        public override AcceptanceReport CanCast => Hediff_LegionIdentity.For(pawn)?.focusBackend == LegionFocusBackend.Fallback
            && LegionFocus.Amount(pawn) >= SeydasMindControl.FocusCost ? base.CanCast : false;
        public override bool Activate(LocalTargetInfo target, LocalTargetInfo dest)
        {
            if (!SeydasMindControl.CanExecute(this, target)) return false;
            Hediff_LegionIdentity.For(pawn).fallbackFocus -= SeydasMindControl.FocusCost;
            return base.Activate(target, dest);
        }
    }
    public sealed class CompProperties_SeydasControl : CompProperties_AbilityEffect
    {
        public CompProperties_SeydasControl()
        { compClass = typeof(CompAbilityEffect_SeydasControl); psychic = false; applicableToMechs = true; }
    }
    public sealed class CompAbilityEffect_SeydasControl : CompAbilityEffect
    {
        public override bool Valid(LocalTargetInfo target, bool throwMessages = false) => base.Valid(target, throwMessages) && SeydasMindControl.CanExecute(parent, target);
        public override bool CanApplyOn(LocalTargetInfo target, LocalTargetInfo dest) => SeydasMindControl.CanExecute(parent, target);
        public override void Apply(LocalTargetInfo target, LocalTargetInfo dest)
        { base.Apply(target, dest); SeydasMindControl.Convert(parent.pawn, target.Pawn); }
    }
}
