using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Nivarian_Race.Code.Comps.ThingComps;
using RimWorld;
using UnityEngine;
using Verse;

namespace NivarianCommander
{
    [HarmonyPatch(typeof(Thing), nameof(Thing.TakeDamage))]
    public static class ProjectionDamage
    {
        public sealed class Before { public ProjectionLink link; public int hp; public bool outer; }
        [HarmonyPriority(Priority.First)]
        static void Prefix(Thing __instance, out Before __state)
        {
            var link = ProjectionRegistry.Projected(__instance);
            __state = link == null || link.retiring ? null : new Before { link = link, hp = Math.Max(0, __instance.HitPoints), outer = link.damageDepth++ == 0 };
        }
        [HarmonyPriority(Priority.Last)]
        static Exception Finalizer(Thing __instance, Before __state, Exception __exception)
        {
            if (__state == null) return __exception;
            var link = __state.link; link.damageDepth--;
            if (!__state.outer) return __exception;
            // Read actual HP after shields, native armor and destruction. An
            // overkill result can never return more than this hit's previous HP.
            int loss = Math.Max(0, __state.hp - Math.Max(0, __instance.HitPoints));
            if (loss == 0 || link.source == null || link.source.Destroyed || !link.source.Spawned || link.source.Map != link.sourceMap) return __exception;
            ProjectionContext.Run(link.sourceMap, () =>
            {
                int damage = Math.Min(loss, Math.Max(0, link.source.HitPoints));
                link.source.HitPoints -= damage; link.damageReturned += damage;
                // HP assignment deliberately bypasses a second armor/shield
                // evaluation, while actual source destruction stays native.
                if (link.source.HitPoints <= 0 && !link.source.Destroyed) link.source.Destroy(DestroyMode.KillFinalize);
            });
            return __exception;
        }
    }
    [HarmonyPatch(typeof(Thing), nameof(Thing.HitPoints), MethodType.Setter)]
    public static class ProjectionCannotHeal
    {
        static bool Prefix(Thing __instance, int value) => !ProjectionRegistry.IsProjection(__instance) || value <= __instance.HitPoints;
    }
    [HarmonyPatch(typeof(WorkGiver_Repair), nameof(WorkGiver_Repair.HasJobOnThing))]
    public static class ProjectionCannotRepair
    {
        static bool Prefix(Thing t, ref bool __result)
        { if (!ProjectionRegistry.IsProjection(t)) return true; __result = false; return false; }
    }
    [HarmonyPatch(typeof(Building), nameof(Building.Destroy))]
    public static class ProjectionNoRebuild
    { static void Prefix(Building __instance, ref DestroyMode mode) { if (ProjectionRegistry.IsProjection(__instance)) mode = DestroyMode.Vanish; } }
    [HarmonyPatch(typeof(Thing), nameof(Thing.Destroy))]
    public static class ProjectionDestroyed
    {
        static void Prefix(Thing __instance, ref DestroyMode mode, out ProjectionLink __state)
        {
            __state = ProjectionRegistry.Projected(__instance);
            if (__state != null) { __state.destroying = true; mode = DestroyMode.Vanish; }
            else if (ProjectionRegistry.Source(__instance) != null) ProjectionRegistry.Current.RemoveThing(__instance);
        }
        static Exception Finalizer(ProjectionLink __state, Exception __exception)
        {
            if (__state != null) { __state.destroying = false; ProjectionRegistry.Current.Retire(__state, false); }
            return __exception;
        }
    }
    [HarmonyPatch(typeof(Thing), nameof(Thing.DeSpawn))]
    public static class ProjectionDespawned
    {
        static void Prefix(Thing __instance, out ProjectionLink __state)
        {
            __state = ProjectionRegistry.Projected(__instance);
            if (__state == null && ProjectionRegistry.Source(__instance) != null) ProjectionRegistry.Current.RemoveThing(__instance);
        }
        static void Postfix(ProjectionLink __state)
        { if (__state != null && !__state.destroying && !__state.retiring) ProjectionRegistry.Current.Retire(__state); }
    }
    [HarmonyPatch(typeof(Thing), nameof(Thing.Notify_MyMapRemoved))]
    public static class ProjectionMapRemoved
    {
        static void Prefix(Thing __instance, out ProjectionLink __state)
        {
            __state = ProjectionRegistry.Projected(__instance);
            if (__state == null && ProjectionRegistry.Source(__instance) != null) ProjectionRegistry.Current.RemoveThing(__instance);
        }
        static void Postfix(ProjectionLink __state) { if (__state != null) ProjectionRegistry.Current.Retire(__state); }
    }
    [HarmonyPatch]
    public static class ProjectionNoLeavings
    {
        static IEnumerable<MethodBase> TargetMethods() => AccessTools.GetDeclaredMethods(typeof(GenLeaving))
            .Where(m => m.Name == "DoLeavingsFor" && m.GetParameters().Length > 0
                && typeof(Thing).IsAssignableFrom(m.GetParameters()[0].ParameterType));
        static bool Prefix([HarmonyArgument(0)] Thing thing) => !ProjectionRegistry.IsProjection(thing);
    }
    [HarmonyPatch(typeof(Building_TurretGun), "get_Active")]
    public static class ProjectionNativeTurretGate
    {
        [HarmonyPriority(Priority.Last)]
        static void Postfix(Building_TurretGun __instance, ref bool __result)
        { if (ProjectionRegistry.BlocksFire(__instance) || !ProjectionResources.CanFireWeapon(__instance, ProjectionRegistry.Weapon(__instance))) __result = false; }
    }
    [HarmonyPatch]
    public static class ProjectionAttachedTurretGate
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.DeclaredMethod(typeof(CompAttachTurret), nameof(CompAttachTurret.CompTick));
            yield return AccessTools.DeclaredMethod(typeof(CompAttachTurret), nameof(CompAttachTurret.Fire));
        }
        static bool Prefix(CompAttachTurret __instance) => !ProjectionRegistry.BlocksFire(__instance.parent)
            && (ProjectionRegistry.Projected(__instance.parent) == null || ProjectionResources.CanFireWeapon(__instance.parent, ProjectionRegistry.Weapon(__instance)));
    }
    [HarmonyPatch]
    public static class ProjectionVerbGate
    {
        static IEnumerable<MethodBase> TargetMethods() => AccessTools.GetDeclaredMethods(typeof(Verb)).Where(m => m.Name == nameof(Verb.TryStartCastOn));
        static bool Prefix(Verb __instance, ref bool __result)
        { if (!ProjectionRegistry.BlocksFire(__instance.Caster) && ProjectionResources.CanFire(__instance)) return true; __result = false; return false; }
    }
    public static class ProjectionResources
    {
        public static bool CanFire(Thing projection)
        {
            if (!CanUseSource(projection)) return false;
            var link = ProjectionRegistry.Projected(projection);
            if (link == null) return true;
            var guns = ProjectionRegistry.Weapons(link.source);
            return guns.Count == 0 || guns.Any(Funded);
        }
        // S12 adds its own energy weapon; native magazine and private-gun fuel
        // are unrelated to this support weapon. Keep ordinary per-gun gates.
        public static bool CanFireSupport(Thing projection) => CanUseSource(projection);
        static bool CanUseSource(Thing projection)
        {
            var link = ProjectionRegistry.Projected(projection);
            if (link == null) return true;
            if (ProjectionRegistry.BlocksFire(projection)) return false;
            var power = link.source.TryGetComp<CompPowerTrader>();
            if (power != null && !power.PowerOn) return false;
            var fuel = link.source.TryGetComp<CompRefuelable>();
            if (fuel != null && !fuel.HasFuel) return false;
            return true;
        }
        public static bool CanFire(Verb verb)
            => CanFireWeapon(verb.Caster, verb.EquipmentSource);
        public static bool CanFireWeapon(Thing projection, Thing gun)
        {
            if (!CanUseSource(projection)) return false;
            var link = ProjectionRegistry.Projected(projection);
            if (link == null) return true;
            if (gun == null) return CanFire(projection);
            var sourceGun = ProjectionRegistry.ResourceSource(gun);
            // Only private projection guns participate. Synthetic support verbs
            // have no EquipmentSource and consume the holder's shared resources.
            if (sourceGun == null) return false;
            return Funded(sourceGun);
        }
        static bool Funded(Thing sourceGun)
        {
            if (sourceGun == null || sourceGun.Destroyed) return false;
            var gunFuel = sourceGun?.TryGetComp<CompRefuelable>();
            if (gunFuel != null && !gunFuel.HasFuel) return false;
            // Attached native verbs can fall back to their default projectile when
            // an equipped shell magazine empties. Require real source ammunition.
            var shells = sourceGun?.TryGetComp<CompChangeableProjectile>();
            return shells == null || shells.Loaded;
        }
        static readonly FieldInfo RawFuel = AccessTools.Field(typeof(CompRefuelable), "fuel");
        public static void ClearIndependentInventory(Thing copy)
        {
            // Raw clone stock must stay empty, including initialFuelPercent=1.
            // Public getters delegate to the original while the link is live.
            var fuel = copy?.TryGetComp<CompRefuelable>();
            if (fuel != null) RawFuel.SetValue(fuel, 0f);
            var shells = copy?.TryGetComp<CompChangeableProjectile>();
            if (shells != null) shells.loadedCount = 0;
        }
    }
    [HarmonyPatch(typeof(CompPowerTrader), "get_PowerOn")]
    public static class ProjectionSourcePower
    {
        [HarmonyPriority(Priority.Last)]
        static void Postfix(CompPowerTrader __instance, ref bool __result)
        {
            var link = ProjectionRegistry.Projected(__instance.parent);
            if (link != null) __result = !ProjectionRegistry.BlocksKnownProjection(__instance.parent, link)
                && (link.source.TryGetComp<CompPowerTrader>()?.PowerOn ?? true);
        }
    }
    [HarmonyPatch(typeof(CompPowerTrader), "get_PowerOutput")]
    public static class ProjectionNoIndependentPower
    {
        [HarmonyPriority(Priority.Last)]
        static void Postfix(CompPowerTrader __instance, ref float __result) { if (ProjectionRegistry.IsProjection(__instance.parent)) __result = 0; }
    }
    [HarmonyPatch(typeof(CompRefuelable), nameof(CompRefuelable.CompTick))]
    public static class ProjectionNoIdleFuel
    { static bool Prefix(CompRefuelable __instance) => ProjectionRegistry.ResourceSource(__instance.parent) == null; }
    [HarmonyPatch(typeof(CompRefuelable), "get_Fuel")]
    public static class ProjectionSourceFuel
    {
        static void Postfix(CompRefuelable __instance, ref float __result)
        { var source = ProjectionRegistry.ResourceSource(__instance.parent); if (source != null) __result = source.TryGetComp<CompRefuelable>()?.Fuel ?? 0; }
    }
    [HarmonyPatch(typeof(CompRefuelable), "get_HasFuel")]
    public static class ProjectionSourceFueled
    {
        static void Postfix(CompRefuelable __instance, ref bool __result)
        { var source = ProjectionRegistry.ResourceSource(__instance.parent); if (source != null) __result = source.TryGetComp<CompRefuelable>()?.HasFuel == true; }
    }
    [HarmonyPatch(typeof(CompRefuelable), nameof(CompRefuelable.ConsumeFuel))]
    public static class ProjectionConsumeSourceFuel
    {
        static bool Prefix(CompRefuelable __instance, float amount)
        {
            var source = ProjectionRegistry.ResourceSource(__instance.parent);
            if (source == null) return true;
            var comp = source.TryGetComp<CompRefuelable>();
            if (comp != null) ProjectionContext.Run(ProjectionRegistry.ResourceMap(source), () => comp.ConsumeFuel(amount));
            return false;
        }
    }
    [HarmonyPatch(typeof(CompRefuelable), "get_ShouldAutoRefuelNowIgnoringFuelPct")]
    public static class ProjectionNoAutoRefuel
    { static void Postfix(CompRefuelable __instance, ref bool __result) { if (ProjectionRegistry.ResourceSource(__instance.parent) != null) __result = false; } }
    [HarmonyPatch]
    public static class ProjectionNoIndependentRefuel
    {
        static IEnumerable<MethodBase> TargetMethods() => AccessTools.GetDeclaredMethods(typeof(CompRefuelable)).Where(m => m.Name == nameof(CompRefuelable.Refuel));
        static bool Prefix(CompRefuelable __instance) => ProjectionRegistry.ResourceSource(__instance.parent) == null;
    }
    [HarmonyPatch(typeof(CompRefuelable), nameof(CompRefuelable.CanEjectFuel))]
    public static class ProjectionNoIndependentFuelEjectionOffer
    {
        static bool Prefix(CompRefuelable __instance, ref AcceptanceReport __result)
        { if (ProjectionRegistry.ResourceSource(__instance.parent) == null) return true; __result = false; return false; }
    }
    [HarmonyPatch(typeof(CompRefuelable), nameof(CompRefuelable.EjectFuel))]
    public static class ProjectionNoIndependentFuelEjection
    { static bool Prefix(CompRefuelable __instance) => ProjectionRegistry.ResourceSource(__instance.parent) == null; }
    [HarmonyPatch(typeof(CompChangeableProjectile), "get_LoadedShell")]
    public static class ProjectionSourceShell
    {
        static void Postfix(CompChangeableProjectile __instance, ref ThingDef __result)
        { var source = ProjectionRegistry.ResourceSource(__instance.parent); if (source != null) __result = source.TryGetComp<CompChangeableProjectile>()?.LoadedShell; }
    }
    [HarmonyPatch(typeof(CompChangeableProjectile), nameof(CompChangeableProjectile.Notify_ProjectileLaunched))]
    public static class ProjectionConsumeSourceShell
    {
        static bool Prefix(CompChangeableProjectile __instance)
        {
            var source = ProjectionRegistry.ResourceSource(__instance.parent);
            if (source == null) return true;
            var comp = source.TryGetComp<CompChangeableProjectile>();
            if (comp != null) ProjectionContext.Run(ProjectionRegistry.ResourceMap(source),
                () => comp.Notify_ProjectileLaunched());
            return false;
        }
    }
    [HarmonyPatch(typeof(CompChangeableProjectile), nameof(CompChangeableProjectile.LoadShell))]
    public static class ProjectionNoIndependentShellLoading
    { static bool Prefix(CompChangeableProjectile __instance) => ProjectionRegistry.ResourceSource(__instance.parent) == null; }
    [HarmonyPatch(typeof(CompChangeableProjectile), nameof(CompChangeableProjectile.RemoveShell))]
    public static class ProjectionNoIndependentShellExtraction
    {
        static bool Prefix(CompChangeableProjectile __instance, ref Thing __result)
        { if (ProjectionRegistry.ResourceSource(__instance.parent) == null) return true; __result = null; return false; }
    }
    [HarmonyPatch(typeof(Building_TurretGun), "get_CanExtractShell")]
    public static class ProjectionHideNativeExtractShell
    {
        [HarmonyPriority(Priority.Last)]
        static void Postfix(Building_TurretGun __instance, ref bool __result)
        { if (ProjectionRegistry.IsProjection(__instance)) __result = false; }
    }
    [HarmonyPatch(typeof(Building_TurretGun), "ExtractShell")]
    public static class ProjectionBlockNativeExtractShell
    { static bool Prefix(Building_TurretGun __instance) => !ProjectionRegistry.IsProjection(__instance); }
    [HarmonyPatch]
    public static class ProjectionBlockMannedReload
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.DeclaredMethod(typeof(JobDriver_ManTurret), "GunNeedsLoading");
            yield return AccessTools.DeclaredMethod(typeof(JobDriver_ManTurret), "GunNeedsRefueling");
        }
        [HarmonyPriority(Priority.Last)]
        static void Postfix(Building b, ref bool __result) { if (ProjectionRegistry.IsProjection(b)) __result = false; }
    }
    [HarmonyPatch(typeof(WorkGiver_Refuel), nameof(WorkGiver_Refuel.HasJobOnThing))]
    public static class ProjectionBlockRefuelWork
    {
        static bool Prefix(Thing t, ref bool __result)
        { if (!ProjectionRegistry.IsProjection(t)) return true; __result = false; return false; }
    }
    [HarmonyPatch(typeof(WorkGiver_Refuel), nameof(WorkGiver_Refuel.JobOnThing))]
    public static class ProjectionBlockRefuelJob
    {
        static bool Prefix(Thing t, ref Verse.AI.Job __result)
        { if (!ProjectionRegistry.IsProjection(t)) return true; __result = null; return false; }
    }
    [HarmonyPatch(typeof(CompFlickable), nameof(CompFlickable.CompGetGizmosExtra))]
    public static class ProjectionHideIndependentPowerSwitch
    {
        static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> __result, CompFlickable __instance)
            => ProjectionRegistry.IsProjection(__instance.parent) ? Enumerable.Empty<Gizmo>() : __result;
    }
    [HarmonyPatch(typeof(CompRefuelable), nameof(CompRefuelable.CompGetGizmosExtra))]
    public static class ProjectionReadOnlyFuelGizmos
    {
        static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> __result, CompRefuelable __instance)
            => ProjectionRegistry.ResourceSource(__instance.parent) != null ? __result.Where(g => !(g is Command)) : __result;
    }
    [HarmonyPatch(typeof(Thing), nameof(Thing.GetGizmos))]
    public static class ProjectionGizmos
    {
        static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> __result, Thing __instance)
        {
            foreach (var gizmo in __result) yield return gizmo;
            var link = ProjectionRegistry.Projected(__instance);
            if (link != null)
            {
                yield return new Command_Action { defaultLabel = NmcText.Get("NMC_Text0414"), defaultDesc = NmcText.Get("NMC_Text0415"),
                    icon = ContentFinder<Texture2D>.Get("UI/Commands/Halt"),
                    action = () => ProjectionActions.Cancel(link.targetMap, __instance) };
                yield break;
            }
            if (!ProjectionRegistry.Eligible(__instance) || CommanderResearch.For(DefenseActions.Ability(19))?.IsFinished != true) yield break;
            yield return new Command_Toggle { defaultLabel = NmcText.Get("NMC_Text0416"), defaultDesc = NmcText.Get("NMC_Text0417"),
                icon = ContentFinder<Texture2D>.Get("NMC/Buildings/hyperdimensional_monolith"),
                isActive = () => ProjectionRegistry.Current.registered.Contains(__instance),
                toggleAction = () => ProjectionActions.Register(__instance.Map, __instance, !ProjectionRegistry.Current.registered.Contains(__instance)) };
            if (ProjectionRegistry.Source(__instance) is ProjectionLink original)
                yield return new Command_Action { defaultLabel = NmcText.Get("NMC_Text0418"), defaultDesc = NmcText.Get("NMC_Text0419"), icon = ContentFinder<Texture2D>.Get("UI/Commands/Halt"),
                    action = () => ProjectionActions.CancelSource(__instance.Map, __instance) };
        }
    }
    [HarmonyPatch(typeof(ThingWithComps), nameof(ThingWithComps.GetInspectString))]
    public static class ProjectionInspect
    {
        static void Postfix(ThingWithComps __instance, ref string __result)
        {
            var link = ProjectionRegistry.Projected(__instance);
            if (link != null) __result += NmcText.Get("NMC_Text0422", (link.remaining > 3600 ? NmcText.Get("NMC_Text0420", ((link.remaining - 3600) / 60f).ToString("0.0")) : NmcText.Get("NMC_Text0421", (link.remaining / 60f).ToString("0.0"))), link.source.LabelCap);
            else if (ProjectionRegistry.Source(__instance) != null) __result += NmcText.Get("NMC_Text0423");
        }
    }
}
