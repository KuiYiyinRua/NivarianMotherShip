using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace NivarianCommander
{
    internal sealed class FieldGroundOverlay
    {
        internal static readonly Color GroundColor = new Color(.10f, .48f, .88f, .16f);
        internal static readonly Color HoneycombColor = new Color(.22f, .78f, 1f, .38f);
        static Material groundMaterial, honeycombMaterial;
        Mesh ground, honeycomb;
        IntVec3 cachedCenter = IntVec3.Invalid, cachedSize = IntVec3.Invalid;

        internal void Draw(Map map, IntVec3 center)
        {
            if (!center.IsValid || !center.InBounds(map)) { Clear(); return; }
            if (ground == null || cachedCenter != center || cachedSize != map.Size)
            {
                Clear();
                // Map cell centres lie at +0.5; mesh positions are relative to this centre.
                var geometry = FieldGroundGeometry.Build(-center.x - .5f, -center.z - .5f,
                    map.Size.x - center.x - .5f, map.Size.z - center.z - .5f);
                ground = BuildMesh("NMC energy field ground", geometry.fillVertices, geometry.fillTriangles);
                honeycomb = BuildMesh("NMC energy field honeycomb", geometry.lineVertices, geometry.lineTriangles);
                cachedCenter = center;
                cachedSize = map.Size;
            }
            if (groundMaterial == null)
                groundMaterial = MaterialPool.MatFrom(BaseContent.WhiteTex, ShaderDatabase.Transparent, GroundColor);
            if (honeycombMaterial == null)
                honeycombMaterial = MaterialPool.MatFrom(BaseContent.WhiteTex, ShaderDatabase.Transparent, HoneycombColor);
            var at = center.ToVector3Shifted();
            // Above flooring, below plants, buildings, items, pawns and fog of war.
            at.y = AltitudeLayer.FloorEmplacement.AltitudeFor();
            Graphics.DrawMesh(ground, Matrix4x4.TRS(at, Quaternion.identity, Vector3.one), groundMaterial, 0);
            at.y += .002f;
            Graphics.DrawMesh(honeycomb, Matrix4x4.TRS(at, Quaternion.identity, Vector3.one), honeycombMaterial, 0);
        }

        static Mesh BuildMesh(string name, List<Vector3> vertices, List<int> triangles)
        {
            var mesh = new Mesh { name = name };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            mesh.UploadMeshData(true);
            return mesh;
        }

        internal void Clear()
        {
            if (ground != null) Object.Destroy(ground);
            if (honeycomb != null) Object.Destroy(honeycomb);
            ground = null;
            honeycomb = null;
            cachedCenter = cachedSize = IntVec3.Invalid;
        }
    }
}
