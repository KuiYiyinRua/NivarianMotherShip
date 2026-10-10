using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace NivarianCommander
{
    [HarmonyPatch(typeof(UIRoot_Play), nameof(UIRoot_Play.UIRootOnGUI))]
    public static class DefenseStatus
    {
        static void Postfix()
        {
            if (Find.WindowStack.WindowOfType<Nivarian_Race.Code.UI.Window_MothershipSupport>() != null) return;
            var target = Find.Selector.SingleSelectedThing;
            if (target?.Map == null || !SupportDiplomacy.Friendly(target)) return;
            var defense = target.Map.GetComponent<CommanderDefense>().For(target);
            if (defense == null || (defense.remaining03 <= 0 && defense.remaining12 <= 0)) return;
            var panel = new Rect(UI.screenWidth - 360, UI.screenHeight - 205, 340, 96);
            var font = Text.Font; var color = GUI.color; var anchor = Text.Anchor;
            try
            {
                Text.Font = GameFont.Small; Text.Anchor = TextAnchor.UpperLeft;
                Widgets.DrawBoxSolid(panel, new Color(.045f, .085f, .11f, .95f)); GUI.color = new Color(.5f, .85f, 1f);
                Widgets.Label(new Rect(panel.x + 12, panel.y + 7, 315, 26), NmcText.Get("NMC_Text0241", defense.Total.ToString("0.#"))); GUI.color = Color.white;
                if (defense.remaining03 > 0) Widgets.Label(new Rect(panel.x + 12, panel.y + 34, 315, 24), NmcText.Get("NMC_Text0242", defense.shield03.ToString("0.#"), (defense.remaining03 / 60f).ToString("0.#")));
                if (defense.remaining12 > 0) Widgets.Label(new Rect(panel.x + 12, panel.y + 61, 315, 24), NmcText.Get("NMC_Text0243", defense.shield12.ToString("0.#"), (defense.remaining12 / 60f).ToString("0.#")));
            }
            finally { Text.Font = font; Text.Anchor = anchor; GUI.color = color; }
        }
    }
}
