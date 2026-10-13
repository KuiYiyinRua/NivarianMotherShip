using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace NivarianCommander
{
    public static class LegionPurchase
    {
        public const string CooldownKey = "LegionShared";
        public const int CooldownTicks = 420000;
        public static Faction NativeFaction => Find.FactionManager.AllFactionsListForReading.FirstOrDefault(f =>
            f.def.defName == "NivarianSurvivorTeam" && !f.IsPlayer && !f.defeated && !f.HostileTo(Faction.OfPlayer));
        // Use the game's beacon query, including its powered-beacon, sellability,
        // holder and overlap semantics. UI reads never mutate goods or the ledger.
        public static List<Thing> Goods(Map map) => map == null ? new List<Thing>() :
            TradeUtility.AllLaunchableThingsForTrade(map).Where(t => t != null && !t.Destroyed && t.stackCount > 0)
                .Distinct().OrderBy(t => t.thingIDNumber).ToList();
        public static long Count(Map map, ThingDef def) => Goods(map).Where(t => t.def == def).Sum(t => (long)t.stackCount);
        public static string Unavailable(Map map, int ability)
        {
            string blocked = DefenseActions.Unavailable(map, ability);
            if (blocked != null) return blocked;
            if (NativeFaction == null) return NmcText.Get("NMC_Text0281");
            if (ability != 15 && GroundReinforcementArrival.Unavailable != null) return GroundReinforcementArrival.Unavailable;
            return null;
        }
    }
}
