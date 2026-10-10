using System;
using System.Collections.Generic;
using System.Linq;
using LumiParticle.Pooling;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace NivarianCommander
{
    public sealed class AircraftPrismVisuals : MapComponent
    {
        const string Prefab = "line_psyBeam";
        sealed class Line
        {
            public GameObject obj; public LineRenderer renderer;
            public Color start, end;
        }
        readonly Dictionary<Building_NivarianAircraft, List<Line>> beams = new Dictionary<Building_NivarianAircraft, List<Line>>();
        AircraftPrismCleanup cleanup;
        bool failed;
        public AircraftPrismVisuals(Map map) : base(map) { }
        internal bool Viewed => Current.ProgramState == ProgramState.Playing && Find.CurrentMap == map
            && WorldRendererUtility.DrawingMap && !LongEventHandler.ShouldWaitForEvent;
        bool Visible(Building_NivarianAircraft ship) => ship != null && ship.Spawned && ship.Map == map
            && ship.beamTicks > 0 && ship.Airborne && !ship.Position.Fogged(map);
        public void Show(Building_NivarianAircraft ship)
        {
            if (failed || !Viewed || !Visible(ship)) { Forget(ship); return; }
            var unityState = UnityEngine.Random.state; Rand.PushState();
            try
            {
                var camera = Find.Camera;
                if (camera != null)
                {
                    cleanup = camera.GetComponent<AircraftPrismCleanup>() ?? camera.gameObject.AddComponent<AircraftPrismCleanup>();
                    cleanup.Bind(this);
                }
                if (!beams.TryGetValue(ship, out var lines))
                {
                    lines = new List<Line>(); beams.Add(ship, lines);
                    for (int i = 0; i < 2; i++)
                    {
                        var line = new Line { obj = LumiPrefabPool.Get(Prefab) }; lines.Add(line);
                        if (line.obj == null) throw new InvalidOperationException("Missing native prism prefab");
                        line.renderer = line.obj.GetComponent<LineRenderer>();
                        if (line.renderer == null) throw new InvalidOperationException("Missing native prism line");
                        line.start = line.renderer.startColor; line.end = line.renderer.endColor;
                        line.renderer.positionCount = 2; line.renderer.enabled = true; line.obj.SetActive(true);
                    }
                }
                Update(ship, lines);
            }
            catch (Exception error) { failed = true; Clear(); Log.ErrorOnce("[NMC] Aircraft prism visual: " + error, 9411001); }
            finally { UnityEngine.Random.state = unityState; Rand.PopState(); }
        }
        void Update(Building_NivarianAircraft ship, List<Line> lines)
        {
            Vector3 end = ship.beamTarget != null && ship.beamTarget.Spawned && ship.beamTarget.Map == map
                ? ship.beamTarget.DrawPos : ship.beamEnd;
            if (end.ToIntVec3().InBounds(map) && end.ToIntVec3().Fogged(map))
            { foreach (var line in lines) line.renderer.enabled = false; return; }
            Vector3 direction = (end - ship.DrawPos).normalized;
            Vector3 side = new Vector3(direction.z, 0, -direction.x) * .45f;
            float alpha = Mathf.Min((25 - ship.beamTicks) / 3f, ship.beamTicks / 8f);
            for (int i = 0; i < lines.Count; i++)
            {
                var r = lines[i].renderer; r.enabled = true;
                r.startColor = new Color(.2f, .75f, 1f, alpha); r.endColor = new Color(.1f, .45f, 1f, alpha);
                r.SetPosition(0, ship.DrawPos + side * (i == 0 ? -1 : 1) + direction * .6f);
                r.SetPosition(1, end);
            }
        }
        public override void MapComponentUpdate()
        {
            if (!Viewed) { Clear(); return; }
            foreach (var ship in beams.Keys.ToList()) if (!Visible(ship)) Forget(ship); else Show(ship);
        }
        void Forget(Building_NivarianAircraft ship)
        {
            if (ship == null || !beams.TryGetValue(ship, out var lines)) return;
            beams.Remove(ship);
            var unityState = UnityEngine.Random.state; Rand.PushState();
            try
            {
                foreach (var line in lines)
                {
                    if (line.obj == null) continue;
                    if (line.renderer != null)
                    {
                        line.renderer.enabled = false; line.renderer.startColor = line.start; line.renderer.endColor = line.end;
                    }
                    LumiPrefabPool.Return(Prefab, line.obj);
                }
            }
            finally { UnityEngine.Random.state = unityState; Rand.PopState(); }
        }
        internal void Clear()
        {
            foreach (var ship in beams.Keys.ToList()) Forget(ship);
            if (cleanup != null) cleanup.Unbind(this); cleanup = null;
        }
        public override void MapRemoved() { Clear(); }
    }
    [DisallowMultipleComponent]
    public sealed class AircraftPrismCleanup : MonoBehaviour
    {
        AircraftPrismVisuals owner;
        internal void Bind(AircraftPrismVisuals next) { if (owner == next) return; var previous = owner; owner = next; previous?.Clear(); }
        internal void Unbind(AircraftPrismVisuals previous) { if (owner == previous) owner = null; }
        void Release() { var previous = owner; owner = null; previous?.Clear(); }
        void LateUpdate() { if (owner != null && !owner.Viewed) Release(); }
        void OnDisable() { Release(); } void OnDestroy() { Release(); }
    }
}
