using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Nivarian_Race.Code.Defs;
using RimWorld;
using Verse;

namespace NivarianCommander
{
    public static class PointShopTrade
    {
        public const decimal SilverPerPurchasePoint = 2m;
        // Quotes and donations retain raw silver market values; only purchases convert to P.
        public static decimal PurchaseCost(decimal marketValue) => marketValue / SilverPerPurchasePoint;
        public static decimal UnitValue(Thing thing) => TryUnitValue(thing, out decimal value) ? value : 0m;

        // Zero is a valid donation quote (e.g. chunks), but an invalid/throwing stat is not.
        static bool TryUnitValue(Thing thing, out decimal result)
        {
            result = 0m;
            if (thing == null || thing.Destroyed) return false;
            try
            {
                float value = thing.GetInnerIfMinified().MarketValue;
                if (float.IsNaN(value) || float.IsInfinity(value) || value < 0f || value >= (float)decimal.MaxValue) return false;
                result = (decimal)value;
                return true;
            }
            catch (Exception ex)
            {
                Log.WarningOnce("[NMC-Shop] Unavailable market value for " + thing.def.defName + ": " + ex.Message,
                    GenText.StableStringHash("NMC-Shop/Value/" + thing.def.defName));
                return false;
            }
        }
        public static string Exact(decimal value) => value.ToString(CultureInfo.InvariantCulture);
        public static string Display(decimal value) => value.ToString("0.######", CultureInfo.CurrentCulture);
        public static decimal Balance => NativeLedger.Balance("PowerCoin") + (PointShopState.Current?.purchaseCredit ?? 0m);
        public static string StockUnavailable => PointShopState.Current?.CatalogReady == true ? null : NmcText.Get("NMC_Text0331");

        public static string MapUnavailable(Map map)
        {
            if (map == null || !Find.Maps.Contains(map) || !map.IsPlayerHome || map.ParentFaction != Faction.OfPlayer) return NmcText.Get("NMC_Text0332");
            if (NativeLedger.Component?.CurrencyHold == null) return NmcText.Get("NMC_Text0333");
            if (!Building_OrbitalTradeBeacon.AllPowered(map).Any()) return NmcText.Get("NMC_Text0334");
            return null;
        }

        public static List<Thing> Donations(Map map)
        {
            var goods = new Dictionary<int, Thing>();
            if (MapUnavailable(map) != null) return goods.Values.ToList();
            var cells = new HashSet<IntVec3>();
            foreach (var beacon in Building_OrbitalTradeBeacon.AllPowered(map).OrderBy(b => b.thingIDNumber))
                foreach (var cell in beacon.TradeableCells) cells.Add(cell);
            // Preserve other mods' extensions of the official orbital export path.
            foreach (var thing in TradeUtility.AllLaunchableThingsForTrade(map))
                if (DonationItem(thing) && thing.MapHeld == map && cells.Contains(thing.PositionHeld)) goods[thing.thingIDNumber] = thing;
            // Donations also accept valid items rejected by traders, including corpses and chunks.
            foreach (var cell in cells.OrderBy(c => c.z).ThenBy(c => c.x))
                foreach (var root in cell.GetThingList(map).OrderBy(t => t.thingIDNumber))
                {
                    if (DonationItem(root)) goods[root.thingIDNumber] = root;
                    if (root is Pawn || root is Corpse || root is MinifiedThing) continue;
                    var owner = root.TryGetInnerInteractableThingOwner();
                    if (owner == null) continue;
                    var contents = new List<Thing>();
                    ThingOwnerUtility.GetAllThingsRecursively(owner.Owner, contents, false, h => !(h is Pawn) && !(h is Corpse));
                    foreach (var thing in contents)
                        if (DonationItem(thing) && !ThingOwnerUtility.AnyParentIs<Pawn>(thing)) goods[thing.thingIDNumber] = thing;
                }
            return goods.Values.OrderBy(t => t.thingIDNumber).ToList();
        }

