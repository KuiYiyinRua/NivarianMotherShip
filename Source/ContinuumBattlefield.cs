using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.BaseGen;
using UnityEngine;
using Verse;
using Verse.AI.Group;

namespace NivarianCommander
{
    public sealed class ContinuumBattlefield : MapComponent
    {
        public int stage, elapsed, waves;
        public bool hadPlayer, cleared, retreated;
        public bool airReinforcementsSpawned;
        public Thing relay;
        public List<Thing> defenders = new List<Thing>();
        public ContinuumBattlefield(Map map) : base(map) { }
        public override void MapComponentTick()
        {
            if (stage <= 0 || cleared || retreated) return;
            bool playerHere = map.mapPawns.AllPawnsSpawned.Any(p => p.Faction == Faction.OfPlayer && !p.Dead);
            if (playerHere) hadPlayer = true;
            if (!hadPlayer) return;
            elapsed++;
            if (!playerHere && elapsed > 600) { retreated = true; return; }
            if (playerHere && stage >= 6 && !airReinforcementsSpawned && elapsed % 60 == 1)
                airReinforcementsSpawned = ContinuumAirReinforcements.TrySpawn(this, map);
            bool coreGone = relay == null || relay.Destroyed || !relay.Spawned || !relay.HostileTo(Faction.OfPlayer);
            bool guarded = stage >= 6 && !airReinforcementsSpawned || defenders.Any(t => t != null && !t.Destroyed && t.HostileTo(Faction.OfPlayer)
                && (!(t is Pawn p) || (!p.Dead && !p.Downed)));
            if (coreGone && !guarded) { cleared = true; return; }
            if (waves < 1 + stage / 3 && elapsed >= 1800 + waves * 2400)
            {
                waves++;
                // Even the introductory wave contains both a Nira and a vanilla mechanoid.
                var faction = map.ParentFaction ?? Faction.OfAncientsHostile;
                var units = ContinuumSiteGenerator.MakeGuards(stage, Math.Max(2, 1 + stage / 2), faction);
                IntVec3 cell = CellFinder.RandomClosewalkCellNear(map.Center, map, 16);
                defenders.AddRange(units);
                DropPodUtility.DropThingsNear(cell, map, units.Cast<Thing>().ToList(), 110, canRoofPunch: true, forbid: false);
                LordMaker.MakeNewLord(faction, new LordJob_AssaultColony(faction, canKidnap: false, canTimeoutOrFlee: false), map, units);
                Messages.Message(NmcText.Get("NMC_Text0214"), new TargetInfo(cell, map), MessageTypeDefOf.ThreatSmall, false);
            }
        }
        public override void ExposeData()
        {
            base.ExposeData(); Scribe_Values.Look(ref stage, "nmcBaseStage"); Scribe_Values.Look(ref elapsed, "nmcBaseElapsed");
            Scribe_Values.Look(ref waves, "nmcBaseWaves"); Scribe_Values.Look(ref hadPlayer, "nmcBaseHadPlayer");
            Scribe_Values.Look(ref cleared, "nmcBaseCleared"); Scribe_Values.Look(ref retreated, "nmcBaseRetreated");
            Scribe_Values.Look(ref airReinforcementsSpawned, "nmcBaseAirReinforcementsSpawned");
            Scribe_References.Look(ref relay, "nmcBaseRelay"); Scribe_Collections.Look(ref defenders, "nmcBaseDefenders", LookMode.Reference);
            if (Scribe.mode == LoadSaveMode.PostLoadInit) defenders = defenders ?? new List<Thing>();
        }
    }

    // Existing saves keep their generated map. New sites use the layered layouts.
    public sealed class ContinuumSiteGenerator : GenStep
    {
        public override int SeedPart => 94101937;
        public override void Generate(Map map, GenStepParams parms)
        {
            int stage = Math.Max(1, ContinuumCampaign.Current?.activeStage ?? 1);
            var state = map.GetComponent<ContinuumBattlefield>(); state.stage = stage;
            Faction faction = map.ParentFaction ?? Faction.OfAncientsHostile;
            ContinuumFortifications.Generate(map, stage, state, faction);
        }
        public static List<Pawn> MakeGuards(int stage, int count, Faction faction)
        {
            var result = new List<Pawn>();
            for (int i = 0; i < count; i++)
            {
                bool nira = i % 3 == 0;
                var kind = DefDatabase<PawnKindDef>.GetNamedSilentFail(nira ? "Nivarian_MechUnit_Colonist" : stage >= 5 && i % 3 == 2 ? "Mech_CentipedeGunner" : stage >= 3 ? "Mech_Lancer" : "Mech_Militor")
                    ?? DefDatabase<PawnKindDef>.GetNamed("Mech_Scyther");
                nira = kind.defName == "Nivarian_MechUnit_Colonist";
                var pawn = PawnGenerator.GeneratePawn(new PawnGenerationRequest(kind, faction, forceGenerateNewPawn: true, canGeneratePawnRelations: false,
                    mustBeCapableOfViolence: true, allowDead: false, allowDowned: false));
                if (nira && pawn.equipment != null)
                {
                    // This native kind was authored for player colonists; its work
                    // components also require initialization when used as an enemy.
                    pawn.workSettings?.EnableAndInitialize();
                    pawn.equipment.DestroyAllEquipment();
                    var weapon = (ThingWithComps)ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("Nivarian_FrontierRifle"));
                    pawn.equipment.AddEquipment(weapon);
                }
                result.Add(pawn);
            }
            return result;
        }
    }
}
