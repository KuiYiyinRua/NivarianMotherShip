using Nivarian_Race.Code.UI;
using RimWorld;
using UnityEngine;
using Verse;

namespace NivarianCommander
{
    // Cursor and targeting are local UI. Only Start(Map, cell) enters MP sync.
    public static class BlackHoleTargeting
    {
        static Map map;
        static bool previousCursor;
        public static bool Active => map != null;
        public static void Begin(Map targetMap)
        {
            Cancel(); SeydasLegionTargeting.Cancel(); CommanderTargeting.Cancel(); SolarTargeting.Cancel(); RayTargeting.Cancel();
            PurificationTargeting.Cancel();
            string blocked = BlackHoleActions.Unavailable(targetMap);
            if (blocked != null) { Messages.Message(blocked, MessageTypeDefOf.RejectInput, false); return; }
            map = targetMap; previousCursor = Cursor.visible;
            Find.WindowStack.WindowOfType<Window_MothershipSupport>()?.Close();
            Find.Targeter.BeginTargeting(
                new TargetingParameters { canTargetLocations = true, canTargetPawns = false,
                    canTargetBuildings = false, canTargetItems = false },
                Select, Highlight, t => BlackHoleMap.ValidCenter(targetMap, t.Cell), actionWhenFinished: Cleanup,
                playSoundOnAction: false, onGuiAction: OnGUI);
        }
        static void Select(LocalTargetInfo target)
        {
            if (Active) BlackHoleActions.Start(map, target.Cell);
        }
        static void Highlight(LocalTargetInfo target)
        {
            if (!Active || !target.Cell.InBounds(map)) return;
            GenDraw.DrawRadiusRing(target.Cell, BlackHoleMap.Radius,
                BlackHoleMap.ValidCenter(map, target.Cell) ? new Color(.25f, .86f, 1f) : Color.red);
        }
        static void OnGUI(LocalTargetInfo target)
        {
            Cursor.visible = Mouse.IsInputBlockedNow || !BlackHoleMap.ValidCenter(map, target.Cell);
            Text.Font = GameFont.Small;
            GUI.color = new Color(.73f, .94f, 1f);
            Widgets.Label(new Rect(18, UI.screenHeight - 115, 850, 28),
                NmcText.Get("NMC_Text0040", DefenseActions.Ability(10).powerCost, DefenseActions.Ability(10).volatileCost));
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
            if (Active && (Current.ProgramState != ProgramState.Playing || map != Find.CurrentMap ||
                !Find.Targeter.IsTargeting || BlackHoleActions.Unavailable(map) != null)) Cancel();
        }
    }
    public sealed class BlackHoleAimWatch : GameComponent
    {
        public BlackHoleAimWatch(Game game) { }
        public override void GameComponentUpdate() => BlackHoleTargeting.Watch();
        public override void LoadedGame() => BlackHoleTargeting.Cancel();
    }
}
