using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Xml;
using HarmonyLib;
using Nivarian.NivarianDrones;
using NivarianCommander;
using RimWorld;
using UnityEngine;
using Verse;

namespace NMC.TickPerformanceProbe
{
    // Test assembly only. No hooks or driver execute without this exact flag.
    public sealed class Probe : GameComponent
    {
        static string Root => Path.GetDirectoryName(GenFilePaths.SaveDataFolderPath);
        static bool Enabled => GenCommandLine.CommandLineArgPassed("nmc-tick-performance-probe");
        static bool Baseline => GenCommandLine.CommandLineArgPassed("nmc-perf-baseline");
        static readonly FieldInfo Buffer = AccessTools.Field(typeof(Building_NutrientEnergyTower), "containedNutrition");
        static readonly Action<Building_NutrientEnergyTower> TickTower =
            (Action<Building_NutrientEnergyTower>)Delegate.CreateDelegate(typeof(Action<Building_NutrientEnergyTower>),
                AccessTools.Method(typeof(Building_NutrientEnergyTower), "Tick"));
        static readonly Func<Building_NutrientEnergyTower, bool> CanReceive =
            (Func<Building_NutrientEnergyTower, bool>)Delegate.CreateDelegate(typeof(Func<Building_NutrientEnergyTower, bool>),
                AccessTools.PropertyGetter(typeof(Building_NutrientEnergyTower), "CanReceiveNutrition"));
        bool done;
        double sink;
        Map map;
        Pawn observer;
        CompMonolithWeapon weapon;
        Building_NutrientEnergyTower tower, shadow;
        readonly List<Thing> enemies = new List<Thing>();
        Func<List<Thing>> select;

