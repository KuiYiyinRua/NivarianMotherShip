using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace NivarianCommander
{
    public sealed class Hediff_GhostIdentity : HediffWithComps
    {
        public int batchId;
        public float speedScale = 1;
        public int templateVersion; public string templateMigration;
        public override void ExposeData()
        { base.ExposeData(); Scribe_Values.Look(ref templateVersion, "nmcTemplateVersion"); Scribe_Values.Look(ref templateMigration, "nmcTemplateMigration"); Scribe_Values.Look(ref batchId, "nmcGhostBatchId"); Scribe_Values.Look(ref speedScale, "nmcGhostSpeedScale", 1f); }
        public static Hediff_GhostIdentity For(Pawn pawn)
        {
            var hediffs = pawn?.health?.hediffSet?.hediffs;
            if (hediffs != null)
                for (int i = 0; i < hediffs.Count; i++)
                    if (hediffs[i] is Hediff_GhostIdentity identity) return identity;
            return null;
        }
    }
    public sealed class StatPart_GhostSpeed : StatPart
    {
        public override void TransformValue(StatRequest req, ref float val)
        { if (req.HasThing && req.Thing is Pawn pawn && Hediff_GhostIdentity.For(pawn) is Hediff_GhostIdentity identity) val *= identity.speedScale; }
        public override string ExplanationPart(StatRequest req)
            => req.HasThing && req.Thing is Pawn pawn && Hediff_GhostIdentity.For(pawn) is Hediff_GhostIdentity identity
                ? NmcText.Get("NMC_Text0273", identity.speedScale.ToString("0.###")) : null;
    }
    public sealed class CompProperties_GhostEquipment : CompProperties
    { public CompProperties_GhostEquipment() { compClass = typeof(CompGhostEquipment); } }
    public sealed class CompGhostEquipment : ThingComp
    {
        // Integer source identity survives a dropped item's original pawn being recalled/discarded.
        public int originalPawnId, batchId;
        public override void PostExposeData()
        { base.PostExposeData(); Scribe_Values.Look(ref originalPawnId, "nmcGhostOriginalPawnId"); Scribe_Values.Look(ref batchId, "nmcGhostEquipmentBatchId"); }
    }
    public sealed class GhostBatch : IExposable
    {
        public int id, remaining = 7200, createdLocalTick;
        public Map originMap;
        public bool firstTickPending = true;
        public bool awaitingDrop;
        public List<Pawn> members = new List<Pawn>();
        public void ExposeData()
        {
            Scribe_Values.Look(ref id, "id"); Scribe_Values.Look(ref remaining, "remainingLocalTicks", 7200);
            Scribe_Values.Look(ref createdLocalTick, "createdLocalTick");
            Scribe_Values.Look(ref awaitingDrop, "awaitingDrop");
            Scribe_References.Look(ref originMap, "originMap"); Scribe_Values.Look(ref firstTickPending, "firstTickPending", true);
            // Native death destroys the pawn's map object while its corpse still owns it.
            Scribe_Collections.Look(ref members, "members", saveDestroyedThings: true, lookMode: LookMode.Reference);
            if (Scribe.mode == LoadSaveMode.PostLoadInit) members = members ?? new List<Pawn>();
        }
    }
    public sealed class CommanderGhosts : WorldComponent
    {
        public List<GhostBatch> batches = new List<GhostBatch>();
        public int nextBatchId = 1;
        public CommanderGhosts(World world) : base(world) { }
        public static CommanderGhosts Current => Find.World?.GetComponent<CommanderGhosts>();
        public void TickMap(Map map)
        {
            if (batches.Count == 0) return;
            foreach (var batch in batches.Where(b => b.originMap == map).ToList())
            {
                if (batch.awaitingDrop)
                {
                    if (!batch.members.Any(p => p != null && p.Spawned)
                        && batch.members.Any(p => p != null && !p.Destroyed && !p.Dead)) continue;
                    batch.awaitingDrop = false; batch.firstTickPending = false;
                    continue;
                }
                if (batch.firstTickPending)
                {
                    batch.firstTickPending = false;
                    if (GenTicks.TicksGame == batch.createdLocalTick) continue;
                }
                if (--batch.remaining <= 0) { Recall(batch); batches.Remove(batch); }
            }
        }
        public override void WorldComponentTick()
        {
            if (batches.Count == 0) return;
            foreach (var batch in batches.Where(b => b.originMap == null || !Find.Maps.Contains(b.originMap)).ToList())
            { Recall(batch); batches.Remove(batch); }
        }
        static void Recall(GhostBatch batch)
        {
            foreach (var member in batch.members.Where(p => p != null).ToList())
            {
                // Follow the exact pawn/corpse reference even inside a container or another map.
                // Native pawn/corpse destruction returns only the member and its current contents.
                if (member.Corpse is Corpse corpse && !corpse.Destroyed) corpse.Destroy(DestroyMode.Vanish);
                if (!member.Destroyed) member.Destroy(DestroyMode.Vanish);
            }
            batch.members.Clear();
        }
        public override void ExposeData()
        {
            base.ExposeData(); Scribe_Values.Look(ref nextBatchId, "nmcGhostNextBatchId", 1);
            Scribe_Collections.Look(ref batches, "nmcGhostBatches", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit) batches = batches ?? new List<GhostBatch>();
        }
    }
    public sealed class CommanderGhostMap : MapComponent
    {
        public CommanderGhostMap(Map map) : base(map) { }
        public override void MapComponentTick()
        {
            CommanderGhosts.Current?.TickMap(map);
            var ghosts = CommanderPawnIndex.For(map).ghosts;
            if (ghosts.Count == 0) return;
            var unassigned = ghosts.Where(p => Hediff_GhostIdentity.For(p) != null
                && !p.Dead && !p.Destroyed && p.GetLord() == null).OrderBy(p => p.thingIDNumber).ToList();
            foreach (var group in unassigned.GroupBy(p => new { p.Faction, Hediff_GhostIdentity.For(p).batchId }))
                if (group.Key.Faction != null) LordMaker.MakeNewLord(group.Key.Faction, new LordJob_GhostAssault(), map, group.ToList());
        }
    }
    public static class GhostActions
    {
        static readonly string[] WeaponNames = { "NMC_PhaseTwinBlade", "NMC_VoidHeavyBlade", "NMC_ShadowLance" };
        static Faction NativeFaction => Find.FactionManager.AllFactionsListForReading
            .FirstOrDefault(f => f.def.defName == "NivarianSurvivorTeam" && !f.IsPlayer && !f.defeated && !f.HostileTo(Faction.OfPlayer));
        public static string Unavailable(Map map)
        {
            var reason = DefenseActions.Unavailable(map, 9);
            if (reason != null) return reason;
            if (NativeFaction == null) return NmcText.Get("NMC_Text0274");
            if (GroundReinforcementArrival.Unavailable != null) return GroundReinforcementArrival.Unavailable;
            if (DefDatabase<PawnKindDef>.GetNamedSilentFail("NMC_GhostGuard") == null
                || DefDatabase<HediffDef>.GetNamedSilentFail("NMC_GhostIdentity") == null
                || DefDatabase<DutyDef>.GetNamedSilentFail("NMC_GhostAssault") == null) return NmcText.Get("NMC_Text0275");
            foreach (var name in WeaponNames.Concat(new[] { "NMC_GhostSuit", "NMC_GhostHelmet" }))
            {
                var definition = DefDatabase<ThingDef>.GetNamedSilentFail(name);
                if (definition?.GetCompProperties<CompProperties_GhostEquipment>() == null) return NmcText.Get("NMC_Text0276");
                if (WeaponNames.Contains(name) && (definition.GetCompProperties<CompProperties_Biocodable>() == null
                    || !definition.HasComp(typeof(CompQuality)))) return NmcText.Get("NMC_Text0277");
            }
            return null;
        }
        public static bool CellLegal(Map map, IntVec3 cell)
            => map != null && cell.InBounds(map) && cell.x > 0 && cell.z > 0
                && cell.x < map.Size.x - 1 && cell.z < map.Size.z - 1
                && !cell.Fogged(map) && cell.Standable(map) && cell.GetRoof(map)?.isThickRoof != true
                && cell.GetThingList(map).All(t => !(t is Pawn) && !(t is Building));
        public static List<IntVec3> LandingCells(Map map, IntVec3 center)
            => CellLegal(map, center) ? GenRadial.RadialCellsAround(center, 8f, true).Where(c => CellLegal(map, c))
                .OrderBy(c => c.DistanceToSquared(center)).ThenBy(c => c.z).ThenBy(c => c.x).Take(4).ToList() : new List<IntVec3>();
        static void Reject(string reason)
        { CommanderState.Current.requestsRejected++; Messages.Message(reason, MessageTypeDefOf.RejectInput, false); }
        public static void Deploy(Map map, IntVec3 cell)
        {
            var reason = Unavailable(map); var cells = LandingCells(map, cell);
            if (reason == null && cells.Count != 4) reason = NmcText.Get("NMC_Text0278");
            if (reason != null) { Reject(reason); return; }
            var state = CommanderGhosts.Current;
            var batch = new GhostBatch { id = state.nextBatchId, originMap = map, createdLocalTick = GenTicks.TicksGame };
            try
            {
                for (int i = 0; i < 4; i++) batch.members.Add(CreateMember(NativeFaction, batch.id));
                if (cells.Any(c => !CellLegal(map, c))) { Reject(NmcText.Get("NMC_Text0279")); return; }
                var ability = DefenseActions.Ability(9);
                if (!NativeLedger.TryPay(ability.powerCost, ability.volatileCost)) { Reject(NmcText.Get("NMC_Text0038")); return; }
                CommanderState.Current.cooldowns[ability.defName] = ability.cooldownTicks;
                state.nextBatchId++; state.batches.Add(batch);
                for (int i = 0; i < 4; i++)
                {
                    GenSpawn.Spawn(batch.members[i], cells[i], map);
                    CalibrateSpawnedMember(batch.members[i]);
                }
                LordMaker.MakeNewLord(NativeFaction, new LordJob_GhostAssault(), map, batch.members);
                GroundReinforcementArrival.Send(map, cell, batch.members);
                batch.awaitingDrop = true;
            }
            finally
            {
                if (!state.batches.Contains(batch)) foreach (var member in batch.members) DiscardPrepared(member);
            }
        }
        static Pawn CreateMember(Faction faction, int batchId)
        {
            var request = new PawnGenerationRequest(DefDatabase<PawnKindDef>.GetNamed("NMC_GhostGuard"), faction,
                forceGenerateNewPawn: true, allowDead: false, allowDowned: false, canGeneratePawnRelations: false,
                mustBeCapableOfViolence: true, allowPregnant: false, allowFood: false, allowAddictions: false,
                fixedBiologicalAge: 25f, fixedChronologicalAge: 25f, fixedGender: Gender.Female,
                forceNoBackstory: true, forbidAnyTitle: true, dontGiveWeapon: true, forceNoGear: true);
            var pawn = PawnGenerator.GeneratePawn(request);
            var unattached = new List<Thing>();
            try
            {
                NativePawnPhysiology.ClearTemplateTraits(pawn);
                pawn.story.bodyType = BodyTypeDefOf.Thin;
                foreach (var skill in pawn.skills.skills) { skill.Level = 0; skill.passion = Passion.None; }
                var melee = pawn.skills.GetSkill(SkillDefOf.Melee);
                melee.DirtyAptitudes();
                // SkillRecord.Level writes the base but reads base + native gene aptitude.
                melee.Level = 16 - melee.Aptitude;
                var marker = (Hediff_GhostIdentity)HediffMaker.MakeHediff(DefDatabase<HediffDef>.GetNamed("NMC_GhostIdentity"), pawn);
                marker.batchId = batchId; pawn.health.AddHediff(marker);
                var weapon = (ThingWithComps)ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed(WeaponNames[Rand.Range(0, WeaponNames.Length)]));
                unattached.Add(weapon);
                weapon.GetComp<CompQuality>().SetQuality(QualityCategory.Masterwork, ArtGenerationContext.Outsider);
                weapon.GetComp<CompBiocodable>().CodeFor(pawn); Stamp(weapon, pawn, batchId);
                pawn.equipment.AddEquipment(weapon);
                unattached.Remove(weapon);
                foreach (var name in new[] { "NMC_GhostSuit", "NMC_GhostHelmet" })
                {
                    var apparel = (Apparel)ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed(name));
                    unattached.Add(apparel);
                    Stamp(apparel, pawn, batchId); pawn.apparel.Wear(apparel, false, false);
                    unattached.Remove(apparel);
                }
                CommanderCloak.EnsurePawnSource(pawn); // Coverage starts when the member is actually spawned.
                return pawn;
            }
            catch
            {
                foreach (var gear in unattached) if (!gear.Destroyed) gear.Destroy(DestroyMode.Vanish);
                DiscardPrepared(pawn);
                throw;
            }
        }
        static void CalibrateSpawnedMember(Pawn pawn)
        {
            // Native Nivarian PostSpawnSetup adds scale health/capacity effects.
            // Calibrate the newborn once after that setup; later injury/gear effects stay native.
            NativePawnPhysiology.InitializeSpawned(pawn);
            var marker = Hediff_GhostIdentity.For(pawn);
            float healthySpeed = StatDefOf.MoveSpeed.Worker.GetValue(StatRequest.For(pawn));
            if (healthySpeed <= 0) throw new InvalidOperationException("[NMC] Ghost template has no healthy movement speed");
            marker.speedScale = 5.2f / healthySpeed;
            marker.templateVersion = 1; marker.templateMigration = "birth-after-native-icy";
            pawn.health.Notify_HediffChanged(marker);
            StatDefOf.MoveSpeed.Worker.ClearCacheForThing(pawn);
        }
        static void DiscardPrepared(Pawn pawn)
        {
            if (!pawn.Destroyed) pawn.Destroy(DestroyMode.Vanish);
            if (Find.WorldPawns.Contains(pawn)) Find.WorldPawns.RemovePawn(pawn);
            Find.WorldPawns.PassToWorld(pawn, PawnDiscardDecideMode.Discard);
        }
        static void Stamp(Thing gear, Pawn owner, int batchId)
        { var source = gear.TryGetComp<CompGhostEquipment>(); source.originalPawnId = owner.thingIDNumber; source.batchId = batchId; }
        public static IEnumerable<Thing> HeldExclusive(Pawn pawn)
        {
            var held = new List<Thing>();
            if (pawn.equipment != null) held.AddRange(pawn.equipment.AllEquipmentListForReading);
            if (pawn.apparel != null) held.AddRange(pawn.apparel.WornApparel);
            if (pawn.inventory != null) held.AddRange(pawn.inventory.innerContainer);
            if (pawn.carryTracker?.CarriedThing is Thing carried) held.Add(carried);
            return held.Where(t => t.TryGetComp<CompGhostEquipment>()?.originalPawnId == pawn.thingIDNumber).Distinct();
        }
    }
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.Kill))]
    public static class GhostDeathEquipment
    {
        static void Prefix(Pawn __instance, out List<Thing> __state)
            => __state = Hediff_GhostIdentity.For(__instance) == null ? null : GhostActions.HeldExclusive(__instance).ToList();
        static void Postfix(Pawn __instance, List<Thing> __state)
        {
            if (!__instance.Dead || __state == null) return;
            foreach (var gear in __state) if (!gear.Destroyed) gear.Destroy(DestroyMode.Vanish);
        }
    }
    [HarmonyPatch(typeof(Pawn_DraftController), "set_Drafted")]
    public static class GhostNoDraft
    { static bool Prefix(bool value, Pawn ___pawn) => !value || Hediff_GhostIdentity.For(___pawn) == null; }
    [HarmonyPatch(typeof(Thing), nameof(Thing.TakeDamage))]
    public static class GhostFriendlyFire
    {
        static bool Prefix(Thing __instance, DamageInfo dinfo, ref DamageWorker.DamageResult __result)
        {
            if (!CommanderResearch.FriendlyFireProtectionActive || !SupportDiplomacy.Friendly(__instance)
                || !(dinfo.Instigator is Pawn pawn) || Hediff_GhostIdentity.For(pawn) == null
                || dinfo.Weapon?.GetCompProperties<CompProperties_GhostEquipment>() == null) return true;
            __result = new DamageWorker.DamageResult(); return false;
        }
    }
    public sealed class LordJob_GhostAssault : LordJob
    {
        public override bool CanAutoAddPawns => false;
        public override StateGraph CreateGraph()
        { var graph = new StateGraph(); graph.AddToil(new LordToil_GhostAssault()); return graph; }
    }
    public sealed class LordToil_GhostAssault : LordToil
    {
        public override void UpdateAllDuties()
        {
            foreach (var pawn in lord.ownedPawns)
                pawn.mindState.duty = new PawnDuty(DefDatabase<DutyDef>.GetNamed("NMC_GhostAssault"), pawn.Position);
        }
    }
    public sealed class JobGiver_GhostAssault : ThinkNode_JobGiver
    {
        protected override Job TryGiveJob(Pawn pawn)
        {
            if (Hediff_GhostIdentity.For(pawn) == null || pawn.Dead || pawn.Downed) return null;
            var target = pawn.Map.listerThings.AllThings.Where(t => t.Spawned && !t.Destroyed && t != pawn
                && (t is Pawn || t is Building || t is Nivarian.NivarianDrones.NivarianDroneBase)
                && (!(t is Pawn enemy) || !enemy.DeadOrDowned) && t.HostileTo(Faction.OfPlayer)
                && t.HostileTo(pawn.Faction) && !SupportDiplomacy.Friendly(t)
                && !CommanderCloak.HiddenFrom(t, pawn) && pawn.CanReach(t, PathEndMode.Touch, Danger.Deadly))
                .OrderBy(t => t.Position.DistanceToSquared(pawn.Position)).ThenBy(t => t.thingIDNumber).FirstOrDefault();
            var job = JobMaker.MakeJob(target == null ? JobDefOf.Wait_Combat : JobDefOf.AttackMelee, target == null ? LocalTargetInfo.Invalid : new LocalTargetInfo(target));
            job.expiryInterval = 60; job.checkOverrideOnExpire = true;
            return job;
        }
    }
}
