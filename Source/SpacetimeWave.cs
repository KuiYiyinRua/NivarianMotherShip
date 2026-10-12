using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace NivarianCommander
{
    // Each loaded map owns its own local clock. The shared cooldown stays in CommanderState.
    public sealed class SpacetimeWaveMap : MapComponent
    {
        public const int Duration = 3600;
        public int remaining;
        public int castsReceived;
        public SpacetimeWaveMap(Map map) : base(map) { }
        public bool Active => remaining > 0;
        public override void MapComponentTick()
        {
            if (remaining > 0 && --remaining == 0) SpacetimeWaveStats.ClearCachedValues(map);
        }
        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref remaining, "nmcWaveRemainingLocalTicks");
            Scribe_Values.Look(ref castsReceived, "nmcWaveCastsReceived");
            if (Scribe.mode == LoadSaveMode.PostLoadInit) remaining = Mathf.Clamp(remaining, 0, Duration);
        }
        public static bool ActiveFor(Map map) => map != null && map.IsPlayerHome &&
            map.GetComponent<SpacetimeWaveMap>()?.Active == true;
    }

    public static class SpacetimeWaveActions
    {
        public static string Unavailable()
        {
            var state = CommanderState.Current;
            var def = DefenseActions.Ability(6);
            if (state == null || !Find.Maps.Any(m => m.IsPlayerHome)) return NmcText.Get("NMC_Text0461");
            if (CommanderResearch.Unavailable(def) is string researchBlock) return researchBlock;
            if (state.Remaining(def) > 0) return NmcText.Get("NMC_Text0035", CommanderPanel.Period(state.Remaining(def)));
            if (NativeLedger.Balance("PowerCoin") < def.powerCost || NativeLedger.Balance("VolatileReactive") < def.volatileCost)
                return NmcText.Get("NMC_Text0036", def.powerCost, def.volatileCost);
            return null;
        }
        // Registered as one world-context MP command. Never invoke from a per-peer local tick.
        public static void Start()
        {
            string blocked = Unavailable();
            if (blocked != null) { Reject(blocked); return; }
            var def = DefenseActions.Ability(6);
            if (!NativeLedger.TryPay(def.powerCost, def.volatileCost)) { Reject(NmcText.Get("NMC_Text0038")); return; }
            CommanderState.Current.cooldowns[def.defName] = def.cooldownTicks;
            foreach (var map in Find.Maps.Where(m => m.IsPlayerHome).OrderBy(m => m.uniqueID))
            {
                var wave = map.GetComponent<SpacetimeWaveMap>();
                wave.remaining = SpacetimeWaveMap.Duration;
                wave.castsReceived++;
                SpacetimeWaveStats.ClearCachedValues(map);
            }
        }
        public static void Cancel()
        {
            foreach (var map in Find.Maps.OrderBy(m => m.uniqueID))
            {
                map.GetComponent<SpacetimeWaveMap>().remaining = 0;
                SpacetimeWaveStats.ClearCachedValues(map);
            }
        }
        static void Reject(string reason)
        {
            if (CommanderState.Current != null) CommanderState.Current.requestsRejected++;
            Messages.Message(reason, MessageTypeDefOf.RejectInput, false);
        }
    }

    // Attached only to final pawn work-speed stats, never to worktable speed factors.
    // A recipe's arbitrary workSpeedStat is included, but its global speed stat is
    // boosted only while that recipe is actually being worked, avoiding nested 100x.
    public sealed class StatPart_SpacetimeWaveWork : StatPart
    {
        public bool recipeOnly;
        internal bool Applies(StatRequest req)
        {
            if (SpacetimeWaveStats.SuppressDepth > 0) return false;
            if (!req.HasThing || !(req.Thing is Pawn pawn) || !SpacetimeWaveStats.ActiveFor(pawn)) return false;
            return !recipeOnly || pawn.CurJob?.RecipeDef?.workSpeedStat == parentStat;
        }
        // The multiplier is applied after native curves, rounding and min/max
        // limits by SpacetimeFinalPawnSpeed. This part supplies the explanation.
        public override void TransformValue(StatRequest req, ref float val) { }
        public override string ExplanationPart(StatRequest req) => Applies(req) ? NmcText.Get("NMC_Text0462") : null;
    }
    public sealed class StatPart_SpacetimeWaveMove : StatPart
    {
        static bool Applies(StatRequest req) => req.HasThing && req.Thing is Pawn pawn && SpacetimeWaveStats.ActiveFor(pawn);
        public override void TransformValue(StatRequest req, ref float val) { }
        public override string ExplanationPart(StatRequest req) => Applies(req) ? NmcText.Get("NMC_Text0748") : null;
    }
    public static class SpacetimeWaveStats
    {
        public const int WorkMultiplier = 10, MoveMultiplier = 2, TurretMultiplier = 3;
        [ThreadStatic] public static int SuppressDepth;
        static readonly List<StatDef> affected = new List<StatDef>();
        static readonly Dictionary<StatDef, StatPart_SpacetimeWaveWork> workParts = new Dictionary<StatDef, StatPart_SpacetimeWaveWork>();
        public static bool ActiveFor(Pawn pawn) => pawn != null && pawn.Spawned && SupportDiplomacy.Friendly(pawn)
            && SpacetimeWaveMap.ActiveFor(pawn.Map);
        public static void ClearCachedValues(Map map)
        {
            if (map == null) return;
            foreach (var pawn in map.mapPawns.AllPawnsSpawned)
                foreach (var stat in affected) stat.Worker.ClearCacheForThing(pawn);
        }
        internal static float Factor(StatDef stat, StatRequest req)
        {
            if (stat == StatDefOf.MoveSpeed)
                return req.HasThing && req.Thing is Pawn pawn && ActiveFor(pawn) ? MoveMultiplier : 1f;
            return workParts.TryGetValue(stat, out var part) && part.Applies(req) ? WorkMultiplier : 1f;
        }
        public static void Install()
        {
            var work = new HashSet<StatDef>
            {
                StatDefOf.DeepDrillingSpeed, StatDefOf.ResearchSpeed,
                StatDefOf.ConstructionSpeed, StatDefOf.PlantWorkSpeed, StatDefOf.SmoothingSpeed,
                StatDefOf.GeneralLaborSpeed, StatDefOf.CleaningSpeed, StatDefOf.PruningSpeed,
                StatDefOf.MechRepairSpeed, StatDefOf.AnimalGatherSpeed,
                StatDefOf.FishingSpeed
            };
            // Include modded final work-speed stats. Mining and tending use the
            // live countdown patches; the global factor must not multiply twice.
            foreach (var stat in DefDatabase<StatDef>.AllDefsListForReading)
                if (stat.category?.defName == "PawnWork" && stat.defName.EndsWith("Speed", StringComparison.Ordinal)
                    && stat != StatDefOf.MiningSpeed && stat != StatDefOf.MedicalTendSpeed
                    && stat.defName != "GlobalWorkSpeed") work.Add(stat);
            var recipe = new HashSet<StatDef>(DefDatabase<RecipeDef>.AllDefsListForReading
                .Select(r => r.workSpeedStat).Where(s => s != null));
            foreach (var stat in work.Concat(recipe).Where(s => s != null).Distinct())
            {
                if (stat.parts == null) stat.parts = new List<StatPart>();
                if (!stat.parts.Any(p => p is StatPart_SpacetimeWaveWork))
                    stat.parts.Add(new StatPart_SpacetimeWaveWork { parentStat = stat, recipeOnly = !work.Contains(stat) });
                workParts[stat] = stat.parts.OfType<StatPart_SpacetimeWaveWork>().First();
                if (!affected.Contains(stat)) affected.Add(stat);
            }
            if (StatDefOf.MoveSpeed.parts == null) StatDefOf.MoveSpeed.parts = new List<StatPart>();
            if (!StatDefOf.MoveSpeed.parts.Any(p => p is StatPart_SpacetimeWaveMove))
                StatDefOf.MoveSpeed.parts.Add(new StatPart_SpacetimeWaveMove { parentStat = StatDefOf.MoveSpeed });
            if (!affected.Contains(StatDefOf.MoveSpeed)) affected.Add(StatDefOf.MoveSpeed);
            SpacetimeTurretTimers.Install();
        }
    }
    [HarmonyPatch(typeof(StatWorker), nameof(StatWorker.GetValue), new[] { typeof(StatRequest), typeof(bool) })]
    public static class SpacetimeFinalPawnSpeed
    {
        public struct State { public int suppression; public float factor; }
        static void Prefix(StatDef ___stat, StatRequest req, out State __state)
        {
            __state = new State { suppression = SpacetimeWaveStats.SuppressDepth, factor = SpacetimeWaveStats.Factor(___stat, req) };
            // A modded final speed may read another work speed. Only its outer
            // value is boosted, preventing two work factors composing into 100x.
            if (__state.factor == SpacetimeWaveStats.WorkMultiplier) SpacetimeWaveStats.SuppressDepth++;
        }
        static void Postfix(ref float __result, State __state)
        {
            SpacetimeWaveStats.SuppressDepth = __state.suppression;
            __result *= __state.factor;
        }
        static Exception Finalizer(Exception __exception, State __state)
        {
            SpacetimeWaveStats.SuppressDepth = __state.suppression;
            return __exception;
        }
    }
}
