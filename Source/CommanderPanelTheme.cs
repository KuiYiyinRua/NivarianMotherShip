using System;
using UnityEngine;
using Verse;

namespace NivarianCommander
{
    // Presentation only. Reuse the required race mod's textures through its content paths.
    internal static class CommanderPanelTheme
    {
        internal static readonly Color Cyan = new Color(.48f, .79f, .88f);
        internal static readonly Color Gold = new Color(.88f, .78f, .49f);
        internal static readonly Color Ink = new Color(.025f, .048f, .072f, .99f);
        internal static readonly Color Surface = new Color(.04f, .08f, .115f, .93f);
        internal static readonly Color Muted = new Color(.57f, .68f, .75f);
        internal static readonly Color White = new Color(.86f, .92f, .95f);
        internal static readonly Color Line = new Color(.28f, .51f, .62f, .42f);
        static Texture2D button, blueprint;

        internal static void Frame(Rect r, Color color)
        {
            Widgets.DrawBoxSolid(new Rect(r.x, r.y, r.width, 1), color);
            Widgets.DrawBoxSolid(new Rect(r.x, r.yMax - 1, r.width, 1), color);
            Widgets.DrawBoxSolid(new Rect(r.x, r.y, 1, r.height), color);
            Widgets.DrawBoxSolid(new Rect(r.xMax - 1, r.y, 1, r.height), color);
        }

        internal static void Panel(Rect r)
        {
            Widgets.DrawBoxSolid(r, Surface);
            Frame(r, Line);
            Widgets.DrawBoxSolid(new Rect(r.x, r.y, 24, 1), Cyan);
            Widgets.DrawBoxSolid(new Rect(r.xMax - 24, r.yMax - 1, 24, 1), Cyan);
        }

        internal static void Polygon(Vector2 center, float radius, int sides, Color color, float angle = -90f)
        {
            for (int i = 0; i < sides; i++)
            {
                float a = (angle + i * 360f / sides) * Mathf.Deg2Rad;
                float b = (angle + (i + 1) * 360f / sides) * Mathf.Deg2Rad;
                Widgets.DrawLine(center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius,
                    center + new Vector2(Mathf.Cos(b), Mathf.Sin(b)) * radius, color, 1f);
            }
        }

        internal static void Backdrop(Rect r)
        {
            Widgets.DrawBoxSolid(r, Ink);
            var old = GUI.color;
            if (blueprint == null) blueprint = ContentFinder<Texture2D>.Get("Nivarian/UI/NMSBackground", false);
            if (blueprint != null)
            {
                GUI.color = new Color(.42f, .72f, .86f, .20f);
                // Sample only the ship schematic, excluding the caption baked into the image.
                GUI.DrawTextureWithTexCoords(new Rect(r.x + r.width * .17f, r.y + r.height * .30f,
                    r.width * .66f, r.width * .23f), blueprint, new Rect(.18f, .35f, .65f, .33f));
            }
            GUI.color = old;
            // Stable geometric marks do not consume either game's random stream.
            for (int i = 0; i < 32; i++)
            {
                float x = r.x + 8 + ((i * 173 + 31) % 997) / 997f * (r.width - 16);
                float y = r.y + 8 + ((i * 97 + 79) % 571) / 571f * (r.height - 16);
                Widgets.DrawBoxSolid(new Rect(x, y, i % 5 == 0 ? 2 : 1, i % 5 == 0 ? 2 : 1), Line);
            }
            Frame(r, Line);
            Widgets.DrawBoxSolid(new Rect(r.x, r.y, 72, 2), Gold);
            Widgets.DrawBoxSolid(new Rect(r.xMax - 72, r.yMax - 2, 72, 2), Cyan);
        }

        internal static void Insignia(Rect r)
        {
            var c = r.center;
            float radius = Mathf.Min(r.width, r.height) * .47f;
            Polygon(c, radius, 6, Gold);
            Polygon(c, radius - 5, 6, Line);
            Polygon(c, radius * .5f, 4, Cyan);
            Polygon(c, radius * .25f, 4, Cyan);
        }

