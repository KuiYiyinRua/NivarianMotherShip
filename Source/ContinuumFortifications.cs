using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using RimWorld.BaseGen;
using UnityEngine;
using Verse;
using Verse.AI.Group;

namespace NivarianCommander
{
    public enum ContinuumFormation { Ring, Square, Compound, EightDirections }

    // Coordinates are generated in quarter-turn groups. Cover is added last, so
    // it cannot wipe a multi-cell gun, a mortar's interaction cell or supplies.
    internal sealed class ContinuumFortifications
    {
        readonly Map map;
        readonly Faction faction;
        readonly ContinuumBattlefield state;
        readonly int stage, outerRadius, middleRadius, innerRadius;
        readonly IntVec3 center;
        readonly ContinuumFormation formation;
        readonly HashSet<IntVec3> reserved = new HashSet<IntVec3>();
        readonly List<Thing> emplacements = new List<Thing>();
        readonly List<Thing> powerPlants = new List<Thing>();
        int mannedMortars;

        ContinuumFortifications(Map map, int stage, ContinuumBattlefield state, Faction faction)
        {
            this.map = map; this.stage = Mathf.Clamp(stage, 1, 8);
            this.state = state; this.faction = faction; center = map.Center;
            formation = (ContinuumFormation)((this.stage - 1) % 4);
            outerRadius = Math.Min(30 + this.stage / 2, Math.Min(map.Size.x, map.Size.z) / 2 - 6);
            innerRadius = Math.Max(12, outerRadius - 16);
            middleRadius = (outerRadius + innerRadius) / 2;
        }

        public static void Generate(Map map, int stage, ContinuumBattlefield state, Faction faction)
            => new ContinuumFortifications(map, stage, state, faction).Build();

