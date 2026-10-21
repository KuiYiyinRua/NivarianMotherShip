using System;
using System.Collections.Generic;
using RimWorld.Planet;
using UnityEngine;
using UnityEngine.Rendering;
using Verse;

namespace NivarianCommander
{
    // Reference 6.6-8.0s: a brief descending lance, blue-white impact, expanding
    // ragged wave, airborne shards and a cooling scar. Geometry uses the native
    // Nivarian ice palette and broad crystal facets, without simulation RNG.
    internal sealed class OrbitalStrikeRenderer : IDisposable
    {
        const float Tau = Mathf.PI * 2f;
        const int MaxVisiblePulses = 64;
        const float BeamScale = 2f;
        static readonly Color Blue = new Color(.12f, .44f, 1f);
        static readonly Color Cyan = new Color(.24f, .84f, 1f);
        static readonly Color Ice = new Color(.82f, .98f, 1f);
        static readonly Color Violet = new Color(.39f, .43f, .90f);
        static readonly Vector3 Axis = new Vector3(-.06f, 0f, 1f);
        static readonly Vector3 Side = new Vector3(1f, 0f, .06f).normalized;
        static readonly float[] Profile = { -1f, -.68f, -.34f, -.13f, 0f, .13f, .34f, .68f, 1f };
        static readonly Color[] BeamColors =
        {
            A(Blue,0), A(Blue,.16f), A(Cyan,.65f), A(Ice,.96f), Color.white,
            A(Ice,.96f), A(Cyan,.65f), A(Blue,.16f), A(Blue,0)
        };
        readonly Map map;
        readonly Batch ground = new Batch("orbital ground");
        readonly Batch haze = new Batch("orbital dust");
        readonly Batch light = new Batch("orbital light");
        Material groundMaterial, hazeMaterial, lightMaterial;
        OrbitalVisualCleanup cleanup;
        bool unavailable;
        internal bool Disposed { get; private set; }
        internal bool Viewed => CanView(map);
        internal static bool CanView(Map map) => Current.ProgramState == ProgramState.Playing
            && !LongEventHandler.ShouldWaitForEvent && Find.CurrentMap == map && WorldRendererUtility.DrawingMap;
        internal OrbitalStrikeRenderer(Map map) { this.map = map; }
        static Color A(Color color, float alpha) { color.a = alpha; return color; }
        static float Ramp(float value, float start, float end) => Mathf.Clamp01((value - start) / (end - start));
        static float Noise(int seed)
        {
            uint n = unchecked((uint)seed);
            n ^= n >> 16; n = unchecked(n * 0x7feb352du);
            n ^= n >> 15; n = unchecked(n * 0x846ca68bu); n ^= n >> 16;
            return (n & 65535u) / 65535f;
        }
        static Vector3 Polar(float radius, float angle)
            => new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
        bool EnsureMaterials()
        {
            if (unavailable || Disposed) return false;
            if (lightMaterial != null) return true;
            var shader = NmcVisualShaders.Colored();
            if (shader == null || !shader.isSupported)
            {
                unavailable = true;
                Log.ErrorOnce("[NMC] Orbital strike visual shader is unavailable.", 19409121);
                return false;
            }
            groundMaterial = Material(shader, BlendMode.OneMinusSrcAlpha, 3100);
            hazeMaterial = Material(shader, BlendMode.OneMinusSrcAlpha, 3105);
            lightMaterial = Material(shader, BlendMode.One, 3110);
            return true;
        }
        static Material Material(Shader shader, BlendMode destination, int queue)
        {
            var result = new Material(NmcVisualShaders.ForBlend(shader, destination == BlendMode.One)) { hideFlags = HideFlags.HideAndDontSave, renderQueue = queue };
            result.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            result.SetInt("_DstBlend", (int)destination);
            result.SetInt("_Cull", (int)CullMode.Off);
            result.SetInt("_ZWrite", 0);
            result.SetInt("_ZTest", (int)CompareFunction.LessEqual);
            return result;
        }
        internal void Draw(IReadOnlyList<OrbitalPulse> pulses)
        {
            if (!Viewed || Disposed || !EnsureMaterials()) return;
            if (cleanup == null && Find.Camera != null)
            {
                cleanup = Find.Camera.GetComponent<OrbitalVisualCleanup>() ?? Find.Camera.gameObject.AddComponent<OrbitalVisualCleanup>();
                cleanup.Bind(this);
            }
            ground.Clear(); haze.Clear(); light.Clear();
            var view = Find.CameraDriver.CurrentViewRect.ExpandedBy(8);
            // Bound presentation work even if a player rapidly queues many strikes.
            for (int i = Math.Max(0, pulses.Count - MaxVisiblePulses); i < pulses.Count; i++)
            {
                var p = pulses[i];
                if (!p.center.InBounds(map) || p.center.Fogged(map) || !view.Contains(p.center)) continue;
                float age = Mathf.Max(0, p.duration - p.remaining) / 60f;
                int seed = unchecked(p.center.x * 73856093 ^ p.center.z * 19349663);
                DrawPulse(p.center.ToVector3Shifted(), age, seed, view.maxZ);
            }
            ground.Draw(groundMaterial); haze.Draw(hazeMaterial); light.Draw(lightMaterial);
        }
        void DrawPulse(Vector3 at, float age, int seed, float viewTop)
        {
            DrawResidue(at, age, seed);
            at.y = AltitudeLayer.MoteOverhead.AltitudeFor();
            float shaft = (1f - Ramp(age, .10f, .25f));
            if (shaft > 0f) DrawShaft(at, age, shaft, viewTop);

            float flash = (1f - Ramp(age, .10f, .43f));
            if (flash > 0f)
            {
                float radius = Mathf.Lerp(.42f, 1.70f, Ramp(age, 0f, .16f));
                Vector3 crown = at + Vector3.forward * (.15f + Ramp(age, 0f, .43f) * 1.05f);
                light.Glow(at, radius * 1.9f, radius * 1.9f, A(Blue, flash * .27f));
                light.Burst(crown, radius * 1.10f, seed, A(Blue, flash * .64f), A(Cyan, flash * .90f), 1.22f);
                light.Burst(crown, radius * .85f, seed + 17, A(Cyan, flash), A(Ice, flash), 1.28f);
                light.Glow(crown, radius * .86f, radius * 1.12f, A(Color.white, flash));
                // Sharp but short glints; no persistent targeting-circle graphic.
                light.Stroke(at - Vector3.right * radius * 1.8f, at + Vector3.right * radius * 1.8f,
                    .045f, A(Ice, flash * .58f));
            }

            float wave = Ramp(age, .045f, .13f) * (1f - Ramp(age, .30f, .83f));
            if (wave > 0f)
            {
                float radius = Mathf.Lerp(.5f, 3.65f, Mathf.Sqrt(Ramp(age, .04f, .83f)));
                light.Wave(at, radius, .20f, age, seed, A(Blue, .24f * wave), A(Cyan, .75f * wave));
                light.Wave(at, radius * .87f, .08f, age, seed + 61, A(Violet, .12f * wave), A(Ice, .30f * wave));
                for (int i = 0; i < 20; i++)
                {
                    float angle = i * Tau / 20f;
                    Vector3 p = at + Polar(radius, angle);
                    Vector3 q = at + Polar(radius + .12f + Noise(seed + i * 19) * .28f, angle + .035f);
                    light.Stroke(p, q, .025f, A(Ice, wave * .6f));
                }
            }
            DrawEjecta(at, age, seed);
        }
        void DrawShaft(Vector3 at, float age, float strength, float viewTop)
        {
            // Extend beyond the camera, so zooming out never exposes a flat cap.
            float height = Mathf.Max(28f, viewTop - at.z + 12f) * BeamScale;
            float halfWidth = Mathf.Lerp(.16f, .49f, Ramp(age, 0f, .07f)) * strength * BeamScale;
            int first = light.Count;
            const int steps = 20;
            for (int row = 0; row <= steps; row++)
            {
                float t = row / (float)steps;
                Vector3 center = at + Axis * (height * t);
                float width = halfWidth * Mathf.Lerp(1.2f, .65f, t);
                for (int column = 0; column < Profile.Length; column++)
                {
                    Color color = BeamColors[column]; color.a *= strength;
                    light.Vertex(center + Side * (Profile[column] * width), color);
                }
            }
            light.Grid(first, steps + 1, Profile.Length);
            for (int i = 0; i < 3; i++)
            {
                float t = Mathf.Repeat(age * 7f + i * .31f, 1f);
                Vector3 from = at + Axis * (height * (1f - t)) + Side * ((i - 1) * .25f * BeamScale);
                light.Stroke(from, from - Axis * (3f * BeamScale), .022f * BeamScale, A(Ice, strength * .5f));
            }
        }
        void DrawEjecta(Vector3 at, float age, int seed)
        {
            if (age > 1.3f) return;
            for (int i = 0; i < 32; i++)
            {
                float born = .035f + Noise(seed + i * 173) * .08f;
                float time = age - born;
                float life = .38f + Noise(seed + i * 281) * .7f;
                if (time < 0f || time >= life) continue;
                float t = time / life;
                float angle = i * 2.39996f + Noise(seed) * Tau;
                float distance = .8f + Noise(seed + i * 83) * 3.1f;
                Vector3 direction = Polar(1f, angle);
                float lift = Mathf.Sin(t * Mathf.PI) * (.45f + Noise(seed + i * 137) * 1.8f);
                Vector3 p = at + direction * (distance * Mathf.Sqrt(t)) + Vector3.forward * lift;
                float fade = (1f - t) * Ramp(time, 0f, .025f);
                if (i % 3 == 0)
                {
                    float size = (.045f + Noise(seed + i) * .045f) * fade;
                    haze.Shard(p, direction, size * 1.4f, A(new Color(.065f,.10f,.16f), fade * .85f));
                    light.Shard(p + new Vector3(.018f,.002f,.025f), direction, size, A(Cyan, fade * .75f));
                }
                else
                {
                    Vector3 tail = p - direction * (.12f + .35f * (1f - t)) - Vector3.forward * .12f;
                    light.Stroke(tail, p, .022f * fade, A(i % 2 == 0 ? Ice : Cyan, fade * .9f));
                }
            }
            float arcs = Ramp(age, .08f, .16f) * (1f - Ramp(age, .28f, .72f));
            if (arcs <= 0f) return;
            for (int arc = 0; arc < 7; arc++)
            {
                float theta = arc * Tau / 7f + Noise(seed + arc) * .4f;
                Vector3 previous = at + Polar(.35f, theta);
                for (int step = 1; step <= 6; step++)
                {
                    float phase = step / 6f;
                    float offset = (Noise(seed + arc * 97 + step * 53 + (int)(age * 20f)) - .5f) * .45f;
                    Vector3 next = at + Polar(.35f + phase * 1.7f, theta + offset) + Vector3.forward * (phase * .8f);
                    light.Stroke(previous, next, .04f * (1f - phase * .7f), A(arc % 2 == 0 ? Cyan : Violet, arcs * .7f));
                    previous = next;
                }
            }
        }
        void DrawResidue(Vector3 at, float age, int seed)
        {
            float fade = 1f - Ramp(age, 1.5f, OrbitalPulse.Lifetime / 60f);
            if (fade <= 0f) return;
            at.y = AltitudeLayer.FloorEmplacement.AltitudeFor() + .015f;
            float reveal = Ramp(age, .08f, .35f) * fade;
            ground.Glow(at, 2.2f, 2.2f, new Color(.045f,.065f,.10f,.44f * reveal));
            ground.Burst(at, 1.05f, seed + 31, new Color(.045f,.065f,.09f,.24f * reveal), new Color(.02f,.03f,.055f,.36f * reveal));
            float heat = (1f - Ramp(age, .32f, 1.65f)) * Ramp(age, .08f, .2f);
            for (int crack = 0; crack < 13; crack++)
            {
                float angle = crack * Tau / 13f + Noise(seed + crack * 347) * .23f;
                Vector3 previous = at + Polar(.30f, angle);
                for (int segment = 1; segment <= 4; segment++)
                {
                    float radius = segment * (.37f + Noise(seed + crack * 109) * .18f);
                    float offset = (Noise(seed + crack * 71 + segment * 13) - .5f) * .22f;
                    Vector3 next = at + Polar(radius, angle + offset);
                    ground.Stroke(previous, next, .095f * (1f - segment * .14f), new Color(.045f,.065f,.085f, reveal * .75f));
                    if (heat > 0f) light.Stroke(previous + Vector3.up * .02f, next + Vector3.up * .02f,
                        .035f * (1f - segment * .14f), A(Cyan, heat * .58f));
                    if (segment == 3)
                        ground.Stroke(next, next + Polar(.35f, angle + .6f), .032f, new Color(.06f,.08f,.11f,reveal * .55f));
                    previous = next;
                }
            }
            at.y = AltitudeLayer.MoteOverhead.AltitudeFor() - .01f;
            for (int puff = 0; puff < 11; puff++)
            {
                float born = .12f + Noise(seed + puff * 197) * .15f;
                float time = age - born;
                if (time <= 0f) continue;
                float life = 1.5f + Noise(seed + puff * 239) * .8f;
                float opacity = Ramp(time, 0f, .22f) * (1f - Ramp(time, .4f, life));
                if (opacity <= 0f) continue;
                float angle = puff * 2.39996f;
                Vector3 p = at + Polar(.45f + time * .55f, angle) + Vector3.forward * time * .85f;
                float radius = .25f + time * .38f;
                haze.Glow(p, radius, radius * .85f, new Color(.12f,.17f,.24f,opacity * .26f));
                haze.Glow(p + Vector3.forward * radius * .2f, radius * .64f, radius * .55f,
                    new Color(.40f,.52f,.64f,opacity * .11f));
                if (heat > 0f) light.Glow(p, radius * .55f, radius * .65f, A(Violet, heat * opacity * .12f));
            }
        }
        public void Dispose()
        {
            if (Disposed) return;
            Disposed = true;
            if (cleanup != null) cleanup.Unbind(this);
            cleanup = null;
            ground.Dispose(); haze.Dispose(); light.Dispose();
            if (groundMaterial != null) UnityEngine.Object.Destroy(groundMaterial);
            if (hazeMaterial != null) UnityEngine.Object.Destroy(hazeMaterial);
            if (lightMaterial != null) UnityEngine.Object.Destroy(lightMaterial);
            groundMaterial = hazeMaterial = lightMaterial = null;
        }
        sealed class Batch : IDisposable
        {
            readonly string name;
            readonly List<Vector3> vertices = new List<Vector3>(8192);
            readonly List<Color> colors = new List<Color>(8192);
            readonly List<int> triangles = new List<int>(24576);
            Mesh mesh;
            internal Batch(string name) { this.name = name; }
            internal int Count => vertices.Count;
            internal void Clear() { vertices.Clear(); colors.Clear(); triangles.Clear(); }
            internal void Vertex(Vector3 p, Color color) { vertices.Add(p); colors.Add(color); }
            void Triangle(int a, int b, int c) { triangles.Add(a); triangles.Add(b); triangles.Add(c); }
            internal void Grid(int first, int rows, int columns)
            {
                for (int row = 0; row < rows - 1; row++)
                    for (int col = 0; col < columns - 1; col++)
                    {
                        int a = first + row * columns + col;
                        Triangle(a, a + columns, a + columns + 1); Triangle(a, a + columns + 1, a + 1);
                    }
            }
            internal void Stroke(Vector3 from, Vector3 to, float width, Color color)
            {
                if (color.a <= 0f || width <= 0f) return;
                Vector3 delta = to - from;
                float length = Mathf.Sqrt(delta.x * delta.x + delta.z * delta.z);
                if (length < .0001f) return;
                Vector3 side = new Vector3(-delta.z, 0f, delta.x) * (width / (2f * length));
                int first = Count;
                Vertex(from - side, color); Vertex(from + side, color); Vertex(to - side, color); Vertex(to + side, color);
                Grid(first, 2, 2);
            }
            internal void Shard(Vector3 at, Vector3 direction, float size, Color color)
            {
                Vector3 side = new Vector3(-direction.z, 0f, direction.x);
                int first = Count;
                Vertex(at + direction * size * 2.2f, color); Vertex(at + side * size, color);
                Vertex(at - direction * size, color); Vertex(at - side * size, color);
                Triangle(first, first + 1, first + 2); Triangle(first, first + 2, first + 3);
            }
            internal void Glow(Vector3 at, float rx, float rz, Color color)
            {
                if (color.a <= 0f) return;
                const int sides = 32;
                int first = Count; Vertex(at, color);
                for (int ring = 0; ring < 3; ring++)
                {
                    float scale = ring == 0 ? .22f : ring == 1 ? .58f : 1f;
                    Color edge = A(color, color.a * (ring == 0 ? .9f : ring == 1 ? .32f : 0f));
                    for (int i = 0; i < sides; i++)
                    {
                        float a = i * Tau / sides;
                        Vertex(at + new Vector3(Mathf.Cos(a) * rx * scale, 0f, Mathf.Sin(a) * rz * scale), edge);
                    }
                }
                for (int i = 0; i < sides; i++)
                {
                    int n = (i + 1) % sides;
                    Triangle(first, first + 1 + i, first + 1 + n);
                    for (int r = 0; r < 2; r++)
                    {
                        int inner = first + 1 + r * sides, outer = inner + sides;
                        Triangle(inner + i, outer + i, outer + n); Triangle(inner + i, outer + n, inner + n);
                    }
                }
            }
            internal void Burst(Vector3 at, float radius, int seed, Color edge, Color fill, float verticalScale = 1f)
            {
                if (fill.a <= 0f) return;
                const int sides = 48;
                int first = Count; Vertex(at, fill);
                for (int i = 0; i <= sides; i++)
                {
                    int j = i % sides;
                    float r = radius * (.91f + Noise(seed + j * 71) * .09f + (j % 8 == 0 ? .08f : 0f));
                    Vector3 offset = Polar(r, j * Tau / sides); offset.z *= verticalScale;
                    Vertex(at + offset, edge);
                    if (i > 0) Triangle(first, first + i, first + i + 1);
                }
            }
            internal void Wave(Vector3 at, float radius, float width, float age, int seed, Color skirt, Color crest)
            {
                const int sides = 96;
                int first = Count;
                for (int i = 0; i <= sides; i++)
                {
                    int j = i % sides;
                    float theta = j * Tau / sides;
                    float wobble = 1f + .018f * Mathf.Sin(theta * 11f + age * 12f) + .02f * (Noise(seed + j * 29) - .5f);
                    Color c = crest; c.a *= .65f + .35f * Noise(seed + j * 43);
                    Vertex(at + Polar((radius - width * 2f) * wobble, theta), A(skirt, 0));
                    Vertex(at + Polar((radius - width * .35f) * wobble, theta), skirt);
                    Vertex(at + Polar(radius * wobble, theta), c);
                    Vertex(at + Polar((radius + width * .4f) * wobble, theta), A(crest, 0));
                }
                Grid(first, sides + 1, 4);
            }
            internal void Draw(Material material)
            {
                if (vertices.Count == 0) return;
                if (mesh == null)
                {
                    mesh = new Mesh { name = "NMC " + name, hideFlags = HideFlags.HideAndDontSave, indexFormat = IndexFormat.UInt32 };
                    mesh.MarkDynamic();
                }
                mesh.Clear(); mesh.SetVertices(vertices); mesh.SetColors(colors); mesh.SetTriangles(triangles, 0, false);
                mesh.RecalculateBounds();
                Graphics.DrawMesh(mesh, Matrix4x4.identity, material, 0, Find.Camera, 0, null, ShadowCastingMode.Off, false);
            }
            public void Dispose() { if (mesh != null) UnityEngine.Object.Destroy(mesh); mesh = null; Clear(); }
        }
    }
    [DisallowMultipleComponent]
    public sealed class OrbitalVisualCleanup : MonoBehaviour
    {
        OrbitalStrikeRenderer owner;
        internal void Bind(OrbitalStrikeRenderer next)
        {
            if (owner == next) return;
            var previous = owner; owner = next; previous?.Dispose();
        }
        internal void Unbind(OrbitalStrikeRenderer previous) { if (owner == previous) owner = null; }
        void Release() { var previous = owner; owner = null; previous?.Dispose(); }
        void LateUpdate() { if (owner != null && !owner.Viewed) Release(); }
        void OnDisable() { Release(); }
        void OnDestroy() { Release(); }
    }
}
