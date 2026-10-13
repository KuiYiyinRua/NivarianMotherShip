using System;
using System.Collections.Generic;
using System.Linq;
using Nivarian.Helper;
using Nivarian_Race.Code.Defs;
using Nivarian_Race.Code.NivarianThing;
using Verse;

namespace NivarianCommander
{
    internal static class GroundReinforcementArrival
    {
        public static string Unavailable => PointShopDelivery.NativeSupplyAvailable ? null : NmcText.Get("NMC_WarpDropUnavailable");
        public static void Send(Map map, IntVec3 cell, List<Pawn> members)
        {
            if (Unavailable != null || map == null || !cell.InBounds(map))
                throw new InvalidOperationException("Native reinforcement drop chain unavailable");
            var supply = DefDatabase<NivarianMotherShipRequestDef>.GetNamed("Request_Droppod_OrbitFood");
            NivarianHelper.ScheduleNivarianLotusDropPod(map, cell, members.Cast<Thing>().ToList(),
                supply.droppodMarkerDelayTicks, supply.droppodOpenDelaySeconds,
                NivarianLotusDropPodDefOf.NivarianLotusDropMarker,
                NivarianLotusDropPodDefOf.NivarianLotusDropPodIncoming,
                NivarianLotusDropPodDefOf.NivarianLotusDropPodLanded);
            if (members.Any(p => p.Spawned || !(p.ParentHolder is NivarianLotusDropMarker)))
                throw new InvalidOperationException("Native marker did not take ownership of the reinforcement squad");
        }
    }
}
