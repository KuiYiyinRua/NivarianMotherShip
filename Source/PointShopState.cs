using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using RimWorld.Planet;
using Verse;

namespace NivarianCommander
{
    public sealed class PointShopState : WorldComponent, IThingHolder
    {
        public const int RefreshInterval = 7 * 60000;
        public ThingOwner<Thing> stock;
        public int generation, revision, refreshRemaining;
        public int catalogVersion;
        public bool CatalogReady => catalogVersion == PointShopCatalog.CatalogVersion;
        public decimal purchaseCredit;
        public List<string> completedRequests = new List<string>();
        public PointShopState(World world) : base(world) { stock = new ThingOwner<Thing>(this, false); }
        public static PointShopState Current => Find.World?.GetComponent<PointShopState>();
        public IThingHolder ParentHolder => null;
        public ThingOwner GetDirectlyHeldThings() => stock;
        public void GetChildHolders(List<IThingHolder> children) => ThingOwnerUtility.AppendThingHoldersFromThings(children, stock);

        public override void WorldComponentTick()
        {
            if (refreshRemaining > 0 && --refreshRemaining > 0) return;
            var map = Find.Maps.Where(m => m.IsPlayerHome).OrderBy(m => m.uniqueID).FirstOrDefault();
            if (map == null || NativeLedger.Component == null) return;
            int seed = Rand.Int;
            // Explicit map context protects third-party stock initialization from the local camera.
            ProjectionContext.Run(map, () =>
            {
                Rand.PushState(seed);
                try
                {
                    var next = PointShopCatalog.Generate(map.Tile);
                    var replacement = new ThingOwner<Thing>(this, false);
                    foreach (var item in next)
                        if (!replacement.TryAdd(item, false)) throw new InvalidOperationException("Could not store generated merchandise.");
                    var expired = stock;
                    stock = replacement;
                    generation++; revision++; refreshRemaining = RefreshInterval;
                    catalogVersion = PointShopCatalog.CatalogVersion;
                    foreach (var item in expired.ToList())
                    {
                        try { if (!item.Destroyed) item.Destroy(DestroyMode.Vanish); }
                        catch (Exception ex) { Log.WarningOnce("[NMC-Shop] Expired stock cleanup: " + ex.Message, GenText.StableStringHash("NMC-Shop/Expired/" + item.def.defName)); }
                        finally { expired.Remove(item); }
                    }
                }
                catch (Exception ex)
                {
                    refreshRemaining = 600;
                    Log.ErrorOnce("[NMC-Shop] Stock refresh deferred: " + ex, 1940611403);
                }
                finally { Rand.PopState(); }
            });
        }

        public void Complete(string id)
        {
            completedRequests.Add(id);
            if (completedRequests.Count > 256) completedRequests.RemoveAt(0);
            revision++;
        }

        public override void ExposeData()
        {
            Scribe_Deep.Look(ref stock, "pointShopStock", this);
            Scribe_Values.Look(ref generation, "pointShopGeneration");
            Scribe_Values.Look(ref revision, "pointShopRevision");
            Scribe_Values.Look(ref refreshRemaining, "pointShopRefreshRemaining");
            Scribe_Values.Look(ref catalogVersion, "pointShopCatalogVersion");
            string exactCredit = purchaseCredit.ToString(CultureInfo.InvariantCulture);
            Scribe_Values.Look(ref exactCredit, "pointShopPurchaseCredit", "0");
            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                decimal.TryParse(exactCredit, NumberStyles.Number, CultureInfo.InvariantCulture, out purchaseCredit);
                if (purchaseCredit < 0m || purchaseCredit >= 1m) purchaseCredit = 0m;
            }
            Scribe_Collections.Look(ref completedRequests, "pointShopCompletedRequests", LookMode.Value);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (stock == null) stock = new ThingOwner<Thing>(this, false);
                if (completedRequests == null) completedRequests = new List<string>();
                refreshRemaining = Math.Max(0, Math.Min(RefreshInterval, refreshRemaining));
                // Version 3 uses sixty kinds across the entire shop. Replace only
                // unpaid stock on the next world tick; credit, receipts and paid cargo remain.
                if (catalogVersion != PointShopCatalog.CatalogVersion) refreshRemaining = 0;
            }
        }
    }
}
