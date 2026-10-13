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
            if (order == null || original == null) return original;
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
            readonly Dictionary<Material, Material> materials = new Dictionary<Material, Material>();
            LumiAnimatorItem printing, core;
            LumiParticleItem orb;
            public PrintingEffect(Map map, FieldWarpOrder order)
            { this.map = map; this.order = order; }
            float Progress => Mathf.Clamp01(order.arriving
                ? 1f - order.remaining / (float)FieldWarp.Duration : order.remaining / (float)FieldWarp.Duration);
            public Material MaterialFor(Material original)
            {
                if (!materials.TryGetValue(original, out var material))
                {
                    if (LumiAssetLoader.Instance == null
                        || !LumiAssetLoader.Instance.GetMaterial("Nivarian_3DPrint", out var template, true) || template == null) return original;
                    material = new Material(template) { mainTexture = original.mainTexture,
                        mainTextureScale = original.mainTextureScale, mainTextureOffset = original.mainTextureOffset,
                        hideFlags = HideFlags.HideAndDontSave };
                    materials.Add(original, material);
                }
                material.SetFloat("_Progress", Progress);
                return material;
            }
            public void Update()
            {
                // Cosmetic native asset creation must never advance a map's Rand stream.
                Rand.PushState();
                try
                {
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
