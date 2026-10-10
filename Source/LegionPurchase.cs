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
            var goods = Goods(map);
            if (goods.Where(t => t.def == ThingDefOf.Silver).Sum(t => (long)t.stackCount) < 2000 &&
                goods.Where(t => t.def == ThingDefOf.Gold).Sum(t => (long)t.stackCount) < 200)
                return NmcText.Get("NMC_Text0282");
            return null;
        }
        internal sealed class Payment : IDisposable
        {
            sealed class Piece { public Thing source, taken; public int amount; }
            readonly List<Piece> pieces = new List<Piece>();
            bool settled;
            public ThingDef Currency { get; private set; }
            public int Amount { get; private set; }
            public Payment(Map map)
            {
                var goods = Goods(map);
                Currency = goods.Where(t => t.def == ThingDefOf.Silver).Sum(t => (long)t.stackCount) >= 2000
                    ? ThingDefOf.Silver : ThingDefOf.Gold;
                Amount = Currency == ThingDefOf.Silver ? 2000 : 200;
                int left = Amount;
                try
                {
                    foreach (Thing source in goods.Where(t => t.def == Currency))
                    {
                        if (left == 0) break;
                        int amount = Math.Min(left, source.stackCount);
                        // Whole stacks stay in their original holder/location until
                        // commit. Only partial stacks are split into refundable escrow.
                        Thing taken = amount == source.stackCount ? source : source.SplitOff(amount);
                        pieces.Add(new Piece { source = source, taken = taken, amount = amount });
                        if (taken == null || taken.Destroyed || taken.stackCount != amount)
                            throw new InvalidOperationException("Native currency split failed");
                        left -= amount;
                    }
                    if (left != 0) throw new InvalidOperationException("Native beacon goods changed before payment");
                }
                catch { Dispose(); throw; }
            }
            public void Commit()
            {
                foreach (var piece in pieces) piece.taken.Destroy(DestroyMode.Vanish);
                settled = true;
            }
            public void Dispose()
            {
                if (settled) return;
                foreach (var piece in pieces.AsEnumerable().Reverse())
                    if (piece.taken != null && piece.taken != piece.source && !piece.taken.Destroyed)
                    {
                        if (piece.source.Destroyed || !piece.source.TryAbsorbStack(piece.taken, false))
                            throw new InvalidOperationException("Cannot restore native currency escrow");
                    }
                settled = true;
            }
        }
    }
}
