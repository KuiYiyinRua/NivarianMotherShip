using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Nivarian.NivarianDrones;
using Nivarian.NivarianDrones.NivarianDroneComps;
using Nivarian.NivarianDrones.NivarianDroneTargetValidator;
using Nivarian_Race.Code.Comps.ThingComps;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace NivarianCommander
{
    public static class FleetWarpState
    {
        static readonly FieldInfo Task = AccessTools.Field(typeof(NivarianDroneComp_Warp), "_currentTask");
        static readonly FieldInfo Queue = AccessTools.Field(typeof(NivarianDroneComp_Warp), "_taskQueue");
        public static void Validate()
        {
            if (Task == null || Queue == null || !typeof(ICollection).IsAssignableFrom(Queue.FieldType))
                throw new MissingFieldException("[NMC] Fleet requires the verified native warp task and queue fields");
        }
        public static bool Pending(NivarianDroneComp_Warp warp)
            => Task.GetValue(warp) != null || ((ICollection)Queue.GetValue(warp)).Count != 0;
    }
    [HarmonyPatch(typeof(NivarianDroneComp_Warp), nameof(NivarianDroneComp_Warp.PostExposeData))]
    public static class FleetWarpQueuePersistence
    {
        sealed class LoadedQueue { public List<WarpTask> tasks; }
        static readonly ConditionalWeakTable<NivarianDroneComp_Warp, LoadedQueue> Loaded
            = new ConditionalWeakTable<NivarianDroneComp_Warp, LoadedQueue>();
        // The native local List is recreated as null on every Scribe phase.
        // Current tasks survive; not-yet-dequeued tasks were lost on reload.
        // Keep that same native list through loading phases for our ships only.
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            int count = 0;
            foreach (var instruction in instructions)
            {
                if (instruction.opcode == OpCodes.Call && instruction.operand is MethodInfo method
                    && method.DeclaringType == typeof(Scribe_Collections) && method.Name == "Look"
                    && method.IsGenericMethod && method.GetGenericArguments().SequenceEqual(new[] { typeof(WarpTask) })
                    && method.GetParameters().Length == 4
                    && method.GetParameters()[0].ParameterType == typeof(List<WarpTask>).MakeByRefType())
                {
                    var parent = new CodeInstruction(OpCodes.Ldarg_0);
                    parent.labels.AddRange(instruction.labels); instruction.labels.Clear();
                    parent.blocks.AddRange(instruction.blocks); instruction.blocks.Clear();
                    yield return parent;
                    instruction.operand = AccessTools.Method(typeof(FleetWarpQueuePersistence), nameof(Look));
                    count++;
                }
                yield return instruction;
            }
            if (count != 1) throw new InvalidOperationException("[NMC] Expected one native WarpTask list Scribe call; found " + count);
        }
        static void Look(ref List<WarpTask> tasks, string label, LookMode mode, object[] args, NivarianDroneComp_Warp warp)
        {
            if (!(warp.parent is FleetShip))
            { Scribe_Collections.Look(ref tasks, label, mode, args); return; }
            if (Scribe.mode == LoadSaveMode.ResolvingCrossRefs || Scribe.mode == LoadSaveMode.PostLoadInit)
                if (Loaded.TryGetValue(warp, out var stored)) tasks = stored.tasks;
            Scribe_Collections.Look(ref tasks, label, mode, args);
            if (Scribe.mode == LoadSaveMode.LoadingVars) Loaded.GetValue(warp, _ => new LoadedQueue()).tasks = tasks;
            if (Scribe.mode == LoadSaveMode.PostLoadInit) Loaded.Remove(warp);
        }
    }
    public sealed class FleetShip : NivarianFixedWing_Drone, IAttackTarget
    {
        protected override void DrawAt(Vector3 drawLoc, bool flip = false)
        { if (!FieldWarpVisuals.DrawShip(this, drawLoc)) base.DrawAt(drawLoc, flip); }
        public int batchId, slot;
        public bool entryQueued, arrived, retiring;
        public Thing Thing => this;
        public LocalTargetInfo TargetCurrentlyAimingAt => GetAllCompAttachTurrets().FirstOrDefault()?.CurTarget ?? LocalTargetInfo.Invalid;
        public float TargetPriorityFactor => 1f;
        public bool ThreatDisabled(IAttackTargetSearcher searcher)
            => Destroyed || !Spawned || !arrived || retiring || searcher?.CurrentEffectiveVerb?.IsMeleeAttack == true;
        public override IEnumerable<Gizmo> GetGizmos()
        {
            if (FieldRecallActions.ResearchFinished && ShipFieldRecallActions.Eligible(this))
                yield return ShipFieldRecallActions.Gizmo(this);
        }
        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);
            foreach (var gun in GetAllCompAttachTurrets())
            { gun.ManualTarget = true; gun.ManualFire = !arrived; gun.EnableGizmo = false; }
        }
        protected override void Tick()
        {
            base.Tick();
            if (Destroyed || !Spawned || !entryQueued || arrived) return;
            var warp = GetComp<NivarianDroneComp_Warp>();
            if (warp == null || FleetWarpState.Pending(warp)) return;
            arrived = true;
            foreach (var gun in GetAllCompAttachTurrets()) gun.ManualFire = false;
            CommanderFleets.Current.Arrival(this);
        }
        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref batchId, "fleetBatch"); Scribe_Values.Look(ref slot, "fleetSlot");
            Scribe_Values.Look(ref entryQueued, "fleetEntryQueued"); Scribe_Values.Look(ref arrived, "fleetArrived");
        }
        public override string GetInspectString()
        {
            var b = CommanderFleets.Current.Get(batchId);
            return NmcText.Get("NMC_Text0263", (b?.abilityNumber == 15 ? NmcText.Get("NMC_Text0258") : NmcText.Get("NMC_Text0259")), HitPoints, MaxHitPoints, (b == null ? NmcText.Get("NMC_Text0260")
                : b.Started ? NmcText.Get("NMC_Text0094", CommanderPanel.Period(b.remaining))
                : NmcText.Get("NMC_Text0262", b.ArrivedCount, b.expectedMembers, (b.LostBeforeArrivalCount > 0 ? NmcText.Get("NMC_Text0261", b.LostBeforeArrivalCount) : ""))));
        }
        public override void Notify_KilledLeavingsLeft(List<Thing> leavings)
        {
            base.Notify_KilledLeavingsLeft(leavings);
            var b = CommanderFleets.Current.Get(batchId);
            foreach (var thing in leavings) CommanderFleets.Current.Track(b, thing);
        }
        public override void Notify_MyMapRemoved()
        {
            // Native map deinit discards ships without calling Destroy. Their
            // deep-saved virtual weapons have no native ThingOwner to discard.
            ReclaimPrivateGuns(GetAllCompAttachTurrets().OfType<FleetGun>().ToList());
            base.Notify_MyMapRemoved();
        }
        static void ReclaimPrivateGuns(IEnumerable<FleetGun> guns)
        {
            foreach (var gun in guns)
            {
                gun.ManualFire = true;
                var weapon = gun.WeaponEq?.parent;
                if (weapon == null || weapon.Destroyed || weapon.Spawned || weapon.ParentHolder != null
                    || weapon.def != gun.Props.turretDef) continue;
                foreach (var verb in gun.WeaponEq.AllVerbs) verb.Reset();
                weapon.Destroy(DestroyMode.Vanish);
            }
        }
        public override void Destroy(DestroyMode mode = DestroyMode.Vanish)
        {
            if (Destroyed) return;
            var map = Map; var cell = Position; var owner = CommanderFleets.Current;
            var b = owner.Get(batchId);
            // Native attach turrets deep-save their private virtual weapons only
            // through the owning ship. Retire those weapons with the ship so
            // living projectiles save a native destroyed reference as null.
            var guns = GetAllCompAttachTurrets().OfType<FleetGun>().ToList();
            base.Destroy(mode);
            ReclaimPrivateGuns(guns);
            if (!retiring && b != null && !b.retiring && mode == DestroyMode.KillFinalize && map != null && cell.InBounds(map))
            {
                var wreck = ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("NMC_FleetWreck"));
                // Never explode or create crash damage; only a zero-value wreck.
                owner.Track(b, wreck);
                GenSpawn.Spawn(wreck, cell, map, WipeMode.Vanish);
            }
            owner.Lost(this);
        }
    }
    public sealed class FleetSearchWorker : NivarianCommonEnemySearchWorker
    {
        public override bool UsesMapCache => false;
        public override bool Validator(LocalTargetInfo target, Thing source, float radius)
            => source is FleetShip ship && ship.arrived && !ship.retiring && target.HasThing
                && !(target.Thing is Pawn pawn && pawn.DeadOrDowned)
                && target.Thing.HostileTo(source) && base.Validator(target, source, radius);
        public override LocalTargetInfo Search(Map map, Thing source, float radius)
        {
            if (map == null || !(source is FleetShip ship) || !ship.arrived) return LocalTargetInfo.Invalid;
            var target = map.attackTargetsCache.TargetsHostileToFaction(source.Faction)
                .Select(t => t.Thing).Where(t => t != null && Validator(t, source, radius))
                .OrderBy(t => (t.Position - source.Position).LengthHorizontalSquared).ThenBy(t => t.thingIDNumber).FirstOrDefault();
            return target == null ? LocalTargetInfo.Invalid : new LocalTargetInfo(target);
        }
    }
    public sealed class FleetGun : CompAttachTurret
    {
        public override IEnumerable<Gizmo> CompGetGizmosExtra() { yield break; }
        public override void CompTick()
        {
            if (!(parent is FleetShip ship)) return;
            if (CurTarget.HasThing && (!CurTarget.Thing.Spawned || CurTarget.Thing.Map != ship.Map
                || CurTarget.Thing.Destroyed || CurTarget.Thing is Pawn pawn && pawn.DeadOrDowned
                || !CurTarget.Thing.HostileTo(ship)))
            {
                CurTarget = LocalTargetInfo.Invalid;
                if (AttackVerb != null && AttackVerb.state == VerbState.Bursting) AttackVerb.Reset();
            }
            var ray = AttackVerb as Verb_FleetRay;
            // Advance before native acquisition: a new shot keeps its full
            // 30/60/120-tick interval rather than losing a tick immediately.
            ray?.CombatTick(ship, CurTarget);
            ManualFire = !ship.arrived || ship.retiring || ray?.Channeling == true;
            base.CompTick();
        }
        public override void PostDraw()
        {
            base.PostDraw();
            if (AttackVerb is Verb_FleetRay ray) ray.DrawBeam(parent);
        }
    }
    public sealed class FleetMoving : NivarianDroneComp_MovingFixedWing
    {
        public override void CompTick()
        {
            // Delayed native warp reports IsWarping=false. Keep its queued
            // entry authoritative and initialize idle orbit only at arrival.
            if (parent is FleetShip ship && !ship.arrived) return;
            base.CompTick();
        }
    }
    [HarmonyPatch(typeof(Verb), nameof(Verb.TryStartCastOn), new[] { typeof(LocalTargetInfo), typeof(LocalTargetInfo), typeof(bool), typeof(bool), typeof(bool), typeof(bool) })]
    public static class FleetGroundMeleeStart
    {
        static bool Prefix(Verb __instance, LocalTargetInfo castTarg, ref bool __result)
        {
            if (!__instance.IsMeleeAttack || !(castTarg.Thing is FleetShip)) return true;
            __result = false; return false;
        }
    }
    [HarmonyPatch(typeof(Verb_MeleeAttack), "TryCastShot")]
    public static class FleetGroundMeleeShot
    {
        static bool Prefix(Verb_MeleeAttack __instance, ref bool __result)
        {
            if (!(__instance.CurrentTarget.Thing is FleetShip)) return true;
            __result = false; return false;
        }
    }
    // Temporary loot cannot merge into permanent stacks and lose its identity.
    [HarmonyPatch(typeof(Thing), nameof(Thing.CanStackWith))]
    public static class FleetMaterialStack
    {
        static void Postfix(Thing __instance, Thing other, ref bool __result)
        {
            if (__result && Current.ProgramState == ProgramState.Playing &&
                (CommanderFleets.Current.Owner(__instance) != null || CommanderFleets.Current.Owner(other) != null)) __result = false;
        }
    }
    [HarmonyPatch(typeof(Thing), nameof(Thing.SplitOff))]
    public static class FleetMaterialSplit
    {
        static void Prefix(Thing __instance, out FleetBatch __state)
            => __state = Current.ProgramState == ProgramState.Playing ? CommanderFleets.Current.Owner(__instance) : null;
        static void Postfix(FleetBatch __state, Thing __result)
        { if (__state != null) CommanderFleets.Current.Track(__state, __result); }
    }
}
