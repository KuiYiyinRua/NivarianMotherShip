using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using RimWorld;
using UnityEngine;
using Verse;

namespace NivarianCommander
{
    public sealed partial class Building_NivarianAircraft
    {
        public const int WarpModeDuration = 60;
        public bool warpFieldMode, warpFieldTarget;
        public int warpModeTicks, warpFieldRevision;
        public float warpFieldAngle;
        static Material warpFieldMaterial;
        public bool IsWarpPrism => def.defName == "NMC_WarpPrism";
        public bool WarpMovementLocked => IsWarpPrism && (warpFieldMode || warpModeTicks > 0);
        public bool CanMoveInMap => CombatFlight && !WarpMovementLocked;
        // The deployed hull and ground disk share one cell centre. Flight lift is
        // only a visual offset; ease it away over deployment, then restore it.
        float WarpFieldLiftFactor => !IsWarpPrism ? 1f : warpModeTicks > 0
            ? (warpFieldTarget ? warpModeTicks / (float)WarpModeDuration : 1f - warpModeTicks / (float)WarpModeDuration)
            : warpFieldMode ? 0f : 1f;
        public bool WarpFieldActive => IsWarpPrism && Spawned && !Destroyed && CombatFlight
            && warpFieldMode && warpModeTicks == 0 && (StarshipPowered || RefuelableComp.HasFuel)
            && Faction == Faction.OfPlayer;
        string WarpFieldInspectString => warpModeTicks > 0
            ? NmcText.Get("NMC_WarpChanging", (warpModeTicks / 60f).ToString("F1"))
            : NmcText.Get(warpFieldMode ? "NMC_WarpFieldStatus" : "NMC_WarpFlightStatus");

        internal void StartWarpMode(bool enable)
        {
            if (!IsWarpPrism || !CombatFlight || warpModeTicks != 0 || warpFieldMode == enable
                || !StarshipPowered && !RefuelableComp.HasFuel) return;
            // Cancel the previous flight order immediately. Neither mode transition
            // can move the ship; the destination field becomes usable after 60 ticks.
            destination = Position;
            if (enable) flightPosition = Position.ToVector3Shifted();
            warpFieldTarget = enable;
            warpModeTicks = WarpModeDuration;
            warpFieldRevision++;
            if (enable) warpFieldAngle = HullHeading;
            Map.GetComponent<WarpPrismFields>().Changed();
        }
        bool TickWarpField()
        {
            if (warpModeTicks > 0)
            {
                if (--warpModeTicks == 0)
                {
                    warpFieldMode = warpFieldTarget;
                    if (!warpFieldMode) heading = warpFieldAngle;
                    Map.GetComponent<WarpPrismFields>().Changed();
                }
                return true;
            }
            if (!warpFieldMode) return false;
            // Six degrees per simulated second, respecting map pause and speed.
            warpFieldAngle = Mathf.Repeat(warpFieldAngle + .1f, 360f);
            return true;
        }
        internal void ResetWarpField()
        {
            if (!IsWarpPrism || !warpFieldMode && warpModeTicks == 0) return;
            warpFieldMode = warpFieldTarget = false;
            warpModeTicks = 0;
            warpFieldRevision++;
            if (Spawned) Map.GetComponent<WarpPrismFields>().Changed();
        }
        bool DrawWarpField()
        {
            bool expanded = warpModeTicks > 0
                ? (warpFieldTarget ? warpModeTicks <= WarpModeDuration / 2 : warpModeTicks > WarpModeDuration / 2)
                : warpFieldMode;
            if (!expanded) return false;
            if (warpFieldMaterial == null)
                warpFieldMaterial = MaterialPool.MatFrom("NMC/Fleet/warp_prism_field", ShaderDatabase.Cutout);
            float unfold = warpModeTicks == 0 ? 1f : warpFieldTarget
                ? Mathf.Lerp(.85f, 1f, 1f - warpModeTicks / 30f)
                : Mathf.Lerp(.85f, 1f, (warpModeTicks - 30f) / 30f);
            Graphics.DrawMesh(MeshPool.plane10, Matrix4x4.TRS(DrawPos,
                Quaternion.AngleAxis(warpFieldAngle, Vector3.up),
                new Vector3(3f * unfold, 1f, 3f)), warpFieldMaterial, 0);
            return true;
        }
        void ExposeWarpField()
        {
            Scribe_Values.Look(ref warpFieldMode, "nmcWarpFieldMode");
            Scribe_Values.Look(ref warpFieldTarget, "nmcWarpFieldTarget");
            Scribe_Values.Look(ref warpModeTicks, "nmcWarpModeTicks");
            Scribe_Values.Look(ref warpFieldRevision, "nmcWarpFieldRevision");
            Scribe_Values.Look(ref warpFieldAngle, "nmcWarpFieldAngle");
        }
    }

