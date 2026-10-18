using System;
using System.IO;
using System.Linq;
using NivarianCommander;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace NMC.ContinuumEntryProbe
{
    public sealed class Probe : GameComponent
    {
        bool done;
        public Probe(Game game) { }
        static string Root => Path.GetDirectoryName(GenFilePaths.SaveDataFolderPath);
        static void Record(string text) => File.AppendAllText(Path.Combine(Root, "evidence.txt"), text + "\n");
        static void Need(bool ok, string label)
        { Record((ok ? "PASS " : "FAIL ") + label); if (!ok) throw new Exception(label); }
        public override void GameComponentUpdate()
        {
            if (done || !GenCommandLine.CommandLineArgPassed("nmc-continuum-entry-probe")
                || Current.ProgramState != ProgramState.Playing || LongEventHandler.AnyEventNowOrWaiting) return;
            done = true;
            try
            {
                Find.TickManager.Pause();
                var campaign = ContinuumCampaign.Current;
                Need(CommanderProgression.CampaignEnabled && campaign.TrySpawn(), "real campaign creates site and quest");
                var site = campaign.activeSite; var quest = campaign.activeQuest;
                var map = GetOrGenerateMapUtility.GetOrGenerateMap(site.Tile, new IntVec3(200, 1, 200), null);
                var battle = map.GetComponent<ContinuumBattlefield>();
                Need(battle.stage == 1 && battle.relay != null && battle.defenders.Count > 0, "real base generated with objective and defenders");
                var pawn = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
                var cell = CellFinder.RandomClosewalkCellNear(new IntVec3(15, 0, 15), map, 10);
                GenSpawn.Spawn(pawn, cell, map);
                for (int i = 0; i < 601; i++) battle.MapComponentTick();
                campaign.WorldComponentTick();
                Need(battle.hadPlayer && campaign.entered && !quest.Historical, "entry plus 601 battlefield ticks stays active");
                var ship = (Building_NivarianAircraft)ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("NMC_SageShip"));
                ship.SetFaction(Faction.OfPlayer);
                GenSpawn.Spawn(ship, CellFinder.RandomClosewalkCellNear(cell, map, 8), map);
                pawn.DeSpawn();
                Need(ship.TransporterComp.innerContainer.TryAdd(pawn), "player boards actual passenger ship");
                bool removeObject;
                Need(!site.ShouldRemoveMapNow(out removeObject), "native site retains map with occupied ship");
                Record("BEFORE spawnedPlayers=" + map.mapPawns.AllPawnsSpawned.Count(p => p.Faction == Faction.OfPlayer)
                    + " elapsed=" + battle.elapsed + " retreated=" + battle.retreated + " quest=" + quest.State);
                battle.MapComponentTick(); campaign.WorldComponentTick();
                Record("AFTER retreated=" + battle.retreated + " activeStage=" + campaign.activeStage + " quest=" + quest.State);
                Need(campaign.activeSite == site && !quest.Historical && !battle.retreated,
                    "occupied ship must not fail or detach the active base mission");
                // Exercise a transient absence while the native map still exists.
                ship.DeSpawn();
                for (int i = 0; i < 900; i++) { battle.MapComponentTick(); campaign.WorldComponentTick(); }
                Need(campaign.activeSite == site && !quest.Historical, "temporary absence does not fail a loaded map");
                GenSpawn.Spawn(ship, cell, map);
                // An empty player combat ship must also count as an encounter.
                ship.TransporterComp.innerContainer.Remove(pawn);
                battle.hadPlayer = false;
                battle.MapComponentTick();
                Need(battle.hadPlayer, "player aircraft alone registers entry");
                ship.TransporterComp.innerContainer.TryAdd(pawn);
                battle.retreated = true; // Legacy false-retreat flag, while the quest is still active.
                campaign.WorldComponentTick();
                Need(campaign.activeSite == site && !quest.Historical, "legacy retreat flag does not invalidate a loaded battlefield");
                ContinuumCampaign.ApplyBattlefieldResult(site.ID, 1, -1);
                Need(campaign.activeSite == site && !quest.Historical, "stale negative world report does not fail a loaded base");
                foreach (var defender in battle.defenders.ToList()) if (defender != null && !defender.Destroyed) defender.Destroy();
                battle.relay.HitPoints = 360; // Matches the reported post-combat snapshot.
                battle.MapComponentTick(); campaign.WorldComponentTick();
                Need(!battle.cleared && !quest.Historical, "cleared defenses with 360 HP core must not complete quest");
                var location = quest.PartsListForReading.OfType<QuestPart_ContinuumLocation>().Single();
                Need(location.QuestSelectTargets.Single().Thing == battle.relay,
                    "quest locator selects the surviving communications core");
                CameraJumper.TryJumpAndSelect(location.QuestSelectTargets.Single());
                Need(Find.CurrentMap == map && Find.Selector.SelectedObjects.Contains(battle.relay),
                    "native camera jump reaches battlefield and selects the core");
                Need(location.DescriptionPart != null && location.DescriptionPart.Contains("360"),
                    "quest describes the surviving core and its current hit points");
                var messages = (System.Collections.Generic.List<Message>)typeof(Messages)
                    .GetField("liveMessages", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic).GetValue(null);
                string reminder = "NMC_Text0818".Translate().ToString();
                Need(messages.Count(m => m.text == reminder) == 1, "cleared defenses emit one explicit core reminder");
                for (int i = 0; i < 120; i++) battle.MapComponentTick();
                Need(messages.Count(m => m.text == reminder) == 1, "core reminder is not repeated every tick");
                GameDataSaveLoader.SaveGame("Continuum-CoreReminder");
                Need(File.ReadAllText(GenFilePaths.FilePathForSavedGame("Continuum-CoreReminder"))
                    .Contains("<nmcBaseCoreReminderSent>True</nmcBaseCoreReminderSent>"),
                    "one-time reminder state is serialized for reload and multiplayer snapshots");
                battle.relay.Destroy();
                battle.MapComponentTick(); campaign.WorldComponentTick();
                Need(quest.State == QuestState.EndedSuccess && campaign.completedStage == 1 && campaign.activeStage == 2,
                    "clearing original base after boarding completes quest and advances stage");
                Need(site.HasMap && !site.Destroyed, "success preserves occupied original map");
                Need(location.DescriptionPart == null && location.QuestSelectTargets.Single().WorldObject == site,
                    "completed quest removes core hint and falls back to site location");
                var nextQuest = campaign.activeQuest;
                campaign.entered = true; // Native removal boundary: entered site no longer has a map.
                campaign.WorldComponentTick();
                Need(nextQuest.State == QuestState.EndedFailed && campaign.activeStage == 0 && campaign.retryRemaining == ContinuumCampaign.RetryTicks,
                    "actual missing map after entry still fails and schedules retry");
                File.WriteAllText(Path.Combine(Root, "complete"), "PASS");
            }
            catch (Exception e) { File.WriteAllText(Path.Combine(Root, "failed"), e.ToString()); Record(e.ToString()); }
            Application.Quit();
        }
    }
}
