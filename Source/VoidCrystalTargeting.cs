using Nivarian_Race.Code.UI;
using RimWorld;
using UnityEngine;
using Verse;

namespace NivarianCommander
{
    // Cursor/camera selection stays local; only Start(map,cell) will be synchronized.
    public static class VoidCrystalTargeting
    {
        static Map map;
        static bool previousCursor;
        public static bool Active => map != null;
        public static void Begin(Map owner)
        {
            Cancel(); SeydasLegionTargeting.Cancel(); CommanderTargeting.Cancel(); SolarTargeting.Cancel(); RayTargeting.Cancel();
            PurificationTargeting.Cancel(); BlackHoleTargeting.Cancel();
            string blocked = VoidCrystalActions.Unavailable(owner);
            if (blocked != null) { Messages.Message(blocked, MessageTypeDefOf.RejectInput, false); return; }
            map = owner; previousCursor = Cursor.visible;
            Find.WindowStack.WindowOfType<Window_MothershipSupport>()?.Close();
            Find.Targeter.BeginTargeting(
                new TargetingParameters { canTargetLocations = true, canTargetPawns = false,
                    canTargetBuildings = false, canTargetItems = false },
                Select, Highlight, t => VoidCrystalMap.PlacementError(owner, t.Cell) == null,
                actionWhenFinished: Cleanup, playSoundOnAction: false, onGuiAction: OnGUI);
        }
        static void Select(LocalTargetInfo target)
        { if (Active) VoidCrystalActions.Start(map, target.Cell); }
        static void Highlight(LocalTargetInfo target)
        {
            if (!Active || !target.Cell.InBounds(map)) return;
            GenDraw.DrawRadiusRing(target.Cell, VoidCrystalMap.Radius,
                VoidCrystalMap.PlacementError(map, target.Cell) == null ? new Color(.55f, .73f, 1f) : Color.red);
        }
        static void OnGUI(LocalTargetInfo target)
        {
            if (!Active) return;
            string blocked = VoidCrystalMap.PlacementError(map, target.Cell);
            Cursor.visible = Mouse.IsInputBlockedNow || blocked != null;
            Text.Font = GameFont.Small; GUI.color = new Color(.76f, .84f, 1f);
            Widgets.Label(new Rect(18, UI.screenHeight - 115, 900, 28), blocked == null
                ? NmcText.Get("NMC_Text0480", DefenseActions.Ability(18).powerCost, DefenseActions.Ability(18).volatileCost)
                : NmcText.Get("NMC_Text0481", blocked));
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
                || !Find.Targeter.IsTargeting || VoidCrystalActions.Unavailable(map) != null)) Cancel();
        }
    }
    public sealed class VoidCrystalAimWatch : GameComponent
    {
        public VoidCrystalAimWatch(Game game) { }
        public override void GameComponentUpdate() => VoidCrystalTargeting.Watch();
        public override void LoadedGame() => VoidCrystalTargeting.Cancel();
    }
}
