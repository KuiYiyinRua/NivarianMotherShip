using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Nivarian.NivarianDrones;
using RimWorld;
using Verse;

namespace NivarianCommander
{
    public sealed partial class FieldEnergy
    {
        static readonly Comparison<Thing> ByThingId = (a, b) => a.thingIDNumber.CompareTo(b.thingIDNumber);
        static readonly Comparison<CompPowerTrader> ByPowerId = (a, b) => a.parent.thingIDNumber.CompareTo(b.parent.thingIDNumber);
        readonly HashSet<IntVec3> scannedCells = new HashSet<IntVec3>();
        readonly List<Building> nearbyBuildings = new List<Building>();
        readonly List<Thing> nearbyChargers = new List<Thing>();
        readonly List<Demand> powerRequests = new List<Demand>();
        readonly List<CompPowerTrader> offGridWork = new List<CompPowerTrader>();
        readonly HashSet<PowerNet> powerNetsWork = new HashSet<PowerNet>();
        readonly List<CompPowerTrader> nativeWirelessWork = new List<CompPowerTrader>();
        readonly List<CompPowerTrader> suppliedPowerWork = new List<CompPowerTrader>();
        readonly HashSet<ThingWithComps> ownedWirelessWork = new HashSet<ThingWithComps>();
        List<ThingWithComps> nextWireless = new List<ThingWithComps>();
        readonly ConditionalWeakTable<PowerNet, NetworkWork> networkWork = new ConditionalWeakTable<PowerNet, NetworkWork>();

        sealed class NetworkWork
        {
            internal readonly Demand demand = new Demand();
            CompPowerTrader[] nativeOrder = Array.Empty<CompPowerTrader>();
            CompPowerTrader[] sorted = Array.Empty<CompPowerTrader>();
            internal CompPower connector;
            internal int connectorIndex;
            internal bool transmitter;

            internal CompPowerTrader[] Ordered(PowerNet net)
            {
                var current = net.powerComps;
                bool changed = nativeOrder.Length != current.Count;
                if (!changed)
                    for (int i = 0; i < current.Count; i++)
                        if (nativeOrder[i] != current[i]) { changed = true; break; }
                if (changed)
                {
                    nativeOrder = current.ToArray();
                    var replacement = (CompPowerTrader[])nativeOrder.Clone();
                    Array.Sort(replacement, ByPowerId);
                    // Publish a new snapshot. A nested power getter must not mutate
                    // the array an outer native callback is already enumerating.
                    sorted = replacement;
                }
                return sorted;
            }
        }
        static NetworkWork NewNetworkWork(PowerNet net) => new NetworkWork();
        NetworkWork WorkFor(PowerNet net) => networkWork.GetValue(net, NewNetworkWork);

        // Enumerate the same anchor-position membership as CommanderField.Contains.
        // ThingGrid also lists multi-cell objects in their other occupied cells;
        // accepting only their anchor removes duplicates without missing large hulls.
        void CollectFieldThings(bool charging)
        {
            scannedCells.Clear();
            if (charging) nearbyChargers.Clear(); else nearbyBuildings.Clear();
            var currentField = CommanderField.Current;
            if (currentField == null) return;
            if (currentField.fieldMap == map && currentField.center.IsValid)
                CollectCircle(currentField.center, 8f, charging);
            foreach (var ship in WarpPrismFields.For(map).ActiveShips)
                CollectCircle(ship.Position, WarpPrismFields.Radius, charging);
            if (charging) nearbyChargers.Sort(ByThingId);
        }
        void CollectCircle(IntVec3 center, float radius, bool charging)
        {
            int count = GenRadial.NumCellsInRadius(radius);
            for (int i = 0; i < count; i++)
            {
                var cell = center + GenRadial.RadialPattern[i];
                if (!cell.InBounds(map) || !scannedCells.Add(cell)) continue;
                var things = cell.GetThingList(map);
                for (int j = 0; j < things.Count; j++)
                {
                    var thing = things[j];
                    if (thing.Position != cell) continue;
                    if (charging)
                    { if (thing is Pawn || thing is NivarianDroneBase) nearbyChargers.Add(thing); }
                    else if (thing is Building building) nearbyBuildings.Add(building);
                }
            }
        }

        // Cache a witness, never an answer: membership, coverage, allegiance,
        // flick, breakdown and EMP are all checked live on every query. When a
        // witness changes or becomes invalid, retain the original ordered scan.
        bool LiveConnector(PowerNet net, CommanderField currentField)
        {
            var work = WorkFor(net);
            var list = work.transmitter ? net.transmitters : net.connectors;
            if (work.connector != null && work.connectorIndex < list.Count
                && list[work.connectorIndex] == work.connector && WorkingConnector(work.connector, currentField)) return true;
            for (int i = 0; i < net.transmitters.Count; i++)
                if (WorkingConnector(net.transmitters[i], currentField))
                { work.connector = net.transmitters[i]; work.connectorIndex = i; work.transmitter = true; return true; }
            for (int i = 0; i < net.connectors.Count; i++)
                if (WorkingConnector(net.connectors[i], currentField))
                { work.connector = net.connectors[i]; work.connectorIndex = i; work.transmitter = false; return true; }
            work.connector = null;
            return false;
        }
    }
}