        void Build()
        {
            var area = CellRect.CenteredOn(center, outerRadius + 4).ClipInsideMap(map);
            foreach (var c in area)
            {
                foreach (var thing in c.GetThingList(map).ToArray())
                    if (!(thing is Pawn)) thing.Destroy(DestroyMode.Vanish);
                map.roofGrid.SetRoof(c, null);
                map.terrainGrid.SetTerrain(c, TerrainDefOf.Concrete);
            }

            // The relay and recoverable loot occupy the centre, not the firing
            // perimeter. There is deliberately no roof above either mortar type.
            state.relay = Place("NMC_ContinuumRelay", center);
            foreach (var offset in new[] { new IntVec3(6, 0, 4), new IntVec3(-6, 0, -4) })
                powerPlants.Add(Place("NMC_ContinuumPowerCore", center + offset));
            if (stage >= 4)
                foreach (var offset in new[] { new IntVec3(-6, 0, 4), new IntVec3(6, 0, -4) })
                    powerPlants.Add(Place("NMC_ContinuumPowerCore", center + offset));

            Stock("Silver", 150 + stage * 100, new IntVec3(-3, 0, 3));
            Stock("ComponentIndustrial", 3 + stage * 2, new IntVec3(3, 0, 3));
            Stock("Plasteel", 15 + stage * 10, new IntVec3(-3, 0, -3));
            Stock("Nivarian_DragonfrostSteel", 15 + stage * 10, new IntVec3(3, 0, -3));
            if (stage >= 5) Stock("ComponentSpacer", stage - 3, new IntVec3(0, 0, -3));

            Gun("Turret_Mortar", center + new IntVec3(0, 0, 7));
            Gun("Turret_CryoMortar", center + new IntVec3(0, 0, -7));
            if (stage >= 4)
            {
                Gun("Turret_Mortar", center + new IntVec3(7, 0, 0));
                Gun("Turret_CryoMortar", center + new IntVec3(-7, 0, 0));
            }
            Stock("Shell_HighExplosive", 8 + stage * 4, new IntVec3(-4, 0, 0));
            Stock("Nivarian_CryoCoolant", 30 + stage * 15, new IntVec3(4, 0, 0));
            Stock("ReinforcedBarrel", 2 + stage / 3, new IntVec3(-3, 0, -1));
            Stock("Uranium", 40 + stage * 15, new IntVec3(3, 0, -1));
            Stock("Steel", 80 + stage * 20, new IntVec3(-3, 0, 1));
            if (stage >= 3) Stock("Chemfuel", 180 + stage * 30, new IntVec3(3, 0, 1));

            // First encounter has two opposed heavy guns; later tiers add
            // quarter-symmetric groups without changing the native gun stats.
            int innerCount = stage == 1 ? 2 : stage <= 3 ? 4 : stage <= 5 ? 8 : 12;
            foreach (var slot in Slots(innerRadius, innerCount, 0f))
            {
                string name;
                if (stage < 3) name = "Turret_Sniper";
                else if (stage == 3) name = slot.index % 2 == 0 ? "Turret_Sniper" : "Turret_RocketswarmLauncher";
                else if (innerCount == 8)
                    name = slot.index % 2 == 0 ? "Turret_NivarianWinter"
                        : slot.index % 4 == 1 ? "Turret_Sniper" : "Turret_RocketswarmLauncher";
                else name = slot.index % 3 == 0 ? "Turret_NivarianWinter"
                    : slot.index % 3 == 1 ? "Turret_Sniper" : "Turret_RocketswarmLauncher";
                Gun(name, slot.cell);
            }

            int middleCount = stage < 4 ? 4 : 8;
            foreach (var slot in Slots(middleRadius, middleCount, 45f))
            {
                bool strafing = stage >= 3 && (middleCount == 4 ? slot.index % 2 == 1 : slot.index % 4 == 2);
                string name = stage >= 4 && slot.index % 2 == 1 ? "Turret_Sniper"
                    : strafing ? "Nivarian_DroneHub_AttackerStrafing" : "Nivarian_DroneHub";
                Gun(name, slot.cell);
            }
            int outerCount = stage == 1 ? 4 : stage <= 3 ? 8 : stage <= 5 ? 12 : 16;
            foreach (var slot in Slots(outerRadius, outerCount, 0f))
            {
                string name = slot.index % 2 == 0 ? "Turret_Cryo" : "Turret_MiniTurret";
                Gun(name, slot.cell);
            }

            // One continuous network, with no duplicate transmitter below a
            // power plant. Its capacity scales with the larger late bases.
            foreach (var plant in powerPlants) Wire(center, plant.Position);
            foreach (var gun in emplacements) Wire(center, gun.Position);
            foreach (var gun in emplacements) CoverGun(gun);
            if (formation != ContinuumFormation.Compound)
                CoverRing(outerRadius + 3);
            CoverRing(innerRadius + 4);
            if (formation == ContinuumFormation.EightDirections) CoverRing(middleRadius + 3);
            if (formation == ContinuumFormation.Compound)
                foreach (var gun in emplacements.Where(t => Radius(t.Position - center) >= outerRadius - 1))
                    CoverPocket(gun);

            var guards = ContinuumSiteGenerator.MakeGuards(stage, 2 + stage * 2, faction);
            foreach (var pawn in guards)
                GenSpawn.Spawn(pawn, CellFinder.RandomClosewalkCellNear(center, map, innerRadius), map);
            state.defenders.AddRange(guards);
            LordMaker.MakeNewLord(faction, new LordJob_DefendBase(faction, center, 1200), map, guards);
            var crew = new List<Pawn>();
            for (int i = 0; i < mannedMortars + 2; i++)
            {
                // MakeGuards' first member is the native, work-capable Nira.
                var pawn = ContinuumSiteGenerator.MakeGuards(stage, 1, faction)[0];
                GenSpawn.Spawn(pawn, CellFinder.RandomClosewalkCellNear(center, map, 5), map);
                crew.Add(pawn);
            }
            state.defenders.AddRange(crew);
            LordMaker.MakeNewLord(faction, new LordJob_ContinuumArtillery(center), map, crew);
            BaseGen.globalSettings.map = map;
            BaseGen.symbolStack.Push("ensureCanReachMapEdge", new ResolveParams { rect = area, faction = faction });
            BaseGen.Generate();
            MapGenerator.SetVar("RectOfInterest", area);
            MapGenerator.GetOrGenerateVar<List<CellRect>>("UsedRects").Add(area);
        }

