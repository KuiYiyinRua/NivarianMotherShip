using System;
using System.Collections.Generic;
using RimWorld.Planet;
using UnityEngine;
using UnityEngine.Rendering;
using Verse;

namespace NivarianCommander
{
    // Video reference: 60 fps, F0-F641. Slanted aqua shaft, white impact,
    // violet electrical spray, then warm embers / dark smoke along its path.
    // Every field here is cosmetic, local to this map and deliberately unsaved.
    internal sealed class SolarRayVisuals : IDisposable
    {
        const int TrailLifetime = 240;
        const int TrailSpacingTicks = 4;
        const int MaxTraces = 720;
        const int ShaftSteps = 36;
        static readonly float[] Profile = { -1f, -.66f, -.32f, -.12f, 0f, .12f, .32f, .66f, 1f };
        static readonly Color[] ProfileColors =
        {
            new Color(.12f,.35f,1f,0f), new Color(.08f,.67f,1f,.055f),
            new Color(.20f,1f,.78f,.24f), new Color(.65f,1f,.95f,.8f), Color.white,
            new Color(.65f,1f,.95f,.8f), new Color(.20f,1f,.78f,.24f),
            new Color(.08f,.67f,1f,.055f), new Color(.12f,.35f,1f,0f)
        };
        readonly Map map;
        readonly List<Trace> traces = new List<Trace>(256);
        readonly List<Emitter> emitters = new List<Emitter>(6);
        readonly Batch ground = new Batch("solar ray scorch");
        readonly Batch smoke = new Batch("solar ray smoke");
        readonly Batch light = new Batch("solar ray light");
        Material alphaMaterial, lightMaterial;
        SolarRayVisualCleanup cleanup;
        int visualTick;
        bool unavailable;

        struct Trace
        {
            internal Vector3 position;
            internal int birth, seed;
        }

        sealed class Emitter
        {
            internal SolarRaySalvo salvo;
            internal int lastSampleTick = int.MinValue / 2;
        }

        internal SolarRayVisuals(Map map) { this.map = map; }
        internal bool HasResidue => traces.Count != 0;
        internal bool Viewed => Current.ProgramState == ProgramState.Playing && !LongEventHandler.ShouldWaitForEvent &&
            Find.CurrentMap == map && WorldRendererUtility.DrawingMap;

        internal static Vector3 DisplayPosition(SolarRaySalvo salvo, SolarRayBeam beam)
        {
            // Pulse already computed the next segment and its damage. Interpolate
            // only the drawing over that same 15-tick segment, never write it back.
            float progress = (salvo.elapsed % SolarRayMap.Interval) / (float)SolarRayMap.Interval;
            Vector2 p = Vector2.Lerp(beam.previous, beam.position, progress);
            return new Vector3(p.x + .5f, 0f, p.y + .5f);
        }

        bool Visible(Vector3 at)
        {
            var cell = new IntVec3(Mathf.FloorToInt(at.x), 0, Mathf.FloorToInt(at.z));
            return cell.InBounds(map) && !cell.Fogged(map) && cell.GetRoof(map)?.isThickRoof != true;
        }

        static float Noise(int seed)
        {
            // A pure hash, not Verse.Rand, UnityEngine.Random or a mutable PRNG.
            uint n = unchecked((uint)seed);
            n ^= n >> 16; n = unchecked(n * 0x7feb352du);
            n ^= n >> 15; n = unchecked(n * 0x846ca68bu);
            n ^= n >> 16;
            return (n & 0xffffu) / 65535f;
        }

        static int Seed(SolarRaySalvo salvo, int beam)
            => unchecked(salvo.center.x * 73856093 ^ salvo.center.z * 19349663 ^ beam * 83492791);

        Emitter GetEmitter(SolarRaySalvo salvo)
        {
            for (int i = 0; i < emitters.Count; i++)
                if (emitters[i].salvo == salvo) return emitters[i];
            var emitter = new Emitter { salvo = salvo };
            emitters.Add(emitter);
            return emitter;
        }

