using Multiplayer.API;
using Verse;

namespace NivarianCommander.StormShip.MultiplayerAdapter
{
    [StaticConstructorOnStartup]
    public static class Bootstrap
    {
        static Bootstrap()
        {
            if (!MP.enabled) return;
            var map = new SyncType(typeof(Map)) { contextMap = true };
            MP.RegisterSyncMethod(typeof(StormShipActions), nameof(StormShipActions.SetTarget),
                new SyncType[] { map, typeof(Building_NivarianAircraft), typeof(Thing) }).CancelIfAnyArgNull();
            MP.RegisterSyncMethod(typeof(StormShipActions), nameof(StormShipActions.SetFireAtWill),
                new SyncType[] { map, typeof(Building_NivarianAircraft), typeof(bool) }).CancelIfAnyArgNull();
            MP.RegisterSyncMethod(typeof(StormShipActions), nameof(StormShipActions.ClearTarget),
                new SyncType[] { map, typeof(Building_NivarianAircraft) }).CancelIfAnyArgNull();
            MP.RegisterSyncMethod(typeof(CarrierAttackActions), nameof(CarrierAttackActions.SetTarget),
                new SyncType[] { map, typeof(Building_NivarianAircraft), typeof(Thing) }).CancelIfAnyArgNull();
            MP.RegisterSyncMethod(typeof(CarrierAttackActions), nameof(CarrierAttackActions.SetFireAtWill),
                new SyncType[] { map, typeof(Building_NivarianAircraft), typeof(bool) }).CancelIfAnyArgNull();
            MP.RegisterSyncMethod(typeof(CarrierAttackActions), nameof(CarrierAttackActions.ClearTarget),
                new SyncType[] { map, typeof(Building_NivarianAircraft) }).CancelIfAnyArgNull();
            Log.Message("[NMC-Storm-MP] Storm and carrier target, fire-at-will and clear-target commands synchronized with source map context.");
        }
    }
}
