using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.AI.Group;

namespace NivarianCommander
{
    public sealed class Hediff_LegionIdentity : HediffWithComps
    {
        public int batchId;
        public int legionAbility = 14;
        public bool hero;
        public float speedScale = 1f;
        public int templateVersion; public string templateMigration;
        public LegionFocusBackend focusBackend; public float fallbackFocus; public int focusRegenTicks;
        public static Hediff_LegionIdentity For(Pawn pawn) => pawn?.health?.hediffSet?.hediffs.OfType<Hediff_LegionIdentity>().FirstOrDefault();
        public override void ExposeData()
        {
            base.ExposeData(); Scribe_Values.Look(ref batchId, "nmcLegionBatch");
            Scribe_Values.Look(ref legionAbility, "nmcLegionAbility", 14);
            Scribe_Values.Look(ref templateVersion, "nmcTemplateVersion"); Scribe_Values.Look(ref templateMigration, "nmcTemplateMigration");
            Scribe_Values.Look(ref hero, "nmcLegionHero"); Scribe_Values.Look(ref speedScale, "nmcLegionSpeed", 1f);
            Scribe_Values.Look(ref focusBackend, "nmcLegionFocusBackend"); Scribe_Values.Look(ref fallbackFocus, "nmcLegionFallbackFocus"); Scribe_Values.Look(ref focusRegenTicks, "nmcLegionFocusRegen");
        }
    }
    public sealed class StatPart_LegionSpeed : StatPart
    {
        public override void TransformValue(StatRequest req, ref float val)
        { if (req.HasThing && req.Thing is Pawn pawn && Hediff_LegionIdentity.For(pawn) is Hediff_LegionIdentity marker) val *= marker.speedScale; }
        public override string ExplanationPart(StatRequest req) => req.HasThing && req.Thing is Pawn pawn && Hediff_LegionIdentity.For(pawn) is Hediff_LegionIdentity marker
            ? NmcText.Get("NMC_Text0280", marker.speedScale.ToString("0.###")) : null;
    }
    public sealed class CompProperties_LegionEquipment : CompProperties
    { public CompProperties_LegionEquipment() { compClass = typeof(CompLegionEquipment); } }
    public sealed class CompLegionEquipment : ThingComp
    {
        public int batchId, originalPawnId;
        public override void PostExposeData()
        { base.PostExposeData(); Scribe_Values.Look(ref batchId, "nmcLegionGearBatch"); Scribe_Values.Look(ref originalPawnId, "nmcLegionGearPawn"); }
    }
    public sealed class LegionBatch : IExposable
    {
        public int id, ability = 14, createdLocalTick, remaining = 7200;
        public bool committed, firstTickPending = true;
        public Map origin;
        public List<Pawn> members = new List<Pawn>();
        public List<Thing> equipment = new List<Thing>();
        public void ExposeData()
        {
            Scribe_Values.Look(ref id, "id"); Scribe_Values.Look(ref ability, "ability", 14);
            Scribe_Values.Look(ref createdLocalTick, "createdLocalTick"); Scribe_Values.Look(ref remaining, "remaining", 7200);
            Scribe_Values.Look(ref committed, "committed"); Scribe_Values.Look(ref firstTickPending, "firstTickPending", true);
            Scribe_References.Look(ref origin, "origin");
            Scribe_Collections.Look(ref members, "members", saveDestroyedThings: true, lookMode: LookMode.Reference);
            Scribe_Collections.Look(ref equipment, "equipment", saveDestroyedThings: true, lookMode: LookMode.Reference);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            { members = members ?? new List<Pawn>(); equipment = equipment ?? new List<Thing>(); }
        }
    }
    public sealed class CommanderLegions : WorldComponent
    {
        public int nextId = 1;
        public List<LegionBatch> batches = new List<LegionBatch>();
        public CommanderLegions(World world) : base(world) { }
        public static CommanderLegions Current => Find.World?.GetComponent<CommanderLegions>();
        public void TickMap(Map map)
        {
            foreach (var batch in batches.Where(b => b.origin == map && b.committed).ToList())
            {
                if (batch.firstTickPending)
                { batch.firstTickPending = false; if (GenTicks.TicksGame == batch.createdLocalTick) continue; }
                if (--batch.remaining <= 0) Retire(batch);
            }
        }
        public void RemoveMap(Map map)
        { foreach (var batch in batches.Where(b => b.origin == map).ToList()) Retire(batch); }
        public override void WorldComponentTick()
        { foreach (var batch in batches.Where(b => b.origin == null || !Find.Maps.Contains(b.origin)).ToList()) Retire(batch); }
        public void Retire(LegionBatch batch)
        {
            batch.origin?.GetComponent<TebrusStormMap>()?.Cancel(batch.members);
            // Retain exact original gear references: a dropped item in another
            // pawn's inventory is recalled without destroying that pawn/holder.
            foreach (var gear in batch.equipment.Where(t => t != null).Distinct().ToList())
                if (!gear.Destroyed) gear.Destroy(DestroyMode.Vanish);
            foreach (var pawn in batch.members.Where(p => p != null).Distinct().ToList())
            {
                if (pawn.Corpse is Corpse corpse && !corpse.Destroyed) corpse.Destroy(DestroyMode.Vanish);
                if (!pawn.Destroyed) pawn.Destroy(DestroyMode.Vanish);
                // Native GC discards remove this pawn's play/battle/tale
                // references through the supported cleanup notifications.
                // Forced Discard warns for each still-referenced battle entry.
                if (pawn.Discarded)
                { if (Find.WorldPawns.Contains(pawn)) Find.WorldPawns.RemovePawn(pawn); continue; }
                if (!Find.WorldPawns.Contains(pawn))
                    Find.WorldPawns.PassToWorld(pawn, PawnDiscardDecideMode.Decide);
                Find.WorldPawns.RemoveAndDiscardPawnViaGC(pawn);
            }
            batches.Remove(batch);
        }
        public override void ExposeData()
        {
            base.ExposeData(); Scribe_Values.Look(ref nextId, "nmcLegionNextId", 1);
            Scribe_Collections.Look(ref batches, "nmcLegions", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit) batches = batches ?? new List<LegionBatch>();
        }
    }
    public sealed class CommanderLegionMap : MapComponent
    {
        public CommanderLegionMap(Map map) : base(map) { }
        public override void MapComponentTick()
        {
            CommanderLegions.Current?.TickMap(map);
            foreach (var pawn in map.mapPawns.AllPawnsSpawned.OrderBy(p => p.thingIDNumber))
                if (Hediff_LegionIdentity.For(pawn) is Hediff_LegionIdentity marker) LegionFocus.Tick(pawn, marker);
            var unassigned = map.mapPawns.AllPawnsSpawned.Where(p => !p.Dead && !p.Downed
                && Hediff_LegionIdentity.For(p) != null && p.GetLord() == null).OrderBy(p => p.thingIDNumber).ToList();
            foreach (var group in unassigned.GroupBy(p => new { p.Faction, Hediff_LegionIdentity.For(p).batchId }))
                if (group.Key.Faction != null) Verse.AI.Group.LordMaker.MakeNewLord(group.Key.Faction, new LordJob_LegionAssault(), map, group.ToList());
        }
        public override void MapRemoved() => CommanderLegions.Current?.RemoveMap(map);
    }
    [HarmonyPatch(typeof(Pawn_DraftController), "set_Drafted")]
    public static class LegionNoDraft
    { static bool Prefix(bool value, Pawn ___pawn) => !value || Hediff_LegionIdentity.For(___pawn) == null; }
    [HarmonyPatch(typeof(Thing), nameof(Thing.TakeDamage))]
    public static class LegionFriendlyFire
    {
        static bool Prefix(Thing __instance, DamageInfo dinfo, ref DamageWorker.DamageResult __result)
        {
            if (!CommanderResearch.FriendlyFireProtectionActive || !SupportDiplomacy.Friendly(__instance)
                || !(dinfo.Instigator is Pawn pawn) || Hediff_LegionIdentity.For(pawn) == null
                || dinfo.Weapon?.GetCompProperties<CompProperties_LegionEquipment>() == null) return true;
            __result = new DamageWorker.DamageResult(); return false;
        }
    }
}
