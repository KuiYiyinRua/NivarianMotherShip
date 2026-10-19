using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using HarmonyLib;
using NivarianCommander;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace NMC.NutrientRefillProbe
{
    public sealed class Probe : GameComponent
    {
        static string Root => Path.GetDirectoryName(GenFilePaths.SaveDataFolderPath);
        static bool Baseline => GenCommandLine.CommandLineArgPassed("nmc-refill-baseline");
        static readonly FieldInfo Buffer = AccessTools.Field(typeof(Building_NutrientEnergyTower), "containedNutrition");
        readonly WorkGiver_FillNutrientEnergyTower giver = new WorkGiver_FillNutrientEnergyTower();
        Building_NutrientEnergyTower[] towers;
        Pawn worker;
        Thing rice;
        int stage, index, deadline;
        bool done;

        public Probe(Game game) { }
        static void Need(bool ok, string label)
        {
            File.AppendAllText(Path.Combine(Root, "evidence.txt"), (ok ? "PASS " : "FAIL ") + label + "\n");
            if (!ok) throw new Exception(label);
        }
        void Check(Building_NutrientEnergyTower tower, float target, double stored, bool automatic, string label)
        {
            NutrientTowerSupply.SetTarget(tower.Map, tower, target);
            Buffer.SetValue(tower, stored);
            Need(giver.HasJobOnThing(worker, tower) == automatic, tower.def.defName + " " + label + " target=" + target + " stored=" + stored);
            Need(GeneralHaulingTargets(tower) == automatic, tower.def.defName + " vanilla general hauling: " + label);
        }
        bool GeneralHaulingTargets(Building_NutrientEnergyTower tower)
            => StoreUtility.TryFindBestBetterNonSlotGroupStorageFor(rice, worker, tower.Map, StoragePriority.Unstored,
                Faction.OfPlayer, out var destination) && ReferenceEquals(destination, tower);
        void Boundaries(Building_NutrientEnergyTower tower)
        {
            foreach (var other in towers) if (other != tower) NutrientTowerSupply.SetTarget(other.Map, other, 0f);
            foreach (float target in new[] { 10f, 6f, 2f, 1f })
            {
                Check(tower, target, 0d, true, "empty automatically refills");
                Check(tower, target, target / 2d - .001d, true, "below half automatically refills");
                Check(tower, target, target / 2d, Baseline, "exact half requires no new automatic job");
                Check(tower, target, target / 2d + .001d, Baseline, "above half requires no new automatic job");
                Check(tower, target, target - .1d, Baseline, "minor consumption requires no new automatic job");
                Need(giver.HasJobOnThing(worker, tower, true), tower.def.defName + " explicit refill still permitted above half");
                Check(tower, target, target, false, "full target blocks refill");
                Need(!giver.HasJobOnThing(worker, tower, true), tower.def.defName + " full target blocks forced refill");
            }
            Check(tower, 6f, 2d, true, "target six triggers below three");
            Check(tower, 2f, 1.5d, Baseline, "lowered target recalculates trigger without discarding food");
            Need(tower.NutritionStored == 1.5d, tower.def.defName + " target adjustment retains stored nutrition");
            Check(tower, 2f, 0d, true, "empty fixture before auto toggle");
            NutrientTowerSupply.SetAutoFill(tower.Map, tower, false);
            Need(!giver.HasJobOnThing(worker, tower) && giver.HasJobOnThing(worker, tower, true), tower.def.defName + " auto off blocks automatic but permits forced refill");
            Need(GeneralHaulingTargets(tower) == Baseline, tower.def.defName + " vanilla general hauling honors auto toggle");
            NutrientTowerSupply.SetAutoFill(tower.Map, tower, true);
            Check(tower, 0f, 0d, false, "zero target blocks automatic refill");
            Need(!giver.HasJobOnThing(worker, tower, true), tower.def.defName + " zero target also blocks forced refill");
            Check(tower, 2f, 1.25d, Baseline, "above half before checking destination acceptance");
            Need(tower.Accepts(rice), tower.def.defName + " active delivery can still enter above half");
            // Count physical food as well as the fractional numeric buffer.
            Buffer.SetValue(tower, 0d);
            var meal = ThingMaker.MakeThing(ThingDefOf.MealSimple); meal.stackCount = 1;
            Need(tower.GetDirectlyHeldThings().TryAdd(meal), tower.def.defName + " physical food fixture accepted");
            Check(tower, 2f, .2d, Baseline, "physical food plus numeric buffer is above half");
            tower.GetDirectlyHeldThings().ClearAndDestroyContents();
            Buffer.SetValue(tower, 0d);
        }
        void StartDelivery()
        {
            var tower = towers[index % towers.Length];
            bool forced = index >= towers.Length;
            foreach (var other in towers) if (other != tower) NutrientTowerSupply.SetTarget(other.Map, other, 0f);
            worker.jobs.EndCurrentJob(JobCondition.InterruptForced, startNewJob: false);
            worker.pather.StopDead();
            worker.Position = tower.Position + new IntVec3(-4, 0, 0);
            NutrientTowerSupply.SetTarget(tower.Map, tower, 2f);
            NutrientTowerSupply.SetAutoFill(tower.Map, tower, true);
            tower.GetDirectlyHeldThings().ClearAndDestroyContents();
            Buffer.SetValue(tower, forced ? 1.25d : .5d);
            var job = giver.JobOnThing(worker, tower, forced);
            Need(job != null && job.def == JobDefOf.HaulToContainer && job.count > 0, tower.def.defName + (forced ? " forced" : " automatic") + " native hauling job created");
            Need(job.count * (double)job.targetA.Thing.GetStatValue(StatDefOf.Nutrition) >= (forced ? .75d : 1.5d),
                tower.def.defName + " delivery amount targets full reserve rather than half");
            job.playerForced = forced;
            worker.jobs.StartJob(job, JobCondition.InterruptForced, resumeCurJobAfterwards: false);
            deadline = Find.TickManager.TicksGame + 3000;
            Find.TickManager.CurTimeSpeed = TimeSpeed.Superfast;
            stage = 1;
        }
        public override void GameComponentUpdate()
        {
            if (done || !GenCommandLine.CommandLineArgPassed("nmc-nutrientrefill-probe") || Current.ProgramState != ProgramState.Playing || LongEventHandler.AnyEventNowOrWaiting) return;
            try
            {
                if (stage == 0)
                {
                    Find.TickManager.Pause();
                    using (var sha = SHA256.Create())
                        Need(BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(typeof(NutrientTowerSupply).Assembly.Location))).Replace("-", "") == File.ReadAllText(Path.Combine(Root, "core.sha256")).Trim(), "exact selected assembly loaded");
                    var map = Find.CurrentMap; map.fogGrid.ClearAllFog();
                    foreach (var enemy in map.mapPawns.AllPawnsSpawned.Where(p => p.HostileTo(Faction.OfPlayer)).ToArray()) enemy.Destroy();
                    foreach (var pawn in map.mapPawns.FreeColonists) if (pawn.drafter != null) pawn.drafter.Drafted = true;
                    foreach (var cell in new CellRect(60, 60, 45, 30).Cells)
                    {
                        map.roofGrid.SetRoof(cell, null);
                        foreach (var thing in cell.GetThingList(map).ToArray()) if (!(thing is Pawn)) thing.Destroy();
                        map.terrainGrid.SetTerrain(cell, TerrainDefOf.Soil);
                    }
                    towers = new[] { "NMC_NutrientEnergyTower", "NMC_ShadowEnergyTower" }.Select((name, i) =>
                    {
                        var tower = (Building_NutrientEnergyTower)ThingMaker.MakeThing(ThingDef.Named(name));
                        tower.SetFaction(Faction.OfPlayer); GenSpawn.Spawn(tower, new IntVec3(80 + i * 10, 0, 75), map); return tower;
                    }).ToArray();
                    worker = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
                    GenSpawn.Spawn(worker, new IntVec3(76, 0, 75), map);
                    worker.playerSettings.hostilityResponse = HostilityResponseMode.Ignore;
                    for (int i = 0; i < 3; i++)
                    {
                        var food = ThingMaker.MakeThing(ThingDef.Named("RawRice")); food.stackCount = food.def.stackLimit;
                        GenSpawn.Spawn(food, new IntVec3(75, 0, 72 + i), map); rice = food;
                    }
                    foreach (var tower in towers) Boundaries(tower);
                    StartDelivery();
                }
                else if (stage == 1)
                {
                    var tower = towers[index % towers.Length];
                    if (tower.NutritionStored >= 1.99d)
                    {
                        Find.TickManager.Pause();
                        Need(tower.NutritionStored <= 2.051d, tower.def.defName + " real native hauling completes at full target");
                        worker.jobs.EndCurrentJob(JobCondition.InterruptForced, startNewJob: false);
                        worker.pather.StopDead();
                        Need(!giver.HasJobOnThing(worker, tower), tower.def.defName + " completed delivery does not immediately repeat");
                        if (++index < towers.Length * 2) StartDelivery();
                        else { done = true; File.WriteAllText(Path.Combine(Root, "complete"), Baseline ? "BASELINE_CONFIRMED" : "PASS"); Application.Quit(); }
                    }
                    else if (Find.TickManager.TicksGame > deadline)
                        throw new Exception("Hauling timeout: " + worker.CurJob + " stored=" + tower.NutritionStored);
                }
            }
            catch (Exception error)
            { done = true; File.WriteAllText(Path.Combine(Root, "failed"), error.ToString()); Application.Quit(); }
        }
    }
}
