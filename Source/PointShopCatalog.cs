using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace NivarianCommander
{
    // Only called by the world's refresh, never by a window, search or tooltip.
    public static class PointShopCatalog
    {
        public const int KindsPerRefresh = 60;
        public const int CatalogVersion = 3;
        public const string OtherCategory = "NMC_OtherGoods";

        public static string CategoryKey(ThingDef def)
        {
            if (def?.thingCategories == null || def.thingCategories.Count == 0) return OtherCategory;
            var roots = new List<ThingCategoryDef>();
            foreach (var category in def.thingCategories)
            {
                var root = category;
                int guard = 0;
                while (root?.parent != null && root.parent.defName != "Root" && ++guard < 32) root = root.parent;
                if (root != null && root.defName != "Root") roots.Add(root);
            }
            return roots.OrderBy(x => x.defName, StringComparer.Ordinal).FirstOrDefault()?.defName ?? OtherCategory;
        }

        public static string CategoryLabel(string key) => key == OtherCategory ? NmcText.Get("NMC_Text0305") :
            DefDatabase<ThingCategoryDef>.GetNamedSilentFail(key)?.LabelCap.ToString() ?? key;

        static bool Candidate(ThingDef def)
        {
            try { return CandidateDefinition(def); }
            catch (Exception ex)
            {
                Log.WarningOnce("[NMC-Shop] Invalid merchandise definition " + def?.defName + ": " + ex.Message,
                    GenText.StableStringHash("NMC-Shop/Def/" + def?.defName));
                return false;
            }
        }
        static bool CandidateDefinition(ThingDef def)
        {
            if (def == null || def.thingClass == null || def.thingClass.IsAbstract || !def.PlayerAcquirable ||
                !def.tradeability.TraderCanSell() || typeof(Pawn).IsAssignableFrom(def.thingClass) ||
                typeof(Corpse).IsAssignableFrom(def.thingClass) || typeof(MinifiedThing).IsAssignableFrom(def.thingClass)) return false;
            if (def.category != ThingCategory.Item && !(def.category == ThingCategory.Building && def.Minifiable)) return false;
            float value = def.BaseMarketValue;
            return !float.IsNaN(value) && !float.IsInfinity(value) && (value > 0f || def.MadeFromStuff);
        }

        static IEnumerable<StockGenerator> OfficialRules()
        {
            foreach (var trader in DefDatabase<TraderKindDef>.AllDefsListForReading
                .Where(t => t.orbital && t.modContentPack != null && t.modContentPack.IsOfficialMod)
                .OrderBy(t => t.defName, StringComparer.Ordinal))
                foreach (var rule in trader.stockGenerators)
                    if (rule.GetType().Assembly == typeof(StockGenerator).Assembly &&
                        !(rule.GetType().Name.StartsWith("StockGenerator_Buy", StringComparison.Ordinal)) &&
                        !(rule is StockGenerator_Animals) && !(rule is StockGenerator_Slaves) &&
                        (rule.countRange.max > 0 || rule.totalPriceRange.max > 0f || rule.customCountRanges?.Any(c => c.countRange.max > 0) == true))
                        yield return rule;
        }

        static int Specificity(StockGenerator rule) => rule is StockGenerator_SingleDef ? 0 :
            rule is StockGenerator_Category ? 1 : rule is StockGenerator_Tag ? 2 : 3;

        static bool Handles(StockGenerator rule, ThingDef def) => (int)def.techLevel <= (int)rule.maxTechLevelGenerate && rule.HandlesThingDef(def);

        static StockGenerator SelectRule(ThingDef def, List<StockGenerator> rules, List<ThingDef> officialGoods)
        {
            var exact = rules.Where(r => Handles(r, def)).OrderBy(Specificity).ToList();
            if (exact.Count != 0)
            {
                int rank = Specificity(exact[0]);
                var best = exact.Where(r => Specificity(r) == rank).ToList();
                return best[Rand.Range(0, best.Count)];
            }
            // Mod goods without a trade tag inherit the nearest official category's supply rule.
            var ancestors = new List<ThingCategoryDef>();
            foreach (var start in (IEnumerable<ThingCategoryDef>)def.thingCategories ?? Enumerable.Empty<ThingCategoryDef>())
            {
                var visited = new HashSet<ThingCategoryDef>();
                for (var category = start; category != null && category.defName != "Root" && visited.Add(category); category = category.parent)
                    if (!ancestors.Contains(category)) ancestors.Add(category);
            }
            foreach (var category in ancestors)
            {
                var analog = officialGoods.FirstOrDefault(d => d.IsWithinCategory(category) && rules.Any(r => Handles(r, d)));
                if (analog != null) return rules.Where(r => Handles(r, analog)).OrderBy(Specificity).First();
            }
            return null;
        }

        sealed class CountSampler : StockGenerator
        {
            public int Count(StockGenerator rule, ThingDef def)
            {
                // MiscItems.countRange is a whole assortment, each selected piece has quantity 1.
                if (rule == null || rule is StockGenerator_MiscItems) return 1;
                countRange = rule.countRange; totalPriceRange = rule.totalPriceRange; customCountRanges = rule.customCountRanges;
                for (int i = 0; i < 100; i++)
                {
                    int count = RandomCountOf(def);
                    if (count > 0) return count;
                }
                return 0;
            }
            public override IEnumerable<Thing> GenerateThings(PlanetTile forTile, Faction faction = null) { yield break; }
            public override bool HandlesThingDef(ThingDef thingDef) => false;
        }

        public static List<Thing> Generate(PlanetTile tile)
        {
            using (PointShopNarrativeSafety.EnterStockGeneration()) return GenerateCore(tile);
        }

        static List<Thing> GenerateCore(PlanetTile tile)
        {
            var goods = DefDatabase<ThingDef>.AllDefsListForReading.Where(Candidate).OrderBy(d => d.defName, StringComparer.Ordinal).ToList();
            var rules = OfficialRules().ToList();
            var official = goods.Where(d => d.modContentPack?.IsOfficialMod == true).ToList();
            var fallbackTrader = DefDatabase<TraderKindDef>.GetNamedSilentFail("Orbital_BulkGoods");
            var result = new List<Thing>();
            var sampler = new CountSampler();
            // Draw one assortment across all categories. Each definition appears
            // once in this pool; categories are only filters for the resulting stock.
            for (int i = 0; i < goods.Count; i++)
            {
                int j = Rand.Range(i, goods.Count);
                var temp = goods[i]; goods[i] = goods[j]; goods[j] = temp;
            }
            int added = 0;
            foreach (var def in goods)
            {
                if (added >= KindsPerRefresh) break;
                var generated = new List<Thing>();
                try
                {
                    var rule = SelectRule(def, rules, official);
                    int count = sampler.Count(rule, def);
                    if (count <= 0) continue;
                    foreach (var original in StockGeneratorUtility.TryMakeForStock(def, count, null))
                    {
                        generated.Add(original);
                        original.PostGeneratedForTrader(rule?.trader ?? fallbackTrader, tile, null);
                        if (original.def.Minifiable)
                        {
                            int stack = original.stackCount; original.stackCount = 1;
                            var minified = original.MakeMinified(); minified.stackCount = stack;
                            generated[generated.Count - 1] = minified;
                        }
                    }
                    if (generated.Count == 0 || generated.Any(t => t.Destroyed || t.stackCount <= 0 || PointShopTrade.UnitValue(t) <= 0m))
                        throw new InvalidOperationException("Item has no usable trader stock or market value.");
                    result.AddRange(generated);
                    added++;
                }
                catch (Exception ex)
                {
                    foreach (var thing in generated) if (!thing.Destroyed && !thing.Spawned) thing.Destroy(DestroyMode.Vanish);
                    Log.WarningOnce("[NMC-Shop] Skipped invalid merchandise " + def.defName + ": " + ex.Message,
                        GenText.StableStringHash("NMC-Shop/" + def.defName));
                }
            }
            return result;
        }
    }
}
