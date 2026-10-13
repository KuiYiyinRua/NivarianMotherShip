using Multiplayer.API;
using Verse;

namespace NivarianCommander.WarpPrism.MultiplayerAdapter
{
    [StaticConstructorOnStartup]
    public static class Bootstrap
    {
        static Bootstrap()
        {
            if (!MP.enabled) return;
            var map = new SyncType(typeof(Map)) { contextMap = true };
            MP.RegisterSyncMethod(typeof(WarpPrismActions), nameof(WarpPrismActions.SetMode),
                new SyncType[] { map, typeof(Building_NivarianAircraft), typeof(bool) }).CancelIfAnyArgNull();
            MP.RegisterSyncMethod(typeof(WarpPrismActions), nameof(WarpPrismActions.RecallToPrismById),
                new SyncType[] { map, typeof(Pawn), typeof(int), typeof(int), typeof(int) }).CancelIfAnyArgNull();
            Log.Message("[NMC-Warp-MP] Mode(Map,ship,bool) and personnel recall(Map,pawn,destinationMapId,prismId,revision) synchronized.");
        }
    }
}
