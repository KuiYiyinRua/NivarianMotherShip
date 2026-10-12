using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace NivarianCommander
{
    public static class CommanderResearch
    {
        public static ResearchProjectDef For(CommanderAbilityDef d)
        {
            return d == null ? null : DefDatabase<ResearchProjectDef>.GetNamedSilentFail("NMC_Support" + d.number.ToString("00"));
        }
        public static string Unavailable(CommanderAbilityDef def)
        {
            var project = For(def);
            if (project?.IsFinished != true) return CommanderProgression.ResearchBlocked(project) ?? NmcText.Get("NMC_Text0045", def?.researchLabel);
            int required = project.GetModExtension<CommanderResearchExtension>()?.mothershipLevel ?? 0;
            return CommanderResearchAccess.Current?.enabled != true && (NativeLedger.Component?.GetLevel ?? 0) < required ? NmcText.Get("NMC_Text0046", required) : null;
        }

        public static bool Finished(string name) => DefDatabase<ResearchProjectDef>.GetNamedSilentFail(name)?.IsFinished == true;
        public const string FriendlyIdentification = "NMC_PreciseFriendlyIdentification";
        public static bool FriendlyFireProtectionActive => CommanderState.Current?.friendlyFireProtection == true
            || Finished(FriendlyIdentification);
    }
    public static class CommanderActions
    {
        static readonly bool Diagnostics = GenCommandLine.CommandLineArgPassed("nmc-node2");
        static CommanderAbilityDef OrbitalAbility => DefDatabase<CommanderAbilityDef>.GetNamed("NMC_S02");
        public static int OrbitalPowerCost => OrbitalAbility.powerCost;
        public static int OrbitalVolatileCost => OrbitalAbility.volatileCost;
        public static string FireUnavailable(Map map)
        {
            if (map == null || !Find.Maps.Contains(map)) return NmcText.Get("NMC_Text0034");
            if (CommanderResearch.Unavailable(OrbitalAbility) is string researchBlock) return researchBlock;
            if (map.generatorDef?.isUnderground == true) return NmcText.Get("NMC_Text0047");
            if (NativeLedger.Balance("PowerCoin") < OrbitalPowerCost || NativeLedger.Balance("VolatileReactive") < OrbitalVolatileCost) return NmcText.Get("NMC_Text0048", OrbitalPowerCost, OrbitalVolatileCost);
            return null;
        }
        public static bool ValidCenter(Map map, IntVec3 cell)
            => map != null && cell.InBounds(map) && cell.GetRoof(map)?.isThickRoof != true;
        public static IntVec3 ResolveCenter(Map map, IntVec3 cell)
        {
            if (cell.InBounds(map)) return ValidCenter(map, cell) ? cell : IntVec3.Invalid;
            // Only out-of-bounds requests are reselected; thick-roof clicks reject without payment.
            int x = UnityEngine.Mathf.Clamp(cell.x, 0, map.Size.x - 1), z = UnityEngine.Mathf.Clamp(cell.z, 0, map.Size.z - 1);
            IntVec3 near = new IntVec3(x, 0, z);
            foreach (IntVec3 candidate in GenRadial.RadialCellsAround(near, 12, true))
                if (ValidCenter(map, candidate)) return candidate;
            // Bounded deterministic full scan provides a legal fallback without random retries.
            for (int zi = 0; zi < map.Size.z; zi++) for (int xi = 0; xi < map.Size.x; xi++)
            { var c = new IntVec3(xi, 0, zi); if (ValidCenter(map, c)) return c; }
            return IntVec3.Invalid;
        }
        static void Reject(string reason)
        {
            if (CommanderState.Current != null) CommanderState.Current.requestsRejected++;
            Messages.Message(reason, MessageTypeDefOf.RejectInput, false);
            if (Diagnostics) Log.Message("[NMC-ACTION] REJECT " + reason);
        }
        // Registered by the optional MP adapter; UI invokes this same executor in single-player.
        public static void Fire(Map map, IntVec3 cell)
        {
            string reason = FireUnavailable(map);
            if (reason != null) { Reject(reason); return; }
            IntVec3 center = ResolveCenter(map, cell);
            if (!center.IsValid) { Reject(NmcText.Get("NMC_Text0049")); return; }
            if (!NativeLedger.TryPay(OrbitalPowerCost, OrbitalVolatileCost)) { Reject(NmcText.Get("NMC_Text0038")); return; }
            CommanderState.Current.shotsFired++;
            var damage = DefDatabase<DamageDef>.GetNamed("NMC_OrbitalEnergy");
            OrbitalStrike.Execute(map, center, damage, .35f);
            if (Diagnostics) Log.Message("[NMC-ACTION] FIRE map=" + map.uniqueID + " cell=" + center + " shot=" + CommanderState.Current.shotsFired + " P=" + NativeLedger.Balance("PowerCoin") + " V=" + NativeLedger.Balance("VolatileReactive"));
        }
        public static string ProtectionUnavailable()
        {
            return CommanderResearch.FriendlyFireProtectionActive ? NmcText.Get("NMC_Text0050")
                : NmcText.Get("NMC_Text0711");
        }
        // Retained for callers of the former purchase API; research is now the only unlock path.
        public static void PurchaseProtection()
        {
            Messages.Message(CommanderResearch.FriendlyFireProtectionActive ? NmcText.Get("NMC_Text0050")
                : NmcText.Get("NMC_Text0712"), MessageTypeDefOf.RejectInput, false);
        }
    }
    public class SupportDamageDef : DamageDef { }
    public static class SupportDiplomacy
    {
        public static bool Friendly(Thing thing)
        {
            if (thing is Pawn pawn && (pawn.IsPrisoner || pawn.HostileTo(Faction.OfPlayer))) return false;
            if (!(thing is Pawn) && !(thing is Building)) return false;
            Faction faction = thing.Faction;
            return faction != null && (faction == Faction.OfPlayer || faction.RelationKindWith(Faction.OfPlayer) == FactionRelationKind.Ally);
        }
    }
    public sealed class DamageWorker_OrbitalEnergy : DamageWorker_AddInjury
    {
        public override void ExplosionAffectCell(Explosion explosion, IntVec3 c, List<Thing> damagedThings, List<Thing> ignoredThings, bool canThrowMotes)
        {
            if (!c.InBounds(explosion.Map) || !OrbitalStrike.Contains(explosion.Position, c)
                || c.GetRoof(explosion.Map)?.isThickRoof == true) return;
            int original = explosion.damAmount;
            try
            {
                // Apply the new base cap to unfinished explosions restored from older saves too.
                explosion.damAmount = Math.Min(original, def.defaultDamage);
                base.ExplosionAffectCell(explosion, c, damagedThings, ignoredThings, canThrowMotes);
            }
            finally { explosion.damAmount = original; }
        }
    }
    [HarmonyPatch(typeof(Thing), nameof(Thing.TakeDamage))]
    public static class FriendlyFireGuard
    {
        static bool Prefix(Thing __instance, DamageInfo dinfo, ref DamageWorker.DamageResult __result)
        {
            if (!(dinfo.Def is SupportDamageDef) || !CommanderResearch.FriendlyFireProtectionActive || !SupportDiplomacy.Friendly(__instance)) return true;
            if (dinfo.Instigator?.HostileTo(Faction.OfPlayer) == true) return true;
            __result = new DamageWorker.DamageResult();
            return false;
        }
    }
}
