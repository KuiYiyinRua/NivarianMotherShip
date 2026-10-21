using System;
using System.Collections.Generic;
using RimWorld.Planet;
using UnityEngine;
using UnityEngine.Rendering;
using Verse;

namespace NivarianCommander
{
    // Reference F34-80: gathering wisps, brief upright flash, then a branching
    // electrical pool. Later footage contains several separate, overlapping casts.
    // Clean blue/cyan silhouettes and white cores match the native Nivarian VFX.
    // All state below is cosmetic; the saved cloud's local elapsed time drives it.
    internal sealed class TebrusStormVisuals : IDisposable
    {
        const int EndingTicks = 18;
        const int MaxEndings = 16;
        const float Tau = Mathf.PI * 2f;
        static readonly Color Cyan = new Color(.23f, .79f, 1f);
        static readonly Color Violet = new Color(.40f, .41f, .97f);
        static readonly Color Core = new Color(.87f, .98f, 1f);
        readonly Map map;
        readonly List<Ending> endings = new List<Ending>(MaxEndings);
        readonly Batch mist = new Batch("storm mist");
        readonly Batch glow = new Batch("storm glow");
        readonly Batch arcs = new Batch("storm branches");
        Material mistMaterial, lightMaterial;
        TebrusStormVisualCleanup cleanup;
        bool unavailable;
        internal bool Disposed { get; private set; }
        internal bool HasResidue => endings.Count != 0;
        internal bool Viewed => CanView(map);
        internal static bool CanView(Map map) => Current.ProgramState == ProgramState.Playing
            && !LongEventHandler.ShouldWaitForEvent && Find.CurrentMap == map && WorldRendererUtility.DrawingMap;

        sealed class Ending
        {
            internal Pawn caster;
            internal IntVec3 center;
            internal int seed, age;
        }

        internal TebrusStormVisuals(Map map) { this.map = map; }
        static int Seed(TebrusStormCloud cloud) => unchecked(cloud.center.x * 73856093
            ^ cloud.center.z * 19349663 ^ (cloud.caster?.thingIDNumber ?? 0) * 83492791);
        static float Noise(int seed)
        {
            uint n = unchecked((uint)seed);
            n ^= n >> 16; n = unchecked(n * 0x7feb352du);
            n ^= n >> 15; n = unchecked(n * 0x846ca68bu);
            n ^= n >> 16;
            return (n & 0xffffu) / 65535f;
        }
        static Color Alpha(Color color, float alpha) { color.a = alpha; return color; }
        static Vector3 Polar(float radius, float angle)
            => new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
        bool Visible(IntVec3 center)
            => center.InBounds(map) && !center.Fogged(map)
                && Find.CameraDriver.CurrentViewRect.ExpandedBy(16).Contains(center);

        internal void Tick()
        {
            if (Disposed) return;
            if (!Viewed) { Dispose(); return; }
            for (int i = endings.Count - 1; i >= 0; i--)
                if (++endings[i].age >= EndingTicks) endings.RemoveAt(i);
        }
        internal void Finish(TebrusStormCloud cloud)
        {
            if (Disposed || !Viewed || !Visible(cloud.center)) return;
            if (endings.Count == MaxEndings) endings.RemoveAt(0);
            endings.Add(new Ending { center = cloud.center, caster = cloud.caster, seed = Seed(cloud) });
        }
        internal void Cancel(HashSet<Pawn> pawns) => endings.RemoveAll(e => pawns.Contains(e.caster));