        void Sample(IReadOnlyList<SolarRaySalvo> salvos)
        {
            for (int s = 0; s < salvos.Count; s++)
            {
                var salvo = salvos[s];
                var emitter = GetEmitter(salvo);
                if (visualTick - emitter.lastSampleTick < TrailSpacingTicks) continue;
                emitter.lastSampleTick = visualTick;
                for (int b = 0; b < salvo.beams.Count; b++)
                {
                    Vector3 at = DisplayPosition(salvo, salvo.beams[b]);
                    if (!Visible(at)) continue;
                    if (traces.Count == MaxTraces) traces.RemoveAt(0);
                    traces.Add(new Trace { position = at, birth = visualTick,
                        seed = unchecked(Seed(salvo, b) + salvo.elapsed * 104729) });
                }
            }
            for (int i = emitters.Count - 1; i >= 0; i--)
            {
                bool found = false;
                for (int s = 0; s < salvos.Count; s++)
                    if (emitters[i].salvo == salvos[s]) { found = true; break; }
                if (!found) emitters.RemoveAt(i);
            }
        }

        internal void Tick(IReadOnlyList<SolarRaySalvo> salvos)
        {
            if (!Viewed) { Clear(); return; }
            visualTick++;
            int expired = 0;
            while (expired < traces.Count && visualTick - traces[expired].birth >= TrailLifetime) expired++;
            if (expired != 0) traces.RemoveRange(0, expired);
            Sample(salvos);
        }

        bool EnsureMaterials()
        {
            if (unavailable) return false;
            if (alphaMaterial != null) return true;
            // The installed player includes this built-in unlit vertex-color shader.
            var shader = Shader.Find("Hidden/Internal-Colored");
            if (shader == null || !shader.isSupported)
            {
                unavailable = true;
                Log.ErrorOnce("[NMC] Solar ray visual shader is unavailable.", 19409051);
                return false;
            }
            alphaMaterial = NewMaterial(shader, BlendMode.OneMinusSrcAlpha, 3100);
            lightMaterial = NewMaterial(shader, BlendMode.One, 3110);
            return true;
        }

        static Material NewMaterial(Shader shader, BlendMode destination, int queue)
        {
            var material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave, renderQueue = queue };
            material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            material.SetInt("_DstBlend", (int)destination);
            material.SetInt("_Cull", (int)CullMode.Off);
            material.SetInt("_ZWrite", 0);
            material.SetInt("_ZTest", (int)CompareFunction.LessEqual);
            return material;
        }

        internal void Draw(IReadOnlyList<SolarRaySalvo> salvos)
        {
            if (!Viewed) { Clear(); return; }
            if (!EnsureMaterials()) return;
            if (cleanup == null && Find.Camera != null)
            {
                cleanup = Find.Camera.GetComponent<SolarRayVisualCleanup>() ??
                    Find.Camera.gameObject.AddComponent<SolarRayVisualCleanup>();
                cleanup.Bind(this);
            }
            // Prime a newly visible cast even if the map is paused before its first tick.
            Sample(salvos);
            ground.Clear(); smoke.Clear(); light.Clear();
            for (int i = 0; i < traces.Count; i++) DrawTrace(traces[i]);
            var view = Find.CameraDriver.CurrentViewRect;
            for (int s = 0; s < salvos.Count; s++)
                for (int b = 0; b < salvos[s].beams.Count; b++)
                {
                    var salvo = salvos[s];
                    Vector3 at = DisplayPosition(salvo, salvo.beams[b]);
                    if (Visible(at)) DrawBeam(salvo, b, at, view.maxZ);
                }
            ground.Draw(alphaMaterial);
            smoke.Draw(alphaMaterial);
            light.Draw(lightMaterial);
        }

