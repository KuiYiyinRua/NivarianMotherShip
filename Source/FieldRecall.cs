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
            if (!FieldEconomy.HasField) return NmcText.Get("NMC_Text0247");
            return null;
        }

        // Pure lookup, nearest first. Never consume random state while drawing a Gizmo.
        static IntVec3 LandingCell(Pawn pawn, Map destination, IntVec3 center)
        {
            foreach (var cell in GenRadial.RadialCellsAround(center, FieldGroundGeometry.Radius, true))
            {
                bool clear = true;
                foreach (var occupied in GenAdj.OccupiedRect(cell, pawn.Rotation, pawn.def.Size))
                {
                    if (!occupied.InBounds(destination) || !occupied.Standable(destination)
                        || !occupied.InHorDistOf(center, FieldGroundGeometry.Radius)) { clear = false; break; }
                    foreach (var thing in occupied.GetThingList(destination))
                        if (thing != pawn && (thing is Pawn || thing is Building || thing is Fire))
                        { clear = false; break; }
                    if (!clear) break;
                }
                if (clear) return cell;
            }
            return IntVec3.Invalid;
        }

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
            if (field.revision != expectedRevision)
            { Reject(NmcText.Get("NMC_Text0249"), pawn); return; }
            var destination = field.fieldMap;
            var landing = LandingCell(pawn, destination, field.center);
            if (!landing.IsValid) { Reject(NmcText.Get("NMC_Text0250"), pawn); return; }
            if (source == destination && pawn.Position == landing)
            { Reject(NmcText.Get("NMC_Text0251"), pawn); return; }
            // Hauling targets belong to the old job/map. Equipment and inventory stay on the pawn.
            if (pawn.carryTracker?.CarriedThing != null
                && !pawn.carryTracker.TryDropCarriedThing(pawn.Position, ThingPlaceMode.Direct, out Thing dropped))
            { Reject(NmcText.Get("NMC_Text0252"), pawn); return; }

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
                    EntryEffect(pawn, source);
                    pawn.Position = landing;
                    pawn.Notify_Teleported();
                    ExitEffect(pawn, destination);
                }
                else
                {
                    // Use the native despawn/spawn lifecycle for map lists, job cleanup and visuals.
                    // Its two halves run in their owning map's time and random context.
                    SkipUtility.SkipDeSpawn(pawn);
                    ProjectionContext.Run(destination, () => FieldRecallContext.SpawnAcrossMaps(pawn, source, destination, () =>
                    {
                        SkipUtility.SkipTo(pawn, landing, destination);
                        RestoreDraft(pawn, drafted, fireAtWill, rotation);
                    }));
                }
                Messages.Message(NmcText.Get("NMC_Text0253", pawn.LabelShortCap), pawn, MessageTypeDefOf.PositiveEvent, false);
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
            }
        }

        static void RestoreDraft(Pawn pawn, bool drafted, bool fireAtWill, Rot4 rotation)
        {
            pawn.Rotation = rotation;
            if (pawn.drafter == null) return;
            pawn.drafter.Drafted = drafted;
            pawn.drafter.FireAtWill = fireAtWill;
        }
        static void EntryEffect(Pawn pawn, Map map)
            => DefDatabase<EffecterDef>.GetNamedSilentFail("Skip_EntryNoDelay")?.Spawn(pawn, map).Cleanup();
        static void ExitEffect(Pawn pawn, Map map)
            => DefDatabase<EffecterDef>.GetNamedSilentFail("Skip_Exit")?.Spawn(pawn, map).Cleanup();
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
                defaultDesc = NmcText.Get("NMC_Text0257"),
                icon = ContentFinder<Texture2D>.Get("NMC/Supports/warp_energy_field"),
                action = () => FieldRecallActions.Recall(source, __instance, revision),
                alsoClickIfOtherInGroupClicked = true
            };
            var reason = FieldRecallActions.Unavailable(__instance);
            if (reason != null) command.Disable(reason);
            yield return command;
        }
    }
}
