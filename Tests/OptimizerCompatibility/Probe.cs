using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using HarmonyLib;
using NivarianCommander;
using UnityEngine;
using Verse;

namespace NMC.OptimizerProbe
{
    public sealed class Probe : GameComponent
    {
        public Probe(Game game) { }
        int frames;
        bool checkedCache, done;
        float started;
        static string Root => Path.GetDirectoryName(GenFilePaths.SaveDataFolderPath);
        static void Need(bool ok, string message)
        {
            File.AppendAllText(Path.Combine(Root, "evidence.txt"), (ok ? "PASS " : "FAIL ") + message + "\n");
            if (!ok) throw new Exception(message);
        }
        public override void GameComponentUpdate()
        {
            if (done || !GenCommandLine.CommandLineArgPassed("nmc-optimizer-probe")
                || Current.ProgramState != ProgramState.Playing || LongEventHandler.AnyEventNowOrWaiting) return;
            try
            {
                if (started == 0) { started = Time.realtimeSinceStartup; Log.Message("NMC_OPTIMIZER START " + typeof(CommanderMod).Assembly.ManifestModule.ModuleVersionId); }
                // PO installs its transpilers over many rendered frames.
                if (Time.realtimeSinceStartup - started < 30) return;
                if (!checkedCache) { CheckCache(); checkedCache = true; }
                foreach (var map in Find.Maps) map.GetComponent<FieldWarpVisuals>().MapComponentUpdate();
                if (++frames < 600) return;
                Need(true, "600 live frames and native FieldWarpVisuals updates completed");
                done = true; File.WriteAllText(Path.Combine(Root, "complete"), "PASS"); Application.Quit();
            }
            catch (Exception e)
            {
                done = true; File.WriteAllText(Path.Combine(Root, "failed"), e.ToString()); Log.Error("NMC_OPTIMIZER FAILED " + e); Application.Quit();
            }
        }
        static void CheckCache()
        {
            var type = AccessTools.TypeByName("YaOpt.OtherMod.PerformanceOptimizer.ThreadSafeGetCompReplacements");
            if (GenCommandLine.CommandLineArgPassed("nmc-no-optimizers"))
            {
                Need(type == null, "optional optimizers absent; normal mother ship load works"); return;
            }
            Need(type != null, "real YaOpt/PerformanceOptimizer adapter loaded");
            var global = AccessTools.TypeByName("YaOpt.YaOptGlobal");
            var settings = AccessTools.Property(global, "Settings").GetValue(null);
            foreach (var name in new[] { "OptParallelPawnTick", "OptParallelWorkGiver", "OptParallelThoughtUpdate", "OptParallelPostMapTick" })
            {
                var option = AccessTools.Property(settings.GetType(), name).GetValue(settings);
                AccessTools.Property(option.GetType(), "Enabled").SetValue(option, false);
            }
            Need(!(bool)AccessTools.Property(global, "NeedThreadSafe").GetValue(null), "original YaOpt simulation-only safety predicate is false");
            var subMod = type.Assembly.GetType("YaOpt.OtherMod.PerformanceOptimizer.SubMod");
            var onPatch = AccessTools.Method(subMod, "OnPatch");
            var enabled = AccessTools.Field(type, "Enabled");
            for (int i = 0; i < 3; i++)
            {
                Need((bool)onPatch.Invoke(Activator.CreateInstance(subMod), new object[] { new Harmony("nmc.optimizer.probe") }), "adapter OnPatch result preserved " + i);
                Need((bool)enabled.GetValue(null), "cache lock remains enabled after option reapplication " + i);
            }
            var original = AccessTools.Method(typeof(FieldWarpVisuals), nameof(FieldWarpVisuals.MapComponentUpdate));
            Need(Harmony.GetPatchInfo(original)?.Transpilers.Any(p => p.owner == "PerformanceOptimizer.Main") == true,
                "PerformanceOptimizer component-query optimization remains on mother ship visual path");
            var method = AccessTools.Method(type, "GetMapComponentFastWrapper");
            var warp = (Func<Map, FieldWarp>)Delegate.CreateDelegate(typeof(Func<Map, FieldWarp>), method.MakeGenericMethod(typeof(FieldWarp)));
            var defense = (Func<Map, CommanderDefense>)Delegate.CreateDelegate(typeof(Func<Map, CommanderDefense>), method.MakeGenericMethod(typeof(CommanderDefense)));
            // Independent map identities cause concurrent first inserts, not just warm-cache reads.
            var maps = new Map[512]; var warps = new FieldWarp[maps.Length]; var defenses = new CommanderDefense[maps.Length];
            var components = AccessTools.Field(typeof(Map), "components");
            for (int i = 0; i < maps.Length; i++)
            {
                maps[i] = new Map(); warps[i] = new FieldWarp(maps[i]); defenses[i] = new CommanderDefense(maps[i]);
                components.SetValue(maps[i], new List<MapComponent> { warps[i], defenses[i] });
            }
            Exception failure = null;
            using (var gate = new ManualResetEvent(false))
            {
                var threads = new Thread[8];
                for (int i = 0; i < threads.Length; i++)
                {
                    int worker = i;
                    threads[i] = new Thread(() =>
                    {
                        try
                        {
                            gate.WaitOne();
                            for (int n = 0; n < 65536; n++)
                            {
                                int j = (n + worker * 67) % maps.Length;
                                if (!ReferenceEquals(warp(maps[j]), warps[j]) || !ReferenceEquals(defense(maps[j]), defenses[j]))
                                    throw new Exception("Wrong cached component identity");
                            }
                        }
                        catch (Exception e) { Interlocked.CompareExchange(ref failure, e, null); }
                    }) { IsBackground = true };
                    threads[i].Start();
                }
                gate.Set();
                foreach (var thread in threads) Need(thread.Join(30000), "cache stress worker completed");
            }
            if (failure != null) throw failure;
            Need(true, "1048576 actual optimized lookups, 8 threads, 512 maps, two mother ship component types; all identities match");
        }
    }
}
