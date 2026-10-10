using System.Linq;
using RimWorld;
using Verse;

namespace NivarianCommander
{
    public static class DefenseActions
    {
        public static CommanderAbilityDef Ability(int number) => DefDatabase<CommanderAbilityDef>.AllDefsListForReading.First(d => d.number == number);
        public static string Unavailable(Map map, int number)
        {
            if (map == null || !Find.Maps.Contains(map)) return NmcText.Get("NMC_Text0034");
            var d = Ability(number); var state = CommanderState.Current;
            if (CommanderResearch.Unavailable(d) is string researchBlock) return researchBlock;
            if (state.Remaining(d) > 0) return NmcText.Get("NMC_Text0035", CommanderPanel.Period(state.Remaining(d)));
            if (number == 12 && state.overloadCharges <= 0) return NmcText.Get("NMC_Text0238");
            int power = FieldEconomy.PowerFor(d), volatileCost = FieldEconomy.VolatileFor(d);
            if (NativeLedger.Balance("PowerCoin") < power || NativeLedger.Balance("VolatileReactive") < volatileCost) return NmcText.Get("NMC_Text0036", power, volatileCost);
            return null;
        }
        static void Reject(string reason)
        { CommanderState.Current.requestsRejected++; Messages.Message(reason, MessageTypeDefOf.RejectInput, false); }
        static bool Pay(Map map, int number)
        {
            var blocked = Unavailable(map, number);
            if (blocked != null) { Reject(blocked); return false; }
            var d = Ability(number);
            if (!NativeLedger.TryPay(FieldEconomy.PowerFor(d), FieldEconomy.VolatileFor(d))) { Reject(NmcText.Get("NMC_Text0038")); return false; }
            if (number == 12) CommanderState.Current.TryConsumeCharge();
            else CommanderState.Current.cooldowns[d.defName] = FieldEconomy.CooldownFor(d);
            return true;
        }
        // Every map/target/resource check executes again at the synchronized command boundary.
        public static void DeployField(Map map, IntVec3 cell)
        {
            if (map == null || !cell.InBounds(map)) { Reject(NmcText.Get("NMC_Text0239")); return; }
            if (!Pay(map, 1)) return;
            CommanderField.Current.Deploy(map, cell);
        }
        public static void RemoveField(Map map)
        { if (map != null && Find.Maps.Contains(map) && CommanderField.Current.fieldMap == map) CommanderField.Current.Remove(); }
        public static void Shield(Map map)
        {
            if (!Pay(map, 3)) return;
            var defense = map.GetComponent<CommanderDefense>();
            foreach (var t in map.listerThings.AllThings.OrderBy(t => t.thingIDNumber).ToList())
                if (SupportDiplomacy.Friendly(t) && (!(t is Pawn p) || !p.Dead)) defense.Refresh(t, 3);
        }
        public static void CancelShield(Map map)
        { if (map != null && Find.Maps.Contains(map)) map.GetComponent<CommanderDefense>().Cancel(3); }
        public static void Overload(Map map, Thing target)
        {
            if (!(target is Building) || target.Destroyed || !target.Spawned || target.Map != map || !SupportDiplomacy.Friendly(target)) { Reject(NmcText.Get("NMC_Text0240")); return; }
            if (!Pay(map, 12)) return;
            map.GetComponent<CommanderDefense>().Refresh(target, 12);
        }
        public static void CancelOverload(Map map, Thing target)
        { if (map != null && Find.Maps.Contains(map) && target != null && target.Map == map) map.GetComponent<CommanderDefense>().Cancel(12, target); }
    }
}
