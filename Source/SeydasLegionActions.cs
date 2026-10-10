using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace NivarianCommander
{
    public static class SeydasLegionActions
    {
        static readonly string[] GearNames = { "NMC_SeydasRobe", "NMC_SeydasCirclet", "NMC_ArchonFocus", "NMC_ArchonRobe", "NMC_ArchonCirclet" };
        public static string Unavailable(Map map)
        {
            string blocked = LegionPurchase.Unavailable(map, 16);
            if (blocked != null) return blocked;
            foreach (string name in GearNames)
                if (DefDatabase<ThingDef>.GetNamedSilentFail(name)?.GetCompProperties<CompProperties_LegionEquipment>() == null) return NmcText.Get("NMC_Text0447");
            return DefDatabase<PawnKindDef>.GetNamedSilentFail("NMC_Seydas") == null
                || DefDatabase<PawnKindDef>.GetNamedSilentFail("NMC_DarkArchon") == null
                || DefDatabase<HediffDef>.GetNamedSilentFail("NMC_LegionIdentity") == null
                || DefDatabase<HediffDef>.GetNamedSilentFail("NMC_SeydasConversion") == null
                || DefDatabase<AbilityDef>.GetNamedSilentFail("NMC_SeydasControlFallback") == null
                || DefDatabase<DutyDef>.GetNamedSilentFail("NMC_LegionAssault") == null ? NmcText.Get("NMC_Text0448") : null;
        }
        public static List<IntVec3> LandingCells(Map map, IntVec3 center) => GhostActions.CellLegal(map, center)
            ? GenRadial.RadialCellsAround(center, 8f, true).Where(c => GhostActions.CellLegal(map, c))
                .OrderBy(c => c.DistanceToSquared(center)).ThenBy(c => c.z).ThenBy(c => c.x).Take(4).ToList() : new List<IntVec3>();
        public static string PlacementError(Map map, IntVec3 center) => map == null || !Find.Maps.Contains(map)
            ? NmcText.Get("NMC_Text0034") : LandingCells(map, center).Count != 4 ? NmcText.Get("NMC_Text0449") : null;
        static void Reject(string reason)
        { CommanderState.Current.requestsRejected++; Messages.Message(reason, MessageTypeDefOf.RejectInput, false); }
        public static void Start(Map map, IntVec3 center)
        {
            string blocked = Unavailable(map) ?? PlacementError(map, center);
            if (blocked != null) { Reject(blocked); return; }
            var faction = LegionPurchase.NativeFaction; var owner = CommanderLegions.Current;
            var batch = new LegionBatch { ability = 16, id = owner.nextId++, origin = map, createdLocalTick = GenTicks.TicksGame };
            owner.batches.Add(batch);
            try
            {
                var cells = LandingCells(map, center);
                for (int i = 0; i < 4; i++)
                {
                    var pawn = CreateMember(faction, batch, i == 0);
                    if (!GhostActions.CellLegal(map, cells[i])) throw new InvalidOperationException("Legion landing cell changed during creation");
                    GenSpawn.Spawn(pawn, cells[i], map);
                    NativePawnPhysiology.InitializeSpawned(pawn);
                    var marker = Hediff_LegionIdentity.For(pawn);
                    float speed = StatDefOf.MoveSpeed.Worker.GetValue(StatRequest.For(pawn));
                    if (speed <= 0) throw new InvalidOperationException("Legion healthy movement speed unavailable");
                    marker.speedScale = 4.4f / speed;
                    marker.templateVersion = 1; marker.templateMigration = "birth-after-native-icy";
                    pawn.health.Notify_HediffChanged(marker); StatDefOf.MoveSpeed.Worker.ClearCacheForThing(pawn);
                }
                LordMaker.MakeNewLord(faction, new LordJob_LegionAssault(), map, batch.members);
            }
            catch (Exception error)
            { owner.Retire(batch); Log.Error("[NMC] Seydas spawn transaction rolled back: " + error); Reject(NmcText.Get("NMC_Text0450")); return; }
            blocked = Unavailable(map);
            if (blocked != null || LegionPurchase.NativeFaction != faction)
            { owner.Retire(batch); Reject(blocked ?? NmcText.Get("NMC_Text0441")); return; }
            LegionPurchase.Payment payment;
            try { payment = new LegionPurchase.Payment(map); }
            catch (Exception error)
            { owner.Retire(batch); Log.Error("[NMC] Seydas goods transaction rolled back: " + error); Reject(NmcText.Get("NMC_Text0442")); return; }
            var ability = DefenseActions.Ability(16);
            using (payment)
            {
                if (!NativeLedger.TryPay(ability.powerCost, ability.volatileCost))
                { owner.Retire(batch); Reject(NmcText.Get("NMC_Text0443")); return; }
                payment.Commit();
            }
            batch.committed = true;
            CommanderState.Current.cooldowns[LegionPurchase.CooldownKey] = LegionPurchase.CooldownTicks;
            Messages.Message(NmcText.Get("NMC_Text0451", payment.Amount, payment.Currency.label), MessageTypeDefOf.PositiveEvent, false);
        }
        static Pawn CreateMember(Faction faction, LegionBatch batch, bool hero)
        {
            var request = new PawnGenerationRequest(DefDatabase<PawnKindDef>.GetNamed(hero ? "NMC_Seydas" : "NMC_DarkArchon"), faction,
                forceGenerateNewPawn: true, allowDead: false, allowDowned: false, canGeneratePawnRelations: false,
                mustBeCapableOfViolence: true, allowPregnant: false, allowFood: false, allowAddictions: false,
                fixedBiologicalAge: 25f, fixedChronologicalAge: 25f, fixedGender: Gender.Female,
                forceNoBackstory: true, forbidAnyTitle: true, dontGiveWeapon: true, forceNoGear: true);
            var pawn = PawnGenerator.GeneratePawn(request); batch.members.Add(pawn);
            NativePawnPhysiology.ClearTemplateTraits(pawn);
            pawn.story.bodyType = BodyTypeDefOf.Thin;
            if (hero) pawn.Name = new NameSingle("Seydas");
            foreach (var skill in pawn.skills.skills) { skill.Level = 0; skill.passion = Passion.None; }
            SetSkill(pawn, SkillDefOf.Melee, 2);
            var marker = (Hediff_LegionIdentity)HediffMaker.MakeHediff(DefDatabase<HediffDef>.GetNamed("NMC_LegionIdentity"), pawn);
            marker.batchId = batch.id; marker.hero = hero; marker.legionAbility = 16; pawn.health.AddHediff(marker);
            LegionFocus.Initialize(pawn, marker);
            var weapon = (ThingWithComps)MakeGear("NMC_ArchonFocus", pawn, batch);
            weapon.GetComp<CompQuality>()?.SetQuality(QualityCategory.Normal, ArtGenerationContext.Outsider);
            weapon.GetComp<CompBiocodable>()?.CodeFor(pawn); pawn.equipment.AddEquipment(weapon);
            foreach (string name in hero ? new[] { "NMC_SeydasRobe", "NMC_SeydasCirclet" } : new[] { "NMC_ArchonRobe", "NMC_ArchonCirclet" })
                pawn.apparel.Wear((Apparel)MakeGear(name, pawn, batch), false, false);
            return pawn;
        }
        static void SetSkill(Pawn pawn, SkillDef def, int level)
        { var skill = pawn.skills.GetSkill(def); skill.DirtyAptitudes(); skill.Level = level - skill.Aptitude; }
        static Thing MakeGear(string name, Pawn pawn, LegionBatch batch)
        {
            var gear = ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed(name)); batch.equipment.Add(gear);
            var marker = gear.TryGetComp<CompLegionEquipment>(); marker.batchId = batch.id; marker.originalPawnId = pawn.thingIDNumber; return gear;
        }
    }
}
