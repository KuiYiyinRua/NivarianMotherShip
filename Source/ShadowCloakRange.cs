using System;
using System.Collections.Generic;
using Verse;

namespace NivarianCommander
{
    public static class ShadowCloakRange
    {
        // The nearest occupied cell is in range exactly when at least one of
        // the footprint's cell-centered circles contains the target cell.
        public static bool Contains(CellRect footprint, IntVec3 cell, float radius)
        {
            if (radius <= 0f || footprint.Width <= 0 || footprint.Height <= 0) return false;
            var nearest = new IntVec3(Math.Max(footprint.minX, Math.Min(footprint.maxX, cell.x)),
                cell.y, Math.Max(footprint.minZ, Math.Min(footprint.maxZ, cell.z)));
            return cell.InHorDistOf(nearest, radius);
        }

        public static List<IntVec3> Cells(CellRect footprint, Map map, float radius)
        {
            var cells = new List<IntVec3>();
            if (map == null || radius <= 0f) return cells;
            foreach (var cell in footprint.ExpandedBy((int)Math.Ceiling(radius)))
                if (cell.InBounds(map) && Contains(footprint, cell, radius)) cells.Add(cell);
            return cells;
        }
    }
}
