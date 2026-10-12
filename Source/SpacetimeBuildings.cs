using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Nivarian_Race.Code.Comps.BuildingComps;
using RimWorld;
using UnityEngine;
using Verse;

namespace NivarianCommander
{
    public static class SpacetimeBuildingWork
    {
        internal static readonly FieldInfo BillWork = AccessTools.Field(typeof(Comp_NivarianBillDoerCompBase), "WorkDone")
            ?? throw new MissingFieldException("Comp_NivarianBillDoerCompBase", "WorkDone");
        static readonly FieldInfo PrintSpeed = AccessTools.Field(typeof(CompCryoPrinter), "_printSpeed")
            ?? throw new MissingFieldException("CompCryoPrinter", "_printSpeed");
        public static bool ProducesCurrency(ThingDef def)
        {
            if (def == null) return false;
            if (def.thingClass != null && typeof(Building_NutrientEnergyTower).IsAssignableFrom(def.thingClass)) return true;
            if (def.comps == null) return false;
            foreach (var props in def.comps)
            {
                if (props.compClass == typeof(Comp_NivarianEnergyTower) || props.compClass == typeof(CompParticleCollector)
                    || props.compClass == typeof(CompColonistPowerCollector)) return true;
                if (props is CompProperties_CommanderStructure income && (income.incomePower > 0 || income.incomeVolatile > 0)) return true;
            }
            return false;
        }
        // The uplink also has a colonist P collector. Its research progress can
        // accelerate independently; currency collector ticks are never patched.
        static bool CurrencyOnlyFacility(ThingDef def) => ProducesCurrency(def)
            && def.comps?.Any(c => c.compClass == typeof(CompUplinkResearch)) != true;
        public static bool Applies(Thing thing)
        {
            if (!(thing is Building) || !thing.Spawned || !SupportDiplomacy.Friendly(thing)
                || !SpacetimeWaveMap.ActiveFor(thing.Map) || CurrencyOnlyFacility(thing.def)) return false;
            var selfBuild = thing.TryGetComp<CompSelfBuilding>();
            if (selfBuild != null && CurrencyOnlyFacility(selfBuild.Props.buildingDef)) return false;
            var core = thing.TryGetComp<CompCommanderConstruction>();
            if (core != null && CurrencyOnlyFacility(core.Props.completedDef)) return false;
            string owner = thing.def.modContentPack?.PackageIdPlayerFacing;
            return thing.def.defName.StartsWith("Nivarian_", StringComparison.Ordinal)
                || thing.def.defName.StartsWith("NMC_", StringComparison.Ordinal)
                || string.Equals(owner, "keeptpa.NivarianRace", StringComparison.OrdinalIgnoreCase)
                || string.Equals(owner, "local.nivarian.mothership.commander", StringComparison.OrdinalIgnoreCase);
        }
        public static float Factor(ThingComp comp) => Applies(comp.parent) ? SpacetimeWaveStats.WorkMultiplier : 1f;
        // Clamp the last automatic bill step; pigment consumption uses this same
        // factor so faster printing/mixing does not make a recipe cheaper.
        static float BillFactor(Comp_NivarianBillDoerCompBase comp, float current, float step)
        {
            float factor = Factor(comp);
            if (factor == 1f || step <= 0f) return factor;
            var bill = comp.GetActiveBill();
            if (bill?.recipe == null) return factor;
            return Mathf.Min(factor, Mathf.Max(0f, bill.recipe.workAmount - current) / step);
        }
        public static float AddFloatWork(float current, float step, ThingComp comp)
        {
            float factor = comp is CompCryoPrinter || comp is CompCoolantFactory
                ? BillFactor((Comp_NivarianBillDoerCompBase)comp, current, step) : Factor(comp);
            return current + step * factor;
        }
        public static int AddIntWork(int current, int step, ThingComp comp) => current + Mathf.RoundToInt(step * Factor(comp));
        public static float Scale(float value, ThingComp comp) => value * Factor(comp);
        internal static float PrinterFactor(CompCryoPrinter comp) => BillFactor(comp,
            (float)BillWork.GetValue(comp), (float)PrintSpeed.GetValue(comp) * 15f);
        public static float CoolantStep(float value, CompCoolantFactory comp) => value * BillFactor(comp,
            (float)BillWork.GetValue(comp), value * comp.GetYieldMultiplier);
    }

