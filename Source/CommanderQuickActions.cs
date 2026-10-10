using Nivarian_Race.Code.UI;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace NivarianCommander
{
    // A local UI router. Existing actions remain the only payment, cooldown and
    // Multiplayer command boundaries; this class never mutates the simulation.
    internal static class CommanderQuickActions
    {
        internal static Map ViewedMap => WorldRendererUtility.DrawingMap ? Find.CurrentMap : null;

        internal static string Unavailable(CommanderAbilityDef def)
        {
            if (CommanderState.Current == null || NativeLedger.Component == null) return NmcText.Get("NMC_Text0202");
            if (CommanderResearch.Unavailable(def) is string researchBlock) return researchBlock;
            var map = ViewedMap;
            if (map == null && def.number != 6 && def.number != 11) return NmcText.Get("NMC_Text0203");
            switch (def.number)
            {
                case 1: case 3: case 12: return DefenseActions.Unavailable(map, def.number);
                case 2: return CommanderActions.FireUnavailable(map);
                case 4: return SolarActions.Unavailable(map);
                case 5: return RayActions.Unavailable(map);
                case 6: return SpacetimeWaveActions.Unavailable();
                case 7: return PurificationActions.Unavailable(map);
                case 8: case 17: return StructureActions.Unavailable(map, def.number);
                case 9: return GhostActions.Unavailable(map);
                case 10: return BlackHoleActions.Unavailable(map);
                case 11: return TimeStopActions.Unavailable();
                case 13: return FleetActions.Unavailable(map);
                case 14: return TebrusLegionActions.Unavailable(map);
                case 15: return RayFleetActions.Unavailable(map);
                case 16: return SeydasLegionActions.Unavailable(map);
                case 18: return VoidCrystalActions.Unavailable(map);
                case 19: return ProjectionActions.Unavailable(map);
                default: return NmcText.Get("NMC_Text0204");
            }
        }

        internal static void Activate(CommanderAbilityDef def)
        {
            // Never trust a cached enabled state at mouse release.
            string reason = Unavailable(def);
            if (reason != null) { Messages.Message(reason, MessageTypeDefOf.RejectInput, false); return; }
            var map = ViewedMap;
            CancelLocalTargeting();
            switch (def.number)
            {
                case 1: case 12: DefenseTargeting.Begin(map, def.number); break;
                case 2: CommanderTargeting.Begin(map); break;
                case 3: DefenseActions.Shield(map); break;
                case 4: SolarTargeting.Begin(map); break;
                case 5: RayTargeting.Begin(map); break;
                case 6: SpacetimeWaveActions.Start(); break;
                case 7: PurificationTargeting.BeginStart(map); break;
                case 8: case 9: case 17: StructureTargeting.Begin(map, def.number); break;
                case 10: BlackHoleTargeting.Begin(map); break;
                case 11: TimeStopActions.Start(); break;
                case 13: FleetTargeting.Begin(map); break;
                case 14: TebrusLegionTargeting.Begin(map); break;
                case 15: RayFleetTargeting.Begin(map); break;
                case 16: SeydasLegionTargeting.Begin(map); break;
                case 18: VoidCrystalTargeting.Begin(map); break;
                case 19: ProjectionTargeting.Begin(map); break;
            }
        }

        internal static void OpenPanel()
        {
            CancelLocalTargeting();
            var window = Find.WindowStack.WindowOfType<Window_MothershipSupport>();
            if (window == null)
            {
                window = new Window_MothershipSupport();
                Find.WindowStack.Add(window);
            }
            NativeCommanderTab.SelectCommander(window);
        }

        static void CancelLocalTargeting()
        {
            ProjectionTargeting.Cancel(); SeydasLegionTargeting.Cancel(); TebrusLegionTargeting.Cancel();
            RayFleetTargeting.Cancel(); FleetTargeting.Cancel(); VoidCrystalTargeting.Cancel();
            BlackHoleTargeting.Cancel(); StructureTargeting.Cancel(); PurificationTargeting.Cancel();
            RayTargeting.Cancel(); SolarTargeting.Cancel(); DefenseTargeting.Cancel(); CommanderTargeting.Cancel();
            // Also leave a vanilla designator/targeter before starting support.
            Find.Targeter?.StopTargeting();
            Find.DesignatorManager?.Deselect();
        }

        internal static string IconPath(CommanderAbilityDef def)
        {
            if (!string.IsNullOrEmpty(def.iconPath)) return def.iconPath;
            switch (def.number)
            {
                case 1: return "NMC/Supports/warp_energy_field";
                case 4: return "NMC/Supports/solar_bombardment";
                case 19: return "NMC/Supports/hyperdimensional_monolith";
                default: return null;
            }
        }
    }
}
