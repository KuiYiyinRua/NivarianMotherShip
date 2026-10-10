using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace NivarianCommander
{
    public sealed class OrbitalPulse : IExposable
    {
        public IntVec3 center;
        public int remaining = 36;
        public OrbitalPulse() { }
        public OrbitalPulse(IntVec3 c) { center = c; }
        public void ExposeData() { Scribe_Values.Look(ref center, "center"); Scribe_Values.Look(ref remaining, "remaining", 36); }
    }
    // Saved, map-clock-driven presentation. Rendering never consumes simulation randomness.
    public sealed class OrbitalVisuals : MapComponent
    {
        List<OrbitalPulse> pulses = new List<OrbitalPulse>();
        static Material outer, core, ring;
        public OrbitalVisuals(Map map) : base(map) { }
        public int Count => pulses.Count;
        public void Add(IntVec3 c) => pulses.Add(new OrbitalPulse(c));
        public override void ExposeData()
        {
            Scribe_Collections.Look(ref pulses, "commanderOrbitalPulses", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit) pulses = pulses ?? new List<OrbitalPulse>();
        }
        public override void MapComponentTick()
        {
            for (int i = pulses.Count - 1; i >= 0; i--) if (--pulses[i].remaining <= 0) pulses.RemoveAt(i);
        }
        public override void MapComponentUpdate()
        {
            if (Find.CurrentMap != map || pulses.Count == 0) return;
            if (outer == null)
            {
                outer = SolidColorMaterials.SimpleSolidColorMaterial(new Color(.15f, .65f, 1f, .38f), true);
                core = SolidColorMaterials.SimpleSolidColorMaterial(new Color(.82f, .97f, 1f, .9f), true);
                ring = SolidColorMaterials.SimpleSolidColorMaterial(new Color(.3f, .85f, 1f, .75f), true);
            }
            foreach (var p in pulses)
            {
                float fade = Mathf.Clamp01(p.remaining / 12f);
                var at = p.center.ToVector3Shifted(); at.y = AltitudeLayer.MetaOverlays.AltitudeFor();
                var beam = at + new Vector3(0, 0, 13);
                Graphics.DrawMesh(MeshPool.plane10, Matrix4x4.TRS(beam, Quaternion.identity, new Vector3(.85f * fade, 1, 26)), outer, 0);
                beam.y += .01f;
                Graphics.DrawMesh(MeshPool.plane10, Matrix4x4.TRS(beam, Quaternion.identity, new Vector3(.18f * fade, 1, 26)), core, 0);
                GenDraw.DrawCircleOutline(at, 3f * (1f - p.remaining / 48f), ring);
            }
        }
    }
}
