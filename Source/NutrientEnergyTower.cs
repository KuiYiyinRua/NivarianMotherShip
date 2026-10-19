using System;
using System.Collections.Generic;
using System.Xml;
using HarmonyLib;
using Nivarian_Race.Code.Comps.BuildingComps;
using Nivarian_Race.Code.Defs;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace NivarianCommander
{
    public sealed class NutrientEnergyTowerProperties : DefModExtension
    {
        public float rateMultiplier = 1f;
    }

    // Growth-vat food storage and the vanilla HaulToContainer pipeline, with a
    // separate destination adapter because WorkGiver_HaulToGrowthVat hard-codes
    // Building_GrowthVat and its embryo/pawn requirements. No fake occupant.
    public sealed class Building_NutrientEnergyTower : Building, IThingHolder, IHaulDestination, ISuspendableThingHolder
    {
        public const float Capacity = Building_GrowthVat.NutritionBuffer;
        NutrientTowerFoodContainer food;
        StorageSettings nutritionSettings;
        double containedNutrition, pendingPower;
        int productionTicks;
        public float nutritionTarget = Capacity;
        public bool autoFillNutrition = true;
        Gizmo_NutrientTowerNutrition nutritionGizmo;
        internal Gizmo_NutrientTowerNutrition NutritionGizmo
        {
            get
            {
                if (nutritionGizmo == null || !nutritionGizmo.IsDragging)
                    nutritionGizmo = new Gizmo_NutrientTowerNutrition(this);
                return nutritionGizmo;
            }
        }
        CompPowerPlant_NutrientTower generator;
        static ThingDef raceDef, collectorDef;
        static LifeStageDef adultStage;
        static ResearchProjectDef supportResearch;
        static NivarianMotherShipRequestCurrencyDef powerCurrency;

        public Building_NutrientEnergyTower() { food = new NutrientTowerFoodContainer(this); }
        public bool StorageTabVisible => true;
        public bool IsContentsSuspended => true;
        bool EnabledForColony => Spawned && Faction == Faction.OfPlayer && FlickUtility.WantsToBeOn(this);
        internal bool CanReceiveNutrition => EnabledForColony && NutritionNeeded > 0d;
        // Gate both the dedicated work giver and vanilla general hauling. Keep
        // acceptance separate so active deliveries and explicit orders can fill
        // the configured target even when the reserve is already above half.
        public bool HaulDestinationEnabled => CanReceiveNutrition && autoFillNutrition &&
            NutritionStored < Math.Min(Capacity, nutritionTarget) * .5d;
        public bool OperationPermitted => EnabledForColony && !this.IsBrokenDown() && !this.IsBurning() &&
            SupportUnlocked && NativeLedger.Component != null;
        static bool SupportUnlocked
        {
            get
            {
                supportResearch = supportResearch ?? DefDatabase<ResearchProjectDef>.GetNamed("Nivarian_MothershipSupportInterface");
                return supportResearch.IsFinished;
            }
        }

        public static double PowerPerDay
        {
            get
            {
                collectorDef = collectorDef ?? DefDatabase<ThingDef>.GetNamed("Nivarian_UplinkResearchBeacon");
                return collectorDef.GetCompProperties<CompProperties_ColonistPowerCollector>().powerCoinPerColonist * .5;
            }
        }
        public static double NutritionPerDay
        {
            get
            {
                raceDef = raceDef ?? DefDatabase<ThingDef>.GetNamed("NivarianRace_Pawn");
                adultStage = adultStage ?? DefDatabase<LifeStageDef>.GetNamed("HumanlikeAdult");
                return Need_Food.BaseHungerRate(adultStage, raceDef) * 60000d * .5d;
            }
        }
        public double NutritionStored
        {
            get
            {
                double amount = containedNutrition;
                foreach (var item in food)
                    if (!item.IsNotFresh()) amount += item.stackCount * (double)item.GetStatValue(StatDefOf.Nutrition);
                return amount;
            }
        }
        public double NutritionNeeded => Math.Max(0d, Math.Min(Capacity, nutritionTarget) - NutritionStored);
        public double RateMultiplier => def.GetModExtension<NutrientEnergyTowerProperties>()?.rateMultiplier ?? 1d;
        public double DailyPower => PowerPerDay * RateMultiplier;
        public double DailyNutrition => NutritionPerDay * RateMultiplier;
        public float RatedWatts => -(def.GetCompProperties<CompProperties_Power>()?.PowerConsumption ?? 0f);
        public bool HasNutrition => NutritionStored >= DailyNutrition / 60000d;
        public bool IsOperating => OperationPermitted && HasNutrition && generator?.PowerOn == true;

        public override void PostMake()
        {
            base.PostMake();
            nutritionSettings = new StorageSettings(this);
            nutritionSettings.CopyFrom(def.building.defaultStorageSettings);
        }
        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);
            generator = GetComp<CompPowerPlant_NutrientTower>();
            generator?.UpdateDesiredPowerOutput();
        }
        public StorageSettings GetStoreSettings() => nutritionSettings;
        public StorageSettings GetParentStoreSettings() => def.building.fixedStorageSettings;
        public void Notify_SettingsChanged() { }
        public ThingOwner GetDirectlyHeldThings() => food;
        public void GetChildHolders(List<IThingHolder> children) => ThingOwnerUtility.AppendThingHoldersFromThings(children, food);

        public bool Accepts(Thing item) => FoodCountThatFits(item) > 0;
        internal int FoodCountThatFits(Thing item)
        {
            if (!CanReceiveNutrition || item == null || item.def.ingestible == null ||
                item.IsNotFresh() || nutritionSettings?.AllowedToAccept(item) != true ||
                GetParentStoreSettings()?.AllowedToAccept(item) == false) return 0;
            float value = item.GetStatValue(StatDefOf.Nutrition);
            double needed = NutritionNeeded;
            if (value <= 0f || float.IsNaN(value) || float.IsInfinity(value) || value > needed) return 0;
            // The original vat rounds the last delivery up to a whole food item.
            return (int)Math.Min(int.MaxValue, Math.Ceiling(needed / value));
        }

        protected override void Tick()
        {
            base.Tick();
            if (generator == null) return;
            if (!OperationPermitted || !HasNutrition) { generator.PowerOutput = 0f; return; }
            if (generator.PowerOutput <= 0f) generator.UpdateDesiredPowerOutput();
            if (!generator.PowerOn) return;
            double required = DailyNutrition / 60000d;
            if (required <= 0d || double.IsNaN(required) || double.IsInfinity(required)) return;
            // Like the vat: convert a whole food unit into the numeric buffer
            // only when needed, retaining leftover nutrition for later ticks.
            while (containedNutrition < required && food.Count > 0)
            {
                Thing item = food[0];
                float value = item.GetStatValue(StatDefOf.Nutrition);
                if (value <= 0f || item.IsNotFresh())
                { food.TryDrop(item, InteractionCell, Map, ThingPlaceMode.Near, out var _); break; }
                containedNutrition += value;
                item.SplitOff(1).Destroy();
            }
            if (containedNutrition < required)
            { generator.PowerOutput = 0f; return; }
            containedNutrition = Math.Max(0d, containedNutrition - required);
            if (!HasNutrition) generator.PowerOutput = 0f;
            if (++productionTicks >= 250)
            {
                pendingPower += DailyPower * productionTicks / 60000d;
                productionTicks = 0;
                int whole = (int)Math.Min(int.MaxValue, Math.Floor(pendingPower));
                if (whole > 0)
                {
                    powerCurrency = powerCurrency ?? DefDatabase<NivarianMotherShipRequestCurrencyDef>.GetNamed("PowerCoin");
                    NativeLedger.Component.AddCurrency(powerCurrency, whole);
                    pendingPower -= whole;
                }
            }
        }

        public override void DeSpawn(DestroyMode mode = DestroyMode.Vanish)
        {
            if (mode != DestroyMode.WillReplace) food.TryDropAll(InteractionCell, Map, ThingPlaceMode.Near);
            base.DeSpawn(mode);
        }
        public override void Destroy(DestroyMode mode = DestroyMode.Vanish)
        {
            base.Destroy(mode);
            food.ClearAndDestroyContents();
        }
        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (var gizmo in base.GetGizmos()) yield return gizmo;
            if (Faction == Faction.OfPlayer) yield return NutritionGizmo;
            foreach (var gizmo in StorageSettingsClipboard.CopyPasteGizmosFor(nutritionSettings)) yield return gizmo;
        }
        public override string GetInspectString()
        {
            string text = base.GetInspectString();
            if (!string.IsNullOrEmpty(text)) text += "\n";
            string status = !FlickUtility.WantsToBeOn(this) ? NmcText.Get("NMC_Text0300") : !HasNutrition ? NmcText.Get("NMC_Text0301") :
                !OperationPermitted || generator?.PowerOn != true ? NmcText.Get("NMC_Text0302") : NmcText.Get("NMC_Text0303");
            return NmcText.Get("NMC_Text0304", text, NutritionStored.ToString("0.###"), nutritionTarget.ToString("0.#"), DailyNutrition.ToString("0.###"), DailyPower.ToString("0.##"), status, RatedWatts.ToString("0"));
        }
        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Deep.Look(ref food, "nmcNutritionFood", this);
            Scribe_Deep.Look(ref nutritionSettings, "nmcNutritionSettings", this);
            Scribe_Values.Look(ref containedNutrition, "nmcContainedNutrition", 0d);
            Scribe_Values.Look(ref pendingPower, "nmcPendingPower", 0d);
            Scribe_Values.Look(ref productionTicks, "nmcProductionTicks", 0);
            Scribe_Values.Look(ref nutritionTarget, "nmcNutritionTarget", Capacity);
            Scribe_Values.Look(ref autoFillNutrition, "nmcAutoFillNutrition", true);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                food = food ?? new NutrientTowerFoodContainer(this);
                if (nutritionSettings == null)
                { nutritionSettings = new StorageSettings(this); nutritionSettings.CopyFrom(def.building.defaultStorageSettings); }
                nutritionSettings.owner = this;
                if (double.IsNaN(containedNutrition) || double.IsInfinity(containedNutrition) || containedNutrition < 0d) containedNutrition = 0d;
                if (double.IsNaN(pendingPower) || double.IsInfinity(pendingPower) || pendingPower < 0d) pendingPower = 0d;
                productionTicks = Mathf.Clamp(productionTicks, 0, 249);
                nutritionTarget = NutrientTowerSupply.ClampTarget(nutritionTarget);
            }
        }
    }

    public sealed class NutrientTowerFoodContainer : ThingOwner<Thing>
    {
        public NutrientTowerFoodContainer(IThingHolder owner) : base(owner) { }
        public override int GetCountCanAccept(Thing item, bool canMergeWithExistingStacks = true)
            => Owner is Building_NutrientEnergyTower tower ? Math.Min(base.GetCountCanAccept(item, canMergeWithExistingStacks), tower.FoodCountThatFits(item)) : 0;
        // Native transfer only asks CanAcceptAnyOf before its bool TryAdd call.
        // Reject an oversized atomic delivery so the caller retains the surplus.
        public override bool TryAdd(Thing item, bool canMergeWithExistingStacks = true)
            => item != null && GetCountCanAccept(item, canMergeWithExistingStacks) >= item.stackCount &&
                base.TryAdd(item, canMergeWithExistingStacks);
        public override int TryAdd(Thing item, int count, bool canMergeWithExistingStacks = true)
            => base.TryAdd(item, Math.Min(count, GetCountCanAccept(item, canMergeWithExistingStacks)), canMergeWithExistingStacks);
    }

    public sealed class CompPowerPlant_NutrientTower : CompPowerPlant
    {
        protected override float DesiredPowerOutput => parent is Building_NutrientEnergyTower tower && tower.HasNutrition && tower.OperationPermitted
            ? base.DesiredPowerOutput : 0f;
    }

    // Same hauling category and reserve/reach checks, with a configurable target
    // and vanilla job/toils as WorkGiver_HaulToGrowthVat; only target type differs.
    public sealed class WorkGiver_FillNutrientEnergyTower : WorkGiver_Scanner
    {
        public override ThingRequest PotentialWorkThingRequest => ThingRequest.ForGroup(ThingRequestGroup.BuildingArtificial);
        public override IEnumerable<Thing> PotentialWorkThingsGlobal(Pawn pawn)
        {
            foreach (string name in new[] { "NMC_NutrientEnergyTower", "NMC_ShadowEnergyTower" })
            {
                var towerDef = DefDatabase<ThingDef>.GetNamedSilentFail(name);
                if (towerDef != null)
                    foreach (var tower in pawn.Map.listerThings.ThingsOfDef(towerDef)) yield return tower;
            }
        }
        public override PathEndMode PathEndMode => PathEndMode.Touch;
        public override bool HasJobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            if (!(t is Building_NutrientEnergyTower tower) || !tower.CanReceiveNutrition
                || (!forced && !tower.HaulDestinationEnabled) || pawn.Faction != tower.Faction || tower.IsForbidden(pawn) ||
                tower.IsBurning() || !pawn.CanReserve(t, 1, -1, null, forced) ||
                pawn.Map.designationManager.DesignationOn(t, DesignationDefOf.Deconstruct) != null) return false;
            if (FindFood(pawn, tower) != null) return true;
            JobFailReason.Is("NoFood".Translate());
            return false;
        }
        public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            if (!HasJobOnThing(pawn, t, forced)) return null;
            var tower = (Building_NutrientEnergyTower)t;
            Thing item = FindFood(pawn, tower);
            return item == null ? null : HaulAIUtility.HaulToContainerJob(pawn, item, tower);
        }
        static Thing FindFood(Pawn pawn, Building_NutrientEnergyTower tower)
            => GenClosest.ClosestThingReachable(pawn.Position, pawn.Map, ThingRequest.ForGroup(ThingRequestGroup.FoodSourceNotPlantOrTree),
                PathEndMode.ClosestTouch, TraverseParms.For(pawn), 9999f,
                item => !item.IsForbidden(pawn) && pawn.CanReserve(item) && tower.Accepts(item));
    }

    // Resolve only legacy shadow towers before Scribe instantiates their saved class.
    // Thing IDs, faction, position and damage remain in the original serialized node.
    [HarmonyPatch(typeof(BackCompatibility), nameof(BackCompatibility.GetBackCompatibleType))]
    public static class ShadowEnergyTowerSaveCompatibility
    {
        static void Postfix(Type baseType, string providedClassName, XmlNode node, ref Type __result)
        {
            if (typeof(Thing).IsAssignableFrom(baseType)
                && providedClassName == typeof(Building_CommanderStructure).FullName
                && node?["def"]?.InnerText == "NMC_ShadowEnergyTower")
                __result = typeof(Building_NutrientEnergyTower);
        }
    }
}