        bool EnsureMaterials()
        {
            if (unavailable || Disposed) return false;
            if (mistMaterial != null) return true;
            var shader = NmcVisualShaders.Colored();
            if (shader == null || !shader.isSupported)
            {
                unavailable = true;
                Log.ErrorOnce("[NMC] Psionic storm visual shader is unavailable.", 19409091);
                return false;
            }
            mistMaterial = Material(shader, BlendMode.OneMinusSrcAlpha, 3100);
            lightMaterial = Material(shader, BlendMode.One, 3110);
            return true;
        }
        static Material Material(Shader shader, BlendMode destination, int queue)
        {
            var material = new Material(NmcVisualShaders.ForBlend(shader, destination == BlendMode.One)) { hideFlags = HideFlags.HideAndDontSave, renderQueue = queue };
            material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            material.SetInt("_DstBlend", (int)destination);
            material.SetInt("_Cull", (int)CullMode.Off);
            material.SetInt("_ZWrite", 0);
            material.SetInt("_ZTest", (int)CompareFunction.LessEqual);
            return material;
        }
        internal void Draw(IReadOnlyList<TebrusStormCloud> clouds)
        {
            if (!Viewed) { Dispose(); return; }
            if (!EnsureMaterials()) return;
            if (cleanup == null && Find.Camera != null)
            {
                cleanup = Find.Camera.GetComponent<TebrusStormVisualCleanup>()
                    ?? Find.Camera.gameObject.AddComponent<TebrusStormVisualCleanup>();
                cleanup.Bind(this);
            }
            mist.Clear(); glow.Clear(); arcs.Clear();
            for (int i = 0; i < clouds.Count; i++)
            {
                var cloud = clouds[i];
                if (Visible(cloud.center)) DrawField(cloud.center, cloud.elapsed, Seed(cloud), 1f);
            }
            for (int i = 0; i < endings.Count; i++)
            {
                var ending = endings[i];
                if (Visible(ending.center)) DrawField(ending.center, TebrusStorm.Duration + ending.age,
                    ending.seed, Mathf.Pow(1f - ending.age / (float)EndingTicks, 1.5f));
            }
            mist.Draw(mistMaterial);
            glow.Draw(lightMaterial);
            arcs.Draw(lightMaterial);
        }

        void DrawField(IntVec3 center, int elapsed, int seed, float fade)
        {
            Vector3 at = center.ToVector3Shifted();
            at.y = AltitudeLayer.MoteOverhead.AltitudeFor();
            float seconds = elapsed / 60f;
            float gather = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((elapsed + 1f) / 22f));
            float strength = gather * fade;
            float initialFlash = Mathf.Clamp01(1f - Mathf.Abs(elapsed - 27f) / 13f);
            float pulse = elapsed >= TebrusStorm.Interval
                ? Mathf.Pow(Mathf.Clamp01(1f - elapsed % TebrusStorm.Interval / 10f), 2f) : 0f;
            float radius = TebrusStorm.Radius;

            // Lobed, translucent cloud rather than an opaque painted disc or a hard ring.
            mist.Glow(at, radius, radius, new Color(.13f, .22f, .49f, .14f * strength));
            glow.Glow(at, radius * .89f, radius * .89f, Alpha(Cyan, (.11f + .09f * pulse) * strength));
            for (int lobe = 0; lobe < 12; lobe++)
            {
                float angle = lobe * Tau / 12f + seconds * (lobe % 2 == 0 ? .19f : -.12f);
                float spread = radius * (.55f + .07f * Mathf.Sin(seconds * 2.1f + lobe * 2.7f));
                Vector3 p = at + Polar(spread, angle);
                float size = 1.55f + .3f * Mathf.Sin(lobe * 3.4f + seconds * 1.6f);
                mist.Glow(p, size, size * .85f, Alpha(Violet, .075f * strength));
                glow.Glow(p, size * 1.05f, size, Alpha(Cyan, .048f * strength));
            }

            // Wide transparent wisps contract at the beginning, with only faint,
            // broken wisps left in the steady state; no persistent targeting decal.
            float entering = Mathf.Clamp01(1f - elapsed / 42f);
            float wispStrength = (.10f + .65f * entering) * fade * Mathf.Clamp01((elapsed + 1f) / 8f);
            for (int ribbon = 0; ribbon < 3; ribbon++)
                Wisp(at, radius * Mathf.Lerp(.95f, .73f, gather),
                    seconds * 2.2f + ribbon * Tau / 3f + Noise(seed) * Tau, wispStrength, ribbon);

