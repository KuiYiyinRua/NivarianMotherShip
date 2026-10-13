using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace NivarianCommander
{
    internal sealed class FieldGroundOverlay
    {
        internal static readonly Color GroundColor = new Color(.10f, .48f, .88f, .16f);
        internal static readonly Color HoneycombColor = new Color(.22f, .78f, 1f, .38f);
        const float EdgeFadeWidth = 1.6f;
        const int FadeTextureSize = 256;
        static Texture2D radialFadeTexture;
        static Material groundMaterial, honeycombMaterial;
        Mesh ground, honeycomb;
        IntVec3 cachedCenter = IntVec3.Invalid, cachedSize = IntVec3.Invalid;
        readonly float radius;
        internal FieldGroundOverlay(float radius = FieldGroundGeometry.Radius) { this.radius = radius; }

        internal void Draw(Map map, IntVec3 center)
        {
            if (!center.IsValid || !center.InBounds(map)) { Clear(); return; }
            if (ground == null || cachedCenter != center || cachedSize != map.Size)
            {
                Clear();
                // Map cell centres lie at +0.5; mesh positions are relative to this centre.
                var geometry = FieldGroundGeometry.Build(-center.x - .5f, -center.z - .5f,
                    map.Size.x - center.x - .5f, map.Size.z - center.z - .5f, radius);
                ground = BuildMesh("NMC energy field ground", geometry.fillVertices, geometry.fillTriangles);
                honeycomb = BuildMesh("NMC energy field honeycomb", geometry.lineVertices, geometry.lineTriangles);
                cachedCenter = center;
                cachedSize = map.Size;
            }
            if (groundMaterial == null)
                groundMaterial = MaterialPool.MatFrom(RadialFadeTexture, ShaderDatabase.Transparent, GroundColor);
            if (honeycombMaterial == null)
                honeycombMaterial = MaterialPool.MatFrom(RadialFadeTexture, ShaderDatabase.Transparent, HoneycombColor);
            var at = center.ToVector3Shifted();
            // Above flooring, below plants, buildings, items, pawns and fog of war.
            at.y = AltitudeLayer.FloorEmplacement.AltitudeFor();
            Graphics.DrawMesh(ground, Matrix4x4.TRS(at, Quaternion.identity, Vector3.one), groundMaterial, 0);
            at.y += .002f;
            Graphics.DrawMesh(honeycomb, Matrix4x4.TRS(at, Quaternion.identity, Vector3.one), honeycombMaterial, 0);
        }

        Mesh BuildMesh(string name, List<Vector3> vertices, List<int> triangles)
        {
            var mesh = new Mesh { name = name };
            mesh.SetVertices(vertices);
            // Both layers sample the same radial mask, even when clipped at map edges.
            var uvs = new List<Vector2>(vertices.Count);
            foreach (var vertex in vertices)
                uvs.Add(new Vector2(.5f + vertex.x / (2f * radius),
                    .5f + vertex.z / (2f * radius)));
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            mesh.UploadMeshData(true);
            return mesh;
        }

        static Texture2D RadialFadeTexture
        {
            get
            {
                if (radialFadeTexture != null) return radialFadeTexture;
                var texture = new Texture2D(FadeTextureSize, FadeTextureSize, TextureFormat.RGBA32, false)
                {
                    name = "NMC energy field radial fade",
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear
                };
                var pixels = new Color32[FadeTextureSize * FadeTextureSize];
                for (int z = 0; z < FadeTextureSize; z++)
                for (int x = 0; x < FadeTextureSize; x++)
                {
                    float localX = ((x + .5f) / FadeTextureSize * 2f - 1f) * FieldGroundGeometry.Radius;
                    float localZ = ((z + .5f) / FadeTextureSize * 2f - 1f) * FieldGroundGeometry.Radius;
                    float distance = Mathf.Sqrt(localX * localX + localZ * localZ);
                    float t = Mathf.Clamp01((FieldGroundGeometry.Radius - distance) / EdgeFadeWidth);
                    // Smoothstep keeps both ends of the fade free of a visible opacity seam.
                    float alpha = t * t * (3f - 2f * t);
                    pixels[z * FadeTextureSize + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(alpha * 255f));
                }
                texture.SetPixels32(pixels);
                texture.Apply(false, true);
                radialFadeTexture = texture;
                return texture;
            }
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
