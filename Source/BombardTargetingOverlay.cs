using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace NivarianCommander
{
    [HarmonyPatch(typeof(UIRoot_Play), nameof(UIRoot_Play.UIRootOnGUI))]
    internal static class BombardTargetingOverlay
    {
        static Texture2D frame;
        static void Prefix()
        {
            if (Event.current.type != EventType.Repaint || Current.ProgramState != ProgramState.Playing
                || !WorldRendererUtility.DrawingMap || Find.CurrentMap == null || !Find.Targeter.IsTargeting
                || !(CommanderTargeting.Active || SolarTargeting.Active || RayTargeting.Active || PurificationTargeting.Active)) return;
            if (frame == null) frame = ContentFinder<Texture2D>.Get("NMC/UI/bombard_targeting_overlay");
            var old = GUI.color;
            try
            {
                // Armor is opaque like CommanderHud; the texture alpha keeps the map viewport open.
                GUI.color = Color.white;
                GUI.DrawTexture(new Rect(2, 2, UI.screenWidth - 4, UI.screenHeight - 38), frame, ScaleMode.StretchToFill, true);
            }
            finally { GUI.color = old; }
        }
    }
}
