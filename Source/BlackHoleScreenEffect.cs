#if !RIMWORLD_1_7
using System;
using RimWorld.Planet;
using UnityEngine;
using UnityEngine.Rendering;
using Verse;

namespace NivarianCommander
{
    // Presentation only: sample this camera's completed world image, never Thing
    // positions or simulation RNG. IMGUI and map labels are rendered afterwards.
    [DisallowMultipleComponent]
    public sealed class BlackHoleScreenEffect : MonoBehaviour
    {
        const int Rings = 64;
        const int Segments = 128;
        const float EdgePixels = 24f;
        const float SpriteSizeCells = 3.5f;
        Camera mapCamera;
        Material copyMaterial;
        CommandBuffer screenCommands;
        MaterialPropertyBlock lensProperties, coreProperties;
        Mesh lensMesh, coreMesh;
        Texture2D singularity;
        Vector3[] vertices;
        Vector2[] uvs, directions;
        BlackHoleVortex failedVortex;

        public static void Sync(Map map)
        {
            if (map != Find.CurrentMap || Find.Camera == null) return;
            var camera = Find.Camera;
            var effect = camera.GetComponent<BlackHoleScreenEffect>();
            var active = CurrentVortex();
            if (active == null)
            {
                if (effect != null)
                {
                    effect.failedVortex = null;
                    effect.enabled = false;
                }
                return;
            }
            if (effect == null) effect = camera.gameObject.AddComponent<BlackHoleScreenEffect>();
            effect.enabled = effect.failedVortex != active;
        }

        static BlackHoleVortex CurrentVortex()
        {
            if (Current.ProgramState != ProgramState.Playing || LongEventHandler.ShouldWaitForEvent ||
                !WorldRendererUtility.DrawingMap || Find.CurrentMap == null) return null;
            return Find.CurrentMap.GetComponent<BlackHoleMap>()?.active;
        }

        void Awake() { mapCamera = GetComponent<Camera>(); }

        void LateUpdate()
        {
            // Also runs while the world view / main menu skips map component updates.
            if (CurrentVortex() == null || Find.Camera != mapCamera) enabled = false;
        }

        void OnDisable() { ReleaseResources(); }
        void OnDestroy() { ReleaseResources(); }

        void ReleaseResources()
        {
            if (copyMaterial != null) Destroy(copyMaterial);
            if (lensMesh != null) Destroy(lensMesh);
            if (coreMesh != null) Destroy(coreMesh);
            screenCommands?.Release();
            screenCommands = null;
            lensProperties = coreProperties = null;
            copyMaterial = null;
            lensMesh = coreMesh = null;
            vertices = null;
            uvs = directions = null;
            // ContentFinder owns the sprite texture; do not destroy its shared asset.
            singularity = null;
        }

        void EnsureResources()
        {
            if (copyMaterial != null) return;
            // Hidden/BlitCopy is included in this installed player's built-in shaders.
            // Its unlit, depth-independent copy pass lets a UV mesh remap real scene
            // pixels without shipping a platform-specific compiled shader bundle.
            var shader = Shader.Find("Hidden/BlitCopy");
            if (shader == null || !shader.isSupported)
                throw new InvalidOperationException("The built-in scene copy shader is unavailable.");
            copyMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            screenCommands = new CommandBuffer { name = "NMC black hole screen-space lens" };
            lensProperties = new MaterialPropertyBlock();
            coreProperties = new MaterialPropertyBlock();
            foreach (var properties in new[] { lensProperties, coreProperties })
            {
                properties.SetVector("_MainTex_ST", new Vector4(1f, 1f, 0f, 0f));
                properties.SetColor("_Color", Color.white);
            }
            coreProperties.SetTexture("_MainTex", Texture2D.blackTexture);
            singularity = ContentFinder<Texture2D>.Get("NMC/Effects/black_hole_singularity");
            directions = new Vector2[Segments];
            for (int s = 0; s < Segments; s++)
            {
                float angle = 2f * Mathf.PI * s / Segments;
                directions[s] = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            }
            vertices = new Vector3[1 + Rings * Segments];
            uvs = new Vector2[vertices.Length];
            lensMesh = new Mesh { name = "NMC black hole scene lens", hideFlags = HideFlags.HideAndDontSave };
            lensMesh.MarkDynamic();
            lensMesh.vertices = vertices;
            lensMesh.uv = uvs;
            lensMesh.triangles = DiskTriangles(Rings);

            // The AI sprite has near-opaque alpha inside. An opaque black disk under
            // its interior guarantees that world pixels cannot show through the core.
            var coreVertices = new Vector3[1 + Segments];
            for (int s = 0; s < Segments; s++) coreVertices[1 + s] = directions[s];
            coreMesh = new Mesh { name = "NMC opaque singularity core", hideFlags = HideFlags.HideAndDontSave };
            coreMesh.vertices = coreVertices;
            coreMesh.uv = new Vector2[coreVertices.Length];
            coreMesh.triangles = DiskTriangles(1);
        }

