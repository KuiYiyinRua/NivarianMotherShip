using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using LumiParticle.Assets;
using LumiParticle.Data;
using LumiParticle.Effects;
using LumiParticle.Items;
using LumiParticle.MapComponents;
using UnityEngine;
using Verse;

namespace NivarianCommander
{
    // Local presentation derived from the saved five-second formation timer.
    // Only the destination projection receives native self-building effects.
    public sealed class ProjectionVisuals : MapComponent
    {
        readonly Dictionary<Thing, Formation> effects = new Dictionary<Thing, Formation>();
        public ProjectionVisuals(Map map) : base(map) { }
        public static bool Forming(Thing thing)
        {
            var link = ProjectionRegistry.Projected(thing);
            return link != null && !link.retiring && link.remaining > 3600;
        }
        public override void MapComponentUpdate()
        {
            var active = new HashSet<Thing>();
            if (Find.CurrentMap == map && ProjectionRegistry.Current != null)
                foreach (var link in ProjectionRegistry.Current.links)
                {
                    if (link.targetMap != map || link.projection?.Spawned != true || !Forming(link.projection)) continue;
                    active.Add(link.projection);
                    if (!effects.TryGetValue(link.projection, out var effect))
                        effects.Add(link.projection, effect = new Formation(map, link));
                    effect.Update();
                }
            foreach (var thing in effects.Keys.Where(t => !active.Contains(t)).ToArray())
            { effects[thing].Dispose(); effects.Remove(thing); }
        }
        public override void MapRemoved()
        { foreach (var effect in effects.Values) effect.Dispose(); effects.Clear(); }

        sealed class Formation
        {
            readonly Map map;
            readonly ProjectionLink link;
            Material material;
            LumiAnimatorItem printing, core;
            LumiParticleItem orb;
            public Formation(Map map, ProjectionLink link) { this.map = map; this.link = link; }
            public void Update()
            {
                var thing = link.projection;
                if (thing.Position.Fogged(map)) return;
                Rand.PushState();
                try
                {
                    var position = thing.DrawPos;
                    var graphic = thing.Graphic;
                    float size = Mathf.Max(graphic.drawSize.x, graphic.drawSize.y);
                    if (printing == null || !printing.HasLiveGameObject || printing.IsOrphanFor(map))
                        printing = LumiParticleEffects.SpawnAnimator(map, new LumiAnimatorData {
                            prefabName = "animator_BuildingPrinting", position = position, scale = size,
                            rotation = Quaternion.identity, altitudeLayer = AltitudeLayer.MoteOverhead }, thing);
                    if (core == null || !core.HasLiveGameObject || core.IsOrphanFor(map))
                        core = LumiParticleEffects.SpawnAnimator(map, new LumiAnimatorData {
                            prefabName = "animator_BuildingCore", position = position, scale = size,
                            rotation = Quaternion.identity, altitudeLayer = AltitudeLayer.MoteOverhead }, thing);
                    if (orb == null || !orb.HasLiveGameObject || orb.IsOrphanFor(map))
                        orb = LumiParticleEffects.SpawnParticle(map, new LumiParticleData {
                            prefabName = "particle_RotateOrb", position = position + new Vector3(0f, -.02f, 0f),
                            size = size, rotation = Quaternion.identity }, thing);
                    printing?.MaintainDeathTick(2f); core?.MaintainDeathTick(2f); orb?.MaintainDeathTick(2f);
                    if (material == null && LumiAssetLoader.Instance != null
                        && LumiAssetLoader.Instance.GetMaterial("Nivarian_3DPrint", out var template, true) && template != null)
                    {
                        var original = graphic.MatAt(thing.Rotation, thing);
                        material = new Material(template) { mainTexture = original.mainTexture,
                            mainTextureScale = original.mainTextureScale, mainTextureOffset = original.mainTextureOffset,
                            hideFlags = HideFlags.HideAndDontSave };
                    }
                    if (material != null)
                    {
                        material.SetFloat("_Progress", Mathf.Clamp01((3900 - link.remaining) / 300f));
                        Graphics.DrawMesh(graphic.MeshAt(thing.Rotation), position + graphic.DrawOffset(thing.Rotation),
                            graphic.QuatFromRot(thing.Rotation), material, 0);
                    }
                }
                finally { Rand.PopState(); }
            }
            public void Dispose()
            {
                if (printing != null) map.GetComponent<LumiAnimatorDrawer>()?.ForceRemoveAndRecycle(printing);
                if (core != null) map.GetComponent<LumiAnimatorDrawer>()?.ForceRemoveAndRecycle(core);
                if (orb != null) { orb.Stop(false); map.GetComponent<LumiParticleManager>()?.ForceRemoveAndRecycle(orb); }
                if (material != null) Object.Destroy(material);
            }
        }
    }
    [HarmonyPatch(typeof(Graphic), nameof(Graphic.Print))]
    public static class ProjectionFormationMesh
    {
        static bool Prefix(Thing thing) => !ProjectionVisuals.Forming(thing);
    }
    [HarmonyPatch(typeof(Graphic), nameof(Graphic.DrawWorker))]
    public static class ProjectionFormationGraphic
    {
        static bool Prefix(Thing thing) => !ProjectionVisuals.Forming(thing);
    }
}
