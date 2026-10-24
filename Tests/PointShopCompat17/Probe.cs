using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace NMC.PointShopCompat17
{
    public sealed class Probe : GameComponent
    {
        static string Root => Path.GetDirectoryName(GenFilePaths.SaveDataFolderPath);
        static int scope, draws, customDraws, plainDraws, injectedIcons;
        static bool inject;
        static Material fixtureMaterial;
        static readonly System.Collections.Generic.HashSet<string> shaders = new System.Collections.Generic.HashSet<string>();
        int frames, cycle, failures;
        bool done;
        Window window;
        Type shopType, workerType;
        public Probe(Game game) { }
        static void Record(string value) => File.AppendAllText(Path.Combine(Root, "evidence.txt"), value + "\n");
        void Check(bool ok, string message) { Record((ok ? "PASS " : "FAIL ") + message); if (!ok) failures++; }
        static void EnterDraw() { scope++; if (Event.current.type == EventType.Repaint) draws++; }
        static void LeaveDraw(Exception __exception)
        { scope--; if (__exception != null) File.WriteAllText(Path.Combine(Root, "failed"), __exception.ToString()); }
        static bool MaterialBoundary(object[] __args)
        {
            if (scope == 0 || Event.current.type != EventType.Repaint) return true;
            Material mat = __args.OfType<Material>().FirstOrDefault();
            if (mat == null) { plainDraws++; return true; }
            customDraws++;
            if (shaders.Add(mat.shader.name)) Record("REJECT custom UI material shader=" + mat.shader.name);
            // Test-only boundary guard: count the unsafe submission but do not let
            // an intentionally injected material reach the graphics driver.
            return false;
        }
        static void InjectIcon(ref Material material)
        {
            if (scope > 0 && inject) { material = fixtureMaterial; injectedIcons++; }
        }
        void Activate() => workerType.GetMethod("Activate").Invoke(Activator.CreateInstance(workerType), null);
        public override void GameComponentUpdate()
        {
            if (!GenCommandLine.CommandLineArgPassed("nmc17-pointshop-compat") || done || Current.ProgramState != ProgramState.Playing || LongEventHandler.AnyEventNowOrWaiting) return;
            try
            {
                if (shopType == null)
                {
                    Record("GAME " + VersionControl.CurrentVersionStringWithRev + " mods=" + string.Join(",", LoadedModManager.RunningMods.Select(m => m.PackageId)));
                    var assembly = AppDomain.CurrentDomain.GetAssemblies().Single(a => a.GetName().Name == "NivarianCommander");
                    Record("CORE " + assembly.Location + " SHA256=" + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(assembly.Location))));
                    shopType = assembly.GetType("NivarianCommander.Window_MothershipPointShop", true);
                    workerType = assembly.GetType("NivarianCommander.MainButtonWorker_PointShop", true);
                    var harmony = new Harmony("nmc17.pointshopcompat.probe");
                    harmony.Patch(AccessTools.Method(shopType, "DoWindowContents"), prefix: new HarmonyMethod(typeof(Probe), nameof(EnterDraw)), finalizer: new HarmonyMethod(typeof(Probe), nameof(LeaveDraw)));
                    foreach (var m in typeof(Graphics).GetMethods().Where(m => m.Name == "DrawTexture" && m.GetParameters().Any(p => p.ParameterType == typeof(Material))))
                        harmony.Patch(m, prefix: new HarmonyMethod(typeof(Probe), nameof(MaterialBoundary)));
                    foreach (var m in typeof(GenUI).GetMethods().Where(m => m.Name == "DrawTextureWithMaterial"))
                        harmony.Patch(m, prefix: new HarmonyMethod(typeof(Probe), nameof(MaterialBoundary)));
                    var iconMethod = typeof(Widgets).GetMethods().Single(m => m.Name == "GetIconFor" && m.GetParameters()[0].ParameterType == typeof(Thing));
                    harmony.Patch(iconMethod, postfix: new HarmonyMethod(typeof(Probe), nameof(InjectIcon)));
                    fixtureMaterial = new Material(ShaderDatabase.Cutout) { mainTexture = Texture2D.whiteTexture };
                    var stateType = assembly.GetType("NivarianCommander.PointShopState", true);
                    var state = stateType.GetProperty("Current").GetValue(null);
                    stateType.GetMethod("WorldComponentTick").Invoke(state, null);
                    Check((bool)stateType.GetProperty("CatalogReady").GetValue(state), "stock generated through real world component");
                    Find.TickManager.Pause();
                }
                if (frames == 0)
                {
                    draws = customDraws = plainDraws = injectedIcons = 0;
                    inject = cycle == 1;
                    Activate();
                    window = Find.WindowStack.Windows.Single(w => w.GetType() == shopType);
                    Check(window != null, "main button opens shop cycle=" + cycle);
                    var nira = AccessTools.Field(shopType, "nira").GetValue(window);
                    var projection = AccessTools.Field(nira.GetType(), "projection")?.GetValue(nira);
                    Check(projection == null, "1.7 portrait does not construct projection material cycle=" + cycle);
                    var dialog = AccessTools.Field(nira.GetType(), "dialog").GetValue(nira);
                    var texture = (Texture)dialog.GetType().GetProperty("PortraitTexture").GetValue(dialog);
                    Check(texture != null && texture.width > 1 && texture.height > 1, "native portrait texture available cycle=" + cycle);
                    Record("PORTRAIT " + texture.width + "x" + texture.height);
                }
                frames++;
                if (frames == 50)
                {
                    Check(draws >= 10, "actual shop repaints cycle=" + cycle);
                    Check(plainDraws > 10, "ordinary textured UI draws reached cycle=" + cycle);
                    ScreenCapture.CaptureScreenshot(Path.Combine(Root, "shop-" + cycle + ".png"));
                }
                if (frames == 65) AccessTools.Method(shopType, "Switch").Invoke(window, new object[] { true });
                if (frames == 90) AccessTools.Method(shopType, "Switch").Invoke(window, new object[] { false });
                if (frames == 110) AccessTools.Field(shopType, "itemScroll").SetValue(window, new Vector2(0, 800));
                if (frames < 140) return;
                Check(customDraws == 0, "no custom material submitted by portrait or goods cycle=" + cycle + " observed=" + customDraws);
                if (inject) Check(injectedIcons > 10, "synthetic material-bearing merchandise exercised");
                Check(!File.Exists(Path.Combine(Root, "failed")), "no rendering exception cycle=" + cycle);
                Activate();
                Check(!Find.WindowStack.Windows.Contains(window) && AccessTools.Field(shopType, "nira").GetValue(window) == null, "close releases portrait cycle=" + cycle);
                Check(scope == 0, "draw scope restored cycle=" + cycle);
                frames = 0;
                if (++cycle < 3) return;
                UnityEngine.Object.Destroy(fixtureMaterial);
                Record("RESULT failures=" + failures);
                File.WriteAllText(Path.Combine(Root, failures == 0 ? "complete" : "failed"), "assertion failures=" + failures);
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
