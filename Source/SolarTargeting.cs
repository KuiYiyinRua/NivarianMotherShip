using Nivarian_Race.Code.UI;
using RimWorld;
using UnityEngine;
using Verse;

namespace NivarianCommander
{
    // Aiming is local UI state; only Start(Map, cell) is synchronized.
    public static class SolarTargeting
    {
        static Map map;
        static bool previousCursor;
        public static bool Active => map != null;

        public static void Begin(Map targetMap)
        {
            Cancel(); SeydasLegionTargeting.Cancel(); CommanderTargeting.Cancel(); RayTargeting.Cancel(); PurificationTargeting.Cancel();
            string blocked = SolarActions.Unavailable(targetMap);
            if (blocked != null) { Messages.Message(blocked, MessageTypeDefOf.RejectInput, false); return; }
            map = targetMap; previousCursor = Cursor.visible;
            Find.WindowStack.WindowOfType<Window_MothershipSupport>()?.Close();
            Find.Targeter.BeginTargeting(
                new TargetingParameters { canTargetLocations = true, canTargetPawns = false, canTargetBuildings = false, canTargetItems = false },
                Select, Highlight, t => t.Cell.InBounds(targetMap), actionWhenFinished: Cleanup, playSoundOnAction: false,
                onGuiAction: OnGUI);
        }

        static void Select(LocalTargetInfo target)
        {
            if (!Active) return;
            SolarActions.Start(map, target.Cell);
        }

        static void Highlight(LocalTargetInfo target)
        {
            if (!Active || !target.Cell.InBounds(map)) return;
            GenDraw.DrawRadiusRing(target.Cell, 13f,
                CommanderActions.ValidCenter(map, target.Cell) ? new Color(.3f, .85f, 1f) : Color.red);
        }

        static void OnGUI(LocalTargetInfo target)
        {
            Cursor.visible = Mouse.IsInputBlockedNow || !target.Cell.InBounds(map);
            Text.Font = GameFont.Small;
            GUI.color = new Color(.7f, .92f, 1f);
            Widgets.Label(new Rect(18, UI.screenHeight - 115, 740, 28),
                NmcText.Get("NMC_Text0460", DefenseActions.Ability(4).powerCost, DefenseActions.Ability(4).volatileCost));
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
            if (Active && (Current.ProgramState != ProgramState.Playing || map != Find.CurrentMap || !Find.Targeter.IsTargeting || SolarActions.Unavailable(map) != null)) Cancel();
        }
    }
    public sealed class SolarAimWatch : GameComponent
    {
        public SolarAimWatch(Game game) { }
        public override void GameComponentUpdate() => SolarTargeting.Watch();
        public override void LoadedGame() => SolarTargeting.Cancel();
    }
}
