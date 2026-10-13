using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI.Group;

namespace NivarianCommander
{
    public static class FieldRecallContext
    {
        // Optional multiplayer integration owns cross-map timestamp conversion.
        public static Action<Pawn, Map, Map, Action> SpawnAcrossMaps = (pawn, source, destination, spawn) => spawn();
        public static Action<Thing, Map, Map> AdjustShipPassengers = (ship, source, destination) => { };
    }

    public static class FieldRecallActions
    {
        public const string ResearchDefName = "NMC_FieldRecall";
        public static bool ResearchFinished => CommanderResearch.Finished(ResearchDefName);
        public static bool Eligible(Pawn pawn)
            => pawn != null && !pawn.Destroyed && !pawn.Dead && pawn.Spawned && pawn.IsColonist && pawn.Faction == Faction.OfPlayer;

        public static string Unavailable(Pawn pawn)
        {
            if (!ResearchFinished) return NmcText.Get("NMC_Text0245");
            if (!Eligible(pawn)) return NmcText.Get("NMC_Text0246");
            if (FieldWarp.Pending(pawn)) return NmcText.Get("NMC_WarpBusy");
            if (!FieldEconomy.HasField && !WarpPrismFields.AnyDestination) return NmcText.Get("NMC_Text0247");
            return null;
        }

        // Pure lookup, nearest first. Never consume random state while drawing a Gizmo.
        internal static IntVec3 LandingCell(Thing pawn, Map destination, IntVec3 center, float radius = FieldGroundGeometry.Radius)
        {
            bool flying = ShipFieldRecallActions.InFlight(pawn);
            foreach (var cell in GenRadial.RadialCellsAround(center, radius, true))
            {
                bool clear = true;
                foreach (var occupied in GenAdj.OccupiedRect(cell, pawn.Rotation, pawn.def.Size))
                {
                    if (!occupied.InBounds(destination) || !occupied.InHorDistOf(center, radius))
                    { clear = false; break; }
                    if (flying)
                    {
                        // Local flight already crosses terrain, roofs and ground structures.
                        // Keep only map/field bounds, visibility and occupied airspace checks.
                        if (occupied.Fogged(destination)) { clear = false; break; }
                        foreach (var thing in occupied.GetThingList(destination))
                            if (thing != pawn && InFlightObstacle(thing)) { clear = false; break; }
                        if (!clear) break;
                        continue;
                    }
                    if (!occupied.Standable(destination)
                        || pawn is Building_NivarianAircraft && (occupied.Roofed(destination) || occupied.Fogged(destination)
                            || !occupied.GetAffordances(destination).Contains(pawn.def.terrainAffordanceNeeded)))
                    { clear = false; break; }
                    foreach (var thing in occupied.GetThingList(destination))
                        if (thing != pawn && (thing is Pawn || thing is Building || thing is Fire
                            || thing is FleetShip || thing is Skyfaller || thing is IActiveTransporter
                            || pawn is Building_NivarianAircraft && thing.def.plant?.IsTree == true))
                        { clear = false; break; }
                    if (!clear) break;
                }
                if (clear) return cell;
            }
            return IntVec3.Invalid;
        }
        static bool InFlightObstacle(Thing thing)
            => ShipFieldRecallActions.InFlight(thing) || thing is Skyfaller || thing is IActiveTransporter;

        static void Reject(string reason, Pawn pawn)
            => Messages.Message(reason, pawn, MessageTypeDefOf.RejectInput, false);

        // Only this executor crosses the MP boundary. UI never moves or despawns a pawn.
        public static void Recall(Map source, Pawn pawn, int expectedRevision)
        {
            string reason = Unavailable(pawn);
            if (reason != null) { Reject(reason, pawn); return; }
            if (source == null || !Find.Maps.Contains(source) || pawn.Map != source)
            { Reject(NmcText.Get("NMC_Text0248"), pawn); return; }
            var field = CommanderField.Current;
            if (!FieldEconomy.HasField) { Reject(NmcText.Get("NMC_Text0247"), pawn); return; }
            if (field.revision != expectedRevision)
            { Reject(NmcText.Get("NMC_Text0249"), pawn); return; }
            var destination = field.fieldMap;
            var landing = LandingCell(pawn, destination, field.center);
            if (!landing.IsValid) { Reject(NmcText.Get("NMC_Text0250"), pawn); return; }
            FieldWarp.Begin(pawn, destination, field.center, FieldGroundGeometry.Radius, expectedRevision);
        }

