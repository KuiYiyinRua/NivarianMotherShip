using System;
using HarmonyLib;
using Nivarian_Race.Code.UI;
using Nivarian_Race.Code.Defs;
using UnityEngine;
using Verse;

namespace NivarianCommander
{
    public sealed class CommanderMod : Mod
    {
        public static CommanderSettings Settings;
        public override string SettingsCategory() => "Nivarian Race: MotherShip";
        public override void DoSettingsWindowContents(Rect inRect)
        {
            var listing = new Listing_Standard();
            listing.Begin(inRect);
            listing.CheckboxLabeled(NmcText.Get("NMC_SettingsShortcut"), ref Settings.showShortcut);
            listing.Gap(16f);
            bool value = CommanderResearchAccess.Current?.enabled ?? Settings.researchWithoutMissions;
            bool previous = value;
            listing.CheckboxLabeled(NmcText.Get("NMC_SettingsUnlockAll"), ref value);
            listing.Label(NmcText.Get("NMC_SettingsUnlockAllHint"));
            if (value != previous)
            {
                Settings.researchWithoutMissions = value;
                Settings.Write();
                if (Current.ProgramState == ProgramState.Playing) CommanderResearchAccess.SetEnabled(value);
            }
            listing.End();
        }
        public CommanderMod(ModContentPack content) : base(content)
        {
            Settings = GetSettings<CommanderSettings>();
            CommanderThinkKeyCompatibility.Install();
            CommanderOptimizerCompatibility.Install();
            LongEventHandler.ExecuteWhenFinished(() =>
            {
                new Harmony("local.nivarian.mothership.commander").PatchAll();
                if (RimWorld.StatDefOf.MoveSpeed.parts == null) RimWorld.StatDefOf.MoveSpeed.parts = new System.Collections.Generic.List<RimWorld.StatPart>();
                if (!RimWorld.StatDefOf.MoveSpeed.parts.Exists(part => part is StatPart_GhostSpeed))
                    RimWorld.StatDefOf.MoveSpeed.parts.Add(new StatPart_GhostSpeed { parentStat = RimWorld.StatDefOf.MoveSpeed });
                if (!RimWorld.StatDefOf.MoveSpeed.parts.Exists(part => part is StatPart_LegionSpeed))
                    RimWorld.StatDefOf.MoveSpeed.parts.Add(new StatPart_LegionSpeed { parentStat = RimWorld.StatDefOf.MoveSpeed });
                SpacetimeWaveStats.Install();
                BlackHoleArmorStats.Install();
                WarshipResearch.Install();
                FleetWarpState.Validate();
                Log.Message("[NMC] node 7 S13 isolated candidate registered; nine-slot fleet, native flight, attack target and temporary material lifecycle hooks resolved.");
            });
        }
    }
    [HarmonyPatch(typeof(Window_MothershipSupport), nameof(Window_MothershipSupport.DoWindowContents))]
    public static class NativeCommanderTab
    {
        static readonly System.Reflection.FieldInfo TabField = AccessTools.Field(typeof(Window_MothershipSupport), "SelectedTab");
        static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Window_MothershipSupport, CommanderPanel> Panels
            = new System.Runtime.CompilerServices.ConditionalWeakTable<Window_MothershipSupport, CommanderPanel>();
        public static bool Prefix(Window_MothershipSupport __instance, Rect inRect)
        {
            var tab = TabField.GetValue(__instance) as NivarianMSSRTabDef;
            if (tab?.defName != "NMC_Commander") return true;
            Panels.GetValue(__instance, _ => new CommanderPanel()).Draw(inRect,
                () => TabField.SetValue(__instance, DefDatabase<NivarianMSSRTabDef>.GetNamed("NivarianMSSR_Supporting")));
            return false;
        }
        public static void SelectCommander(Window_MothershipSupport window)
            => TabField.SetValue(window, DefDatabase<NivarianMSSRTabDef>.GetNamed("NMC_Commander"));
        public static CommanderPanel PanelFor(Window_MothershipSupport window) => Panels.GetValue(window, _ => new CommanderPanel());
    }
    [HarmonyPatch(typeof(RimWorld.UIRoot_Play), nameof(RimWorld.UIRoot_Play.UIRootOnGUI))]
    public static class CommanderShortcut
    {
        // Register the native window before high-priority input and map clicks.
        // Drawing a button in the old postfix happened after Selector/Targeter.
        public static void Prefix() => CommanderHud.EnsureWindow();
    }
}
