using System.Collections.Generic;
using Verse;

namespace NivarianCommander
{
    public sealed class OrbitalPulse : IExposable
    {
        public IntVec3 center;
        public const int Lifetime = 240;
        public int remaining = Lifetime;
        public int duration = Lifetime;
        public OrbitalPulse() { }
        public OrbitalPulse(IntVec3 c) { center = c; }
        public void ExposeData()
        {
            Scribe_Values.Look(ref center, "center");
            // Old saves have only remaining and used a 36-tick pulse.
            Scribe_Values.Look(ref remaining, "remaining", 36);
            Scribe_Values.Look(ref duration, "duration", 36);
        }
    }
    // Saved, map-clock-driven presentation. Rendering never consumes simulation randomness.
    public sealed class OrbitalVisuals : MapComponent
    {
        List<OrbitalPulse> pulses = new List<OrbitalPulse>();
        OrbitalStrikeRenderer renderer;
        public OrbitalVisuals(Map map) : base(map) { }
        public int Count => pulses.Count;
        public void Add(IntVec3 c) => pulses.Add(new OrbitalPulse(c));
        internal void AddLegacySolar(IntVec3 c, int remaining)
        {
            if (remaining > 0) pulses.Add(new OrbitalPulse(c) { remaining = remaining, duration = 32 });
        }
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
            if (!OrbitalStrikeRenderer.CanView(map) || pulses.Count == 0)
            {
                renderer?.Dispose(); renderer = null; return;
            }
            if (renderer == null || renderer.Disposed) renderer = new OrbitalStrikeRenderer(map);
            renderer.Draw(pulses);
        }
        public override void MapRemoved() { renderer?.Dispose(); renderer = null; pulses.Clear(); }
    }
}