        void DrawBeam(SolarRaySalvo salvo, int beam, Vector3 at, float viewTop)
        {
            float seconds = salvo.elapsed / 60f;
            float phase = seconds * 6f + beam * 2.0944f;
            float enter = Mathf.Lerp(.55f, 1f, Mathf.Clamp01(salvo.elapsed / 7f));
            float leave = Mathf.Clamp01((SolarRayMap.Duration - salvo.elapsed) / 12f);
            float strength = enter * leave;
            // The reference impact flashes for about two frames before its shaft.
            float shaftStrength = strength * Mathf.Clamp01(salvo.elapsed / 2f);
            float pulse = .94f + .06f * Mathf.Sin(phase * 3.3f);
            at.y = AltitudeLayer.MoteOverhead.AltitudeFor();
            // Extend beyond the current view so the orbital origin is never a floating
            // rectangular cap. The reference's shaft leans in from screen upper-left.
            float height = Mathf.Max(30f, viewTop - at.z + 5f);
            float retract = Mathf.SmoothStep(0f, 1f, leave);
            Vector3 sky = at + new Vector3(-.62f * height, 0f, height) * retract;
            Vector3 side = new Vector3(1f, 0f, .62f).normalized;
            int first = light.VertexCount;
            for (int step = 0; step <= ShaftSteps; step++)
            {
                float t = step / (float)ShaftSteps;
                float tremble = (.028f * Mathf.Sin(t * 28f + phase * 1.7f) +
                    .012f * Mathf.Sin(t * 73f - phase * 2.2f)) * Mathf.Sin(t * Mathf.PI);
                Vector3 middle = Vector3.Lerp(at, sky, t) + side * tremble;
                float halfWidth = Mathf.Lerp(.64f, .95f, t) * pulse * Mathf.Sqrt(shaftStrength);
                float flux = .89f + .075f * Mathf.Sin(t * 83f + seconds * 18f + beam) +
                    .035f * Mathf.Sin(t * 151f + seconds * 31f);
                for (int column = 0; column < Profile.Length; column++)
                {
                    Color color = ProfileColors[column];
                    color.a *= shaftStrength * flux;
                    light.Vertex(middle + side * (Profile[column] * halfWidth), color);
                }
            }
            light.Grid(first, ShaftSteps + 1, Profile.Length);
            for (int filament = 0; filament < 3; filament++)
            {
                Vector3 previous = at;
                for (int segment = 1; segment <= ShaftSteps; segment++)
                {
                    float t = segment / (float)ShaftSteps;
                    float wave = Mathf.Sin(t * 30f + phase * 1.4f + filament * 2.0944f);
                    Vector3 p = Vector3.Lerp(at, sky, t) + side * (wave * (.11f + t * .19f));
                    light.Stroke(previous, p, .022f * shaftStrength,
                        new Color(.22f, .78f, 1f, (.20f + .12f * wave) * shaftStrength));
                    previous = p;
                }
            }

            // Bloom has soft filled gradients rather than an outlined targeting circle.
            light.Glow(at, 2.1f, 1.65f, new Color(.08f,.45f,1f,.13f * strength));
            light.Glow(at, 1.15f, .9f, new Color(.15f,1f,.86f,.46f * strength * pulse));
            light.Glow(at, .53f, .43f, new Color(.92f,1f,1f,.98f * strength));
            light.Glow(at, .25f, .3f, new Color(1f,1f,1f,strength));
            light.Stroke(at - Vector3.right * 1.3f, at + Vector3.right * 1.3f,
                .025f, new Color(.65f,.95f,1f,.35f * strength));

            for (int arc = 0; arc < 4; arc++)
            {
                float angle = phase * .65f + arc * 1.5708f;
                Vector3 previous = at;
                for (int p = 0; p < 10; p++)
                {
                    float along = p / 9f;
                    float theta = angle + along * 1.1f;
                    float radius = (.78f + .19f * Mathf.Sin(theta * 7f + phase * 2.6f)) * (1f - .35f * along);
                    Vector3 next = at + new Vector3(Mathf.Cos(theta) * radius, .008f,
                        Mathf.Sin(theta) * radius * .63f + along * .82f);
                    if (p != 0) light.Stroke(previous, next, .035f * (1f - .6f * along),
                        new Color(arc % 2 == 0 ? .28f : .72f, .35f, 1f, .55f * strength * (1f - along)));
                    previous = next;
                }
            }
            for (int spark = 0; spark < 10; spark++)
            {
                float life = Mathf.Repeat(seconds * (2.3f + Noise(spark + beam * 73)) + Noise(spark * 31), 1f);
                float angle = spark * 2.39996f + beam;
                Vector3 velocity = new Vector3(Mathf.Cos(angle) * 1.35f, 0f,
                    Mathf.Sin(angle) * .5f + .75f);
                Vector3 p = at + velocity * life + Vector3.forward * (life * (1f - life));
                light.Stroke(p - velocity * .07f, p, .022f,
                    new Color(.42f,.76f,1f,(1f - life) * .6f * strength));
            }
        }

