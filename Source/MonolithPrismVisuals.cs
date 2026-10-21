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
    // The staff's actual line prefab, driven by the monolith's existing 12-tick
    // flash. Damage/stun remain in CompMonolithWeapon; this cache is never saved.
    public sealed class MonolithPrismVisuals : MapComponent
    {
        const string Prefab = "line_psyBeam";
        sealed class Beam
        {
            internal Thing target;
            internal Vector3 lastEnd;
            internal GameObject gameObject;
            internal LineRenderer line;
            internal Color originalStart, originalEnd;
        }
        sealed class Volley
        {
            internal int round;
            internal CompMonolithWeapon source;
            internal readonly List<Beam> beams = new List<Beam>(10);
        }
        readonly Dictionary<CompMonolithWeapon, Volley> volleys = new Dictionary<CompMonolithWeapon, Volley>();
        readonly List<CompMonolithWeapon> remove = new List<CompMonolithWeapon>();
        MonolithPrismVisualCleanup cleanup;
        bool failed;

        public MonolithPrismVisuals(Map map) : base(map) { }
        internal bool Viewed => Current.ProgramState == ProgramState.Playing && Find.CurrentMap == map &&
            WorldRendererUtility.DrawingMap && !LongEventHandler.ShouldWaitForEvent;
        bool VisibleSource(CompMonolithWeapon weapon)
        {
            var source = weapon?.parent;
            return weapon != null && weapon.beamTicks > 0 && source != null && source.Spawned &&
                !source.Destroyed && source.Map == map && source.Position.InBounds(map) && !source.Position.Fogged(map);
        }

        // Called immediately before damage so a lethal first hit still has an
        // endpoint. PostDraw can also reconstruct an unexpired saved flash.
        public void Show(CompMonolithWeapon weapon)
        {
            if (!Viewed || failed || !VisibleSource(weapon)) { Forget(weapon); return; }
            var unityRandom = UnityEngine.Random.state;
            Rand.PushState();
            try
            {
                BindCamera();
                if (!volleys.TryGetValue(weapon, out var volley) || volley.round != weapon.roundsFired)
                {
                    Forget(weapon);
                    volley = new Volley { source = weapon, round = weapon.roundsFired };
                    volleys.Add(weapon, volley);
                    foreach (var target in weapon.LastTargets)
                    {
                        if (target == null || target.Destroyed || !target.Spawned || target.Map != map ||
                            !target.Position.InBounds(map) || target.Position.Fogged(map)) continue;
                        var go = LumiPrefabPool.Get(Prefab);
                        if (go == null) throw new InvalidOperationException("line_psyBeam prefab is missing");
                        // Register ownership before any further Unity calls can fail.
                        var beam = new Beam { target = target, gameObject = go };
                        volley.beams.Add(beam);
                        beam.line = go.GetComponent<LineRenderer>();
                        if (beam.line == null) throw new InvalidOperationException("line_psyBeam has no LineRenderer");
                        beam.originalStart = beam.line.startColor;
                        beam.originalEnd = beam.line.endColor;
                        beam.lastEnd = target.DrawPos;
                        beam.line.positionCount = 2;
                        beam.line.enabled = true;
                        go.SetActive(true);
                    }
                }
                UpdateLines(volley);
            }
            catch (Exception error)
            {
                failed = true; Clear();
                Log.ErrorOnce("[NMC] Monolith Prism beam visual unavailable: " + error, 9401701);
            }
            finally { UnityEngine.Random.state = unityRandom; Rand.PopState(); }
        }

        void UpdateLines(Volley volley)
        {
            Vector3 muzzle = volley.source.TurretVisuals.Muzzle(volley.source);
            int remaining = volley.source.beamTicks;
            // Keep the old 12-tick pulse; only its appearance changes.
            float alpha = Mathf.Min(Mathf.Clamp01((13 - remaining) / 2f), Mathf.Clamp01(remaining / 4f));
            foreach (var beam in volley.beams)
            {
                if (beam.gameObject == null || beam.line == null) continue;
                var target = beam.target;
                if (target != null && target.Spawned && !target.Destroyed)
                {
                    // Never follow a target into another map or reveal a fogged cell.
                    if (target.Map != map || !target.Position.InBounds(map) || target.Position.Fogged(map))
                    {
                        beam.line.enabled = false;
                        continue;
                    }
                    beam.lastEnd = target.DrawPos;
                }
                Vector3 from = muzzle, to = beam.lastEnd;
                Vector3 direction = to - from;
                if (direction.sqrMagnitude > 1f)
                {
                    direction.Normalize();
                    to -= direction * .4f;
                }
                beam.line.enabled = true;
                beam.line.startColor = new Color(.2f, .75f, 1f, alpha);
                beam.line.endColor = new Color(.1f, .45f, 1f, alpha);
                // The shared staff material, width, texture and damage are untouched.
                beam.line.SetPosition(0, from); beam.line.SetPosition(1, to);
            }
        }

        public override void MapComponentUpdate()
        {
            if (!Viewed) { Clear(); return; }
            if (volleys.Count == 0) return;
            var unityRandom = UnityEngine.Random.state;
            Rand.PushState();
            try
            {
                remove.Clear();
                foreach (var pair in volleys)
                {
                    if (!VisibleSource(pair.Key) || pair.Value.round != pair.Key.roundsFired) remove.Add(pair.Key);
                    else UpdateLines(pair.Value);
                }
                foreach (var weapon in remove) Forget(weapon);
            }
            catch (Exception error)
            {
                failed = true; Clear();
                Log.ErrorOnce("[NMC] Monolith Prism beam update failed: " + error, 9401702);
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
                    beam.line.startColor = beam.originalStart;
                    beam.line.endColor = beam.originalEnd;
                }
                LumiPrefabPool.Return(Prefab, beam.gameObject);
            }
            catch (Exception error)
            {
                if (beam.gameObject != null) VisualObject.Destroy(beam.gameObject);
                Log.ErrorOnce("[NMC] Monolith Prism beam cleanup failed: " + error, 9401703);
            }
            finally
            {
                beam.gameObject = null; beam.line = null;
                UnityEngine.Random.state = unityRandom; Rand.PopState();
            }
        }
        void Forget(CompMonolithWeapon weapon)
        {
            if (weapon == null || !volleys.TryGetValue(weapon, out var volley)) return;
            volleys.Remove(weapon);
            foreach (var beam in volley.beams) Return(beam);
        }
        internal void Clear()
        {
            foreach (var volley in volleys.Values)
                foreach (var beam in volley.beams) Return(beam);
            volleys.Clear(); remove.Clear();
            if (cleanup != null) cleanup.Unbind(this);
            cleanup = null;
        }
        void BindCamera()
        {
            var camera = Find.Camera;
            if (camera == null) return;
            if (cleanup == null) cleanup = camera.GetComponent<MonolithPrismVisualCleanup>() ??
                camera.gameObject.AddComponent<MonolithPrismVisualCleanup>();
            cleanup.Bind(this);
        }
        public override void MapRemoved() { Clear(); }
    }

    [DisallowMultipleComponent]
    public sealed class MonolithPrismVisualCleanup : MonoBehaviour
    {
        MonolithPrismVisuals owner;
        internal void Bind(MonolithPrismVisuals next)
        {
            if (owner == next) return;
            var previous = owner; owner = next; previous?.Clear();
        }
        internal void Unbind(MonolithPrismVisuals previous) { if (owner == previous) owner = null; }
        void Release() { var previous = owner; owner = null; previous?.Clear(); }
        void LateUpdate() { if (owner != null && !owner.Viewed) Release(); }
        void OnDisable() { Release(); }
        void OnDestroy() { Release(); }
    }
}
