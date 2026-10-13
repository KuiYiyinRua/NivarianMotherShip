using System;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace NivarianCommander
{
    // Pawn render callbacks query our map components too. YaOpt 1.1.4's
    // NeedThreadSafe only considers parallel simulation, not parallel drawing.
    // Keep its own synchronized cache implementation; do not disable caching,
    // replace components, or introduce a second lock for the same dictionary.
    internal static class CommanderOptimizerCompatibility
    {
        const string Owner = "local.nivarian.mothership.optimizercompat";
        static FieldInfo enabled;

        internal static void Install()
        {
            var replacements = AccessTools.TypeByName(
                "YaOpt.OtherMod.PerformanceOptimizer.ThreadSafeGetCompReplacements");
            if (replacements == null) return; // Both optimizers are optional.
            var subMod = replacements.Assembly.GetType("YaOpt.OtherMod.PerformanceOptimizer.SubMod");
            var onPatch = AccessTools.DeclaredMethod(subMod, "OnPatch", new[] { typeof(Harmony) });
            var field = AccessTools.DeclaredField(replacements, "Enabled");
            if (onPatch == null || onPatch.ReturnType != typeof(bool) || field == null
                || !field.IsStatic || field.IsInitOnly || field.FieldType != typeof(bool))
            {
                Log.Error("[NMC] Optimizer component-cache bridge could not resolve the YaOpt adapter; review optimizer version.");
                return;
            }
            try
            {
                enabled = field;
                // OnPatch runs at startup and again when optimizer options change.
                // Install before late patching and also cover an already initialized adapter.
                new Harmony(Owner).Patch(onPatch, postfix: new HarmonyMethod(
                    typeof(CommanderOptimizerCompatibility), nameof(KeepThreadSafe)) { priority = Priority.Last });
                KeepThreadSafe();
                Log.Message("[NMC] Optimizer component-cache bridge active: YaOpt/PerformanceOptimizer synchronized caches retained for parallel rendering.");
            }
            catch (Exception e)
            {
                Log.Error("[NMC] Optimizer component-cache bridge installation failed: " + e);
            }
        }

        static void KeepThreadSafe() => enabled.SetValue(null, true);
    }
}
