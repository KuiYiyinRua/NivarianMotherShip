using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Nivarian.Helper;
using Nivarian_Race.Code.Comps.BuildingComps;
using RimWorld;
using UnityEngine;
using Verse;

namespace NivarianCommander
{
    public sealed class CompProperties_CommanderConstruction : CompProperties
    {
        public ThingDef completedDef;
        public int constructionTicks = 1200;
        public bool cloakImmune;
        public List<ThingDefCountClass> ingredients = new List<ThingDefCountClass>();
        public CompProperties_CommanderConstruction() { compClass = typeof(CompCommanderConstruction); }
    }
    // Not deployed. Native container and native hauling hold the actual material objects.
    public sealed class CompCommanderConstruction : ThingComp
    {
        public bool started;
        public int elapsedTicks;
        bool completing;
        public CompProperties_CommanderConstruction Props => (CompProperties_CommanderConstruction)props;
        public CompUniversalContainer Container => parent.GetComp<CompUniversalContainer>();
        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            string error = ConfigurationError(parent.def);
            if (error != null) throw new InvalidOperationException("[NMC] " + error);
            if (!respawningAfterLoad) Container.SetRequiredIngredients(Props.ingredients);
        }
        public static string ConfigurationError(ThingDef core)
        {
            var construction = core?.GetCompProperties<CompProperties_CommanderConstruction>();
            if (construction == null || construction.completedDef == null || construction.constructionTicks <= 0)
                return NmcText.Get("NMC_Text0206");
            if (core.GetCompProperties<CompProperties_UniversalContainer>() == null)
                return NmcText.Get("NMC_Text0207");
            if (!typeof(Building_CommanderStructure).IsAssignableFrom(core.thingClass)
                || !typeof(Building_CommanderStructure).IsAssignableFrom(construction.completedDef.thingClass)
                || core.Size != construction.completedDef.Size || !core.useHitPoints || !core.leaveResourcesWhenKilled
                || core.costList?.Count > 0)
                return NmcText.Get("NMC_Text0208");
            if (construction.ingredients == null || construction.ingredients.Count == 0
                || construction.ingredients.Any(i => i == null || i.thingDef == null || i.count <= 0)
                || construction.ingredients.Select(i => i.thingDef).Distinct().Count() != construction.ingredients.Count)
                return NmcText.Get("NMC_Text0209");
            return null;
        }
        bool MaterialsPresent => Props.ingredients.All(i => Container.StoredCountOf(i.thingDef) >= i.count);
        public override void CompTick()
        {
            if (!SupportDiplomacy.Friendly(parent)) return;
            if (!MaterialsPresent)
            {
                // The native container can be emptied by its real UI while work is underway.
                // Restart only after the materials actually return; never build from a saved count.
                if (started) { started = false; elapsedTicks = 0; Container.SetRequiredIngredients(Props.ingredients); }
                return;
            }
            if (!started) { started = true; elapsedTicks = 0; Container.ClearRequirements(); return; }
            if (++elapsedTicks >= Props.constructionTicks) Complete();
        }
        void Complete()
        {
            var map = parent.Map; var cell = parent.Position; var rotation = parent.Rotation;
            var built = (Building)ThingMaker.MakeThing(Props.completedDef);
            built.SetFaction(parent.Faction);
            built.HitPoints = Mathf.Clamp(parent.HitPoints, 1, built.MaxHitPoints);
            foreach (var ingredient in Props.ingredients)
                Container.ConsumeThing(new ThingDefWithCount { thingDef = ingredient.thingDef, count = ingredient.count }, true);
            // Keep true surplus objects alive after the footprint is replaced.
            var surplus = new ThingOwner<Thing>();
            Container.GetIngredientsContainer().TryTransferAllToContainer(surplus);
            completing = true;
            parent.Destroy(DestroyMode.Vanish);
            GenSpawn.Spawn(built, cell, map, rotation);
            GenLeaving.DropThingOwnerContents(surplus, map, built.OccupiedRect().ExpandedBy(1), forbid: false);
        }
        public override void PostDestroy(DestroyMode mode, Map previousMap)
        {
            base.PostDestroy(mode, previousMap);
            if (completing || previousMap == null || Container == null) return;
            var held = Container.GetIngredientsContainer();
            if (mode == DestroyMode.Cancel || mode == DestroyMode.Deconstruct || mode == DestroyMode.Refund)
            { Container.PopOutThings(parent.Position, previousMap); return; }
            if (mode == DestroyMode.KillFinalize)
            {
                // Native frame leavings operate on actual held stacks, rather than a full unpaid recipe.
                var calculator = (Func<int, int>)AccessTools.Method(typeof(GenLeaving), "GetBuildingResourcesLeaveCalculator")
                    .Invoke(null, new object[] { parent, mode });
                var remains = new ThingOwner<Thing>();
                foreach (var stack in held.ToList())
                {
                    int count = calculator(stack.stackCount);
                    if (count > 0) held.TryTransferToContainer(stack, remains, count);
                }
                held.ClearAndDestroyContents();
                GenLeaving.DropThingOwnerContents(remains, previousMap, parent.OccupiedRect().ExpandedBy(1),
                    forbid: !previousMap.areaManager.Home[parent.Position]);
            }
            else held.ClearAndDestroyContents();
        }
        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref started, "nmcConstructionStarted");
            Scribe_Values.Look(ref elapsedTicks, "nmcConstructionLocalTicks");
        }
        public override string CompInspectStringExtra()
            => started ? NmcText.Get("NMC_Text0210", Math.Max(0, Props.constructionTicks - elapsedTicks) / 60f)
                : NmcText.Get("NMC_Text0211");
        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            if (parent.Faction != Faction.OfPlayer) yield break;
            yield return new Command_Action
            {
                defaultLabel = NmcText.Get("NMC_Text0212"),
                defaultDesc = NmcText.Get("NMC_Text0213"),
                icon = TexCommand.ClearPrioritizedWork,
                action = () => StructureActions.CancelCore(parent.Map, parent)
            };
        }
    }

    public sealed class CompProperties_CommanderStructure : CompProperties
    {
        public int incomePower, incomeVolatile;
        public float cloakRadius;
        public bool cloakImmune;
        public int incomeIntervalTicks = 60000;
        public CompProperties_CommanderStructure() { compClass = typeof(CompCommanderStructure); }
    }
    public sealed class CompCommanderStructure : ThingComp
    {
        public int incomeClock;
        public CompProperties_CommanderStructure Props => (CompProperties_CommanderStructure)props;
        public override void CompTick()
        {
            if ((Props.incomePower == 0 && Props.incomeVolatile == 0) || !SupportDiplomacy.Friendly(parent)) return;
            if (++incomeClock < Props.incomeIntervalTicks) return;
            var ledger = NativeLedger.Component?.CurrencyHold;
            var power = DefDatabase<Nivarian_Race.Code.Defs.NivarianMotherShipRequestCurrencyDef>.GetNamed("PowerCoin");
            var volatileCurrency = DefDatabase<Nivarian_Race.Code.Defs.NivarianMotherShipRequestCurrencyDef>.GetNamed("VolatileReactive");
            if (ledger == null) throw new InvalidOperationException("[NMC] Native income ledger absent");
            ledger.TryGetValue(power, out int p); ledger.TryGetValue(volatileCurrency, out int v);
            int nextPower = checked(p + Props.incomePower), nextVolatile = checked(v + Props.incomeVolatile);
            ledger[power] = nextPower;
            ledger[volatileCurrency] = nextVolatile;
            incomeClock -= Props.incomeIntervalTicks;
        }
        public override void PostExposeData()
        { base.PostExposeData(); Scribe_Values.Look(ref incomeClock, "nmcStructureIncomeLocalClock"); }
    }
    // Native turret threat identity gives cores and finished towers the same targeting priority.
    // The monolith's own weapon is a separate component; these methods do not add a second gun.
    public sealed class Building_CommanderStructure : Building_Turret
    {
        public override LocalTargetInfo CurrentTarget => LocalTargetInfo.Invalid;
        public override Verb AttackVerb => null;
        public override void OrderAttack(LocalTargetInfo target) { }
    }
    [HarmonyPatch(typeof(GenLeaving), "GetBuildingResourcesLeaveCalculator")]
    public static class CommanderStructureDeconstruction
    {
        static void Postfix(Thing destroyedThing, DestroyMode mode, ref Func<int, int> __result)
        {
            if (mode == DestroyMode.Deconstruct && destroyedThing.TryGetComp<CompCommanderStructure>() != null)
                __result = count => count / 2;
        }
    }
}
