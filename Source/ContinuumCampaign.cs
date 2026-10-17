using System;
using System.Collections.Generic;
using System.Linq;
using LudeonTK;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace NivarianCommander
{
    public sealed class ContinuumCampaign : WorldComponent
    {
        public static string[] StageNames => new[] { NmcText.Get("NMC_Text0215"), NmcText.Get("NMC_Text0216"), NmcText.Get("NMC_Text0217"), NmcText.Get("NMC_Text0218"), NmcText.Get("NMC_Text0219"), NmcText.Get("NMC_Text0220"), NmcText.Get("NMC_Text0221"), NmcText.Get("NMC_Text0222") };
        public const int FirstDayTicks = 7 * 60000, RetryTicks = 3 * 60000;
        public int completedStage, retryRemaining, activeStage, attempts;
        public int missionRemaining;
        public Site activeSite;
        public Quest activeQuest;
        public bool entered;
        bool persistentQuestMigrated;
        public ContinuumCampaign(World world) : base(world) { }
        public static ContinuumCampaign Current => Find.World?.GetComponent<ContinuumCampaign>();
        static Faction GetOrCreateFaction()
        {
            var def = DefDatabase<FactionDef>.GetNamedSilentFail("NMC_Continuum");
            if (def == null) return null;
            var faction = Find.FactionManager.AllFactionsListForReading.FirstOrDefault(f => f.def == def);
            if (faction == null)
            {
                // Runs in the world command/tick, including when adding the mod to
                // an existing save. Native faction serialization owns persistence.
                FactionGenerator.CreateFactionAndAddToManager(def);
                faction = Find.FactionManager.AllFactionsListForReading.FirstOrDefault(f => f.def == def);
            }
            return faction;
        }
        public override void WorldComponentTick()
        {
            if (!CommanderProgression.CampaignEnabled || completedStage >= 8) return;
            if (activeStage > 0)
            {
                // Migrate old timed missions in the deterministic world tick.
                // Keep the serialized legacy counter for save compatibility only.
                if (!persistentQuestMigrated)
                {
                    missionRemaining = 0;
                    if (activeQuest != null && !activeQuest.Historical)
                        activeQuest.description = MissionDescription(activeStage);
                    persistentQuestMigrated = true;
                }
                int result = ReadBattlefieldStatus();
                entered |= result == 1;
                if (result == 2) { Resolve(true); return; }
                if (activeSite == null || activeSite.Destroyed
                    || (entered && activeSite.Map == null) || activeQuest?.Historical == true) Resolve(false);
                return;
            }
            if (retryRemaining > 0) { retryRemaining--; if (retryRemaining > 0) return; }
            if (Find.TickManager.TicksGame < FirstDayTicks) return;
            TrySpawn();
        }
        // Multiplayer replaces this map read with an explicit host observation command.
        // An asynchronous map may be at a different simulation tick from the world.
        public int ReadBattlefieldStatus()
        {
            var battlefield = activeSite?.Map?.GetComponent<ContinuumBattlefield>();
            return battlefield?.cleared == true ? 2 : battlefield?.hadPlayer == true ? 1 : 0;
        }
        public static void ApplyBattlefieldResult(int siteId, int stage, int result)
        {
            var campaign = Current;
            if (campaign?.activeSite == null || campaign.activeSite.ID != siteId || campaign.activeStage != stage) return;
            if (result == 1) campaign.entered = true;
            else if (result == 2) campaign.Resolve(true);
            // Ignore legacy/stale retreat reports while the battlefield still exists.
            else if (result == -1 && campaign.activeSite.Map == null) campaign.Resolve(false);
        }
        public bool TrySpawn()
        {
            if (!CommanderProgression.CampaignEnabled || completedStage >= 8 || activeStage > 0) return false;
            if (!TryFindSiteTile(out PlanetTile tile)) { retryRemaining = 2500; return false; }
            var part = DefDatabase<SitePartDef>.GetNamedSilentFail("NMC_ContinuumBase");
            var faction = GetOrCreateFaction();
            if (part == null || faction == null) { retryRemaining = 2500; return false; }
            int stage = completedStage + 1;
            activeSite = SiteMaker.MakeSite(part, tile, faction, threatPoints: 250 + stage * stage * 90);
            activeSite.customLabel = NmcText.Get("NMC_Text0223", stage);
            Find.WorldObjects.Add(activeSite);
            activeStage = stage; missionRemaining = 0; attempts++; entered = false; persistentQuestMigrated = true;
            activeQuest = Quest.MakeRaw();
            activeQuest.root = DefDatabase<QuestScriptDef>.GetNamed("NMC_ContinuumCampaign");
            activeQuest.name = NmcText.Get("NMC_Text0224", StageNames[stage - 1], stage);
            activeQuest.description = MissionDescription(stage);
            activeQuest.challengeRating = Math.Min(3, 1 + (stage - 1) / 3);
            activeQuest.AddPart(new QuestPart_ContinuumLocation { site = activeSite, stage = stage });
            activeQuest.SetInitiallyAccepted();
            Find.QuestManager.Add(activeQuest);
            Find.LetterStack.ReceiveLetter(activeQuest.name, activeQuest.description, LetterDefOf.NeutralEvent, activeSite);
            return true;
        }
        static string MissionDescription(int stage) => NmcText.Get("NMC_Text0227", UnlockLabels(stage),
            stage < 8 ? NmcText.Get("NMC_Text0225") : NmcText.Get("NMC_Text0226"));

        static PlanetTile SurfaceOrigin(PlanetTile tile) => !tile.Valid ? PlanetTile.Invalid
            : tile.LayerDef.SurfaceTiles ? tile : Find.WorldGrid.Surface.GetClosestTile_NewTemp(tile);

        static bool UsableSiteTile(PlanetTile tile) => tile.Valid && tile.LayerDef.SurfaceTiles
            && !Find.World.Impassable(tile) && !Find.WorldObjects.AnyWorldObjectAt(tile)
            && TileFinder.IsValidTileForNewSettlement(tile);

        bool TryFindSiteTile(out PlanetTile tile)
        {
            // Stable ordering keeps map focus and collection order out of world RNG.
            // Caravan-only and orbital starts still have a surface search origin.
            var origins = Find.Maps.Where(m => m.IsPlayerHome).OrderBy(m => m.uniqueID).Select(m => m.Tile)
                .Concat(Find.Maps.Where(m => !m.IsPlayerHome && m.mapPawns.FreeColonistsSpawnedCount > 0)
                    .OrderBy(m => m.uniqueID).Select(m => m.Tile))
                .Concat(Find.WorldObjects.Caravans.Where(c => c.IsPlayerControlled).OrderBy(c => c.ID).Select(c => c.Tile))
                .Select(SurfaceOrigin).Where(t => t.Valid).Distinct().ToList();
            int minDistance = completedStage == 0 ? 1 : 3;
            foreach (var origin in origins)
                if (TileFinder.TryFindPassableTileWithTraversalDistance(origin, minDistance, 9, out tile,
                    validator: UsableSiteTile, ignoreFirstTilePassability: true, tileFinderMode: TileFinderMode.Near)) return true;
            foreach (var origin in origins)
                if (TileFinder.TryFindPassableTileWithTraversalDistance(origin, 1, 30, out tile,
                    validator: UsableSiteTile, ignoreFirstTilePassability: true, tileFinderMode: TileFinderMode.Near)) return true;
            // An isolated island or mountain enclave may have no caravan route to
            // any empty tile. Locate the nearest valid surface site for air travel.
            foreach (var origin in origins)
                if (TileFinder.TryFindTileWithDistance(origin, 1, origin.Layer.TilesCount, out tile,
                    validator: UsableSiteTile, exitOnFirstTileFound: true)) return true;
            tile = PlanetTile.Invalid;
            return false;
        }
        public static string UnlockLabels(int stage) => string.Join("、", DefDatabase<ResearchProjectDef>.AllDefsListForReading
            .Where(r => r.GetModExtension<CommanderResearchExtension>()?.requiredStage == stage)
            .OrderBy(r => r.defName, StringComparer.Ordinal).Select(r => r.label));
        public void Resolve(bool success)
        {
            if (activeStage <= 0) return;
            int stage = activeStage;
            var site = activeSite; var quest = activeQuest;
            activeSite = null; activeQuest = null; activeStage = 0; entered = false; missionRemaining = 0;
            if (quest != null && !quest.Historical) quest.End(success ? QuestEndOutcome.Success : QuestEndOutcome.Fail, sendLetter: false);
            if (success)
            {
                completedStage = Math.Max(completedStage, stage);
                retryRemaining = 0;
                Find.LetterStack.ReceiveLetter(NmcText.Get("NMC_Text0228"), NmcText.Get("NMC_Text0231", UnlockLabels(stage), (completedStage < 8 ? NmcText.Get("NMC_Text0229") : NmcText.Get("NMC_Text0230"))), LetterDefOf.PositiveEvent);
                TrySpawn();
            }
            else
            {
                retryRemaining = RetryTicks;
                Find.LetterStack.ReceiveLetter(NmcText.Get("NMC_Text0232"), NmcText.Get("NMC_Text0233", stage), LetterDefOf.NegativeEvent);
            }
            // Leave loaded maps intact so retreating colonists and their cargo remain recoverable.
            if (site != null && !site.Destroyed && !site.HasMap) site.Destroy();
        }
        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref completedStage, "nmcContinuumCompletedStage");
            Scribe_Values.Look(ref activeStage, "nmcContinuumActiveStage");
            Scribe_Values.Look(ref retryRemaining, "nmcContinuumRetryRemaining");
            Scribe_Values.Look(ref missionRemaining, "nmcContinuumMissionRemaining");
            Scribe_Values.Look(ref attempts, "nmcContinuumAttempts");
            Scribe_Values.Look(ref entered, "nmcContinuumEntered");
            Scribe_Values.Look(ref persistentQuestMigrated, "nmcContinuumPersistentQuestMigrated");
            Scribe_References.Look(ref activeSite, "nmcContinuumSite");
            Scribe_References.Look(ref activeQuest, "nmcContinuumQuest");
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            { completedStage = Math.Max(0, Math.Min(8, completedStage)); retryRemaining = Math.Max(0, retryRemaining); }
        }
    }
    public sealed class QuestPart_ContinuumLocation : QuestPart
    {
        public Site site;
        public int stage;
        public override IEnumerable<GlobalTargetInfo> QuestLookTargets { get { if (site != null && !site.Destroyed) yield return site; } }
        public override IEnumerable<GlobalTargetInfo> QuestSelectTargets => QuestLookTargets;
        public override string QuestSelectTargetsLabel => NmcText.Get("NMC_Text0234");
        public override IEnumerable<Dialog_InfoCard.Hyperlink> Hyperlinks => DefDatabase<ResearchProjectDef>.AllDefsListForReading
            .Where(r => r.GetModExtension<CommanderResearchExtension>()?.requiredStage == stage).Select(r => new Dialog_InfoCard.Hyperlink(r));
        public override void ExposeData()
        { base.ExposeData(); Scribe_References.Look(ref site, "site"); Scribe_Values.Look(ref stage, "stage"); }
    }
    public static class ContinuumDebug
    {
        [DebugAction("Nivarian Commander", "Continuum: locate current stage now", allowedGameStates = AllowedGameStates.Playing)]
        public static void Spawn() { if (Prefs.DevMode) { ContinuumCampaign.Current.retryRemaining = 0; ContinuumCampaign.Current.TrySpawn(); } }
        [DebugAction("Nivarian Commander", "Continuum: complete current stage", allowedGameStates = AllowedGameStates.Playing)]
        public static void Complete() { if (Prefs.DevMode) ContinuumCampaign.Current.Resolve(true); }
        [DebugAction("Nivarian Commander", "Continuum: fail and wait 3 days", allowedGameStates = AllowedGameStates.Playing)]
        public static void Fail() { if (Prefs.DevMode) ContinuumCampaign.Current.Resolve(false); }
    }
}
