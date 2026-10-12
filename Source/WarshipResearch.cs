using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace NivarianCommander
{
    public static class WarshipResearch
    {
        public const string Hangar = "NMC_CarrierHangarExpansion";
        static readonly HashSet<string> Ships = new HashSet<string>
        {
            "NMC_Carrier", "NMC_GravityFrigate", "NMC_CarrierInterceptor",
            "NMC_TacticalMothership", "NMC_FleetDestroyer", "NMC_VoidRay", "NMC_Zoraya"
        };
        public static bool Applies(ThingDef def) => def != null && Ships.Contains(def.defName);
        public static bool Finished(string name) => Current.Game != null
            && DefDatabase<ResearchProjectDef>.GetNamedSilentFail(name)?.IsFinished == true;
        public static int Level(string prefix)
        {
            // Only contiguous completed tiers count, including debug-unlocked saves.
            int level = 0;
            while (level < 3 && Finished(prefix + (level + 1))) level++;
            return level;
        }
        public static float TierFactor(int level) => 1f + .2f * Math.Max(0, Math.Min(3, level));
        public static float ArmorFactor => TierFactor(Level("NMC_WarshipArmor"));
        public static float WeaponFactor => TierFactor(Level("NMC_WarshipWeapon"));
        public static int FighterCapacity => Finished(Hangar) ? 8 : 6;
        public static void Install()
        {
            var stat = StatDefOf.MaxHitPoints;
            if (stat.parts == null) stat.parts = new List<StatPart>();
            if (!stat.parts.Any(p => p is StatPart_WarshipHull))
                stat.parts.Add(new StatPart_WarshipHull { parentStat = stat });
            foreach (var def in DefDatabase<ThingDef>.AllDefsListForReading.Where(Applies))
            {
                if (def.comps == null) def.comps = new List<CompProperties>();
                if (!def.comps.Any(p => p.compClass == typeof(CompWarshipHull)))
                    def.comps.Add(new CompProperties { compClass = typeof(CompWarshipHull) });
            }
        }
    }

    public sealed class StatPart_WarshipHull : StatPart
    {
        public override void TransformValue(StatRequest req, ref float val)
        {
            if (WarshipResearch.Applies(req.Def as ThingDef)) val *= WarshipResearch.ArmorFactor;
        }
        public override string ExplanationPart(StatRequest req) => WarshipResearch.Applies(req.Def as ThingDef)
            ? DefDatabase<ResearchProjectDef>.GetNamed("NMC_WarshipArmor1").LabelCap + ": x" + WarshipResearch.ArmorFactor.ToString("0.0") : null;
    }

    // MaxHitPoints is immutable in vanilla. Only our research-sensitive hulls
    // must bypass its per-Thing immutable cache, including UI stat requests.
    [HarmonyPatch(typeof(StatWorker), nameof(StatWorker.GetValue), new[] { typeof(Thing), typeof(bool), typeof(int) })]
    public static class WarshipHullStatCache
    {
        static bool Prefix(StatWorker __instance, Thing thing, bool applyPostProcess,
            StatDef ___stat, ref float __result)
        {
            if (___stat != StatDefOf.MaxHitPoints || !WarshipResearch.Applies(thing?.def)) return true;
            __result = __instance.GetValue(StatRequest.For(thing), applyPostProcess);
            return false;
        }
    }

    // Preserve hull condition as max HP changes; new hulls still initialize at full HP.
    public sealed class CompWarshipHull : ThingComp
    {
        int previousMaximum;
        public override void PostPostMake() { previousMaximum = parent.MaxHitPoints; }
        public void Refresh()
        {
            int maximum = parent.MaxHitPoints;
            if (previousMaximum > 0 && previousMaximum != maximum)
                parent.HitPoints = Mathf.Clamp(Mathf.RoundToInt(parent.HitPoints * (float)maximum / previousMaximum), 1, maximum);
            else if (parent.HitPoints > maximum) parent.HitPoints = maximum;
            previousMaximum = maximum;
        }
        public override void PostSpawnSetup(bool respawningAfterLoad) { Refresh(); }
        public override void CompTick()
        { if ((Find.TickManager.TicksGame + parent.thingIDNumber) % 60 == 0) Refresh(); }
        public override void PostExposeData()
        { Scribe_Values.Look(ref previousMaximum, "nmcWarshipPreviousMaximum"); }
    }

    [HarmonyPatch(typeof(Thing), nameof(Thing.TakeDamage))]
    public static class WarshipWeaponDamage
    {
        static readonly AccessTools.StructFieldRef<DamageInfo, float> Penetration
            = AccessTools.StructFieldRefAccess<DamageInfo, float>("armorPenetrationInt");
        static void Prefix(ref DamageInfo dinfo)
        {
            if (!WarshipResearch.Applies(dinfo.Instigator?.def)) return;
            float factor = WarshipResearch.WeaponFactor;
            // Interceptors keep native drone projectile behavior without changing
            // a shared projectile Def used by unrelated drones.
            if (dinfo.Instigator is CarrierInterceptor) factor *= .5f;
            dinfo.SetAmount(dinfo.Amount * factor);
            if (dinfo.ArmorPenetrationInt >= 0f)
                Penetration(ref dinfo) *= WarshipResearch.WeaponFactor;
        }
    }
}