        static bool DonationItem(Thing thing)
        {
            if (thing == null || thing.Destroyed || thing.stackCount <= 0 || thing.def.category != ThingCategory.Item) return false;
            var inner = thing.GetInnerIfMinified();
            if (inner is Pawn) return false;
            // The corpse is the explicitly selected item, including its dead pawn. Never
            // enumerate worn gear separately, or take a living pawn from a holder.
            if (inner is Corpse corpse) return corpse.InnerPawn?.Dead == true && TryUnitValue(thing, out _);
            var cargo = inner.TryGetInnerInteractableThingOwner();
            return (cargo == null || cargo.Count == 0) && TryUnitValue(thing, out _);
        }

        public static bool TryDropCell(Map map, out IntVec3 cell)
        {
            // Match the vanilla unroofed orbital landing rules, in stable beacon order.
            foreach (var beacon in Building_OrbitalTradeBeacon.AllPowered(map).OrderBy(b => b.thingIDNumber))
                if (DropCellFinder.TryFindDropSpotNear(beacon.Position, map, out cell, false, false, 40, false)) return true;
            cell = IntVec3.Invalid;
            return false;
        }

        static bool Request(PointShopState state, string request, out string reason)
        {
            reason = null;
            if (state == null || request.NullOrEmpty() || request.Length > 80) reason = NmcText.Get("NMC_Text0335");
            else if (state.completedRequests.Contains(request)) reason = NmcText.Get("NMC_Text0336");
            return reason == null;
        }

        static bool Quote(IEnumerable<Thing> available, int[] ids, int[] counts, string expected,
            out List<Thing> selected, out decimal value, out string reason, bool allowZeroValue = false)
        {
            selected = new List<Thing>(); value = 0m; reason = null;
            if (ids == null || counts == null || ids.Length == 0 || ids.Length != counts.Length || ids.Length > 4096 || ids.Distinct().Count() != ids.Length)
            { reason = NmcText.Get("NMC_Text0337"); return false; }
            var lookup = available.ToDictionary(t => t.thingIDNumber);
            for (int i = 0; i < ids.Length; i++)
            {
                if (counts[i] <= 0 || !lookup.TryGetValue(ids[i], out var thing) || thing.Destroyed || thing.stackCount < counts[i])
                { reason = NmcText.Get("NMC_Text0338"); return false; }
                if (!TryUnitValue(thing, out decimal unit) || (unit == 0m && !allowZeroValue) ||
                    unit > int.MaxValue || value > decimal.MaxValue - unit * counts[i])
                { reason = NmcText.Get("NMC_Text0339"); return false; }
                value += unit * counts[i]; selected.Add(thing);
            }
            if (!decimal.TryParse(expected, NumberStyles.Number, CultureInfo.InvariantCulture, out var quoted) || quoted != value)
            { reason = NmcText.Get("NMC_Text0340"); return false; }
            return true;
        }

        sealed class Detached
        {
            public Thing original, part;
            public ThingOwner owner;
            public Map map;
            public IntVec3 cell;
        }
        static List<Detached> Take(List<Thing> selected, int[] counts, ThingOwner destination)
        {
            var result = new List<Detached>();
            try
            {
                for (int i = 0; i < selected.Count; i++)
                {
                    var original = selected[i];
                    var entry = new Detached { original = original, owner = original.holdingOwner, map = original.Map, cell = original.Position };
                    entry.part = original.SplitOff(counts[i]); result.Add(entry);
                    if (entry.part == null || entry.part.stackCount != counts[i] || !destination.TryAdd(entry.part, false))
                        throw new InvalidOperationException("Could not transfer complete item stack.");
                }
                return result;
            }
            catch { Restore(result); throw; }
        }
        static void Restore(List<Detached> entries)
        {
            for (int i = entries.Count - 1; i >= 0; i--)
            {
                var e = entries[i]; var part = e.part;
                if (part == null || part.Destroyed) continue;
                part.holdingOwner?.Remove(part);
                if (part != e.original && !e.original.Destroyed && e.original.TryAbsorbStack(part, false)) continue;
                if (e.owner != null && e.owner.TryAdd(part, false)) continue;
                if (e.map != null && GenPlace.TryPlaceThing(part, e.cell, e.map, ThingPlaceMode.Near)) continue;
                // Stock is saved and visible, so an exceptional mod callback cannot orphan returned cargo.
                PointShopState.Current.stock.TryAdd(part, false);
                Log.Error("[NMC-Shop] Rollback used saved stock for " + part.ThingID);
            }
        }

