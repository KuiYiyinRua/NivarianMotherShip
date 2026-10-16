using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
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
    // Presentation lives outside simulation and is rebuilt from saved orders.
    // These are the same assets used by the native CompSelfBuilding.
    public sealed class FieldWarpVisuals : MapComponent
    {
        readonly Dictionary<FieldWarpOrder, PrintingEffect> effects = new Dictionary<FieldWarpOrder, PrintingEffect>();
        public FieldWarpVisuals(Map map) : base(map) { }
        public override void MapComponentUpdate()
        {
            var active = new HashSet<FieldWarpOrder>();
            if (Find.CurrentMap == map)
                foreach (var order in map.GetComponent<FieldWarp>().orders)
                {
                    if (order.subject == null || !order.subject.Spawned || order.subject.Destroyed
                        || order.subject.Map != map || !order.arriving && !order.DestinationValid) continue;
                    active.Add(order);
                    if (!effects.TryGetValue(order, out var effect))
                    {
                        effect = new PrintingEffect(map, order);
                        effects.Add(order, effect);
                        if (order.subject is Pawn pawn) pawn.Drawer?.renderer?.SetAllGraphicsDirty();
                    }
                    effect.Update();
                }
            foreach (var order in effects.Keys.Where(o => !active.Contains(o)).ToArray())
            { effects[order].Dispose(); effects.Remove(order); }
        }
        public override void MapRemoved()
        { foreach (var effect in effects.Values) effect.Dispose(); effects.Clear(); }
        public static Material MaterialFor(Thing subject, Material original)
        {
            var order = FieldWarp.For(subject);
            if (order == null || ReferenceEquals(original, null)) return original;
            var visuals = subject.Map.GetComponent<FieldWarpVisuals>();
            return visuals.effects.TryGetValue(order, out var effect) ? effect.MaterialFor(original) : original;
        }
        public static bool DrawShip(Thing ship, Vector3 at)
        {
            if (!FieldWarp.Pending(ship)) return false;
            var graphic = ship.Graphic;
            var original = graphic.MatAt(ship.Rotation, ship);
            var material = MaterialFor(ship, original);
            if (material == original) return false;
            var rotation = ship is Building_NivarianAircraft aircraft && aircraft.Airborne
                ? Quaternion.AngleAxis(aircraft.HullHeading, Vector3.up) : graphic.QuatFromRot(ship.Rotation);
            Graphics.DrawMesh(MeshPool.plane10, Matrix4x4.TRS(ship is Building_NivarianAircraft flying && flying.Airborne ? ship.DrawPos : at, rotation,
                new Vector3(graphic.drawSize.x, 1f, graphic.drawSize.y)), material, 0);
            return true;
        }

        sealed class PrintingEffect
        {
            readonly Map map;
            readonly FieldWarpOrder order;
            // GetFinalizedMaterial also runs in ParallelPreDraw. It may only publish
            // managed requests/read prepared materials; all Unity calls belong in Update.
            sealed class MaterialIdentity : IEqualityComparer<Material>
            {
                public bool Equals(Material a, Material b) => ReferenceEquals(a, b);
                public int GetHashCode(Material value) => RuntimeHelpers.GetHashCode(value);
            }
            readonly ConcurrentDictionary<Material, Material> materials = new ConcurrentDictionary<Material, Material>(new MaterialIdentity());
            readonly ConcurrentDictionary<Material, byte> requested = new ConcurrentDictionary<Material, byte>(new MaterialIdentity());
            LumiAnimatorItem printing, core;
            LumiParticleItem orb;
            public PrintingEffect(Map map, FieldWarpOrder order)
            { this.map = map; this.order = order; }
            float Progress => Mathf.Clamp01(order.arriving
                ? 1f - order.remaining / (float)FieldWarp.Duration : order.remaining / (float)FieldWarp.Duration);
            public Material MaterialFor(Material original)
            {
                if (materials.TryGetValue(original, out var material)) return material;
                requested.TryAdd(original, 0);
                return original;
            }
            void PrepareMaterials()
            {
                bool changed = false;
                foreach (var original in requested.Keys)
                {
                    if (original == null) { requested.TryRemove(original, out _); continue; }
                    if (LumiAssetLoader.Instance == null
                        || !LumiAssetLoader.Instance.GetMaterial("Nivarian_3DPrint", out var template, true) || template == null) continue;
                    var material = new Material(template) { mainTexture = original.mainTexture,
                        mainTextureScale = original.mainTextureScale, mainTextureOffset = original.mainTextureOffset,
                        hideFlags = HideFlags.HideAndDontSave };
                    material.SetFloat("_Progress", Progress);
                    materials[original] = material;
                    requested.TryRemove(original, out _);
                    changed = true;
                }
                if (changed && order.subject is Pawn pawn) pawn.Drawer?.renderer?.SetAllGraphicsDirty();
            }
            public void Update()
            {
                // Cosmetic native asset creation must never advance a map's Rand stream.
                Rand.PushState();
                try
                {
                    PrepareMaterials();
                    var position = order.subject.DrawPos;
                    var follow = order.subject;
                    float size = order.subject is Pawn ? 1.4f : Mathf.Max(order.subject.def.graphicData.drawSize.x, order.subject.def.graphicData.drawSize.y);
                    if (printing == null || !printing.HasLiveGameObject || printing.IsOrphanFor(map))
                        printing = LumiParticleEffects.SpawnAnimator(map, new LumiAnimatorData {
                            prefabName = "animator_BuildingPrinting", position = position, scale = size,
                            rotation = Quaternion.identity, altitudeLayer = AltitudeLayer.MoteOverhead }, follow);
                    if (core == null || !core.HasLiveGameObject || core.IsOrphanFor(map))
                        core = LumiParticleEffects.SpawnAnimator(map, new LumiAnimatorData {
                            prefabName = "animator_BuildingCore", position = position, scale = size,
                            rotation = Quaternion.identity, altitudeLayer = AltitudeLayer.MoteOverhead }, follow);
                    if (orb == null || !orb.HasLiveGameObject || orb.IsOrphanFor(map))
                        orb = LumiParticleEffects.SpawnParticle(map, new LumiParticleData {
                            prefabName = "particle_RotateOrb", position = position + new Vector3(0f, -.02f, 0f),
                            size = size, rotation = Quaternion.identity }, follow);
                    printing?.MaintainDeathTick(2f); core?.MaintainDeathTick(2f); orb?.MaintainDeathTick(2f);
                    foreach (var material in materials.Values) material.SetFloat("_Progress", Progress);
                }
                finally { Rand.PopState(); }
            }
            public void Dispose()
            {
                if (printing != null) map.GetComponent<LumiAnimatorDrawer>()?.ForceRemoveAndRecycle(printing);
                if (core != null) map.GetComponent<LumiAnimatorDrawer>()?.ForceRemoveAndRecycle(core);
                if (orb != null) { orb.Stop(false); map.GetComponent<LumiParticleManager>()?.ForceRemoveAndRecycle(orb); }
                if (order.subject is Pawn pawn) pawn.Drawer?.renderer?.SetAllGraphicsDirty();
                foreach (var material in materials.Values) Object.Destroy(material);
                materials.Clear();
                requested.Clear();
            }
        }
    }
    [HarmonyPatch(typeof(PawnRenderNodeWorker), nameof(PawnRenderNodeWorker.GetFinalizedMaterial))]
    public static class FieldWarpPawnMaterial
    {
        static void Postfix(PawnDrawParms parms, ref Material __result)
        { if (!parms.Portrait && parms.pawn != null) __result = FieldWarpVisuals.MaterialFor(parms.pawn, __result); }
    }
}
