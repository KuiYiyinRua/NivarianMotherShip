using System;
using System.Collections.Generic;
using System.Linq;
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
            if (remaining > 0) remaining--;
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
            }
        }
        public static void Cancel()
        {
            foreach (var map in Find.Maps.OrderBy(m => m.uniqueID))
                map.GetComponent<SpacetimeWaveMap>().remaining = 0;
        }
        static void Reject(string reason)
        {
            if (CommanderState.Current != null) CommanderState.Current.requestsRejected++;
            Messages.Message(reason, MessageTypeDefOf.RejectInput, false);
        }
    }

    // Attached only to final pawn work-speed stats, never to worktable speed factors.
    // A recipe's arbitrary workSpeedStat is included, but its global speed stat is
    // boosted only while that recipe is actually being worked, avoiding nested 25x.
    public sealed class StatPart_SpacetimeWaveWork : StatPart
    {
        public bool recipeOnly;
        bool Applies(StatRequest req)
        {
            if (SpacetimeWaveStats.SuppressDepth > 0) return false;
            if (!req.HasThing || !(req.Thing is Pawn pawn) || !pawn.Spawned || !SupportDiplomacy.Friendly(pawn)
                || !SpacetimeWaveMap.ActiveFor(pawn.Map)) return false;
            return !recipeOnly || pawn.CurJob?.RecipeDef?.workSpeedStat == parentStat;
        }
        public override void TransformValue(StatRequest req, ref float val)
        {
            if (Applies(req)) val *= 5f;
        }
        public override string ExplanationPart(StatRequest req) => Applies(req) ? NmcText.Get("NMC_Text0462") : null;
    }
    public static class SpacetimeWaveStats
    {
        [ThreadStatic] public static int SuppressDepth;
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
            var recipe = new HashSet<StatDef>(DefDatabase<RecipeDef>.AllDefsListForReading
                .Select(r => r.workSpeedStat).Where(s => s != null));
            foreach (var stat in work.Concat(recipe).Where(s => s != null).Distinct())
            {
                if (stat.parts == null) stat.parts = new List<StatPart>();
                if (!stat.parts.Any(p => p is StatPart_SpacetimeWaveWork))
                    stat.parts.Add(new StatPart_SpacetimeWaveWork { parentStat = stat, recipeOnly = !work.Contains(stat) });
            }
        }
    }
}
