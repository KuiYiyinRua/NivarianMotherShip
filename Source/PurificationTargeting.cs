using System.Linq;
using Nivarian_Race.Code.UI;
using RimWorld;
using UnityEngine;
using Verse;

namespace NivarianCommander
{
    // Only Start/Move are synchronized. The cursor and preview never touch simulation state.
    public static class PurificationTargeting
    {
        static Map map;
        static int moveSerial = -1;
        static bool previousCursor;
        public static bool Active => map != null;

        public static void BeginStart(Map targetMap)
        {
            string blocked = PurificationActions.Unavailable(targetMap);
            if (blocked != null) { Messages.Message(blocked, MessageTypeDefOf.RejectInput, false); return; }
            Begin(targetMap, -1);
        }

        public static void BeginMove(Map targetMap, int serial)
        {
            if (targetMap?.GetComponent<PurificationBeamMap>()?.ActiveBeams.Any(b => b.serial == serial && b.drafted) != true) return;
            Begin(targetMap, serial);
        }

        static void Begin(Map targetMap, int serial)
        {
            Cancel(); SeydasLegionTargeting.Cancel(); CommanderTargeting.Cancel(); SolarTargeting.Cancel(); RayTargeting.Cancel();
            map = targetMap; moveSerial = serial; previousCursor = Cursor.visible;
            Find.WindowStack.WindowOfType<Window_MothershipSupport>()?.Close();
            Find.Targeter.BeginTargeting(
                new TargetingParameters { canTargetLocations = true, canTargetPawns = false, canTargetBuildings = false, canTargetItems = false },
                Select, Highlight, t => t.Cell.InBounds(targetMap), actionWhenFinished: Cleanup, playSoundOnAction: false,
                onGuiAction: OnGUI);
        }

        static void Select(LocalTargetInfo target)
        {
            if (!Active) return;
            if (moveSerial < 0) PurificationActions.Start(map, target.Cell);
            else PurificationActions.Move(map, moveSerial, target.Cell);
        }

        static void Highlight(LocalTargetInfo target)
        {
            if (!Active || !target.Cell.InBounds(map)) return;
            if (moveSerial < 0) GenDraw.DrawRadiusRing(target.Cell, PurificationBeamMap.DamageRadius,
                CommanderActions.ValidCenter(map, target.Cell) ? new Color(.3f, .88f, 1f) : Color.red);
            else
            {
                GenDraw.DrawTargetHighlight(target);
                var beam = map.GetComponent<PurificationBeamMap>().ActiveBeams.FirstOrDefault(b => b.serial == moveSerial);
                if (beam != null) GenDraw.DrawLineBetween(
                    new Vector3(beam.position.x + .5f, AltitudeLayer.MetaOverlays.AltitudeFor(), beam.position.y + .5f),
                    target.Cell.ToVector3Shifted(), SimpleColor.Cyan, .1f);
            }
        }

        static void OnGUI(LocalTargetInfo target)
        {
            Cursor.visible = Mouse.IsInputBlockedNow || !target.Cell.InBounds(map);
            Text.Font = GameFont.Small; GUI.color = new Color(.7f, .92f, 1f);
            Widgets.Label(new Rect(18, UI.screenHeight - 115, 880, 28), moveSerial < 0
                ? NmcText.Get("NMC_Text0434")
                : NmcText.Get("NMC_Text0435"));
            GUI.color = Color.white;
        }

        static void Cleanup() { map = null; moveSerial = -1; Cursor.visible = previousCursor; }
        public static void Cancel()
        {
            if (!Active) return;
            if (Find.UIRoot is UIRoot_Play play) play.mapUI?.targeter?.StopTargeting();
            if (Active) Cleanup();
        }
        public static void Watch()
        {
            if (!Active) return;
            bool commandReady = moveSerial < 0 ? PurificationActions.Unavailable(map) == null :
                map.GetComponent<PurificationBeamMap>().ActiveBeams.Any(b => b.serial == moveSerial && b.drafted);
            if (Current.ProgramState != ProgramState.Playing || map != Find.CurrentMap || !Find.Targeter.IsTargeting || !commandReady) Cancel();
        }
    }

    public sealed class PurificationAimWatch : GameComponent
    {
        public PurificationAimWatch(Game game) { }
        public override void GameComponentUpdate() => PurificationTargeting.Watch();
        public override void LoadedGame() => PurificationTargeting.Cancel();
    }
}
