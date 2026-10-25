using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Nivarian.Helper;
using Nivarian_Race.Code.Defs;
using Nivarian_Race.Code.NivarianThing;
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
            try
            {
                foreach (var thing in TradeUtility.AllLaunchableThingsForTrade(map))
                    if (DonationItem(thing) && thing.MapHeld == map && cells.Contains(thing.PositionHeld)) goods[thing.thingIDNumber] = thing;
            }
            catch (Exception ex) { DonationWarning(null, "orbital export scan", ex); }
            // Donations also accept valid items rejected by traders, including corpses and chunks.
            foreach (var cell in cells.OrderBy(c => c.z).ThenBy(c => c.x))
                foreach (var root in cell.GetThingList(map).OrderBy(t => t.thingIDNumber))
                {
                    if (DonationItem(root)) goods[root.thingIDNumber] = root;
                    if (root is Pawn || root is Corpse || root is MinifiedThing) continue;
                    foreach (var thing in ContainerContents(root))
                        if (DonationItem(thing) && SafeDonationParents(thing)) goods[thing.thingIDNumber] = thing;
                }
            return goods.Values.OrderBy(t => t.thingIDNumber).ToList();
        }

        // Some mod containers return a ThingOwner without an Owner, null child
        // holders, or throw while enumerating. Keep traversal state local: the
        // vanilla recursive helper assumes valid holders and uses shared scratch.
        static List<Thing> ContainerContents(Thing root)
        {
            var contents = new List<Thing>();
            try
            {
                var owner = root.TryGetInnerInteractableThingOwner();
                if (owner == null) return contents;
                if (owner.Owner == null) throw new InvalidOperationException("Container has no owning holder.");
                var pending = new Stack<IThingHolder>();
                var visited = new HashSet<IThingHolder>(HolderIdentity.Instance);
                var children = new List<IThingHolder>();
                pending.Push(owner.Owner);
                while (pending.Count > 0)
                {
                    var holder = pending.Pop();
                    if (holder == null)
                    {
                        DonationWarning(root, "null child holder", null);
                        continue;
                    }
                    if (holder is Pawn || holder is Corpse || !visited.Add(holder)) continue;
                    if (visited.Count > 4096) throw new InvalidOperationException("Container holder graph exceeds 4096 nodes.");
                    if (ThingOwnerUtility.AreImmediateContentsReal(holder))
                    {
                        var held = holder.GetDirectlyHeldThings();
                        if (held != null) contents.AddRange(held);
                    }
                    children.Clear();
                    holder.GetChildHolders(children);
                    if (children.Count > 4096) throw new InvalidOperationException("Container has too many child holders.");
                    // Preserve the native traversal order; de-duplicate the result
                    // by thing ID at the donation boundary, without changing cargo.
                    foreach (var child in children) pending.Push(child);
                }
            }
            catch (Exception ex)
            {
                contents.Clear(); // Never offer a partially read failing container.
                DonationWarning(root, "container scan", ex);
            }
            return contents;
        }

        sealed class HolderIdentity : IEqualityComparer<IThingHolder>
        {
            internal static readonly HolderIdentity Instance = new HolderIdentity();
            public bool Equals(IThingHolder x, IThingHolder y) => ReferenceEquals(x, y);
            public int GetHashCode(IThingHolder value) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(value);
        }

        static bool SafeDonationParents(Thing thing)
        {
            try
            {
                var seen = new HashSet<IThingHolder>(HolderIdentity.Instance);
                for (var holder = thing.ParentHolder; holder != null; holder = holder.ParentHolder)
                {
                    if (holder is Pawn || holder is Corpse) return false;
                    if (!seen.Add(holder) || seen.Count > 4096) throw new InvalidOperationException("Invalid container parent chain.");
                }
                return true;
            }
            catch (Exception ex) { DonationWarning(thing, "parent chain", ex); return false; }
        }

        static void DonationWarning(Thing thing, string operation, Exception ex)
        {
            string def = thing?.def?.defName ?? "unknown";
            string type = thing?.GetType().FullName ?? "TradeUtility";
            string mod = thing?.def?.modContentPack?.PackageId ?? "unknown";
            string detail = ex == null ? "Ignored an empty holder." : ex.GetType().Name + ": " + ex.Message;
            Log.WarningOnce("[NMC-Shop] Donation " + operation + " skipped unsafe data: " + def + " #" +
                (thing?.thingIDNumber ?? 0) + " (" + type + ", mod=" + mod + "). " + detail,
                GenText.StableStringHash("NMC-Shop/Donation/" + operation + "/" + def + "/" + type));
        }

        static bool DonationItem(Thing thing)
        {
            if (thing == null || thing.Destroyed || thing.stackCount <= 0 || thing.def.category != ThingCategory.Item) return false;
            try
            {
                var inner = thing.GetInnerIfMinified();
                if (inner == null || inner is Pawn) return false;
                // The corpse is the explicitly selected item, including its dead pawn. Never
                // enumerate worn gear separately, or take a living pawn from a holder.
                if (inner is Corpse corpse) return corpse.InnerPawn?.Dead == true && TryUnitValue(thing, out _);
                var cargo = inner.TryGetInnerInteractableThingOwner();
                return (cargo == null || cargo.Count == 0) && TryUnitValue(thing, out _);
            }
            catch (Exception ex) { DonationWarning(thing, "item eligibility", ex); return false; }
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
            if (!PointShopDelivery.NativeSupplyAvailable) { Reply(request, false, NmcText.Get("NMC_Text0345")); return; }
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
        static NivarianMotherShipRequestDef NativeSupply => DefDatabase<NivarianMotherShipRequestDef>.GetNamedSilentFail("Request_Droppod_OrbitFood");
        public static bool NativeSupplyAvailable => NativeSupply != null
            && NivarianLotusDropPodDefOf.NivarianLotusDropMarker?.thingClass != null
            && typeof(NivarianLotusDropMarker).IsAssignableFrom(NivarianLotusDropPodDefOf.NivarianLotusDropMarker.thingClass)
            && NivarianLotusDropPodDefOf.NivarianLotusDropPodIncoming?.thingClass != null
            && typeof(NivarianLotusDropPodIncoming).IsAssignableFrom(NivarianLotusDropPodDefOf.NivarianLotusDropPodIncoming.thingClass)
            && NivarianLotusDropPodDefOf.NivarianLotusDropPodLanded?.thingClass != null
            && typeof(NivarianLotusDropPodLanded).IsAssignableFrom(NivarianLotusDropPodDefOf.NivarianLotusDropPodLanded.thingClass);
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
            // The native scheduler returns void and silently rejects invalid defs/cells.
            // Keep paid cargo in its saved owner until the full native chain is available.
            if (!NativeSupplyAvailable || !drop.InBounds(map)) return;
            var supply = NativeSupply;
            var batch = cargo.OrderBy(t => t.thingIDNumber).Take(12).ToList();
            if (batch.Count == 0) return;
            foreach (var item in batch) cargo.Remove(item);
            try
            {
                // Pass the purchased instances, including quality/stuff/health, into
                // the same saved marker -> incoming pod -> landed pod chain as supplies.
                NivarianHelper.ScheduleNivarianLotusDropPod(map, drop, batch,
                    supply.droppodMarkerDelayTicks, supply.droppodOpenDelaySeconds,
                    NivarianLotusDropPodDefOf.NivarianLotusDropMarker,
                    NivarianLotusDropPodDefOf.NivarianLotusDropPodIncoming,
                    NivarianLotusDropPodDefOf.NivarianLotusDropPodLanded);
            }
            catch (Exception ex)
            {
                foreach (var item in batch)
                {
                    if (!item.Destroyed && !item.Spawned && !ThingOwnerUtility.SpawnedOrAnyParentSpawned(item.ParentHolder))
                    { item.holdingOwner?.Remove(item); cargo.TryAdd(item, false); }
                }
                Log.ErrorOnce("[NMC-Shop] Native lotus delivery deferred: " + ex.Message, 1940611402);
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
