using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace NivarianCommander
{
    public sealed class CommanderSettings : ModSettings
    {
        public bool showShortcut = true;
        public bool hudCollapsed, hudPositioned;
        public float hudCenterX = .5f, hudCenterY;
        public List<string> hudSlots = new List<string> { "", "", "", "", "", "" };
        public List<string> order = new List<string>();
        public override void ExposeData()
        {
            Scribe_Values.Look(ref showShortcut, "showCommanderShortcut", true);
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
        static readonly Color Cyan = new Color(.42f, .85f, .93f);
        static readonly Color Muted = new Color(.59f, .68f, .74f);
        static readonly Color Surface = new Color(.065f, .09f, .115f, .98f);
        static string[] Groups => new[] { NmcText.Get("NMC_Text0074"), NmcText.Get("NMC_Text0075"), NmcText.Get("NMC_Text0076"), NmcText.Get("NMC_Text0077"), NmcText.Get("NMC_Text0078"), NmcText.Get("NMC_Text0079") };
        public string group = NmcText.Get("NMC_Text0074");
        public int selected = 2;
        Vector2 scroll;
        Vector2 detailScroll;
        int detailNumber = -1;
        static void Label(Rect rect, string text, Color color, GameFont font = GameFont.Small)
        {
            Text.Font = font; GUI.color = color;
            if (!text.NullOrEmpty() && Text.CalcHeight(text, rect.width) > rect.height)
            {
                TooltipHandler.TipRegion(rect, text);
                text = text.Replace("\n", " ").Truncate(rect.width);
            }
            Widgets.Label(rect, text); GUI.color = Color.white;
        }
        public void Draw(Rect rect, Action back)
        {
            var oldFont = Text.Font; var oldAnchor = Text.Anchor; var oldColor = GUI.color;
            try { DrawContents(rect, back); }
            finally { Text.Font = oldFont; Text.Anchor = oldAnchor; GUI.color = oldColor; }
        }
        void DrawContents(Rect rect, Action back)
        {
            var groups = Groups;
            if (!groups.Contains(group)) group = groups[0];
            Text.Anchor = TextAnchor.UpperLeft;
            Widgets.DrawBoxSolid(rect, new Color(.04f, .06f, .08f, .99f));
            Widgets.DrawBoxSolid(new Rect(rect.x, rect.y, rect.width, 3), Cyan);
            Label(new Rect(18, 15, 320, 32), NmcText.Get("NMC_Text0080"), Color.white, GameFont.Medium);
            Label(new Rect(18, 52, 305, 24), NmcText.Get("NMC_Text0081"), Muted);
            float rx = rect.width - 600;
            Metric(new Rect(rx, 18, 130, 56), NmcText.Get("NMC_Text0082"), NativeLedger.Balance("PowerCoin").ToString("N0"));
            Metric(new Rect(rx + 142, 18, 130, 56), NmcText.Get("NMC_Text0083"), NativeLedger.Balance("VolatileReactive").ToString("N0"));
            Metric(new Rect(rx + 284, 18, 112, 56), NmcText.Get("NMC_Text0084"), (NativeLedger.Component?.GetLevel ?? 0).ToString());
            Metric(new Rect(rx + 408, 18, 162, 56), NmcText.Get("NMC_Text0085"), (CommanderState.Current?.overloadCharges ?? 0) + " / 3");
            var main = new Rect(18, 100, rect.width - 36, rect.height - 150);
            var sidebar = new Rect(main.x, main.y, 142, main.height);
            float detailWidth = 282;
            var details = new Rect(main.xMax - detailWidth, main.y, detailWidth, main.height);
            var list = new Rect(sidebar.xMax + 12, main.y, details.x - sidebar.xMax - 24, main.height);
            Widgets.DrawBoxSolid(sidebar, Surface); Widgets.DrawBoxSolid(details, Surface);
            Label(new Rect(sidebar.x + 10, sidebar.y + 12, 130, 24), NmcText.Get("NMC_Text0086"), Muted);
            for (int i = 0; i < groups.Length; i++)
            {
                Rect button = new Rect(sidebar.x + 8, sidebar.y + 47 + i * 44, 126, 36);
                if (group == groups[i]) Widgets.DrawBoxSolid(button, new Color(.12f, .30f, .37f));
                if (Widgets.ButtonText(button, groups[i], false)) { group = groups[i]; scroll = Vector2.zero; }
            }
            Label(new Rect(sidebar.x + 12, sidebar.yMax - 82, 119, 64), NmcText.Get("NMC_Text0087"), Muted, GameFont.Tiny);
            var filtered = CommanderMod.Settings.Ordered().Where(d => group == NmcText.Get("NMC_Text0074") || d.group == group).ToList();
            var chosen = DefDatabase<CommanderAbilityDef>.AllDefsListForReading.FirstOrDefault(d => d.number == selected);
            var view = new Rect(0, 0, list.width - 18, filtered.Count * 78);
            Widgets.BeginScrollView(list, ref scroll, view);
            for (int i = 0; i < filtered.Count; i++)
            {
                var d = filtered[i]; Rect row = new Rect(0, i * 78, view.width, 70);
                Widgets.DrawBoxSolid(row, d == chosen ? new Color(.10f, .23f, .29f) : Surface);
                if (d == chosen) Widgets.DrawBoxSolid(new Rect(0, row.y, 3, 70), Cyan);
                Widgets.DrawBoxSolid(new Rect(10, row.y + 12, 44, 44), new Color(.07f, .14f, .19f));
                if (!string.IsNullOrEmpty(d.iconPath)) GUI.DrawTexture(new Rect(10, row.y + 12, 44, 44), ContentFinder<Texture2D>.Get(d.iconPath));
                else if (d.number == 19) GUI.DrawTexture(new Rect(10, row.y + 12, 44, 44), ContentFinder<Texture2D>.Get("NMC/Buildings/hyperdimensional_monolith"));
                else if (d.number == 18) GUI.DrawTexture(new Rect(10, row.y + 12, 44, 44), ContentFinder<Texture2D>.Get("NMC/Buildings/void_crystal"));
                else if (d.number == 13) GUI.DrawTexture(new Rect(10, row.y + 12, 44, 44), ContentFinder<Texture2D>.Get("NMC/Fleet/tactical_mothership"));
                else if (d.number == 15) GUI.DrawTexture(new Rect(10, row.y + 12, 44, 44), ContentFinder<Texture2D>.Get("NMC/Fleet/zoraya_v1"));
                else if (d.number == 1 || d.number == 2 || d.number == 3 || d.number == 4 || d.number == 5 || d.number == 6 || d.number == 7 || d.number == 10 || d.number == 11 || d.number == 12) GUI.DrawTexture(new Rect(10, row.y + 12, 44, 44), ContentFinder<Texture2D>.Get("NMC/Supports/" + (d.number == 1 ? "warp_energy_field" : d.number == 2 ? "orbital_strike" : d.number == 3 ? "shield_overload" : d.number == 4 ? "solar_bombardment" : d.number == 5 ? "solar_ray_gun" : d.number == 6 ? "spacetime_wave" : d.number == 7 ? "purification_beam" : d.number == 10 ? "black_hole" : d.number == 11 ? "time_stop" : "building_overload")));
                else Label(new Rect(15, row.y + 23, 40, 22), NmcText.Get("NMC_Text0060"), Cyan);
                Label(new Rect(66, row.y + 9, row.width - 78, 26), d.label, Color.white);
                Label(new Rect(66, row.y + 38, row.width - 78, 24), FieldEconomy.PowerFor(d) + " P  ·  " + FieldEconomy.VolatileFor(d) + " V  /  " + Cooldown(d), Muted, GameFont.Tiny);
                if (Widgets.ButtonInvisible(row)) selected = d.number;
                TooltipHandler.TipRegion(row, d.description);
            }
            Widgets.EndScrollView();
            if (chosen != null) Detail(details.ContractedBy(14), chosen);
            if (Widgets.ButtonText(new Rect(18, rect.height - 37, 126, 28), NmcText.Get("NMC_Text0088"))) back();
            bool show = CommanderMod.Settings.showShortcut;
            Widgets.CheckboxLabeled(new Rect(164, rect.height - 33, 214, 26), NmcText.Get("NMC_Text0089"), ref show);
            if (show != CommanderMod.Settings.showShortcut) { CommanderMod.Settings.showShortcut = show; CommanderMod.Settings.Write(); }
            Label(new Rect(rect.width - 295, rect.height - 33, 279, 24), NmcText.Get("NMC_Text0090"), Muted, GameFont.Tiny);
        }
        static void Metric(Rect r, string label, string value)
        {
            Widgets.DrawBoxSolid(r, Surface);
            Label(new Rect(r.x + 10, r.y + 5, r.width - 20, 20), label, Muted, GameFont.Tiny);
            Label(new Rect(r.x + 10, r.y + 25, r.width - 20, 30), value, Cyan, GameFont.Medium);
        }
        void Detail(Rect r, CommanderAbilityDef d)
        {
            if (detailNumber != d.number) { detailNumber = d.number; detailScroll = Vector2.zero; }
            bool compact = d.number == 1 || d.number == 2 || d.number == 3 || d.number == 12;
            float reserved = d.number == 7 ? 270 : d.number == 2 ? 190 : compact || d.number == 5 ? 202 : d.number == 4 || d.number == 6 || d.number == 10 || d.number == 11 || d.number == 8 || d.number == 9 || d.number == 17 || d.number == 18 || d.number == 13 || d.number == 14 || d.number == 15 || d.number == 16 || d.number == 19 ? 176 : 108;
            var infoRect = new Rect(r.x, r.y, r.width, Mathf.Max(80, r.height - reserved));
            float width = r.width - 18;
            Text.Font = GameFont.Small;
            float descriptionHeight = Mathf.Max(84, Text.CalcHeight(d.description, width));
            string price = FieldEconomy.PowerFor(d) + " P + " + FieldEconomy.VolatileFor(d) + " V";
            if (d.number == 1) price += NmcText.Get("NMC_Text0091");

            string cooldown = d.number == 12 && CommanderState.Current != null
                ? (CommanderState.Current.overloadCharges == 3 ? NmcText.Get("NMC_Text0092") : NmcText.Get("NMC_Text0093", Period(CommanderState.Current.rechargeRemaining)))
                : CommanderState.Current?.Remaining(d) > 0 ? NmcText.Get("NMC_Text0094", Period(CommanderState.Current.Remaining(d))) : Cooldown(d);
            string research = CommanderProgression.RequirementText(d);
            float metadataY = Mathf.Max(compact ? 180 : 201, 90 + descriptionHeight + 16);
            float height = metadataY + PairHeight(width, price) + PairHeight(width, cooldown) + PairHeight(width, research);
            bool extra = !compact && !string.IsNullOrEmpty(d.extraCost);
            if (extra) height += PairHeight(width, d.extraCost);
            var content = new Rect(0, 0, width, height);
            Widgets.BeginScrollView(infoRect, ref detailScroll, content);
            try
            {
                Label(new Rect(0, 0, width, 23), d.group, Cyan, GameFont.Tiny);
                Label(new Rect(0, 30, width, 54), d.label, Color.white, GameFont.Medium);
                Label(new Rect(0, 90, width, descriptionHeight), d.description, Muted);
                float y = metadataY;
                y += Pair(content, y, NmcText.Get("NMC_Text0095"), price);
                y += Pair(content, y, NmcText.Get("NMC_Text0096"), cooldown);
                y += Pair(content, y, NmcText.Get("NMC_Text0097"), research);
                if (extra) Pair(content, y, NmcText.Get("NMC_Text0098"), d.extraCost);
            }
            finally { Widgets.EndScrollView(); }
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
                if (Widgets.ButtonText(new Rect(r.x, bottom - 93, r.width, 32), NmcText.Get("NMC_Text0103"))) CommanderTargeting.Begin(Find.CurrentMap);
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
                if (Widgets.ButtonText(new Rect(r.x, bottom - 119, r.width, 32), d.number == 1 ? NmcText.Get("NMC_Text0111") : d.number == 3 ? NmcText.Get("NMC_Text0112") : NmcText.Get("NMC_Text0113")))
                { if (d.number == 3) DefenseActions.Shield(map); else DefenseTargeting.Begin(map, d.number); }
                GUI.enabled = enabled;
                GUI.enabled = enabled && map != null;
                if (Widgets.ButtonText(new Rect(r.x, bottom - 82, r.width, 27), d.number == 1 ? NmcText.Get("NMC_Text0114") : d.number == 3 ? NmcText.Get("NMC_Text0115") : NmcText.Get("NMC_Text0116")))
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
                if (Widgets.ButtonText(new Rect(r.x, bottom - 93, r.width, 32), NmcText.Get("NMC_Text0120"))) SolarTargeting.Begin(map);
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
                if (Widgets.ButtonText(new Rect(r.x, bottom - 119, r.width, 32), NmcText.Get("NMC_Text0124"))) RayTargeting.Begin(map);
                GUI.enabled = enabled && active > 0;
                if (Widgets.ButtonText(new Rect(r.x, bottom - 82, r.width, 27), NmcText.Get("NMC_Text0125"))) RayActions.Cancel(map);
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
                if (Widgets.ButtonText(new Rect(r.x, bottom - 119, r.width, 32), NmcText.Get("NMC_Text0130"))) SpacetimeWaveActions.Start();
                GUI.enabled = enabled && maps.Count > 0;
                if (Widgets.ButtonText(new Rect(r.x, bottom - 82, r.width, 27), NmcText.Get("NMC_Text0131"))) SpacetimeWaveActions.Cancel();
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
                if (Widgets.ButtonText(new Rect(r.x, bottom - 189, r.width, 31), NmcText.Get("NMC_Text0137"))) PurificationTargeting.BeginStart(map);
                GUI.enabled = enabled && beam != null;
                if (Widgets.ButtonText(new Rect(r.x, bottom - 153, r.width, 29), beam?.drafted == true ? NmcText.Get("NMC_Text0138") : NmcText.Get("NMC_Text0139")))
                    PurificationActions.Draft(map, beam.serial, !beam.drafted);
                GUI.enabled = enabled && beam?.drafted == true;
                if (Widgets.ButtonText(new Rect(r.x, bottom - 120, r.width, 29), NmcText.Get("NMC_Text0140"))) PurificationTargeting.BeginMove(map, beam.serial);
                GUI.enabled = enabled && beam != null;
                if (Widgets.ButtonText(new Rect(r.x, bottom - 87, r.width, 27), NmcText.Get("NMC_Text0141"))) PurificationActions.Cancel(map);
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
                if (Widgets.ButtonText(new Rect(r.x, bottom - 93, r.width, 32), d.number == 8 ? NmcText.Get("NMC_Text0145") : NmcText.Get("NMC_Text0146"))) StructureTargeting.Begin(map, d.number);
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
                if (Widgets.ButtonText(new Rect(r.x, bottom - 93, r.width, 32), NmcText.Get("NMC_Text0149"))) StructureTargeting.Begin(map, 9);
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
                if (Widgets.ButtonText(new Rect(r.x, bottom - 119, r.width, 32), NmcText.Get("NMC_Text0153"))) BlackHoleTargeting.Begin(map);
                GUI.enabled = enabled && vortex != null;
                if (Widgets.ButtonText(new Rect(r.x, bottom - 82, r.width, 27), NmcText.Get("NMC_Text0154"))) BlackHoleActions.Cancel(map);
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
                if (Widgets.ButtonText(new Rect(r.x, bottom - 119, r.width, 32), NmcText.Get("NMC_Text0158"))) TimeStopActions.Start();
                GUI.enabled = enabled && states.Any(s => s.Active);
                if (Widgets.ButtonText(new Rect(r.x, bottom - 82, r.width, 27), NmcText.Get("NMC_Text0159"))) TimeStopActions.Cancel();
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
                if (Widgets.ButtonText(new Rect(r.x, bottom - 119, r.width, 32), NmcText.Get("NMC_Text0163"))) VoidCrystalTargeting.Begin(map);
                GUI.enabled = enabled && state?.ActiveCount > 0;
                if (Widgets.ButtonText(new Rect(r.x, bottom - 82, r.width, 27), NmcText.Get("NMC_Text0164"))) VoidCrystalActions.Cancel(map);
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
                if (Widgets.ButtonText(new Rect(r.x, bottom - 119, r.width, 32), NmcText.Get("NMC_Text0168"))) FleetTargeting.Begin(map);
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
                if (Widgets.ButtonText(new Rect(r.x, bottom - 119, r.width, 32), NmcText.Get("NMC_Text0174"))) TebrusLegionTargeting.Begin(map);
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
                if (Widgets.ButtonText(new Rect(r.x, bottom - 119, r.width, 32), NmcText.Get("NMC_Text0177"))) RayFleetTargeting.Begin(map);
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
                if (Widgets.ButtonText(new Rect(r.x, bottom - 119, r.width, 32), NmcText.Get("NMC_Text0180"))) SeydasLegionTargeting.Begin(map);
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
                if (Widgets.ButtonText(new Rect(r.x, bottom - 119, r.width, 32), NmcText.Get("NMC_Text0184"))) ProjectionTargeting.Begin(map);
                GUI.enabled = enabled && CommanderResearch.For(d)?.IsFinished == true;
                if (Widgets.ButtonText(new Rect(r.x, bottom - 82, (r.width - 8) / 2, 27), NmcText.Get("NMC_Text0185"))) ProjectionActions.RegisterAll(true);
                if (Widgets.ButtonText(new Rect(r.x + (r.width + 8) / 2, bottom - 82, (r.width - 8) / 2, 27), NmcText.Get("NMC_Text0186"))) ProjectionActions.RegisterAll(false);
                GUI.enabled = enabled;
                TooltipHandler.TipRegion(new Rect(r.x, bottom - 174, r.width, 26), NmcText.Get("NMC_Text0187"));
            }
            else Label(new Rect(r.x, bottom - 90, r.width, 28), NmcText.Get("NMC_Text0188"), new Color(.85f, .72f, .46f));
            if (Widgets.ButtonText(new Rect(r.x, bottom - 53, (r.width - 8) / 2, 28), NmcText.Get("NMC_Text0189"))) CommanderMod.Settings.Move(d, -1);
            if (Widgets.ButtonText(new Rect(r.x + (r.width + 8) / 2, bottom - 53, (r.width - 8) / 2, 28), NmcText.Get("NMC_Text0190"))) CommanderMod.Settings.Move(d, 1);
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
            Label(new Rect(r.x, y + 20, r.width, height - 30), value, Color.white);
            return height;
        }
        public static string Period(int ticks) => ticks <= 0 ? NmcText.Get("NMC_Text0192") : ticks >= 30000 ? NmcText.Get("NMC_Text0193", (ticks / 60000f).ToString("0.##")) : NmcText.Get("NMC_Text0194", (ticks / 60f).ToString("0.#"));
        public static string Cooldown(CommanderAbilityDef d) => d.number == 2 ? NmcText.Get("NMC_Text0195") : d.number == 12 ? NmcText.Get("NMC_Text0196") : Period(FieldEconomy.CooldownFor(d)) + (d.number >= 14 && d.number <= 16 ? NmcText.Get("NMC_Text0197") : "");
    }
}
