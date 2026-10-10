using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace NivarianCommander
{
    // Presentation only. Reuse the real sprite's alpha instead of drawing a disk or shield icon.
    internal static class DefenseShieldOverlay
    {
        static readonly Dictionary<Graphic, Graphic> layers = new Dictionary<Graphic, Graphic>();
        static readonly Dictionary<Material, Material> materials = new Dictionary<Material, Material>();
        // Silhouette uses the source alpha, independently of skin/hair/apparel tint channels.
        static readonly Color FilterColor = new Color(.58f, 1.08f, 1.34f, .62f);
        internal static Material FilterMaterial(Material original)
        {
            if (!materials.TryGetValue(original, out var result))
            {
                result = new Material(ShaderDatabase.Silhouette) { mainTexture = original.mainTexture,
                    mainTextureScale = original.mainTextureScale, mainTextureOffset = original.mainTextureOffset,
                    color = FilterColor, hideFlags = HideFlags.HideAndDontSave };
                materials.Add(original, result);
            }
            return result;
        }
        public static bool PotentialType(DefenseRecord r) => r.target is Pawn || r.target is Building;
        public static bool VisibleType(DefenseRecord r) => !(r.target is Pawn p && p.Dead);
        public static bool Active(Thing t)
        {
            if (t == null || !t.Spawned || t.Destroyed || !SupportDiplomacy.Friendly(t)) return false;
            var r = t.Map.GetComponent<CommanderDefense>()?.For(t);
            return r != null && ((r.remaining03 > 0 && r.shield03 > 0) || r.remaining12 > 0);
        }
        public static void Draw(Thing target)
        {
            if (!(target is Building) || !Active(target)) return;
            var graphic = target.Graphic;
            if (graphic != null)
            {
                if (target is Building_NivarianAircraft ship && ship.Airborne)
                {
                    var pos = ship.DrawPos; pos.y += .012f;
                    Graphics.DrawMesh(MeshPool.plane10, Matrix4x4.TRS(pos, Quaternion.AngleAxis(ship.heading, Vector3.up),
                        new Vector3(graphic.drawSize.x, 1, graphic.drawSize.y)), FilterMaterial(graphic.MatSingle), 0);
                }
                else
                {
                if (!layers.TryGetValue(graphic, out var layer))
                {
                    layer = graphic.GetColoredVersion(ShaderDatabase.Silhouette, FilterColor, FilterColor);
                    layers.Add(graphic, layer);
                }
                Vector3 at = target.DrawPos; at.y += .012f;
                layer.Draw(at, target.Rotation, target);
                }
            }
            var state = target.Map.GetComponent<CommanderDefense>().For(target);
            if (state?.remaining12 > 0) BuildingOverloadRibbons.Draw(target, state.remaining12);
        }
        internal static void DrawPawnMesh(Mesh mesh, Matrix4x4 matrix, Material material, bool drawNow)
        {
            if (mesh == null || material == null || material.mainTexture == null || material.shader == ShaderDatabase.Invisible) return;
            // The same mesh, UVs, rotation and layer order as the actual pawn draw request.
            // World-only: the far-view atlas stays unfiltered and gets its own live overlay.
            matrix.m13 += .0001f;
            GenDraw.DrawMeshNowOrLater(mesh, matrix, FilterMaterial(material), drawNow);
        }
    }
    [HarmonyPatch(typeof(TurretTop), nameof(TurretTop.DrawTurret))]
    internal static class ShieldTurretTopFilter
    {
        static void Postfix(TurretTop __instance, Building_Turret ___parentTurret, Vector3 drawLoc,
            Vector3 recoilDrawOffset, float recoilAngleOffset)
        {
            var turret = ___parentTurret;
            if (!DefenseShieldOverlay.Active(turret)) return;
            Vector3 offset = new Vector3(turret.def.building.turretTopOffset.x, 0, turret.def.building.turretTopOffset.y);
            offset = offset.RotatedBy(recoilAngleOffset) + recoilDrawOffset;
            Vector3 at = drawLoc + Altitudes.AltIncVect + offset; at.y += .012f;
            float size = turret.def.building.turretTopDrawSize;
            float angle = turret.CurrentEffectiveVerb?.AimAngleOverride ?? __instance.CurRotation;
            Graphics.DrawMesh(MeshPool.plane10, Matrix4x4.TRS(at, (TurretTop.ArtworkRotation + angle).ToQuat(),
                new Vector3(size, 1, size)), DefenseShieldOverlay.FilterMaterial(turret.TurretTopMaterial), 0);
        }
    }
    [HarmonyPatch(typeof(PawnRenderTree), nameof(PawnRenderTree.Draw))]
    internal static class ShieldPawnDrawFilter
    {
        [ThreadStatic] static bool active;
        static void Prefix(PawnDrawParms parms, out bool __state)
        { __state = active; active = !parms.Portrait && !parms.flags.FlagSet(PawnRenderFlags.Cache) && DefenseShieldOverlay.Active(parms.pawn); }
        static Exception Finalizer(bool __state, Exception __exception)
        { active = __state; return __exception; }
        static void Draw(Mesh mesh, Matrix4x4 matrix, Material material, bool drawNow, MaterialPropertyBlock block)
        {
            GenDraw.DrawMeshNowOrLater(mesh, matrix, material, drawNow, block);
            if (active) DefenseShieldOverlay.DrawPawnMesh(mesh, matrix, material, drawNow);
        }
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var native = AccessTools.Method(typeof(GenDraw), nameof(GenDraw.DrawMeshNowOrLater),
                new[] { typeof(Mesh), typeof(Matrix4x4), typeof(Material), typeof(bool), typeof(MaterialPropertyBlock) });
            int hits = 0;
            foreach (var instruction in instructions)
            {
                if (instruction.Calls(native))
                { hits++; instruction.opcode = OpCodes.Call; instruction.operand = AccessTools.Method(typeof(ShieldPawnDrawFilter), nameof(Draw)); }
                yield return instruction;
            }
            if (hits != 1) throw new InvalidOperationException("[NMC] Pawn shield draw boundary unresolved: " + hits);
        }
    }
    [HarmonyPatch(typeof(PawnRenderer), nameof(PawnRenderer.RenderPawnAt))]
    internal static class ShieldPawnCachedDrawFilter
    {
        [ThreadStatic] static bool active;
        static void Prefix(Pawn ___pawn, out bool __state)
        { __state = active; active = DefenseShieldOverlay.Active(___pawn); }
        static Exception Finalizer(bool __state, Exception __exception)
        { active = __state; return __exception; }
        static void Draw(Mesh mesh, Vector3 location, Quaternion rotation, Material material, bool drawNow)
        {
            GenDraw.DrawMeshNowOrLater(mesh, location, rotation, material, drawNow);
            if (active) DefenseShieldOverlay.DrawPawnMesh(mesh,
                Matrix4x4.TRS(location, rotation, Vector3.one), material, drawNow);
        }
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var native = AccessTools.Method(typeof(GenDraw), nameof(GenDraw.DrawMeshNowOrLater),
                new[] { typeof(Mesh), typeof(Vector3), typeof(Quaternion), typeof(Material), typeof(bool) });
            int hits = 0;
            foreach (var instruction in instructions)
            {
                if (instruction.Calls(native))
                { hits++; instruction.opcode = OpCodes.Call; instruction.operand = AccessTools.Method(typeof(ShieldPawnCachedDrawFilter), nameof(Draw)); }
                yield return instruction;
            }
            if (hits != 1) throw new InvalidOperationException("[NMC] Cached pawn shield draw boundary unresolved: " + hits);
        }
    }
}
