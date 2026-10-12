using System;
using System.Collections.Generic;
using LumiParticle.Assets;
using Nivarian;
using Nivarian.Helper;
using UnityEngine;
using Verse;
using Theme = NivarianCommander.CommanderPanelTheme;

namespace NivarianCommander
{
    // Native portrait + native projection shader + native typewriter, entirely local to this window.
    public sealed class PointShopNira : IDisposable
    {
        static Material template;
        Material projection;
        readonly StoryDialogTypewriter writer = new StoryDialogTypewriter();
        readonly NivarianStoryDialog dialog = new NivarianStoryDialog { speaker = NmcText.Get("NMC_Text0306"), uiTexturePath = "Nivarian/UI/nira" };
        readonly Dictionary<string, int> rotations = new Dictionary<string, int>();
        int serial;
        float nextChat;
        Vector2 dialogueScroll;
        public string text;

        static Dictionary<string, string[]> Lines => new Dictionary<string, string[]>
        {
            ["welcome"] = new[] { NmcText.Get("NMC_Text0307"), NmcText.Get("NMC_Text0308"), NmcText.Get("NMC_Text0309") },
            ["idle"] = new[] {
                NmcText.Get("NMC_Text0310"), NmcText.Get("NMC_Text0311"),
                NmcText.Get("NMC_Text0312"), NmcText.Get("NMC_Text0313"),
                NmcText.Get("NMC_Text0314"), NmcText.Get("NMC_Text0315"),
                NmcText.Get("NMC_Text0316"), NmcText.Get("NMC_Text0317") },
            ["buy"] = new[] { NmcText.Get("NMC_Text0318"), NmcText.Get("NMC_Text0319"), NmcText.Get("NMC_Text0320"), NmcText.Get("NMC_Text0321") },
            ["donate"] = new[] { NmcText.Get("NMC_Text0322"), NmcText.Get("NMC_Text0323"), NmcText.Get("NMC_Text0324"), NmcText.Get("NMC_Text0325") },
            ["refresh"] = new[] { NmcText.Get("NMC_Text0326"), NmcText.Get("NMC_Text0327") },
            ["error"] = new[] { NmcText.Get("NMC_Text0328"), NmcText.Get("NMC_Text0329") }
        };

        public PointShopNira()
        {
            if (template == null) LumiAssetLoader.Instance?.GetMaterial("projectionEfx", out template, true, false);
            if (template != null)
            {
                projection = new Material(template);
                projection.mainTexture = dialog.PortraitTexture;
                projection.SetTexture("_ScanLineTex", dialog.PortraitTexture);
            }
            Say("welcome");
        }
        public void Say(string kind)
        {
            if (!Lines.TryGetValue(kind, out var choices)) choices = Lines["idle"];
            rotations.TryGetValue(kind, out int index); rotations[kind] = index + 1;
            text = choices[index % choices.Length]; dialog.content = text;
            dialogueScroll = Vector2.zero;
            writer.BeginDialog("NMC-Shop/" + ++serial,
                new List<DecipherSegment> { new DecipherSegment { text = dialog.content, color = new Color(.76f, .9f, .95f) } });
            nextChat = Time.realtimeSinceStartup + 22f;
        }
        public void Draw(Rect rect)
        {
            if (writer.IsComplete && Time.realtimeSinceStartup >= nextChat) Say("idle");
            writer.Tick();
            var oldFont = Text.Font; var oldAnchor = Text.Anchor; var oldWrap = Text.WordWrap; var oldColor = GUI.color;
            try
            {
                Theme.Panel(rect);
                float portraitSize = Mathf.Min(60, rect.height - 16);
                var portrait = new Rect(rect.x + 8, rect.y + 8, portraitSize, portraitSize);
                Widgets.DrawBoxSolid(portrait, Theme.Ink); Theme.Frame(portrait, Theme.Gold);
                if (projection != null && Event.current.type == EventType.Repaint)
                    Graphics.DrawTexture(portrait.ContractedBy(3), dialog.PortraitTexture, projection);
                else Widgets.DrawTextureFitted(portrait.ContractedBy(3), dialog.PortraitTexture, 1f);
                Text.Font = GameFont.Small; Text.Anchor = TextAnchor.UpperLeft;
                GUI.color = Theme.Gold;
                float bodyWidth = rect.width - portraitSize - 28;
                bool shortCard = rect.height < 76;
                Text.Anchor = shortCard ? TextAnchor.MiddleLeft : TextAnchor.UpperLeft;
                Widgets.Label(shortCard ? new Rect(portrait.xMax + 9, rect.y + 8, 52, rect.height - 16)
                    : new Rect(portrait.xMax + 9, rect.y + 6, bodyWidth, 22), dialog.speaker);
                Text.Anchor = TextAnchor.UpperLeft;
                var body = shortCard ? new Rect(portrait.xMax + 66, rect.y + 8, bodyWidth - 57, rect.height - 16)
                    : new Rect(portrait.xMax + 9, rect.y + 29, bodyWidth, rect.height - 33);
                // The native tight renderer measures Latin "A" for line height, which overlaps
                // CJK fallback glyphs in an English UI. Keep its reveal timing, but let the
                // normal game label lay out this single-colour conversation text.
                Text.WordWrap = true;
                GUI.color = Theme.Cyan;
                string visible = text.Substring(0, Math.Min(text.Length, writer.RevealedChars));
                float height = Text.CalcHeight(text, body.width);
                if (height > body.height)
                {
                    var content = new Rect(0, 0, body.width - 18, Text.CalcHeight(text, body.width - 18) + 3);
                    Widgets.BeginScrollView(body, ref dialogueScroll, content);
                    try { Widgets.Label(content, visible); }
                    finally { Widgets.EndScrollView(); }
                }
                else Widgets.Label(body, visible);
                GUI.color = oldColor;
                TooltipHandler.TipRegion(rect, NmcText.Get("NMC_Text0330", dialog.speaker, text));
                if (Widgets.ButtonInvisible(portrait)) Say("idle");
                if (Widgets.ButtonInvisible(body)) writer.Complete();
            }
            finally { Text.Font = oldFont; Text.Anchor = oldAnchor; Text.WordWrap = oldWrap; GUI.color = oldColor; }
        }
        public void Dispose() { if (projection != null) UnityEngine.Object.Destroy(projection); projection = null; }
    }
}
