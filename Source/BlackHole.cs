using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Nivarian.NivarianDrones;
using Nivarian.NivarianDrones.NivarianDroneComps;
using Nivarian_Race.Code.Comps.ThingComps;
using RimWorld;
using UnityEngine;
using Verse;

namespace NivarianCommander
{
    public sealed class BlackHoleTarget : IExposable
    {
        public Thing thing;
        public float cellProgress;
        public void ExposeData()
        {
            Scribe_References.Look(ref thing, "thing");
            Scribe_Values.Look(ref cellProgress, "cellProgress");
        }
    }

    public sealed class BlackHoleVortex : IExposable
    {
        public IntVec3 center;
        public int elapsed;
        public int pulledCells;
        public List<BlackHoleTarget> targets = new List<BlackHoleTarget>();
        public BlackHoleVortex() { }
        public BlackHoleVortex(IntVec3 center) { this.center = center; }
        public void ExposeData()
        {
            Scribe_Values.Look(ref center, "center");
            Scribe_Values.Look(ref elapsed, "elapsed");
            Scribe_Values.Look(ref pulledCells, "pulledCells");
            Scribe_Collections.Look(ref targets, "targets", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit) targets = targets ?? new List<BlackHoleTarget>();
        }
    }

    // All motion and lifetime belong to this map's local time. The world cooldown
    // and single payment remain in CommanderState and NativeLedger.
    public sealed class BlackHoleMap : MapComponent
    {
        public const int Duration = 480;
        public const float Radius = 8f;
        public const float CellsPerTick = 0.2f; // 12 cells per native second.
        static readonly IntVec3[] Steps = { IntVec3.North, IntVec3.East, IntVec3.South, IntVec3.West };
        public BlackHoleVortex active;
        public int lastCompletedElapsed, lastCompletedPulledCells;
        public BlackHoleMap(Map map) : base(map) { }
        public bool Active => active != null;
        public IReadOnlyList<BlackHoleTarget> Targets => active?.targets;

        public static bool Controlled(Thing thing) => ControlledTarget(thing, out _) != null;

        // Only component references are cached. Membership, diplomacy, position
        // and line of sight remain live, including queries between local ticks.
        static BlackHoleTarget ControlledTarget(Thing thing, out BlackHoleMap comp)
        {
            comp = null;
            if (thing == null || !thing.Spawned || thing.Destroyed) return null;
            var map = thing.Map;
            if (map == null) return null;
            comp = CommanderPawnIndex.For(map).BlackHole;
            var vortex = comp?.active;
            if (vortex == null) return null;
            BlackHoleTarget record = null;
            for (int i = 0; i < vortex.targets.Count; i++)
                if (vortex.targets[i].thing == thing) { record = vortex.targets[i]; break; }
            if (record == null || SupportDiplomacy.Friendly(thing) || !thing.HostileTo(Faction.OfPlayer)
                || !thing.Position.InHorDistOf(vortex.center, Radius)
                || !GenSight.LineOfSight(thing.Position, vortex.center, map, skipFirstCell: true)) return null;
            return record;
        }

        public static bool ValidCenter(Map map, IntVec3 center)
            => map != null && Find.Maps.Contains(map) && center.InBounds(map) &&
                !center.Fogged(map) && OpenCell(map, center);

        public static bool OpenCell(Map map, IntVec3 cell)
            => cell.InBounds(map) && cell.GetTerrain(map).passability == Traversability.Standable &&
                !map.thingGrid.ThingsListAt(cell).Any(t => !(t is Pawn) && !(t is NivarianDroneBase) &&
                    (t.def.passability != Traversability.Standable || t is Building_Door door && !door.Open));

        public void Start(IntVec3 center)
        {
            Cancel();
            active = new BlackHoleVortex(center);
        }
        public void Cancel()
        {
            if (active == null) return;
            foreach (var record in active.targets.OrderBy(r => r.thing?.thingIDNumber ?? int.MaxValue).ToArray())
                Release(record, settle: true);
            lastCompletedElapsed = active.elapsed;
            lastCompletedPulledCells = active.pulledCells;
            active = null;
        }
        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Deep.Look(ref active, "nmcBlackHoleActive");
            Scribe_Values.Look(ref lastCompletedElapsed, "nmcBlackHoleCompletedElapsed");
            Scribe_Values.Look(ref lastCompletedPulledCells, "nmcBlackHoleCompletedPullCells");
            if (Scribe.mode == LoadSaveMode.PostLoadInit && active != null)
            {
                active.elapsed = Mathf.Clamp(active.elapsed, 0, Duration);
                active.targets.RemoveAll(r => r == null || r.thing == null);
            }
        }
        public override void MapComponentTick()
        {
            if (active == null) return;
            if (active.elapsed >= Duration) { Cancel(); return; }
            PullTick();
            active.elapsed++;
            if (active.elapsed >= Duration) Cancel();
        }

