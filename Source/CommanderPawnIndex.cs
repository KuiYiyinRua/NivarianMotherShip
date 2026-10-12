using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Verse;

namespace NivarianCommander
{
    // Derived membership only. No timers, random state or persisted gameplay data.
    // Native spawn and health-cache notifications also cover transport, resurrection,
    // direct AddDirect/Clear, and save loading. Always retain the old thing-ID order.
    public sealed class CommanderPawnIndex : MapComponent
    {
        static readonly ConditionalWeakTable<Map, CommanderPawnIndex> states = new ConditionalWeakTable<Map, CommanderPawnIndex>();
        static readonly IComparer<Pawn> byId = Comparer<Pawn>.Create((a, b) => a.thingIDNumber.CompareTo(b.thingIDNumber));
        internal readonly List<Pawn> ghosts = new List<Pawn>();
        internal readonly List<Pawn> legions = new List<Pawn>();
        readonly List<Pawn> cloakSources = new List<Pawn>();
        readonly List<Pawn> cloaked = new List<Pawn>();
        CommanderControl control;
        TimeStopMap timeStop;
        VoidCrystalMap voidCrystals;
        AircraftGravity gravity;
        BlackHoleMap blackHole;
        internal AircraftGravity Gravity => gravity ?? (gravity = map.GetComponent<AircraftGravity>());
        internal BlackHoleMap BlackHole => blackHole ?? (blackHole = map.GetComponent<BlackHoleMap>());
        internal CommanderControl Control => control ?? (control = map.GetComponent<CommanderControl>());
        internal TimeStopMap TimeStop => timeStop ?? (timeStop = map.GetComponent<TimeStopMap>());
        internal VoidCrystalMap VoidCrystals => voidCrystals ?? (voidCrystals = map.GetComponent<VoidCrystalMap>());

        public CommanderPawnIndex(Map map) : base(map)
        { states.Remove(map); states.Add(map, this); }
        public static CommanderPawnIndex For(Map map)
            => map == null ? null : states.TryGetValue(map, out var state) ? state : map.GetComponent<CommanderPawnIndex>();

        static void Set(List<Pawn> list, Pawn pawn, bool present)
        {
            int at = list.BinarySearch(pawn, byId);
            if (present) { if (at < 0) list.Insert(~at, pawn); }
            else if (at >= 0) list.RemoveAt(at);
        }
        public void Refresh(Pawn pawn)
        {
            if (pawn == null) return;
            bool ghost = false, legion = false, own = false, source = false;
            if (pawn.Spawned && pawn.Map == map && !pawn.Destroyed)
            {
                var hediffs = pawn.health?.hediffSet?.hediffs;
                if (hediffs != null)
                    for (int i = 0; i < hediffs.Count; i++)
                    {
                        var h = hediffs[i];
                        ghost |= h is Hediff_GhostIdentity;
                        legion |= h is Hediff_LegionIdentity;
                        own |= h.def.defName == CommanderCloak.OwnHediff;
                        source |= h.def.defName == "NMC_GhostIdentity";
                    }
            }
            Set(ghosts, pawn, ghost); Set(legions, pawn, legion);
            Set(cloaked, pawn, own); Set(cloakSources, pawn, source);
        }
        public void Remove(Pawn pawn)
        { Set(ghosts, pawn, false); Set(legions, pawn, false); Set(cloaked, pawn, false); Set(cloakSources, pawn, false); }
        internal void CopyCloakTargets(List<Pawn> result, bool towersPresent)
        {
            result.Clear();
            if (towersPresent) result.AddRange(map.mapPawns.AllPawnsSpawned);
            else
            {
                result.AddRange(cloakSources);
                foreach (var pawn in cloaked)
                    if (cloakSources.BinarySearch(pawn, byId) < 0) result.Add(pawn);
            }
            result.Sort(byId);
        }
        public override void FinalizeInit()
        {
            ghosts.Clear(); legions.Clear(); cloakSources.Clear(); cloaked.Clear();
            foreach (var pawn in map.mapPawns.AllPawnsSpawned) Refresh(pawn);
        }
    }

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.SpawnSetup))]
    internal static class CommanderPawnIndexSpawn
    { static void Postfix(Pawn __instance) => CommanderPawnIndex.For(__instance.Map)?.Refresh(__instance); }

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.DeSpawn))]
    internal static class CommanderPawnIndexDespawn
    {
        static void Prefix(Pawn __instance, out Map __state) => __state = __instance.Map;
        static void Postfix(Pawn __instance, Map __state)
        { if (!__instance.Spawned || __instance.Map != __state) CommanderPawnIndex.For(__state)?.Remove(__instance); }
    }

    [HarmonyPatch(typeof(HediffSet), nameof(HediffSet.DirtyCache))]
    internal static class CommanderPawnIndexHealth
    { static void Postfix(HediffSet __instance) => CommanderPawnIndex.For(__instance.pawn.Map)?.Refresh(__instance.pawn); }
}
