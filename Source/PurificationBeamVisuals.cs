using System;
using System.Collections.Generic;
using RimWorld.Planet;
using UnityEngine;
using UnityEngine.Rendering;
using Verse;

namespace NivarianCommander
{
    // Reference F0000-F0733: gold/white column, orbiting solid facets, contact
    // spray and a brief curtain of white threads after the main column shuts off.
    // This object is local, unsaved, and never changes damage, targets or Rand.
    internal sealed class PurificationBeamVisuals : IDisposable
    {
        const int TrailLifetime = 180;
        const int MaxTraces = 256;
        const int EndingLifetime = 30;
        const int ShaftSteps = 64;
        static readonly Vector3 ShaftAxis = new Vector3(-.18f, 0f, 1f);
        static readonly Vector3 ShaftSide = new Vector3(1f, 0f, .18f).normalized;
        static readonly float[] Profile = { -1f, -.76f, -.51f, -.30f, -.16f, 0f, .16f, .30f, .51f, .76f, 1f };
        static readonly Color[] ProfileColors =
        {
            new Color(1f,.46f,.10f,0f), new Color(1f,.64f,.20f,.045f),
            new Color(1f,.81f,.39f,.25f), new Color(1f,.96f,.74f,.85f),
            new Color(1f,1f,.95f,.99f), Color.white,
            new Color(1f,1f,.95f,.99f), new Color(1f,.96f,.74f,.85f),
            new Color(1f,.81f,.39f,.25f), new Color(1f,.64f,.20f,.045f),
            new Color(1f,.46f,.10f,0f)
        };
        readonly Map map;
        readonly List<Trace> traces = new List<Trace>(MaxTraces);
        readonly List<Emitter> emitters = new List<Emitter>(4);
        readonly List<Ending> endings = new List<Ending>(16);
        readonly Batch ground = new Batch("purification ground");
        readonly Batch haze = new Batch("purification dust");
        readonly Batch backFacets = new Batch("purification far facets");
        readonly Batch light = new Batch("purification light");
        readonly Batch frontFacets = new Batch("purification near facets");
        Material groundMaterial, hazeMaterial, backMaterial, lightMaterial, frontMaterial;
        PurificationVisualCleanup cleanup;
        int visualTick;
        bool unavailable;
        internal bool Disposed { get; private set; }
        internal bool HasResidue => traces.Count != 0 || endings.Count != 0;
        internal bool Viewed => CanView(map);
        internal static bool CanView(Map map) => Current.ProgramState == ProgramState.Playing &&
            !LongEventHandler.ShouldWaitForEvent && Find.CurrentMap == map && WorldRendererUtility.DrawingMap;

        struct Trace
        {
            internal Vector3 position;
            internal int birth, seed;
            internal float strength;
        }
        struct Ending
        {
            internal Vector3 position;
            internal int birth, serial, elapsed;
            internal float strength;
        }
        sealed class Emitter
        {
            internal PurificationBeam beam;
            internal Vector3 previous;
            internal int sampledAt = int.MinValue / 2;
        }

