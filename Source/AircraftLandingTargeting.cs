using RimWorld;
using Verse;

namespace NivarianCommander
{
    public static class AircraftLandingTargeting
    {
        public static void Begin(Building_NivarianAircraft ship)
        {
            if (ship == null || !ship.Spawned || !ship.CombatFlight) return;
            var map = ship.Map;
            var rotation = Rot4.FromAngleFlat(ship.heading);
            // Match the native MapParent shuttle picker. Preview rotation stays
            // local; only the confirmed cell and rotation cross the sync boundary.
            Find.Targeter.BeginTargeting(TargetingParameters.ForCell(),
                target => AircraftActions.Land(map, ship, target.Cell, rotation),
                target => GhostDrawer.DrawGhostThing(target.Cell, rotation, ship.def, ship.Graphic,
                    AircraftTargeting.LandingClear(ship, target.Cell, rotation)
                        ? Designator_Place.CanPlaceColor : Designator_Place.CannotPlaceColor,
                    AltitudeLayer.Blueprint),
                target =>
                {
                    bool accepted = Find.CurrentMap == map && ship.CombatFlight
                        && AircraftTargeting.LandingClear(ship, target.Cell, rotation);
                    if (!accepted) Messages.Message(NmcText.Get("NMC_Text0012"),
                        new LookTargets(target.Cell, map), MessageTypeDefOf.RejectInput, false);
                    return accepted;
                }, mouseAttachment: CompLaunchable.TargeterMouseAttachment,
                onGuiAction: target =>
                {
                    if (KeyBindingDefOf.Designator_RotateRight.KeyDownEvent)
                        rotation = rotation.Rotated(RotationDirection.Clockwise);
                    if (KeyBindingDefOf.Designator_RotateLeft.KeyDownEvent)
                        rotation = rotation.Rotated(RotationDirection.Counterclockwise);
                });
        }
    }
}
