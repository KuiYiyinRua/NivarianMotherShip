using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Nivarian.NivarianDrones;
using Nivarian_Race.Code.Helper;
using Verse;

namespace NivarianCommander
{
    // The military expansion's three defense buildings use the core drone hub.
    // No dependency on the optional military assembly is needed.
    public static class ProjectionDrones
    {
        static readonly FieldInfo Batteries = AccessTools.Field(typeof(NivarianDroneHubComp), "_batteries");
        static readonly FieldInfo Drones = AccessTools.Field(typeof(NivarianDroneHubComp), "_spawnedDrones");
        static readonly FieldInfo Initialized = AccessTools.Field(typeof(NivarianDroneHubComp), "batteriesInited");
        static readonly FieldInfo Enabled = AccessTools.Field(typeof(NivarianDroneHubComp), "_enable");
        static readonly MethodInfo Cache = AccessTools.Method(typeof(NivarianDroneHubComp), "EnsureDroneDefCacheInitialized");
        static readonly MethodInfo Init = AccessTools.Method(typeof(NivarianDroneHubComp), "InitBatteries");
        static readonly MethodInfo Recall = AccessTools.Method(typeof(NivarianDroneHubComp), "RecallDroneImmediately");
        public static bool Eligible(Thing thing)
            => thing?.TryGetComp<NivarianDroneHubComp>() != null
                && (thing.def.defName == "Nivarian_DroneHub"
                    || thing.def.defName == "Nivarian_DroneHub_AttackerStrafing"
                    || thing.def.defName == "Nivarian_DroneHub_Recon");

        static void RecallAll(NivarianDroneHubComp hub)
        {
            var drones = (List<NivarianDroneBase>)Drones.GetValue(hub);
            foreach (var drone in drones.ToArray())
            {
                if (drone == null || drone.Destroyed) { drones.Remove(drone); continue; }
                // Native recall returns the actual remaining flight battery and
                // clears the owning-hub binding before destroying the drone.
                Recall.Invoke(hub, new object[] { drone });
            }
        }
        public static void TransferPool(ProjectionLink link)
        {
            if (!Eligible(link.source)) return;
            var source = link.source.TryGetComp<NivarianDroneHubComp>();
            var copy = link.projection.TryGetComp<NivarianDroneHubComp>();
            Cache.Invoke(copy, null);
            ProjectionContext.Run(link.sourceMap, () =>
            {
                Cache.Invoke(source, null);
                var pool = (List<NivarianGauge>)Batteries.GetValue(source);
                var drones = (List<NivarianDroneBase>)Drones.GetValue(source);
                if (!(bool)Initialized.GetValue(source) && pool.Count == 0 && drones.Count == 0)
                    Init.Invoke(source, null);
                Initialized.SetValue(source, true);
                RecallAll(source);
                Batteries.SetValue(copy, Batteries.GetValue(source));
                Batteries.SetValue(source, new List<NivarianGauge>());
                Initialized.SetValue(copy, true);
                Enabled.SetValue(copy, Enabled.GetValue(source));
                link.dronePoolTransferred = true;
            });
        }
        public static void ReturnPool(ProjectionLink link)
        {
            if (!link.dronePoolTransferred) return;
            var copy = link.projection.TryGetComp<NivarianDroneHubComp>();
            var source = link.source.TryGetComp<NivarianDroneHubComp>();
            ProjectionContext.Run(link.targetMap, () => RecallAll(copy));
            ProjectionContext.Run(link.sourceMap, () =>
            {
                Batteries.SetValue(source, Batteries.GetValue(copy));
                Batteries.SetValue(copy, new List<NivarianGauge>());
                link.dronePoolTransferred = false;
            });
        }
        public static bool CanRun(NivarianDroneHubComp hub)
        {
            if (ProjectionRegistry.Source(hub.parent) != null) return false;
            var link = ProjectionRegistry.Projected(hub.parent);
            return link == null || !ProjectionRegistry.BlocksFire(hub.parent);
        }
    }
    [HarmonyPatch(typeof(NivarianDroneHubComp), nameof(NivarianDroneHubComp.CompTick))]
    public static class ProjectionDroneHubTick
    {
        static bool Prefix(NivarianDroneHubComp __instance) => ProjectionDrones.CanRun(__instance);
    }
    [HarmonyPatch(typeof(NivarianDroneHubComp), nameof(NivarianDroneHubComp.ShouldReleaseDrone))]
    public static class ProjectionDroneLaunchGate
    {
        static bool Prefix(NivarianDroneHubComp __instance, ref bool __result)
        {
            if (ProjectionDrones.CanRun(__instance)) return true;
            __result = false;
            return false;
        }
    }
}
