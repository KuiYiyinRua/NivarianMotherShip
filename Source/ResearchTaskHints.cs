using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using HarmonyLib;
using Nivarian_Race.Code.UI;
using Verse;

namespace NivarianCommander
{
    // Read current campaign state while drawing; never change shared Defs or research progress.
    internal static class ResearchTaskHints
    {
        internal static string Description(Def def)
        {
            var project = def as ResearchProjectDef;
            string reason = project?.IsFinished == true ? null : CommanderProgression.ResearchBlocked(project);
            if (reason == null) return def.description;
            int required = project.GetModExtension<CommanderResearchExtension>().requiredStage;
            int completed = ContinuumCampaign.Current?.completedStage ?? 0;
            return def.description + "\n\n<color=#FF8888>" + NmcText.Get("NMC_ResearchTaskLocked")
                + "\n" + reason + "\n" + NmcText.Get("NMC_ResearchTaskProgress", completed, required) + "</color>";
        }

        internal static TaggedString ButtonText(TaggedString original, ResearchProjectDef project)
        {
            if (project?.IsFinished == true || CommanderProgression.ResearchBlocked(project) == null) return original;
            bool missingResearch = (project.prerequisites?.Any(p => !p.IsFinished) ?? false)
                || (project.hiddenPrerequisites?.Any(p => !p.IsFinished) ?? false);
            return NmcText.Get(missingResearch ? "NMC_ResearchTaskAndResearchButton" : "NMC_ResearchTaskButton");
        }
    }

    [HarmonyPatch(typeof(UplinkResearchTabPanel), "DrawProjectDetails")]
    internal static class ResearchTaskDetailHint
    {
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var code = instructions.ToList();
            var field = AccessTools.Field(typeof(Def), nameof(Def.description));
            var replacement = AccessTools.Method(typeof(ResearchTaskHints), nameof(ResearchTaskHints.Description));
            int matches = 0;
            foreach (var instruction in code)
                if (instruction.opcode == OpCodes.Ldfld && Equals(instruction.operand, field))
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = replacement;
                    matches++;
                }
            if (matches != 1) throw new InvalidOperationException("NMC task detail hint: expected one description field, found " + matches);
            return code;
        }
    }

#if !RIMWORLD_1_7
    [HarmonyPatch(typeof(UplinkResearchTabPanel), "DrawBeaconStatus")]
    internal static class ResearchTaskButtonHint
    {
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var code = instructions.ToList();
            var translate = AccessTools.Method(typeof(Translator), nameof(Translator.Translate), new[] { typeof(string) });
            var replacement = AccessTools.Method(typeof(ResearchTaskHints), nameof(ResearchTaskHints.ButtonText));
            int matches = 0;
            for (int i = 0; i < code.Count - 1; i++)
                if (code[i].opcode == OpCodes.Ldstr && Equals(code[i].operand, "Nivarian.Uplink.MissingPrerequisites")
                    && code[i + 1].Calls(translate))
                {
                    // Keep the original translation for research-only blockers and all other mods.
                    code.InsertRange(i + 2, new[] { new CodeInstruction(OpCodes.Ldarg_2), new CodeInstruction(OpCodes.Call, replacement) });
                    matches++;
                    i += 3;
                }
            if (matches != 1) throw new InvalidOperationException("NMC task button hint: expected one missing prerequisite label, found " + matches);
            return code;
        }
    }
#else
    [HarmonyPatch]
    internal static class ResearchTaskQueueHint
    {
        static System.Reflection.MethodBase TargetMethod() => AccessTools.Method(
            AccessTools.TypeByName("Nivarian_Race.Code.Features.UplinkResearch.UplinkResearchQueueSidebar"), "ActionHint");
        static void Postfix(ResearchProjectDef project, ref string __result)
        {
            string reason = project?.IsFinished == true ? null : CommanderProgression.ResearchBlocked(project);
            if (reason != null) __result = reason + "\n" + __result;
        }
    }
    [HarmonyPatch]
    internal static class ResearchTaskQueueWaiting
    {
        static System.Reflection.MethodBase TargetMethod() => AccessTools.Method(
            AccessTools.TypeByName("Nivarian_Race.Code.Features.UplinkResearch.UplinkResearchQueueComponent"), "WaitingReason");
        static void Postfix(ResearchProjectDef project, ref string __result)
        {
            string reason = project?.IsFinished == true ? null : CommanderProgression.ResearchBlocked(project);
            if (reason != null) __result = reason + (string.IsNullOrEmpty(__result) ? "" : "\n" + __result);
        }
    }
#endif
}
