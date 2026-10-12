using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace NivarianCommander
{
    public sealed class LordJob_LegionAssault : LordJob
    {
        public override bool CanAutoAddPawns => false;
        public override StateGraph CreateGraph()
        { var graph = new StateGraph(); graph.AddToil(new LordToil_LegionAssault()); return graph; }
    }
    public sealed class LordToil_LegionAssault : LordToil
    {
        public override void UpdateAllDuties()
        { foreach (var pawn in lord.ownedPawns) pawn.mindState.duty = new PawnDuty(DefDatabase<DutyDef>.GetNamed("NMC_LegionAssault"), pawn.Position); }
    }
    public sealed class JobGiver_LegionAssault : ThinkNode_JobGiver
    {
        protected override Job TryGiveJob(Pawn pawn)
        {
            var marker = Hediff_LegionIdentity.For(pawn);
            if (marker == null || pawn.Dead || pawn.Downed || !pawn.Spawned) return null;
            if (marker.legionAbility == 16) return SeydasMindControl.CombatJob(pawn);
            if (marker.hero && TebrusStorm.CastJob(pawn) is Job storm) return storm;
            var target = pawn.Map.listerThings.AllThings.Where(t => t != pawn && t.Spawned && !t.Destroyed
                && (t is Pawn || t is Building || marker.hero && t is Nivarian.NivarianDrones.NivarianDroneBase)
                && (!(t is Pawn enemy) || !enemy.DeadOrDowned) && t.HostileTo(pawn.Faction) && t.HostileTo(Faction.OfPlayer)
                && !SupportDiplomacy.Friendly(t) && !CommanderCloak.HiddenFrom(t, pawn)
                && pawn.CanReach(t, PathEndMode.Touch, Danger.Deadly))
                .OrderBy(t => t.Position.DistanceToSquared(pawn.Position)).ThenBy(t => t.thingIDNumber).FirstOrDefault();
            Job job;
            if (target == null) job = JobMaker.MakeJob(JobDefOf.Wait_Combat);
            else if (!marker.hero) job = JobMaker.MakeJob(JobDefOf.AttackMelee, target);
            else
            {
                var verb = pawn.equipment.Primary?.GetComp<CompEquippable>()?.PrimaryVerb;
                if (verb == null) return null;
                if (verb.CanHitTarget(target))
                { job = JobMaker.MakeJob(JobDefOf.AttackStatic, target); job.endIfCantShootTargetFromCurPos = true; }
                else if (CastPositionFinder.TryFindCastPosition(new CastPositionRequest {
                    caster = pawn, target = target, verb = verb, maxRangeFromTarget = verb.verbProps.range,
                    wantCoverFromTarget = true }, out IntVec3 position))
                    job = JobMaker.MakeJob(JobDefOf.Goto, position);
                else return null;
            }
            job.expiryInterval = 60; job.checkOverrideOnExpire = true; return job;
        }
    }
}
