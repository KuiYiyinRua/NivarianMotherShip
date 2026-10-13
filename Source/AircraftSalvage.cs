using System;
using HarmonyLib;
using RimWorld;
using Verse;

namespace NivarianCommander
{
    // Use native building leavings so a destroyed hull refunds its recipe only once.
    [HarmonyPatch(typeof(GenLeaving), "GetBuildingResourcesLeaveCalculator")]
    public static class AircraftSalvage
    {
        static void Postfix(Thing destroyedThing, DestroyMode mode, ref Func<int, int> __result)
        {
            if (mode == DestroyMode.KillFinalize && destroyedThing is Building_NivarianAircraft
                && (destroyedThing.def.defName == "NMC_Carrier"
                    || destroyedThing.def.defName == "NMC_GravityFrigate" || destroyedThing.def.defName == "NMC_WarpPrism"
                    || destroyedThing.def.defName == "NMC_StormShip" || destroyedThing.def.defName == "NMC_SageShip"))
                __result = count => count / 2;
        }
    }
}
