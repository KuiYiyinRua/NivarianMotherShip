using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace NivarianCommander
{
    [HarmonyPatch(typeof(TransportShip), nameof(TransportShip.Tick))]
    internal static class CarrierManufacturingInTransit
    {
        static void Postfix(TransportShip __instance)
        {
            if (__instance.shipThing is Building_NivarianAircraft ship && ship.IsCarrier && !ship.Spawned
                && !ship.Destroyed && ship.ParentHolder != null && !ship.IsInCaravan()) ship.Manufacture();
        }
    }
    [HarmonyPatch(typeof(WorkGiver_Refuel), nameof(WorkGiver_Refuel.CanRefuelThing))]
    internal static class AircraftRefuelOnGround
    {
        static void Postfix(Thing t, ref bool __result)
        { if (t is Building_NivarianAircraft ship && ship.Airborne) __result = false; }
    }
    [HarmonyPatch(typeof(LoadTransportersJobUtility), nameof(LoadTransportersJobUtility.HasJobOnTransporter))]
    internal static class AircraftLoadOnGround
    {
        static bool Prefix(CompTransporter transporter, ref bool __result)
        {
            if (!(transporter?.parent is Building_NivarianAircraft ship) || !ship.Airborne) return true;
            __result = false; return false;
        }
    }
}
