using UnityEngine;
using Verse;

namespace NivarianCommander
{
    // Six reusable draw calls replace sixty thin lines. All animation reads the saved
    // effect lifetime; no particles, random calls or simulation state are generated.
    internal static class BuildingOverloadRibbons
    {
        const int Segments = 36;
        static Mesh broad, narrow;
        static Material violet, pale;

        static void EnsureAssets()
        {
            if (broad != null) return;
            var texture = new Texture2D(64, 16, TextureFormat.RGBA32, false) {
                name = "NMC overload ribbon falloff", wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear, hideFlags = HideFlags.HideAndDontSave };
            var pixels = new Color[64 * 16];
            for (int y = 0; y < 16; y++)
                for (int x = 0; x < 64; x++)
                {
                    float along = Mathf.Pow(Mathf.Max(0f, Mathf.Sin(x / 63f * Mathf.PI)), .7f);
                    float across = Mathf.Pow(Mathf.Max(0f, Mathf.Sin(y / 15f * Mathf.PI)), 1.5f);
                    pixels[y * 64 + x] = new Color(1, 1, 1, Mathf.Max(0, along * across));
                }
            texture.SetPixels(pixels); texture.Apply(false, true);
            violet = new Material(ShaderDatabase.MoteGlow) { mainTexture = texture,
                color = new Color(.9f, .28f, 1f, .85f), hideFlags = HideFlags.HideAndDontSave };
            pale = new Material(ShaderDatabase.MoteGlow) { mainTexture = texture,
                color = new Color(.75f, .85f, 1f, .95f), hideFlags = HideFlags.HideAndDontSave };
            broad = Ribbon(.20f); narrow = Ribbon(.055f);
        }
        static Mesh Ribbon(float width)
        {
            var vertices = new Vector3[(Segments + 1) * 2];
            var uv = new Vector2[vertices.Length];
            var triangles = new int[Segments * 6];
            for (int i = 0; i <= Segments; i++)
            {
                float t = i / (float)Segments, a = t * Mathf.PI * 1.4f;
                float half = width * Mathf.Max(0f, Mathf.Sin(t * Mathf.PI));
                for (int edge = 0; edge < 2; edge++)
                {
                    float radius = 1f + (edge == 0 ? -half : half);
                    vertices[i * 2 + edge] = new Vector3(Mathf.Cos(a) * radius, 0, Mathf.Sin(a) * radius);
                    uv[i * 2 + edge] = new Vector2(t, edge);
                }
                if (i == Segments) continue;
                int v = i * 2, k = i * 6;
                triangles[k] = v; triangles[k + 1] = v + 2; triangles[k + 2] = v + 1;
                triangles[k + 3] = v + 1; triangles[k + 4] = v + 2; triangles[k + 5] = v + 3;
            }
            var mesh = new Mesh { name = "NMC tapered overload ribbon", vertices = vertices, uv = uv,
                triangles = triangles, hideFlags = HideFlags.HideAndDontSave };
            mesh.RecalculateBounds(); mesh.RecalculateNormals();
            return mesh;
        }
        internal static void Draw(Thing target, int remaining)
        {
            EnsureAssets();
            Vector3 center = target.DrawPos;
            center.y = AltitudeLayer.MetaOverlays.AltitudeFor();
            center.z += Mathf.Min(1.3f, target.def.graphicData.drawSize.y * .25f);
            float radius = Mathf.Clamp(target.def.graphicData.drawSize.x * .40f, .6f, 1.65f);
            float phase = (2700 - remaining) * 2f;
            for (int ring = 0; ring < 3; ring++)
            {
                var at = center + new Vector3(0, ring * .001f, (ring - 1) * radius * .18f);
                float spin = phase * (ring == 1 ? -1.15f : .85f) + ring * 120;
                // Flatten after rotation, preserving an elliptical orbit at every phase.
                var matrix = Matrix4x4.TRS(at, Quaternion.AngleAxis((ring - 1) * 12f, Vector3.up),
                    new Vector3(radius, 1f, radius * (.42f + ring * .055f)))
                    * Matrix4x4.Rotate(Quaternion.AngleAxis(spin, Vector3.up));
                Graphics.DrawMesh(broad, matrix, violet, 0);
                matrix.m13 += .0002f;
                Graphics.DrawMesh(narrow, matrix, pale, 0);
            }
        }
    }
}
