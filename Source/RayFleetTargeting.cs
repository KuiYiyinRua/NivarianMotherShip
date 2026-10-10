using System.Linq;
using Nivarian_Race.Code.UI;
using RimWorld;
using UnityEngine;
using Verse;

namespace NivarianCommander
{
    public static class RayFleetTargeting
    {
        static Map map;
        static bool previousCursor;
        public static bool Active => map != null;
        public static void Begin(Map owner)
        {
            Cancel(); SeydasLegionTargeting.Cancel(); FleetTargeting.Cancel(); CommanderTargeting.Cancel(); SolarTargeting.Cancel(); RayTargeting.Cancel();
            PurificationTargeting.Cancel(); BlackHoleTargeting.Cancel(); VoidCrystalTargeting.Cancel();
            string blocked = RayFleetActions.Unavailable(owner);
            if (blocked != null) { Messages.Message(blocked, MessageTypeDefOf.RejectInput, false); return; }
            map = owner; previousCursor = Cursor.visible;
            Find.WindowStack.WindowOfType<Window_MothershipSupport>()?.Close();
            Find.Targeter.BeginTargeting(new TargetingParameters { canTargetLocations = true, canTargetPawns = false, canTargetBuildings = false, canTargetItems = false },
                t => { if (Active) RayFleetActions.Start(map, t.Cell); }, Highlight,
                t => RayFleetActions.PlacementError(owner, t.Cell) == null,
                actionWhenFinished: Cleanup, playSoundOnAction: false, onGuiAction: OnGUI);
        }
        static void Highlight(LocalTargetInfo target)
        {
            if (!Active) return;
            var color = RayFleetActions.PlacementError(map, target.Cell) == null ? new Color(.55f,.85f,1f) : Color.red;
            foreach (var cell in RayFleetActions.Cells(target.Cell).Where(c => c.InBounds(map))) GenDraw.DrawRadiusRing(cell, .7f, color);
        }
        static void OnGUI(LocalTargetInfo target)
        {
            if (!Active) return;
            string blocked = RayFleetActions.PlacementError(map, target.Cell);
            Cursor.visible = Mouse.IsInputBlockedNow || blocked != null;
            Text.Font = GameFont.Small; GUI.color = new Color(.76f,.84f,1f);
            Widgets.Label(new Rect(18, UI.screenHeight - 115, 1100, 28), blocked ?? NmcText.Get("NMC_Text0445", DefenseActions.Ability(15).powerCost, DefenseActions.Ability(15).volatileCost));
            GUI.color = Color.white;
        }
        static void Cleanup() { map = null; Cursor.visible = previousCursor; }
        public static void Cancel()
        {
            if (!Active) return;
            if (Find.UIRoot is UIRoot_Play play) play.mapUI?.targeter?.StopTargeting();
            if (Active) Cleanup();
        }
        public static void Watch()
        {
            if (Active && (Current.ProgramState != ProgramState.Playing || map != Find.CurrentMap
                || !Find.Targeter.IsTargeting || RayFleetActions.Unavailable(map) != null)) Cancel();
        }
    }
    public sealed class RayFleetAimWatch : GameComponent
    {
        public RayFleetAimWatch(Game game) { }
        public override void GameComponentUpdate() => RayFleetTargeting.Watch();
        public override void LoadedGame() => RayFleetTargeting.Cancel();
    }
}