        internal static void Teleport(Map source, Pawn pawn, Map destination, IntVec3 landing,
            Building_NivarianAircraft prism = null)
        {
            if (source == null || pawn?.Map != source || !Eligible(pawn) || FieldWarp.Pending(pawn)) return;
            if (prism != null)
                FieldWarp.Begin(pawn, destination, prism.Position, WarpPrismFields.Radius, prism.warpFieldRevision, prism);
            else
            {
                var field = CommanderField.Current;
                if (field != null && field.Exists && field.fieldMap == destination)
                    FieldWarp.Begin(pawn, destination, field.center, FieldGroundGeometry.Radius, field.revision);
            }
        }

        internal static bool CompleteTeleport(Map source, Pawn pawn, Map destination, IntVec3 landing)
        {
            if (source == destination && pawn.Position == landing)
            { Reject(NmcText.Get("NMC_Text0251"), pawn); return false; }
            // Hauling targets belong to the old job/map. Equipment and inventory stay on the pawn.
            if (pawn.carryTracker?.CarriedThing != null
                && !pawn.carryTracker.TryDropCarriedThing(pawn.Position, ThingPlaceMode.Direct, out Thing dropped))
            { Reject(NmcText.Get("NMC_Text0252"), pawn); return false; }

            var origin = pawn.Position;
            var rotation = pawn.Rotation;
            bool drafted = pawn.Drafted, fireAtWill = pawn.drafter?.FireAtWill ?? true;
            try
            {
                pawn.GetLord()?.Notify_PawnLost(pawn, PawnLostCondition.ForcedByPlayerAction);
                pawn.jobs?.StopAll();
                pawn.pather?.StopDead();
                pawn.ClearAllReservations();
                if (source == destination)
                {
                    pawn.Position = landing;
                    pawn.Notify_Teleported();
                }
                else
                {
                    // Use the native despawn/spawn lifecycle for map lists, job cleanup and visuals.
                    // Its two halves run in their owning map's time and random context.
                    pawn.DeSpawn();
                    ProjectionContext.Run(destination, () => FieldRecallContext.SpawnAcrossMaps(pawn, source, destination, () =>
                    {
                        GenSpawn.Spawn(pawn, landing, destination, rotation);
                        pawn.Notify_Teleported();
                        RestoreDraft(pawn, drafted, fireAtWill, rotation);
                    }));
                }
                Messages.Message(NmcText.Get("NMC_Text0253", pawn.LabelShortCap), pawn, MessageTypeDefOf.PositiveEvent, false);
                return true;
            }
            catch (Exception error)
            {
                // A failed spawn must not leave the colonist unowned between maps.
                if (!pawn.Spawned && !pawn.Destroyed && Find.Maps.Contains(source))
                {
                    ProjectionContext.Run(source, () =>
                    {
                        GenSpawn.Spawn(pawn, origin, source, rotation);
                        RestoreDraft(pawn, drafted, fireAtWill, rotation);
                        pawn.Notify_Teleported();
                    });
                }
                Log.Error("[NMC] Field recall failed for " + pawn.ThingID + ": " + error);
                Reject(pawn.Spawned && pawn.Map == destination ? NmcText.Get("NMC_Text0254") : NmcText.Get("NMC_Text0255"), pawn);
                return false;
            }
        }

        static void RestoreDraft(Pawn pawn, bool drafted, bool fireAtWill, Rot4 rotation)
        {
            pawn.Rotation = rotation;
            if (pawn.drafter == null) return;
            pawn.drafter.Drafted = drafted;
            pawn.drafter.FireAtWill = fireAtWill;
        }
    }

    internal sealed class Command_FieldRecall : Command_Action
    {
        public override bool GroupsWith(Gizmo other)
            => other is Command_FieldRecall command && disabled == command.disabled && base.GroupsWith(other);
    }

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.GetGizmos))]
    public static class FieldRecallGizmos
    {
        static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> __result, Pawn __instance)
        {
            foreach (var gizmo in __result) yield return gizmo;
            if (!FieldRecallActions.ResearchFinished || !FieldRecallActions.Eligible(__instance)) yield break;
            var source = __instance.Map;
            var revision = CommanderField.Current?.revision ?? -1;
            var command = new Command_FieldRecall
            {
                defaultLabel = NmcText.Get("NMC_Text0256"),
                defaultDesc = NmcText.Get("NMC_WarpRecallDescription"),
                icon = ContentFinder<Texture2D>.Get("NMC/Supports/warp_energy_field"),
                action = () => WarpPrismActions.ChooseDestination(source, __instance, revision),
                alsoClickIfOtherInGroupClicked = false
            };
            var reason = FieldRecallActions.Unavailable(__instance);
            if (reason != null) command.Disable(reason);
            yield return command;
        }
    }
}