    // One small per-map index, updated by the ship lifecycle. Power getters never
    // scan every building, allocate a list, or change simulation state.
    public sealed class WarpPrismFields : MapComponent
    {
        public const float Radius = 6f;
        readonly List<Building_NivarianAircraft> ships = new List<Building_NivarianAircraft>();
        readonly Dictionary<Building_NivarianAircraft, FieldGroundOverlay> overlays
            = new Dictionary<Building_NivarianAircraft, FieldGroundOverlay>();
        static readonly ConditionalWeakTable<Map, WarpPrismFields> maps = new ConditionalWeakTable<Map, WarpPrismFields>();
        public int Revision { get; private set; }
        public WarpPrismFields(Map map) : base(map)
        { if (map != null) { maps.Remove(map); maps.Add(map, this); } }
        public static WarpPrismFields For(Map map) => map == null ? null
            : maps.TryGetValue(map, out var fields) ? fields : map.GetComponent<WarpPrismFields>();
        public IEnumerable<Building_NivarianAircraft> ActiveShips => ships.Where(s => s.WarpFieldActive);
        public bool HasActive
        { get { for (int i = 0; i < ships.Count; i++) if (ships[i].WarpFieldActive) return true; return false; } }
        public bool Covers(IntVec3 cell)
        {
            for (int i = 0; i < ships.Count; i++)
                if (ships[i].WarpFieldActive && cell.InHorDistOf(ships[i].Position, Radius)) return true;
            return false;
        }
        public void Register(Building_NivarianAircraft ship)
        {
            if (ships.Contains(ship)) return;
            ships.Add(ship); ships.Sort((a, b) => a.thingIDNumber.CompareTo(b.thingIDNumber)); Changed();
        }
        public void Unregister(Building_NivarianAircraft ship)
        {
            ships.Remove(ship);
            if (overlays.TryGetValue(ship, out var overlay)) { overlay.Clear(); overlays.Remove(ship); }
            Changed();
        }
        public void Changed() { Revision++; }
        public static bool AnyDestination => Find.Maps.Any(m => For(m).HasActive);
        public override void MapComponentUpdate()
        {
            if (Find.CurrentMap != map) return;
            foreach (var ship in ships)
            {
                if (!ship.WarpFieldActive)
                { if (overlays.TryGetValue(ship, out var old)) { old.Clear(); overlays.Remove(ship); } continue; }
                if (!overlays.TryGetValue(ship, out var overlay))
                { overlay = new FieldGroundOverlay(Radius); overlays.Add(ship, overlay); }
                overlay.Draw(map, ship.Position);
            }
        }
        public override void MapRemoved()
        { foreach (var overlay in overlays.Values) overlay.Clear(); overlays.Clear(); ships.Clear(); }
    }

