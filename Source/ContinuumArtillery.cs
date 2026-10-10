using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace NivarianCommander
{
    // A separate lord keeps the gun crews at their posts when the ordinary
    // defenders transition from holding the base to assaulting the attackers.
    public sealed class LordJob_ContinuumArtillery : LordJob
    {
        IntVec3 center;
        public LordJob_ContinuumArtillery() { }
        public LordJob_ContinuumArtillery(IntVec3 center) { this.center = center; }
        public override bool CanAutoAddPawns => false;
        public override StateGraph CreateGraph()
        {
            var graph = new StateGraph();
            graph.AddToil(new LordToil_ContinuumArtillery(center));
            return graph;
        }
        public override void ExposeData()
        { base.ExposeData(); Scribe_Values.Look(ref center, "nmcArtilleryCenter"); }
    }

    public sealed class LordToil_ContinuumArtillery : LordToil
    {
        readonly IntVec3 center;
        public LordToil_ContinuumArtillery(IntVec3 center) { this.center = center; }
        public override void UpdateAllDuties()
        {
            var duty = DefDatabase<DutyDef>.GetNamed("NMC_ContinuumArtillery");
            foreach (var pawn in lord.ownedPawns)
                pawn.mindState.duty = new PawnDuty(duty, center);
        }
    }

    // Only this mod's think nodes are added to the duty. The compatibility key
    // allocator can therefore isolate their keys without shifting native trees.
    public sealed class JobGiver_ContinuumArtillery : ThinkNode_JobGiver
    {
        protected override Job TryGiveJob(Pawn pawn)
        {
            if (!pawn.Spawned || pawn.Dead || pawn.Downed || !pawn.HostileTo(Faction.OfPlayer)) return null;
            var state = pawn.Map.GetComponent<ContinuumBattlefield>();
            if (state == null || state.stage <= 0 || state.cleared) return null;

            // Crews fight an immediate breach, but do not chase across the map.
            var verb = pawn.equipment?.Primary?.GetComp<CompEquippable>()?.PrimaryVerb;
            if (verb != null && !pawn.WorkTagIsDisabled(WorkTags.Violent))
            {
                var enemy = pawn.Map.mapPawns.AllPawnsSpawned
                    .Where(p => !p.Dead && !p.Downed && p.HostileTo(pawn.Faction)
                        && p.Position.DistanceToSquared(pawn.Position) <= 144
                        && !CommanderCloak.HiddenFrom(p, pawn) && verb.CanHitTarget(p))
                    .OrderBy(p => p.Position.DistanceToSquared(pawn.Position)).ThenBy(p => p.thingIDNumber).FirstOrDefault();
                if (enemy != null)
                {
                    var combat = JobMaker.MakeJob(JobDefOf.AttackStatic, enemy);
                    combat.endIfCantShootTargetFromCurPos = true;
                    combat.expiryInterval = 120; combat.checkOverrideOnExpire = true;
                    return combat;
                }
            }

            var guns = state.defenders.OfType<Building>()
                .Where(t => t.Spawned && !t.Destroyed && t.Map == pawn.Map && t.Faction == pawn.Faction)
                .OrderBy(t => t.Position.DistanceToSquared(pawn.Position)).ThenBy(t => t.thingIDNumber).ToList();
            if (pawn.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation))
            {
                if (!pawn.WorkTagIsDisabled(WorkTags.Violent))
                {
                    // Native ManTurret fetches actual shells, consumes one on
                    // loading, and repeats the supply run after every shot. It
                    // also replaces worn mortar barrels from the finite stock.
                    foreach (var turret in guns.OfType<Building_TurretGun>())
                    {
                        if (turret.TryGetComp<CompMannable>() == null || !turret.def.hasInteractionCell
                            || !pawn.CanReserveAndReach(turret, PathEndMode.InteractionCell, Danger.Deadly)) continue;
                        var shells = turret.gun?.TryGetComp<CompChangeableProjectile>();
                        if (shells != null && !shells.Loaded && JobDriver_ManTurret.FindAmmoForTurret(pawn, turret) == null) continue;
                        var barrel = turret.TryGetComp<CompRefuelable>();
                        if (barrel != null && !barrel.HasFuel && !Find.Storyteller.difficulty.classicMortars
                            && JobDriver_ManTurret.FindFuelForTurret(pawn, turret) == null) continue;
                        var man = JobMaker.MakeJob(JobDefOf.ManTurret, turret);
                        man.expiryInterval = 2000; man.checkOverrideOnExpire = true;
                        return man;
                    }
                    foreach (var turret in guns.OfType<Building_TurretGun>())
                    {
                        var launcher = turret.TryGetComp<CompInteractableRocketswarmLauncher>();
                        if (launcher == null || !launcher.CanInteract(pawn).Accepted
                            || !pawn.CanReserveAndReach(turret, PathEndMode.Touch, Danger.Deadly)) continue;
                        var target = turret.TryFindNewTarget();
                        if (!target.IsValid) continue;
                        // OrderForceTarget opens the player's targeting UI. AI
                        // instead assigns a native target and walks over to use
                        // the same timed activation job as a player operator.
                        turret.OrderAttack(target);
                        return JobMaker.MakeJob(JobDefOf.InteractThing, turret);
                    }
                }
                foreach (var gun in guns)
                {
                    var fuel = gun.TryGetComp<CompRefuelable>();
                    if (fuel == null || fuel.IsFull || !fuel.allowAutoRefuel || !fuel.ShouldAutoRefuelNow
                        || fuel.FuelPercentOfMax > 0f && !fuel.Props.allowRefuelIfNotEmpty
                        || !pawn.CanReserveAndReach(gun, PathEndMode.Touch, Danger.Deadly)) continue;
                    var activator = gun.TryGetComp<CompInteractable>();
                    if (activator != null && activator.Props.cooldownPreventsRefuel && activator.OnCooldown) continue;
                    // Player exploration fog must not stop an enemy supplying
                    // its own base. All native fuel, reservation, capacity and
                    // cooldown rules still apply, and the native job performs
                    // the actual hauling and inventory consumption.
                    if (fuel.Props.atomicFueling && !RefuelWorkGiverUtility.CanRefuel(pawn, gun)) continue;
                    var refill = RefuelWorkGiverUtility.RefuelJob(pawn, gun);
                    if (fuel.Props.atomicFueling || refill.targetB.IsValid) return refill;
                }
            }
            var focus = pawn.mindState.duty?.focus ?? LocalTargetInfo.Invalid;
            Job idle = null;
            if (focus.IsValid && pawn.Position.DistanceToSquared(focus.Cell) > 100)
                foreach (var cell in GenRadial.RadialCellsAround(focus.Cell, 5f, true))
                    if (cell.InBounds(pawn.Map) && cell.Standable(pawn.Map) && cell.GetFirstPawn(pawn.Map) == null
                        && pawn.CanReach(cell, PathEndMode.OnCell, Danger.Deadly))
                    { idle = JobMaker.MakeJob(JobDefOf.Goto, cell); break; }
            idle = idle ?? JobMaker.MakeJob(JobDefOf.Wait_Combat);
            idle.expiryInterval = 120; idle.checkOverrideOnExpire = true;
            return idle;
        }
    }
}