        void DrawTrace(Trace trace)
        {
            if (!Visible(trace.position)) return;
            float age = (visualTick - trace.birth) / 60f;
            float remain = Mathf.Clamp01((TrailLifetime / 60f - age) / 1.05f);
            float spread = Noise(trace.seed);
            Vector3 at = trace.position;
            at.y = AltitudeLayer.FloorEmplacement.AltitudeFor() + .01f;
            float charAlpha = Mathf.Clamp01(age * 4f) * remain * .18f;
            ground.Glow(at, .6f + spread * .25f, .38f + spread * .18f,
                new Color(.07f,.055f,.045f,charAlpha), 12);

            at.y = AltitudeLayer.MoteOverhead.AltitudeFor() - .02f;
            float cold = Mathf.Clamp01(1f - age / .28f);
            if (cold > 0f)
            {
                light.Glow(at, .4f, .26f, new Color(.06f,.58f,1f,cold * .24f), 12);
                light.Glow(at, .54f, .33f, new Color(.47f,.22f,1f,cold * .12f), 12);
            }
            float heat = Mathf.Clamp01(age / .16f) * Mathf.Clamp01((2.35f - age) / 1.75f);
            if (heat > 0f)
            {
                float flicker = .65f + .35f * Mathf.Sin(age * 29f + spread * 21f);
                light.Glow(at, .46f, .30f, new Color(1f,.18f,.015f,heat * .18f), 12);
                for (int flame = 0; flame < 2; flame++)
                {
                    float side = Noise(trace.seed + flame * 631) - .5f;
                    Vector3 root = at + new Vector3(side * 1.1f, .005f, (spread - .5f) * .35f);
                    float tall = (.23f + .40f * Noise(trace.seed + flame * 37 + 4)) * flicker;
                    Vector3 tip = root + new Vector3(.11f * Mathf.Sin(age * 13f + spread * 41f), 0f, tall);
                    light.Flame(root, tip, .12f + .07f * spread,
                        new Color(1f,.28f,.025f,.52f * heat), new Color(1f,.75f,.24f,.22f * heat));
                    light.Glow(root, .105f, .08f, new Color(1f,.63f,.15f,.65f * heat), 8);
                }
            }
            float haze = Mathf.Clamp01((age - .12f) / .45f) * remain;
            if (haze > 0f)
            {
                Vector3 drift = new Vector3((spread - .5f) * age * .3f, 0f, age * .27f);
                Vector3 puff = at + drift;
                float radius = .22f + age * .19f;
                smoke.Glow(puff, radius * 1.1f, radius * .86f,
                    new Color(.17f,.19f,.21f,.105f * haze), 12);
                puff += new Vector3(.13f * Mathf.Sin(spread * 15f), .002f, radius * .32f);
                smoke.Glow(puff, radius * .63f, radius * .56f,
                    new Color(.30f,.32f,.35f,.075f * haze), 10);
            }
        }

        internal void Clear()
        {
            traces.Clear(); emitters.Clear(); visualTick = 0;
            ground.Clear(); smoke.Clear(); light.Clear();
        }

        public void Dispose()
        {
            if (cleanup != null) cleanup.Unbind(this);
            cleanup = null;
            Clear();
            ground.Dispose(); smoke.Dispose(); light.Dispose();
            if (alphaMaterial != null) UnityEngine.Object.Destroy(alphaMaterial);
            if (lightMaterial != null) UnityEngine.Object.Destroy(lightMaterial);
            alphaMaterial = lightMaterial = null;
        }

