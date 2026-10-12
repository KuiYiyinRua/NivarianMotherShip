using System;
using System.Collections.Generic;
using Verse;

namespace NivarianCommander
{
    // Direct orbital fire and every solar shell share the same square footprint
    // and presentation. Their callers retain payment, scheduling and targeting.
    public static class OrbitalStrike
    {
        public const int HalfExtent = 1;
        public static bool Contains(IntVec3 center, IntVec3 cell)
            => Math.Abs(cell.x - center.x) <= HalfExtent && Math.Abs(cell.z - center.z) <= HalfExtent;

        public static List<IntVec3> Cells(Map map, IntVec3 center)
        {
            var cells = new List<IntVec3>(9);
            foreach (IntVec3 cell in CellRect.CenteredOn(center, HalfExtent).Cells)
                if (cell.InBounds(map)) cells.Add(cell);
            return cells;
        }

        public static void Execute(Map map, IntVec3 center, DamageDef damage, float armorPenetration)
        {
            map.GetComponent<OrbitalVisuals>().Add(center);
            // overrideCells includes all four diagonal corners, with no extra
            // neighboring cells. The radius is native sound/falloff metadata.
            GenExplosion.DoExplosion(center, map, 1.5f, damage, null,
                damAmount: damage.defaultDamage, armorPenetration: armorPenetration,
                chanceToStartFire: 0f, damageFalloff: false,
                applyDamageToExplosionCellsNeighbors: false, doVisualEffects: false,
                overrideCells: Cells(map, center));
        }
    }
}
