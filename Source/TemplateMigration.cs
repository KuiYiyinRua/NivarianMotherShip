using System;
using System.Linq;
using RimWorld;
using Verse;

namespace NivarianCommander
{
    // Game.FinalizeInit runs after map/reference loading, before LoadedGame
    // observers and the native pause-on-load tick. No duration/payment reset.
    public sealed class TemplateMigration : GameComponent
    {
        public TemplateMigration(Game game) { }
        public override void FinalizeInit() => Apply();
        public static void Apply()
        {
            var legions = CommanderLegions.Current;
            if (legions != null)
                foreach (var b in legions.batches.Where(b => b.ability == 14).OrderBy(b => b.id))
                    foreach (var p in b.members.Where(p => p != null).OrderBy(p => p.thingIDNumber))
                    {
                        var m = Hediff_LegionIdentity.For(p);
                        if (m == null || m.batchId != b.id || m.templateVersion != 0) continue;
                        m.templateMigration = Migrate(p, ref m.speedScale, m.hero ? 5f : 4.8f);
                        if (m.hero) p.Name = new NameSingle("Tebrus");
                        m.templateVersion = 1;
                        p.health.Notify_HediffChanged(m); StatDefOf.MoveSpeed.Worker.ClearCacheForThing(p);
                    }
            var ghosts = CommanderGhosts.Current;
            if (ghosts != null)
                foreach (var b in ghosts.batches.OrderBy(b => b.id))
                    foreach (var p in b.members.Where(p => p != null).OrderBy(p => p.thingIDNumber))
                    {
                        var m = Hediff_GhostIdentity.For(p);
                        if (m == null || m.batchId != b.id || m.templateVersion != 0) continue;
                        m.templateMigration = Migrate(p, ref m.speedScale, 5.2f);
                        m.templateVersion = 1;
                        p.health.Notify_HediffChanged(m); StatDefOf.MoveSpeed.Worker.ClearCacheForThing(p);
                    }
        }
        static string Migrate(Pawn p, ref float scale, float target)
        {
            // Fingerprint only the verified native 4.6-base templates. The
            // stored multiplier reconstructs BIRTH moving capacity, independent
            // of current wounds, temperature, equipment or later scale growth.
            // Unknown histories retain their multiplier, rather than healing
            // a pawn indirectly by recalibrating against its current speed.
            float baseSpeed = p.def.statBases.FirstOrDefault(s => s.stat == StatDefOf.MoveSpeed)?.value ?? 0f;
            var stable = DefDatabase<HediffDef>.GetNamed("Nivarian_IcyCore").stages[0];
            float offset = stable.capMods.FirstOrDefault(c => c.capacity == PawnCapacityDefOf.Moving)?.offset ?? 0f;
            string result = "legacy-unrecognized-speed-preserved";
            if (Math.Abs(baseSpeed - 4.6f) < .00001f && Math.Abs(offset - .15f) < .00001f && scale > 0f)
            {
                float birthMoving = target / (baseSpeed * scale);
                if (new[] { .95f, .97f, .98f, 1f }.Any(x => Math.Abs(x - birthMoving) < .00002f))
                { scale *= birthMoving / (birthMoving + offset); result = "legacy-before-native-icy"; }
                else if (new[] { 1.10f, 1.12f, 1.13f, 1.15f }.Any(x => Math.Abs(x - birthMoving) < .00002f))
                    result = "legacy-after-native-icy-preserved";
            }
            if (!p.Dead && p.Spawned) NativePawnPhysiology.InitializeSpawned(p);
            NativePawnPhysiology.RepairLoadedTemplate(p);
            return result;
        }
    }
}
