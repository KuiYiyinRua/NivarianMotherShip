using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using HarmonyLib;
using Nivarian.NivarianDrones;
using RimWorld;
using UnityEngine;
using Verse;

namespace NivarianCommander
{
    public sealed class DefenseRecord : IExposable
    {
        public Thing target;
        public float shield03, shield12;
        public int remaining03, remaining12, hitFlash, weaponWait, weaponShots;
        public float Total => shield03 + shield12;
        public void ExposeData()
        {
            Scribe_References.Look(ref target, "target");
            Scribe_Values.Look(ref shield03, "shield03"); Scribe_Values.Look(ref shield12, "shield12");
            Scribe_Values.Look(ref remaining03, "remaining03"); Scribe_Values.Look(ref remaining12, "remaining12");
            Scribe_Values.Look(ref hitFlash, "hitFlash");
            Scribe_Values.Look(ref weaponWait, "weaponWait"); Scribe_Values.Look(ref weaponShots, "weaponShots");
        }
        public float Absorb(float damage)
        {
            float original = damage;
            if (remaining03 > 0 && (remaining12 <= 0 || remaining03 <= remaining12))
            { Drain(ref shield03, ref damage); Drain(ref shield12, ref damage); }
            else { Drain(ref shield12, ref damage); Drain(ref shield03, ref damage); }
            if (damage < original) hitFlash = 12;
            return damage;
        }
        static void Drain(ref float pool, ref float damage)
        { float taken = Mathf.Min(pool, damage); pool -= taken; damage -= taken; }
    }

    public sealed class CommanderDefense : MapComponent
    {
        List<DefenseRecord> records = new List<DefenseRecord>();
        readonly List<DefenseRecord> shieldOverlayRecords = new List<DefenseRecord>();
        readonly HashSet<Pawn> tintedPawns = new HashSet<Pawn>();
        readonly HashSet<Pawn> nextTintedPawns = new HashSet<Pawn>();
        public CommanderDefense(Map map) : base(map) { }
        public IReadOnlyList<DefenseRecord> Records => records;
        public DefenseRecord For(Thing target) => records.FirstOrDefault(r => r.target == target);
        public void ClearInvalid()
        { if (records.RemoveAll(r => r.target == null || r.target.Destroyed || !r.target.Spawned || r.target.Map != map || !SupportDiplomacy.Friendly(r.target)) > 0) shieldOverlayDirty = true; }
        public static void ClearInvalidAcrossMaps()
        {
            if (Current.Game == null || Find.World == null) return;
            foreach (var map in Find.Maps) map.GetComponent<CommanderDefense>().ClearInvalid();
        }
        public void Refresh(Thing target, int source)
        {
            var r = For(target);
            if (r == null) { r = new DefenseRecord { target = target }; records.Add(r); shieldOverlayDirty = true; }
            if (source == 3) { r.shield03 = 200; r.remaining03 = 1800; }
            else { if (r.remaining12 <= 0) { r.weaponWait = 0; shieldOverlayDirty = true; } r.shield12 = 200; r.remaining12 = 2700; }
        }
        public void Cancel(int source, Thing target = null)
        {
            foreach (var r in records)
            {
                if (target != null && r.target != target) continue;
                if (source == 3) { r.shield03 = 0; r.remaining03 = 0; }
                else { r.shield12 = 0; r.remaining12 = 0; }
            }
        }
        public override void MapComponentTick()
        {
            for (int i = records.Count - 1; i >= 0; i--)
            {
                var r = records[i];
                if (r.target == null || r.target.Destroyed || !r.target.Spawned || r.target.Map != map || !SupportDiplomacy.Friendly(r.target))
                { records.RemoveAt(i); shieldOverlayDirty = true; continue; }
                if (r.remaining03 > 0 && --r.remaining03 == 0) r.shield03 = 0;
                if (r.remaining12 > 0 && --r.remaining12 == 0) r.shield12 = 0;
                if (r.remaining12 > 0 && r.target is Building) OverloadWeapon.Tick(r, map);
                if (r.hitFlash > 0) r.hitFlash--;
                if (r.remaining03 == 0 && r.remaining12 == 0) { records.RemoveAt(i); shieldOverlayDirty = true; }
            }
        }
        public override void ExposeData()
        {
            Scribe_Collections.Look(ref records, "nmcDefense", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit) { records = records ?? new List<DefenseRecord>(); shieldOverlayDirty = true; }
        }
        public override void MapComponentUpdate()
        {
            if (Find.CurrentMap != map) return;
            nextTintedPawns.Clear();
            foreach (var record in records)
                if (record.target is Pawn pawn && !pawn.Dead && DefenseShieldOverlay.Active(pawn)) nextTintedPawns.Add(pawn);
            foreach (var pawn in tintedPawns)
                if (!nextTintedPawns.Contains(pawn)) pawn.Drawer?.renderer?.SetAllGraphicsDirty();
            foreach (var pawn in nextTintedPawns)
                if (!tintedPawns.Contains(pawn)) pawn.Drawer?.renderer?.SetAllGraphicsDirty();
            tintedPawns.Clear(); tintedPawns.UnionWith(nextTintedPawns);
            if (shieldOverlayDirty)
            {
                shieldOverlayRecords.Clear();
                for (int i = 0; i < records.Count; i++)
                    if (records[i].target != null && DefenseShieldOverlay.PotentialType(records[i]))
                        shieldOverlayRecords.Add(records[i]);
                shieldOverlayDirty = false;
            }
            if (shieldOverlayRecords.Count == 0) return;
            var view = Find.CameraDriver.CurrentViewRect.ExpandedBy(1);
            for (int i = 0; i < shieldOverlayRecords.Count; i++)
            {
                var r = shieldOverlayRecords[i]; var target = r.target;
                if (target == null || target.Destroyed || !target.Spawned || target.Map != map
                    || !((r.remaining03 > 0 && r.shield03 > 0) || r.remaining12 > 0)
                    || !DefenseShieldOverlay.VisibleType(r)
                    || !view.Overlaps(target.OccupiedDrawRect()) || !SupportDiplomacy.Friendly(target)) continue;
                DefenseShieldOverlay.Draw(target);
            }
        }
        public static float Absorb(float amount, Thing target, DamageInfo dinfo)
        {
            if (target == null || !target.Spawned) return amount;
            if (!SupportDiplomacy.Friendly(target)) { target.Map.GetComponent<CommanderDefense>().ClearInvalid(); return amount; }
            if (amount <= 0 || !dinfo.Def.harmsHealth || dinfo.Def == DamageDefOf.EMP || dinfo.InstantPermanentInjury) return amount;
            var record = target.Map.GetComponent<CommanderDefense>().For(target);
            return record == null ? amount : record.Absorb(amount);
        }
        bool shieldOverlayDirty = true;
    }

