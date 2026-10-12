using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace NivarianCommander
{
    // Settings UI dispatches a synchronized command; local preferences never drive MP ticks.
    public sealed class CommanderResearchAccess : WorldComponent
    {
        public static Func<bool> MultiplayerActive = () => false;
        static readonly System.Reflection.FieldInfo Progress = AccessTools.Field(typeof(ResearchManager), "progress");
        public static CommanderResearchAccess Current => Find.World?.GetComponent<CommanderResearchAccess>();
        public bool enabled;
        bool pending;
        public CommanderResearchAccess(World world) : base(world) { }

        public override void FinalizeInit(bool fromLoad)
        {
            base.FinalizeInit(fromLoad);
            if (!MultiplayerActive())
            {
                enabled = CommanderMod.Settings?.unlockAllResearch == true;
                pending = enabled;
            }
        }

        public static void SetEnabled(bool value)
        {
            var state = Current;
            if (state == null) return;
            state.enabled = value;
            state.pending = value;
        }

        public override void WorldComponentTick()
        {
            if (!enabled || !pending || Find.ResearchManager == null) return;
            var progress = (Dictionary<ResearchProjectDef, float>)Progress.GetValue(Find.ResearchManager);
            foreach (var project in DefDatabase<ResearchProjectDef>.AllDefsListForReading
                .Where(p => p.modContentPack?.PackageIdPlayerFacing.Equals("local.nivarian.mothership.commander", StringComparison.OrdinalIgnoreCase) == true)
                .OrderBy(p => p.defName, StringComparer.Ordinal))
                progress[project] = project.baseCost;
            Find.ResearchManager.ReapplyAllMods();
            pending = false;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref enabled, "nmcUnlockAllResearch", false);
            Scribe_Values.Look(ref pending, "nmcUnlockAllResearchPending", false);
        }
    }
}