        IEnumerable<(IntVec3 cell, int index)> Slots(int radius, int count, float offset)
        {
            if (count == 2)
            {
                yield return (center + new IntVec3(0, 0, radius), 0);
                yield return (center - new IntVec3(0, 0, radius), 1);
                yield break;
            }
            int perQuadrant = count / 4;
            for (int i = 0; i < count; i++)
            {
                float angle = (offset + (i % perQuadrant) * 90f / perQuadrant) * Mathf.Deg2Rad;
                float x = Mathf.Sin(angle), z = Mathf.Cos(angle);
                if (formation == ContinuumFormation.Square || formation == ContinuumFormation.Compound)
                {
                    float longest = Mathf.Max(Mathf.Abs(x), Mathf.Abs(z));
                    x /= longest; z /= longest;
                }
                else if (formation == ContinuumFormation.EightDirections)
                {
                    float edge = Mathf.Max(Mathf.Max(Mathf.Abs(x), Mathf.Abs(z)), (Mathf.Abs(x) + Mathf.Abs(z)) * 0.70710678f);
                    x /= edge; z /= edge;
                }
                var delta = new IntVec3(Mathf.RoundToInt(x * radius), 0, Mathf.RoundToInt(z * radius));
                for (int q = 0; q < i / perQuadrant; q++) delta = new IntVec3(delta.z, 0, -delta.x);
                yield return (center + delta, i);
            }
        }

        float Radius(IntVec3 delta)
        {
            int x = Math.Abs(delta.x), z = Math.Abs(delta.z);
            if (formation == ContinuumFormation.Square || formation == ContinuumFormation.Compound)
                return Math.Max(x, z);
            if (formation == ContinuumFormation.EightDirections)
                return Mathf.Max(Mathf.Max(x, z), (x + z) * 0.70710678f);
            return Mathf.Sqrt(x * x + z * z);
        }

        static float Facing(IntVec3 delta) => Mathf.Repeat(Mathf.Atan2(delta.x, delta.z) * Mathf.Rad2Deg, 360f);
        static Rot4 Rotation(IntVec3 delta) => new Rot4(Mathf.FloorToInt((Facing(delta) + 45f) / 90f) % 4);

        Thing Place(string name, IntVec3 cell, Rot4? rotation = null)
        {
            var def = DefDatabase<ThingDef>.GetNamed(name);
            Rot4 rot = rotation ?? Rot4.North;
            var rect = GenAdj.OccupiedRect(cell, rot, def.size);
            if (rect.Any(c => !c.InBounds(map) || reserved.Contains(c)))
                throw new InvalidOperationException("Continuum emplacement overlaps reserved cells: " + name + " at " + cell);
            var thing = ThingMaker.MakeThing(def, def.MadeFromStuff ? ThingDefOf.Steel : null);
            if (def.CanHaveFaction) thing.SetFaction(faction);
            GenSpawn.Spawn(thing, cell, map, rot, WipeMode.Vanish);
            foreach (var c in thing.OccupiedRect()) reserved.Add(c);
            if (def.hasInteractionCell) reserved.Add(thing.InteractionCell);
            var fuel = thing.TryGetComp<CompRefuelable>();
            if (fuel != null) fuel.Refuel(fuel.Props.fuelCapacity);
            return thing;
        }

        void Gun(string name, IntVec3 cell)
        {
            var gun = Place(name, cell, Rotation(cell - center));
            state.defenders.Add(gun); emplacements.Add(gun);
            if (gun.TryGetComp<CompMannable>() != null) mannedMortars++;
            if (gun is Building_TurretGun turret) turret.Top.CurRotation = Facing(cell - center);
            if (name == "Turret_NivarianWinter")
            {
                // Winter is rotatable=false. Its native cone reads this Scribed
                // state rather than Rot4; set the completed emplacement direction
                // once during generation, never while loading or during a tick.
                float angle = Facing(cell - center);
                SetWinterField(gun, "coneFacingDegrees", angle);
                SetWinterField(gun, "previewConeFacingDegrees", angle);
                SetWinterField(gun, "coneAdjustmentStartRotation", angle);
                SetWinterField(gun, "hasConeFacingPreview", false);
                SetWinterField(gun, "coneAdjustmentTicksLeft", 0);
            }
        }