        internal PurificationBeamVisuals(Map map) { this.map = map; }
        static Vector3 Position(PurificationBeam beam) => new Vector3(beam.position.x + .5f,
            AltitudeLayer.MoteOverhead.AltitudeFor(), beam.position.y + .5f);
        static float MainPower(int elapsed) => Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((elapsed - 36f) / 42f));
        static Color Opacity(Color color, float multiplier) { color.a *= multiplier; return color; }
        static float Noise(int seed)
        {
            uint n = unchecked((uint)seed);
            n ^= n >> 16; n = unchecked(n * 0x7feb352du);
            n ^= n >> 15; n = unchecked(n * 0x846ca68bu);
            n ^= n >> 16;
            return (n & 0xffffu) / 65535f;
        }
        bool Visible(Vector3 at)
        {
            var cell = new IntVec3(Mathf.FloorToInt(at.x), 0, Mathf.FloorToInt(at.z));
            return cell.InBounds(map) && !cell.Fogged(map) && cell.GetRoof(map)?.isThickRoof != true;
        }
        Emitter GetEmitter(PurificationBeam beam)
        {
            for (int i = 0; i < emitters.Count; i++)
                if (emitters[i].beam == beam) return emitters[i];
            var emitter = new Emitter { beam = beam };
            emitters.Add(emitter);
            return emitter;
        }
        void Sample(IReadOnlyList<PurificationBeam> beams)
        {
            for (int i = 0; i < beams.Count; i++)
            {
                var beam = beams[i];
                var emitter = GetEmitter(beam);
                Vector3 at = Position(beam);
                int since = visualTick - emitter.sampledAt;
                if (since < 8 || (since < 30 && (at - emitter.previous).sqrMagnitude < .09f)) continue;
                emitter.sampledAt = visualTick;
                emitter.previous = at;
                if (!Visible(at)) continue;
                if (traces.Count == MaxTraces) traces.RemoveAt(0);
                traces.Add(new Trace { position = at, birth = visualTick,
                    seed = unchecked(beam.serial * 83492791 + beam.elapsed * 104729),
                    strength = .10f + .90f * MainPower(beam.elapsed) });
            }
            for (int i = emitters.Count - 1; i >= 0; i--)
            {
                bool found = false;
                for (int b = 0; b < beams.Count; b++)
                    if (emitters[i].beam == beams[b]) { found = true; break; }
                if (!found) emitters.RemoveAt(i);
            }
        }

