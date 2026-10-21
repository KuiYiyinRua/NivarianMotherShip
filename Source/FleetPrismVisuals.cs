using System;
using System.Collections.Generic;
using LumiParticle.Pooling;
using RimWorld.Planet;
using UnityEngine;
using Verse;
#if RIMWORLD_1_7
using GameObject = RimUnity.GameObject;
using LineRenderer = RimUnity.LineRenderer;
using Color = RimUnity.Color;
using VisualObject = RimUnity.Object;
#else
using VisualObject = UnityEngine.Object;
#endif

namespace NivarianCommander
{
    // Own the exact staff prefab locally rather than repeatedly spawning timed
    // flashes. A single line follows each continuous lock without a fade gap.
    public sealed class FleetPrismVisuals : MapComponent
    {
        const string Prefab = "line_psyBeam";
        sealed class Beam
        {
            public GameObject gameObject;
            public LineRenderer line;
            public Color originalStart, originalEnd;
            public int finishingTicks;
        }
        readonly Dictionary<Verb_FleetRay, Beam> beams = new Dictionary<Verb_FleetRay, Beam>();
        readonly List<Verb_FleetRay> remove = new List<Verb_FleetRay>();
        FleetPrismVisualCleanup cleanup;
        bool failed;

        public FleetPrismVisuals(Map map) : base(map) { }
        internal bool Viewed => Current.ProgramState == ProgramState.Playing && Find.CurrentMap == map
            && WorldRendererUtility.DrawingMap && !LongEventHandler.ShouldWaitForEvent;
        bool Visible(Verb_FleetRay ray)
        {
            var source = ray.Caster;
            var target = ray.beamTarget;
            return ray.VisibleBeam && source != null && source.Spawned && source.Map == map && !source.Destroyed
                && target != null && target.Spawned && !target.Destroyed && target.Map == map
                && source.Position.InBounds(map) && target.Position.InBounds(map)
                && !source.Position.Fogged(map) && !target.Position.Fogged(map);
        }

        public void Show(Verb_FleetRay ray)
        {
            if (!Viewed || failed) { Forget(ray); return; }
            if (!Visible(ray))
            {
                if (!beams.TryGetValue(ray, out var last) || last.finishingTicks <= 0) Forget(ray);
                return;
            }
            var unityRandom = UnityEngine.Random.state;
            Rand.PushState();
            try
            {
                BindCamera();
                if (!beams.TryGetValue(ray, out var beam))
                {
                    var go = LumiPrefabPool.Get(Prefab);
                    if (go == null) throw new InvalidOperationException("line_psyBeam prefab is missing");
                    var line = go.GetComponent<LineRenderer>();
                    if (line == null)
                    {
                        LumiPrefabPool.Return(Prefab, go);
                        throw new InvalidOperationException("line_psyBeam has no LineRenderer");
                    }
                    beam = new Beam { gameObject = go, line = line,
                        originalStart = line.startColor, originalEnd = line.endColor };
                    beams.Add(ray, beam);
                    line.positionCount = 2;
                    line.enabled = true;
                    go.SetActive(true);
                }
                beam.finishingTicks = 0;
                UpdateLine(ray, beam);
            }
            catch (Exception error)
            {
                failed = true; Clear();
                Log.ErrorOnce("[NMC] Fleet Prism beam visual unavailable: " + error, 9401301);
            }
            finally { UnityEngine.Random.state = unityRandom; Rand.PopState(); }
        }

        static void UpdateLine(Verb_FleetRay ray, Beam beam)
        {
            var from = ray.Caster.DrawPos;
            var to = ray.beamTarget.DrawPos;
            var direction = to - from;
            if (direction.sqrMagnitude > 1f)
            {
                direction.Normalize();
                from += direction * .6f; to -= direction * .4f;
            }
            // Same prefab/material/geometry as the native staff; only per-line
            // tint differs. Never mutate its shared material or width profile.
            var color = ray.Hero ? new Color(.62f, .6f, 1f, 1f)
                : ray.Freezing ? new Color(.38f, .72f, 1f, 1f) : new Color(.2f, .75f, 1f, 1f);
            float alpha = 1f;
            if (ray.Mothership)
            {
                int age = 36 - ray.flashRemaining;
                alpha = age < 6 ? Mathf.Clamp01((age + 1) / 6f) : Mathf.Clamp01(ray.flashRemaining / 18f);
            }
            color.a = alpha;
            beam.line.startColor = color;
            beam.line.endColor = ray.Freezing ? new Color(.52f, .86f, 1f, alpha) : new Color(.1f, .45f, 1f, alpha);
            beam.line.SetPosition(0, from); beam.line.SetPosition(1, to);
        }

