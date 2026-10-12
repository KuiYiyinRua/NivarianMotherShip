using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using Theme = NivarianCommander.CommanderPanelTheme;

namespace NivarianCommander
{
    public sealed class CommanderSettings : ModSettings
    {
        public bool showShortcut = true;
        public bool unlockAllResearch;
        public bool hudCollapsed, hudPositioned;
        public float hudCenterX = .5f, hudCenterY;
        public List<string> hudSlots = new List<string> { "", "", "", "", "", "" };
        public List<string> order = new List<string>();
        public override void ExposeData()
        {
            Scribe_Values.Look(ref showShortcut, "showCommanderShortcut", true);
            Scribe_Values.Look(ref unlockAllResearch, "unlockAllResearch", false);
            Scribe_Values.Look(ref hudCollapsed, "commanderHudCollapsed");
            Scribe_Values.Look(ref hudPositioned, "commanderHudPositioned");
            Scribe_Values.Look(ref hudCenterX, "commanderHudCenterX", .5f);
            Scribe_Values.Look(ref hudCenterY, "commanderHudCenterY");
            Scribe_Collections.Look(ref hudSlots, "commanderHudSlots", LookMode.Value);
            Scribe_Collections.Look(ref order, "commanderOrder", LookMode.Value);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                order = order ?? new List<string>();
                hudSlots = hudSlots ?? new List<string>();
                while (hudSlots.Count < 6) hudSlots.Add("");
                if (hudSlots.Count > 6) hudSlots.RemoveRange(6, hudSlots.Count - 6);
            }
        }
        public List<CommanderAbilityDef> Ordered()
        {
            var defs = DefDatabase<CommanderAbilityDef>.AllDefsListForReading.OrderBy(d => d.number).ToList();
            return defs.OrderBy(d => order.Contains(d.defName) ? order.IndexOf(d.defName) : 100 + d.number).ToList();
        }
        public void Move(CommanderAbilityDef def, int direction)
        {
            var defs = Ordered();
            int i = defs.IndexOf(def), j = i + direction;
            if (i < 0 || j < 0 || j >= defs.Count) return;
            defs[i] = defs[j]; defs[j] = def;
            order = defs.Select(d => d.defName).ToList(); Write();
        }
    }
    public sealed class CommanderPanel
    {
        static readonly Color Cyan = Theme.Cyan;
        static readonly Color Muted = Theme.Muted;
        readonly Dictionary<int, Texture2D> icons = new Dictionary<int, Texture2D>();
        static string[] Groups => new[] { NmcText.Get("NMC_Text0074"), NmcText.Get("NMC_Text0075"), NmcText.Get("NMC_Text0076"), NmcText.Get("NMC_Text0077"), NmcText.Get("NMC_Text0078"), NmcText.Get("NMC_Text0079") };
        public string group = NmcText.Get("NMC_Text0074");
        public int selected = 2;
        Vector2 scroll;
        Vector2 detailScroll;
        int detailNumber = -1;
        static void Label(Rect rect, string text, Color color, GameFont font = GameFont.Small)
        {
            var oldFont = Text.Font; var oldColor = GUI.color; var oldAnchor = Text.Anchor;
            try
            {
                Text.Font = font; Text.Anchor = TextAnchor.UpperLeft; GUI.color = color;
                if (!text.NullOrEmpty() && Text.CalcHeight(text, rect.width) > rect.height)
                {
                    TooltipHandler.TipRegion(rect, text);
                    text = text.Replace("\n", " ").Truncate(rect.width);
                }
                Widgets.Label(rect, text);
            }
            finally { Text.Font = oldFont; GUI.color = oldColor; Text.Anchor = oldAnchor; }
        }
        public void Draw(Rect rect, Action back)
        {
            var oldFont = Text.Font; var oldAnchor = Text.Anchor; var oldColor = GUI.color;
            bool oldEnabled = GUI.enabled, oldWrap = Text.WordWrap;
            GUI.BeginGroup(rect);
            try { Text.WordWrap = true; DrawContents(new Rect(0, 0, rect.width, rect.height), back); }
            finally { GUI.EndGroup(); Text.Font = oldFont; Text.Anchor = oldAnchor; GUI.color = oldColor; GUI.enabled = oldEnabled; Text.WordWrap = oldWrap; }
        }
        void DrawContents(Rect rect, Action back)
        {
            var groups = Groups;
            if (!groups.Contains(group)) group = groups[0];
            Text.Anchor = TextAnchor.UpperLeft;
            Theme.Backdrop(rect);
            const float pad = 16f, gap = 12f;
            float headerTrim = rect.height < 640 ? 20 : 0;
            Theme.Insignia(new Rect(pad, 14, 48, 48));
            Label(new Rect(76, 13, rect.width - 100, 32), NmcText.Get("NMC_Text0080"), Theme.White, GameFont.Medium);
            Label(new Rect(78, 48, rect.width - 100, 22), NmcText.Get("NMC_Text0081"), Muted, GameFont.Tiny);
            Widgets.DrawBoxSolid(new Rect(pad, 79 - headerTrim / 2, rect.width - pad * 2, 1), Theme.Line);
            float metricWidth = (rect.width - pad * 2 - gap * 3) / 4f;
            float metricY = 88 - headerTrim / 2, metricHeight = 52 - headerTrim / 2;
            Metric(new Rect(pad, metricY, metricWidth, metricHeight), NmcText.Get("NMC_Text0082"), NativeLedger.Balance("PowerCoin").ToString("N0"), "P");
            Metric(new Rect(pad + metricWidth + gap, metricY, metricWidth, metricHeight), NmcText.Get("NMC_Text0083"), NativeLedger.Balance("VolatileReactive").ToString("N0"), "V");
            Metric(new Rect(pad + (metricWidth + gap) * 2, metricY, metricWidth, metricHeight), NmcText.Get("NMC_Text0084"), (NativeLedger.Component?.GetLevel ?? 0).ToString(), "LV");
            Metric(new Rect(pad + (metricWidth + gap) * 3, metricY, metricWidth, metricHeight), NmcText.Get("NMC_Text0085"), (CommanderState.Current?.overloadCharges ?? 0) + " / 3", "+");

            var main = new Rect(pad, 154 - headerTrim, rect.width - pad * 2, rect.height - 202 + headerTrim);
            float sidebarWidth = rect.width < 900 ? 138 : 158;
            float detailWidth = Mathf.Clamp(rect.width * .33f, 280, 338);
            var sidebar = new Rect(main.x, main.y, sidebarWidth, main.height);
            var details = new Rect(main.xMax - detailWidth, main.y, detailWidth, main.height);
            var list = new Rect(sidebar.xMax + gap, main.y, details.x - sidebar.xMax - gap * 2, main.height);
            Theme.Panel(sidebar); Theme.Panel(list); Theme.Panel(details);
            Label(new Rect(sidebar.x + 12, sidebar.y + 12, sidebar.width - 24, 24), NmcText.Get("NMC_Text0086"), Cyan, GameFont.Tiny);
            for (int i = 0; i < groups.Length; i++)
            {
                Rect button = new Rect(sidebar.x + 8, sidebar.y + 42 + i * 42, sidebar.width - 16, 34);
                if (Theme.Button(button, groups[i], true, group == groups[i]))
                {
                    group = groups[i]; scroll = Vector2.zero;
                    var first = CommanderMod.Settings.Ordered().FirstOrDefault(d => i == 0 || d.group == group);
                    if (first != null && !DefDatabase<CommanderAbilityDef>.AllDefsListForReading.Any(d => d.number == selected && (i == 0 || d.group == group)))
                        selected = first.number;
                }
            }
            if (sidebar.height > 350)
            {
                if (sidebar.height > 440) Theme.Insignia(new Rect(sidebar.center.x - 24, sidebar.yMax - 132, 48, 48));
                Label(new Rect(sidebar.x + 12, sidebar.yMax - 71, sidebar.width - 24, 58), NmcText.Get("NMC_Text0087"), Muted, GameFont.Tiny);
            }
            var filtered = CommanderMod.Settings.Ordered().Where(d => group == groups[0] || d.group == group).ToList();
            Label(new Rect(list.x + 12, list.y + 10, list.width - 65, 24), group, Theme.White);
            Label(new Rect(list.xMax - 42, list.y + 12, 32, 22), filtered.Count.ToString("00"), Theme.Gold, GameFont.Tiny);
            Widgets.DrawBoxSolid(new Rect(list.x + 10, list.y + 36, list.width - 20, 1), Theme.Line);
            var listBody = new Rect(list.x + 8, list.y + 45, list.width - 16, list.height - 53);
            var view = new Rect(0, 0, listBody.width - 18, filtered.Count * 86);
            Widgets.BeginScrollView(listBody, ref scroll, view);
            try
            {
                for (int i = 0; i < filtered.Count; i++)
                {
                    var row = new Rect(0, i * 86, view.width, 78);
                    if (row.yMax >= scroll.y && row.y <= scroll.y + listBody.height) DrawRow(row, filtered[i]);
                }
            }
            finally { Widgets.EndScrollView(); }
            var chosen = DefDatabase<CommanderAbilityDef>.AllDefsListForReading.FirstOrDefault(d => d.number == selected);
            if (chosen != null) Detail(details.ContractedBy(14), chosen);
            Widgets.DrawBoxSolid(new Rect(pad, rect.height - 39, rect.width - pad * 2, 1), Theme.Line);
            Text.Font = GameFont.Small;
            string backLabel = NmcText.Get("NMC_Text0088");
            float backWidth = Mathf.Clamp(Text.CalcSize(backLabel).x + 24, 154, 260);
            if (Theme.Button(new Rect(pad, rect.height - 32, backWidth, 27), backLabel, true)) back();
            bool show = CommanderMod.Settings.showShortcut;
            Text.Font = GameFont.Small;
            float toggleX = pad + backWidth + 16;
            Widgets.CheckboxLabeled(new Rect(toggleX, rect.height - 29, Mathf.Min(320, rect.width - toggleX - pad), 24), NmcText.Get("NMC_Text0089"), ref show);
            if (show != CommanderMod.Settings.showShortcut) { CommanderMod.Settings.showShortcut = show; CommanderMod.Settings.Write(); }
            if (rect.width - toggleX - 320 > 270) Label(new Rect(rect.width - 270, rect.height - 27, 254, 22), NmcText.Get("NMC_Text0090"), Muted, GameFont.Tiny);
        }

        Texture2D Icon(CommanderAbilityDef d)
        {
            if (!icons.TryGetValue(d.number, out var icon))
            {
                string path = CommanderQuickActions.IconPath(d);
                icon = path.NullOrEmpty() ? null : ContentFinder<Texture2D>.Get(path, false);
                icons[d.number] = icon;
            }
            return icon;
        }

        void DrawIcon(Rect r, CommanderAbilityDef d, bool selected)
        {
            Widgets.DrawBoxSolid(r, new Color(.025f, .065f, .10f, .95f));
            Theme.Frame(r, selected ? Theme.Gold : Theme.Line);
            var icon = Icon(d);
            if (icon != null) GUI.DrawTexture(r.ContractedBy(3), icon, ScaleMode.ScaleToFit);
            else Theme.Insignia(r.ContractedBy(5));
        }

        void DrawRow(Rect row, CommanderAbilityDef d)
        {
            bool chosen = d.number == selected;
            bool hover = Mouse.IsOver(row);
            Widgets.DrawBoxSolid(row, chosen ? new Color(.10f, .19f, .255f, .96f) : hover ? new Color(.07f, .14f, .19f, .96f) : Theme.Surface);
            Theme.Frame(row, chosen ? Theme.Gold : Theme.Line);
            if (chosen) Widgets.DrawBoxSolid(new Rect(row.x, row.y + 12, 2, row.height - 24), Theme.Gold);
            DrawIcon(new Rect(row.x + 10, row.y + 11, 56, 56), d, chosen);
            float x = row.x + 78, width = row.width - 90;
            Label(new Rect(x, row.y + 5, width, 18), d.Code, chosen ? Theme.Gold : Cyan, GameFont.Tiny);
            Label(new Rect(x, row.y + 22, width, 27), d.label, Theme.White);
            Label(new Rect(x, row.y + 52, width, 21), FieldEconomy.PowerFor(d) + " P  ·  " + FieldEconomy.VolatileFor(d) + " V  /  " + Cooldown(d), Muted, GameFont.Tiny);
            if (Widgets.ButtonInvisible(row)) selected = d.number;
            TooltipHandler.TipRegion(row, d.LabelCap + "\n\n" + d.description);
        }

        static void Metric(Rect r, string label, string value, string code)
        {
            Widgets.DrawBoxSolid(r, Theme.Surface);
            Widgets.DrawBoxSolid(new Rect(r.x, r.yMax - 1, r.width, 1), Theme.Line);
            Theme.Polygon(new Vector2(r.x + 20, r.center.y), 13, 4, Theme.Line);
            Text.Font = GameFont.Tiny;
            float codeWidth = Text.CalcSize(code).x;
            Label(new Rect(r.x + 20 - codeWidth / 2, r.center.y - 9, 30, 20), code, Theme.Gold, GameFont.Tiny);
            Label(new Rect(r.x + 43, r.y + 3, r.width - 50, 18), label, Muted, GameFont.Tiny);
            Label(new Rect(r.x + 43, r.y + 20, r.width - 50, r.height - 18), value, Cyan, r.height < 50 ? GameFont.Small : GameFont.Medium);
        }
        void Detail(Rect r, CommanderAbilityDef d)
        {
            if (detailNumber != d.number) { detailNumber = d.number; detailScroll = Vector2.zero; }
            bool compact = d.number == 1 || d.number == 2 || d.number == 3 || d.number == 12;
            float reserved = d.number == 7 ? 270 : d.number == 2 ? 190 : compact || d.number == 5 ? 202 : d.number == 4 || d.number == 6 || d.number == 10 || d.number == 11 || d.number == 8 || d.number == 9 || d.number == 17 || d.number == 18 || d.number == 13 || d.number == 14 || d.number == 15 || d.number == 16 || d.number == 19 ? 176 : 108;
            var infoRect = new Rect(r.x, r.y, r.width, Mathf.Max(60, r.height - reserved - 12));
            float width = r.width - 18;
            Text.Font = GameFont.Small;
            float descriptionHeight = Mathf.Max(84, Text.CalcHeight(d.description, width));
            string price = FieldEconomy.PowerFor(d) + " P + " + FieldEconomy.VolatileFor(d) + " V";
            if (d.number == 1) price += NmcText.Get("NMC_Text0091");

            string cooldown = d.number == 12 && CommanderState.Current != null
                ? (CommanderState.Current.overloadCharges == 3 ? NmcText.Get("NMC_Text0092") : NmcText.Get("NMC_Text0093", Period(CommanderState.Current.rechargeRemaining)))
                : CommanderState.Current?.Remaining(d) > 0 ? NmcText.Get("NMC_Text0094", Period(CommanderState.Current.Remaining(d))) : Cooldown(d);
            string research = CommanderProgression.RequirementText(d);
            float iconSize = infoRect.height < 140 ? 48 : 72;
            float titleX = iconSize + 14;
            float titleHeight = Mathf.Max(28, Text.CalcHeight(d.label, width - titleX));
            float descriptionY = Mathf.Max(iconSize + 28, 28 + titleHeight + 12);
            float metadataY = descriptionY + descriptionHeight + 18;
            float height = metadataY + PairHeight(width, price) + PairHeight(width, cooldown) + PairHeight(width, research);
            bool extra = !compact && !string.IsNullOrEmpty(d.extraCost);
            if (extra) height += PairHeight(width, d.extraCost);
            var content = new Rect(0, 0, width, height);
            Widgets.BeginScrollView(infoRect, ref detailScroll, content);
            try
            {
                DrawIcon(new Rect(0, 4, iconSize, iconSize), d, true);
                Label(new Rect(titleX, 0, width - titleX, 23), d.Code + " / " + d.group, Theme.Gold, GameFont.Tiny);
                Label(new Rect(titleX, 27, width - titleX, titleHeight), d.label, Theme.White, GameFont.Small);
                Widgets.DrawBoxSolid(new Rect(0, descriptionY - 12, width, 1), Theme.Line);
                Label(new Rect(0, descriptionY, width, descriptionHeight), d.description, Muted);
                float y = metadataY;
                y += Pair(content, y, NmcText.Get("NMC_Text0095"), price);
                y += Pair(content, y, NmcText.Get("NMC_Text0096"), cooldown);
                y += Pair(content, y, NmcText.Get("NMC_Text0097"), research);
                if (extra) Pair(content, y, NmcText.Get("NMC_Text0098"), d.extraCost);
            }
            finally { Widgets.EndScrollView(); }
            Widgets.DrawBoxSolid(new Rect(r.x, infoRect.yMax + 5, r.width, 1), Theme.Line);
            float bottom = r.yMax;
            if (d.number == 2)
            {
                string upgrade = CommanderActions.ProtectionUnavailable();
                bool protectedNow = CommanderResearch.FriendlyFireProtectionActive;
                bool enabled = GUI.enabled;
                Label(new Rect(r.x, bottom - 162, r.width, 30), protectedNow ? NmcText.Get("NMC_Text0099") : NmcText.Get("NMC_Text0713"), protectedNow ? Cyan : Muted, GameFont.Tiny);
                TooltipHandler.TipRegion(new Rect(r.x, bottom - 162, r.width, 30), protectedNow
                    ? NmcText.Get("NMC_Text0714")
                    : NmcText.Get("NMC_Text0715", upgrade));
                string blocked = CommanderActions.FireUnavailable(Find.CurrentMap);
                Label(new Rect(r.x, bottom - 125, r.width, 25), blocked ?? NmcText.Get("NMC_Text0102"), blocked == null ? Cyan : Muted, GameFont.Tiny);
                GUI.enabled = enabled && blocked == null;
                if (Theme.Button(new Rect(r.x, bottom - 93, r.width, 32), NmcText.Get("NMC_Text0103"))) CommanderTargeting.Begin(Find.CurrentMap);
                GUI.enabled = enabled;
            }
            else if (d.number == 1 || d.number == 3 || d.number == 12)
            {
                var map = Find.CurrentMap;
                var blocked = DefenseActions.Unavailable(map, d.number);
                bool enabled = GUI.enabled;
                string status = d.number == 1 ? (FieldEconomy.HasField ? (CommanderField.Current.fieldMap == map ? NmcText.Get("NMC_Text0104", map.GetComponent<FieldEnergy>().AllocatedWatts.ToString("0")) : NmcText.Get("NMC_Text0105")) : NmcText.Get("NMC_Text0106"))
                    : d.number == 3 ? NmcText.Get("NMC_Text0107") : NmcText.Get("NMC_Text0108");
                Label(new Rect(r.x, bottom - 174, r.width, 26), status, Cyan, GameFont.Tiny);
                TooltipHandler.TipRegion(new Rect(r.x, bottom - 174, r.width, 26), NmcText.Get("NMC_Text0109"));
                Label(new Rect(r.x, bottom - 149, r.width, 22), blocked ?? NmcText.Get("NMC_Text0110"), blocked == null ? Cyan : Muted, GameFont.Tiny);
                GUI.enabled = enabled && blocked == null;
                if (Theme.Button(new Rect(r.x, bottom - 119, r.width, 32), d.number == 1 ? NmcText.Get("NMC_Text0111") : d.number == 3 ? NmcText.Get("NMC_Text0112") : NmcText.Get("NMC_Text0113")))
                { if (d.number == 3) DefenseActions.Shield(map); else DefenseTargeting.Begin(map, d.number); }
                GUI.enabled = enabled;
                GUI.enabled = enabled && map != null;
                if (Theme.Button(new Rect(r.x, bottom - 82, r.width, 27), d.number == 1 ? NmcText.Get("NMC_Text0114") : d.number == 3 ? NmcText.Get("NMC_Text0115") : NmcText.Get("NMC_Text0116")))
                { if (d.number == 1) DefenseActions.RemoveField(map); else if (d.number == 3) DefenseActions.CancelShield(map); else DefenseTargeting.Begin(map, 12, true); }
                GUI.enabled = enabled;
            }
            else if (d.number == 4)
            {
                var map = Find.CurrentMap;
                string blocked = SolarActions.Unavailable(map);
                bool enabled = GUI.enabled;
                int active = map?.GetComponent<SolarBombardmentMap>()?.ActiveCount ?? 0;
                Label(new Rect(r.x, bottom - 155, r.width, 26), NmcText.Get("NMC_Text0117"), Cyan, GameFont.Tiny);
                Label(new Rect(r.x, bottom - 124, r.width, 25), active > 0 ? NmcText.Get("NMC_Text0118") : blocked ?? NmcText.Get("NMC_Text0119"), blocked == null ? Cyan : Muted, GameFont.Tiny);
                GUI.enabled = enabled && blocked == null;
                if (Theme.Button(new Rect(r.x, bottom - 93, r.width, 32), NmcText.Get("NMC_Text0120"))) SolarTargeting.Begin(map);
                GUI.enabled = enabled;
            }
            else if (d.number == 5)
            {
                var map = Find.CurrentMap;
                string blocked = RayActions.Unavailable(map);
                bool enabled = GUI.enabled;
                int active = map?.GetComponent<SolarRayMap>()?.ActiveCount ?? 0;
                Label(new Rect(r.x, bottom - 174, r.width, 26), NmcText.Get("NMC_Text0121"), Cyan, GameFont.Tiny);
                Label(new Rect(r.x, bottom - 149, r.width, 25), active > 0 ? NmcText.Get("NMC_Text0122") : blocked ?? NmcText.Get("NMC_Text0123"),
                    blocked == null ? Cyan : Muted, GameFont.Tiny);
                GUI.enabled = enabled && blocked == null;
                if (Theme.Button(new Rect(r.x, bottom - 119, r.width, 32), NmcText.Get("NMC_Text0124"))) RayTargeting.Begin(map);
                GUI.enabled = enabled && active > 0;
                if (Theme.Button(new Rect(r.x, bottom - 82, r.width, 27), NmcText.Get("NMC_Text0125"))) RayActions.Cancel(map);
                GUI.enabled = enabled;
            }
            else if (d.number == 6)
            {
                var maps = Find.Maps.Where(m => m.IsPlayerHome && m.GetComponent<SpacetimeWaveMap>().Active).ToList();
                string blocked = SpacetimeWaveActions.Unavailable();
                bool enabled = GUI.enabled;
                string remaining = maps.Count == 0 ? NmcText.Get("NMC_Text0126") :
                    NmcText.Get("NMC_Text0127", maps.Count, Period(Find.CurrentMap?.GetComponent<SpacetimeWaveMap>()?.remaining ?? 0));
                Label(new Rect(r.x, bottom - 174, r.width, 26), NmcText.Get("NMC_Text0128"), Cyan, GameFont.Tiny);
                Label(new Rect(r.x, bottom - 149, r.width, 25), maps.Count > 0 ? remaining : blocked ?? NmcText.Get("NMC_Text0129"), maps.Count > 0 || blocked == null ? Cyan : Muted, GameFont.Tiny);
                GUI.enabled = enabled && blocked == null;
                if (Theme.Button(new Rect(r.x, bottom - 119, r.width, 32), NmcText.Get("NMC_Text0130"))) SpacetimeWaveActions.Start();
                GUI.enabled = enabled && maps.Count > 0;
                if (Theme.Button(new Rect(r.x, bottom - 82, r.width, 27), NmcText.Get("NMC_Text0131"))) SpacetimeWaveActions.Cancel();
                GUI.enabled = enabled;
            }
            else if (d.number == 7)
            {
                var map = Find.CurrentMap;
                var beam = map?.GetComponent<PurificationBeamMap>()?.ActiveBeams.LastOrDefault();
                string blocked = PurificationActions.Unavailable(map);
                bool enabled = GUI.enabled;
                Label(new Rect(r.x, bottom - 242, r.width, 24), NmcText.Get("NMC_Text0132"), Cyan, GameFont.Tiny);
                Label(new Rect(r.x, bottom - 216, r.width, 24), beam == null ? blocked ?? NmcText.Get("NMC_Text0133") :
                    NmcText.Get("NMC_Text0136", (beam.drafted ? NmcText.Get("NMC_Text0134") : NmcText.Get("NMC_Text0135")), Period(PurificationBeamMap.Duration - beam.elapsed)),
                    beam != null || blocked == null ? Cyan : Muted, GameFont.Tiny);
                GUI.enabled = enabled && blocked == null;
                if (Theme.Button(new Rect(r.x, bottom - 189, r.width, 31), NmcText.Get("NMC_Text0137"))) PurificationTargeting.BeginStart(map);
                GUI.enabled = enabled && beam != null;
                if (Theme.Button(new Rect(r.x, bottom - 153, r.width, 29), beam?.drafted == true ? NmcText.Get("NMC_Text0138") : NmcText.Get("NMC_Text0139")))
                    PurificationActions.Draft(map, beam.serial, !beam.drafted);
                GUI.enabled = enabled && beam?.drafted == true;
                if (Theme.Button(new Rect(r.x, bottom - 120, r.width, 29), NmcText.Get("NMC_Text0140"))) PurificationTargeting.BeginMove(map, beam.serial);
                GUI.enabled = enabled && beam != null;
                if (Theme.Button(new Rect(r.x, bottom - 87, r.width, 27), NmcText.Get("NMC_Text0141"))) PurificationActions.Cancel(map);
                GUI.enabled = enabled;
            }
            else if (d.number == 8 || d.number == 17)
            {
                var map = Find.CurrentMap;
                string blocked = StructureActions.Unavailable(map, d.number);
                bool enabled = GUI.enabled;
                Label(new Rect(r.x, bottom - 155, r.width, 26), d.number == 8 ? NmcText.Get("NMC_Text0142") : NmcText.Get("NMC_Text0143"), Cyan, GameFont.Tiny);
                Label(new Rect(r.x, bottom - 124, r.width, 25), blocked ?? NmcText.Get("NMC_Text0144"), blocked == null ? Cyan : Muted, GameFont.Tiny);
                GUI.enabled = enabled && blocked == null;
                if (Theme.Button(new Rect(r.x, bottom - 93, r.width, 32), d.number == 8 ? NmcText.Get("NMC_Text0145") : NmcText.Get("NMC_Text0146"))) StructureTargeting.Begin(map, d.number);
                GUI.enabled = enabled;
            }
            else if (d.number == 9)
            {
                var map = Find.CurrentMap;
                string blocked = GhostActions.Unavailable(map);
                bool enabled = GUI.enabled;
                Label(new Rect(r.x, bottom - 155, r.width, 26), NmcText.Get("NMC_Text0147"), Cyan, GameFont.Tiny);
                Label(new Rect(r.x, bottom - 124, r.width, 25), blocked ?? NmcText.Get("NMC_Text0148"), blocked == null ? Cyan : Muted, GameFont.Tiny);
                GUI.enabled = enabled && blocked == null;
                if (Theme.Button(new Rect(r.x, bottom - 93, r.width, 32), NmcText.Get("NMC_Text0149"))) StructureTargeting.Begin(map, 9);
                GUI.enabled = enabled;
            }
            else if (d.number == 10)
            {
                var map = Find.CurrentMap;
                var vortex = map?.GetComponent<BlackHoleMap>()?.active;
                string blocked = BlackHoleActions.Unavailable(map);
                bool enabled = GUI.enabled;
                Label(new Rect(r.x, bottom - 174, r.width, 26), NmcText.Get("NMC_Text0150"), Cyan, GameFont.Tiny);
                Label(new Rect(r.x, bottom - 149, r.width, 25), vortex == null ? blocked ?? NmcText.Get("NMC_Text0151") :
                    NmcText.Get("NMC_Text0152", vortex.targets.Count, Period(BlackHoleMap.Duration - vortex.elapsed)), Cyan, GameFont.Tiny);
                GUI.enabled = enabled && blocked == null;
                if (Theme.Button(new Rect(r.x, bottom - 119, r.width, 32), NmcText.Get("NMC_Text0153"))) BlackHoleTargeting.Begin(map);
                GUI.enabled = enabled && vortex != null;
                if (Theme.Button(new Rect(r.x, bottom - 82, r.width, 27), NmcText.Get("NMC_Text0154"))) BlackHoleActions.Cancel(map);
                GUI.enabled = enabled;
            }
            else if (d.number == 11)
            {
                var states = Find.Maps.Where(m => m.IsPlayerHome).OrderBy(m => m.uniqueID)
                    .Select(m => m.GetComponent<TimeStopMap>()).ToList();
                string blocked = TimeStopActions.Unavailable();
                bool enabled = GUI.enabled;
                Label(new Rect(r.x, bottom - 174, r.width, 26), NmcText.Get("NMC_Text0155", (TimeStopMap.Duration / 60)), Cyan, GameFont.Tiny);
                Label(new Rect(r.x, bottom - 149, r.width, 25), states.Any(s => s.Active)
                    ? NmcText.Get("NMC_Text0156", states.Sum(s => s.Targets.Count), Period(Find.CurrentMap?.GetComponent<TimeStopMap>()?.remaining ?? 0))
                    : blocked ?? NmcText.Get("NMC_Text0157"), Cyan, GameFont.Tiny);
                GUI.enabled = enabled && blocked == null;
                if (Theme.Button(new Rect(r.x, bottom - 119, r.width, 32), NmcText.Get("NMC_Text0158"))) TimeStopActions.Start();
                GUI.enabled = enabled && states.Any(s => s.Active);
                if (Theme.Button(new Rect(r.x, bottom - 82, r.width, 27), NmcText.Get("NMC_Text0159"))) TimeStopActions.Cancel();
                GUI.enabled = enabled;
            }
            else if (d.number == 18)
            {
                var map = Find.CurrentMap;
                string blocked = VoidCrystalActions.Unavailable(map);
                var state = map?.GetComponent<VoidCrystalMap>();
                bool enabled = GUI.enabled;
                Label(new Rect(r.x, bottom - 174, r.width, 26), NmcText.Get("NMC_Text0160"), Cyan, GameFont.Tiny);
                Label(new Rect(r.x, bottom - 149, r.width, 25), state?.ActiveCount > 0
                    ? NmcText.Get("NMC_Text0161", state.ActiveCount)
                    : blocked ?? NmcText.Get("NMC_Text0162"), Cyan, GameFont.Tiny);
                GUI.enabled = enabled && blocked == null;
                if (Theme.Button(new Rect(r.x, bottom - 119, r.width, 32), NmcText.Get("NMC_Text0163"))) VoidCrystalTargeting.Begin(map);
                GUI.enabled = enabled && state?.ActiveCount > 0;
                if (Theme.Button(new Rect(r.x, bottom - 82, r.width, 27), NmcText.Get("NMC_Text0164"))) VoidCrystalActions.Cancel(map);
                GUI.enabled = enabled;
            }
            else if (d.number == 13)
            {
                var map = Find.CurrentMap;
                string blocked = FleetActions.Unavailable(map);
                var batches = CommanderFleets.Current.batches.Where(b => b.origin == map && b.abilityNumber == 13).ToList();
                bool enabled = GUI.enabled;
                Label(new Rect(r.x, bottom - 174, r.width, 26), NmcText.Get("NMC_Text0165"), Cyan, GameFont.Tiny);
                Label(new Rect(r.x, bottom - 149, r.width, 25), batches.Count > 0
                    ? NmcText.Get("NMC_Text0166", batches.Count, batches.Sum(b => b.ships.Count(s => s != null && !s.Destroyed)))
                    : blocked ?? NmcText.Get("NMC_Text0167"), Cyan, GameFont.Tiny);
                GUI.enabled = enabled && blocked == null;
                if (Theme.Button(new Rect(r.x, bottom - 119, r.width, 32), NmcText.Get("NMC_Text0168"))) FleetTargeting.Begin(map);
                GUI.enabled = enabled;
                Label(new Rect(r.x, bottom - 80, r.width, 27), NmcText.Get("NMC_Text0169"), Muted, GameFont.Tiny);
            }
            else if (d.number == 14)
            {
                var map = Find.CurrentMap;
                string blocked = TebrusLegionActions.Unavailable(map);
                var batches = CommanderLegions.Current.batches.Where(b => b.origin == map && b.ability == 14).ToList();
                bool enabled = GUI.enabled;
                Label(new Rect(r.x, bottom - 174, r.width, 26), NmcText.Get("NMC_Text0170"), Cyan, GameFont.Tiny);
                Label(new Rect(r.x, bottom - 149, r.width, 25), batches.Count > 0
                    ? NmcText.Get("NMC_Text0171", batches.Count, batches.Sum(b => b.members.Count(p => p != null && !p.Dead && !p.Destroyed)))
                    : blocked ?? NmcText.Get("NMC_Text0172"), blocked == null ? Cyan : Muted, GameFont.Tiny);
                TooltipHandler.TipRegion(new Rect(r.x, bottom - 149, r.width, 25), blocked ?? NmcText.Get("NMC_Text0173"));
                GUI.enabled = enabled && blocked == null;
                if (Theme.Button(new Rect(r.x, bottom - 119, r.width, 32), NmcText.Get("NMC_Text0174"))) TebrusLegionTargeting.Begin(map);
                GUI.enabled = enabled;
                Label(new Rect(r.x, bottom - 80, r.width, 27), NmcText.Get("NMC_Text0175"), Muted, GameFont.Tiny);
            }
            else if (d.number == 15)
            {
                var map = Find.CurrentMap;
                string blocked = RayFleetActions.Unavailable(map);
                var batches = CommanderFleets.Current.batches.Where(b => b.origin == map && b.abilityNumber == 15).ToList();
                bool enabled = GUI.enabled;
                Label(new Rect(r.x, bottom - 174, r.width, 26), NmcText.Get("NMC_Text0176"), Cyan, GameFont.Tiny);
                Label(new Rect(r.x, bottom - 149, r.width, 25), batches.Count > 0
                    ? NmcText.Get("NMC_Text0171", batches.Count, batches.Sum(b => b.ships.Count(s => s != null && !s.Destroyed)))
                    : blocked ?? NmcText.Get("NMC_Text0172"), blocked == null ? Cyan : Muted, GameFont.Tiny);
                TooltipHandler.TipRegion(new Rect(r.x, bottom - 149, r.width, 25), blocked ?? NmcText.Get("NMC_Text0173"));
                GUI.enabled = enabled && blocked == null;
                if (Theme.Button(new Rect(r.x, bottom - 119, r.width, 32), NmcText.Get("NMC_Text0177"))) RayFleetTargeting.Begin(map);
                GUI.enabled = enabled;
                Label(new Rect(r.x, bottom - 80, r.width, 27), NmcText.Get("NMC_Text0169"), Muted, GameFont.Tiny);
            }
            else if (d.number == 16)
            {
                var map = Find.CurrentMap;
                string blocked = SeydasLegionActions.Unavailable(map);
                var batches = CommanderLegions.Current.batches.Where(b => b.origin == map && b.ability == 16).ToList();
                bool enabled = GUI.enabled;
                Label(new Rect(r.x, bottom - 174, r.width, 26), NmcText.Get("NMC_Text0178"), Cyan, GameFont.Tiny);
                Label(new Rect(r.x, bottom - 149, r.width, 25), batches.Count > 0
                    ? NmcText.Get("NMC_Text0171", batches.Count, batches.Sum(b => b.members.Count(p => p != null && !p.Dead && !p.Destroyed)))
                    : blocked ?? NmcText.Get("NMC_Text0172"), blocked == null ? Cyan : Muted, GameFont.Tiny);
                TooltipHandler.TipRegion(new Rect(r.x, bottom - 149, r.width, 25), blocked ?? NmcText.Get("NMC_Text0179"));
                GUI.enabled = enabled && blocked == null;
                if (Theme.Button(new Rect(r.x, bottom - 119, r.width, 32), NmcText.Get("NMC_Text0180"))) SeydasLegionTargeting.Begin(map);
                GUI.enabled = enabled;
                Label(new Rect(r.x, bottom - 80, r.width, 27), NmcText.Get("NMC_Text0181"), Muted, GameFont.Tiny);
            }
            else if (d.number == 19)
            {
                var map = Find.CurrentMap; string blocked = ProjectionActions.Unavailable(map); bool enabled = GUI.enabled;
                int count = ProjectionRegistry.Current.registered.Count(t => ProjectionRegistry.Eligible(t) && t.HitPoints > 1 && ProjectionRegistry.Source(t) == null);
                Label(new Rect(r.x, bottom - 174, r.width, 26), NmcText.Get("NMC_Text0182", count), Cyan, GameFont.Tiny);
                Label(new Rect(r.x, bottom - 149, r.width, 25), blocked ?? NmcText.Get("NMC_Text0183"), blocked == null ? Cyan : Muted, GameFont.Tiny);
                GUI.enabled = enabled && blocked == null;
                if (Theme.Button(new Rect(r.x, bottom - 119, r.width, 32), NmcText.Get("NMC_Text0184"))) ProjectionTargeting.Begin(map);
                GUI.enabled = enabled && CommanderResearch.For(d)?.IsFinished == true;
                if (Theme.Button(new Rect(r.x, bottom - 82, (r.width - 8) / 2, 27), NmcText.Get("NMC_Text0185"))) ProjectionActions.RegisterAll(true);
                if (Theme.Button(new Rect(r.x + (r.width + 8) / 2, bottom - 82, (r.width - 8) / 2, 27), NmcText.Get("NMC_Text0186"))) ProjectionActions.RegisterAll(false);
                GUI.enabled = enabled;
                TooltipHandler.TipRegion(new Rect(r.x, bottom - 174, r.width, 26), NmcText.Get("NMC_Text0187"));
            }
            else Label(new Rect(r.x, bottom - 90, r.width, 28), NmcText.Get("NMC_Text0188"), new Color(.85f, .72f, .46f));
            if (Theme.Button(new Rect(r.x, bottom - 53, (r.width - 8) / 2, 28), NmcText.Get("NMC_Text0189"), true)) CommanderMod.Settings.Move(d, -1);
            if (Theme.Button(new Rect(r.x + (r.width + 8) / 2, bottom - 53, (r.width - 8) / 2, 28), NmcText.Get("NMC_Text0190"), true)) CommanderMod.Settings.Move(d, 1);
            Label(new Rect(r.x, bottom - 17, r.width, 24), NmcText.Get("NMC_Text0191"), Muted, GameFont.Tiny);
        }
        static float PairHeight(float width, string value)
        {
            Text.Font = GameFont.Small;
            return 20 + Mathf.Max(25, Text.CalcHeight(value, width)) + 10;
        }
        static float Pair(Rect r, float y, string key, string value)
        {
            float height = PairHeight(r.width, value);
            Label(new Rect(r.x, y, r.width, 19), key, Muted, GameFont.Tiny);
            Label(new Rect(r.x, y + 20, r.width, height - 30), value, Theme.White);
            return height;
        }
        public static string Period(int ticks) => ticks <= 0 ? NmcText.Get("NMC_Text0192") : ticks >= 30000 ? NmcText.Get("NMC_Text0193", (ticks / 60000f).ToString("0.##")) : NmcText.Get("NMC_Text0194", (ticks / 60f).ToString("0.#"));
        public static string Cooldown(CommanderAbilityDef d) => d.number == 2 ? NmcText.Get("NMC_Text0195") : d.number == 12 ? NmcText.Get("NMC_Text0196") : Period(FieldEconomy.CooldownFor(d)) + (d.number >= 14 && d.number <= 16 ? NmcText.Get("NMC_Text0197") : "");
    }
}
