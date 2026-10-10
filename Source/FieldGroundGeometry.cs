using System;
using System.Collections.Generic;
using UnityEngine;

namespace NivarianCommander
{
    // Pure geometry: no map mutation, clock, random source, or Unity native allocation.
    internal sealed class FieldGroundGeometry
    {
        internal const float Radius = 8f;
        internal const float HexRadius = .65f;
        internal const float LineWidth = .035f;
        const int CircleSegments = 120;
        internal readonly List<Vector3> fillVertices = new List<Vector3>();
        internal readonly List<int> fillTriangles = new List<int>();
        internal readonly List<Vector3> lineVertices = new List<Vector3>();
        internal readonly List<int> lineTriangles = new List<int>();

        internal static FieldGroundGeometry Build(float minX, float minZ, float maxX, float maxZ)
        {
            var result = new FieldGroundGeometry();
            var circle = new List<Vector2>(CircleSegments);
            for (int i = 0; i < CircleSegments; i++)
            {
                double angle = i * Math.PI * 2 / CircleSegments;
                circle.Add(new Vector2((float)Math.Cos(angle) * Radius, (float)Math.Sin(angle) * Radius));
            }
            var bounds = new List<Vector2>
            {
                new Vector2(minX, minZ), new Vector2(maxX, minZ),
                new Vector2(maxX, maxZ), new Vector2(minX, maxZ)
            };
            var outline = Clip(circle, bounds);
            AddPolygon(outline, result.fillVertices, result.fillTriangles);
            if (outline.Count < 3) return result;

            var corners = new Vector2[6];
            for (int i = 0; i < corners.Length; i++)
            {
                double angle = i * Math.PI / 3;
                corners[i] = new Vector2((float)Math.Cos(angle) * HexRadius, (float)Math.Sin(angle) * HexRadius);
            }
            float rowHeight = (float)Math.Sqrt(3) * HexRadius;
            int columns = (int)Math.Ceiling((Radius + HexRadius) / (1.5f * HexRadius));
            int rows = (int)Math.Ceiling((Radius + HexRadius) / rowHeight) + 1;
            for (int q = -columns; q <= columns; q++)
            for (int r = -rows; r <= rows; r++)
            {
                var hexCenter = new Vector2(q * 1.5f * HexRadius, (r + ((q & 1) == 0 ? 0 : .5f)) * rowHeight);
                if (hexCenter.sqrMagnitude > (Radius + HexRadius) * (Radius + HexRadius)) continue;
                // Each shared edge is drawn once; the opposite three belong to neighbours.
                for (int edge = 0; edge < 3; edge++)
                    result.AddLine(hexCenter + corners[edge], hexCenter + corners[edge + 1], outline);
            }
            // Clip the perimeter as well: no blue spill outside the map or ability radius.
            for (int i = 0; i < circle.Count; i++)
                result.AddLine(circle[i], circle[(i + 1) % circle.Count], outline);
            return result;
        }

        void AddLine(Vector2 a, Vector2 b, List<Vector2> boundary)
        {
            var delta = b - a;
            float length = (float)Math.Sqrt(delta.sqrMagnitude);
            if (length < .00001f) return;
            var normal = new Vector2(-delta.y, delta.x) * (LineWidth * .5f / length);
            AddPolygon(Clip(new List<Vector2> { a - normal, b - normal, b + normal, a + normal }, boundary), lineVertices, lineTriangles);
        }

        static void AddPolygon(List<Vector2> polygon, List<Vector3> vertices, List<int> triangles)
        {
            if (polygon.Count < 3) return;
            int first = vertices.Count;
            foreach (var point in polygon) vertices.Add(new Vector3(point.x, 0, point.y));
            for (int i = 1; i + 1 < polygon.Count; i++)
            {
                // Counter-clockwise X/Z polygons need reversed indices to face upward.
                triangles.Add(first); triangles.Add(first + i + 1); triangles.Add(first + i);
            }
        }

        static List<Vector2> Clip(List<Vector2> polygon, List<Vector2> boundary)
        {
            if (boundary.Count < 3) return new List<Vector2>();
            for (int edge = 0; edge < boundary.Count && polygon.Count > 0; edge++)
            {
                var start = boundary[edge];
                var direction = boundary[(edge + 1) % boundary.Count] - start;
                var output = new List<Vector2>(polygon.Count + 1);
                var previous = polygon[polygon.Count - 1];
                float previousDistance = Cross(direction, previous - start);
                foreach (var current in polygon)
                {
                    float distance = Cross(direction, current - start);
                    if ((distance >= 0) != (previousDistance >= 0))
                    {
                        float t = previousDistance / (previousDistance - distance);
                        output.Add(previous + (current - previous) * t);
                    }
                    if (distance >= 0) output.Add(current);
                    previous = current;
                    previousDistance = distance;
                }
                polygon = output;
            }
            return polygon;
        }

        static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;
    }
}
