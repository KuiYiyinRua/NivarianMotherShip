using System;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace NMC.RimWorld17Smoke
{
    public sealed class Probe : GameComponent
    {
        static bool Enabled => GenCommandLine.CommandLineArgPassed("nmc17-smoke");
        static bool Baseline => GenCommandLine.CommandLineArgPassed("nmc17-baseline");
        static string Root => Path.GetDirectoryName(GenFilePaths.SaveDataFolderPath);
        int start = -1, frames;
        bool done;
        Window window;
        static int panelDraws;
        static void CountPanelDraw() { panelDraws++; }
        public Probe(Game game) { }
        static void Record(string value) => File.AppendAllText(Path.Combine(Root, "evidence.txt"), value + "\n");
        static void Need(bool ok, string message)
        { Record((ok ? "PASS " : "FAIL ") + message); if (!ok) throw new InvalidOperationException(message); }

        public override void GameComponentUpdate()
        {
            if (!Enabled || done || Current.ProgramState != ProgramState.Playing || LongEventHandler.AnyEventNowOrWaiting) return;
            try
            {
                if (start < 0)
                {
                    start = Find.TickManager.TicksGame;
                    Record("GAME " + VersionControl.CurrentVersionStringWithRev + " mods=" + string.Join(",", LoadedModManager.RunningMods.Select(m => m.PackageId)));
                    Need(DefDatabase<ThingDef>.GetNamedSilentFail("Nivarian_DroneHub") != null, "native 1.7 definitions loaded");
                    Need(Find.CurrentMap != null && Find.CurrentMap.mapPawns.FreeColonistsSpawned.Count > 0, "real quicktest colony generated");
                    if (!Baseline)
                    {
                        var assembly = AppDomain.CurrentDomain.GetAssemblies().Single(a => a.GetName().Name == "NivarianCommander");
                        Record("CORE " + assembly.Location);
                        Need((bool)assembly.GetType("NivarianCommander.CommanderMod").GetProperty("Initialized").GetValue(null), "complete commander initialization");
                        Need(assembly.Location.EndsWith(".rimunity", StringComparison.OrdinalIgnoreCase), "core loaded through deferred script protocol");
                        var patches = Harmony.GetAllPatchedMethods().Count(m => Harmony.GetPatchInfo(m).Owners.Contains("local.nivarian.mothership.commander"));
                        Record("PATCHED_METHODS " + patches);
                        Need(patches > 100, "commander Harmony patches registered");
                        var defType = assembly.GetType("NivarianCommander.CommanderAbilityDef", true);
                        for (int i = 1; i <= 19; i++)
                            Need(GenDefDatabase.GetDef(defType, "NMC_S" + i.ToString("00"), false) != null, "support S" + i);
                        var nativeWindow = AccessTools.TypeByName("Nivarian_Race.Code.UI.Window_MothershipSupport");
                        new Harmony("nmc17.smoke.panel").Patch(AccessTools.Method(nativeWindow, "DoWindowContents"),
                            postfix: new HarmonyMethod(typeof(Probe), nameof(CountPanelDraw)));
                        window = (Window)Activator.CreateInstance(nativeWindow);
                        Find.WindowStack.Add(window);
                        assembly.GetType("NivarianCommander.NativeCommanderTab").GetMethod("SelectCommander").Invoke(null, new object[] { window });
                        Need(Find.WindowStack.Windows.Contains(window), "native mothership commander window opened");
                        Find.TickManager.Pause();
                    }
                }
                frames++;
                if (!Baseline && frames == 100)
                {
                    Need(panelDraws > 10, "commander panel really rendered");
                    ScreenCapture.CaptureScreenshot(Path.Combine(Root, "commander-panel.png"));
                }
                if (!Baseline && frames == 120) window.Close();
                if (Baseline || frames > 130) Find.TickManager.CurTimeSpeed = TimeSpeed.Superfast;
                if (Find.TickManager.TicksGame - start < 1200) return;
                Need(Find.CurrentMap != null, "1200 natural ticks completed");
                Record("COMPLETE ticks=" + (Find.TickManager.TicksGame - start));
                File.WriteAllText(Path.Combine(Root, "complete"), "PASS");
                done = true;
                Application.Quit();
            }
            catch (Exception error)
            {
                done = true;
                Record(error.ToString());
                File.WriteAllText(Path.Combine(Root, "failed"), error.ToString());
                Application.Quit();
            }
        }
    }
}
