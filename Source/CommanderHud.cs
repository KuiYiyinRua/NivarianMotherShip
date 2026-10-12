using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace NivarianCommander
{
    // A genuine GameUI window: WindowStack owns pointer input before the map's
    // Selector/Targeter run. All layout, pointer and shortcut choices are local.
    public sealed class CommanderHud : Window
    {
        const float Height = 124f, IconSize = 48f, Gap = 6f, CenterWidth = 132f, EdgeWidth = 20f;
        const float HoldSeconds = .35f, MoveTolerance = 6f, ScreenMargin = 6f;
        const int SlotCount = 6, SlotButton = 100, PanelButton = -1, CollapseButton = -2, ExpandButton = -5;
        static readonly Color Navy = new Color(.035f, .055f, .105f, .98f);
        static readonly Color Gold = new Color(.66f, .52f, .29f);
        static readonly Color Cyan = new Color(.39f, .81f, 1f);
        static readonly Color Muted = new Color(.47f, .52f, .59f);
        static Texture2D hudFrame;
        readonly Game owner;
        readonly List<CommanderAbilityDef> abilities = new List<CommanderAbilityDef>(19);
        readonly CommanderAbilityDef[] assigned = new CommanderAbilityDef[SlotCount];
        readonly Dictionary<int, Texture2D> icons = new Dictionary<int, Texture2D>();
        readonly Dictionary<int, string> reasons = new Dictionary<int, string>();
        readonly List<Hit> hits = new List<Hit>(12);
        List<string> orderedReference, slotsReference;
        int unlockMask = -1;
        Vector2 center;
        bool hasPosition, collapsed;
        Map availabilityMap;
        float nextAvailability;
        int lastPower = -1, lastVolatile = -1;
        bool pressed, moved, held;
        int pointerControl, pressedButton;
        bool pressedEnabled;
        Rect pressedRect;
        Vector2 downScreen, downCenter;
        Map downMap;
        float pressedAt;
        int lastWidth, lastHeight;

        struct Hit
        {
            internal Rect rect;
            internal int action;
            internal bool enabled;
            internal Hit(Rect rect, int action, bool enabled) { this.rect = rect; this.action = action; this.enabled = enabled; }
        }

        CommanderHud(Game game)
        {
            owner = game;
            var settings = CommanderMod.Settings;
            collapsed = settings.hudCollapsed;
            hasPosition = settings.hudPositioned;
            center = new Vector2(SafeFraction(settings.hudCenterX, .5f), SafeFraction(settings.hudCenterY, 0f));
            layer = WindowLayer.GameUI;
            doWindowBackground = drawShadow = doCloseX = doCloseButton = false;
            closeOnAccept = closeOnCancel = closeOnClickedOutside = false;
            forcePause = preventCameraMotion = absorbInputAroundWindow = false;
            focusWhenOpened = draggable = false;
            soundAppear = soundClose = null;
        }

        protected override float Margin => 0f;
        public override Vector2 InitialSize => new Vector2(WidthFor(SlotCount), Height);
        static float WidthFor(int count) => count * IconSize + (count - 2) * Gap + CenterWidth + 4 * Gap + 2 * EdgeWidth;
        static float SafeFraction(float value, float fallback) => float.IsNaN(value) || float.IsInfinity(value) ? fallback : Mathf.Clamp01(value);

        public static void EnsureWindow()
        {
            var stack = Find.WindowStack;
            if (stack == null) return;
            var current = stack.WindowOfType<CommanderHud>();
            // Full mothership windows already expose the same resources and actions.
            // Remove this input surface while they are open; saved slots/position stay local and intact.
            foreach (var window in stack.Windows)
                if (window is Nivarian_Race.Code.UI.Window_MothershipSupport ||
                    window is Window_MothershipPointShop || window is Window_PointShopOrder)
                {
                    current?.Close(false);
                    return;
                }
            int mask = 0;
            if (CommanderMod.Settings?.showShortcut == true && Current.ProgramState == ProgramState.Playing &&
                Current.Game != null && Find.ResearchManager != null && !LongEventHandler.ShouldWaitForEvent)
                foreach (var def in DefDatabase<CommanderAbilityDef>.AllDefsListForReading)
                    if (def.number >= 1 && def.number <= 19 && CommanderResearch.For(def)?.IsFinished == true)
                        mask |= 1 << def.number;
            if (current != null && (mask == 0 || current.owner != Current.Game))
            { current.Close(false); current = null; }
            if (mask == 0) return;
            if (current == null)
            {
                current = new CommanderHud(Current.Game);
                current.RefreshAbilities(mask);
                stack.Add(current);
            }
            else current.RefreshAbilities(mask);
            // Also run before UIRoot's high-priority input so hit bounds are
            // current after resolution changes, collapse or a new unlock.
            current.UpdateLayout();
        }

        void RefreshAbilities(int mask)
        {
            var settings = CommanderMod.Settings;
            if (unlockMask == mask && ReferenceEquals(orderedReference, settings.order) && ReferenceEquals(slotsReference, settings.hudSlots)) return;
            ReleasePointer();
            unlockMask = mask; orderedReference = settings.order; slotsReference = settings.hudSlots;
            abilities.Clear();
            foreach (var def in settings.Ordered())
                if (def.number >= 1 && def.number <= 19 && (mask & (1 << def.number)) != 0) abilities.Add(def);
            for (int i = 0; i < SlotCount; i++)
            {
                string name = slotsReference != null && i < slotsReference.Count ? slotsReference[i] : null;
                assigned[i] = string.IsNullOrEmpty(name) ? null : DefDatabase<CommanderAbilityDef>.GetNamedSilentFail(name);
            }
            reasons.Clear(); nextAvailability = -1f;
        }

        public override void PreOpen() { base.PreOpen(); UpdateLayout(); }
        public override void WindowOnGUI() { UpdateLayout(); base.WindowOnGUI(); }
        public override void WindowUpdate()
        {
            base.WindowUpdate();
            if (pressed && (!Application.isFocused || downMap != CommanderQuickActions.ViewedMap || !Find.WindowStack.GetsInput(this)))
                ReleasePointer();
        }
        public override void PreClose() { ReleasePointer(); base.PreClose(); }

        static float DefaultTop()
        {
            const float topMargin = 8f;
            var bar = Find.ColonistBar;
            if (bar == null || Find.PlaySettings?.showColonistBar != true ||
                UI.screenWidth < 800 || UI.screenHeight < 500 || Find.TilePicker.Active) return topMargin;
            // Entries refreshes the native layout cache. DrawLocs/Size then include
            // every actual row and colony, rather than guessing a portrait count.
            var entries = bar.Entries;
            var locations = bar.DrawLocs;
            float top = topMargin, height = bar.Size.y;
            // The native group frame extends 12*scale below its entries; MP puts
            // its map time controls immediately below that frame.
            float footer = Mathf.Max(32f * bar.Scale, 12f * bar.Scale + TimeControls.TimeButSize.y);
            for (int i = 0; i < entries.Count && i < locations.Count; i++)
            {
                float bottom = locations[i].y + height + footer;
                var pawn = entries[i].pawn;
                var weapon = pawn?.equipment?.Primary;
                if (weapon?.def.IsWeapon == true && (Prefs.ShowWeaponsUnderPortraitMode == ShowWeaponsUnderPortraitMode.Always ||
                    Prefs.ShowWeaponsUnderPortraitMode == ShowWeaponsUnderPortraitMode.WhileDrafted && pawn.Drafted))
                    bottom = Mathf.Max(bottom, locations[i].y + height * 2.05f);
                top = Mathf.Max(top, bottom + topMargin);
            }
            return top;
        }

        void UpdateLayout()
        {
            if (lastWidth != UI.screenWidth || lastHeight != UI.screenHeight)
            {
                ReleasePointer();
                lastWidth = UI.screenWidth; lastHeight = UI.screenHeight;
            }
            float scale = Mathf.Min(1f, Mathf.Max(1f, UI.screenWidth - ScreenMargin * 2) / WidthFor(SlotCount));
            float width = collapsed ? 46f : WidthFor(SlotCount) * scale, height = collapsed ? 46f : Height * scale;
            Vector2 pixelCenter = hasPosition ? new Vector2(center.x * UI.screenWidth, center.y * UI.screenHeight)
                : new Vector2(UI.screenWidth / 2f, Height * scale / 2f + DefaultTop());
            float x = Mathf.Clamp(pixelCenter.x - width / 2f, ScreenMargin, Mathf.Max(ScreenMargin, UI.screenWidth - width - ScreenMargin));
            float y = Mathf.Clamp(pixelCenter.y - height / 2f, ScreenMargin, Mathf.Max(ScreenMargin, UI.screenHeight - height - ScreenMargin));
            windowRect = new Rect(x, y, width, height);
        }

        void RefreshAvailability()
        {
            var map = CommanderQuickActions.ViewedMap;
            int power = NativeLedger.Balance("PowerCoin"), reactive = NativeLedger.Balance("VolatileReactive");
            float now = Time.realtimeSinceStartup;
            if (map == availabilityMap && power == lastPower && reactive == lastVolatile && now < nextAvailability) return;
            availabilityMap = map; lastPower = power; lastVolatile = reactive; nextAvailability = now + .25f;
            // Some existing availability checks enumerate trade goods. Cache
            // only the six assigned slots and isolate native query randomness.
            var unityRandom = UnityEngine.Random.state;
            Rand.PushState();
            try
            {
                foreach (var def in assigned)
                    if (def != null) reasons[def.number] = CommanderQuickActions.Unavailable(def);
            }
            finally { UnityEngine.Random.state = unityRandom; Rand.PopState(); }
        }

        public override void DoWindowContents(Rect inRect)
        {
            var oldColor = GUI.color; var oldFont = Text.Font; var oldAnchor = Text.Anchor; bool oldWrap = Text.WordWrap;
            try
            {
                GUI.color = Color.white; Text.WordWrap = false;
                hits.Clear();
                if (collapsed) DrawCollapsed(inRect);
                else { RefreshAvailability(); DrawExpanded(inRect); }
                if (pressed && !held)
                    Widgets.DrawBoxSolid(new Rect(5, inRect.height - 4, (inRect.width - 10) * Mathf.Clamp01((Time.realtimeSinceStartup - pressedAt) / HoldSeconds), 2), Cyan);
                HandlePointer(inRect);
            }
            finally { GUI.color = oldColor; Text.Font = oldFont; Text.Anchor = oldAnchor; Text.WordWrap = oldWrap; }
        }

        static void Label(Rect rect, string text, Color color, GameFont font = GameFont.Tiny)
        {
            Text.Font = font; Text.Anchor = TextAnchor.MiddleCenter; GUI.color = color;
            Widgets.Label(rect, text); GUI.color = Color.white;
        }
        static void Frame(Rect rect, Color border)
        {
            Widgets.DrawBoxSolid(rect, border);
            Widgets.DrawBoxSolid(rect.ContractedBy(1), new Color(.09f, .15f, .25f, .99f));
            Widgets.DrawBoxSolid(rect.ContractedBy(3), Navy);
        }

        void DrawCollapsed(Rect rect)
        {
            DrawArtwork(rect, true);
            Label(rect, NmcText.Get("NMC_Text0052"), Cyan, GameFont.Medium);
            hits.Add(new Hit(rect, ExpandButton, true));
            TooltipHandler.TipRegion(rect, NmcText.Get("NMC_Text0053"));
        }

        void DrawExpanded(Rect rect)
        {
            DrawArtwork(rect, false);
            float scale = rect.width / WidthFor(SlotCount);
            int perSide = SlotCount / 2;
            float wingWidth = perSide * IconSize + (perSide - 1) * Gap;
            float left = EdgeWidth + Gap;
            float centerX = left + wingWidth + Gap;
            for (int slot = 0; slot < SlotCount; slot++)
            {
                float x = slot < perSide ? left + slot * (IconSize + Gap)
                    : centerX + CenterWidth + Gap + (slot - perSide) * (IconSize + Gap);
                Rect button = Scaled(new Rect(x, (Height - IconSize) / 2f, IconSize, IconSize), scale);
                if (assigned[slot] != null) DrawAbility(button, assigned[slot], slot);
                else
                {
                    Frame(button, Mouse.IsOver(button) ? Cyan : new Color(.19f, .32f, .45f));
                    Label(button, "+", Cyan, GameFont.Medium);
                    hits.Add(new Hit(button, SlotButton + slot, true));
                    TooltipHandler.TipRegion(button, NmcText.Get("NMC_Text0054", (slot + 1)));
                }
            }
            // Native Small gives Chinese fallback glyphs a full readable line.
            Label(Scaled(new Rect(centerX + 11, 20, 110, 25), scale), NmcText.Get("NMC_Text0055"), new Color(.89f, .79f, .54f), GameFont.Small);
            int p = NativeLedger.Balance("PowerCoin"), v = NativeLedger.Balance("VolatileReactive");
            var valueFont = scale < .9f ? GameFont.Tiny : GameFont.Small;
            Label(Scaled(new Rect(centerX + 16, 47, 100, 21), scale), "P " + Compact(p), Cyan, valueFont);
            Label(Scaled(new Rect(centerX + 16, 68, 100, 21), scale), "V " + Compact(v), new Color(.77f, .70f, 1f), valueFont);
            TooltipHandler.TipRegion(Scaled(new Rect(centerX + 16, 46, 100, 44), scale), NmcText.Get("NMC_Text0056", p.ToString("N0"), v.ToString("N0")));
            DrawControl(Scaled(new Rect(centerX + 24, 94, 56, 21), scale), NmcText.Get("NMC_Text0057"), PanelButton, true, NmcText.Get("NMC_Text0058"));
            DrawControl(Scaled(new Rect(centerX + 84, 94, 24, 21), scale), "−", CollapseButton, true, NmcText.Get("NMC_Text0059"));
        }

        static Rect Scaled(Rect rect, float scale) => new Rect(rect.x * scale, rect.y * scale, rect.width * scale, rect.height * scale);

        static void DrawArtwork(Rect rect, bool compact)
        {
            if (hudFrame == null) hudFrame = ContentFinder<Texture2D>.Get("NMC/UI/commander_hud");
            // Consume the accepted PNG without modifying its pixels. Trim only
            // transparent margins in UVs; retain circular dial/end-cap proportions.
            Rect uv = new Rect(2f / 2171f, (724f - 591f) / 724f, 2168f / 2171f, 490f / 724f);
            const float aspect = 2168f / 490f;
            if (compact)
            {
                float span = 490f / 2171f;
                GUI.DrawTextureWithTexCoords(rect, hudFrame, new Rect(uv.center.x - span / 2f, uv.y, span, uv.height));
                return;
            }
            float middleWidth = CenterWidth * rect.height / Height;
            float middleFraction = middleWidth / (rect.height * aspect);
            const float capFraction = .1f;
            float wingFraction = (1f - middleFraction) / 2f - capFraction;
            float capWidth = rect.height * aspect * capFraction;
            float wingWidth = (rect.width - middleWidth) / 2f - capWidth;
            DrawSlice(rect.x, capWidth, 0f, capFraction);
            DrawSlice(rect.x + capWidth, wingWidth, capFraction, wingFraction);
            DrawSlice(rect.center.x - middleWidth / 2f, middleWidth, (1f - middleFraction) / 2f, middleFraction);
            DrawSlice(rect.center.x + middleWidth / 2f, wingWidth, (1f + middleFraction) / 2f, wingFraction);
            DrawSlice(rect.xMax - capWidth, capWidth, 1f - capFraction, capFraction);

            void DrawSlice(float x, float width, float start, float span)
                => GUI.DrawTextureWithTexCoords(new Rect(x, rect.y, width, rect.height), hudFrame,
                    new Rect(uv.x + start * uv.width, uv.y, span * uv.width, uv.height));
        }

        void DrawAbility(Rect rect, CommanderAbilityDef def, int slot)
        {
            reasons.TryGetValue(def.number, out var reason);
            bool enabled = reason == null;
            Frame(rect, enabled ? (Mouse.IsOver(rect) ? Cyan : Gold) : Muted * .7f);
            if (!icons.TryGetValue(def.number, out var icon))
            {
                string path = CommanderQuickActions.IconPath(def);
                icon = path == null ? null : ContentFinder<Texture2D>.Get(path, false);
                icons[def.number] = icon;
            }
            if (icon != null)
            {
                GUI.color = enabled ? Color.white : new Color(.58f, .58f, .58f, .94f);
                GenUI.DrawTextureWithMaterial(rect.ContractedBy(4), icon, enabled ? null : TexUI.GrayscaleGUI);
                GUI.color = Color.white;
            }
            else Label(rect, NmcText.Get("NMC_Text0060"), enabled ? Cyan : Muted, GameFont.Small);
            int remaining = CommanderState.Current?.Remaining(def) ?? 0;
            if (remaining > 0)
            {
                Rect badge = new Rect(rect.x + 3, rect.yMax - 20, rect.width - 6, 17);
                Widgets.DrawBoxSolid(badge, new Color(.035f, .045f, .055f, .94f));
                Label(badge, CooldownShort(remaining), Color.white);
            }
            else if (def.number == 12)
            {
                Rect badge = new Rect(rect.xMax - 31, rect.yMax - 20, 28, 17);
                Widgets.DrawBoxSolid(badge, Navy);
                Label(badge, (CommanderState.Current?.overloadCharges ?? 0) + "/3", enabled ? Cyan : Muted);
            }
            hits.Add(new Hit(rect, SlotButton + slot, enabled));
            string action = def.number == 3 || def.number == 6 || def.number == 11 ? NmcText.Get("NMC_Text0061") : NmcText.Get("NMC_Text0062");
            string extra = string.IsNullOrEmpty(def.extraCost) ? "" : "\n" + def.extraCost;
            TooltipHandler.TipRegion(rect, NmcText.Get("NMC_Text0064", def.label, FieldEconomy.PowerFor(def), FieldEconomy.VolatileFor(def), extra, (reason ?? action), (remaining > 0 ? NmcText.Get("NMC_Text0063", CommanderPanel.Period(remaining)) : "")));
        }

        void DrawControl(Rect rect, string label, int action, bool enabled, string tip)
        {
            if (action == PanelButton || action == CollapseButton) Frame(rect, Mouse.IsOver(rect) ? Cyan : Gold);
            Label(rect, label, enabled ? Color.white : Muted);
            hits.Add(new Hit(rect, action, enabled)); TooltipHandler.TipRegion(rect, tip);
        }

        void HandlePointer(Rect rect)
        {
            Event ev = Event.current;
            int control = GUIUtility.GetControlID(ID + 940200, FocusType.Passive);
            bool getsInput = Find.WindowStack.GetsInput(this) && Application.isFocused;
            if (pressed && (!getsInput || downMap != CommanderQuickActions.ViewedMap || GUIUtility.hotControl != pointerControl)) ReleasePointer();
            if (pressed && (ev.type == EventType.Ignore || (ev.type == EventType.KeyDown && ev.keyCode == KeyCode.Escape)))
            {
                center = new Vector2(downCenter.x / UI.screenWidth, downCenter.y / UI.screenHeight); hasPosition = true;
                ReleasePointer(); if (ev.type != EventType.Ignore) ev.Use(); return;
            }
            Vector2 screen = ev.mousePosition + windowRect.position;
            if (ev.type == EventType.MouseDown && ev.button == 1 && getsInput && Mouse.IsOver(rect))
            {
                int slot = -1;
                foreach (var hit in hits)
                    if (hit.action >= SlotButton && hit.action < SlotButton + SlotCount && hit.rect.Contains(ev.mousePosition))
                    { slot = hit.action - SlotButton; break; }
                ReleasePointer(); ev.Use();
                if (slot >= 0) OpenPicker(slot);
                return;
            }
            if (ev.type == EventType.MouseDown && ev.button == 0 && getsInput && Mouse.IsOver(rect))
            {
                pressed = true; moved = held = false; pointerControl = control;
                GUIUtility.hotControl = control; pressedAt = Time.realtimeSinceStartup;
                downScreen = screen; downCenter = windowRect.center; downMap = CommanderQuickActions.ViewedMap;
                pressedButton = 0; pressedEnabled = false; pressedRect = rect;
                foreach (var hit in hits)
                    if (hit.rect.Contains(ev.mousePosition)) { pressedButton = hit.action; pressedEnabled = hit.enabled; pressedRect = hit.rect; break; }
                ev.Use(); return;
            }
            if (!pressed) return;
            Vector2 delta = screen - downScreen;
            if (delta.sqrMagnitude > MoveTolerance * MoveTolerance) moved = true;
            if (Time.realtimeSinceStartup - pressedAt >= HoldSeconds) held = true;
            if (ev.type == EventType.MouseDrag && ev.button == 0)
            {
                if (held)
                {
                    Vector2 desired = downCenter + delta;
                    float halfW = windowRect.width / 2f, halfH = windowRect.height / 2f;
                    desired.x = Mathf.Clamp(desired.x, halfW + ScreenMargin, Mathf.Max(halfW + ScreenMargin, UI.screenWidth - halfW - ScreenMargin));
                    desired.y = Mathf.Clamp(desired.y, halfH + ScreenMargin, Mathf.Max(halfH + ScreenMargin, UI.screenHeight - halfH - ScreenMargin));
                    center = new Vector2(desired.x / UI.screenWidth, desired.y / UI.screenHeight); hasPosition = true;
                }
                ev.Use(); return;
            }
            if (ev.type == EventType.MouseUp && ev.button == 0)
            {
                bool click = !held && !moved && pressedEnabled && pressedRect.Contains(ev.mousePosition);
                int action = pressedButton; bool save = held && moved;
                ReleasePointer(); ev.Use();
                if (save) SaveLayout();
                if (click) Activate(action);
            }
        }

        void ReleasePointer()
        {
            if (pressed && GUIUtility.hotControl == pointerControl) GUIUtility.hotControl = 0;
            pressed = moved = held = false; pointerControl = 0; downMap = null;
        }
        void SaveLayout()
        {
            var settings = CommanderMod.Settings;
            settings.hudCollapsed = collapsed; settings.hudPositioned = hasPosition;
            settings.hudCenterX = center.x; settings.hudCenterY = center.y;
            settings.Write();
        }
        void Activate(int action)
        {
            nextAvailability = -1f;
            if (action == PanelButton) CommanderQuickActions.OpenPanel();
            else if (action == CollapseButton || action == ExpandButton)
            {
                center = new Vector2(windowRect.center.x / UI.screenWidth, windowRect.center.y / UI.screenHeight);
                hasPosition = true; collapsed = !collapsed; SaveLayout();
            }
            else if (action >= SlotButton && action < SlotButton + SlotCount)
            {
                int slot = action - SlotButton;
                if (assigned[slot] == null) OpenPicker(slot);
                else CommanderQuickActions.Activate(assigned[slot]);
            }
        }

        void OpenPicker(int slot)
        {
            var options = new List<FloatMenuOption>();
            string currentName = slotsReference != null && slot < slotsReference.Count ? slotsReference[slot] : null;
            if (!string.IsNullOrEmpty(currentName))
                options.Add(new FloatMenuOption(NmcText.Get("NMC_Text0065", (slot + 1)), () => SetSlot(slot, null)));
            foreach (var def in abilities)
            {
                string label = def.label;
                bool same = def.defName == currentName;
                if (same) label += NmcText.Get("NMC_Text0066");
                else
                    for (int i = 0; i < SlotCount; i++)
                        if (assigned[i] == def) { label += NmcText.Get("NMC_Text0067", (i + 1)); break; }
                options.Add(new FloatMenuOption(label, same ? (Action)null : () => SetSlot(slot, def)));
            }
            if (options.Count == 0) options.Add(new FloatMenuOption(NmcText.Get("NMC_Text0068"), null));
            Find.WindowStack.Add(new FloatMenu(options));
        }

        void SetSlot(int slot, CommanderAbilityDef def)
        {
            // The native menu may outlive a map/game/research change. Choices
            // are local ModSettings, never a multiplayer gameplay command.
            if (Current.Game != owner || Find.WindowStack.WindowOfType<CommanderHud>() != this ||
                slot < 0 || slot >= SlotCount || (def != null && CommanderResearch.For(def)?.IsFinished != true)) return;
            var settings = CommanderMod.Settings;
            var next = new List<string>(SlotCount);
            for (int i = 0; i < SlotCount; i++)
            {
                string name = settings.hudSlots != null && i < settings.hudSlots.Count ? settings.hudSlots[i] : "";
                next.Add(def != null && name == def.defName ? "" : name ?? "");
            }
            next[slot] = def?.defName ?? "";
            settings.hudSlots = next; settings.Write();
            RefreshAbilities(unlockMask);
        }

        static string Compact(int value) => value >= 100000000 ? NmcText.Get("NMC_Text0069", (value / 100000000d).ToString("0.##"))
            : value >= 100000 ? NmcText.Get("NMC_Text0070", (value / 10000d).ToString("0.#")) : value.ToString("N0");
        static string CooldownShort(int ticks) => ticks >= 60000 ? NmcText.Get("NMC_Text0071", (ticks / 60000f).ToString("0.#"))
            : ticks >= 3600 ? NmcText.Get("NMC_Text0072", (ticks / 3600f).ToString("0.#")) : NmcText.Get("NMC_Text0073", Mathf.CeilToInt(ticks / 60f));

    }
}