        public override void MapComponentUpdate()
        {
            if (!Viewed) { Clear(); return; }
            if (beams.Count == 0) return;
            var unityRandom = UnityEngine.Random.state;
            Rand.PushState();
            try
            {
                remove.Clear();
                foreach (var pair in beams)
                {
                    if (pair.Value.finishingTicks > 0) continue;
                    if (!Visible(pair.Key) || pair.Value.gameObject == null || pair.Value.line == null)
                        remove.Add(pair.Key);
                    else UpdateLine(pair.Key, pair.Value);
                }
                foreach (var ray in remove) Forget(ray);
            }
            catch (Exception error)
            {
                failed = true; Clear();
                Log.ErrorOnce("[NMC] Fleet Prism beam update failed: " + error, 9401302);
            }
            finally { UnityEngine.Random.state = unityRandom; Rand.PopState(); }
        }

        static void Return(Beam beam)
        {
            if (beam.gameObject == null) return;
            var unityRandom = UnityEngine.Random.state;
            Rand.PushState();
            try
            {
                if (beam.line != null)
                {
                    beam.line.enabled = false;
                    beam.line.startColor = beam.originalStart; beam.line.endColor = beam.originalEnd;
                }
                LumiPrefabPool.Return(Prefab, beam.gameObject);
            }
            catch (Exception error)
            {
                if (beam.gameObject != null) VisualObject.Destroy(beam.gameObject);
                Log.ErrorOnce("[NMC] Fleet Prism beam cleanup failed: " + error, 9401303);
            }
            finally
            {
                beam.gameObject = null; beam.line = null;
                UnityEngine.Random.state = unityRandom; Rand.PopState();
            }
        }
        public void Forget(Verb_FleetRay ray)
        {
            if (!beams.TryGetValue(ray, out var beam)) return;
            beams.Remove(ray); Return(beam);
        }
        public void FinishHit(Verb_FleetRay ray)
        {
            if (beams.TryGetValue(ray, out var beam)) beam.finishingTicks = 6;
        }
        public override void MapComponentTick()
        {
            if (beams.Count == 0) return;
            remove.Clear();
            foreach (var pair in beams)
                if (pair.Value.finishingTicks > 0 && --pair.Value.finishingTicks == 0) remove.Add(pair.Key);
            foreach (var ray in remove) Forget(ray);
        }
        internal void Clear()
        {
            foreach (var beam in beams.Values) Return(beam);
            beams.Clear(); remove.Clear();
            if (cleanup != null) cleanup.Unbind(this);
            cleanup = null;
        }
        void BindCamera()
        {
            var camera = Find.Camera;
            if (camera == null) return;
            if (cleanup == null)
                cleanup = camera.GetComponent<FleetPrismVisualCleanup>() ?? camera.gameObject.AddComponent<FleetPrismVisualCleanup>();
            cleanup.Bind(this);
        }
        public override void MapRemoved() { Clear(); }
    }

    [DisallowMultipleComponent]
    public sealed class FleetPrismVisualCleanup : MonoBehaviour
    {
        FleetPrismVisuals owner;
        internal void Bind(FleetPrismVisuals next)
        {
            if (owner == next) return;
            var previous = owner; owner = next; previous?.Clear();
        }
        internal void Unbind(FleetPrismVisuals previous) { if (owner == previous) owner = null; }
        void Release() { var previous = owner; owner = null; previous?.Clear(); }
        void LateUpdate() { if (owner != null && !owner.Viewed) Release(); }
        void OnDisable() { Release(); }
        void OnDestroy() { Release(); }
    }
}