        internal static void WindowContents(Rect r, Action<Rect> header, Action<Rect> content)
        {
            var font = Text.Font; var anchor = Text.Anchor; var color = GUI.color;
            bool wrap = Text.WordWrap, enabled = GUI.enabled;
            try
            {
                Text.Font = GameFont.Small; Text.Anchor = TextAnchor.UpperLeft; Text.WordWrap = true;
                GUI.color = Color.white;
                Backdrop(r);
                header(new Rect(r.x + 16, r.y + 14, r.width - 32, 62));
                Widgets.DrawBoxSolid(new Rect(r.x + 16, r.y + 82, r.width - 32, 1), Line);
                content(new Rect(r.x + 16, r.y + 94, r.width - 32, r.height - 110));
                GUI.DragWindow(new Rect(r.x, r.y, r.width, 70));
            }
            finally { Text.Font = font; Text.Anchor = anchor; GUI.color = color; Text.WordWrap = wrap; GUI.enabled = enabled; }
        }

        internal static string TextField(Rect r, string value)
        {
            Color old = GUI.backgroundColor;
            try { GUI.backgroundColor = new Color(.32f, .49f, .62f); return Widgets.TextField(r, value); }
            finally { GUI.backgroundColor = old; Frame(r, Line); }
        }

        internal static void Quantity(Rect r, ref int value, ref string buffer, int maximum)
        {
            Color old = GUI.backgroundColor;
            try { GUI.backgroundColor = new Color(.32f, .49f, .62f); Widgets.TextFieldNumeric(r, ref value, ref buffer, 0, maximum); }
            finally { GUI.backgroundColor = old; Frame(r, Line); }
        }

        // Nine slices keep the original corner bevels crisp at both button sizes.
        static void ButtonMaterial(Rect r, Color tint)
        {
            if (button == null) button = ContentFinder<Texture2D>.Get("Nivarian/UI/btn", false);
            if (button == null) { Widgets.DrawBoxSolid(r, Surface); Frame(r, Line); return; }
            var old = GUI.color;
            GUI.color = tint;
            const float uvEdge = .16f;
            float edge = Mathf.Min(8f, r.height / 3f);
            for (int row = 0; row < 3; row++)
                for (int col = 0; col < 3; col++)
                {
                    float x = col == 0 ? r.x : col == 1 ? r.x + edge : r.xMax - edge;
                    float y = row == 0 ? r.y : row == 1 ? r.y + edge : r.yMax - edge;
                    float w = col == 1 ? r.width - edge * 2 : edge;
                    float h = row == 1 ? r.height - edge * 2 : edge;
                    float u = col == 0 ? 0 : col == 1 ? uvEdge : 1 - uvEdge;
                    float v = row == 0 ? 1 - uvEdge : row == 1 ? uvEdge : 0;
                    GUI.DrawTextureWithTexCoords(new Rect(x, y, w, h), button,
                        new Rect(u, v, col == 1 ? 1 - uvEdge * 2 : uvEdge, row == 1 ? 1 - uvEdge * 2 : uvEdge));
                }
            GUI.color = old;
        }

        internal static bool Button(Rect r, string label, bool quiet = false, bool selected = false)
        {
            var oldColor = GUI.color; var oldFont = Text.Font; var oldAnchor = Text.Anchor;
            bool hover = GUI.enabled && Mouse.IsOver(r);
            try
            {
                ButtonMaterial(r, !GUI.enabled ? new Color(.28f, .37f, .44f, .65f)
                    : selected || hover ? new Color(.7f, .88f, .97f, 1f)
                    : quiet ? new Color(.37f, .55f, .67f, .8f) : new Color(.5f, .7f, .81f));
                if (selected || (!quiet && GUI.enabled))
                    Widgets.DrawBoxSolid(new Rect(r.x + 8, r.yMax - 2, r.width - 16, 1), selected ? Gold : Cyan);
                Text.Anchor = TextAnchor.MiddleCenter; Text.Font = GameFont.Small;
                if (Text.CalcSize(label).x > r.width - 16) Text.Font = GameFont.Tiny;
                GUI.color = !GUI.enabled ? Muted : selected ? Gold : White;
                Widgets.Label(r.ContractedBy(5f, 1f), label);
                TooltipHandler.TipRegion(r, label);
                return GUI.enabled && Widgets.ButtonInvisible(r);
            }
            finally { GUI.color = oldColor; Text.Font = oldFont; Text.Anchor = oldAnchor; }
        }
    }
}