            // Independently staggered bolts keep a connected branching pool alive
            // without a synchronized whole-screen strobe or a mutable random stream.
            for (int slot = 0; slot < 18; slot++)
            {
                int period = 14 + slot % 6;
                int offset = (int)(Noise(unchecked(seed + slot * 113)) * period);
                int cycle = (elapsed + offset) / period;
                float age = (elapsed + offset) % period / (float)period;
                int boltSeed = unchecked(seed + slot * 7919 + cycle * 104729);
                float angle = Noise(boltSeed) * Tau;
                float r1 = radius * (.10f + .75f * Mathf.Sqrt(Noise(boltSeed + 1)));
                float r2 = radius * (.25f + .65f * Noise(boltSeed + 2));
                Vector3 from = at + Polar(r1, angle);
                Vector3 to = at + Polar(r2, angle + 1.1f + Noise(boltSeed + 3) * 2.5f);
                float blink = .20f + .80f * Mathf.Pow(Mathf.Sin(age * Mathf.PI), .65f);
                float power = strength * blink * (.61f + .16f * pulse);
                Bolt(from, to, boltSeed, age, .085f, power, slot % 3 == 0);
                if (slot % 3 == 0)
                    glow.Glow(to, .75f, .75f, Alpha(Core, .11f * power));
            }

            // The reference's tall flare belongs to cast onset, not a permanent
            // orbital laser. Its finer forks withdraw, leaving ground electricity.
            float shaft = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((elapsed - 16f) / 10f))
                * Mathf.Clamp01((49f - elapsed) / 18f) * fade;
            if (shaft > 0f)
            {
                for (int fiber = 0; fiber < 5; fiber++)
                {
                    float spread = (fiber - 2) * .24f;
                    Vector3 root = at + new Vector3(spread, .018f, .12f * (fiber % 2));
                    Vector3 top = root + new Vector3(.65f + spread * .7f, 0f, 8.5f - Mathf.Abs(spread) * 2f);
                    RisingFilament(root, top, seconds, fiber, shaft);
                }
                glow.Glow(at, 2.35f, 1.85f, Alpha(Core, .38f * shaft));
            }
            if (initialFlash > 0f)
                glow.Glow(at, 3.25f, 2.9f, Alpha(Core, .25f * initialFlash * fade));

