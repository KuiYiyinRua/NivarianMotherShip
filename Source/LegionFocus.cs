using System;
using RimWorld;
using Verse;
namespace NivarianCommander
{
    public enum LegionFocusBackend { Unselected, NativePsyfocus, Fallback }
    public static class LegionFocus
    {
        public static bool Caster(Hediff_LegionIdentity marker) => marker != null && (marker.legionAbility == 16 || marker.legionAbility == 14 && marker.hero);
        public static string AbilityName(Hediff_LegionIdentity marker) => (marker.legionAbility == 16 ? "NMC_SeydasControl" : "NMC_TebrusStorm") + (marker.focusBackend == LegionFocusBackend.NativePsyfocus ? "Native" : "Fallback");
        public static void Initialize(Pawn pawn, Hediff_LegionIdentity marker)
        {
            if (!Caster(marker) || marker.focusBackend != LegionFocusBackend.Unselected) return;
            var native = DefDatabase<AbilityDef>.GetNamedSilentFail(marker.legionAbility == 16 ? "NMC_SeydasControlNative" : "NMC_TebrusStormNative");
            bool usable = ModsConfig.RoyaltyActive && native != null && pawn.psychicEntropy != null && pawn.abilities != null && pawn.health.hediffSet.GetBrain() != null;
            marker.focusBackend = usable ? LegionFocusBackend.NativePsyfocus : LegionFocusBackend.Fallback;
            if (usable)
            {
                // Give the dedicated level-one ability before linking; native
                // linking then has no reason to choose a random level-one gift.
                pawn.abilities.GainAbility(native);
                if (pawn.GetPsylinkLevel() < 1) pawn.ChangePsylinkLevel(1, false);
                if (pawn.GetPsylinkLevel() < 1) throw new InvalidOperationException("Native legion psylink initialization failed");
                pawn.psychicEntropy.OffsetPsyfocusDirectly(1f - pawn.psychicEntropy.CurrentPsyfocus);
            }
            else
            {
                marker.fallbackFocus = 100f;
                if (pawn.abilities == null) pawn.abilities = new Pawn_AbilityTracker(pawn);
                pawn.abilities?.GainAbility(DefDatabase<AbilityDef>.GetNamed(AbilityName(marker)));
            }
        }
        public static float Amount(Pawn pawn)
        {
            var marker = Hediff_LegionIdentity.For(pawn);
            if (marker == null) return 0f;
            return marker.focusBackend == LegionFocusBackend.NativePsyfocus
                ? (pawn.psychicEntropy?.CurrentPsyfocus ?? 0f) * 100f
                : marker.focusBackend == LegionFocusBackend.Fallback ? marker.fallbackFocus : 0f;
        }
        public static void Tick(Pawn pawn, Hediff_LegionIdentity marker)
        {
            if (!Caster(marker) || pawn.Dead || pawn.Destroyed || !pawn.Spawned) return;
            Initialize(pawn, marker);
            if (++marker.focusRegenTicks < 60) return;
            marker.focusRegenTicks = 0;
            if (marker.focusBackend == LegionFocusBackend.NativePsyfocus)
                pawn.psychicEntropy?.OffsetPsyfocusDirectly(.01f);
            else if (marker.focusBackend == LegionFocusBackend.Fallback)
                marker.fallbackFocus = Math.Min(100f, marker.fallbackFocus + 1f);
        }
    }
}
