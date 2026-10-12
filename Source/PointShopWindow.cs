using System;
using System.Collections.Generic;
using System.Linq;
using Nivarian.Helper;
using Nivarian_Race.Code.UI;
using RimWorld;
using UnityEngine;
using Verse;
using Theme = NivarianCommander.CommanderPanelTheme;

namespace NivarianCommander
{
    public sealed class MainButtonWorker_PointShop : MainButtonWorker
    {
        public override bool Visible => base.Visible && Current.ProgramState == ProgramState.Playing && NativeLedger.Component != null;
        public override void Activate()
        {
            var existing = Find.WindowStack.Windows.OfType<Window_MothershipPointShop>().FirstOrDefault();
            if (existing != null) existing.Close();
            else Find.WindowStack.Add(new Window_MothershipPointShop());
        }
    }

    public sealed class PointShopOrder
    {
        public Map map;
        public bool donation, includesCorpses;
        public int generation;
        public int[] ids, counts;
        public string[] labels;
        public decimal[] units;
        public decimal value;
        public string request;
    }

    public sealed class Window_MothershipPointShop : Window_NivarianTerminalBase
    {
        static readonly Color Cyan = Theme.Cyan;
        static readonly Color Muted = Theme.Muted;
        readonly Dictionary<int, int> cart = new Dictionary<int, int>();
        readonly Dictionary<int, string> buffers = new Dictionary<int, string>();
        List<Thing> rows = new List<Thing>();
        readonly Dictionary<string, int> categoryCounts = new Dictionary<string, int>();
        PointShopNira nira;
        Map selectedMap;
        bool donation;
        string category = "", source = "", search = "", status, pending;
        int observedRevision = -1, observedGeneration = -1;
        float nextScan;
        Vector2 itemScroll, categoryScroll, cartScroll, compactScroll;

        public override Vector2 InitialSize => new Vector2(Mathf.Min(1180f, UI.screenWidth - 76f), Mathf.Min(780f, UI.screenHeight - 64f));
        public Window_MothershipPointShop()
        {
            doCloseX = true; forcePause = false; absorbInputAroundWindow = true;
            selectedMap = Find.CurrentMap?.IsPlayerHome == true ? Find.CurrentMap : HomeMaps().FirstOrDefault();
        }
        public override void PreOpen()
        {
            base.PreOpen(); closeOnClickedOutside = false; layer = WindowLayer.Dialog; doWindowBackground = false;
            nira = new PointShopNira();
        }
        public override void DoWindowContents(Rect inRect) => Theme.WindowContents(inRect, DrawHeader, DrawContent);
        public override void PostClose() { nira?.Dispose(); nira = null; base.PostClose(); }
        static IEnumerable<Map> HomeMaps() => Find.Maps.Where(m => m.IsPlayerHome && m.ParentFaction == Faction.OfPlayer).OrderBy(m => m.uniqueID);
        static string MapLabel(Map map) => map?.Parent?.LabelCap.ToString() ?? NmcText.Get("NMC_Text0351");
        static string ItemLabel(Thing thing) => thing.GetInnerIfMinified().LabelNoCount.CapitalizeFirst();
        static string SourceName(Thing thing) => thing.GetInnerIfMinified().def.modContentPack?.Name ?? "RimWorld";

