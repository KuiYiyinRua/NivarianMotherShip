using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace NivarianCommander
{
    public sealed class GravityLift : IExposable
    {
        public Pawn target;
        public Building_NivarianAircraft source;
        public int remaining;
        public void ExposeData()
        {
            Scribe_References.Look(ref target, "target"); Scribe_References.Look(ref source, "source");
            Scribe_Values.Look(ref remaining, "remaining");
        }
    }
    public sealed class AircraftGravity : MapComponent
    {
        public List<GravityLift> lifts = new List<GravityLift>();
        static Material blue, violet;
        public AircraftGravity(Map map) : base(map) { }
        public bool Contains(Pawn pawn) => lifts.Exists(l => l.target == pawn && l.remaining > 0);
        public void Lift(Building_NivarianAircraft ship, Pawn pawn)
        {
            if (Contains(pawn)) return;
            lifts.Add(new GravityLift { source = ship, target = pawn, remaining = 300 });
            pawn.stances?.stunner?.StunFor(300, ship, addBattleLog: false);
        }
        public float Height(Pawn pawn)
        {
            var lift = lifts.Find(l => l.target == pawn);
            if (lift == null) return 0f;
            return Mathf.Min(Mathf.Clamp01((300 - lift.remaining) / 20f), Mathf.Clamp01(lift.remaining / 20f)) * 1.3f;
        }
        public override void MapComponentTick()
        {
            for (int i = lifts.Count - 1; i >= 0; i--)
            {
                var l = lifts[i];
                if (--l.remaining <= 0 || l.target == null || !l.target.Spawned || l.target.Destroyed || l.target.Dead
                    || l.target.Map != map) lifts.RemoveAt(i);
            }
        }
        public override void MapComponentDraw()
        {
            if (map != Find.CurrentMap) return;
            if (blue == null)
            {
                blue = SolidColorMaterials.SimpleSolidColorMaterial(new Color(.25f, .8f, 1f, .58f), true);
                violet = SolidColorMaterials.SimpleSolidColorMaterial(new Color(.7f, .5f, 1f, .4f), true);
            }
            foreach (var l in lifts)
            {
                if (l.target == null || !l.target.Spawned || l.target.Position.Fogged(map)) continue;
                var at = l.target.DrawPos; at.y = AltitudeLayer.MetaOverlays.AltitudeFor();
                float phase = (300 - l.remaining) * .03f;
                // Three tilted elliptical bands form a light, faceted gravity cage.
                for (int ring = 0; ring < 3; ring++)
                    for (int edge = 0; edge < 18; edge++)
                    {
                        float a = phase + edge * Mathf.PI / 9f, b = a + Mathf.PI / 9f;
                        GenDraw.DrawLineBetween(Cage(at, a, ring), Cage(at, b, ring), ring == 1 ? violet : blue, .025f);
                    }
                if (l.source != null && l.source.Spawned && l.source.Map == map)
                    GenDraw.DrawLineBetween(l.source.DrawPos, at, violet, .06f);
            }
        }
        static Vector3 Cage(Vector3 at, float a, int ring)
            => at + new Vector3(Mathf.Cos(a) * (.82f - ring * .12f), ring * .001f,
                Mathf.Sin(a) * .75f + Mathf.Cos(a) * (ring - 1) * .32f);
        public override void ExposeData()
        {
            base.ExposeData(); Scribe_Collections.Look(ref lifts, "nmcGravityLifts", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit) lifts = lifts ?? new List<GravityLift>();
        }
    }
    [HarmonyPatch(typeof(Pawn), "get_DrawPos")]
    internal static class AircraftLiftDrawing
    {
        static void Postfix(Pawn __instance, ref Vector3 __result)
        {
            if (!__instance.Spawned) return;
            float height = __instance.Map.GetComponent<AircraftGravity>().Height(__instance);
            if (height <= 0) return;
            __result.z += height; __result.y = AltitudeLayer.Skyfaller.AltitudeFor();
        }
    }
}
