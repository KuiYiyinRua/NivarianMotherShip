using System.Collections.Generic;
using Nivarian;
using Nivarian_Race.Code.Defs;
using RimWorld.Planet;
using Verse;

namespace NivarianCommander
{
    // WorldComponentTick is driven by the MP world context, independent of the viewed map.
    // Remaining durations avoid absolute timestamp conversion on async map save/load.
    public sealed class CommanderState : WorldComponent
    {
        public const int ChargeInterval = 180000;
        public int overloadCharges;
        public int rechargeRemaining = ChargeInterval;
        public bool friendlyFireProtection;
        public int shotsFired, requestsRejected, protectionPurchases;
        public int solarCastSerial;
        public int rayCastSerial;
        public int purificationCastSerial;
        public Dictionary<string, int> cooldowns = new Dictionary<string, int>();
        public CommanderState(World world) : base(world) { }
        public static CommanderState Current => Find.World?.GetComponent<CommanderState>();
        // Called only by the future synchronized action executor, never by local UI.
        public bool TryConsumeCharge()
        {
            if (overloadCharges <= 0) return false;
            if (overloadCharges == 3) rechargeRemaining = ChargeInterval;
            overloadCharges--;
            return true;
        }
        public override void WorldComponentTick()
        {
            if (overloadCharges < 3 && --rechargeRemaining <= 0)
            {
                overloadCharges++;
                rechargeRemaining = overloadCharges < 3 ? ChargeInterval : 0;
            }
            foreach (string key in new List<string>(cooldowns.Keys))
                if (cooldowns[key] > 0) cooldowns[key]--;
        }
        public int Remaining(CommanderAbilityDef def)
        {
            string key = def.number >= 14 && def.number <= 16 ? "LegionShared" : def.defName;
            return cooldowns.TryGetValue(key, out int value) ? value : 0;
        }
        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref overloadCharges, "nmcOverloadCharges", 0);
            Scribe_Values.Look(ref rechargeRemaining, "nmcRechargeRemaining", ChargeInterval);
            Scribe_Values.Look(ref friendlyFireProtection, "nmcFriendlyFireProtection", false);
            Scribe_Values.Look(ref shotsFired, "nmcShotsFired", 0);
            Scribe_Values.Look(ref requestsRejected, "nmcRequestsRejected", 0);
            Scribe_Values.Look(ref protectionPurchases, "nmcProtectionPurchases", 0);
            Scribe_Values.Look(ref solarCastSerial, "nmcSolarCastSerial", 0);
            Scribe_Values.Look(ref rayCastSerial, "nmcRayCastSerial", 0);
            Scribe_Values.Look(ref purificationCastSerial, "nmcPurificationCastSerial", 0);
            Scribe_Collections.Look(ref cooldowns, "nmcCooldowns", LookMode.Value, LookMode.Value);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                cooldowns = cooldowns ?? new Dictionary<string, int>();
                // Existing saves may retain the former half-day or full-day field cooldown.
                if (cooldowns.TryGetValue("NMC_S01", out int fieldCooldown))
                    cooldowns["NMC_S01"] = UnityEngine.Mathf.Clamp(fieldCooldown, 0, FieldEconomy.CooldownTicks);
                if (cooldowns.TryGetValue("NMC_S19", out int projectionCooldown))
                    cooldowns["NMC_S19"] = UnityEngine.Mathf.Clamp(projectionCooldown, 0, DefenseActions.Ability(19).cooldownTicks);
                overloadCharges = UnityEngine.Mathf.Clamp(overloadCharges, 0, 3);
                if (overloadCharges == 3) rechargeRemaining = 0;
                else if (rechargeRemaining <= 0 || rechargeRemaining > ChargeInterval) rechargeRemaining = ChargeInterval;
            }
        }
    }

    public static class NativeLedger
    {
        public static GameComp_NivarianMotherShip Component => Verse.Current.Game?.GetComponent<GameComp_NivarianMotherShip>();
        public static int Balance(string defName)
        {
            var def = DefDatabase<NivarianMotherShipRequestCurrencyDef>.GetNamedSilentFail(defName);
            var ledger = Component?.CurrencyHold;
            return def != null && ledger != null && ledger.TryGetValue(def, out int amount) ? amount : 0;
        }
        internal static bool TryPay(int power, int volatileReactive)
        {
            var p = DefDatabase<NivarianMotherShipRequestCurrencyDef>.GetNamedSilentFail("PowerCoin");
            var v = DefDatabase<NivarianMotherShipRequestCurrencyDef>.GetNamedSilentFail("VolatileReactive");
            var ledger = Component?.CurrencyHold;
            if (power < 0 || volatileReactive < 0 || p == null || v == null || ledger == null ||
                !ledger.TryGetValue(p, out int availableP) || !ledger.TryGetValue(v, out int availableV) ||
                availableP < power || availableV < volatileReactive) return false;
            ledger[p] = availableP - power;
            ledger[v] = availableV - volatileReactive;
            return true;
        }
    }
}
