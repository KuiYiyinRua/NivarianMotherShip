using System;
using HarmonyLib;
using Nivarian_Race.Code.UI;
using RimWorld;
using UnityEngine;
using Verse;

namespace NivarianCommander
{
    public static class DefenseTargeting
    {
        static Map map;
        static int number;
        static bool cancelling;
        static bool previousCursor;
        public static bool Active => map != null;
        public static void Begin(Map selectedMap, int ability, bool cancel = false)
        {
            Cancel(); SeydasLegionTargeting.Cancel(); CommanderTargeting.Cancel();
            string reason = cancel ? null : DefenseActions.Unavailable(selectedMap, ability);
            if (reason != null) { Messages.Message(reason, MessageTypeDefOf.RejectInput, false); return; }
            if (selectedMap == null) return;
            map = selectedMap; number = ability; cancelling = cancel; previousCursor = Cursor.visible;
            Find.WindowStack.WindowOfType<Window_MothershipSupport>()?.Close();
            var parameters = new TargetingParameters { canTargetLocations = ability == 1, canTargetBuildings = ability == 12, canTargetPawns = false, canTargetItems = false, validator = t => ability == 1 ? t.Cell.InBounds(selectedMap) : t.Thing is Building && SupportDiplomacy.Friendly(t.Thing) };
            Find.Targeter.BeginTargeting(parameters, Select, Highlight, t => t.Cell.InBounds(selectedMap), actionWhenFinished: Cleanup, playSoundOnAction: false);
        }
        public static void Select(LocalTargetInfo target)
        {
            if (!Active) return;
            if (number == 1) DefenseActions.DeployField(map, target.Cell);
            else if (cancelling) DefenseActions.CancelOverload(map, target.Thing);
            else DefenseActions.Overload(map, target.Thing);
            // A single selection sends exactly one command. Native Targeter closes it.
        }
        static void Highlight(LocalTargetInfo target)
        { if (map == null) return; if (number == 1) GenDraw.DrawRadiusRing(target.Cell, 8f); else if (target.HasThing) GenDraw.DrawTargetHighlight(target); }
        static void Cleanup() { map = null; Cursor.visible = previousCursor; }
        public static void Cancel()
        { if (!Active) return; if (Find.UIRoot is UIRoot_Play play) play.mapUI?.targeter?.StopTargeting(); if (Active) Cleanup(); }
        public static void Watch()
        { if (Active && (Current.ProgramState != ProgramState.Playing || Find.CurrentMap != map || !Find.Targeter.IsTargeting || (!cancelling && DefenseActions.Unavailable(map, number) != null))) Cancel(); }
    }
    public sealed class DefenseAimWatch : GameComponent
    {
        public DefenseAimWatch(Game game) { }
        public override void GameComponentUpdate() => DefenseTargeting.Watch();
        public override void LoadedGame() => DefenseTargeting.Cancel();
    }
}