    public static class WarpPrismActions
    {
        // Explicit desired mode makes duplicate/delayed clicks idempotent.
        public static void SetMode(Map map, Building_NivarianAircraft ship, bool energyField)
        {
            if (map == null || ship == null || !ship.Spawned || ship.Destroyed || ship.Map != map
                || ship.Faction != Faction.OfPlayer) return;
            ship.StartWarpMode(energyField);
        }
        public static Command_Action ModeGizmo(Building_NivarianAircraft ship)
        {
            bool enabled = ship.warpFieldMode;
            var command = new Command_Action {
                defaultLabel = NmcText.Get(enabled ? "NMC_WarpFlightMode" : "NMC_WarpFieldMode"),
                defaultDesc = NmcText.Get("NMC_WarpModeDescription"),
                icon = ContentFinder<Texture2D>.Get(enabled ? "NMC/Fleet/warp_prism" : "NMC/Fleet/warp_prism_field"),
                action = () => SetMode(ship.Map, ship, !enabled)
            };
            if (!ship.CombatFlight) command.Disable(NmcText.Get("NMC_WarpTakeoffFirst"));
            else if (ship.warpModeTicks > 0) command.Disable(NmcText.Get("NMC_WarpChanging", (ship.warpModeTicks / 60f).ToString("F1")));
            return command;
        }
        public static void RecallToPrism(Map source, Pawn pawn, Building_NivarianAircraft prism, int expectedRevision)
            => RecallToPrismById(source, pawn, prism?.Map?.uniqueID ?? -1,
                prism?.thingIDNumber ?? -1, expectedRevision);

        // MP's Thing serializer binds map context to its owning map. Send stable
        // destination IDs so only the pawn's source map is the command context.
        public static void RecallToPrismById(Map source, Pawn pawn, int destinationMapId, int prismId, int expectedRevision)
        {
            if (!FieldRecallActions.ResearchFinished || !FieldRecallActions.Eligible(pawn)
                || source == null || !Find.Maps.Contains(source) || pawn.Map != source) return;
            var destinationMap = Find.Maps.FirstOrDefault(m => m.uniqueID == destinationMapId);
            var prism = destinationMap == null ? null : WarpPrismFields.For(destinationMap).ActiveShips
                .FirstOrDefault(s => s.thingIDNumber == prismId);
            if (prism == null || !prism.WarpFieldActive || prism.warpFieldRevision != expectedRevision
                || !Find.Maps.Contains(prism.Map))
            { Messages.Message(NmcText.Get("NMC_WarpDestinationLost"), pawn, MessageTypeDefOf.RejectInput, false); return; }
            var cell = FieldRecallActions.LandingCell(pawn, prism.Map, prism.Position, WarpPrismFields.Radius);
            if (!cell.IsValid)
            { Messages.Message(NmcText.Get("NMC_Text0250"), pawn, MessageTypeDefOf.RejectInput, false); return; }
            FieldRecallActions.Teleport(source, pawn, prism.Map, cell, prism);
        }
        internal static void ChooseDestination(Map source, Pawn pawn, int primaryRevision)
        {
            var pawns = Find.Selector.SelectedObjectsListForReading.OfType<Pawn>()
                .Where(p => p.Map == source && FieldRecallActions.Eligible(p)).OrderBy(p => p.thingIDNumber).ToList();
            if (!pawns.Contains(pawn)) pawns = new List<Pawn> { pawn };
            var prisms = Find.Maps.OrderBy(m => m.uniqueID).SelectMany(m => WarpPrismFields.For(m).ActiveShips).ToList();
            if (prisms.Count == 0)
            { foreach (var selected in pawns) FieldRecallActions.Recall(source, selected, primaryRevision); return; }
            var options = new List<FloatMenuOption>();
            options.Add(new FloatMenuOption(NmcText.Get("NMC_WarpPrimaryDestination"), FieldEconomy.HasField ? (Action)(() => {
                foreach (var selected in pawns) FieldRecallActions.Recall(source, selected, primaryRevision);
            }) : null));
            foreach (var prism in prisms)
            {
                var destination = prism; int revision = prism.warpFieldRevision;
                options.Add(new FloatMenuOption(NmcText.Get("NMC_WarpPrismDestination", prism.Map.Parent.LabelCap,
                    prism.Position.x, prism.Position.z), () => {
                    foreach (var selected in pawns) RecallToPrism(source, selected, destination, revision);
                }));
            }
            Find.WindowStack.Add(new FloatMenu(options));
        }
    }
}
