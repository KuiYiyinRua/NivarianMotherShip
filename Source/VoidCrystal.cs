using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Nivarian.NivarianDrones;
using Nivarian_Race.Code.Comps.ThingComps;
using RimWorld;
using UnityEngine;
using Verse;

namespace NivarianCommander
{
    // Staged S18 source only. Native tests and release integration are still required.
    public sealed class Building_VoidCrystal : Building
    {
        public int remaining = VoidCrystalMap.Duration;
        public int serial;
        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref remaining, "nmcVoidCrystalRemaining", VoidCrystalMap.Duration);
            Scribe_Values.Look(ref serial, "nmcVoidCrystalSerial");
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
                remaining = Math.Max(0, Math.Min(VoidCrystalMap.Duration, remaining));
        }
        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);
            map.GetComponent<VoidCrystalMap>().Register(this);
        }
        public override void DeSpawn(DestroyMode mode = DestroyMode.Vanish)
        {
            var owner = Map;
            owner?.GetComponent<VoidCrystalMap>()?.Unregister(this);
            base.DeSpawn(mode);
        }
        public override void PreApplyDamage(ref DamageInfo dinfo, out bool absorbed)
        {
            // Includes ordinary bullets, explosions and direct damage calls.
            // The Def remains removable so expiry and MapRemoved can clean it.
            absorbed = true;
        }
        public override string GetInspectString()
            => NmcText.Get("NMC_Text0474", CommanderPanel.Period(remaining));
    }

    public sealed class VoidCrystalMap : MapComponent
    {
        public const int Radius = 18, Duration = 1800;
        public int castsReceived, lastCompletedElapsed;
        List<Building_VoidCrystal> sources = new List<Building_VoidCrystal>();
        static Material ring;
        public VoidCrystalMap(Map map) : base(map) { }
        public IReadOnlyList<Building_VoidCrystal> Sources => sources;
        public int ActiveCount => sources.Count(c => Valid(c));
        bool Valid(Building_VoidCrystal c)
            => c != null && !c.Destroyed && c.Spawned && c.Map == map && c.remaining > 0;
        public void Register(Building_VoidCrystal c)
        {
            if (sources == null) sources = new List<Building_VoidCrystal>();
            if (!sources.Contains(c)) sources.Add(c);
        }
        public void Unregister(Building_VoidCrystal c) => sources?.Remove(c);
        public static bool Eligible(Thing t)
            => t != null && !t.Destroyed && t.Spawned && t.Map != null
                && !(t is Pawn pawn && pawn.Dead) && t.HostileTo(Faction.OfPlayer)
                && !SupportDiplomacy.Friendly(t)
                && (t is Pawn || t is NivarianDroneBase || t is Building_Turret
                    || t.TryGetComp<CompAttachTurret>() != null || t.TryGetComp<CompMonolithWeapon>() != null);
        public static bool Affected(Thing t)
        {
            if (t?.Map == null) return false;
            var state = CommanderPawnIndex.For(t.Map).VoidCrystals;
            if (state == null || state.sources.Count == 0 || !Eligible(t)) return false;
            // Dynamic lookup on a short source list: no whole-map target scan,
            // cached faction/membership, or multiplying overlapping crystals.
            for (int i = 0; i < state.sources.Count; i++)
            {
                var crystal = state.sources[i];
                if (state.Valid(crystal) && t.Position.InHorDistOf(crystal.Position, Radius)) return true;
            }
            return false;
        }
        public static string PlacementError(Map map, IntVec3 center)
        {
            if (map == null || !Find.Maps.Contains(map)) return NmcText.Get("NMC_Text0034");
            if (!center.InBounds(map))
                return NmcText.Get("NMC_Text0475");
            if (center.Fogged(map)) return NmcText.Get("NMC_Text0476");
            // The crystal overlaps existing things and needs no supporting terrain.
            return null;
        }
        public void Cancel()
        {
            foreach (var c in sources.ToArray())
            {
                if (c == null || c.Destroyed || !c.Spawned || c.Map != map) continue;
                lastCompletedElapsed = Duration - c.remaining;
                c.remaining = 0;
                c.Destroy(DestroyMode.Vanish);
            }
            sources.Clear();
        }
        public override void MapRemoved() => Cancel();
        public override void MapComponentTick()
        {
            // A map-local clock, independent of Building tick batching and world CD.
            for (int i = sources.Count - 1; i >= 0; i--)
            {
                var c = sources[i];
                if (!Valid(c))
                {
                    sources.RemoveAt(i);
                    if (c != null && !c.Destroyed && c.Spawned && c.Map == map && c.remaining <= 0)
                        c.Destroy(DestroyMode.Vanish);
                    continue;
                }
                if (--c.remaining > 0) continue;
                lastCompletedElapsed = Duration;
                sources.RemoveAt(i);
                c.Destroy(DestroyMode.Vanish);
            }
        }
        public override void MapComponentUpdate()
        {
            if (Find.CurrentMap != map || sources.Count == 0) return;
            if (ring == null) ring = SolidColorMaterials.SimpleSolidColorMaterial(new Color(.45f, .62f, 1f, .24f), true);
            // Drawing performs no simulation writes, RNG calls, or target acquisition.
            foreach (var c in sources)
                if (Valid(c)) GenDraw.DrawCircleOutline(c.DrawPos, Radius, ring);
        }
        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref sources, "nmcVoidCrystalSources", LookMode.Reference);
            Scribe_Values.Look(ref castsReceived, "nmcVoidCrystalCastsReceived");
            Scribe_Values.Look(ref lastCompletedElapsed, "nmcVoidCrystalLastCompletedElapsed");
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                sources = sources ?? new List<Building_VoidCrystal>();
                sources.RemoveAll(c => c == null);
                // Native MP restoration can precede SpawnSetup. Keep live refs
                // without querying Spawned or Map here; first local tick validates.
            }
        }
    }

    public static class VoidCrystalActions
    {
        public static string Unavailable(Map map)
        {
            string blocked = DefenseActions.Unavailable(map, 18);
            if (blocked != null) return blocked;
            return DefDatabase<ThingDef>.GetNamedSilentFail("NMC_VoidSuppressionCrystal")?.thingClass
                == typeof(Building_VoidCrystal) ? null : NmcText.Get("NMC_Text0478");
        }
        public static void Start(Map map, IntVec3 center)
        {
            string blocked = Unavailable(map) ?? VoidCrystalMap.PlacementError(map, center);
            if (blocked != null) { Reject(blocked); return; }
            var crystal = (Building_VoidCrystal)ThingMaker.MakeThing(ThingDef.Named("NMC_VoidSuppressionCrystal"));
            crystal.SetFaction(Faction.OfPlayer);
            // Place successfully before committing the shared transaction. Native
            // SpawnSetup only registers this source; it cannot spend any resource.
            GenSpawn.Spawn(crystal, center, map, Rot4.North);
            if (!crystal.Spawned || crystal.Map != map || crystal.Position != center)
            {
                if (!crystal.Destroyed) crystal.Destroy(DestroyMode.Vanish);
                Reject(NmcText.Get("NMC_Text0479")); return;
            }
            var def = DefenseActions.Ability(18);
            if (!NativeLedger.TryPay(def.powerCost, def.volatileCost))
            { crystal.Destroy(DestroyMode.Vanish); Reject(NmcText.Get("NMC_Text0038")); return; }
            var state = map.GetComponent<VoidCrystalMap>();
            crystal.serial = ++state.castsReceived;
            state.lastCompletedElapsed = 0;
            CommanderState.Current.cooldowns[def.defName] = def.cooldownTicks;
        }
        public static void Cancel(Map map)
        {
            if (map != null && Find.Maps.Contains(map)) map.GetComponent<VoidCrystalMap>().Cancel();
        }
        static void Reject(string reason)
        {
            if (CommanderState.Current != null) CommanderState.Current.requestsRejected++;
            Messages.Message(reason, MessageTypeDefOf.RejectInput, false);
        }
    }

    [HarmonyPatch(typeof(GenSpawn), nameof(GenSpawn.SpawningWipes))]
    public static class VoidCrystalAllowOverlap
    {
        static bool Prefix(BuildableDef newEntDef, BuildableDef oldEntDef, ref bool __result)
        {
            if (!(newEntDef is ThingDef incoming && incoming.thingClass == typeof(Building_VoidCrystal))
                && !(oldEntDef is ThingDef existing && existing.thingClass == typeof(Building_VoidCrystal)))
                return true;
            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(TargetingParameters), nameof(TargetingParameters.CanTarget))]
    public static class VoidCrystalUntargetable
    {
        static void Postfix(TargetInfo targ, ref bool __result)
        { if (targ.Thing is Building_VoidCrystal) __result = false; }
    }
    [HarmonyPatch]
    public static class VoidCrystalRejectDirectAttack
    {
        static IEnumerable<System.Reflection.MethodBase> TargetMethods()
            => AccessTools.GetDeclaredMethods(typeof(Verb)).Where(m => m.Name == nameof(Verb.TryStartCastOn));
        static bool Prefix(LocalTargetInfo castTarg, ref bool __result)
        {
            if (!(castTarg.Thing is Building_VoidCrystal)) return true;
            __result = false; return false;
        }
    }
}
