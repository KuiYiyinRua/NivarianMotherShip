using System;
using System.Linq;
using Nivarian;
using RimWorld;
using Verse;

namespace NivarianCommander
{
    // Native needs can add the healthy Icy Core capacity stage on their first
    // interval, after PostSpawnSetup. Calibrate only after that native stage
    // exists; retain normal later temperature, injury and equipment changes.
    public static class NativePawnPhysiology
    {
        // Only used while creating our own unspawned combat templates.
        // Native RemoveTrait's HasTrait guard ignores suppressed traits.
        // Lift their suppression immediately before removal so the native
        // ability, gene, work, skill and meditation notifications still run.
        public static void ClearTemplateTraits(Pawn pawn)
        {
            if (pawn == null || pawn.Spawned || pawn.story?.traits == null)
                throw new InvalidOperationException("Trait cleanup requires an unspawned template pawn");
            RemoveTemplateTraits(pawn);
        }

        internal static void RepairLoadedTemplate(Pawn pawn)
        {
            if (Hediff_LegionIdentity.For(pawn) == null && Hediff_GhostIdentity.For(pawn) == null)
                throw new InvalidOperationException("Loaded template must have an owned identity");
            if (pawn.story != null) { EnsureStory(pawn); RemoveTemplateTraits(pawn); }
        }

        static void RemoveTemplateTraits(Pawn pawn)
        {
            var traits = pawn.story.traits;
            foreach (var trait in traits.allTraits.ToList())
            {
                if (!traits.allTraits.Contains(trait)) continue;
                if (trait.sourceGene != null && trait.sourceGene.Overridden)
                {
                    // Remove the actual source gene first, using its native
                    // notification path. Its suppressed trait may remain
                    // because that same native guard cannot see it.
                    pawn.genes.RemoveGene(trait.sourceGene);
                    if (!traits.allTraits.Contains(trait)) continue;
                    trait.sourceGene = null;
                }
                trait.suppressedByGene = null;
                trait.suppressedByTrait = false;
                traits.RemoveTrait(trait);
                if (traits.allTraits.Contains(trait))
                    throw new InvalidOperationException("Native template trait removal did not complete");
            }
        }

        static void EnsureStory(Pawn pawn)
        {
            if (pawn.story == null) return;
            if (pawn.story.Childhood == null) pawn.story.Childhood = DefDatabase<BackstoryDef>.GetNamed("NMC_FieldTrainingChildhood");
            if (pawn.story.Adulthood == null) pawn.story.Adulthood = DefDatabase<BackstoryDef>.GetNamed("NMC_FieldTrainingAdulthood");
        }

        public static void InitializeSpawned(Pawn pawn)
        {
            if (pawn == null || !pawn.Spawned)
                throw new InvalidOperationException("Native physiology initialization requires a spawned template member");
            // Royalty's native meditation explanation dereferences both
            // story slots. Template backgrounds grant no skills or traits.
            EnsureStory(pawn);
            var icyCore = pawn.needs?.TryGetNeed<Need_IcyCore>();
            if (icyCore != null && !pawn.health.hediffSet.HasHediff(DefDatabase<HediffDef>.GetNamed("Nivarian_IcyCore")))
                icyCore.NeedInterval();
        }
    }
}