        // Three reusable meshes per map: alpha ground, alpha smoke, additive light.
        // No texture generation, particle GameObjects, spawned fires, or damage calls.
        sealed class Batch : IDisposable
        {
            static readonly Vector2[] Circle8 = Circle(8), Circle10 = Circle(10), Circle12 = Circle(12), Circle24 = Circle(24);
            readonly string name;
            readonly List<Vector3> vertices = new List<Vector3>(8192);
            readonly List<Color> colors = new List<Color>(8192);
            readonly List<int> triangles = new List<int>(24576);
            Mesh mesh;
            static Vector2[] Circle(int sides)
            {
                var result = new Vector2[sides];
                for (int i = 0; i < sides; i++)
                {
                    float angle = i * 2f * Mathf.PI / sides;
                    result[i] = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                }
                return result;
            }
            internal Batch(string name) { this.name = name; }
            internal int VertexCount => vertices.Count;
            internal void Clear() { vertices.Clear(); colors.Clear(); triangles.Clear(); }
            internal void Vertex(Vector3 p, Color c) { vertices.Add(p); colors.Add(c); }
            void Triangle(int a, int b, int c) { triangles.Add(a); triangles.Add(b); triangles.Add(c); }
            internal void Grid(int first, int rows, int columns)
            {
                for (int r = 0; r < rows - 1; r++)
                    for (int c = 0; c < columns - 1; c++)
                    {
                        int a = first + r * columns + c;
                        Triangle(a, a + columns, a + columns + 1);
                        Triangle(a, a + columns + 1, a + 1);
                    }
            }
            internal void Stroke(Vector3 from, Vector3 to, float width, Color color)
            {
                Vector3 delta = to - from;
                float length = Mathf.Sqrt(delta.x * delta.x + delta.z * delta.z);
                if (length < .0001f || color.a <= 0f || width <= 0f) return;
                Vector3 side = new Vector3(-delta.z, 0f, delta.x) * (width / (2f * length));
                int first = VertexCount;
                Vertex(from - side, color); Vertex(from + side, color);
                Vertex(to - side, color); Vertex(to + side, color);
                Grid(first, 2, 2);
            }
            internal void Flame(Vector3 root, Vector3 tip, float width, Color baseColor, Color tipColor)
            {
                int first = VertexCount;
                Color clear = baseColor; clear.a = 0f;
                Vertex(root - Vector3.right * width, clear);
                Vertex(root, baseColor);
                Vertex(root + Vector3.right * width, clear);
                Vertex(Vector3.Lerp(root, tip, .53f) - Vector3.right * width * .45f, tipColor);
                Vertex(Vector3.Lerp(root, tip, .53f) + Vector3.right * width * .45f, tipColor);
                Vertex(tip, clear);
                Triangle(first, first + 1, first + 3); Triangle(first + 1, first + 4, first + 3);
                Triangle(first + 1, first + 2, first + 4); Triangle(first + 3, first + 4, first + 5);
            }
            internal void Glow(Vector3 at, float rx, float rz, Color color, int sides = 24)
            {
                if (color.a <= 0f) return;
                var circle = sides == 8 ? Circle8 : sides == 10 ? Circle10 : sides == 12 ? Circle12 : Circle24;
                sides = circle.Length;
                int center = VertexCount;
                Vertex(at, color);
                for (int ring = 1; ring <= 3; ring++)
                {
                    float size = ring == 1 ? .27f : ring == 2 ? .62f : 1f;
                    Color edge = color;
                    edge.a *= ring == 1 ? .78f : ring == 2 ? .23f : 0f;
                    for (int side = 0; side < sides; side++)
                    {
                        Vector2 direction = circle[side];
                        Vertex(at + new Vector3(direction.x * rx * size, 0f,
                            direction.y * rz * size), edge);
                    }
                }
                for (int side = 0; side < sides; side++)
                {
                    int next = (side + 1) % sides;
                    Triangle(center, center + 1 + side, center + 1 + next);
                    for (int ring = 0; ring < 2; ring++)
                    {
                        int inner = center + 1 + ring * sides;
                        int outer = inner + sides;
                        Triangle(inner + side, outer + side, outer + next);
                        Triangle(inner + side, outer + next, inner + next);
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
                mesh.Clear();
                mesh.SetVertices(vertices); mesh.SetColors(colors); mesh.SetTriangles(triangles, 0, false);
                mesh.RecalculateBounds();
                Graphics.DrawMesh(mesh, Matrix4x4.identity, material, 0, Find.Camera, 0, null, ShadowCastingMode.Off, false);
            }
            public void Dispose() { if (mesh != null) UnityEngine.Object.Destroy(mesh); mesh = null; }
        }
    }

    // Unity calls this on the main thread even when the map's update loop has
    // stopped (world view, main menu or a destroyed map camera).
    [DisallowMultipleComponent]
    public sealed class SolarRayVisualCleanup : MonoBehaviour
    {
        SolarRayVisuals owner;
        internal void Bind(SolarRayVisuals next)
        {
            if (owner == next) return;
            var previous = owner;
            owner = next;
            previous?.Dispose();
        }
        internal void Unbind(SolarRayVisuals previous) { if (owner == previous) owner = null; }
        void Release() { var previous = owner; owner = null; previous?.Dispose(); }
        void LateUpdate() { if (owner != null && !owner.Viewed) Release(); }
        void OnDisable() { Release(); }
        void OnDestroy() { Release(); }
    }
}
