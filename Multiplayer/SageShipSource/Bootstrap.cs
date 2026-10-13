using Multiplayer.API;
using Verse;
namespace NivarianCommander.SageShip.MultiplayerAdapter
{
    [StaticConstructorOnStartup]
    public static class Bootstrap
    {
        static Bootstrap()
        {
            if (!MP.enabled) return;
            var map = new SyncType(typeof(Map)) { contextMap = true };
            MP.RegisterSyncMethod(typeof(SageShipActions), nameof(SageShipActions.SetAttackMode),
                new SyncType[] { map, typeof(Building_NivarianAircraft), typeof(bool) }).CancelIfAnyArgNull();
            MP.RegisterSyncMethod(typeof(SageShipActions), nameof(SageShipActions.SetTarget),
                new SyncType[] { map, typeof(Building_NivarianAircraft), typeof(Thing) }).CancelIfAnyArgNull();
            Log.Message("[NMC-Sage-MP] Attack mode and ground target synchronized with source map context.");
        }
    }
}
