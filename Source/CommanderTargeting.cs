using System;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace NivarianCommander
{
    public static class CommanderTargeting
    {
        static Map map;
        static bool previousCursor;
        static readonly System.Reflection.FieldInfo StopFlag = AccessTools.Field(typeof(Targeter), "needsStopTargetingCall");
        public static bool Active => map != null;
        public static void Begin(Map targetMap)
        {
            Cancel(); SeydasLegionTargeting.Cancel();
            string reason = CommanderActions.FireUnavailable(targetMap);
            if (reason != null) { Messages.Message(reason, MessageTypeDefOf.RejectInput, false); return; }
            Find.WindowStack.WindowOfType<Nivarian_Race.Code.UI.Window_MothershipSupport>()?.Close();
            map = targetMap; previousCursor = Cursor.visible;
            Find.Targeter.BeginTargeting(new TargetingParameters { canTargetLocations = true, canTargetPawns = false, canTargetBuildings = false, canTargetItems = false },
                Select, Highlight, t => t.Cell.InBounds(targetMap), actionWhenFinished: Cleanup, playSoundOnAction: false, onGuiAction: OnGUI);
        }
        // Local callback stays in targeting mode; only Fire crosses the synchronization boundary.
        public static void Select(LocalTargetInfo target)
        {
            if (!Active) return;
            StopFlag.SetValue(Find.Targeter, false);
            CommanderActions.Fire(map, target.Cell);
            Watch();
        }
        static void Highlight(LocalTargetInfo target)
        {
            if (!Active || !target.Cell.InBounds(map)) return;
            GenDraw.DrawRadiusRing(target.Cell, 3f, CommanderActions.ValidCenter(map, target.Cell) ? new Color(.35f, .85f, 1f) : Color.red);
        }
        static void OnGUI(LocalTargetInfo target)
        {
            Cursor.visible = Mouse.IsInputBlockedNow || !target.Cell.InBounds(map);
            Text.Font = GameFont.Small;
            GUI.color = new Color(.65f, .9f, 1);
            Widgets.Label(new Rect(18, UI.screenHeight - 115, 700, 28), NmcText.Get("NMC_Text0205", CommanderActions.OrbitalPowerCost, CommanderActions.OrbitalVolatileCost));
            GUI.color = Color.white;
        }
        static void Cleanup() { map = null; Cursor.visible = previousCursor; }
        public static void Cancel()
        {
            if (!Active) return;
            if (Find.UIRoot is UIRoot_Play play) play.mapUI?.targeter?.StopTargeting();
            // A load or a return to the menu can replace the old targeter and its finish callback.
            if (Active) Cleanup();
        }
        public static void Watch()
        {
            if (!Active) return;
            if (Current.ProgramState != ProgramState.Playing || map != Find.CurrentMap || !Find.Targeter.IsTargeting || CommanderActions.FireUnavailable(map) != null)
                Cancel();
        }
    }
    public sealed class TargetingWatch : GameComponent
    {
        public TargetingWatch(Game game) { }
        public override void GameComponentUpdate() => CommanderTargeting.Watch();
        public override void LoadedGame() => CommanderTargeting.Cancel();
    }
}