        static void SetWinterField(Thing gun, string name, object value)
        {
            var field = AccessTools.Field(gun.GetType(), name);
            if (field == null || field.FieldType != value.GetType())
                throw new MissingFieldException(gun.GetType().FullName, name);
            field.SetValue(gun, value);
        }

        void Stock(string name, int count, IntVec3 offset)
        {
            var def = DefDatabase<ThingDef>.GetNamed(name);
            var start = center + offset;
            // Supplies remain normal finite, stealable items. Nothing restores
            // stock on load or creates ammunition when a gun fires.
            int index = 0;
            while (count > 0)
            {
                IntVec3 cell = start;
                while (!cell.InBounds(map) || reserved.Contains(cell))
                    cell = start + GenRadial.RadialPattern[++index];
                var thing = ThingMaker.MakeThing(def);
                thing.stackCount = Math.Min(def.stackLimit, count); count -= thing.stackCount;
                GenSpawn.Spawn(thing, cell, map, WipeMode.Vanish);
                reserved.Add(cell);
            }
        }

        void CoverGun(Thing gun)
        {
            var occupied = gun.OccupiedRect();
            var outward = (gun.Position - center).ToVector3().normalized;
            foreach (var c in occupied.ExpandedBy(1).EdgeCells)
            {
                // Open the inward edge for the crew and refuelling route.
                if (Vector3.Dot((c - gun.Position).ToVector3(), outward) < -0.75f) continue;
                Sandbag(c);
            }
        }

        void CoverPocket(Thing gun)
        {
            foreach (var c in CellRect.CenteredOn(gun.Position, 3).EdgeCells)
            {
                var inward = center - gun.Position;
                if (Vector3.Dot((c - gun.Position).ToVector3(), inward.ToVector3().normalized) > 1f) continue;
                Sandbag(c);
            }
        }

        void CoverRing(int radius)
        {
            foreach (var c in CellRect.CenteredOn(center, radius + 1).ClipInsideMap(map))
            {
                var delta = c - center;
                if (Math.Abs(delta.x) <= 2 || Math.Abs(delta.z) <= 2) continue;
                if (formation == ContinuumFormation.EightDirections && Math.Abs(Math.Abs(delta.x) - Math.Abs(delta.z)) <= 1) continue;
                if (Radius(delta) > radius) continue;
                if (GenAdj.CardinalDirections.All(dir => Radius(delta + dir) <= radius)) continue;
                Sandbag(c);
            }
        }

        void Sandbag(IntVec3 cell)
        {
            if (!cell.InBounds(map) || reserved.Contains(cell) || cell.GetEdifice(map) != null) return;
            var def = DefDatabase<ThingDef>.GetNamed("Sandbags");
            var cover = ThingMaker.MakeThing(def, def.MadeFromStuff ? ThingDefOf.Cloth : null);
            cover.SetFaction(faction); GenSpawn.Spawn(cover, cell, map, WipeMode.Vanish);
            reserved.Add(cell);
        }

        void Wire(IntVec3 from, IntVec3 to)
        {
            var current = from;
            while (true)
            {
                if (!current.GetThingList(map).Any(t => t.TryGetComp<CompPower>()?.Props.transmitsPower == true))
                {
                    var conduit = ThingMaker.MakeThing(ThingDefOf.PowerConduit);
                    conduit.SetFaction(faction); GenSpawn.Spawn(conduit, current, map, WipeMode.Vanish);
                }
                if (current == to) break;
                current += current.x != to.x ? new IntVec3(Math.Sign(to.x - current.x), 0, 0)
                    : new IntVec3(0, 0, Math.Sign(to.z - current.z));
            }
        }
    }
}
