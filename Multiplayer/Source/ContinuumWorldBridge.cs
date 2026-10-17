using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace NivarianCommander.MultiplayerAdapter
{
    // Local observation only. The resulting simulation mutation always enters
    // the common world command stream, never a client's arbitrary current map.
    public sealed class ContinuumWorldBridge : GameComponent
    {
        static ISyncMethod report;
        int observedSite = -1, observedStage, observedResult;
        public ContinuumWorldBridge(Game game) { }
        internal static void Install()
        {
            report = MP.RegisterSyncMethod(typeof(ContinuumCampaign), nameof(ContinuumCampaign.ApplyBattlefieldResult));
            new Harmony("local.nivarian.commander.continuum-world").Patch(
                AccessTools.Method(typeof(ContinuumCampaign), nameof(ContinuumCampaign.ReadBattlefieldStatus)),
                prefix: new HarmonyMethod(typeof(ContinuumWorldBridge), nameof(ReadStatus)));
        }
        static bool ReadStatus(ref int __result)
        {
            if (!MP.IsInMultiplayer) return true;
            __result = 0; return false;
        }
        public override void GameComponentUpdate()
        {
            if (!MP.IsInMultiplayer || !MP.IsHosting || !MP.InInterface || report == null) return;
            var campaign = ContinuumCampaign.Current;
            var site = campaign?.activeSite;
            var battle = site?.Map?.GetComponent<ContinuumBattlefield>();
            if (battle == null || campaign.activeStage <= 0) return;
            int result = battle.cleared ? 2 : battle.hadPlayer ? 1 : 0;
            if (result == 0 || observedSite == site.ID && observedStage == campaign.activeStage && observedResult == result) return;
            if (report.DoSync(null, site.ID, campaign.activeStage, result))
            { observedSite = site.ID; observedStage = campaign.activeStage; observedResult = result; }
        }
    }
}
