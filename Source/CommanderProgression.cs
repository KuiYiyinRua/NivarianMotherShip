using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Nivarian_Race.Code.Comps.BuildingComps;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace NivarianCommander
{
    public sealed class CommanderResearchExtension : DefModExtension
    {
        public int requiredStage, mothershipLevel;
    }

    public static class CommanderProgression
    {
        public const string MilitaryPackage = "keeptpa.NivarianDraconicMilitary";
        public static bool CampaignEnabled => ModsConfig.IsActive(MilitaryPackage);
        public static readonly string[] Legacy = { "TacticalProtocol", "LightWeapons", "ShadowWarp", "Spacetime", "LegionContact", "HigherDimensions", "FleetBeacon" };
        public static string LegacyFor(int n) => "NMC_" + (n == 1 || n == 2 || n == 3 || n == 12 ? Legacy[0]
            : n == 4 || n == 5 || n == 7 ? Legacy[1] : n == 8 || n == 9 || n == 10 ? Legacy[2]
            : n == 6 || n == 11 || n == 18 ? Legacy[3] : n >= 14 && n <= 16 ? Legacy[4] : n == 13 ? Legacy[6] : Legacy[5]);
        public static bool IsLegacy(ResearchProjectDef def) => def != null && Legacy.Any(n => def.defName == "NMC_" + n);
        public static string ResearchBlocked(ResearchProjectDef def)
        {
            int stage = def?.GetModExtension<CommanderResearchExtension>()?.requiredStage ?? 0;
            return CampaignEnabled && stage > (ContinuumCampaign.Current?.completedStage ?? 0)
                ? NmcText.Get("NMC_Text0198", stage, ContinuumCampaign.StageNames[stage - 1]) : null;
        }
        public static string RequirementText(CommanderAbilityDef def)
        {
            var project = CommanderResearch.For(def);
            int level = project?.GetModExtension<CommanderResearchExtension>()?.mothershipLevel ?? 0;
            return NmcText.Get("NMC_Text0201", def.researchLabel, (project?.IsFinished == true ? NmcText.Get("NMC_Text0199") : NmcText.Get("NMC_Text0200")), level, (NativeLedger.Component?.GetLevel ?? 0), (ResearchBlocked(project) is string block && project?.IsFinished != true ? "\n" + block : ""));
        }
    }

    // Runs only in deterministic world ticks, never while a client draws the research UI.
    public sealed class CommanderResearchMigration : WorldComponent
    {
        bool initialized;
        List<string> legacyInProgress = new List<string>();
        static readonly System.Reflection.FieldInfo Progress = AccessTools.Field(typeof(ResearchManager), "progress");
        public CommanderResearchMigration(World world) : base(world) { }
        public override void WorldComponentTick()
        {
            // Preserve the old purchased unlock in the deterministic world context.
            var identification = DefDatabase<ResearchProjectDef>.GetNamedSilentFail(CommanderResearch.FriendlyIdentification);
            if (Find.ResearchManager != null && CommanderState.Current?.friendlyFireProtection == true
                && identification != null && !identification.IsFinished)
                ((Dictionary<ResearchProjectDef, float>)Progress.GetValue(Find.ResearchManager))[identification] = identification.baseCost;
            if (Find.ResearchManager == null || (initialized && legacyInProgress.Count == 0)) return;
            if (!initialized)
            {
                foreach (var name in CommanderProgression.Legacy.Select(n => "NMC_" + n))
                {
                    var old = DefDatabase<ResearchProjectDef>.GetNamedSilentFail(name);
                    // A saved current project can still have zero progress. Watching the
                    // seven global entries covers it without reading asynchronous maps.
                    if (old != null) legacyInProgress.Add(name);
                }
                initialized = true;
            }
            else if (Find.TickManager.TicksGame % 60 != 0) return;
            foreach (var name in legacyInProgress.ToArray())
            {
                if (!CommanderResearch.Finished(name)) continue;
                var progress = (Dictionary<ResearchProjectDef, float>)Progress.GetValue(Find.ResearchManager);
                foreach (var ability in DefDatabase<CommanderAbilityDef>.AllDefsListForReading)
                    if (CommanderProgression.LegacyFor(ability.number) == name && CommanderResearch.For(ability) is ResearchProjectDef research)
                        progress[research] = research.baseCost; // Preserve unlocks, without awarding duplicate native research XP.
                legacyInProgress.Remove(name);
            }
        }
        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref initialized, "nmcIndividualResearchMigrated");
            Scribe_Collections.Look(ref legacyInProgress, "nmcLegacyResearchInProgress", LookMode.Value);
            if (Scribe.mode == LoadSaveMode.PostLoadInit) legacyInProgress = legacyInProgress ?? new List<string>();
        }
    }
    [HarmonyPatch(typeof(ResearchProjectDef), "get_IsHidden")]
    internal static class CommanderLegacyResearchHidden
    {
        static void Postfix(ResearchProjectDef __instance, ref bool __result)
        { if (CommanderProgression.IsLegacy(__instance)) __result = true; }
    }
    [HarmonyPatch(typeof(Nivarian_Race.Code.UI.Window_UplinkResearch), nameof(Nivarian_Race.Code.UI.Window_UplinkResearch.IsNivarianTech))]
    internal static class CommanderLegacyUplinkHidden
    {
        static void Postfix(ResearchProjectDef proj, ref bool __result)
        { if (CommanderProgression.IsLegacy(proj)) __result = false; }
    }
    [HarmonyPatch(typeof(Nivarian_Race.Code.Helper.NivarianResearchUtility), nameof(Nivarian_Race.Code.Helper.NivarianResearchUtility.CompletionRatio))]
    internal static class CommanderResearchCompletionRatio
    {
        static bool Prefix(ref float __result)
        {
            var projects = DefDatabase<ResearchProjectDef>.AllDefsListForReading
                .Where(p => p.GetModExtension<Nivarian.ResearchCostExtension>() != null && !CommanderProgression.IsLegacy(p)).ToList();
            __result = projects.Count == 0 ? 0f : (float)projects.Count(p => p.IsFinished) / projects.Count;
            return false;
        }
    }
    [HarmonyPatch(typeof(ResearchProjectDef), "get_PrerequisitesCompleted")]
    internal static class CommanderMissionResearchGate
    {
        static void Postfix(ResearchProjectDef __instance, ref bool __result)
        { if (__result && !__instance.IsFinished && CommanderProgression.ResearchBlocked(__instance) != null) __result = false; }
    }
    [HarmonyPatch(typeof(CompUplinkResearch), nameof(CompUplinkResearch.StartNewProject))]
    internal static class CommanderUplinkStartGate
    {
        static bool Prefix(ResearchProjectDef project)
        {
            string reason = CommanderProgression.ResearchBlocked(project);
            if (reason == null || project?.IsFinished == true) return true;
            Messages.Message(reason, MessageTypeDefOf.RejectInput, false);
            return false;
        }
    }
    [HarmonyPatch(typeof(CompUplinkResearch), "CanResearch")]
    internal static class CommanderUplinkProgressGate
    {
        static void Postfix(CompUplinkResearch __instance, ref bool __result)
        { if (__result && CommanderProgression.ResearchBlocked(__instance.CurrentProject) != null) __result = false; }
    }
    [HarmonyPatch(typeof(CompParticleCollector), "get_IsActive")]
    internal static class CommanderParallelParticleCollectors
    {
        static void Postfix(CompParticleCollector __instance, ref bool __result)
        {
            if (CommanderResearch.Finished("NMC_ParallelParticleCollectors") && __instance.parent.Spawned
                && __instance.parent.Faction == Faction.OfPlayer) __result = true;
        }
    }
}
