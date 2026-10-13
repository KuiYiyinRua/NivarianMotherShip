using System;
using RimWorld.Planet;
using Verse;

namespace NivarianCommander
{
    // Settings UI dispatches a synchronized command; local preferences never drive MP ticks.
    public sealed class CommanderResearchAccess : WorldComponent
    {
        public static Func<bool> MultiplayerActive = () => false;
        public static CommanderResearchAccess Current => Find.World?.GetComponent<CommanderResearchAccess>();
        public bool enabled;
        public CommanderResearchAccess(World world) : base(world) { }

        public override void FinalizeInit(bool fromLoad)
        {
            base.FinalizeInit(fromLoad);
            if (!MultiplayerActive())
            {
                enabled = CommanderMod.Settings?.researchWithoutMissions == true;
            }
        }

        public static void SetEnabled(bool value)
        {
            var state = Current;
            if (state == null) return;
            state.enabled = value;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            // This is a new opt-in rule, not the old complete-all-research cheat.
            Scribe_Values.Look(ref enabled, "nmcResearchWithoutMissions", false);
        }
    }
}