        internal static void Label(Rect rect, string text, Color color, GameFont font = GameFont.Small, TextAnchor anchor = TextAnchor.UpperLeft)
        {
            var oldFont = Text.Font; var oldAnchor = Text.Anchor; var oldColor = GUI.color;
            try
            {
                Text.Font = font; Text.Anchor = anchor; GUI.color = color;
                if (!text.NullOrEmpty() && Text.CalcHeight(text, rect.width) > rect.height)
                {
                    TooltipHandler.TipRegion(rect, text);
                    text = text.Replace("\n", " ").Truncate(rect.width);
                }
                Widgets.Label(rect, text);
            }
            finally { Text.Font = oldFont; Text.Anchor = oldAnchor; GUI.color = oldColor; }
        }
        static void Panel(Rect rect) => Theme.Panel(rect);
        internal static void HeaderPanel(Rect rect) => Theme.Insignia(new Rect(rect.x, rect.y + 1, 46, 46));
        static bool Button(Rect rect, string label, bool active = true, bool selected = false, bool quiet = false)
        {
            bool enabled = GUI.enabled;
            try { GUI.enabled = enabled && active; return Theme.Button(rect, label, quiet, selected); }
            finally { GUI.enabled = enabled; }
        }
        protected override void DrawHeader(Rect rect)
        {
            HeaderPanel(rect);
            Label(new Rect(rect.x + 60, rect.y, rect.width - 164, 32), NmcText.Get("NMC_Text0350"), Theme.White, GameFont.Medium);
            if (Button(new Rect(rect.xMax - 78, rect.y + 1, 78, 28), NmcText.Get("NMC_Text0352"), true, false, true)) Close();
            int ticks = Math.Max(0, PointShopState.Current?.refreshRemaining ?? 0);
            string refresh = PointShopState.Current?.CatalogReady != true ? NmcText.Get("NMC_Text0353") : NmcText.Get("NMC_Text0354", ticks / 60000, ticks % 60000 / 2500);
            float refreshWidth = rect.width < 850 ? 205 : 250;
            Label(new Rect(rect.xMax - refreshWidth, rect.y + 37, refreshWidth, 24), refresh, Theme.Gold, GameFont.Tiny, TextAnchor.MiddleRight);
            Label(new Rect(rect.x + 60, rect.y + 36, rect.width - refreshWidth - 80, 27), NmcText.Get("NMC_Text0355"), Muted, GameFont.Tiny);
        }
        void Rebuild()
        {
            var state = PointShopState.Current;
            if (state == null) return;
            if (observedGeneration != state.generation)
            {
                if (observedGeneration > 0) { nira?.Say("refresh"); if (!donation) { cart.Clear(); buffers.Clear(); status = NmcText.Get("NMC_Text0356"); } }
                observedGeneration = state.generation;
            }
            if (selectedMap != null && !Find.Maps.Contains(selectedMap)) { selectedMap = HomeMaps().FirstOrDefault(); cart.Clear(); }
            if (observedRevision == state.revision && Time.realtimeSinceStartup < nextScan) return;
            nextScan = Time.realtimeSinceStartup + .75f; observedRevision = state.revision;
            rows = (donation ? PointShopTrade.Donations(selectedMap) : state.CatalogReady ? state.stock.ToList() : new List<Thing>())
                .Where(t => t != null && !t.Destroyed && t.stackCount > 0)
                .OrderBy(ItemLabel, StringComparer.CurrentCulture).ThenBy(t => t.thingIDNumber).ToList();
            categoryCounts.Clear();
            foreach (var group in rows.GroupBy(t => PointShopCatalog.CategoryKey(t.GetInnerIfMinified().def)))
                categoryCounts[group.Key] = group.Select(t => t.GetInnerIfMinified().def).Distinct().Count();
            categoryCounts[""] = rows.Select(t => t.GetInnerIfMinified().def).Distinct().Count();
            var valid = new HashSet<int>(rows.Select(t => t.thingIDNumber));
            foreach (var key in cart.Keys.Where(k => !valid.Contains(k)).ToList()) { cart.Remove(key); buffers.Remove(key); }
        }
        protected override void DrawContent(Rect rect)
        {
            if (rect.height >= 340 && rect.width >= 560) { DrawShopContent(rect); return; }
            var inner = new Rect(0, 0, rect.width - 18, Mathf.Max(650, rect.height));
            Widgets.BeginScrollView(rect, ref compactScroll, inner);
            try { DrawShopContent(inner); }
            finally { Widgets.EndScrollView(); }
        }
        void DrawShopContent(Rect rect)
        {
            Rebuild();
            bool wide = rect.width >= 1000f, stacked = rect.width < 690f;
            DrawToolbar(new Rect(rect.x, rect.y, rect.width, 70), !wide);
            string notice = status ?? PointShopTrade.MapUnavailable(selectedMap) ?? (donation ? NmcText.Get("NMC_Text0357") : PointShopTrade.StockUnavailable ?? NmcText.Get("NMC_Text0358"));
            var statusRect = new Rect(rect.x + 5, rect.y + 74, rect.width - 10, 24);
            bool shortStack = stacked && rect.height < 440;
            if (!shortStack) Label(statusRect, notice, Muted, GameFont.Tiny);
            TooltipHandler.TipRegion(shortStack ? new Rect(rect.x, rect.y, rect.width, 30) : statusRect, notice);
            float bodyTop = shortStack ? 78 : 102;
            var body = new Rect(rect.x, rect.y + bodyTop, rect.width, rect.height - bodyTop);
            Rect list, summary;
            if (stacked)
            {
                // Keep the checkout and dialogue inside an 800x600 window; only the
                // merchandise list scrolls at this size, not the entire checkout.
                float summaryHeight = Mathf.Clamp(body.height * .52f, shortStack ? 198 : 216, 250);
                list = new Rect(body.x, body.y, body.width, body.height - summaryHeight - 10);
                summary = new Rect(body.x, list.yMax + 10, body.width, summaryHeight);
            }
            else
            {
                float nav = wide ? 166 : 0;
                float summaryWidth = Mathf.Clamp(body.width * .29f, 276, 322);
                summary = new Rect(body.xMax - summaryWidth, body.y, summaryWidth, body.height);
                list = new Rect(body.x + nav, body.y, body.width - nav - summaryWidth - 12, body.height);
                if (wide) DrawCategories(new Rect(body.x, body.y, 154, body.height));
            }
            DrawItems(list);
            DrawSummary(summary);
        }
        void DrawToolbar(Rect rect, bool compact)
        {
            bool idle = pending == null;
            if (Button(new Rect(rect.x, rect.y, 103, 30), (donation ? NmcText.Get("NMC_Text0359") : NmcText.Get("NMC_Text0360")).Replace("◆ ", ""), idle, !donation) && donation) Switch(false);
            if (Button(new Rect(rect.x + 111, rect.y, 103, 30), (donation ? NmcText.Get("NMC_Text0361") : NmcText.Get("NMC_Text0362")).Replace("◆ ", ""), idle, donation) && !donation) Switch(true);
            if (Button(new Rect(rect.x + 228, rect.y, rect.width - 228, 30), NmcText.Get("NMC_Text0363", MapLabel(selectedMap)), idle))
            {
                var options = HomeMaps().Select(m => new FloatMenuOption(MapLabel(m), () =>
                { selectedMap = m; if (donation) { cart.Clear(); buffers.Clear(); } observedRevision = -1; status = null; })).ToList();
                if (options.Count > 0) Find.WindowStack.Add(new FloatMenu(options));
            }
            float categoryWidth = compact ? 134 : 0, sourceWidth = rect.width < 600 ? 112 : 172;
            float searchWidth = rect.width - categoryWidth - sourceWidth - (compact ? 16 : 8);
            var searchRect = new Rect(rect.x, rect.y + 40, searchWidth, 28);
            search = Theme.TextField(searchRect, search);
            TooltipHandler.TipRegion(searchRect, NmcText.Get("NMC_Text0364"));
            if (search.Length == 0 && !Mouse.IsOver(searchRect)) Label(searchRect.ContractedBy(5, 2), NmcText.Get("NMC_Text0365"), Muted, GameFont.Tiny);
            float x = searchRect.xMax + 8;
            if (compact)
            {
                if (Button(new Rect(x, searchRect.y, categoryWidth, 28), category.Length == 0 ? NmcText.Get("NMC_Text0366") : PointShopCatalog.CategoryLabel(category)))
                {
                    var options = new List<FloatMenuOption> { new FloatMenuOption(NmcText.Get("NMC_Text0366"), () => { category = ""; itemScroll = Vector2.zero; }) };
                    options.AddRange(CategoryKeys().Select(k => new FloatMenuOption(PointShopCatalog.CategoryLabel(k), () => { category = k; itemScroll = Vector2.zero; })));
                    Find.WindowStack.Add(new FloatMenu(options));
                }
                x += categoryWidth + 8;
            }
            if (Button(new Rect(x, searchRect.y, sourceWidth, 28), source.Length == 0 ? NmcText.Get("NMC_Text0367") : source))
            {
                var options = new List<FloatMenuOption> { new FloatMenuOption(NmcText.Get("NMC_Text0367"), () => source = "") };
                options.AddRange(rows.Select(SourceName).Distinct().OrderBy(s => s).Select(s => new FloatMenuOption(s, () => source = s)));
                Find.WindowStack.Add(new FloatMenu(options));
            }
        }
        void Switch(bool donate)
        { donation = donate; cart.Clear(); buffers.Clear(); category = source = search = ""; status = null; observedRevision = -1; itemScroll = Vector2.zero; }
        IEnumerable<string> CategoryKeys() => categoryCounts.Keys.Where(k => k.Length != 0).OrderBy(PointShopCatalog.CategoryLabel);
        void DrawCategories(Rect rect)
        {
            Panel(rect);
            Label(new Rect(rect.x + 10, rect.y + 8, rect.width - 20, 23), NmcText.Get("NMC_Text0368"), Cyan);
            var keys = new[] { "" }.Concat(CategoryKeys()).ToList();
            var outer = new Rect(rect.x + 6, rect.y + 39, rect.width - 12, rect.height - 45);
            var inner = new Rect(0, 0, outer.width - 16, keys.Count * 47);
            Widgets.BeginScrollView(outer, ref categoryScroll, inner);
            for (int i = 0; i < keys.Count; i++)
            {
                string key = keys[i]; var button = new Rect(0, i * 47, inner.width, 42);
                if (category == key) { Widgets.DrawBoxSolid(button, new Color(.10f, .19f, .255f, .96f)); Theme.Frame(button, Theme.Gold); }
                else if (Mouse.IsOver(button)) Widgets.DrawBoxSolid(button, new Color(.07f, .14f, .19f, .96f));
                Label(new Rect(7, button.y + 2, button.width - 10, 23), key.Length == 0 ? NmcText.Get("NMC_Text0369") : PointShopCatalog.CategoryLabel(key), category == key ? Theme.Gold : Theme.White, GameFont.Tiny);
                categoryCounts.TryGetValue(key, out int kinds);
                Label(new Rect(7, button.y + 22, button.width - 10, 18), NmcText.Get("NMC_Text0370", kinds), Muted, GameFont.Tiny);
                if (Widgets.ButtonInvisible(button)) { category = key; itemScroll = Vector2.zero; }
            }
            Widgets.EndScrollView();
        }
        void DrawItems(Rect rect)
        {
            Panel(rect);
            string query = search.Trim();
            var filtered = rows.Where(t => (category.Length == 0 || PointShopCatalog.CategoryKey(t.GetInnerIfMinified().def) == category) &&
                (source.Length == 0 || SourceName(t) == source) && (query.Length == 0 ||
                (ItemLabel(t) + " " + SourceName(t) + " " + t.GetInnerIfMinified().def.defName).IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)).ToList();
            Label(new Rect(rect.x + 10, rect.y + 7, rect.width - 20, 24), donation ? NmcText.Get("NMC_Text0371") : NmcText.Get("NMC_Text0372"), Cyan);
            var outer = new Rect(rect.x + 5, rect.y + 36, rect.width - 10, Mathf.Max(20, rect.height - 42));
            var inner = new Rect(0, 0, outer.width - 16, Mathf.Max(outer.height, filtered.Count * 78));
            Widgets.BeginScrollView(outer, ref itemScroll, inner);
            if (filtered.Count == 0) Label(new Rect(10, 20, inner.width - 20, 70), donation ? NmcText.Get("NMC_Text0373") : PointShopTrade.StockUnavailable ?? NmcText.Get("NMC_Text0374"), Muted);
            int first = Math.Max(0, (int)(itemScroll.y / 78) - 1), last = Math.Min(filtered.Count, (int)((itemScroll.y + outer.height) / 78) + 2);
            for (int i = first; i < last; i++) DrawItem(new Rect(0, i * 78, inner.width, 72), filtered[i], i % 2 == 0);
            Widgets.EndScrollView();
        }
        void DrawItem(Rect rect, Thing thing, bool even)
        {
            cart.TryGetValue(thing.thingIDNumber, out int count);
            Widgets.DrawBoxSolid(rect, count > 0 ? new Color(.10f, .19f, .255f, .96f)
                : even || Mouse.IsOver(rect) ? new Color(.06f, .12f, .17f, .9f) : Theme.Surface);
            Widgets.DrawBoxSolid(new Rect(rect.x, rect.yMax - 1, rect.width, 1), Theme.Line);
            if (count > 0) Widgets.DrawBoxSolid(new Rect(rect.x, rect.y + 8, 2, rect.height - 16), Theme.Gold);
            var icon = new Rect(rect.x + 8, rect.y + 14, 44, 44);
            Theme.Frame(icon.ExpandedBy(3), count > 0 ? Theme.Gold : Theme.Line);
            Widgets.ThingIcon(icon, thing);
            if (Widgets.ButtonInvisible(icon)) Find.WindowStack.Add(new Dialog_InfoCard(thing.GetInnerIfMinified()));
            float controls = 104, textWidth = Math.Max(55, rect.width - controls - 76);
            var name = new Rect(rect.x + 65, rect.y + 5, textWidth, 25);
            Label(name, ItemLabel(thing), Theme.White, rect.width < 450 ? GameFont.Tiny : GameFont.Small);
            Label(new Rect(name.x, rect.y + 31, textWidth, 18), SourceName(thing), Muted, GameFont.Tiny);
            decimal marketValue = PointShopTrade.UnitValue(thing);
            string price = PointShopTrade.Display(PointShopTrade.PurchaseCost(marketValue)) + " P";
            Label(new Rect(name.x, rect.y + 51, textWidth, 18), donation ? NmcText.Get("NMC_Text0375", thing.stackCount) : NmcText.Get("NMC_Text0376", price, thing.stackCount), Cyan, GameFont.Tiny);
            TooltipHandler.TipRegion(new Rect(rect.x, rect.y, rect.width - controls, rect.height),
                NmcText.Get("NMC_Text0379", ItemLabel(thing), SourceName(thing), (donation ? NmcText.Get("NMC_Text0377") : NmcText.Get("NMC_Text0378", price))));
            int old = count;
            string buffer = buffers.TryGetValue(thing.thingIDNumber, out string saved) ? saved : count.ToString();
            bool enabled = GUI.enabled; GUI.enabled = enabled && pending == null;
            float x = rect.xMax - controls;
            int step = Event.current.control ? 100 : Event.current.shift ? 10 : 1;
            if (Theme.Button(new Rect(x, rect.y + 23, 24, 27), "−", true)) { count = Math.Max(0, count - step); buffer = count.ToString(); }
            Theme.Quantity(new Rect(x + 25, rect.y + 23, 50, 27), ref count, ref buffer, thing.stackCount);
            if (Theme.Button(new Rect(x + 76, rect.y + 23, 24, 27), "+", true)) { count = (int)Math.Min(thing.stackCount, (long)count + step); buffer = count.ToString(); }
            GUI.enabled = enabled;
            buffers[thing.thingIDNumber] = buffer;
            if (count != old) { if (count <= 0) cart.Remove(thing.thingIDNumber); else cart[thing.thingIDNumber] = count; }
        }
        void DrawSummary(Rect rect)
        {
            Panel(rect);
            bool compact = rect.height < 360;
            var selected = rows.Where(t => cart.ContainsKey(t.thingIDNumber)).ToList();
            decimal value = selected.Sum(t => PointShopTrade.UnitValue(t) * cart[t.thingIDNumber]);
            decimal total = donation ? Math.Floor(value / 10m) : PointShopTrade.PurchaseCost(value);
            Label(new Rect(rect.x + 12, rect.y + 9, rect.width - 24, 27), NmcText.Get("NMC_Text0380", PointShopTrade.Display(PointShopTrade.Balance)), Theme.Gold);
            TooltipHandler.TipRegion(new Rect(rect.x, rect.y, rect.width, 36), NmcText.Get("NMC_Text0381"));
            if (!compact)
            {
                Label(new Rect(rect.x + 12, rect.y + 39, rect.width - 24, 42), donation ? NmcText.Get("NMC_Text0382") : NmcText.Get("NMC_Text0383"), Muted, GameFont.Tiny);
                var view = new Rect(rect.x + 10, rect.y + 87, rect.width - 20, Math.Max(20, rect.height - 330));
                var content = new Rect(0, 0, view.width - 16, selected.Count * 43);
                Widgets.BeginScrollView(view, ref cartScroll, content);
                for (int i = 0; i < selected.Count; i++)
                {
                    var thing = selected[i]; string line = cart[thing.thingIDNumber] + " × " + ItemLabel(thing);
                    Label(new Rect(0, i * 43, content.width, 38), line, Theme.White, GameFont.Tiny);
                    TooltipHandler.TipRegion(new Rect(0, i * 43, content.width, 40), line);
                }
                Widgets.EndScrollView();
            }
            float totalY = compact ? rect.y + 39 : rect.yMax - 234;
            if (!compact) Widgets.DrawBoxSolid(new Rect(rect.x + 12, totalY - 10, rect.width - 24, 1), Theme.Line);
            Label(new Rect(rect.x + 12, totalY, rect.width - 24, 24), (donation ? NmcText.Get("NMC_Text0384") : NmcText.Get("NMC_Text0385")) + PointShopTrade.Display(total) + " P", Theme.Gold);
            Label(new Rect(rect.x + 12, totalY + 28, rect.width - 78, 22), NmcText.Get("NMC_Text0386", selected.Count), Muted, GameFont.Tiny);
            if (pending == null && Theme.Button(new Rect(rect.xMax - 78, totalY + 24, 66, 25), NmcText.Get("NMC_Text0387"), true)) { cart.Clear(); buffers.Clear(); }
            string blocked = PointShopTrade.MapUnavailable(selectedMap) ?? (donation ? null : PointShopTrade.StockUnavailable);
            var button = new Rect(rect.x + 12, totalY + 60, rect.width - 24, 34);
            bool canSubmit = pending == null && selected.Count > 0 && blocked == null && (donation || total <= PointShopTrade.Balance);
            if (Button(button, pending != null ? NmcText.Get("NMC_Text0388") : donation ? NmcText.Get("NMC_Text0389") : NmcText.Get("NMC_Text0390"), canSubmit)) Review(selected);
            TooltipHandler.TipRegion(button, blocked ?? (!donation && total > PointShopTrade.Balance ? NmcText.Get("NMC_Text0391") : NmcText.Get("NMC_Text0392")));
            var talk = new Rect(rect.x + 8, totalY + 105, rect.width - 16, Math.Max(compact ? 48 : 72, rect.yMax - totalY - 113));
            nira?.Draw(talk);
        }
        void Review(List<Thing> selected)
        {
            selected = selected.OrderBy(t => t.thingIDNumber).ToList();
            var order = new PointShopOrder
            {
                map = selectedMap, donation = donation, generation = PointShopState.Current.generation,
                includesCorpses = donation && selected.Any(t => t.GetInnerIfMinified() is Corpse),
                ids = selected.Select(t => t.thingIDNumber).ToArray(), counts = selected.Select(t => cart[t.thingIDNumber]).ToArray(),
                labels = selected.Select(ItemLabel).ToArray(), units = selected.Select(PointShopTrade.UnitValue).ToArray(), request = Guid.NewGuid().ToString("N")
            };
            order.value = selected.Sum(t => PointShopTrade.UnitValue(t) * cart[t.thingIDNumber]);
            Find.WindowStack.Add(new Window_PointShopOrder(order, Submit));
        }
        void Submit(PointShopOrder order)
        {
            if (pending != null) return;
            pending = order.request; status = NmcText.Get("NMC_Text0393");
            if (order.donation) PointShopTrade.Donate(order.map, order.ids, order.counts, PointShopTrade.Exact(order.value), order.request);
            else PointShopTrade.Buy(order.map, order.generation, order.ids, order.counts, PointShopTrade.Exact(order.value), order.request);
        }
        public void Receive(string request, bool success, string message, string chat)
        {
            if (pending != request) return;
            pending = null; status = message; observedRevision = -1;
            if (success) { cart.Clear(); buffers.Clear(); }
            nira?.Say(chat);
        }
    }

