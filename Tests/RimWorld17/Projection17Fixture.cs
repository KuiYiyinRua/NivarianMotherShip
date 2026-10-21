using System.Linq;
using HarmonyLib;
using Nivarian_Race.Code.Features.ComputeNetwork;
using RimWorld;
using Verse;

namespace NMC.RimWorld17Fixtures
{
    // 1.7 fixed hubs need device bandwidth. Supply a real native host, retaining
    // its reservation ledger rather than bypassing the new launch rules.
    internal static class Projection17Fixture
    {
        internal static void EnsureCompute()
        {
            var map=Find.CurrentMap;
            var def=ThingDef.Named("Nivarian_ComputeHost");
            var host=map.listerThings.ThingsOfDef(def).FirstOrDefault();
            if(host==null){
                var at=map.Center+new IntVec3(50,0,50);
                foreach(var c in CellRect.CenteredOn(at,3).ClipInsideMap(map)){
                    foreach(var thing in c.GetThingList(map).ToArray())if(!(thing is Pawn)){
                        if(thing.def.destroyable)thing.Destroy();else thing.DeSpawn();
                    }
                    map.terrainGrid.SetTerrain(c,TerrainDefOf.Concrete);
                }
                host=ThingMaker.MakeThing(def);host.SetFaction(Faction.OfPlayer);GenSpawn.Spawn(host,at,map);
                host.TryGetComp<CompRefuelable>()?.Refuel(3f);
            }
            AccessTools.Field(typeof(CompPowerTrader),"powerOnInt").SetValue(host.TryGetComp<CompPowerTrader>(),true);
            var network=GameComp_NivarianCompute.Instance;
            network.SetAllocation(0,100);
            AccessTools.Method(typeof(GameComp_NivarianCompute),"RefreshProvidersNow").Invoke(network,null);
            if(!network.Ready || network.DeviceBandwidthTotal<3)throw new System.Exception("Native compute fixture did not become ready");
        }
    }
}