    // Native drones are units implemented as ThingWithComps, not Pawn or Building.
    [HarmonyPatch(typeof(SupportDiplomacy), nameof(SupportDiplomacy.Friendly))]
    public static class NativeDroneDiplomacy
    {
        static bool Prefix(Thing thing, ref bool __result)
        {
            if (!(thing is NivarianDroneBase)) return true;
            var faction = thing.Faction;
            __result = faction != null && (faction == Faction.OfPlayer || faction.RelationKindWith(Faction.OfPlayer) == FactionRelationKind.Ally);
            return false;
        }
    }

    [HarmonyPatch(typeof(DamageWorker), nameof(DamageWorker.Apply))]
    public static class BuildingDefenseAfterFactors
    {
        static float Absorb(float amount, Thing target, DamageInfo info) => target is Building || target is NivarianDroneBase ? CommanderDefense.Absorb(amount, target, info) : amount;
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            int hits = 0;
            foreach (var c in instructions)
            {
                if (c.Calls(AccessTools.Method(typeof(GenMath), nameof(GenMath.RoundRandom), new[] { typeof(float) })))
                {
                    // Full damage after building factors, before HP clipping and rounding.
                    hits++; var first = new CodeInstruction(OpCodes.Ldarg_2); first.labels.AddRange(c.labels); c.labels.Clear();
                    yield return first; yield return new CodeInstruction(OpCodes.Ldarg_1);
                    yield return CodeInstruction.Call(typeof(BuildingDefenseAfterFactors), nameof(Absorb));
                }
                yield return c;
            }
            if (hits != 1) throw new InvalidOperationException("[NMC] Building post-factor shield boundary unresolved: " + hits);
        }
    }

    [HarmonyPatch(typeof(DamageWorker_AddInjury), "ApplyDamageToPart")]
    public static class PawnDefenseAfterArmor
    {
        sealed class Blocked { public bool fullyShielded; public float pendingAbsorbed; }
        static readonly System.Runtime.CompilerServices.ConditionalWeakTable<DamageWorker.DamageResult, Blocked> FullyShielded = new System.Runtime.CompilerServices.ConditionalWeakTable<DamageWorker.DamageResult, Blocked>();
        static float Absorb(float amount, Pawn target, DamageInfo info, DamageWorker.DamageResult result)
        {
            float left = CommanderDefense.Absorb(amount, target, info);
            if (amount > left)
            {
                var state = FullyShielded.GetValue(result, _ => new Blocked());
                if (left <= 0) state.fullyShielded = true;
                if (info.ApplyAllDamage) state.pendingAbsorbed += amount - left;
            }
            return left;
        }
        // Vanilla small-animal propagation revisits a parent body part even after a
        // fully deflected first hit. A hit absorbed by this shield must not spend twice.
        public static bool AllowSmallPropagation(DamageWorker.DamageResult result)
        { bool blocked = FullyShielded.TryGetValue(result, out var state) && state.fullyShielded; FullyShielded.Remove(result); return !blocked; }
        public static float AccountAllDamage(DamageWorker.DamageResult result, float ordinary)
        {
            if (!FullyShielded.TryGetValue(result, out var state)) return ordinary;
            float spent = state.pendingAbsorbed; state.pendingAbsorbed = 0;
            return ordinary + spent;
        }
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var codes = instructions.ToList();
            int armor = codes.FindIndex(c => c.Calls(AccessTools.Method(typeof(ArmorUtility), nameof(ArmorUtility.GetPostArmorDamage))));
            int boundary = -1;
            for (int i = armor + 1; armor >= 0 && i < codes.Count - 1; i++)
                if (codes[i].opcode == OpCodes.Ldc_R4 && Equals(codes[i].operand, 0f) && codes[i - 1].IsLdloc()) { boundary = i - 1; break; }
            if (boundary < 0) throw new InvalidOperationException("[NMC] Pawn post-armor shield boundary unresolved");
            var load = codes[boundary].Clone(); load.labels.Clear(); load.blocks.Clear();
            load.labels.AddRange(codes[boundary].labels); codes[boundary].labels.Clear();
            int slot = codes[boundary].LocalIndex();
            codes.InsertRange(boundary, new[] { load, new CodeInstruction(OpCodes.Ldarg_2), new CodeInstruction(OpCodes.Ldarg_1), new CodeInstruction(OpCodes.Ldarg_3), CodeInstruction.Call(typeof(PawnDefenseAfterArmor), nameof(Absorb)), new CodeInstruction(OpCodes.Stloc, slot) });
            return codes;
        }
    }
    [HarmonyPatch(typeof(DamageWorker_AddInjury), "ApplySmallPawnDamagePropagation")]
    public static class ShieldedSmallPawnPropagation
    { static bool Prefix(DamageWorker.DamageResult result) => PawnDefenseAfterArmor.AllowSmallPropagation(result); }
    [HarmonyPatch(typeof(DamageWorker_AddInjury), "ApplyToPawn")]
    public static class ShieldAllDamageLoop
    {
        static System.Collections.Generic.IEnumerable<CodeInstruction> Transpiler(System.Collections.Generic.IEnumerable<CodeInstruction> instructions)
        {
            var codes = instructions.ToList(); int hits = 0;
            var dealt = AccessTools.Field(typeof(DamageWorker.DamageResult), nameof(DamageWorker.DamageResult.totalDamageDealt));
            for (int i = 0; i < codes.Count; i++)
            {
                var c = codes[i];
                if (c.opcode == OpCodes.Ldfld && Equals(c.operand, dealt) && i + 1 < codes.Count && codes[i + 1].opcode == OpCodes.Sub)
                {
                    hits++; var duplicate = new CodeInstruction(OpCodes.Dup); duplicate.labels.AddRange(c.labels); c.labels.Clear();
                    yield return duplicate; yield return c;
                    yield return CodeInstruction.Call(typeof(PawnDefenseAfterArmor), nameof(PawnDefenseAfterArmor.AccountAllDamage));
                }
                else yield return c;
            }
            if (hits != 1) throw new InvalidOperationException("[NMC] ApplyAllDamage shield accounting boundary unresolved: " + hits);
        }
    }
    [HarmonyPatch(typeof(Thing), nameof(Thing.SetFaction))]
    public static class DefenseFactionChanged { static void Postfix() => CommanderDefense.ClearInvalidAcrossMaps(); }
    [HarmonyPatch(typeof(Thing), nameof(Thing.SetFactionDirect))]
    public static class DefenseFactionChangedDirect { static void Postfix() => CommanderDefense.ClearInvalidAcrossMaps(); }
    [HarmonyPatch(typeof(Faction), nameof(Faction.Notify_RelationKindChanged))]
    public static class DefenseDiplomacyChanged { static void Postfix() => CommanderDefense.ClearInvalidAcrossMaps(); }
    [HarmonyPatch(typeof(Pawn_GuestTracker), nameof(Pawn_GuestTracker.SetGuestStatus))]
    public static class DefensePrisonerChanged { static void Postfix() => CommanderDefense.ClearInvalidAcrossMaps(); }
}
