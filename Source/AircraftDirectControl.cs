using System.Linq;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace NivarianCommander
{
    // Selector runs after windows, gizmos, designators and targeting consume input.
    [HarmonyPatch(typeof(Selector), "HandleMapClicks")]
    public static class AircraftDirectControl
    {
        public static void Prefix()
        {
            var ev = Event.current;
            var map = Find.CurrentMap;
            if (ev == null || ev.type != EventType.MouseDown || ev.button != 1 || map == null
                || !WorldRendererUtility.DrawingMap || Find.Targeter.IsTargeting
                || Find.DesignatorManager.SelectedDesignator != null) return;
            var selected = Find.Selector.SelectedObjectsListForReading;
            // Preserve the normal colonist menu for mixed selections.
            if (selected.Count == 0 || selected.Any(o => !(o is Building_NivarianAircraft ship)
                || !ship.CombatFlight || ship.Faction != Faction.OfPlayer || ship.Map != map)) return;
            var cell = UI.MouseCell();
            foreach (var ship in selected.Cast<Building_NivarianAircraft>().OrderBy(s => s.thingIDNumber))
                AircraftActions.Move(map, ship, cell);
            ev.Use();
        }
    }

    // All native ground/arrival/departure draws share this hull correction.
    public sealed class Graphic_GravityFrigate : Graphic_Single
    {
        public override void DrawWorker(Vector3 loc, Rot4 rot, ThingDef thingDef, Thing thing, float extraRotation)
            => base.DrawWorker(loc, rot, thingDef, thing, extraRotation + 180f);
        public override Graphic GetColoredVersion(Shader shader, Color color, Color colorTwo)
            => GraphicDatabase.Get<Graphic_GravityFrigate>(path, shader, drawSize, color, colorTwo, data);
    }
}