        static int[] DiskTriangles(int rings)
        {
            var indices = new int[Segments * 3 + (rings - 1) * Segments * 6];
            int index = 0;
            for (int s = 0; s < Segments; s++)
            {
                indices[index++] = 0;
                indices[index++] = 1 + s;
                indices[index++] = 1 + (s + 1) % Segments;
            }
            for (int r = 1; r < rings; r++)
                for (int s = 0; s < Segments; s++)
                {
                    int next = (s + 1) % Segments;
                    int inner = 1 + (r - 1) * Segments;
                    int outer = inner + Segments;
                    indices[index++] = inner + s;
                    indices[index++] = outer + s;
                    indices[index++] = outer + next;
                    indices[index++] = inner + s;
                    indices[index++] = outer + next;
                    indices[index++] = inner + next;
                }
            return indices;
        }

        bool Project(BlackHoleVortex active, out Vector2 center, out Vector2 east, out Vector2 north)
        {
            Vector3 world = active.center.ToVector3Shifted();
            world.y = 0f;
            Vector3 projected = mapCamera.WorldToViewportPoint(world);
            center = projected;
            east = (Vector2)mapCamera.WorldToViewportPoint(world + Vector3.right * BlackHoleMap.Radius) - center;
            north = (Vector2)mapCamera.WorldToViewportPoint(world + Vector3.forward * BlackHoleMap.Radius) - center;
            float halfWidth = Mathf.Abs(east.x) + Mathf.Abs(north.x);
            float halfHeight = Mathf.Abs(east.y) + Mathf.Abs(north.y);
            return projected.z > 0f && center.x + halfWidth > 0f && center.x - halfWidth < 1f &&
                center.y + halfHeight > 0f && center.y - halfHeight < 1f;
        }

        static float BorderDistance(Vector2 uv, int width, int height)
            => Mathf.Min(Mathf.Min(uv.x, 1f - uv.x) * width, Mathf.Min(uv.y, 1f - uv.y) * height);

        void UpdateLens(Vector2 center, Vector2 east, Vector2 north, float seconds, float envelope, int width, int height)
        {
            vertices[0] = ClipPoint(center);
            uvs[0] = center;
            for (int r = 1; r <= Rings; r++)
            {
                float radius = (float)r / Rings;
                // Zero displacement AND zero slope at the outer edge prevent a seam.
                float falloff = (1f - radius) * (1f - radius);
                float angle = (1.6f + seconds * 1.2f) * falloff * envelope;
                float sin = Mathf.Sin(angle), cos = Mathf.Cos(angle);
                float sampleRadius = radius * (1f + .12f * falloff * envelope);
                for (int s = 0; s < Segments; s++)
                {
                    Vector2 d = directions[s];
                    Vector2 position = center + radius * (east * d.x + north * d.y);
                    Vector2 sample = center + sampleRadius *
                        (east * (d.x * cos - d.y * sin) + north * (d.x * sin + d.y * cos));
                    // Off-screen pixels do not exist in the captured scene. Blend to
                    // identity near either viewport edge rather than smear a clamp.
                    float edge = Mathf.Clamp01(Mathf.Min(BorderDistance(position, width, height),
                        BorderDistance(sample, width, height)) / EdgePixels);
                    edge = edge * edge * (3f - 2f * edge);
                    int index = 1 + (r - 1) * Segments + s;
                    vertices[index] = ClipPoint(position);
                    uvs[index] = Vector2.Lerp(position, sample, edge);
                }
            }
            lensMesh.vertices = vertices;
            lensMesh.uv = uvs;
            lensMesh.RecalculateBounds();
        }

        static Vector3 ClipPoint(Vector2 viewport) => new Vector3(viewport.x * 2f - 1f, viewport.y * 2f - 1f, 0f);

