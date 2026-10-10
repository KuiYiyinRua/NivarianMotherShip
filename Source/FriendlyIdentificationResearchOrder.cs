using System.Collections.Generic;
using HarmonyLib;
using Nivarian_Race.Code.UI;
using RimWorld;
using Verse;

namespace NivarianCommander
{
    // Presentation only: retain the native filters and place the new technology after orbital support.
    [HarmonyPatch(typeof(UplinkResearchTabPanel), "RebuildFilterCaches")]
    internal static class FriendlyIdentificationResearchOrder
    {
        static void Postfix(List<ResearchProjectDef> ____filteredCache)
        {
            if (____filteredCache == null) return;
            var research = ____filteredCache.Find(p => p.defName == CommanderResearch.FriendlyIdentification);
            if (research == null) return;
            int last = ____filteredCache.FindLastIndex(IsOrbitalSupport);
            if (last < 0) return;
            ____filteredCache.Remove(research);
            last = ____filteredCache.FindLastIndex(IsOrbitalSupport);
            ____filteredCache.Insert(last + 1, research);
        }
        static bool IsOrbitalSupport(ResearchProjectDef project) => project.defName == "NMC_Support02"
            || project.defName == "NMC_Support04" || project.defName == "NMC_Support05" || project.defName == "NMC_Support07";
    }
}
