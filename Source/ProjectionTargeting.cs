using System.Linq;
using Nivarian_Race.Code.UI;
using RimWorld;
using Verse;

namespace NivarianCommander
{
    public static class ProjectionTargeting
    {
        static Map map;
        public static bool Active => map != null;
        public static void Begin(Map selectedMap)
        {
            Cancel(); StructureTargeting.Cancel(); SeydasLegionTargeting.Cancel(); TebrusLegionTargeting.Cancel();
            RayFleetTargeting.Cancel(); FleetTargeting.Cancel(); VoidCrystalTargeting.Cancel(); BlackHoleTargeting.Cancel();
            SolarTargeting.Cancel(); RayTargeting.Cancel(); PurificationTargeting.Cancel(); DefenseTargeting.Cancel(); CommanderTargeting.Cancel();
            string reason = ProjectionActions.Unavailable(selectedMap);
            if (reason != null) { Messages.Message(reason, MessageTypeDefOf.RejectInput, false); return; }
            map = selectedMap; Find.WindowStack.WindowOfType<Window_MothershipSupport>()?.Close();
            var parameters = new TargetingParameters { canTargetLocations = true, canTargetBuildings = false,
                canTargetPawns = false, canTargetItems = false,
                validator = target => ProjectionActions.Plan(map, target.Cell, false).Count > 0 };
            Find.Targeter.BeginTargeting(parameters, Select, Highlight, target => target.Cell.InBounds(selectedMap),
                actionWhenFinished: Cleanup, playSoundOnAction: false);
        }
        public static void Select(LocalTargetInfo target) { if (Active) ProjectionActions.Start(map, target.Cell); }
        static void Highlight(LocalTargetInfo target)
        {
            if (!Active || !target.Cell.InBounds(map)) return;
            GenDraw.DrawRadiusRing(target.Cell, 20);
            foreach (var p in ProjectionActions.Plan(map, target.Cell, false))
                GenDraw.DrawFieldEdges(GenAdj.OccupiedRect(p.cell, p.source.Rotation, p.source.def.Size).Cells.ToList());
        }
        static void Cleanup() => map = null;
        public static void Cancel()
        {
            if (!Active) return;
            if (Find.UIRoot is UIRoot_Play play) play.mapUI?.targeter?.StopTargeting();
            if (Active) Cleanup();
        }
        public static void Watch()
        {
            if (Active && (Current.ProgramState != ProgramState.Playing || Find.CurrentMap != map
                || !Find.Targeter.IsTargeting || ProjectionActions.Unavailable(map) != null)) Cancel();
        }
    }
    public sealed class ProjectionAimWatch : GameComponent
    {
        public ProjectionAimWatch(Game game) { }
        public override void GameComponentUpdate() => ProjectionTargeting.Watch();
        public override void LoadedGame()
        {
            ProjectionTargeting.Cancel();
            ProjectionRegistry.Current?.FinalizeAfterLoad();
        }
    }
}
