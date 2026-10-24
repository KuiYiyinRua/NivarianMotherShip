using RimWorld;
using UnityEngine;
using Verse;

namespace NivarianCommander
{
    internal static class PointShopIcon
    {
        internal static void ThingIcon(Rect rect, Thing thing)
        {
#if RIMWORLD_1_7
            if (Event.current.type != EventType.Repaint || thing == null) return;
            thing = thing.GetInnerIfMinified();
            // Resolve native textures, styles and portraits, but never send their
            // optional material into the 1.7 ImGui custom-pipeline path (issue #1).
            Texture texture = Widgets.GetIconFor(thing, rect.size, null, false,
                out float scale, out float angle, out _, out Color color, out Material material);
            if (texture == null || texture == BaseContent.BadTex || texture.width <= 0 || texture.height <= 0) return;
            var def = thing.def;
            if (thing.StyleDef?.UIIcon != null || !def.uiIconPath.NullOrEmpty())
                rect.position += new Vector2(def.uiIconOffset.x * rect.width, def.uiIconOffset.y * rect.height);
            Vector2 proportions = new Vector2(texture.width, texture.height);
            Rect texCoords = new Rect(0, 0, 1, 1);
            if (def.graphicData != null)
            {
                proportions = def.defaultPlacingRot.IsHorizontal ? def.graphicData.drawSize.Rotated() : def.graphicData.drawSize;
                if (def.uiIconPath.NullOrEmpty() && def.graphicData.linkFlags != LinkFlags.None)
                    texCoords = new Rect(0, .5f, .25f, .25f);
            }
            Color previous = GUI.color;
            try
            {
                // The native resolver returns white when a material owns the tint.
                // Keep the item's base colour without executing its mask/effect shader.
                GUI.color = material != null ? thing.DrawColor : color;
                Widgets.DrawTextureFitted(rect, texture, scale, proportions, texCoords, angle, null);
            }
            finally { GUI.color = previous; }
#else
            Widgets.ThingIcon(rect, thing);
#endif
        }
    }
}