        public Probe(Game game) { }
        static void Record(string text) => File.AppendAllText(Path.Combine(Root, "evidence.txt"), text + "\n");
        static void Need(bool ok, string label)
        { Record((ok ? "PASS " : "FAIL ") + label); if (!ok) throw new Exception(label); }
        static string Ids(IEnumerable<Thing> things) => string.Join(",", things.Select(t => t.thingIDNumber));
        static double Stored(Building_NutrientEnergyTower t)
        {
            double value = (double)Buffer.GetValue(t);
            foreach (var item in t.GetDirectlyHeldThings())
                if (!item.IsNotFresh()) value += item.stackCount * (double)item.GetStatValue(StatDefOf.Nutrition);
            return value;
        }
        static bool Operation(Building_NutrientEnergyTower t)
            => t.Spawned && t.Faction == Faction.OfPlayer && FlickUtility.WantsToBeOn(t)
                && !t.IsBrokenDown() && !t.IsBurning()
                && ResearchProjectDef.Named("Nivarian_MothershipSupportInterface").IsFinished && NativeLedger.Component != null;
        void CheckNutrition(Building_NutrientEnergyTower t, string label)
        {
            double stored = Stored(t), target = Math.Min(Building_NutrientEnergyTower.Capacity, t.nutritionTarget);
            bool enabled = t.Spawned && t.Faction == Faction.OfPlayer && FlickUtility.WantsToBeOn(t);
            double needed = Math.Max(0d, target - stored);
            Need(t.NutritionStored == stored && t.NutritionNeeded == needed, label + " exact stored/needed");
            Need(CanReceive(t) == (enabled && needed > 0d), label + " receive");
            Need(t.HaulDestinationEnabled == (enabled && needed > 0d && t.autoFillNutrition && stored < target * .5d), label + " haul");
            Need(t.OperationPermitted == Operation(t), label + " operation");
            Need(t.IsOperating == (Operation(t) && stored >= t.DailyNutrition / 60000d
                && t.GetComp<CompPowerPlant_NutrientTower>().PowerOn), label + " operating");
        }
        T Spawn<T>(ThingDef def, IntVec3 at, Faction faction = null) where T : Thing
        {
            var thing = (T)ThingMaker.MakeThing(def, def.MadeFromStuff ? ThingDefOf.Steel : null);
            if (faction != null) thing.SetFaction(faction);
            GenSpawn.Spawn(thing, at, map);
            return thing;
        }
        bool OldValid(Thing t)
        {
            var parent = weapon.parent;
            if (t == parent || t.Destroyed || !t.Spawned || t.Map != parent.Map
                || (!(t is Pawn) && !(t is Building) && !(t is NivarianDroneBase))
                || t is Pawn p && p.DeadOrDowned || CommanderCloak.HiddenFrom(t, parent)
                || !t.HostileTo(parent.Faction ?? Faction.OfPlayer) || SupportDiplomacy.Friendly(t)) return false;
            return t.Position.InHorDistOf(parent.Position, weapon.Props.range)
                && GenSight.LineOfSight(parent.Position, t.Position, map, skipFirstCell: true);
        }
        List<Thing> OldSelect() => map.listerThings.AllThings.Where(OldValid)
            .OrderBy(t => t.Position.DistanceToSquared(weapon.parent.Position)).ThenBy(t => t.thingIDNumber)
            .Take(weapon.Props.maximumTargets).ToList();
        void CheckSelection(string label)
        { Need(Ids(select()) == Ids(OldSelect()), label + " same ordered targets"); }
        void Measure(string name, int iterations, Action action)
        {
            for (int i = 0; i < 30; i++) action();
            var samples = new List<double>();
            for (int round = 0; round < 5; round++)
            {
                var watch = Stopwatch.StartNew();
                for (int i = 0; i < iterations; i++) action();
                watch.Stop(); samples.Add(watch.Elapsed.TotalMilliseconds * 1000d / iterations);
            }
            samples.Sort();
            Record("METRIC " + name + " us_per_call=" + samples[2].ToString("R", CultureInfo.InvariantCulture)
                + " samples=" + string.Join(",", samples.Select(v => v.ToString("R", CultureInfo.InvariantCulture))));
        }
        void Setup()
        {
            map = Find.CurrentMap;
            observer = map.mapPawns.FreeColonists.First();
            foreach (var t in map.listerThings.AllThings.ToArray()) if (t != observer) t.Destroy();
            observer.Position = new IntVec3(5, 0, 5);
            if (observer.drafter != null) observer.drafter.Drafted = true;
            foreach (var c in map.AllCells)
            { map.roofGrid.SetRoof(c, null); map.terrainGrid.SetTerrain(c, TerrainDefOf.Concrete); }
            map.fogGrid.ClearAllFog();
            var research = ResearchProjectDef.Named("Nivarian_MothershipSupportInterface");
            ((Dictionary<ResearchProjectDef, float>)AccessTools.Field(typeof(ResearchManager), "progress").GetValue(Find.ResearchManager))[research] = research.baseCost;
            for (int i = 0; i < 6000; i++) Spawn<Thing>(ThingDefOf.Steel, new IntVec3(10 + i % 100, 0, 10 + i / 100));
            tower = Spawn<Building_NutrientEnergyTower>(ThingDef.Named("NMC_NutrientEnergyTower"), new IntVec3(125, 0, 125), Faction.OfPlayer);
            shadow = Spawn<Building_NutrientEnergyTower>(ThingDef.Named("NMC_ShadowEnergyTower"), new IntVec3(135, 0, 125), Faction.OfPlayer);
            var monolith = Spawn<Building>(ThingDef.Named("NMC_HyperdimensionalMonolith"), new IntVec3(140, 0, 150), Faction.OfPlayer);
            weapon = monolith.GetComp<CompMonolithWeapon>();
            monolith.GetComp<CompPowerTrader>().PowerOn = true;
            var method = AccessTools.Method(typeof(CompMonolithWeapon), "SelectTargets");
            select = method == null ? (Func<List<Thing>>)OldSelect : (Func<List<Thing>>)Delegate.CreateDelegate(typeof(Func<List<Thing>>), weapon, method);
            for (int i = 0; i < 48; i++) enemies.Add(Spawn<Building>(ThingDef.Named("Sandbags"), new IntVec3(120 + i % 8 * 5, 0, 170 + i / 8 * 4), Faction.OfMechanoids));
            var droneDef = DefDatabase<ThingDef>.AllDefsListForReading.First(d => d.thingClass != null && typeof(NivarianDroneBase).IsAssignableFrom(d.thingClass));
            enemies.Add(Spawn<Thing>(droneDef, new IntVec3(145, 0, 155), Faction.OfMechanoids));
            Record("FIXTURE allThings=" + map.listerThings.AllThings.Count + " enemies=" + enemies.Count + " drone=" + droneDef.defName);
            Need(weapon.Powered && OldSelect().Count == weapon.Props.maximumTargets, "powered weapon with full target set");
        }
        void NutritionChecks()
        {
            foreach (var t in new[] { tower, shadow })
            {
                var food = t.GetDirectlyHeldThings();
                t.nutritionTarget = 10f; Buffer.SetValue(t, 0d);
                var rice = ThingMaker.MakeThing(ThingDef.Named("RawRice")); rice.stackCount = 40;
                Need(food.TryAdd(rice), t.def.defName + " initial food");
                CheckNutrition(t, "food only");
                rice.SplitOff(3).Destroy(); CheckNutrition(t, "same-tick split");
                var more = ThingMaker.MakeThing(ThingDef.Named("RawRice")); more.stackCount = 5;
                Need(food.TryAdd(more), "same-stack merge"); CheckNutrition(t, "same-tick merge");
                rice.stackCount--; CheckNutrition(t, "direct stack change");
                var rot = rice.TryGetComp<CompRottable>(); rot.RotProgress = rot.PropsRot.TicksToRotStart + 1;
                CheckNutrition(t, "same-tick rot"); rot.RotProgress = 0;
                foreach (double amount in new[] { 0d, 1d, 5d, 10d })
                { Buffer.SetValue(t, amount); CheckNutrition(t, "buffer " + amount); }
                t.autoFillNutrition = false; CheckNutrition(t, "auto off"); t.autoFillNutrition = true;
                t.GetComp<CompFlickable>().SwitchIsOn = false; CheckNutrition(t, "switch off");
                t.GetComp<CompFlickable>().SwitchIsOn = true;
                t.GetComp<CompBreakdownable>().DoBreakdown(); CheckNutrition(t, "broken");
                t.GetComp<CompBreakdownable>().Notify_Repaired();
                food.ClearAndDestroyContents(); Buffer.SetValue(t, 0d); CheckNutrition(t, "empty");
                Buffer.SetValue(t, 3d);
                // This fixture is paused: the power net has not processed the
                // switch/repair signals. Restore the explicit power precondition.
                t.GetComp<CompPowerPlant_NutrientTower>().PowerOn = true;
                t.GetComp<CompPowerPlant_NutrientTower>().UpdateDesiredPowerOutput();
                CheckNutrition(t, "restored");
                Need(t.IsOperating, "restored tower actually operating");
            }
            var at = observer.Position;
            observer.Position = shadow.Position + new IntVec3(3, 0, 0);
            Need(CommanderCloak.Covered(observer), "nearby fueled shadow tower cloaks pawn");
            Buffer.SetValue(shadow, 0d); Need(!CommanderCloak.Covered(observer), "empty shadow tower stops cloak immediately");
            Buffer.SetValue(shadow, 3d); observer.Position = at;
            Need(!CommanderCloak.Covered(observer), "distant pawn outside shadow range");
        }
        void WeaponChecks()
        {
            CheckSelection("initial");
            foreach (var target in enemies)
            {
                target.SetFaction(Faction.OfPlayer); CheckSelection("live faction " + target.thingIDNumber);
                target.SetFaction(Faction.OfMechanoids);
            }
            var moved = enemies[0]; var original = moved.Position;
            moved.Position = new IntVec3(240, 0, 240); CheckSelection("same-tick move outside range");
            moved.Position = original;
            moved.DeSpawn(); CheckSelection("despawn"); GenSpawn.Spawn(moved, original, map); CheckSelection("respawn");
            int cap = weapon.Props.maximumTargets;
            foreach (int maximum in new[] { 0, 1, 3, 10, 100 })
            { weapon.Props.maximumTargets = maximum; CheckSelection("cap " + maximum); }
            weapon.Props.maximumTargets = cap;
            var expected = Ids(OldSelect()); int shots = weapon.roundsFired, hits = weapon.targetsHit;
            weapon.weaponWait = 0; weapon.CompTick();
            Need(weapon.roundsFired == shots + 1 && weapon.targetsHit == hits + cap && Ids(weapon.LastTargets) == expected, "real pulse hits same ordered targets");
            Need(weapon.weaponWait == weapon.Props.intervalTicks, "ordinary pulse interval retained");
            var wave = map.GetComponent<SpacetimeWaveMap>(); wave.remaining = 100;
            int wait = weapon.weaponWait; weapon.CompTick();
            Need(weapon.weaponWait == Mathf.CeilToInt(wait / (float)SpacetimeWaveStats.TurretMultiplier) - 1, "wave activation rescales cooldown");
            wave.remaining = 0; wait = weapon.weaponWait; weapon.CompTick();
            Need(weapon.weaponWait == Math.Min(weapon.Props.intervalTicks, wait * SpacetimeWaveStats.TurretMultiplier) - 1, "wave end rescales cooldown");
            Measure("selection_reference", 500, () => sink += OldSelect().Count);
            Measure("selection_current", 500, () => sink += select().Count);
            foreach (var target in enemies) target.Destroy();
            weapon.weaponWait = 0; weapon.beamTicks = 0;
            Need(OldSelect().Count == 0, "no-target benchmark precondition");
            Measure("monolith_idle_tick", 1000, weapon.CompTick);
            Need(weapon.roundsFired == shots + 1, "idle tick never fires");
        }
        void TowerBenchmarks()
        {
            Buffer.SetValue(tower, 3d); tower.nutritionTarget = 10f;
            var food = ThingMaker.MakeThing(ThingDef.Named("RawRice")); food.stackCount = 40;
            Need(tower.GetDirectlyHeldThings().TryAdd(food), "benchmark physical food");
            Measure("nutrition_stored", 20000, () => sink += tower.NutritionStored);
            Measure("nutrition_needed", 20000, () => sink += tower.NutritionNeeded);
            Measure("can_receive", 20000, () => sink += CanReceive(tower) ? 1 : 0);
            Measure("haul_enabled", 20000, () => sink += tower.HaulDestinationEnabled ? 1 : 0);
            Measure("operation_permitted", 20000, () => sink += tower.OperationPermitted ? 1 : 0);
            Measure("is_operating", 20000, () => sink += tower.IsOperating ? 1 : 0);
            double before = Stored(tower), required = tower.DailyNutrition / 60000d;
            const int totalCalls = 30 + 5 * 1000;
            double expectedBuffer = (double)Buffer.GetValue(tower);
            for (int i = 0; i < totalCalls; i++) expectedBuffer = Math.Max(0d, expectedBuffer - required);
            Measure("tower_tick", 1000, () => TickTower(tower));
            Need((double)Buffer.GetValue(tower) == expectedBuffer && Stored(tower) < before, "tick consumes exact original per-tick nutrition");
            CheckNutrition(tower, "after production");
            Measure("distant_cloak_query", 20000, () => sink += CommanderCloak.Covered(observer) ? 1 : 0);
        }
        static double ReferenceStored(NutrientTowerFoodContainer food, double buffer)
        {
            double amount = buffer;
            foreach (var item in food)
                if (!item.IsNotFresh()) amount += item.stackCount * (double)item.GetStatValue(StatDefOf.Nutrition);
            return amount;
        }
        void Pair(string name, Func<double> reference, Func<double> current)
        {
            Need(reference() == current(), "paired " + name + " same result");
            for (int i = 0; i < 10000; i++) { sink += reference(); sink += current(); }
            const int count = 200000;
            var oldSamples = new List<double>(); var newSamples = new List<double>();
            for (int round = 0; round < 7; round++)
                for (int half = 0; half < 2; half++)
                {
                    bool old = (round + half) % 2 == 0;
                    var callback = old ? reference : current;
                    var watch = Stopwatch.StartNew();
                    for (int i = 0; i < count; i++) sink += callback();
                    watch.Stop();
                    (old ? oldSamples : newSamples).Add(watch.Elapsed.TotalMilliseconds * 1000d / count);
                }
            oldSamples.Sort(); newSamples.Sort();
            Record("PAIR " + name + " reference_us=" + oldSamples[3].ToString("R", CultureInfo.InvariantCulture)
                + " current_us=" + newSamples[3].ToString("R", CultureInfo.InvariantCulture)
                + " reference_samples=" + string.Join(",", oldSamples.Select(v => v.ToString("R", CultureInfo.InvariantCulture)))
                + " current_samples=" + string.Join(",", newSamples.Select(v => v.ToString("R", CultureInfo.InvariantCulture))));
        }
        void ReadBenchmarks()
        {
            map = Find.CurrentMap;
            var at = map.Center;
            foreach (var c in CellRect.CenteredOn(at, 4))
            {
                foreach (var t in c.GetThingList(map).ToArray()) if (!(t is Pawn)) t.Destroy();
                map.terrainGrid.SetTerrain(c, TerrainDefOf.Concrete);
            }
            var research = ResearchProjectDef.Named("Nivarian_MothershipSupportInterface");
            ((Dictionary<ResearchProjectDef, float>)AccessTools.Field(typeof(ResearchManager), "progress").GetValue(Find.ResearchManager))[research] = research.baseCost;
            tower = Spawn<Building_NutrientEnergyTower>(ThingDef.Named("NMC_NutrientEnergyTower"), at, Faction.OfPlayer);
            tower.nutritionTarget = 10f; Buffer.SetValue(tower, 3d);
            var rice = ThingMaker.MakeThing(ThingDef.Named("RawRice")); rice.stackCount = 40;
            Need(tower.GetDirectlyHeldThings().TryAdd(rice) && tower.IsOperating, "paired benchmark fueled/operating precondition");
            var food = (NutrientTowerFoodContainer)tower.GetDirectlyHeldThings();
            double buffer = (double)Buffer.GetValue(tower);
            Func<double> stored = () => ReferenceStored(food, buffer);
            Func<double> needed = () => Math.Max(0d, Math.Min(Building_NutrientEnergyTower.Capacity, tower.nutritionTarget) - ReferenceStored(food, buffer));
            Func<bool> enabled = () => tower.Spawned && tower.Faction == Faction.OfPlayer && FlickUtility.WantsToBeOn(tower);
            Func<bool> operation = () => enabled() && !tower.IsBrokenDown() && !tower.IsBurning() && research.IsFinished && NativeLedger.Component != null;
            Pair("nutrition_stored", stored, () => tower.NutritionStored);
            Pair("nutrition_needed", needed, () => tower.NutritionNeeded);
            Pair("can_receive", () => enabled() && needed() > 0 ? 1d : 0d, () => CanReceive(tower) ? 1d : 0d);
            Pair("haul_enabled", () => enabled() && needed() > 0 && tower.autoFillNutrition && stored() < Math.Min(Building_NutrientEnergyTower.Capacity, tower.nutritionTarget) * .5d ? 1d : 0d, () => tower.HaulDestinationEnabled ? 1d : 0d);
            Pair("operation_permitted", () => operation() ? 1d : 0d, () => tower.OperationPermitted ? 1d : 0d);
            Pair("is_operating", () => operation() && stored() >= tower.DailyNutrition / 60000d && tower.GetComp<CompPowerPlant_NutrientTower>().PowerOn ? 1d : 0d, () => tower.IsOperating ? 1d : 0d);
            Need((double)Buffer.GetValue(tower) == buffer && food[0].stackCount == 40, "paired benchmark did not change nutrition state");
            File.WriteAllText(Path.Combine(Root, "complete"), "PASS"); Application.Quit();
        }
        public override void LoadedGame()
        {
            if (!Enabled || !File.Exists(Path.Combine(Root, "reload"))) return;
            try
            {
                Find.TickManager.Pause();
                var loaded = Find.CurrentMap.listerThings.ThingsOfDef(ThingDef.Named("NMC_NutrientEnergyTower")).Cast<Building_NutrientEnergyTower>().Single();
                string expected = File.ReadAllText(Path.Combine(Root, "stored.txt"));
                string actual = loaded.NutritionStored.ToString("R", CultureInfo.InvariantCulture);
                Record("LOAD expected=" + expected + " actual=" + actual);
                Need(actual == expected, "save/load exact serialized nutrition and rebuilt component references");
                CheckNutrition(loaded, "loaded");
                File.WriteAllText(Path.Combine(Root, "complete"), "PASS"); done = true; Application.Quit();
            }
            catch (Exception e) { Fail(e); }
        }
        void Fail(Exception e)
        { done = true; Record(e.ToString()); File.WriteAllText(Path.Combine(Root, "failed"), e.ToString()); Application.Quit(); }
        public override void GameComponentUpdate()
        {
            if (done || !Enabled || Current.ProgramState != ProgramState.Playing || LongEventHandler.AnyEventNowOrWaiting) return;
            try
            {
                done = true; Find.TickManager.Pause();
                using (var sha = SHA256.Create())
                    Need(BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(typeof(CompMonolithWeapon).Assembly.Location))).Replace("-", "")
                        == File.ReadAllText(Path.Combine(Root, "core.sha256")).Trim(), "exact candidate loaded baseline=" + Baseline);
                if (GenCommandLine.CommandLineArgPassed("nmc-tick-read-benchmark")) { ReadBenchmarks(); return; }
                Rand.PushState(210021);
                try { Setup(); NutritionChecks(); WeaponChecks(); TowerBenchmarks(); }
                finally { Rand.PopState(); }
                GameDataSaveLoader.SaveGame("TickPerformance");
                // Verse writes doubles with its own XML precision. Compare the
                // reloaded value to the actual saved value, not unsaved low bits.
                var xml = new XmlDocument();
                xml.Load(Path.Combine(GenFilePaths.SaveDataFolderPath, "Saves", "TickPerformance.rws"));
                double saved = double.Parse(xml.SelectSingleNode("//thing[def='NMC_NutrientEnergyTower']/nmcContainedNutrition").InnerText, CultureInfo.InvariantCulture);
                foreach (var item in tower.GetDirectlyHeldThings())
                    if (!item.IsNotFresh()) saved += item.stackCount * (double)item.GetStatValue(StatDefOf.Nutrition);
                File.WriteAllText(Path.Combine(Root, "stored.txt"), saved.ToString("R", CultureInfo.InvariantCulture));
                File.WriteAllText(Path.Combine(Root, "reload"), "1");
                GameDataSaveLoader.LoadGame("TickPerformance");
            }
            catch (Exception e) { Fail(e); }
        }
    }
}