        static bool Enemy(Thing t) => t != null && t.Spawned && !t.Destroyed &&
            !(t is Pawn pawn && pawn.DeadOrDowned) &&
            (t is Pawn || t is NivarianDroneBase) &&
            !SupportDiplomacy.Friendly(t) && t.HostileTo(Faction.OfPlayer);

        bool Within(Thing t) => Enemy(t) && t.Map == map &&
            t.Position.InHorDistOf(active.center, Radius) &&
            GenSight.LineOfSight(t.Position, active.center, map, skipFirstCell: true) &&
            (t.Position == active.center || NextCell(t.Position, active.center, t).IsValid);

        void PullTick()
        {
            foreach (var record in active.targets.ToArray())
                if (record.thing == null || !Within(record.thing))
                { Release(record, settle: false); active.targets.Remove(record); }

            var candidates = map.mapPawns.AllPawnsSpawned.Cast<Thing>()
                .Concat(map.listerThings.AllThings.OfType<NivarianDroneBase>())
                .Where(Within).OrderBy(t => t.thingIDNumber).ToArray();
            foreach (var thing in candidates)
            {
                var record = active.targets.FirstOrDefault(r => r.thing == thing);
                if (record == null)
                {
                    record = new BlackHoleTarget { thing = thing };
                    active.targets.Add(record);
                    if (thing is NivarianDroneBase drone)
                        drone.GetComp<NivarianDroneComp_MovingBase>()?.ClearBehavior();
                }
                Stun(record);
                Pull(record);
            }
        }

        void Stun(BlackHoleTarget record)
        {
            // The source lease answers native stun queries without writing the
            // shared EMP/monolith counter. Other sources continue ticking.
            if (record.thing is Pawn pawn) pawn.pather?.StopDead();
        }
        void Pull(BlackHoleTarget record)
        {
            var thing = record.thing;
            if (!(thing is NivarianDroneBase) && thing.Position == active.center) return;
            IntVec3 next = NextCell(thing.Position, active.center, thing);
            if (!next.IsValid) return;
            if (thing is NivarianDroneBase drone)
            {
                var moving = drone.GetComp<NivarianDroneComp_MovingBase>();
                if (moving == null) return;
                Vector3 destination = next.ToVector3Shifted();
                destination.y = moving.PhysicPosition.y;
                Vector3 delta = destination - moving.PhysicPosition;
                float length = delta.magnitude;
                moving.PhysicPosition = length <= CellsPerTick ? destination :
                    moving.PhysicPosition + delta / length * CellsPerTick;
                moving.Velocity = Vector3.zero;
                IntVec3 cell = IntVec3Utility.ToIntVec3(moving.PhysicPosition);
                if (cell != thing.Position && Fits(thing, cell))
                { thing.Position = cell; active.pulledCells++; }
                return;
            }
            record.cellProgress += CellsPerTick;
            if (record.cellProgress >= 1f)
            {
                record.cellProgress -= 1f;
                thing.Position = next;
                active.pulledCells++;
            }
        }

        // A bounded, deterministic four-neighbour walk never crosses an occupied
        // wall cell or clips a wall corner. Pawn occupancy is deliberately ignored
        // during the effect, allowing units to converge on one cell.
        bool Fits(Thing thing, IntVec3 cell)
        {
            foreach (var part in GenAdj.OccupiedRect(cell, thing.Rotation, thing.def.Size).Cells)
                if (!OpenCell(map, part)) return false;
            return true;
        }
        IntVec3 NextCell(IntVec3 from, IntVec3 goal, Thing thing)
        {
            if (from == goal) return goal;
            var queue = new Queue<IntVec3>();
            var previous = new Dictionary<IntVec3, IntVec3>();
            queue.Enqueue(from); previous[from] = from;
            while (queue.Count > 0 && previous.Count <= 512)
            {
                var cell = queue.Dequeue();
                foreach (var step in Steps)
                {
                    var next = cell + step;
                    if (previous.ContainsKey(next) || !Fits(thing, next) ||
                        !next.InHorDistOf(goal, Radius * 2f)) continue;
                    previous[next] = cell;
                    if (next == goal)
                    {
                        var first = next;
                        while (previous[first] != from) first = previous[first];
                        return first;
                    }
                    queue.Enqueue(next);
                }
            }
            return IntVec3.Invalid;
        }

