using System;
using System.Collections;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace NivarianCommander.MultiplayerAdapter
{
    [StaticConstructorOnStartup]
    public static class Bootstrap
    {
        static ISyncMethod opened;
        static Action<Signal> emit;
        static FieldInfo tickingMap;
        static Bootstrap()
        {
            if (!MP.enabled) return;
            CommanderResearchAccess.MultiplayerActive = () => MP.IsInMultiplayer;
            MP.RegisterSyncMethod(typeof(CommanderResearchAccess), nameof(CommanderResearchAccess.SetEnabled));
            MP.RegisterSyncMethod(typeof(CommanderActions), nameof(CommanderActions.Fire), new SyncType[] { new SyncType(typeof(Map)) { contextMap = true }, typeof(IntVec3) });
            MP.RegisterSyncMethod(typeof(CommanderActions), nameof(CommanderActions.PurchaseProtection));
            var mapType = new SyncType(typeof(Map)) { contextMap = true };
            MP.RegisterSyncMethod(typeof(NutrientTowerSupply), nameof(NutrientTowerSupply.SetTarget), new SyncType[] { mapType, typeof(Building_NutrientEnergyTower), typeof(float) }).CancelIfAnyArgNull();
            MP.RegisterSyncMethod(typeof(NutrientTowerSupply), nameof(NutrientTowerSupply.SetAutoFill), new SyncType[] { mapType, typeof(Building_NutrientEnergyTower), typeof(bool) }).CancelIfAnyArgNull();
            MP.RegisterSyncMethod(typeof(CarrierDragonSteelSupply), nameof(CarrierDragonSteelSupply.SetTarget), new SyncType[] { mapType, typeof(Building_NivarianAircraft), typeof(int) }).CancelIfAnyArgNull();
            MP.RegisterSyncMethod(typeof(CarrierDragonSteelSupply), nameof(CarrierDragonSteelSupply.SetAutoFill), new SyncType[] { mapType, typeof(Building_NivarianAircraft), typeof(bool) }).CancelIfAnyArgNull();
            ContinuumWorldBridge.Install();
            MP.RegisterSyncMethod(typeof(ContinuumDebug), nameof(ContinuumDebug.Spawn)).SetDebugOnly();
            MP.RegisterSyncMethod(typeof(ContinuumDebug), nameof(ContinuumDebug.Complete)).SetDebugOnly();
            MP.RegisterSyncMethod(typeof(ContinuumDebug), nameof(ContinuumDebug.Fail)).SetDebugOnly();
            MP.RegisterSyncMethod(typeof(AircraftActions), nameof(AircraftActions.Takeoff), new SyncType[] { mapType, typeof(Building_NivarianAircraft) }).CancelIfAnyArgNull();
            MP.RegisterSyncMethod(typeof(AircraftStarshipUpgrade), nameof(AircraftStarshipUpgrade.Request), new SyncType[] { mapType, typeof(Building_NivarianAircraft) }).CancelIfAnyArgNull();
            MP.RegisterSyncMethod(typeof(AircraftActions), nameof(AircraftActions.Move), new SyncType[] { mapType, typeof(Building_NivarianAircraft), typeof(IntVec3) }).CancelIfAnyArgNull();
            MP.RegisterSyncMethod(typeof(AircraftActions), nameof(AircraftActions.Land), new SyncType[] { mapType, typeof(Building_NivarianAircraft), typeof(IntVec3), typeof(Rot4) }).CancelIfAnyArgNull();
            MP.RegisterSyncMethod(typeof(AircraftActions), nameof(AircraftActions.Recall), new SyncType[] { mapType, typeof(Building_NivarianAircraft) }).CancelIfAnyArgNull();
            MP.RegisterSyncMethod(typeof(AircraftActions), nameof(AircraftActions.Gravity), new SyncType[] { mapType, typeof(Building_NivarianAircraft), typeof(Thing) }).CancelIfAnyArgNull();
            MP.RegisterSyncMethod(typeof(AircraftActions), nameof(AircraftActions.ToggleManufacture), new SyncType[] { mapType, typeof(Building_NivarianAircraft) }).CancelIfAnyArgNull();
            MP.RegisterSyncMethod(typeof(AircraftActions), nameof(AircraftActions.ToggleGravity), new SyncType[] { mapType, typeof(Building_NivarianAircraft) }).CancelIfAnyArgNull();
            MP.RegisterSyncMethod(typeof(PointShopTrade), nameof(PointShopTrade.Buy), new SyncType[]
                { mapType, typeof(int), typeof(int[]), typeof(int[]), typeof(string), typeof(string) }).CancelIfAnyArgNull();
            MP.RegisterSyncMethod(typeof(PointShopTrade), nameof(PointShopTrade.Donate), new SyncType[]
                { mapType, typeof(int[]), typeof(int[]), typeof(string), typeof(string) }).CancelIfAnyArgNull();
            Log.Message("[NMC-MP] point shop: Buy/Donate synchronized with explicit map, stock generation, stable item IDs, quoted value and request ID; UI/chat remain local.");
            MP.RegisterSyncMethod(typeof(ProjectionActions), nameof(ProjectionActions.Start), new SyncType[] { mapType, typeof(IntVec3) });
            MP.RegisterSyncMethod(typeof(ProjectionActions), nameof(ProjectionActions.Register), new SyncType[] { mapType, typeof(Thing), typeof(bool) });
            MP.RegisterSyncMethod(typeof(ProjectionActions), nameof(ProjectionActions.RegisterAll));
            MP.RegisterSyncMethod(typeof(ProjectionActions), nameof(ProjectionActions.Cancel), new SyncType[] { mapType, typeof(Thing) });
            MP.RegisterSyncMethod(typeof(ProjectionActions), nameof(ProjectionActions.CancelSource), new SyncType[] { mapType, typeof(Thing) });
            ProjectionMapContext.Install();
            MP.RegisterSyncMethod(typeof(FieldRecallActions), nameof(FieldRecallActions.Recall), new SyncType[] { mapType, typeof(Pawn), typeof(int) }).CancelIfAnyArgNull();
            FieldRecallMapContext.Install();
            MP.RegisterSyncMethod(typeof(CaravanFieldRecall), nameof(CaravanFieldRecall.Recall));
            LegionFactionMapContext.Install();
            MP.RegisterSyncMethod(typeof(SolarActions), nameof(SolarActions.Start), new SyncType[] { mapType, typeof(IntVec3) });
            MP.RegisterSyncMethod(typeof(RayActions), nameof(RayActions.Start), new SyncType[] { mapType, typeof(IntVec3) });
            MP.RegisterSyncMethod(typeof(RayActions), nameof(RayActions.Cancel), new SyncType[] { mapType });
            MP.RegisterSyncMethod(typeof(PurificationActions), nameof(PurificationActions.Start), new SyncType[] { mapType, typeof(IntVec3) });
            MP.RegisterSyncMethod(typeof(PurificationActions), nameof(PurificationActions.Draft), new SyncType[] { mapType, typeof(int), typeof(bool) });
            MP.RegisterSyncMethod(typeof(PurificationActions), nameof(PurificationActions.Move), new SyncType[] { mapType, typeof(int), typeof(IntVec3) });
            MP.RegisterSyncMethod(typeof(PurificationActions), nameof(PurificationActions.Cancel), new SyncType[] { mapType });
            MP.RegisterSyncMethod(typeof(SpacetimeWaveActions), nameof(SpacetimeWaveActions.Start));
            MP.RegisterSyncMethod(typeof(SpacetimeWaveActions), nameof(SpacetimeWaveActions.Cancel));
            MP.RegisterSyncMethod(typeof(TimeStopActions), nameof(TimeStopActions.Start));
            MP.RegisterSyncMethod(typeof(TimeStopActions), nameof(TimeStopActions.Cancel));
            MP.RegisterSyncMethod(typeof(VoidCrystalActions), nameof(VoidCrystalActions.Start), new SyncType[] { mapType, typeof(IntVec3) });
            MP.RegisterSyncMethod(typeof(VoidCrystalActions), nameof(VoidCrystalActions.Cancel), new SyncType[] { mapType });
            MP.RegisterSyncMethod(typeof(FleetActions), nameof(FleetActions.Start), new SyncType[] { mapType, typeof(IntVec3) });
            MP.RegisterSyncMethod(typeof(TebrusLegionActions), nameof(TebrusLegionActions.Start), new SyncType[] { mapType, typeof(IntVec3) });
            MP.RegisterSyncMethod(typeof(SeydasLegionActions), nameof(SeydasLegionActions.Start), new SyncType[] { mapType, typeof(IntVec3) });
            MP.RegisterSyncMethod(typeof(RayFleetActions), nameof(RayFleetActions.Start), new SyncType[] { mapType, typeof(IntVec3) });
            MP.RegisterSyncMethod(typeof(BlackHoleActions), nameof(BlackHoleActions.Start), new SyncType[] { mapType, typeof(IntVec3) });
            MP.RegisterSyncMethod(typeof(BlackHoleActions), nameof(BlackHoleActions.Cancel), new SyncType[] { mapType });
            MP.RegisterSyncMethod(typeof(DefenseActions), nameof(DefenseActions.DeployField), new SyncType[] { mapType, typeof(IntVec3) });
            MP.RegisterSyncMethod(typeof(DefenseActions), nameof(DefenseActions.RemoveField), new SyncType[] { mapType });
            MP.RegisterSyncMethod(typeof(DefenseActions), nameof(DefenseActions.Shield), new SyncType[] { mapType });
            MP.RegisterSyncMethod(typeof(DefenseActions), nameof(DefenseActions.CancelShield), new SyncType[] { mapType });
            MP.RegisterSyncMethod(typeof(DefenseActions), nameof(DefenseActions.Overload), new SyncType[] { mapType, typeof(Thing) });
            MP.RegisterSyncMethod(typeof(DefenseActions), nameof(DefenseActions.CancelOverload), new SyncType[] { mapType, typeof(Thing) });
            MP.RegisterSyncMethod(typeof(StructureActions), nameof(StructureActions.DeployCore), new SyncType[] { mapType, typeof(IntVec3), typeof(int) });
            MP.RegisterSyncMethod(typeof(StructureActions), nameof(StructureActions.CancelCore), new SyncType[] { mapType, typeof(Thing) });
            MP.RegisterSyncMethod(typeof(GhostActions), nameof(GhostActions.Deploy), new SyncType[] { mapType, typeof(IntVec3) });
            RegisterMapTickGuard();
            NativeDroneOrbitRandom.Install();
            NativeTurretIdleRandom.Install();
            opened = MP.RegisterSyncMethod(typeof(Bootstrap), nameof(Opened));
            LongEventHandler.ExecuteWhenFinished(RegisterResearch);
        }
        static void RegisterMapTickGuard()
        {
            tickingMap = AccessTools.Field(AccessTools.TypeByName("Multiplayer.Client.AsyncTimeComp"), "tickingMap");
            if (tickingMap == null) throw new MissingFieldException("[NMC-MP] Actual map tick context unresolved");
            var harmony = new Harmony("local.nivarian.commander.map-tick");
            var guard = new HarmonyMethod(typeof(Bootstrap), nameof(BeforeFieldMapTick));
            // Harmony still runs simple prefixes when MP skips vanilla map managers.
            // Guard the field hook itself, including paused maps and world tick calls.
            harmony.Patch(AccessTools.Method(typeof(FieldChargingTick), "Prefix"), prefix: guard);
            harmony.Patch(AccessTools.Method(typeof(FieldPowerTick), "Prefix"), prefix: guard);
            Log.Message("[NMC-MP] field charging and power rebuild restricted to actual tickingMap identity.");
        }
        static bool BeforeFieldMapTick([HarmonyArgument(0)] Map map)
            => !MP.IsInMultiplayer || ReferenceEquals(tickingMap.GetValue(null), map);
        static void RegisterResearch()
        {
            var native = AccessTools.TypeByName("Nivarian_Race.Code.Comps.BuildingComps.CompUplinkResearch");
            var registryType = AccessTools.TypeByName("Multiplayer.Client.Sync");
            var registry = AccessTools.Field(registryType, "methodBaseToInternalId")?.GetValue(null) as IDictionary;
            if (native == null || registry == null) throw new InvalidOperationException("[NMC-MP] Required research/sync registry unresolved");
            var container = AccessTools.TypeByName("Nivarian_Race.Code.Comps.BuildingComps.CompUniversalContainer");
            var eject = AccessTools.Method(container, "PopOutThings", new[] { typeof(IntVec3), typeof(Map) });
            if (eject == null) throw new MissingMethodException("[NMC-MP] Native container eject unresolved");
            bool containerReused = registry.Contains(eject);
            if (!containerReused) MP.RegisterSyncMethod(eject);
            Log.Message("[NMC-MP] core deploy/cancel synchronized; native container eject registration reused=" + containerReused);
            int reused = 0;
            foreach (string name in new[] { "StartNewProject", "CancelCurrentProject" })
            {
                var method = AccessTools.Method(native, name);
                if (method == null) throw new MissingMethodException(native.FullName, name);
                if (registry.Contains(method)) reused++;
                else MP.RegisterSyncMethod(method);
            }
            var receiver = AccessTools.TypeByName("Nivarian_Race.Code.Progress.ProgressSignalReceiver");
            var nativeEmit = AccessTools.Method(receiver, "Emit", new[] { typeof(Signal) });
            var window = AccessTools.TypeByName("Nivarian_Race.Code.UI.Window_UplinkResearch");
            var target = AccessTools.Method(window, "TryEmitUplinkOpenedSignal");
            if (nativeEmit == null || target == null) throw new MissingMethodException("[NMC-MP] Required uplink progress event unresolved");
            emit = (Action<Signal>)Delegate.CreateDelegate(typeof(Action<Signal>), nativeEmit);
            bool existing = false;
            var patches = Harmony.GetPatchInfo(target);
            if (patches != null) foreach (var patch in patches.Transpilers)
                if (patch.PatchMethod.DeclaringType?.FullName == "Meow.TaleNivarianCompatibility.NivarianUplinkProgressSignal") existing = true;
            if (!existing) new Harmony("local.nivarian.commander.uplink-open").Patch(target, prefix: new HarmonyMethod(typeof(Bootstrap), nameof(BeforeOpened)));
            Log.Message("[NMC-MP] resolved Fire(Map context, cell), PurchaseProtection, native research start/cancel; existing registrations reused=" + reused + "; uplink progress route=" + (existing ? "existing compatibility" : "commander"));
        }
        // Boot animation and its per-window guard stay local; its real progress event is a world command.
        static bool BeforeOpened(bool ____bootFinished, ref bool ____uplinkOpenedSignalEmitted)
        {
            if (!MP.IsInMultiplayer || !MP.InInterface) return true;
            if (____bootFinished && !____uplinkOpenedSignalEmitted)
            { ____uplinkOpenedSignalEmitted = true; opened.DoSync(null); }
            return false;
        }
        static void Opened() => emit(new Signal("Nivarian.Progress.UplinkOpened", false));
    }
}
