using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace NivarianCommander
{
    public static class CaravanFieldRecall
    {
        public static bool Eligible(Caravan caravan)
            => caravan != null && caravan.Spawned && !caravan.Destroyed
                && caravan.Faction == Faction.OfPlayer && caravan.PawnsListForReading.Count > 0;

        public static string Unavailable(Caravan caravan)
        {
            if (!FieldRecallActions.ResearchFinished) return NmcText.Get("NMC_Text0245");
            if (!Eligible(caravan)) return NmcText.Get("NMC_CaravanWarpUnavailable");
            if (!FieldEconomy.HasField && !WarpPrismFields.AnyDestination) return NmcText.Get("NMC_Text0247");
            return null;
        }

        public static void ChooseDestination(Caravan caravan)
        {
            if (!Eligible(caravan)) return;
            var options = new List<FloatMenuOption>();
            var field = CommanderField.Current;
            if (FieldEconomy.HasField)
            {
                int mapId = field.fieldMap.uniqueID, revision = field.revision;
                options.Add(new FloatMenuOption(NmcText.Get("NMC_WarpPrimaryDestination") + " — " + field.fieldMap.Parent.LabelCap,
                    () => Recall(caravan.ID, mapId, -1, revision)));
            }
            foreach (var map in Find.Maps.OrderBy(m => m.uniqueID))
                foreach (var prism in WarpPrismFields.For(map).ActiveShips.OrderBy(p => p.thingIDNumber))
                {
                    int mapId = map.uniqueID, prismId = prism.thingIDNumber, revision = prism.warpFieldRevision;
                    options.Add(new FloatMenuOption(NmcText.Get("NMC_WarpPrismDestination", map.Parent.LabelCap,
                        prism.Position.x, prism.Position.z), () => Recall(caravan.ID, mapId, prismId, revision)));
                }
            if (options.Count == 0) Reject(NmcText.Get("NMC_Text0247"), caravan);
            else Find.WindowStack.Add(new FloatMenu(options));
        }

        static void Reject(string reason, Caravan caravan)
            => Messages.Message(reason, caravan, MessageTypeDefOf.RejectInput, false);

        // World command: resolve destination IDs only after synchronization. Do not
        // serialize a Map/Thing and accidentally move this action into a map queue.
        public static void Recall(int caravanId, int destinationMapId, int prismId, int expectedRevision)
        {
            var caravan = Find.WorldObjects.Caravans.FirstOrDefault(c => c.ID == caravanId);
            if (!Eligible(caravan)) return; // A repeated click after arrival is harmless.
            string reason = Unavailable(caravan);
            if (reason != null) { Reject(reason, caravan); return; }
            var map = Find.Maps.FirstOrDefault(m => m.uniqueID == destinationMapId);
            if (map == null) { Reject(NmcText.Get("NMC_WarpDestinationLost"), caravan); return; }
            IntVec3 center;
            float radius;
            if (prismId < 0)
            {
                var field = CommanderField.Current;
                if (!FieldEconomy.HasField || field.fieldMap != map || field.revision != expectedRevision)
                { Reject(NmcText.Get("NMC_WarpDestinationLost"), caravan); return; }
                center = field.center; radius = FieldGroundGeometry.Radius;
            }
            else
            {
                var prism = WarpPrismFields.For(map).ActiveShips.FirstOrDefault(p => p.thingIDNumber == prismId);
                if (prism == null || !prism.WarpFieldActive || prism.warpFieldRevision != expectedRevision)
                { Reject(NmcText.Get("NMC_WarpDestinationLost"), caravan); return; }
                center = prism.Position; radius = WarpPrismFields.Radius;
            }

            // Reserve a complete, non-overlapping layout before touching ownership.
            // Native Enter handles world-pawn removal, prisoners, animals and cargo.
            var pawns = caravan.PawnsListForReading.OrderBy(p => p.thingIDNumber).ToList();
            var cells = new Dictionary<Pawn, IntVec3>();
            var reserved = new HashSet<IntVec3>();
            foreach (var pawn in pawns)
            {
                var cell = FieldRecallActions.LandingCell(pawn, map, center, radius, reserved);
                if (!cell.IsValid) { Reject(NmcText.Get("NMC_Text0250"), caravan); return; }
                cells.Add(pawn, cell);
                foreach (var occupied in GenAdj.OccupiedRect(cell, pawn.Rotation, pawn.def.Size)) reserved.Add(occupied);
            }
            ProjectionContext.Run(map, () =>
            {
                CaravanEnterMapUtility.Enter(caravan, map, pawn => cells[pawn], CaravanDropInventoryMode.DoNotDrop);
                foreach (var pawn in pawns)
                {
                    pawn.Notify_Teleported();
                    map.GetComponent<FieldWarp>().orders.Add(new FieldWarpOrder {
                        subject = pawn, arriving = true, center = pawn.Position, startedTick = GenTicks.TicksGame });
                }
            });
        }
    }

    [HarmonyPatch(typeof(Caravan), nameof(Caravan.GetGizmos))]
    public static class CaravanFieldRecallGizmos
    {
        static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> __result, Caravan __instance)
        {
            foreach (var gizmo in __result) yield return gizmo;
            if (!FieldRecallActions.ResearchFinished || !CaravanFieldRecall.Eligible(__instance)) yield break;
            var command = new Command_Action {
                defaultLabel = NmcText.Get("NMC_Text0256"),
                defaultDesc = NmcText.Get("NMC_CaravanWarpDescription"),
                icon = ContentFinder<Texture2D>.Get("NMC/Supports/warp_energy_field"),
                action = () => CaravanFieldRecall.ChooseDestination(__instance),
                alsoClickIfOtherInGroupClicked = false
            };
            var reason = CaravanFieldRecall.Unavailable(__instance);
            if (reason != null) command.Disable(reason);
            yield return command;
        }
    }
}