        // These are the only UI-to-simulation transaction boundaries. MP registers both with a context map.
        public static void Buy(Map map, int generation, int[] ids, int[] counts, string expected, string request)
        {
            var state = PointShopState.Current;
            string reason = MapUnavailable(map);
            if (reason != null || !Request(state, request, out reason)) { Reply(request, false, reason); return; }
            if (!state.CatalogReady) { Reply(request, false, StockUnavailable); return; }
            if (state.generation != generation) { Reply(request, false, NmcText.Get("NMC_Text0341")); return; }
            if (!Quote(state.stock, ids, counts, expected, out var selected, out var value, out reason)) { Reply(request, false, reason); return; }
            decimal cost = PurchaseCost(value);
            decimal debit = Math.Ceiling(Math.Max(0m, cost - state.purchaseCredit));
            if (debit > int.MaxValue || debit > NativeLedger.Balance("PowerCoin")) { Reply(request, false, NmcText.Get("NMC_Text0342")); return; }
            if (!TryDropCell(map, out var drop)) { Reply(request, false, NmcText.Get("NMC_Text0343")); return; }
            var currency = DefDatabase<NivarianMotherShipRequestCurrencyDef>.GetNamedSilentFail("PowerCoin");
            if (currency == null) { Reply(request, false, NmcText.Get("NMC_Text0344")); return; }
            var deliveries = map.GetComponent<PointShopDelivery>();
            List<Detached> moved = null;
            try
            {
                moved = Take(selected, counts, deliveries.cargo);
                var trader = new ShopTrader(state);
                var buyer = map.mapPawns.FreeColonistsSpawned.OrderBy(p => p.thingIDNumber).FirstOrDefault();
                foreach (var entry in moved) entry.part.PreTraded(TradeAction.PlayerBuys, buyer, trader);
            }
            catch (Exception ex)
            {
                if (moved != null) Restore(moved);
                Log.Error("[NMC-Shop] Purchase transfer rejected: " + ex);
                Reply(request, false, NmcText.Get("NMC_Text0345")); return;
            }
            // Commit only after all selected instances are safely in a saved delivery container.
            NativeLedger.Component.CurrencyHold[currency] = NativeLedger.Balance("PowerCoin") - (int)debit;
            state.purchaseCredit = state.purchaseCredit + debit - cost;
            state.Complete(request);
            deliveries.Deliver(drop);
            Reply(request, true, NmcText.Get("NMC_Text0346", Display(cost)), "buy");
        }

        public static void Donate(Map map, int[] ids, int[] counts, string expected, string request)
        {
            var state = PointShopState.Current;
            string reason = MapUnavailable(map);
            if (reason != null || !Request(state, request, out reason)) { Reply(request, false, reason); return; }
            if (!Quote(Donations(map), ids, counts, expected, out var selected, out var value, out reason, allowZeroValue: true)) { Reply(request, false, reason); return; }
            decimal gain = Math.Floor(value / 10m);
            int current = NativeLedger.Balance("PowerCoin");
            if (gain > int.MaxValue - (long)current) { Reply(request, false, NmcText.Get("NMC_Text0347")); return; }
            var currency = DefDatabase<NivarianMotherShipRequestCurrencyDef>.GetNamedSilentFail("PowerCoin");
            if (currency == null) { Reply(request, false, NmcText.Get("NMC_Text0344")); return; }
            var bin = map.GetComponent<PointShopDelivery>().donated;
            try { Take(selected, counts, bin); }
            catch (Exception ex)
            {
                Log.Error("[NMC-Shop] Donation transfer rejected: " + ex);
                Reply(request, false, NmcText.Get("NMC_Text0348")); return;
            }
            NativeLedger.Component.CurrencyHold[currency] = current + (int)gain;
            state.Complete(request);
            // Already credited goods are isolated from both map and shop before their disposal.
            Reply(request, true, NmcText.Get("NMC_Text0349", Display(gain)), "donate");
        }