            // Short upward sparks and angular white knots echo the reference's
            // raised electrical filaments without obscuring all units in the area.
            for (int spark = 0; spark < 7; spark++)
            {
                int period = 23 + spark * 2;
                int phase = (elapsed + spark * 9) / period;
                float age = (elapsed + spark * 9) % period / (float)period;
                int sparkSeed = unchecked(seed + spark * 4099 + phase * 65537);
                Vector3 p = at + Polar(radius * (.28f + .50f * Noise(sparkSeed)), Noise(sparkSeed + 1) * Tau);
                float power = Mathf.Sin(age * Mathf.PI) * strength;
                Vector3 tip = p + new Vector3((Noise(sparkSeed + 2) - .5f) * .7f, .02f, .7f + age * 1.2f);
                DirectBolt(p, tip, sparkSeed, age, .042f, .62f * power, Cyan);
                arcs.Stroke(p - new Vector3(.12f, 0, 0), p + new Vector3(.12f, 0, 0), .065f, Alpha(Core, .6f * power));
            }
        }

        void Wisp(Vector3 at, float radius, float angle, float strength, int ribbon)
        {
            const int steps = 38;
            Vector3 previous = at + Polar(radius, angle);
            for (int step = 1; step <= steps; step++)
            {
                float t = step / (float)steps;
                float theta = angle - t * 2.7f;
                float r = radius * (1f - .24f * t);
                Vector3 next = at + Polar(r, theta);
                next.z += Mathf.Sin(t * Mathf.PI) * (.24f + ribbon * .07f);
                float taper = Mathf.Pow(Mathf.Sin(t * Mathf.PI), 1.5f);
                glow.Stroke(previous, next, .40f * taper + .015f, Alpha(Violet, .16f * strength * taper));
                arcs.Stroke(previous, next, .085f * taper + .009f, Alpha(Cyan, .43f * strength * taper));
                previous = next;
            }
        }
        void RisingFilament(Vector3 root, Vector3 top, float seconds, int fiber, float strength)
        {
            const int segments = 24;
            Vector3 previous = root;
            for (int step = 1; step <= segments; step++)
            {
                float t = step / (float)segments;
                Vector3 p = Vector3.Lerp(root, top, t);
                p.x += Mathf.Sin(t * 13f - seconds * 5f + fiber) * .22f * Mathf.Sin(t * Mathf.PI);
                float alpha = strength * (1f - t * t);
                glow.Stroke(previous, p, .43f * (1f - t * .65f), Alpha(Cyan, .33f * alpha));
                arcs.Stroke(previous, p, .075f * (1f - t * .7f), Alpha(Core, .83f * alpha));
                previous = p;
            }
        }
        void Bolt(Vector3 from, Vector3 to, int seed, float age, float width, float strength, bool violet)
        {
            Color color = violet ? Color.Lerp(Cyan, Violet, .62f) : Cyan;
            DirectBolt(from, to, seed, age, width, strength, color);
            Vector3 delta = to - from;
            Vector3 side = new Vector3(-delta.z, 0f, delta.x).normalized;
            for (int branch = 0; branch < 2; branch++)
            {
                float t = branch == 0 ? .36f : .71f;
                Vector3 root = BoltPoint(from, to, seed, age, branch == 0 ? 4 : 8, 11);
                Vector3 tip = Vector3.Lerp(from, to, t + .20f)
                    + side * ((branch == 0 ? 1f : -1f) * (.55f + .70f * Noise(seed + branch + 31)));
                DirectBolt(root, tip, unchecked(seed + branch * 977 + 91), age, width * .60f, strength * .58f, color);
            }
        }
        static Vector3 BoltPoint(Vector3 from, Vector3 to, int seed, float age, int step, int steps)
        {
            float t = step / (float)steps;
            Vector3 delta = to - from;
            Vector3 side = new Vector3(-delta.z, 0f, delta.x).normalized;
            float jitter = (Noise(unchecked(seed + step * 73)) - .5f) * .95f;
            jitter += .055f * Mathf.Sin(step * 2.3f + age * 5f);
            return Vector3.Lerp(from, to, t) + side * jitter * Mathf.Sin(t * Mathf.PI);
        }
        void DirectBolt(Vector3 from, Vector3 to, int seed, float age, float width, float strength, Color color)
        {
            const int steps = 11;
            Vector3 previous = from;
            for (int step = 1; step <= steps; step++)
            {
                Vector3 next = BoltPoint(from, to, seed, age, step, steps);
                float taper = .4f + .6f * Mathf.Sin((step - .5f) / steps * Mathf.PI);
                glow.Stroke(previous, next, width * 4.8f, Alpha(color, .17f * strength * taper));
                arcs.Stroke(previous, next, width, Alpha(color, .72f * strength * taper));
                arcs.Stroke(previous, next, width * .34f, Alpha(Core, .76f * strength * taper));
                previous = next;
            }
        }

        public void Dispose()
        {
            if (Disposed) return;
            Disposed = true;
            if (cleanup != null) cleanup.Unbind(this);
            cleanup = null;
            endings.Clear();
            mist.Dispose(); glow.Dispose(); arcs.Dispose();
            if (mistMaterial != null) UnityEngine.Object.Destroy(mistMaterial);
            if (lightMaterial != null) UnityEngine.Object.Destroy(lightMaterial);
            mistMaterial = lightMaterial = null;
        }

        // Three map-local meshes, reused every draw. Vertex colors keep the outer
        // glow soft while retaining sharply readable inner electrical branches.
        sealed class Batch : IDisposable
        {
            const int Sides = 20;
            static readonly Vector3[] Circle = MakeCircle();
            readonly string name;
            readonly List<Vector3> vertices = new List<Vector3>(16384);
            readonly List<Color> colors = new List<Color>(16384);
            readonly List<int> triangles = new List<int>(49152);
            Mesh mesh;
            static Vector3[] MakeCircle()
            {
                var result = new Vector3[Sides];
                for (int i = 0; i < Sides; i++) result[i] = Polar(1f, i * Tau / Sides);
                return result;
            }
            internal Batch(string name) { this.name = name; }
            internal void Clear() { vertices.Clear(); colors.Clear(); triangles.Clear(); }
            void Vertex(Vector3 p, Color color) { vertices.Add(p); colors.Add(color); }
            void Triangle(int a, int b, int c) { triangles.Add(a); triangles.Add(b); triangles.Add(c); }
            internal void Stroke(Vector3 from, Vector3 to, float width, Color color)
            {
                if (width <= .0001f || color.a <= .0001f) return;
                Vector3 delta = to - from;
                float length = Mathf.Sqrt(delta.x * delta.x + delta.z * delta.z);
                if (length <= .0001f) return;
                Vector3 side = new Vector3(-delta.z, 0f, delta.x) * (width * .5f / length);
                int first = vertices.Count;
                Color clear = Alpha(color, 0f);
                Vertex(from - side, clear); Vertex(from - side * .20f, color);
                Vertex(from + side * .20f, color); Vertex(from + side, clear);
                Vertex(to - side, clear); Vertex(to - side * .20f, color);
                Vertex(to + side * .20f, color); Vertex(to + side, clear);
                for (int c = 0; c < 3; c++)
                {
                    int a = first + c;
                    Triangle(a, a + 4, a + 5); Triangle(a, a + 5, a + 1);
                }
            }
            internal void Glow(Vector3 at, float rx, float rz, Color color)
            {
                if (color.a <= .0001f) return;
                int first = vertices.Count;
                Vertex(at, color);
                for (int ring = 0; ring < 3; ring++)
                {
                    float scale = ring == 0 ? .27f : ring == 1 ? .62f : 1f;
                    Color c = Alpha(color, color.a * (ring == 0 ? .78f : ring == 1 ? .23f : 0f));
                    for (int i = 0; i < Sides; i++)
                        Vertex(at + new Vector3(Circle[i].x * rx * scale, 0f, Circle[i].z * rz * scale), c);
                }
                for (int i = 0; i < Sides; i++)
                {
                    int next = (i + 1) % Sides;
                    Triangle(first, first + 1 + i, first + 1 + next);
                    for (int ring = 0; ring < 2; ring++)
                    {
                        int inner = first + 1 + ring * Sides, outer = inner + Sides;
                        Triangle(inner + i, outer + i, outer + next);
                        Triangle(inner + i, outer + next, inner + next);
                    }
                }
            }
            internal void Draw(Material material)
            {
                if (vertices.Count == 0) return;
                if (mesh == null)
                {
                    mesh = new Mesh { name = "NMC " + name, hideFlags = HideFlags.HideAndDontSave,
                        indexFormat = IndexFormat.UInt32 };
                    mesh.MarkDynamic();
                }
                mesh.Clear(); mesh.SetVertices(vertices); mesh.SetColors(colors);
                mesh.SetTriangles(triangles, 0, false); mesh.RecalculateBounds();
                Graphics.DrawMesh(mesh, Matrix4x4.identity, material, 0, Find.Camera, 0, null, ShadowCastingMode.Off, false);
            }
            public void Dispose()
            { if (mesh != null) UnityEngine.Object.Destroy(mesh); mesh = null; Clear(); }
        }
    }

    [DisallowMultipleComponent]
    public sealed class TebrusStormVisualCleanup : MonoBehaviour
    {
        TebrusStormVisuals owner;
        internal void Bind(TebrusStormVisuals next)
        {
            if (owner == next) return;
            var previous = owner; owner = next; previous?.Dispose();
        }
        internal void Unbind(TebrusStormVisuals previous) { if (owner == previous) owner = null; }
        void Release() { var previous = owner; owner = null; previous?.Dispose(); }
        void LateUpdate() { if (owner != null && !owner.Viewed) Release(); }
        void OnDisable() { Release(); }
        void OnDestroy() { Release(); }
    }
}