    public sealed class Window_PointShopOrder : Window_NivarianTerminalBase
    {
        readonly PointShopOrder order;
        readonly Action<PointShopOrder> submit;
        Vector2 scroll;
        public override Vector2 InitialSize => new Vector2(Mathf.Min(700, UI.screenWidth - 80), Mathf.Min(590, UI.screenHeight - 70));
        public Window_PointShopOrder(PointShopOrder order, Action<PointShopOrder> submit)
        { this.order = order; this.submit = submit; doCloseX = true; absorbInputAroundWindow = true; forcePause = false; }
        public override void PreOpen() { base.PreOpen(); closeOnClickedOutside = false; layer = WindowLayer.Dialog; doWindowBackground = false; }
        public override void DoWindowContents(Rect inRect) => Theme.WindowContents(inRect, DrawHeader, DrawContent);
        protected override void DrawHeader(Rect rect)
        {
            Window_MothershipPointShop.HeaderPanel(rect);
            Window_MothershipPointShop.Label(new Rect(rect.x + 60, rect.y + 5, rect.width - 68, 44), order.donation ? NmcText.Get("NMC_Text0394") : NmcText.Get("NMC_Text0395"), Theme.White, GameFont.Medium);
        }
        protected override void DrawContent(Rect rect)
        {
            Window_MothershipPointShop.Label(new Rect(rect.x, rect.y, rect.width, 26), NmcText.Get("NMC_Text0363", order.map.Parent.LabelCap), Theme.Cyan);
            decimal total = order.donation ? Math.Floor(order.value / 10m) : PointShopTrade.PurchaseCost(order.value);
            string note = order.donation ? NmcText.Get("NMC_Text0396") : NmcText.Get("NMC_Text0397");
            if (order.donation && total == 0) note = NmcText.Get("NMC_Text0398");
            if (order.includesCorpses) note += NmcText.Get("NMC_Text0399");
            var oldFont = Text.Font;
            Text.Font = GameFont.Tiny;
            float noteHeight = Text.CalcHeight(note, rect.width - 10) + 4;
            Text.Font = oldFont;
            float footerHeight = Mathf.Max(120, noteHeight + 82);
            var outer = new Rect(rect.x, rect.y + 36, rect.width, Mathf.Max(20, rect.height - footerHeight - 44));
            var inner = new Rect(0, 0, outer.width - 16, order.ids.Length * 52);
            Widgets.BeginScrollView(outer, ref scroll, inner);
            for (int i = 0; i < order.ids.Length; i++)
            {
                Widgets.DrawBoxSolid(new Rect(0, i * 52, inner.width, 48), Theme.Surface);
                Widgets.DrawBoxSolid(new Rect(0, i * 52, 2, 48), Theme.Line);
                Window_MothershipPointShop.Label(new Rect(10, i * 52, inner.width - 20, 25), order.labels[i] + " × " + order.counts[i], Theme.White);
                decimal lineValue = order.units[i] * order.counts[i];
                string linePrice = order.donation ? NmcText.Get("NMC_Text0400") : NmcText.Get("NMC_Text0401", PointShopTrade.Display(PointShopTrade.PurchaseCost(lineValue)));
                Window_MothershipPointShop.Label(new Rect(10, i * 52 + 27, inner.width - 20, 22), linePrice, Theme.Muted, GameFont.Tiny);
            }
            Widgets.EndScrollView();
            float y = rect.yMax - footerHeight;
            Widgets.DrawBoxSolid(new Rect(rect.x, y - 8, rect.width, 1), Theme.Line);
            Window_MothershipPointShop.Label(new Rect(rect.x + 5, y, rect.width - 10, 28), (order.donation ? NmcText.Get("NMC_Text0402") : NmcText.Get("NMC_Text0403")) + PointShopTrade.Display(total), Theme.Gold);
            Window_MothershipPointShop.Label(new Rect(rect.x + 5, y + 32, rect.width - 10, noteHeight), note, Theme.Cyan, GameFont.Tiny);
            if (Theme.Button(new Rect(rect.x, rect.yMax - 37, rect.width * .42f, 34), NmcText.Get("NMC_Text0404"), true)) Close();
            if (Theme.Button(new Rect(rect.x + rect.width * .46f, rect.yMax - 37, rect.width * .54f, 34), order.donation ? NmcText.Get("NMC_Text0405", total) : NmcText.Get("NMC_Text0406")))
            { Close(); submit(order); }
        }
    }
}
