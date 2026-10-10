using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Nivarian.NivarianDrones;
using RimWorld;
using Verse;

namespace NivarianCommander
{
    public sealed class CloakDisruption : IExposable
    {
        public Thing target;
        public int remaining;
        public bool firstTickPending;
        public int createdLocalTick = -1;
        public int lastLocalTick = -1;
        public void ExposeData()
        {
            Scribe_References.Look(ref target, "target");
            Scribe_Values.Look(ref remaining, "remainingLocalTicks");
            Scribe_Values.Look(ref firstTickPending, "firstTickPending");
            Scribe_Values.Look(ref createdLocalTick, "createdLocalTick", -1);
            Scribe_Values.Look(ref lastLocalTick, "lastLocalTick", -1);
        }
    }
    public sealed class CommanderCloak : MapComponent
    {
        public const string OwnHediff = "NMC_ShadowInvisibility";
        static readonly MethodInfo Forced = AccessTools.PropertyGetter(typeof(HediffComp_Invisibility), "ForcedVisible");
        List<CloakDisruption> disruptions = new List<CloakDisruption>();
        public CommanderCloak(Map map) : base(map) { }
        public static bool Immune(Thing target)
            => target?.TryGetComp<CompCommanderConstruction>()?.Props.cloakImmune == true
                || target?.TryGetComp<CompCommanderStructure>()?.Props.cloakImmune == true;
        // Every query derives coverage from live sources/faction; no simulation writes or random rolls.
        public static bool Covered(Thing target)
        {
            if (target == null || !target.Spawned || target.Destroyed || Immune(target)
                || (target is Pawn dead && dead.Dead)) return false;
            if (target is Pawn guard && guard.health.hediffSet.hediffs.Any(h => h.def.defName == "NMC_GhostIdentity")) return true;
            if (!SupportDiplomacy.Friendly(target)) return false;
            var towerDef = DefDatabase<ThingDef>.GetNamedSilentFail("NMC_ShadowEnergyTower");
            if (towerDef == null) return false;
            foreach (var tower in target.Map.listerThings.ThingsOfDef(towerDef))
            {
                var properties = tower.TryGetComp<CompCommanderStructure>()?.Props;
                if (tower.Spawned && !tower.Destroyed && properties != null && properties.cloakRadius > 0
                    && SupportDiplomacy.Friendly(tower) && target.Position.InHorDistOf(tower.Position, properties.cloakRadius)) return true;
            }
            return false;
        }
        public static bool PawnForced(Pawn pawn)
            => pawn.health.hediffSet.hediffs.Select(h => h.TryGetComp<HediffComp_Invisibility>())
                .Any(comp => comp != null && (bool)Forced.Invoke(comp, null));
        // Carrying and platform containment despawn a living pawn without leaving
        // its map. Keep an already-issued reveal on that map's natural clock.
        static Map DisruptionMap(Thing target)
            => target == null || target.Destroyed ? null : target.Spawned ? target.Map
                : target is Pawn pawn && !pawn.Dead && pawn.SpawnedOrAnyParentSpawned ? pawn.MapHeld : null;
        public static bool Disrupted(Thing target)
            => DisruptionMap(target)?.GetComponent<CommanderCloak>().disruptions.Any(r => r.target == target
                    && r.remaining > (r.lastLocalTick < 0 ? 0 : Math.Max(0, GenTicks.TicksGame - r.lastLocalTick))) == true;
        public static bool UninitializedPawnForced(Pawn pawn)
        {
            var disruptor = DefDatabase<HediffDef>.GetNamedSilentFail("DisruptorFlash");
            var foam = DefDatabase<HediffDef>.GetNamedSilentFail("CoveredInFirefoam");
            return PawnForced(pawn) || pawn.Downed || pawn.IsBurning()
                || ((pawn.Faction == null || !pawn.Faction.IsPlayer) && pawn.stances?.stunner?.Stunned == true)
                || (disruptor != null && pawn.health.hediffSet.HasHediff(disruptor))
                || (foam != null && pawn.health.hediffSet.HasHediff(foam))
                || pawn.ParentHolder is Pawn_CarryTracker
                || pawn.TryGetComp<CompHoldingPlatformTarget>()?.CurrentlyHeldOnPlatform == true;
        }
        public static bool Invisible(Thing target)
        {
            if (target == null || target.Destroyed || !target.Spawned || Immune(target)) return false;
            if (target is Pawn pawn) return pawn.IsPsychologicallyInvisible();
            return Covered(target) && !target.IsBurning()
                && !target.Map.GetComponent<CommanderCloak>().disruptions.Any(r => r.target == target && r.remaining > 0);
        }
        public static bool HiddenFrom(Thing target, Thing observer)
            => observer != null && target != null && target.HostileTo(observer.Faction)
                && Invisible(target) && !NivarianAntiCloakingHelper.CanEngageInvisibleFrom(observer);
        public static void EnsurePawnSource(Pawn pawn)
        {
            if (Covered(pawn) && !pawn.health.hediffSet.hediffs.Any(h => h.def.defName == OwnHediff))
                pawn.health.AddHediff(DefDatabase<HediffDef>.GetNamed(OwnHediff));
        }
        // Integration entry for non-Pawn anti-cloak; local duration freezes with the affected map.
        public static void Disrupt(Thing target, int ticks = 180)
        {
            if (target == null || !target.Spawned || target.Destroyed || Immune(target) || ticks <= 0) return;
            if (target is Pawn pawn)
            {
                EnsurePawnSource(pawn);
                var sources = pawn.health.hediffSet.hediffs.Select(h => h.TryGetComp<HediffComp_Invisibility>()).Where(c => c != null).ToList();
                if (sources.Any(c => c.parent.def.defName == OwnHediff))
                    RecordDisruption(pawn, Math.Max(ticks, sources.Max(c => c.Props.recoverFromDisruptedTicks)));
                foreach (var comp in sources)
                    comp.DisruptInvisibility();
                return;
            }
            RecordDisruption(target, ticks);
        }
        // A shared native reveal must survive loss of our aura while another native source remains.
        public static void RecordDisruption(Thing target, int ticks)
        {
            var ownerMap = DisruptionMap(target);
            if (ownerMap == null || ticks <= 0) return;
            var records = ownerMap.GetComponent<CommanderCloak>().disruptions;
            var record = records.FirstOrDefault(r => r.target == target);
            if (record == null) { record = new CloakDisruption { target = target }; records.Add(record); }
            record.remaining = Math.Max(record.remaining, ticks); record.firstTickPending = true;
            record.createdLocalTick = GenTicks.TicksGame;
            record.lastLocalTick = GenTicks.TicksGame;
        }
        public override void MapComponentTick()
        {
            foreach (var record in disruptions)
            {
                if (record.firstTickPending)
                {
                    record.firstTickPending = false;
                    if (record.createdLocalTick == GenTicks.TicksGame) continue;
                }
                record.remaining--;
                record.lastLocalTick = GenTicks.TicksGame;
            }
            disruptions.RemoveAll(r => DisruptionMap(r.target) != map || r.remaining <= 0);
            foreach (var pawn in map.mapPawns.AllPawnsSpawned.OrderBy(p => p.thingIDNumber).ToList())
            {
                if (Covered(pawn)) EnsurePawnSource(pawn);
                else foreach (var own in pawn.health.hediffSet.hediffs.Where(h => h.def.defName == OwnHediff).ToList()) pawn.health.RemoveHediff(own);
            }
        }
        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref disruptions, "nmcCloakDisruptions", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit) disruptions = disruptions ?? new List<CloakDisruption>();
        }
    }
    [HarmonyPatch(typeof(InvisibilityUtility), nameof(InvisibilityUtility.IsPsychologicallyInvisible))]
    public static class CommanderCloakNewPawnQuery
    {
        static void Postfix(Pawn pawn, ref bool __result)
        {
            if (CommanderCloak.Covered(pawn)
                && !pawn.health.hediffSet.hediffs.Any(h => h.def.defName == CommanderCloak.OwnHediff)
                && !CommanderCloak.UninitializedPawnForced(pawn)) __result = true;
        }
    }
    [HarmonyPatch(typeof(HediffComp_Invisibility), "get_PsychologicallyVisible")]
    public static class CommanderCloakOwnVisibility
    {
        static void Postfix(HediffComp_Invisibility __instance, ref bool __result)
        {
            if (__instance.parent.def.defName == CommanderCloak.OwnHediff
                && (!CommanderCloak.Covered(__instance.Pawn) || CommanderCloak.PawnForced(__instance.Pawn))) __result = true;
        }
    }
    [HarmonyPatch(typeof(HediffComp_Invisibility), "UpdateTarget")]
    public static class CommanderCloakOwnCache
    {
        static bool Prefix(HediffComp_Invisibility __instance)
        {
            if (__instance.parent.def.defName != CommanderCloak.OwnHediff) return true;
            var pawn = __instance.Pawn;
            if (pawn.Spawned) pawn.Map.attackTargetsCache.UpdateTarget(pawn);
            if (pawn.RaceProps.Humanlike)
            { PortraitsCache.SetDirty(pawn); GlobalTextureAtlasManager.TryMarkPawnFrameSetDirty(pawn); }
            return false;
        }
    }
    [HarmonyPatch(typeof(NivarianAntiCloakingHelper), nameof(NivarianAntiCloakingHelper.Reveal))]
    public static class CommanderCloakNativeReveal
    {
        static void Prefix(Pawn pawn, out bool __state)
        {
            __state = pawn != null && pawn.Spawned && !pawn.Destroyed && pawn.IsPsychologicallyInvisible();
            if (pawn != null) CommanderCloak.EnsurePawnSource(pawn);
        }
        static void Postfix(Pawn pawn, bool __state)
        {
            // Guarantee shared ownership at the actual helper entry, including optimized native calls.
            if (__state && pawn.health.hediffSet.hediffs.Any(h => h.def.defName == CommanderCloak.OwnHediff))
                CommanderCloak.Disrupt(pawn);
        }
    }
    [HarmonyPatch(typeof(HediffComp_Invisibility), nameof(HediffComp_Invisibility.DisruptInvisibility))]
    public static class CommanderCloakShareDisruption
    {
        [ThreadStatic] static bool forwarding;
        static void Postfix(HediffComp_Invisibility __instance)
        {
            var pawn = __instance.Pawn;
            if (forwarding || !pawn.health.hediffSet.hediffs.Any(h => h.def.defName == CommanderCloak.OwnHediff)) return;
            var sources = pawn.health.hediffSet.hediffs.Select(h => h.TryGetComp<HediffComp_Invisibility>()).Where(c => c != null).ToList();
            CommanderCloak.RecordDisruption(pawn, sources.Max(c => c.Props.recoverFromDisruptedTicks));
            forwarding = true;
            try
            {
                foreach (var comp in sources.Where(c => c != __instance))
                    comp.DisruptInvisibility();
            }
            finally { forwarding = false; }
        }
    }
    [HarmonyPatch(typeof(HediffComp_Invisibility), "get_ForcedVisible")]
    public static class CommanderCloakSharedVisibility
    {
        static void Postfix(HediffComp_Invisibility __instance, ref bool __result)
        {
            if (CommanderCloak.Disrupted(__instance.Pawn)) __result = true;
        }
    }
}
