using System;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace NivarianCommander.ShipUpgrades.MultiplayerAdapter
{
    // Preserve the original adapter's MVID: MP-Race-Compatibility-Plus recognizes
    // that exact assembly as the owner of native orbit/turret randomness.
    [StaticConstructorOnStartup]
    public static class Bootstrap
    {
        static Action<Pawn, Map, Map> fixPawn;
        static Func<Map, object> mapTime;
        static FieldInfo mapTicks, turretCooldown, turretIdle;
        static Bootstrap()
        {
            if (!MP.enabled) return;
            var method = AccessTools.DeclaredMethod(AccessTools.TypeByName("Multiplayer.Client.Patches.TimestampFixer"),
                "FixPawn", new[] { typeof(Pawn), typeof(Map), typeof(Map) });
            if (method == null) throw new MissingMethodException("[NMC-Ship-MP] Native passenger timestamp conversion unresolved");
            fixPawn = (Action<Pawn, Map, Map>)Delegate.CreateDelegate(typeof(Action<Pawn, Map, Map>), method);
            var getter = AccessTools.DeclaredMethod(AccessTools.TypeByName("Multiplayer.Client.Extensions"), "AsyncTime", new[] { typeof(Map) });
            mapTicks = AccessTools.Field(AccessTools.TypeByName("Multiplayer.Client.AsyncTimeComp"), "mapTicks");
            var turret = AccessTools.TypeByName("Nivarian_Race.Code.Comps.ThingComps.CompAttachTurret");
            turretCooldown = AccessTools.Field(turret, "_cooldownUntilTick");
            turretIdle = AccessTools.Field(turret, "_nextIdleRotationTick");
            if (getter == null || mapTicks == null || turretCooldown == null || turretIdle == null)
                throw new MissingMemberException("[NMC-Ship-MP] Native map clock/turret deadlines unresolved");
            mapTime = (Func<Map, object>)Delegate.CreateDelegate(typeof(Func<Map, object>), getter);
            var mapType = new SyncType(typeof(Map)) { contextMap = true };
            MP.RegisterSyncMethod(typeof(AircraftStarshipUpgrade), nameof(AircraftStarshipUpgrade.Cancel),
                new SyncType[] { mapType, typeof(Building_NivarianAircraft) }).CancelIfAnyArgNull();
            MP.RegisterSyncMethod(typeof(ShipFieldRecallActions), nameof(ShipFieldRecallActions.Recall),
                new SyncType[] { mapType, typeof(Thing), typeof(int) }).CancelIfAnyArgNull();
            FieldRecallContext.AdjustShipPassengers = AdjustPassengers;
            Log.Message("[NMC-Ship-MP] Cancel(Map,Aircraft) and Recall(Map,Thing,revision) synchronized; native passenger/turret timestamp conversion resolved; original adapter/random owners retained.");
        }
        static void AdjustPassengers(Thing ship, Map source, Map destination)
        {
            if (!MP.IsInMultiplayer) return;
            // Held passengers neither spawn nor pass through WorldPawns here.
            if (ship is Building_NivarianAircraft aircraft)
                foreach (var thing in aircraft.TransporterComp.innerContainer)
                    if (thing is Pawn pawn) fixPawn(pawn, source, destination);
            // Native fleet turret deadlines are absolute map ticks. Preserve their
            // remaining time when the destination map advances at another speed.
            if (ship is FleetShip fleet)
            {
                int offset = (int)mapTicks.GetValue(mapTime(destination)) - (int)mapTicks.GetValue(mapTime(source));
                foreach (var gun in fleet.GetAllCompAttachTurrets())
                {
                    turretCooldown.SetValue(gun, (int)turretCooldown.GetValue(gun) + offset);
                    turretIdle.SetValue(gun, (int)turretIdle.GetValue(gun) + offset);
                    gun.LastAttackTargetTick += offset;
                }
            }
        }
    }
}
