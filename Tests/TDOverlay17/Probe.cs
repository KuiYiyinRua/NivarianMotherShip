using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Waylith.Rendering;

namespace NMC.TDOverlay17;

// Separate, opt-in test mod. No patches or changes to the production runtime.
public sealed class Probe : GameComponent
{
    static string Root => Path.GetDirectoryName(GenFilePaths.SaveDataFolderPath);
    int frames, failures, stage;
    bool started, done;
    Map map;
    IntVec3 center;
    Type overlayType, fertilityType, coverageType;
    HashSet<Type> toggles;
    object settings;
    Thing beacon;
    Mesh effectMesh;
    UnityEngine.Material effectMaterial;
    public Probe(Game game) { }
    static void Record(string text) => File.AppendAllText(Path.Combine(Root, "evidence.txt"), text + "\n");
    void Check(bool value, string message)
    { Record((value ? "PASS " : "FAIL ") + message); if (!value) failures++; }

    void Setup()
    {
        map = Find.CurrentMap; center = map.Center;
        Record("GAME " + VersionControl.CurrentVersionStringWithRev + " MODS " + string.Join(",", LoadedModManager.RunningMods.Select(m => m.PackageId)));
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies().Where(a => a.GetName().Name is "NivarianCommander" or "TD_Enhancement_Pack"))
            Record("ASSEMBLY " + asm.Location + " SHA256=" + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(asm.Location))));
        var path = ShaderSourceLoader.ResolveShaderPath("VertexColor.frag.glsl");
        Record("VANILLA_VERTEX_SOURCE " + path);
        Check(path.Replace('\\', '/').Contains("/Data/Core/Shaders/Map/VertexColor.frag.glsl"), "vanilla vertex shader resolves to Core");
        Check(ShaderSourceLoader.ReadShaderSource("VertexColor.frag.glsl").Contains("uMaterial._Color"), "vanilla fragment multiplies vertex color by material opacity");
        var solid = SolidColorMaterials.SimpleSolidColorMaterial(new UnityEngine.Color(.8f, .5f, .2f, .1f), true);
        Check(solid.HasProperty("_Color"), "CellBoolDrawer vertex material exposes _Color");
        Check(Math.Abs(solid.color.a - .1f) < .005f, "material retains requested alpha");
        foreach (var name in new[] { "NMC_VertexAlpha", "NMC_VertexAdditive", "NMC_BlackHoleLens" })
        {
            var def = DefDatabase<ShaderDef>.GetNamed(name);
            var source = ShaderSourceLoader.ResolveShaderPath(def.shaderProgram.fragmentShaderFileName);
            Record(name + " SOURCE " + source);
            Check(source.Replace('\\', '/').Contains("/Mods/NivarianMotherShip/1.7/Shaders/NMC/"), name + " resolves to own source");
            Check(ShaderRegistry.TryResolve(name, out _), name + " registered");
        }
        var td = AppDomain.CurrentDomain.GetAssemblies().Single(a => a.GetName().Name == "TD_Enhancement_Pack");
        overlayType = td.GetType("TD_Enhancement_Pack.BaseOverlay", true);
        fertilityType = td.GetType("TD_Enhancement_Pack.FertilityOverlay", true);
        coverageType = td.GetType("TD_Enhancement_Pack.Overlays.CoverageOverlay", true);
        toggles = (HashSet<Type>)AccessTools.Field(overlayType, "toggleShow").GetValue(null);
        settings = AccessTools.Field(td.GetType("TD_Enhancement_Pack.Mod", true), "settings").GetValue(null);
        toggles.Clear();
        Find.WindowStack.TryRemove(typeof(Dialog_MessageBox), false);
        foreach (var cell in CellRect.CenteredOn(center, 24))
        {
            foreach (var thing in cell.GetThingList(map).ToArray())
                if (thing is not Pawn && thing.def.destroyable) thing.Destroy(DestroyMode.Vanish);
            map.roofGrid.SetRoof(cell, null);
            map.fogGrid.Unfog(cell);
            map.terrainGrid.SetTerrain(cell, ((cell.x / 3 + cell.z / 3) & 1) == 0 ? TerrainDefOf.Gravel : TerrainDefOf.SoilRich);
        }
        beacon = ThingMaker.MakeThing(ThingDefOf.OrbitalTradeBeacon);
        beacon.SetFaction(Faction.OfPlayer);
        GenSpawn.Spawn(beacon, center, map);
        Find.Selector.ClearSelection();
        Find.CameraDriver.JumpToCurrentMapLoc(center.ToVector3Shifted());
        Find.CameraDriver.SetRootSize(15f);
        // Exercise the mod's unchanged vertex-alpha effect alongside the overlays.
        effectMaterial = new UnityEngine.Material(DefDatabase<ShaderDef>.GetNamed("NMC_VertexAlpha").Shader);
        effectMesh = new Mesh { name = "NMC alpha regression sample" };
        effectMesh.vertices = new[] { new Vector3(-3,0,-3), new Vector3(-3,0,3), new Vector3(3,0,3), new Vector3(3,0,-3) };
        effectMesh.triangles = new[] { 0,1,2,0,2,3 };
        effectMesh.colors = Enumerable.Repeat(new UnityEngine.Color(.2f,.8f,1f,.25f),4).ToArray();
        Record("SETUP_COMPLETE");
    }

    object Overlay(Type type) => overlayType.GetMethod("GetOverlay", new[] { typeof(Type) }).Invoke(null, new object[] { type });
    void SetOpacity(float opacity)
    {
        AccessTools.Field(settings.GetType(), "overlayOpacity").SetValue(settings, opacity);
        AccessTools.Method(overlayType, "ResetAll").Invoke(null, null);
    }
    void StartStage()
    {
        toggles.Clear(); Find.Selector.ClearSelection();
        if (stage is 1 or 2) { SetOpacity(stage == 1 ? 1f : .2f); toggles.Add(fertilityType); }
        if (stage == 3) { SetOpacity(1f); Find.Selector.Select(beacon); toggles.Add(coverageType); }
        Record("STAGE " + stage);
    }
    void InspectDrawer(Type type)
    {
        object overlay = Overlay(type);
        object drawer = AccessTools.Field(overlayType, "drawer").GetValue(overlay);
        Check(drawer != null, type.Name + " creates actual TD drawer");
        if (drawer == null) return;
        var material = (UnityEngine.Material)AccessTools.Field(typeof(CellBoolDrawer), "material").GetValue(drawer);
        Check(material != null && material.HasProperty("_Color"), type.Name + " honors material opacity");
        Record(type.Name + " MATERIAL " + material?.name + " ALPHA " + material?.color.a);
        var meshes = (List<Mesh>)AccessTools.Field(typeof(CellBoolDrawer), "meshes").GetValue(drawer);
        Check(meshes.Any(m => m.vertexCount > 0), type.Name + " submits nonempty geometry");
    }
    public override void GameComponentUpdate()
    {
        if (!GenCommandLine.CommandLineArgPassed("nmc-td-overlay17") || done || Current.ProgramState != ProgramState.Playing || LongEventHandler.AnyEventNowOrWaiting) return;
        try
        {
            Find.TickManager.Pause();
            if (!started) { started = true; Setup(); StartStage(); }
            if (stage == 4)
                UnityEngine.Graphics.DrawMesh(effectMesh, center.ToVector3ShiftedWithAltitude(AltitudeLayer.MetaOverlays), Quaternion.identity, effectMaterial, 0);
            if (++frames == 25)
            {
                if (stage is 1 or 2) InspectDrawer(fertilityType);
                if (stage == 3) InspectDrawer(coverageType);
                ScreenCapture.CaptureScreenshot(Path.Combine(Root, new[] { "00-terrain.png", "01-fertility.png", "02-fertility-low-opacity.png", "03-coverage.png", "04-nmc-effect.png" }[stage]));
            }
            if (frames < 40) return;
            if (++stage < 5) { frames = 0; StartStage(); return; }
            done = true;
            File.WriteAllText(Path.Combine(Root, "complete"), "failures=" + failures);
            Application.Quit();
        }
        catch (Exception e) { done = true; File.WriteAllText(Path.Combine(Root,"failed"), e.ToString()); Application.Quit(); }
    }
}