        static void Reply(string request, bool success, string message, string chat = "error")
        {
            foreach (var window in Find.WindowStack.Windows.OfType<Window_MothershipPointShop>().ToList())
                window.Receive(request, success, message, chat);
        }

        sealed class ShopTrader : ITrader
        {
            readonly PointShopState state;
            public ShopTrader(PointShopState state) { this.state = state; }
            public TraderKindDef TraderKind => DefDatabase<TraderKindDef>.GetNamedSilentFail("Orbital_BulkGoods");
            public IEnumerable<Thing> Goods => state.stock;
            public int RandomPriceFactorSeed => 0;
            public string TraderName => NmcText.Get("NMC_Text0350");
            public bool CanTradeNow => true;
            public float TradePriceImprovementOffsetForPlayer => 0f;
            public Faction Faction => null;
            public TradeCurrency TradeCurrency => TradeCurrency.Silver;
            public IEnumerable<Thing> ColonyThingsWillingToBuy(Pawn playerNegotiator) => Enumerable.Empty<Thing>();
            public void GiveSoldThingToTrader(Thing thing, int count, Pawn negotiator) => throw new InvalidOperationException("Use the synchronized point-shop donation order.");
            public void GiveSoldThingToPlayer(Thing thing, int count, Pawn negotiator) => throw new InvalidOperationException("Use the synchronized point-shop purchase order.");
        }
    }

    public sealed class PointShopDelivery : MapComponent, IThingHolder
    {
        public ThingOwner<Thing> cargo, donated;
        int retry;
        public PointShopDelivery(Map map) : base(map)
        { cargo = new ThingOwner<Thing>(this, false); donated = new ThingOwner<Thing>(this, false); }
        public IThingHolder ParentHolder => map;
        public ThingOwner GetDirectlyHeldThings() => cargo;
        public void GetChildHolders(List<IThingHolder> children)
        { ThingOwnerUtility.AppendThingHoldersFromThings(children, cargo); ThingOwnerUtility.AppendThingHoldersFromThings(children, donated); }
        public override void MapComponentTick()
        {
            if (--retry > 0) return;
            retry = 60;
            // The credited donation bin is never offered for purchase or refunded again.
            try { donated.ClearAndDestroyContents(); }
            catch (Exception ex) { Log.ErrorOnce("[NMC-Shop] Donation disposal deferred: " + ex.Message, 1940611401); }
            if (cargo.Count > 0 && PointShopTrade.TryDropCell(map, out var drop)) Deliver(drop);
        }
        public void Deliver(IntVec3 drop)
        {
            foreach (var item in cargo.OrderBy(t => t.thingIDNumber).Take(12).ToList())
            {
                cargo.Remove(item);
                try { TradeUtility.SpawnDropPod(drop, map, item); }
                catch (Exception ex)
                {
                    if (!item.Destroyed && !item.Spawned && !ThingOwnerUtility.SpawnedOrAnyParentSpawned(item.ParentHolder))
                    { item.holdingOwner?.Remove(item); cargo.TryAdd(item, false); }
                    Log.ErrorOnce("[NMC-Shop] Orbital delivery deferred: " + ex.Message, 1940611402);
                }
            }
        }
        public override void ExposeData()
        {
            Scribe_Deep.Look(ref cargo, "pointShopPaidCargo", this);
            Scribe_Deep.Look(ref donated, "pointShopDonatedGoods", this);
            Scribe_Values.Look(ref retry, "pointShopDeliveryRetry");
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (cargo == null) cargo = new ThingOwner<Thing>(this, false);
                if (donated == null) donated = new ThingOwner<Thing>(this, false);
            }
        }
    }
}