        void DrawLens(RenderTexture snapshot, RenderTexture destination, Vector2 center, float pixels)
        {
            // This mesh is a screen pass. Give the GPU explicit identity view/projection
            // matrices and clip-space vertices instead of relying on GL state around
            // DrawMeshNow, which produced no visible lens in the installed D3D11 player.
            screenCommands.Clear();
            // Always a real intermediate texture, avoiding CameraTarget resolution
            // while OnRenderImage's nullable final destination is being composed.
            screenCommands.SetRenderTarget(new RenderTargetIdentifier(destination));
            screenCommands.SetViewport(new Rect(0f, 0f, snapshot.width, snapshot.height));
            screenCommands.SetViewProjectionMatrices(Matrix4x4.identity, Matrix4x4.identity);
            lensProperties.SetTexture("_MainTex", snapshot);
            screenCommands.DrawMesh(lensMesh, Matrix4x4.identity, copyMaterial, 0, 0, lensProperties);
            screenCommands.DrawMesh(coreMesh, Matrix4x4.TRS(ClipPoint(center), Quaternion.identity,
                new Vector3(pixels * .58f / snapshot.width, pixels * .58f / snapshot.height, 1f)),
                copyMaterial, 0, 0, coreProperties);
            screenCommands.SetViewProjectionMatrices(mapCamera.worldToCameraMatrix, mapCamera.projectionMatrix);
            Graphics.ExecuteCommandBuffer(screenCommands);
        }

        static void SetOutput(RenderTexture target)
        {
            Graphics.SetRenderTarget(target);
            GL.sRGBWrite = QualitySettings.activeColorSpace == ColorSpace.Linear && (target == null || target.sRGB);
        }

        void OnRenderImage(RenderTexture source, RenderTexture destination)
        {
            var active = CurrentVortex();
            if (active == null || mapCamera != Find.Camera || !mapCamera.orthographic ||
                !Project(active, out var center, out var east, out var north))
            {
                Graphics.Blit(source, destination);
                return;
            }
            RenderTexture snapshot = null, composite = null;
            bool matrixPushed = false;
            bool previousSRGB = GL.sRGBWrite;
            try
            {
                EnsureResources();
                // Animation follows this saved map-local elapsed count. No real-time
                // accumulator means pausing, switching maps and reloading cannot drift.
                float seconds = active.elapsed / 60f;
                float appear = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(active.elapsed / 18f));
                float disappear = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((BlackHoleMap.Duration - active.elapsed) / 24f));
                float envelope = appear * disappear;
                UpdateLens(center, east, north, seconds, envelope, source.width, source.height);

                // Resolve through Blit into a single-sample texture first. Unity deals
                // with source/MSAA orientation here; all UVs below use bottom-left origin.
                snapshot = RenderTexture.GetTemporary(source.width, source.height, 0, source.format);
                snapshot.wrapMode = TextureWrapMode.Clamp;
                snapshot.filterMode = FilterMode.Bilinear;
                SetOutput(snapshot);
                Graphics.Blit(source, snapshot);
                composite = RenderTexture.GetTemporary(source.width, source.height, 0, source.format);
                composite.wrapMode = TextureWrapMode.Clamp;
                composite.filterMode = FilterMode.Bilinear;
                SetOutput(composite);
                Graphics.Blit(snapshot, composite);

                // Draw the core after refraction so it stays round and opaque. Its size
                // is measured in camera pixels, not UI-scaled points or screen aspect.
                float pixels = SpriteSizeCells * source.height / (2f * mapCamera.orthographicSize) *
                    (1f + .025f * Mathf.Sin(seconds * 2f)) * Mathf.Lerp(.6f, 1f, appear) * disappear;
                DrawLens(snapshot, composite, center, pixels);
                SetOutput(composite);
                GL.PushMatrix();
                matrixPushed = true;
                GL.LoadPixelMatrix(0f, source.width, source.height, 0f);
                Graphics.DrawTexture(new Rect(center.x * source.width - pixels / 2f,
                    (1f - center.y) * source.height - pixels / 2f, pixels, pixels), singularity);
                GL.PopMatrix();
                matrixPushed = false;
                // Exactly one final copy submits the fully composed frame, whether
                // the callback's destination is another effect or the backbuffer.
                SetOutput(destination);
                Graphics.Blit(composite, destination);
            }
            catch (Exception exception)
            {
                failedVortex = active;
                Log.ErrorOnce("[NMC] Black hole scene filter disabled for this cast: " + exception, 19408317);
                // A renderer failure must not leave a partially drawn / black frame.
                if (matrixPushed) { GL.PopMatrix(); matrixPushed = false; }
                SetOutput(destination);
                Graphics.Blit(source, destination);
            }
            finally
            {
                if (matrixPushed) GL.PopMatrix();
                if (copyMaterial != null) copyMaterial.mainTexture = null;
                if (snapshot != null) RenderTexture.ReleaseTemporary(snapshot);
                if (composite != null) RenderTexture.ReleaseTemporary(composite);
                // Unity expects destination to remain bound when OnRenderImage ends.
                Graphics.SetRenderTarget(destination);
                GL.sRGBWrite = previousSRGB;
            }
            if (failedVortex == active) enabled = false;
        }
    }
}
#endif