        void Release(BlackHoleTarget record, bool settle)
        {
            var thing = record.thing;
            if (thing == null) return;
            if (!settle || !thing.Spawned || thing.Map != map) return;
            IntVec3 destination = LegalLanding(thing);
            if (destination.IsValid && destination != thing.Position)
            {
                thing.Position = destination;
                if (thing is Pawn pawn) pawn.Notify_Teleported(endCurrentJob: false);
                if (thing is NivarianDroneBase drone)
                {
                    var moving = drone.GetComp<NivarianDroneComp_MovingBase>();
                    if (moving != null)
                    {
                        var at = destination.ToVector3Shifted(); at.y = moving.PhysicPosition.y;
                        moving.PhysicPosition = at; moving.Velocity = Vector3.zero;
                    }
                }
            }
        }
        IntVec3 LegalLanding(Thing thing)
        {
            // Reachable cells only: a radial search alone can teleport across a
            // wall when releasing a dense group. Stable BFS ignores pawn collision
            // while searching, then reserves the full final footprint.
            var queue = new Queue<IntVec3>();
            var visited = new HashSet<IntVec3>();
            var legal = new List<IntVec3>();
            queue.Enqueue(thing.Position); visited.Add(thing.Position);
            while (queue.Count > 0 && visited.Count <= 2048)
            {
                var cell = queue.Dequeue();
                if (Fits(thing, cell) && GenAdj.OccupiedRect(cell, thing.Rotation, thing.def.Size).Cells.All(c =>
                    !map.thingGrid.ThingsListAt(c).Any(t => t != thing && (t is Pawn || t is NivarianDroneBase))))
                    legal.Add(cell);
                foreach (var step in Steps)
                {
                    var next = cell + step;
                    if (!visited.Contains(next) && Fits(thing, next) && next.InHorDistOf(active.center, 16f))
                    { visited.Add(next); queue.Enqueue(next); }
                }
            }
            return legal.OrderBy(c => c.DistanceToSquared(active.center)).ThenBy(c => c.x).ThenBy(c => c.z)
                .DefaultIfEmpty(thing.Position).First();
        }
        public static Vector3 VisualPosition(Pawn pawn, Vector3 original)
        {
            var record = ControlledTarget(pawn, out var comp);
            if (record == null) return original;
            var next = comp.NextCell(pawn.Position, comp.active.center, pawn);
            if (!next.IsValid) return original;
            var position = pawn.Position.ToVector3Shifted();
            position.y = original.y;
            return position + (next - pawn.Position).ToVector3() * record.cellProgress;
        }
        public static void ClearFriendly()
        {
            if (Current.ProgramState != ProgramState.Playing) return;
            foreach (var map in Find.Maps)
            {
                var comp = map.GetComponent<BlackHoleMap>();
                if (comp.active == null) continue;
                foreach (var record in comp.active.targets.ToArray())
                    if (record.thing == null || !Enemy(record.thing))
                    { comp.Release(record, settle: true); comp.active.targets.Remove(record); }
            }
        }
        public override void MapComponentUpdate()
        {
            if (Find.CurrentMap == map) BlackHoleScreenEffect.Sync(map);
        }
    }

    public static class BlackHoleActions
    {
        public static string Unavailable(Map map)
        {
            var state = CommanderState.Current;
            var def = DefenseActions.Ability(10);
            if (state == null || map == null || !Find.Maps.Contains(map)) return NmcText.Get("NMC_Text0034");
            if (CommanderResearch.Unavailable(def) is string researchBlock) return researchBlock;
            if (state.Remaining(def) > 0) return NmcText.Get("NMC_Text0035", CommanderPanel.Period(state.Remaining(def)));
            if (NativeLedger.Balance("PowerCoin") < def.powerCost ||
                NativeLedger.Balance("VolatileReactive") < def.volatileCost)
                return NmcText.Get("NMC_Text0036", def.powerCost, def.volatileCost);
            return null;
        }
        public static void Start(Map map, IntVec3 center)
        {
            string blocked = Unavailable(map);
            if (blocked != null) { Reject(blocked); return; }
            if (!BlackHoleMap.ValidCenter(map, center)) { Reject(NmcText.Get("NMC_Text0037")); return; }
            var def = DefenseActions.Ability(10);
            if (!NativeLedger.TryPay(def.powerCost, def.volatileCost)) { Reject(NmcText.Get("NMC_Text0038")); return; }
            CommanderState.Current.cooldowns[def.defName] = def.cooldownTicks;
            map.GetComponent<BlackHoleMap>().Start(center);
        }
        public static void Cancel(Map map)
        {
            if (map != null && Find.Maps.Contains(map)) map.GetComponent<BlackHoleMap>().Cancel();
        }
        static void Reject(string reason)
        {
            if (CommanderState.Current != null) CommanderState.Current.requestsRejected++;
            Messages.Message(reason, MessageTypeDefOf.RejectInput, false);
        }
    }

    public sealed class StatPart_BlackHoleArmor : StatPart
    {
        static bool Applies(StatRequest request) => request.HasThing && BlackHoleMap.Controlled(request.Thing);
        public override void TransformValue(StatRequest req, ref float val)
        { if (Applies(req)) val = 0f; }
        public override string ExplanationPart(StatRequest req) => Applies(req) ? NmcText.Get("NMC_Text0039") : null;
    }
    public static class BlackHoleArmorStats
    {
        public static void Install()
        {
            foreach (var stat in DefDatabase<DamageArmorCategoryDef>.AllDefs
                .Select(d => d.armorRatingStat).Where(s => s != null).Distinct())
            {
                if (stat.parts == null) stat.parts = new List<StatPart>();
                if (!stat.parts.Any(p => p is StatPart_BlackHoleArmor))
                    stat.parts.Add(new StatPart_BlackHoleArmor { parentStat = stat });
            }
        }
    }
    [HarmonyPatch(typeof(ArmorUtility), "ApplyArmor")]
    public static class BlackHoleApparelArmor
    {
        static void Prefix(Pawn pawn, ref float armorRating)
        { if (BlackHoleMap.Controlled(pawn)) armorRating = 0f; }
    }
    [HarmonyPatch(typeof(Pawn), "get_DrawPos")]
    public static class BlackHolePawnDraw
    { static void Postfix(Pawn __instance, ref Vector3 __result)
        => __result = BlackHoleMap.VisualPosition(__instance, __result); }
    [HarmonyPatch(typeof(StunHandler), "get_Stunned")]
    public static class BlackHoleNativeStun
    { static void Postfix(StunHandler __instance, ref bool __result)
        { if (!__result && BlackHoleMap.Controlled(__instance.parent) && NativeMonolithStun.Primary(__instance.parent) == __instance) __result = true; } }
    [HarmonyPatch(typeof(StunHandler), "get_StunTicksLeft")]
    public static class BlackHoleNativeStunDuration
    { static void Postfix(StunHandler __instance, ref int __result)
        { if (__result < 2 && BlackHoleMap.Controlled(__instance.parent) && NativeMonolithStun.Primary(__instance.parent) == __instance) __result = 2; } }
    [HarmonyPatch]
    public static class BlackHoleFactionChange
    {
        static IEnumerable<System.Reflection.MethodBase> TargetMethods()
        {
            yield return AccessTools.DeclaredMethod(typeof(Thing), nameof(Thing.SetFaction),
                new[] { typeof(Faction), typeof(Pawn) });
            yield return AccessTools.DeclaredMethod(typeof(Pawn), nameof(Pawn.SetFaction),
                new[] { typeof(Faction), typeof(Pawn) });
        }
        static void Postfix() => BlackHoleMap.ClearFriendly();
    }
    [HarmonyPatch(typeof(Faction), nameof(Faction.Notify_RelationKindChanged))]
    public static class BlackHoleDiplomacyChange
    { static void Postfix() => BlackHoleMap.ClearFriendly(); }
    [HarmonyPatch]
    public static class BlackHoleDroneMovement
    {
        static IEnumerable<System.Reflection.MethodBase> TargetMethods()
            => typeof(NivarianDroneComp_MovingBase).Assembly.GetTypes()
                .Where(t => typeof(NivarianDroneComp_MovingBase).IsAssignableFrom(t))
                .Select(t => AccessTools.DeclaredMethod(t, nameof(ThingComp.CompTick), Type.EmptyTypes))
                .Where(m => m != null);
        static bool Prefix(ThingComp __instance) => !BlackHoleMap.Controlled(__instance.parent);
    }
    [HarmonyPatch(typeof(CompAttachTurret), nameof(CompAttachTurret.CompTick))]
    public static class BlackHoleDroneTurret
    { static bool Prefix(CompAttachTurret __instance) => !BlackHoleMap.Controlled(__instance.parent); }
    [HarmonyPatch(typeof(NivarianDroneBase), nameof(NivarianDroneBase.GoHome))]
    public static class BlackHoleDroneHome
    { static bool Prefix(NivarianDroneBase __instance) => !BlackHoleMap.Controlled(__instance); }
    [HarmonyPatch]
    public static class BlackHoleVerb
    {
        static IEnumerable<System.Reflection.MethodBase> TargetMethods()
            => AccessTools.GetDeclaredMethods(typeof(Verb)).Where(m => m.Name == nameof(Verb.TryStartCastOn));
        static bool Prefix(Verb __instance, ref bool __result)
        { if (!BlackHoleMap.Controlled(__instance.Caster)) return true; __result = false; return false; }
    }
}
