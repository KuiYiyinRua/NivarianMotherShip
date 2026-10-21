#if RIMWORLD_1_7
using RimUnity.ScreenEfx;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace NivarianCommander
{
    // ScreenEfx composes the map before UI and retires captured GPU resources safely.
    public sealed class BlackHoleScreenEffect : EfxClip
    {
        static System.WeakReference<Game> owner;
        static EfxHandle handle;
        Material material;
        readonly MaterialPropertyBlock properties = new MaterialPropertyBlock();

        public static void Sync(Map map)
        {
            if (map != Find.CurrentMap) return;
            if (CurrentVortex() == null)
            {
                handle?.Stop(); handle = null; owner = null;
                return;
            }
            if (owner != null && owner.TryGetTarget(out var game) && game == Current.Game && handle?.IsPlaying == true) return;
            handle?.Stop();
            owner = new System.WeakReference<Game>(Current.Game);
            handle = EfxPlayer.Play(new BlackHoleScreenEffect());
        }

        static BlackHoleVortex CurrentVortex() => Current.ProgramState == ProgramState.Playing &&
            !LongEventHandler.AnyEventNowOrWaiting && WorldRendererUtility.DrawingMap
            ? Find.CurrentMap?.GetComponent<BlackHoleMap>()?.active : null;

        public override void Update(EfxUpdateContext context) => Enabled = CurrentVortex() != null;

        public override void BuildFrame(EfxFrameBuilder frame)
        {
            var active = CurrentVortex();
            var camera = Find.Camera;
            if (active == null || camera == null || !camera.orthographic) return;
            Vector3 world = active.center.ToVector3Shifted(); world.y = 0f;
            // Waylith's camera returns top-origin viewport coordinates.
            Vector3 center = camera.WorldToViewportPoint(world);
            Vector2 east = (Vector2)camera.WorldToViewportPoint(world + Vector3.right * BlackHoleMap.Radius) - (Vector2)center;
            Vector2 north = (Vector2)camera.WorldToViewportPoint(world + Vector3.forward * BlackHoleMap.Radius) - (Vector2)center;
            if (center.z <= 0f || Mathf.Abs(east.x * north.y - east.y * north.x) < 1e-8f) return;
            float halfWidth = Mathf.Abs(east.x) + Mathf.Abs(north.x);
            float halfHeight = Mathf.Abs(east.y) + Mathf.Abs(north.y);
            if (center.x + halfWidth <= 0f || center.x - halfWidth >= 1f ||
                center.y + halfHeight <= 0f || center.y - halfHeight >= 1f) return;
            if (material == null)
            {
                material = new Material(DefDatabase<ShaderDef>.GetNamed("NMC_BlackHoleLens").Shader);
                material.SetTexture("_Singularity", ContentFinder<Texture2D>.Get("NMC/Effects/black_hole_singularity"));
            }
            float seconds = active.elapsed / 60f;
            float appear = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(active.elapsed / 18f));
            float disappear = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((BlackHoleMap.Duration - active.elapsed) / 24f));
            float size = 3.5f / (2f * camera.orthographicSize) *
                (1f + .025f * Mathf.Sin(seconds * 2f)) * Mathf.Lerp(.6f, 1f, appear) * disappear;
            properties.SetVector("_CenterEast", new Vector4(center.x, center.y, east.x, east.y));
            properties.SetVector("_NorthAnim", new Vector4(north.x, north.y, seconds, appear * disappear));
            properties.SetFloat("_CoreSize", size);
            frame.AddPass(material, properties: properties);
        }

        protected override void OnRelease()
        {
            if (material != null) UnityEngine.Object.Destroy(material);
            material = null;
        }
    }
}
#endif