        internal void Finish(PurificationBeam beam)
        {
            if (Disposed || !Viewed) return;
            Vector3 at = Position(beam);
            if (!Visible(at)) return;
            if (endings.Count == 16) endings.RemoveAt(0);
            endings.Add(new Ending { position = at, birth = visualTick, serial = beam.serial,
                elapsed = beam.elapsed, strength = .1f + .9f * MainPower(beam.elapsed) });
        }
        internal void Tick(IReadOnlyList<PurificationBeam> beams)
        {
            if (Disposed) return;
            if (!Viewed) { Clear(); return; }
            visualTick++;
            int expired = 0;
            while (expired < traces.Count && visualTick - traces[expired].birth >= TrailLifetime) expired++;
            if (expired != 0) traces.RemoveRange(0, expired);
            for (int i = endings.Count - 1; i >= 0; i--)
                if (visualTick - endings[i].birth >= EndingLifetime) endings.RemoveAt(i);
            Sample(beams);
        }
        bool EnsureMaterials()
        {
            if (unavailable || Disposed) return false;
            if (lightMaterial != null) return true;
            var shader = Shader.Find("Hidden/Internal-Colored");
            if (shader == null || !shader.isSupported)
            {
                unavailable = true;
                Log.ErrorOnce("[NMC] Purification beam visual shader is unavailable.", 19409071);
                return false;
            }
            groundMaterial = Material(shader, BlendMode.OneMinusSrcAlpha, 3100);
            hazeMaterial = Material(shader, BlendMode.OneMinusSrcAlpha, 3102);
            backMaterial = Material(shader, BlendMode.OneMinusSrcAlpha, 3105);
            lightMaterial = Material(shader, BlendMode.One, 3110);
            frontMaterial = Material(shader, BlendMode.OneMinusSrcAlpha, 3115);
            return true;
        }
        static Material Material(Shader shader, BlendMode destination, int queue)
        {
            var material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave, renderQueue = queue };
            material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            material.SetInt("_DstBlend", (int)destination);
            material.SetInt("_Cull", (int)CullMode.Off);
            material.SetInt("_ZWrite", 0);
            material.SetInt("_ZTest", (int)CompareFunction.LessEqual);
            return material;
        }

        internal void Draw(IReadOnlyList<PurificationBeam> beams)
        {
            if (Disposed) return;
            if (!Viewed) { Dispose(); return; }
            if (!EnsureMaterials()) return;
            if (cleanup == null && Find.Camera != null)
            {
                cleanup = Find.Camera.GetComponent<PurificationVisualCleanup>() ??
                    Find.Camera.gameObject.AddComponent<PurificationVisualCleanup>();
                cleanup.Bind(this);
            }
            // A paused first frame still gets a visible targeting/arrival effect.
            Sample(beams);
            ground.Clear(); haze.Clear(); backFacets.Clear(); light.Clear(); frontFacets.Clear();
            for (int i = 0; i < traces.Count; i++) DrawTrace(traces[i]);
            float viewTop = Find.CameraDriver.CurrentViewRect.maxZ;
            for (int i = 0; i < beams.Count; i++)
            {
                var beam = beams[i];
                Vector3 at = Position(beam);
                if (!Visible(at)) continue;
                DrawBeam(beam, at, viewTop);
            }
            for (int i = 0; i < endings.Count; i++) DrawEnding(endings[i], viewTop);
            ground.Draw(groundMaterial);
            haze.Draw(hazeMaterial);
            backFacets.Draw(backMaterial);
            light.Draw(lightMaterial);
            frontFacets.Draw(frontMaterial);
        }

        void DrawBeam(PurificationBeam beam, Vector3 at, float viewTop)
        {
            float seconds = beam.elapsed / 60f;
            float phase = seconds * 1.45f + Noise(beam.serial) * 6.283185f;
            float power = MainPower(beam.elapsed);
            float pilot = Mathf.Clamp01((beam.elapsed - 18f) / 18f);
            float height = Mathf.Max(30f, viewTop - at.z + 6f);
            if (beam.elapsed < 78) DrawArrival(at, beam.elapsed, seconds);
            if (pilot > 0f)
            {
                DrawColumn(at, height, seconds, power, pilot);
                DrawFacets(at, phase, Mathf.Clamp01((beam.elapsed - 22f) / 30f));
            }
            DrawContact(at, seconds, beam.serial, .10f + .9f * power);
        }

        void DrawArrival(Vector3 at, int elapsed, float seconds)
        {
            float fade = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((elapsed - 40f) / 38f));
            float radius = Mathf.Lerp(3.3f, 2.65f, Mathf.Clamp01(elapsed / 60f));
            for (int i = 0; i < 96; i++)
            {
                float theta = i * Mathf.PI * 2f / 96f;
                float wave = .12f * Mathf.Sin(theta * 6f + seconds * 5f);
                Vector3 p = at + new Vector3(Mathf.Cos(theta) * (radius + wave), 0f,
                    Mathf.Sin(theta) * (radius + wave) * .70f);
                float tall = .20f + .21f * (.5f + .5f * Mathf.Sin(theta * 6f + seconds * 3f));
                light.Stroke(p, p + Vector3.forward * tall, .025f,
                    new Color(1f,.90f,.64f,.36f * fade));
            }
            light.Glow(at, 1.1f, .8f, new Color(1f,.79f,.42f,.28f * fade));
            // Initial dust front expands once; no permanent damage-radius outline.
            float expand = Mathf.Clamp01((elapsed - 38f) / 35f);
            for (int i = 0; i < 12 && expand > 0f; i++)
            {
                float theta = i * Mathf.PI / 6f;
                Vector3 p = at + new Vector3(Mathf.Cos(theta) * expand * 4.2f, 0f,
                    Mathf.Sin(theta) * expand * 2.8f);
                haze.Glow(p, .50f + expand * .60f, .32f + expand * .42f,
                    new Color(.70f,.68f,.60f,.17f * Mathf.Sin(expand * Mathf.PI)), 12);
            }
        }

        void DrawColumn(Vector3 at, float height, float seconds, float power, float pilot)
        {
            float widthScale = Mathf.Lerp(.075f, 1f, power);
            float intensity = Mathf.Lerp(.48f, 1f, power) * pilot;
            int first = light.VertexCount;
            for (int row = 0; row <= ShaftSteps; row++)
            {
                float distance = height * row / ShaftSteps;
                float sway = .038f * Mathf.Sin(distance * 2.1f + seconds * 8f) +
                    .020f * Mathf.Sin(distance * 5.3f - seconds * 10f);
                Vector3 middle = at + ShaftAxis * distance + ShaftSide * sway * power;
                float flux = .93f + .05f * Mathf.Sin(distance * 4.3f + seconds * 17f);
                float halfWidth = (1.20f + .25f * Mathf.Clamp01(distance / 16f)) * widthScale * flux;
                for (int col = 0; col < Profile.Length; col++)
                    light.Vertex(middle + ShaftSide * (Profile[col] * halfWidth),
                        Opacity(ProfileColors[col], intensity * flux));
            }
            light.Grid(first, ShaftSteps + 1, Profile.Length);
            for (int filament = 0; filament < 4; filament++)
            {
                Vector3 previous = at;
                for (int row = 0; row <= ShaftSteps; row++)
                {
                    float distance = height * row / ShaftSteps;
                    float wave = Mathf.Sin(distance * 1.85f + seconds * 7.5f + filament * 1.5708f);
                    Vector3 next = at + ShaftAxis * distance + ShaftSide * (wave * .63f * widthScale);
                    if (row > 0) light.Stroke(previous, next, .032f + .025f * power,
                        new Color(1f,.85f,.39f,(.16f + .16f * wave * wave) * intensity));
                    previous = next;
                }
            }
        }

        void DrawFacets(Vector3 at, float phase, float arrival)
        {
            if (arrival <= 0f) return;
            for (int band = 0; band < 3; band++)
            {
                float height = 2.8f + band * 3.0f + (1f - arrival) * 5f;
                Vector3 center = at + ShaftAxis * height;
                for (int piece = 0; piece < 3; piece++)
                {
                    float angle = (band == 1 ? -phase : phase) + piece * 2.094395f + band * .64f;
                    float depth = Mathf.Sin(angle);
                    Vector3 radial = new Vector3(Mathf.Cos(angle), 0f, depth * .33f);
                    Vector3 tangent = new Vector3(-depth, 0f, Mathf.Cos(angle) * .33f);
                    Vector3 root = center + radial * (1.52f + band * .12f);
                    root.y += depth < 0f ? .016f : -.008f;
                    Batch batch = depth < 0f ? frontFacets : backFacets;
                    Vector3 a = root + radial * .58f + Vector3.forward * .82f;
                    Vector3 b = root + tangent * .75f + Vector3.forward * .27f;
                    Vector3 c = root - Vector3.forward * .62f - tangent * .12f;
                    Vector3 joint = root + Vector3.forward * .13f;
                    Color rim = new Color(.97f,.95f,.83f,.96f * arrival);
                    Color gold = new Color(.72f,.59f,.33f,.98f * arrival);
                    Color dark = new Color(.115f,.14f,.18f,.97f * arrival);
                    batch.Triangle(a, b, joint, rim, gold, dark);
                    batch.Triangle(b, c, joint, gold, dark, dark);
                    batch.Triangle(c, a, joint, dark, rim, dark);
                    // Inset faces, bright bevels and a hollow-looking dark inner seam.
                    Vector3 ia = Vector3.Lerp(a, joint, .23f);
                    Vector3 ib = Vector3.Lerp(b, joint, .28f);
                    Vector3 ic = Vector3.Lerp(c, joint, .20f);
                    batch.Triangle(ia, ib, ic, dark, dark, new Color(.27f,.29f,.30f,arrival));
                    batch.Stroke(a, b, .038f, rim);
                    batch.Stroke(b, c, .028f, rim);
                    batch.Stroke(c, a, .032f, rim);
                    batch.Stroke(ia, ic, .026f, new Color(1f,.88f,.56f,.88f * arrival));
                }
            }
        }

        void DrawContact(Vector3 at, float seconds, int seed, float strength)
        {
            float pulse = .94f + .045f * Mathf.Sin(seconds * 19f) + .015f * Mathf.Sin(seconds * 37f);
            light.Glow(at, 4.5f, 3.25f, new Color(1f,.65f,.23f,.16f * strength));
            light.Glow(at, 2.80f, 2.12f, new Color(1f,.91f,.57f,.43f * strength * pulse));
            light.Glow(at, 1.63f, 1.28f, new Color(1f,1f,.94f,.96f * strength));
            light.Glow(at, .75f, .69f, new Color(1f,1f,1f,strength));
            // Two long, tapered horizontal streaks seen across the reference impact.
            light.Glow(at, 7.2f, .045f, new Color(1f,.97f,.81f,.55f * strength), 24);
            light.Glow(at + Vector3.forward * .20f, 5.7f, .026f,
                new Color(1f,.91f,.67f,.27f * strength), 24);
            for (int ray = 0; ray < 26; ray++)
            {
                float theta = ray * 2.399963f + .07f * Mathf.Sin(seconds * 3f + ray);
                float length = (2.0f + 2.3f * Noise(unchecked(seed + ray * 73))) *
                    (.83f + .17f * Mathf.Sin(seconds * 12f + ray * 1.72f));
                Vector3 direction = new Vector3(Mathf.Cos(theta), 0f, Mathf.Sin(theta) * .74f);
                light.Taper(at + direction * .48f, at + direction * length,
                    .16f + .23f * Noise(ray * 17 + 1), new Color(1f,.94f,.72f,.38f * strength));
            }
            for (int spark = 0; spark < 40; spark++)
            {
                int s = unchecked(seed * 397 ^ spark * 104729);
                float rate = 1.1f + Noise(s) * 1.4f;
                float life = Mathf.Repeat(seconds * rate + Noise(s + 7), 1f);
                float angle = Noise(s + 11) * Mathf.PI * 2f;
                float travel = (.9f + Noise(s + 13) * 3.0f) * life;
                Vector3 velocity = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle) * .66f);
                Vector3 p = at + velocity * travel + Vector3.forward * (life * (1f - life) * 2.4f);
                if (!Visible(p)) continue;
                float alpha = Mathf.Sin(life * Mathf.PI) * strength;
                light.Stroke(p - velocity * (.08f + .18f * life), p, .020f + .018f * Noise(s + 3),
                    new Color(1f,.91f,.67f,.75f * alpha));
                if (spark % 5 == 0)
                    light.Glow(p, .070f, .070f, new Color(1f,1f,.94f,.8f * alpha), 8);
            }
        }

        void DrawEnding(Ending ending, float viewTop)
        {
            if (!Visible(ending.position)) return;
            float age = visualTick - ending.birth;
            float fade = Mathf.Clamp01(1f - age / EndingLifetime);
            Vector3 at = ending.position;
            float height = Mathf.Max(30f, viewTop - at.z + 6f);
            for (int thread = 0; thread < 28; thread++)
            {
                int seed = unchecked(ending.serial * 397 + thread * 7919);
                float distance = Mathf.Repeat(Noise(seed) * height + age * .22f, height);
                float length = .55f + Noise(seed + 5) * 2.5f;
                Vector3 p = at + ShaftAxis * distance + ShaftSide * ((Noise(seed + 7) - .5f) * 1.10f * fade);
                light.Taper(p, p + ShaftAxis * length, (.04f + .095f * Noise(seed + 11)) * fade,
                    new Color(1f,1f,.97f,.92f * fade * ending.strength));
            }
            float contact = Mathf.Clamp01(1f - age / 17f) * ending.strength;
            if (contact > 0f) DrawContact(at, (ending.elapsed + age) / 60f, ending.serial, contact);
        }

        void DrawTrace(Trace trace)
        {
            if (!Visible(trace.position)) return;
            float age = (visualTick - trace.birth) / 60f;
            float remain = Mathf.Clamp01((TrailLifetime / 60f - age) / .85f) * trace.strength;
            float n = Noise(trace.seed);
            Vector3 at = trace.position;
            at.y = AltitudeLayer.FloorEmplacement.AltitudeFor() + .01f;
            ground.Glow(at, .86f + n * .4f, .62f + n * .25f,
                new Color(.11f,.085f,.045f,.09f * Mathf.Clamp01(age * 3f) * remain), 12);
            at.y = AltitudeLayer.MoteOverhead.AltitudeFor() - .02f;
            float hot = Mathf.Clamp01(1f - age / .95f) * remain;
            if (hot > 0f)
            {
                light.Glow(at, .74f, .48f, new Color(1f,.51f,.09f,.14f * hot), 12);
                for (int spark = 0; spark < 3; spark++)
                {
                    float angle = Noise(trace.seed + spark * 73) * 6.283185f;
                    Vector3 delta = new Vector3(Mathf.Cos(angle) * age * 1.3f, 0f,
                        Mathf.Sin(angle) * age * .8f + age * .35f);
                    light.Stroke(at + delta, at + delta + Vector3.forward * .08f, .021f,
                        new Color(1f,.65f,.23f,.52f * hot));
                }
            }
            float dust = Mathf.Clamp01(age / .18f) * remain;
            Vector3 drift = new Vector3((n - .5f) * age * .40f, 0f, age * .32f);
            float radius = .48f + age * .30f;
            haze.Glow(at + drift, radius, radius * .69f,
                new Color(.40f,.38f,.32f,.11f * dust), 12);
            haze.Glow(at + drift + Vector3.forward * .20f, radius * .66f, radius * .54f,
                new Color(.69f,.66f,.56f,.075f * dust), 12);
        }

        internal void Clear()
        {
            traces.Clear(); endings.Clear(); emitters.Clear(); visualTick = 0;
            ground.Clear(); haze.Clear(); backFacets.Clear(); light.Clear(); frontFacets.Clear();
        }
        public void Dispose()
        {
            if (Disposed) return;
            Disposed = true;
            if (cleanup != null) cleanup.Unbind(this);
            cleanup = null;
            Clear();
            ground.Dispose(); haze.Dispose(); backFacets.Dispose(); light.Dispose(); frontFacets.Dispose();
            if (groundMaterial != null) UnityEngine.Object.Destroy(groundMaterial);
            if (hazeMaterial != null) UnityEngine.Object.Destroy(hazeMaterial);
            if (backMaterial != null) UnityEngine.Object.Destroy(backMaterial);
            if (lightMaterial != null) UnityEngine.Object.Destroy(lightMaterial);
            if (frontMaterial != null) UnityEngine.Object.Destroy(frontMaterial);
            groundMaterial = hazeMaterial = backMaterial = lightMaterial = frontMaterial = null;
        }

        // Reused vertex-color meshes: no generated textures, live ParticleSystems,
        // real fire, filth, temperature changes, sounds or gameplay callbacks.
        sealed class Batch : IDisposable
        {
            static readonly Vector2[] Circle8 = Circle(8), Circle12 = Circle(12), Circle24 = Circle(24);
            readonly string name;
            readonly List<Vector3> vertices = new List<Vector3>(16384);
            readonly List<Color> colors = new List<Color>(16384);
            readonly List<int> triangles = new List<int>(49152);
            Mesh mesh;
            internal Batch(string name) { this.name = name; }
            internal int VertexCount => vertices.Count;
            internal void Clear() { vertices.Clear(); colors.Clear(); triangles.Clear(); }
            internal void Vertex(Vector3 p, Color c) { vertices.Add(p); colors.Add(c); }
            void Indices(int a, int b, int c) { triangles.Add(a); triangles.Add(b); triangles.Add(c); }
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
            internal void Triangle(Vector3 a, Vector3 b, Vector3 c, Color ca, Color cb, Color cc)
            {
                int first = VertexCount;
                Vertex(a, ca); Vertex(b, cb); Vertex(c, cc);
                Indices(first, first + 1, first + 2);
            }
            internal void Grid(int first, int rows, int columns)
            {
                for (int row = 0; row < rows - 1; row++)
                    for (int col = 0; col < columns - 1; col++)
                    {
                        int a = first + row * columns + col;
                        Indices(a, a + columns, a + columns + 1);
                        Indices(a, a + columns + 1, a + 1);
                    }
            }
            internal void Stroke(Vector3 from, Vector3 to, float width, Color color)
            {
                Vector3 delta = to - from;
                float length = Mathf.Sqrt(delta.x * delta.x + delta.z * delta.z);
                if (length < .0001f || width <= 0f || color.a <= 0f) return;
                Vector3 side = new Vector3(-delta.z, 0f, delta.x) * (width / (length * 2f));
                int first = VertexCount;
                Vertex(from - side, color); Vertex(from + side, color);
                Vertex(to - side, color); Vertex(to + side, color);
                Grid(first, 2, 2);
            }
            internal void Taper(Vector3 from, Vector3 to, float width, Color color)
            {
                Vector3 delta = to - from;
                float length = Mathf.Sqrt(delta.x * delta.x + delta.z * delta.z);
                if (length < .0001f || width <= 0f || color.a <= 0f) return;
                Vector3 side = new Vector3(-delta.z, 0f, delta.x) * (width / (length * 2f));
                Color clear = color; clear.a = 0f;
                Vector3 middle = Vector3.Lerp(from, to, .27f);
                int first = VertexCount;
                Vertex(from - side, clear); Vertex(from, color); Vertex(from + side, clear);
                Vertex(middle - side * .65f, clear); Vertex(middle, Opacity(color, .64f));
                Vertex(middle + side * .65f, clear); Vertex(to, clear);
                Grid(first, 2, 3);
                Indices(first + 3, first + 4, first + 6);
                Indices(first + 4, first + 5, first + 6);
            }
            internal void Glow(Vector3 at, float rx, float rz, Color color, int sides = 24)
            {
                if (color.a <= 0f) return;
                var circle = sides == 8 ? Circle8 : sides == 12 ? Circle12 : Circle24;
                sides = circle.Length;
                int center = VertexCount;
                Vertex(at, color);
                for (int ring = 1; ring <= 3; ring++)
                {
                    float size = ring == 1 ? .27f : ring == 2 ? .62f : 1f;
                    Color edge = Opacity(color, ring == 1 ? .78f : ring == 2 ? .23f : 0f);
                    for (int side = 0; side < sides; side++)
                        Vertex(at + new Vector3(circle[side].x * rx * size, 0f, circle[side].y * rz * size), edge);
                }
                for (int side = 0; side < sides; side++)
                {
                    int next = (side + 1) % sides;
                    Indices(center, center + 1 + side, center + 1 + next);
                    for (int ring = 0; ring < 2; ring++)
                    {
                        int inner = center + 1 + ring * sides;
                        int outer = inner + sides;
                        Indices(inner + side, outer + side, outer + next);
                        Indices(inner + side, outer + next, inner + next);
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

    // Also release GPU objects when the map loop stops in world view, menus or
    // camera destruction. Returning to a map reconstructs its local renderer.
    [DisallowMultipleComponent]
    public sealed class PurificationVisualCleanup : MonoBehaviour
    {
        PurificationBeamVisuals owner;
        internal void Bind(PurificationBeamVisuals next)
        {
            if (owner == next) return;
            var previous = owner;
            owner = next;
            previous?.Dispose();
        }
        internal void Unbind(PurificationBeamVisuals previous) { if (owner == previous) owner = null; }
        void Release() { var previous = owner; owner = null; previous?.Dispose(); }
        void LateUpdate() { if (owner != null && !owner.Viewed) Release(); }
        void OnDisable() { Release(); }
        void OnDestroy() { Release(); }
    }
}
