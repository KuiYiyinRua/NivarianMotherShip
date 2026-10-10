using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace NivarianCommander
{
    // A batch survives its leader and owns every temporary material reference.
    // Lifetime advances only in the originating map's actual simulation ticks.
    public sealed class FleetBatch : IExposable
    {
        public const int Duration = 7200;
        public int id, remaining, startedAt = -1;
        public int abilityNumber = 13, expectedMembers = 9;
        public Map origin;
        public bool committed, retiring;
        public List<FleetShip> ships = new List<FleetShip>();
        public List<int> memberIds = new List<int>();
        public List<bool> arrived = new List<bool>(), lost = new List<bool>();
        public List<Thing> materials = new List<Thing>();
        public int ArrivedCount => arrived.Count(v => v);
        public int LostBeforeArrivalCount => Enumerable.Range(0, lost.Count).Count(i => lost[i] && !arrived[i]);
        public bool Started => startedAt >= 0;
        public void TryStart()
        {
            // A killed entry cannot arrive. Settle it explicitly instead of
            // inventing a successful arrival or waiting forever for a dead ship.
            if (!committed || retiring || Started || ships.Count != expectedMembers) return;
            if (Enumerable.Range(0, expectedMembers).Any(i => !arrived[i] && !lost[i])) return;
            startedAt = GenTicks.TicksGame; remaining = Duration;
        }
        public void ExposeData()
        {
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                for (int i = 0; i < ships.Count; i++)
                    if (ships[i]?.Destroyed == true) ships[i] = null;
                materials.RemoveAll(t => t == null || t.Destroyed);
            }
            Scribe_Values.Look(ref id, "id");
            Scribe_Values.Look(ref abilityNumber, "abilityNumber", 13);
            Scribe_Values.Look(ref expectedMembers, "expectedMembers", 9);
            Scribe_Values.Look(ref committed, "committed");
            Scribe_Values.Look(ref remaining, "remaining");
            Scribe_Values.Look(ref startedAt, "startedAt", -1);
            Scribe_References.Look(ref origin, "origin");
            Scribe_Collections.Look(ref ships, "ships", LookMode.Reference);
            Scribe_Collections.Look(ref memberIds, "memberIds", LookMode.Value);
            Scribe_Collections.Look(ref arrived, "arrived", LookMode.Value);
            Scribe_Collections.Look(ref lost, "lostBeforeArrival", LookMode.Value);
            Scribe_Collections.Look(ref materials, "temporaryMaterials", LookMode.Reference);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                ships = ships ?? new List<FleetShip>();
                memberIds = memberIds ?? new List<int>();
                arrived = arrived ?? new List<bool>(); lost = lost ?? new List<bool>();
                materials = materials ?? new List<Thing>();
            }
        }
    }
    public sealed class CommanderFleets : WorldComponent
    {
        public List<FleetBatch> batches = new List<FleetBatch>();
        public int nextId = 1;
        public CommanderFleets(World world) : base(world) { }
        public static CommanderFleets Current => Find.World.GetComponent<CommanderFleets>();
        public FleetBatch Get(int id) => batches.FirstOrDefault(b => b.id == id);
        public FleetBatch Owner(Thing thing) => thing == null ? null : batches.FirstOrDefault(b => b.materials.Contains(thing));
        public void Track(FleetBatch batch, Thing thing)
        {
            if (batch == null || thing == null || thing.Destroyed) return;
            if (batch.retiring) { thing.Destroy(DestroyMode.Vanish); return; }
            if (!batch.materials.Contains(thing)) batch.materials.Add(thing);
        }
        public void Arrival(FleetShip ship)
        {
            var b = Get(ship.batchId);
            if (b == null || b.retiring || ship.slot < 0 || ship.slot >= b.arrived.Count) return;
            b.arrived[ship.slot] = true; b.TryStart();
        }
        public void Lost(FleetShip ship)
        {
            var b = Get(ship.batchId);
            if (b == null || ship.slot < 0 || ship.slot >= b.ships.Count) return;
            b.lost[ship.slot] = true; b.ships[ship.slot] = null; b.TryStart();
        }
        public void Retire(FleetBatch b)
        {
            if (b.retiring) return;
            b.retiring = true;
            foreach (var ship in b.ships.Where(s => s != null && !s.Destroyed).OrderBy(s => s.thingIDNumber).ToList())
            { ship.retiring = true; DestroyTemporary(ship); }
            // Destroy also removes a Thing from its native holdingOwner. This
            // reaches inventories, nested containers and other maps by identity.
            foreach (var thing in b.materials.Where(t => t != null && !t.Destroyed).OrderBy(t => t.thingIDNumber).ToList())
                DestroyTemporary(thing);
            b.materials.Clear(); b.ships.Clear(); batches.Remove(b);
        }
        static void DestroyTemporary(Thing thing)
        {
            if (thing.Destroyed) return;
            // A native minified wrapper has exactly one payload. Destroy that
            // wrapper too, or native Label/Graphic would dereference an empty
            // InnerThing after the payload is removed from its holdingOwner.
            Thing root = thing;
            while (root.ParentHolder is MinifiedThing wrapper && wrapper.InnerThing == root) root = wrapper;
            root.Destroy(DestroyMode.Vanish);
        }
        public void Tick(Map map)
        {
            foreach (var b in batches.Where(b => b.origin == map).OrderBy(b => b.id).ToList())
            {
                for (int i = 0; i < b.ships.Count; i++)
                {
                    var ship = b.ships[i];
                    if (ship == null || ship.Destroyed) { b.lost[i] = true; b.ships[i] = null; }
                    else if (ship.arrived) b.arrived[i] = true;
                }
                b.TryStart();
                if (b.Started && b.startedAt != GenTicks.TicksGame && --b.remaining <= 0) Retire(b);
            }
        }
        public void RemoveMap(Map map)
        { foreach (var b in batches.Where(b => b.origin == map).OrderBy(b => b.id).ToList()) Retire(b); }
        public override void WorldComponentTick()
        {
            foreach (var b in batches.Where(b => b.origin == null || !Find.Maps.Contains(b.origin)).OrderBy(b => b.id).ToList()) Retire(b);
        }
        public override void ExposeData()
        {
            Scribe_Values.Look(ref nextId, "fleetNextId", 1);
            Scribe_Collections.Look(ref batches, "fleetBatches", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit) batches = batches ?? new List<FleetBatch>();
        }
    }
    public sealed class FleetMap : MapComponent
    {
        public FleetMap(Map map) : base(map) { }
        public override void MapComponentTick() => CommanderFleets.Current.Tick(map);
        public override void MapRemoved() => CommanderFleets.Current.RemoveMap(map);
    }
}
