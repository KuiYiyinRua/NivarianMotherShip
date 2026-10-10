using System;
using System.Collections.Generic;
using System.Linq;
using Nivarian.NivarianDrones;
using Nivarian.NivarianDrones.NivarianDroneComps;
using RimWorld;
using UnityEngine;
using Verse;

namespace NivarianCommander
{
    public static class FleetActions
    {
        static readonly IntVec3[] Formation = {
            IntVec3.Zero, new IntVec3(-6,0,-6), new IntVec3(0,0,-6), new IntVec3(6,0,-6),
            new IntVec3(-6,0,0), new IntVec3(6,0,0), new IntVec3(-6,0,6), new IntVec3(0,0,6), new IntVec3(6,0,6)
        };
        public static IEnumerable<IntVec3> Cells(IntVec3 center) => Formation.Select(offset => center + offset);
        public static string Unavailable(Map map) => DefenseActions.Unavailable(map, 13);
        public static string PlacementError(Map map, IntVec3 center)
        {
            if (map == null || !Find.Maps.Contains(map)) return NmcText.Get("NMC_Text0034");
            foreach (var cell in Cells(center))
            {
                if (!cell.InBounds(map) || cell.x <= 0 || cell.z <= 0 || cell.x >= map.Size.x - 1 || cell.z >= map.Size.z - 1) return NmcText.Get("NMC_Text0264");
                if (cell.Fogged(map)) return NmcText.Get("NMC_Text0265");
                if (cell.GetRoof(map)?.isThickRoof == true) return NmcText.Get("NMC_Text0266");
                if (!cell.Standable(map) || cell.GetEdifice(map)?.def.passability == Traversability.Impassable) return NmcText.Get("NMC_Text0267");
                if (cell.GetThingList(map).Any(t => t is FleetShip)) return NmcText.Get("NMC_Text0268");
            }
            return null;
        }
        static void Reject(string reason)
        { CommanderState.Current.requestsRejected++; Messages.Message(reason, MessageTypeDefOf.RejectInput, false); }
        // MP synchronizes only this command. Re-evaluate all nine cells and the
        // shared ledger at execution, then spawn the whole transaction before pay.
        public static void Start(Map map, IntVec3 center)
        {
            string blocked = Unavailable(map) ?? PlacementError(map, center);
            if (blocked != null) { Reject(blocked); return; }
            FleetWarpState.Validate();
            var owner = CommanderFleets.Current;
            var batch = new FleetBatch { id = owner.nextId++, origin = map };
            owner.batches.Add(batch);
            try
            {
                int i = 0;
                foreach (var cell in Cells(center))
                {
                    var ship = (FleetShip)ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed(i == 0 ? "NMC_TacticalMothership" : "NMC_FleetDestroyer"));
                    ship.batchId = batch.id; ship.slot = i;
                    ship.Home = new LocalTargetInfo(cell); ship.State = DroneState.Running;
                    ship.SetFaction(Faction.OfPlayer);
                    batch.ships.Add(ship); batch.memberIds.Add(ship.thingIDNumber);
                    batch.arrived.Add(false); batch.lost.Add(false);
                    GenSpawn.Spawn(ship, cell, map, WipeMode.Vanish);
                    batch.memberIds[i] = ship.thingIDNumber;
                    var moving = ship.GetComp<NivarianDroneComp_MovingBase>();
                    var warp = ship.GetComp<NivarianDroneComp_Warp>();
                    if (!ship.Spawned || moving == null || warp == null) throw new InvalidOperationException("Fleet native movement/warp initialization failed");
                    var start = new IntVec3(Mathf.Clamp(cell.x, 1, map.Size.x - 2), 0, 1).ToVector3Shifted();
                    start.y = cell.ToVector3Shifted().y;
                    moving.PhysicPosition = start;
                    moving.Velocity = new Vector3(0, 0, i == 0 ? 3f : 8f);
                    warp.WarpAfterDelay(i * .12f, cell.ToVector3Shifted(), start, 2f, .2f, .8f, false);
                    ship.entryQueued = true; i++;
                }
            }
            catch (Exception error)
            {
                owner.Retire(batch);
                Log.Error("[NMC] Fleet spawn transaction rolled back: " + error);
                Reject(NmcText.Get("NMC_Text0269"));
                return;
            }
            var d = DefenseActions.Ability(13);
            if (!NativeLedger.TryPay(d.powerCost, d.volatileCost))
            { owner.Retire(batch); Reject(NmcText.Get("NMC_Text0270")); return; }
            batch.committed = true;
            CommanderState.Current.cooldowns[d.defName] = d.cooldownTicks;
            Messages.Message(NmcText.Get("NMC_Text0271"), MessageTypeDefOf.PositiveEvent, false);
        }
    }
}
