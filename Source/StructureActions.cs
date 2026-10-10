using System.Linq;
using RimWorld;
using Verse;

namespace NivarianCommander
{
    public static class StructureActions
    {
        public static ThingDef CoreFor(int number) => DefDatabase<ThingDef>.GetNamedSilentFail(
            number == 8 ? "NMC_ShadowCore" : number == 17 ? "NMC_MonolithCore" : "");
        public static string Unavailable(Map map, int number)
        {
            if (number != 8 && number != 17) return NmcText.Get("NMC_Text0463");
            string blocked = DefenseActions.Unavailable(map, number);
            return blocked ?? (CompCommanderConstruction.ConfigurationError(CoreFor(number)) == null ? null : NmcText.Get("NMC_Text0464"));
        }
        public static string PlacementError(Map map, IntVec3 center, ThingDef core)
        {
            if (map == null || !Find.Maps.Contains(map) || core == null) return NmcText.Get("NMC_Text0465");
            foreach (var cell in GenAdj.OccupiedRect(center, Rot4.North, core.Size))
            {
                if (!cell.InBounds(map) || cell.x <= 0 || cell.z <= 0 || cell.x >= map.Size.x - 1 || cell.z >= map.Size.z - 1)
                    return NmcText.Get("NMC_Text0466");
                if (cell.Fogged(map)) return NmcText.Get("NMC_Text0467");
                if (cell.GetThingList(map).Any(t => t.def.category == ThingCategory.Building
                    || t.def.category == ThingCategory.Pawn || t.def.category == ThingCategory.Item
                    || t.def.passability == Traversability.Impassable)) return NmcText.Get("NMC_Text0468");
            }
            // The approved edge rule is exactly the outermost row, rather than vanilla's wider edge area.
            var report = GenConstruct.CanPlaceBlueprintAt(core, center, Rot4.North, map,
                godMode: false, ignoreEdgeArea: true);
            return report.Accepted ? null : report.Reason;
        }
        static void Reject(string reason)
        {
            CommanderState.Current.requestsRejected++;
            Messages.Message(reason, MessageTypeDefOf.RejectInput, false);
        }
        public static void DeployCore(Map map, IntVec3 cell, int number)
        {
            string error = Unavailable(map, number);
            var core = CoreFor(number);
            if (error == null) error = PlacementError(map, cell, core);
            if (error != null) { Reject(error); return; }
            // Construct before touching the shared ledger. Config/target validation is repeated in this executor.
            var building = (Building)ThingMaker.MakeThing(core);
            building.SetFaction(Faction.OfPlayer);
            var ability = DefenseActions.Ability(number);
            if (!NativeLedger.TryPay(ability.powerCost, ability.volatileCost))
            { building.Destroy(DestroyMode.Vanish); Reject(NmcText.Get("NMC_Text0038")); return; }
            CommanderState.Current.cooldowns[ability.defName] = ability.cooldownTicks;
            GenSpawn.Spawn(building, cell, map, Rot4.North);
        }
        public static void CancelCore(Map map, Thing core)
        {
            if (map == null || !Find.Maps.Contains(map) || core == null || core.Map != map
                || core.Destroyed || !core.Spawned || core.Faction != Faction.OfPlayer
                || core.TryGetComp<CompCommanderConstruction>() == null) return;
            core.Destroy(DestroyMode.Cancel);
        }
    }
}