    // Patch only the native progress addition. Every original power/material gate,
    // completion callback, fuel tick, animation and research unlock remains native.
    [HarmonyPatch]
    public static class SpacetimeNativeProgress
    {
        static readonly Dictionary<MethodBase, FieldInfo> targets = new Dictionary<MethodBase, FieldInfo>();
        static void Add(Type type, string method, string field)
        {
            var target = AccessTools.DeclaredMethod(type, method) ?? throw new MissingMethodException(type.FullName, method);
            var progress = AccessTools.Field(type, field) ?? throw new MissingFieldException(type.FullName, field);
            targets.Add(target, progress);
        }
        static IEnumerable<MethodBase> TargetMethods()
        {
            targets.Clear();
            Add(typeof(CompUplinkResearch), "UpdateResearchProgress", "_researchProgress");
            Add(typeof(CompCryoForge), "AdvanceForging", "WorkDone");
            Add(typeof(CompBrewingBarrel), "AdvanceBrewing", "WorkDone");
            Add(typeof(CompPigmentMixer), "AdvanceMixing", "WorkDone");
            Add(typeof(CompCoolantFactory), "AdvanceMixing", "WorkDone");
            Add(typeof(CompCryoPrinter), "StepPrinting", "WorkDone");
            Add(typeof(CompDustCatcher), "CompTick", "_progressTicks");
            Add(typeof(CompSoilExtractor), "CompTick", "_progressTicks");
            Add(typeof(CompNiraControlCenter), "CompTick", "_stageProgressTicks");
            Add(typeof(CompNiraControlCenter), "TickModuleInstallationProgress", "_installProgressTicks");
            Add(typeof(CompNiraControlCenter), "TickModuleUninstallProgress", "_uninstallProgressTicks");
            Add(typeof(CompNiraControlCenter), "TickResurrection", "_resurrectProgressTicks");
            return targets.Keys;
        }
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
        {
            var codes = new List<CodeInstruction>(instructions);
            var field = targets[__originalMethod];
            var add = AccessTools.Method(typeof(SpacetimeBuildingWork), field.FieldType == typeof(int) ? "AddIntWork" : "AddFloatWork");
            int matched = 0;
            for (int i = 1; i < codes.Count; i++)
            {
                if (codes[i].opcode != OpCodes.Stfld || !(codes[i].operand is FieldInfo stored)
                    || stored.DeclaringType != field.DeclaringType || stored.Name != field.Name
                    || codes[i - 1].opcode != OpCodes.Add) continue;
                var instance = new CodeInstruction(OpCodes.Ldarg_0);
                instance.labels.AddRange(codes[i - 1].labels);
                instance.blocks.AddRange(codes[i - 1].blocks);
                codes[i - 1] = instance;
                codes.Insert(i, new CodeInstruction(OpCodes.Call, add));
                i++; matched++;
            }
            if (matched != 1) throw new InvalidOperationException("[NMC] Spacetime progress target mismatch: " + __originalMethod + " matches=" + matched);
            return codes;
        }
    }
    [HarmonyPatch(typeof(CompSelfBuilding), "GetConstructionWorkPerTick")]
    public static class SpacetimeSelfBuilding
    {
        static void Postfix(CompSelfBuilding __instance, ref float __result) => __result = SpacetimeBuildingWork.Scale(__result, __instance);
    }
    [HarmonyPatch(typeof(CompCryoPrinter), "GetStepPigmentRequirement")]
    public static class SpacetimePrinterPigments
    {
        static void Postfix(CompCryoPrinter __instance, float[] __result)
        {
            float factor = SpacetimeBuildingWork.PrinterFactor(__instance);
            for (int i = 0; i < __result.Length; i++) __result[i] *= factor;
        }
    }
    [HarmonyPatch(typeof(CompCoolantFactory), "ConsumePigmentStep")]
    public static class SpacetimeCoolantPigments
    {
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            int matched = 0;
            foreach (var code in instructions)
            {
                yield return code;
                if (code.opcode != OpCodes.Ldc_R4 || !Equals(code.operand, 60f)) continue;
                yield return new CodeInstruction(OpCodes.Ldarg_0);
                yield return CodeInstruction.Call(typeof(SpacetimeBuildingWork), nameof(SpacetimeBuildingWork.CoolantStep));
                matched++;
            }
            if (matched != 1) throw new InvalidOperationException("[NMC] Spacetime coolant pigment target mismatch: " + matched);
        }
    }
    [HarmonyPatch(typeof(CompUplinkResearch), "GetFinalSpeed", MethodType.Getter)]
    public static class SpacetimeUplinkSpeedDisplay
    {
        static void Postfix(CompUplinkResearch __instance, ref float __result) => __result = SpacetimeBuildingWork.Scale(__result, __instance);
    }
    [HarmonyPatch(typeof(CompUplinkResearch), nameof(CompUplinkResearch.CompInspectStringExtra))]
    public static class SpacetimeUplinkInspectSpeed
    {
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            int matched = 0;
            foreach (var code in instructions)
            {
                yield return code;
                if (code.opcode != OpCodes.Ldfld || !(code.operand is FieldInfo field)
                    || field.DeclaringType != typeof(CompUplinkResearch) || field.Name != "_finalSpeedFactor") continue;
                yield return new CodeInstruction(OpCodes.Ldarg_0);
                yield return CodeInstruction.Call(typeof(SpacetimeBuildingWork), nameof(SpacetimeBuildingWork.Scale));
                matched++;
            }
            if (matched != 1) throw new InvalidOperationException("[NMC] Spacetime uplink inspect target mismatch: " + matched);
        }
    }
}
