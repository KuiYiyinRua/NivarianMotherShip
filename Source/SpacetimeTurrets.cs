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
    public sealed class CompProperties_SpacetimeTurretCadence : CompProperties
    {
        public CompProperties_SpacetimeTurretCadence() { compClass = typeof(CompSpacetimeTurretCadence); }
    }
    public sealed class CompSpacetimeTurretCadence : ThingComp
    {
        public bool waveActive;
        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref waveActive, "nmcTurretWaveCadenceActive");
        }
    }

    public static class SpacetimeTurretTimers
    {
        internal static readonly FieldInfo GunCooldown = AccessTools.Field(typeof(Building_TurretGun), "burstCooldownTicksLeft")
            ?? throw new MissingFieldException("Building_TurretGun", "burstCooldownTicksLeft");
        internal static readonly FieldInfo AttachedCooldown = AccessTools.Field(typeof(CompAttachTurret), "_cooldownUntilTick")
            ?? throw new MissingFieldException("CompAttachTurret", "_cooldownUntilTick");
        public static bool ActiveFor(Thing thing) => thing is Building && thing.Spawned
            && SupportDiplomacy.Friendly(thing) && SpacetimeWaveMap.ActiveFor(thing.Map);
        public static void Install()
        {
            foreach (var def in DefDatabase<ThingDef>.AllDefsListForReading)
            {
                if (def.thingClass == null || !typeof(Building).IsAssignableFrom(def.thingClass)) continue;
                bool gun = typeof(Building_TurretGun).IsAssignableFrom(def.thingClass);
                bool attached = def.comps?.Any(c => c.compClass != null && typeof(CompAttachTurret).IsAssignableFrom(c.compClass)) == true;
                if (!gun && !attached) continue;
                if (def.comps == null) def.comps = new List<CompProperties>();
                if (!def.comps.Any(c => c.compClass == typeof(CompSpacetimeTurretCadence)))
                    def.comps.Add(new CompProperties_SpacetimeTurretCadence());
            }
        }
        internal static IEnumerable<MethodBase> GunMethods(string name)
        {
            return DefDatabase<ThingDef>.AllDefsListForReading.Select(d => d.thingClass)
                .Where(t => t != null && typeof(Building_TurretGun).IsAssignableFrom(t))
                .Concat(new[] { typeof(Building_TurretGun) }).Distinct()
                .Select(t => AccessTools.Method(t, name)).Where(m => m != null).Distinct();
        }
        static int Adjust(int ticks, bool active) => active
            ? Mathf.CeilToInt(ticks / (float)SpacetimeWaveStats.TurretMultiplier)
            : ticks * SpacetimeWaveStats.TurretMultiplier;
        // Runs only from the simulation tick, never from a stat or UI getter.
        public static void Reconcile(ThingWithComps thing)
        {
            var cadence = thing.GetComp<CompSpacetimeTurretCadence>();
            if (cadence == null) return;
            bool active = ActiveFor(thing);
            if (active == cadence.waveActive) return;
            if (thing is Building_TurretGun)
            {
                int left = (int)GunCooldown.GetValue(thing);
                if (left > 0) GunCooldown.SetValue(thing, Adjust(left, active));
            }
            int now = Find.TickManager.TicksGame;
            foreach (var turret in thing.AllComps.OfType<CompAttachTurret>())
            {
                int left = (int)AttachedCooldown.GetValue(turret) - now;
                if (left > 0) AttachedCooldown.SetValue(turret, now + Adjust(left, active));
            }
            cadence.waveActive = active;
        }
    }

    [HarmonyPatch]
    public static class SpacetimeGunTick
    {
        static IEnumerable<MethodBase> TargetMethods() => SpacetimeTurretTimers.GunMethods("Tick");
        static void Prefix(Building_TurretGun __instance) => SpacetimeTurretTimers.Reconcile(__instance);
    }
    [HarmonyPatch]
    public static class SpacetimeGunCooldown
    {
        [ThreadStatic] static int depth;
        static IEnumerable<MethodBase> TargetMethods() => SpacetimeTurretTimers.GunMethods("BurstCooldownTime");
        static void Prefix(out int __state) { __state = depth; depth++; }
        static void Postfix(Building_TurretGun __instance, ref float __result, int __state)
        {
            depth = __state;
            if (__state == 0 && SpacetimeTurretTimers.ActiveFor(__instance)) __result /= SpacetimeWaveStats.TurretMultiplier;
        }
        static Exception Finalizer(Exception __exception, int __state) { depth = __state; return __exception; }
    }
    [HarmonyPatch(typeof(CompAttachTurret), nameof(CompAttachTurret.CompTick))]
    public static class SpacetimeAttachedTurretTick
    {
        static void Prefix(CompAttachTurret __instance) => SpacetimeTurretTimers.Reconcile(__instance.parent);
    }
    [HarmonyPatch(typeof(CompAttachTurret), "FireComplete")]
    public static class SpacetimeAttachedTurretCooldown
    {
        static void Postfix(CompAttachTurret __instance)
        {
            if (!SpacetimeTurretTimers.ActiveFor(__instance.parent)) return;
            int now = Find.TickManager.TicksGame;
            int left = (int)SpacetimeTurretTimers.AttachedCooldown.GetValue(__instance) - now;
            if (left > 0) SpacetimeTurretTimers.AttachedCooldown.SetValue(__instance,
                now + Mathf.CeilToInt(left / (float)SpacetimeWaveStats.TurretMultiplier));
        }
    }
}
