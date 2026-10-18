using System.Linq;
using Nivarian_Race.Code.UI;
using RimWorld;
using Verse;

namespace NivarianCommander
{
    public static class StructureTargeting
    {
        static Map map;
        static int number;
        public static bool Active => map != null;
        public static void Begin(Map selectedMap, int ability)
        {
            Cancel(); SeydasLegionTargeting.Cancel(); DefenseTargeting.Cancel(); CommanderTargeting.Cancel();
            string reason = ability == 9 ? GhostActions.Unavailable(selectedMap) : StructureActions.Unavailable(selectedMap, ability);
            if (reason != null) { Messages.Message(reason, MessageTypeDefOf.RejectInput, false); return; }
            map = selectedMap; number = ability;
            Find.WindowStack.WindowOfType<Window_MothershipSupport>()?.Close();
            var parameters = new TargetingParameters
            {
                canTargetLocations = true, canTargetBuildings = false, canTargetPawns = false, canTargetItems = false,
                validator = target => number == 9 ? GhostActions.LandingCells(map, target.Cell).Count == 4
                    : StructureActions.PlacementError(map, target.Cell, StructureActions.CoreFor(number)) == null
            };
            Find.Targeter.BeginTargeting(parameters, Select, Highlight,
                target => target.Cell.InBounds(selectedMap), actionWhenFinished: Cleanup, playSoundOnAction: false);
        }
        public static void Select(LocalTargetInfo target)
        {
            if (!Active) return;
            if (number == 9) GhostActions.Deploy(map, target.Cell);
            else StructureActions.DeployCore(map, target.Cell, number);
        }
        static void Highlight(LocalTargetInfo target)
        {
            if (!Active || !target.Cell.InBounds(map)) return;
            if (number == 9) { GenDraw.DrawFieldEdges(GhostActions.LandingCells(map, target.Cell)); return; }
            var definition = StructureActions.CoreFor(number);
            if (definition == null) return;
            var footprint = GenAdj.OccupiedRect(target.Cell, Rot4.North, definition.Size);
            GenDraw.DrawFieldEdges(footprint.Cells.ToList());
            if (number == 8)
            {
                var tower = DefDatabase<ThingDef>.GetNamedSilentFail("NMC_ShadowEnergyTower");
                float radius = tower?.GetCompProperties<CompProperties_CommanderStructure>()?.cloakRadius ?? 0f;
                GenDraw.DrawFieldEdges(ShadowCloakRange.Cells(footprint, map, radius));
            }
        }
        static void Cleanup() { map = null; number = 0; }
        public static void Cancel()
        {
            if (!Active) return;
            if (Find.UIRoot is UIRoot_Play play) play.mapUI?.targeter?.StopTargeting();
            if (Active) Cleanup();
        }
        public static void Watch()
        {
            if (Active && (Current.ProgramState != ProgramState.Playing || Find.CurrentMap != map
                || !Find.Targeter.IsTargeting || (number == 9 ? GhostActions.Unavailable(map) : StructureActions.Unavailable(map, number)) != null)) Cancel();
        }
    }
    public sealed class StructureAimWatch : GameComponent
    {
        public StructureAimWatch(Game game) { }
        public override void GameComponentUpdate() => StructureTargeting.Watch();
        public override void LoadedGame() => StructureTargeting.Cancel();
    }
}
