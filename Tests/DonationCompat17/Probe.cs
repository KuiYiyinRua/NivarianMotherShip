using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace NMC.DonationCompat17
{
    public sealed class Holder : IThingHolder
    {
        public IThingHolder ParentHolder { get; set; }
        public ThingOwner<Thing> contents;
        public readonly List<IThingHolder> children = new List<IThingHolder>();
        public bool throwDirect, throwChildren;
        public Holder(IThingHolder parent) { ParentHolder = parent; contents = new ThingOwner<Thing>(this); }
        public ThingOwner GetDirectlyHeldThings() { if (throwDirect) throw new InvalidOperationException("fixture direct holder failure"); return contents; }
        public void GetChildHolders(List<IThingHolder> list) { if (throwChildren) throw new InvalidOperationException("fixture child holder failure"); list.AddRange(children); }
    }
    public sealed class Probe : GameComponent
    {
        static string Root => Path.GetDirectoryName(GenFilePaths.SaveDataFolderPath);
        static readonly Dictionary<Thing, Holder> fixtures = new Dictionary<Thing, Holder>();
        static Thing brokenRoot;
        static Thing malformedItem;
        static string mode;
        static int ownerCalls, draws, uiErrors;
        static Holder broken, healthy;
        Type trade, shopType, worker;
        Map map;
        Thing ground, healthyCargo, badCargo, pawnCargo;
        Window window;
        int frames, cycle, failures;
        bool initialized, done, candidate;
        public Probe(Game game) { }
        static void Record(string text) => File.AppendAllText(Path.Combine(Root, "evidence.txt"), text + "\n");
        void Check(bool ok, string text) { Record((ok ? "PASS " : "FAIL ") + text); if (!ok) failures++; }
        static bool ResolveOwner(Thing thing, ref ThingOwner __result)
        {
            if (!fixtures.TryGetValue(thing, out var holder)) return true;
            ownerCalls++;
            if (thing == malformedItem && mode == "itemThrows") throw new InvalidOperationException("fixture item owner failure");
            if (thing == brokenRoot && mode == "ownerThrows") throw new InvalidOperationException("fixture interactable owner failure");
            __result = thing == brokenRoot && mode == "nullOwner" ? new ThingOwner<Thing>(null) : holder.contents;
            return false;
        }
        static void ExportResult(ref IEnumerable<Thing> __result)
        { if (mode == "exportThrows") __result = BrokenExport(__result); }
        static IEnumerable<Thing> BrokenExport(IEnumerable<Thing> original)
        {
            foreach (var thing in original) { yield return thing; break; }
            throw new InvalidOperationException("fixture orbital export enumeration failure");
        }
        static void DrawEnter() { if (Event.current.type == EventType.Repaint) draws++; }
        static void DrawExit(Exception __exception) { if (__exception != null) { uiErrors++; Record("UI_EXCEPTION " + __exception.GetType().Name + " " + __exception.Message); } }
        object Call(string method, params object[] args) => trade.GetMethod(method).Invoke(null, args);
        List<Thing> Scan() => (List<Thing>)Call("Donations", map);
        void Configure(string value)
        {
            mode = value; broken.throwDirect = broken.throwChildren = false; broken.children.Clear();
            if (value == "nullChild") broken.children.Add(null);
            if (value == "childrenThrow") broken.throwChildren = true;
            if (value == "directThrow") broken.children.Add(new Holder(broken) { throwDirect = true });
            if (value == "cycle") broken.children.Add(broken);
        }
        void Case(string value)
        {
            Configure(value);
            int before = ownerCalls;
            try
            {
                var goods = Scan();
                Check(goods.Contains(ground) && goods.Contains(healthyCargo), value + " retains ground and healthy container goods");
                Check(!goods.Contains(pawnCargo), value + " excludes pawn inventory");
                Check(goods.Select(t => t.thingIDNumber).Distinct().Count() == goods.Count, value + " has no duplicate goods");
            }
            catch (Exception ex) { Record("CASE_EXCEPTION " + value + " " + ex.GetBaseException()); Check(false, value + " scan must not throw"); }
            Check(ownerCalls > before, value + " reached real container scan");
        }
        Thing Spawn(ThingDef def, IntVec3 cell, int count = 1)
        {
            var thing = ThingMaker.MakeThing(def, def.MadeFromStuff ? ThingDefOf.Steel : null);
            thing.stackCount = count;
            if (def.CanHaveFaction) thing.SetFaction(Faction.OfPlayer);
            return GenSpawn.Spawn(thing, cell, map);
        }
        void Setup()
        {
            map = Find.CurrentMap; Find.TickManager.Pause();
            Record("GAME " + VersionControl.CurrentVersionStringWithRev + " mods=" + string.Join(",", LoadedModManager.RunningMods.Select(m => m.PackageId)));
            var assembly = AppDomain.CurrentDomain.GetAssemblies().Single(a => a.GetName().Name == "NivarianCommander");
            Record("CORE " + assembly.Location + " SHA256=" + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(assembly.Location))));
            trade = assembly.GetType("NivarianCommander.PointShopTrade", true);
            candidate = AccessTools.Method(trade, "ContainerContents") != null;
            shopType = assembly.GetType("NivarianCommander.Window_MothershipPointShop", true);
            worker = assembly.GetType("NivarianCommander.MainButtonWorker_PointShop", true);
            var center = map.Center;
            foreach (var cell in CellRect.CenteredOn(center, 8).Cells)
            {
                map.roofGrid.SetRoof(cell, null);
                foreach (var t in cell.GetThingList(map).ToList()) if (!(t is Pawn)) t.Destroy(DestroyMode.Vanish);
                map.terrainGrid.SetTerrain(cell, TerrainDefOf.Soil);
            }
            var beacon = (Building_OrbitalTradeBeacon)Spawn(ThingDefOf.OrbitalTradeBeacon, center);
            beacon.GetComp<CompPowerTrader>().PowerOn = true;
            ground = Spawn(ThingDefOf.Gold, center + new IntVec3(2,0,0), 30);
            var healthyRoot = Spawn(DefDatabase<ThingDef>.GetNamed("Sarcophagus"), center + new IntVec3(3,0,2));
            brokenRoot = Spawn(DefDatabase<ThingDef>.GetNamed("Sarcophagus"), center + new IntVec3(3,0,-2));
            healthy = new Holder((IThingHolder)healthyRoot); broken = new Holder((IThingHolder)brokenRoot);
            healthyCargo = ThingMaker.MakeThing(ThingDefOf.Silver); healthyCargo.stackCount = 50; healthy.contents.TryAdd(healthyCargo);
            badCargo = ThingMaker.MakeThing(ThingDefOf.Gold); badCargo.stackCount = 5; broken.contents.TryAdd(badCargo);
            fixtures.Add(healthyRoot, healthy); fixtures.Add(brokenRoot, broken);
            malformedItem = Spawn(ThingDefOf.Gold, center + new IntVec3(-2,0,0), 3);
            fixtures.Add(malformedItem, new Holder(null));
            var pawn = map.mapPawns.FreeColonists.First();
            pawnCargo = ThingMaker.MakeThing(ThingDefOf.Gold); pawnCargo.stackCount = 7; pawn.inventory.innerContainer.TryAdd(pawnCargo);
            map.regionAndRoomUpdater.RebuildAllRegionsAndRooms();
            var harmony = new Harmony("nmc17.donationcompat.probe");
            harmony.Patch(AccessTools.Method(typeof(ThingOwnerUtility), "TryGetInnerInteractableThingOwner"), prefix: new HarmonyMethod(typeof(Probe), nameof(ResolveOwner)));
            harmony.Patch(AccessTools.Method(typeof(TradeUtility), "AllLaunchableThingsForTrade"), postfix: new HarmonyMethod(typeof(Probe), nameof(ExportResult)));
            harmony.Patch(AccessTools.Method(shopType, "DoWindowContents"), prefix: new HarmonyMethod(typeof(Probe), nameof(DrawEnter)), finalizer: new HarmonyMethod(typeof(Probe), nameof(DrawExit)));
            Check(Call("MapUnavailable", map) == null, "real home map ledger and powered trade beacon pass eligibility");
            Check(beacon.TradeableCells.Contains(healthyRoot.Position) && beacon.TradeableCells.Contains(brokenRoot.Position), "both real container roots are in beacon coverage");
            Case("normal"); Case("nullOwner"); Case("nullChild"); Case("childrenThrow"); Case("directThrow"); Case("ownerThrows"); Case("itemThrows"); Case("exportThrows");
            if (candidate)
            {
                Case("cycle");
                Configure("normal"); var parent = healthy.ParentHolder; healthy.ParentHolder = healthy;
                var cycleGoods = Scan();
                Check(cycleGoods.Contains(ground) && !cycleGoods.Contains(healthyCargo), "cyclic parent chain excluded without blocking healthy map goods");
                healthy.ParentHolder = parent;
                Check(Scan().Contains(healthyCargo), "repaired parent chain becomes available on next scan");
            }
            else Record("SKIP baseline cyclic graph to avoid unbounded native traversal");
            Configure("nullChild");
        }
        void Open()
        {
            worker.GetMethod("Activate").Invoke(Activator.CreateInstance(worker), null);
            window = Find.WindowStack.Windows.Single(w => w.GetType() == shopType);
            AccessTools.Field(shopType, "selectedMap").SetValue(window, map);
            AccessTools.Method(shopType, "Switch").Invoke(window, new object[] { true });
            Configure(new[] { "nullChild", "childrenThrow", "ownerThrows" }[cycle]);
            draws = uiErrors = 0;
        }
        void DonateThroughReview()
        {
            if (!candidate) return;
            Configure("ownerThrows");
            var cart = (Dictionary<int,int>)AccessTools.Field(shopType, "cart").GetValue(window);
            cart[ground.thingIDNumber] = 10;
            decimal old = (decimal)trade.GetProperty("Balance").GetValue(null);
            decimal expected = Math.Floor((decimal)Call("UnitValue", ground) * 10 / 10m);
            AccessTools.Method(shopType, "Review").Invoke(window, new object[] { new List<Thing> { ground } });
            var confirm = Find.WindowStack.Windows.Single(w => w.GetType().Name == "Window_PointShopOrder");
            var order = AccessTools.Field(confirm.GetType(), "order").GetValue(confirm);
            var callback = (Delegate)AccessTools.Field(confirm.GetType(), "submit").GetValue(confirm);
            callback.DynamicInvoke(order); confirm.Close();
            Check(ground.stackCount == 20, "real review callback removes exactly ten selected items");
            Check((decimal)trade.GetProperty("Balance").GetValue(null) == old + expected, "real donation credits exact points despite unrelated broken container");
            callback.DynamicInvoke(order);
            Check(ground.stackCount == 20 && (decimal)trade.GetProperty("Balance").GetValue(null) == old + expected, "repeated request cannot double donate or double credit");
            Check(healthyCargo.stackCount == 50 && badCargo.stackCount == 5 && pawnCargo.stackCount == 7, "unselected healthy broken and pawn goods unchanged");
            cart[healthyCargo.thingIDNumber] = 10;
            old = (decimal)trade.GetProperty("Balance").GetValue(null);
            AccessTools.Method(shopType, "Review").Invoke(window, new object[] { new List<Thing> { healthyCargo } });
            confirm = Find.WindowStack.Windows.Single(w => w.GetType().Name == "Window_PointShopOrder");
            order = AccessTools.Field(confirm.GetType(), "order").GetValue(confirm);
            callback = (Delegate)AccessTools.Field(confirm.GetType(), "submit").GetValue(confirm);
            callback.DynamicInvoke(order); confirm.Close();
            Check(healthyCargo.stackCount == 40 && (decimal)trade.GetProperty("Balance").GetValue(null) == old + 1m, "actual healthy container cargo donation removes ten silver and credits one point");
            old = (decimal)trade.GetProperty("Balance").GetValue(null);
            Call("Donate", map, new[] { badCargo.thingIDNumber }, new[] { 1 }, Call("Exact", Call("UnitValue", badCargo)), "blocked-container-request");
            Check(badCargo.stackCount == 5 && (decimal)trade.GetProperty("Balance").GetValue(null) == old, "invalid-container request rejected without removal or credit");
            Configure("nullChild");
        }
        public override void GameComponentUpdate()
        {
            if (!GenCommandLine.CommandLineArgPassed("nmc17-donation-compat") || done || Current.ProgramState != ProgramState.Playing || LongEventHandler.AnyEventNowOrWaiting) return;
            try
            {
                if (!initialized) { initialized = true; Setup(); }
                if (frames == 0) Open();
                frames++;
                if (frames == 80)
                {
                    Check(draws > 20 && uiErrors == 0, "donation page renders without exceptions cycle=" + cycle);
                    var rows = (List<Thing>)AccessTools.Field(shopType, "rows").GetValue(window);
                    Check(rows.Contains(ground) && rows.Contains(healthyCargo), "donation UI lists healthy goods cycle=" + cycle);
                    ScreenCapture.CaptureScreenshot(Path.Combine(Root, "shop-" + cycle + ".png"));
                    if (cycle == 0) DonateThroughReview();
                }
                if (frames < 150) return;
                window.Close(); Check(!Find.WindowStack.Windows.Contains(window), "donation window closes cycle=" + cycle);
                if (++cycle < 3) { frames = 0; return; }
                Record("RESULT failures=" + failures);
                File.WriteAllText(Path.Combine(Root, failures == 0 ? "complete" : "failed"), "assertion failures=" + failures);
                done = true; Application.Quit();
            }
            catch (Exception ex) { Record(ex.ToString()); File.WriteAllText(Path.Combine(Root,"failed"), ex.ToString()); done=true; Application.Quit(); }
        }
    }
}
