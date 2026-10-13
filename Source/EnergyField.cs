using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Nivarian;
using Nivarian.NivarianDrones.NivarianDroneComps;
using Nivarian_Race.Code.Needs;
using Nivarian_Race.Code.Comps.BuildingComps;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace NivarianCommander
{
    public sealed class CommanderField : WorldComponent
    {
        public Map fieldMap;
        public IntVec3 center = IntVec3.Invalid;
        public int revision;
        public int upkeepRemaining = FieldEconomy.UpkeepInterval;
        static readonly ConditionalWeakTable<World, CommanderField> worlds = new ConditionalWeakTable<World, CommanderField>();
        public CommanderField(World world) : base(world)
        { if (world != null) { worlds.Remove(world); worlds.Add(world, this); } }
        public static CommanderField Current
        {
            get
            {
                var world = Find.World;
                return world == null ? null : worlds.TryGetValue(world, out var currentField) ? currentField : world.GetComponent<CommanderField>();
            }
        }
        public bool Exists => fieldMap != null && Find.Maps.Contains(fieldMap) && center.IsValid && center.InBounds(fieldMap);
        public bool Contains(Thing t) => t != null && t.Spawned
            && (t.Map == fieldMap && center.IsValid && t.Position.InHorDistOf(center, 8f)
                || WarpPrismFields.For(t.Map).Covers(t.Position))
            && !ProjectionRegistry.IsProjection(t) && SupportDiplomacy.Friendly(t);
        public void Deploy(Map map, IntVec3 cell)
        {
            if (!Exists) upkeepRemaining = FieldEconomy.UpkeepInterval;
            fieldMap = map; center = cell; revision++;
        }
        public void Remove() { fieldMap = null; center = IntVec3.Invalid; upkeepRemaining = FieldEconomy.UpkeepInterval; revision++; }
        public override void ExposeData()
        {
            Scribe_References.Look(ref fieldMap, "nmcFieldMap");
            Scribe_Values.Look(ref center, "nmcFieldCenter", IntVec3.Invalid);
            Scribe_Values.Look(ref revision, "nmcFieldRevision");
            Scribe_Values.Look(ref upkeepRemaining, "nmcFieldUpkeepRemaining", FieldEconomy.UpkeepInterval);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
                upkeepRemaining = Mathf.Clamp(upkeepRemaining, 1, FieldEconomy.UpkeepInterval);
        }
        public override void WorldComponentTick()
        {
            if (fieldMap != null)
            {
                if (!Exists) Remove();
                else if (--upkeepRemaining <= 0)
                {
                    if (NativeLedger.TryPay(1, 1)) upkeepRemaining = FieldEconomy.UpkeepInterval;
                    else
                    {
                        var target = new TargetInfo(center, fieldMap);
                        Remove();
                        Messages.Message(NmcText.Get("NMC_Text0244"), target, MessageTypeDefOf.NegativeEvent, false);
                    }
                }
            }
            CommanderDefense.ClearInvalidAcrossMaps();
        }
    }
    public sealed class FieldPowerLease : IExposable
    {
        public ThingWithComps target;
        public bool previous;
        public bool previousWireless;
        public void ExposeData() { Scribe_References.Look(ref target, "target"); Scribe_Values.Look(ref previous, "previous"); Scribe_Values.Look(ref previousWireless, "previousWireless"); }
    }
    public sealed partial class FieldEnergy : MapComponent
    {
        public const float Budget = 10000f;
        public List<FieldPowerLease> leases = new List<FieldPowerLease>();
        // Consumer identities survive a paused-map save. PowerNet objects do not.
        public List<ThingWithComps> gridLeases = new List<ThingWithComps>();
        // A powered wireless adapter is not proof that its native gauge paid for it.
        // Preserve source ownership through paused-map saves and cold joins.
        public List<ThingWithComps> wirelessOverrides = new List<ThingWithComps>();
        // Saved lists retain their order. Query indexes change at the same lease
        // boundaries and are rebuilt after reference resolution, never by a getter.
        readonly Dictionary<ThingWithComps, FieldPowerLease> leaseIndex = new Dictionary<ThingWithComps, FieldPowerLease>();
        readonly HashSet<ThingWithComps> gridLeaseIndex = new HashSet<ThingWithComps>();
        readonly HashSet<ThingWithComps> wirelessIndex = new HashSet<ThingWithComps>();
        static readonly ConditionalWeakTable<Map, FieldEnergy> maps = new ConditionalWeakTable<Map, FieldEnergy>();
        internal bool HasPowerLeases => leases.Count != 0 || gridLeases.Count != 0;
        readonly Dictionary<PowerNet, float> netWatts = new Dictionary<PowerNet, float>();
        readonly Dictionary<CompPowerTrader, float> directWatts = new Dictionary<CompPowerTrader, float>();
        readonly HashSet<string> unsupported = new HashSet<string>();
        int cacheRevision = -1, cachePrismRevision = -1, chargeClock;
        bool CacheCurrent => cacheRevision == (CommanderField.Current?.revision ?? -1)
            && cachePrismRevision == WarpPrismFields.For(map).Revision;
        bool restoring;
        readonly FieldGroundOverlay groundOverlay = new FieldGroundOverlay();
        static readonly Action<Need_NiraEnergy> UpdateNira = AccessTools.MethodDelegate<Action<Need_NiraEnergy>>(AccessTools.Method(typeof(Need_NiraEnergy), "UpdateHediff"));
        static readonly System.Reflection.FieldInfo MechShutdown = AccessTools.Field(typeof(Need_MechEnergy), "selfShutdown");
        static readonly AccessTools.FieldRef<CompPowerTrader, bool> RawPowerOn = AccessTools.FieldRefAccess<CompPowerTrader, bool>("powerOnInt");
        public FieldEnergy(Map map) : base(map)
        { if (map != null) { maps.Remove(map); maps.Add(map, this); } }
        public static FieldEnergy For(Map map)
            => map == null ? null : maps.TryGetValue(map, out var energy) ? energy : map.GetComponent<FieldEnergy>();
        public float AllocatedWatts => netWatts.Values.Sum() + directWatts.Values.Sum();
        public float NetWatts(PowerNet net) => Active && CacheCurrent && netWatts.TryGetValue(net, out float w) ? w : 0f;
        public bool Active
        { get { var currentField = CommanderField.Current; return currentField != null
            && (currentField.fieldMap == map && currentField.Exists || WarpPrismFields.For(map).HasActive); } }
        static bool Working(CompPowerTrader comp) => comp?.parent?.Spawned == true && FlickUtility.WantsToBeOn(comp.parent) && !comp.parent.IsBrokenDown() && !comp.parent.Map.gameConditionManager.ElectricityDisabled(comp.parent.Map) && !EMP(comp);
        bool Eligible(CompPowerTrader comp) => Working(comp) && SupportDiplomacy.Friendly(comp.parent);
        static bool EMP(CompPowerTrader comp)
        { var stun = comp.parent.GetComp<CompStunnable>()?.StunHandler; return stun != null && stun.Stunned && stun.StunFromEMP; }
        public bool DirectGrant(CompPowerTrader comp)
        {
            if (restoring) return false;
            var field = CommanderField.Current;
            return field != null && Active && comp.PowerNet == null && field.Contains(comp.parent) && Eligible(comp)
                && (cacheRevision == -1 ? HasLease(comp) : CacheCurrent && directWatts.TryGetValue(comp, out float w) && w > 0);
        }
        public bool HasLease(CompPowerTrader comp) => leaseIndex.ContainsKey(comp.parent);
        void AddLease(FieldPowerLease lease)
        { leases.Add(lease); if (lease.target != null && !leaseIndex.ContainsKey(lease.target)) leaseIndex.Add(lease.target, lease); }
        void RemoveLeaseAt(int index)
        {
            var target = leases[index].target; leases.RemoveAt(index);
            if (target == null) return;
            leaseIndex.Remove(target);
            for (int i = 0; i < leases.Count; i++)
                if (leases[i].target == target) { leaseIndex.Add(target, leases[i]); break; }
        }
        void RemoveGridLeaseAt(int index)
        {
            var target = gridLeases[index]; gridLeases.RemoveAt(index);
            if (!gridLeases.Contains(target)) gridLeaseIndex.Remove(target);
        }
        void RebuildLeaseIndexes()
        {
            leaseIndex.Clear(); gridLeaseIndex.Clear(); wirelessIndex.Clear();
            foreach (var lease in leases)
                if (lease.target != null && !leaseIndex.ContainsKey(lease.target)) leaseIndex.Add(lease.target, lease);
            foreach (var target in gridLeases) gridLeaseIndex.Add(target);
            foreach (var target in wirelessOverrides) wirelessIndex.Add(target);
        }
        static bool NativeWireReady(CompPowerTrader comp)
        {
            var adapter = comp?.parent?.GetComp<Comp_NivarianWirelessPowerAdapter>();
            var wireless = GameComp_NivarianGlobalPowerTransmitter.Instance;
            return adapter?.WirelessMode == true && Working(comp) && comp.EnergyOutputPerTick < 0f && wireless != null
                && DefDatabase<ResearchProjectDef>.GetNamedSilentFail("Nivarian_DirectionalPowerTransmission")?.IsFinished == true
                && wireless.UnreservedEnergy >= -comp.EnergyOutputPerTick;
        }
        bool NativeWireFunded(CompPowerTrader comp) => !wirelessIndex.Contains(comp.parent) && RawPowerOn(comp) && NativeWireReady(comp);
        public bool RestorePrevious(CompPowerTrader comp)
        {
            leaseIndex.TryGetValue(comp.parent, out var lease);
            return Working(comp) && (NativeWireReady(comp) || (lease?.previous == true && !lease.previousWireless));
        }
        public bool OwnWirelessSupply(CompPowerTrader comp)
        {
            if (comp == null || !wirelessIndex.Contains(comp.parent) || !Active || !Eligible(comp)) return false;
            if (comp.PowerNet == null)
                return CommanderField.Current.Contains(comp.parent) && (!CacheCurrent ? HasLease(comp) : DirectGrant(comp));
            return HasFieldConnector(comp.PowerNet) && (!CacheCurrent || NetWatts(comp.PowerNet) > 0f);
        }
        bool HasFieldConnector(PowerNet net)
        {
            if (net == null) return false;
            var field = CommanderField.Current;
            if (field == null || !Active || map.gameConditionManager.ElectricityDisabled(map)) return false;
            return LiveConnector(net, field);
        }
        static bool WorkingConnector(CompPower cp, CommanderField field)
            => field.Contains(cp.parent) && FlickUtility.WantsToBeOn(cp.parent) && !cp.parent.IsBrokenDown()
                && (!(cp is CompPowerTrader p) || !EMP(p)) && (!(cp is CompPowerBattery b) || !b.StunnedByEMP);
        bool HasNativeSource(PowerNet net)
        {
            if (net == null) return false;
            if (net.batteryComps.Any(b => !b.StunnedByEMP && b.StoredEnergy > 0f)) return true;
            float balance = 0f;
            foreach (var p in WorkFor(net).Ordered(net))
            {
                if (!Working(p) || !RawPowerOn(p)) continue;
                balance += p.EnergyOutputPerTick;
                // Native wireless cancels only this adapter's consumption. It cannot
                // be counted as an unlimited source for the remaining consumers.
                if (p.EnergyOutputPerTick < 0f && NativeWireReady(p)) balance -= p.EnergyOutputPerTick;
            }
            return balance >= -.0000001f;
        }
        public bool GridSupplyLost(CompPowerTrader comp)
        {
            if (!gridLeaseIndex.Contains(comp.parent) || ProjectionRegistry.IsProjection(comp.parent)) return false;
            if (!Working(comp)) return true;
            return !HasFieldConnector(comp.PowerNet) && !NativeWireReady(comp) && !HasNativeSource(comp.PowerNet);
        }
        sealed class Demand
        {
            public PowerNet net;
            public float missing, watts;
            public int id;
            public bool battery;
        }
        public void Rebuild()
        {
            if (!Active && leases.Count == 0 && gridLeases.Count == 0 && wirelessOverrides.Count == 0 && netWatts.Count == 0) return;
            netWatts.Clear(); directWatts.Clear();
            cacheRevision = CommanderField.Current?.revision ?? -1;
            cachePrismRevision = WarpPrismFields.For(map).Revision;
            var requests = powerRequests; requests.Clear();
            var offGrid = offGridWork; offGrid.Clear();
            var nets = powerNetsWork; nets.Clear();
            if (Active && !map.gameConditionManager.ElectricityDisabled(map))
            {
                CollectFieldThings(false);
                var currentField = CommanderField.Current;
                foreach (var b in nearbyBuildings)
                {
                    if (!currentField.Contains(b)) continue;
                    var power = b.GetComp<CompPower>();
                    if (power == null) continue;
                    if (power is CompPowerTrader trader)
                    {
                        if (!Eligible(trader)) continue;
                        if (trader.PowerNet != null) nets.Add(trader.PowerNet);
                        else if (trader.PowerOutput < 0 && !NativeWireFunded(trader)) offGrid.Add(trader);
                    }
                    else if (power.PowerNet != null && (!(power is CompPowerBattery battery) || !battery.StunnedByEMP)) nets.Add(power.PowerNet);
                }
                // A network is connected by a friendly in-circle building. Its normal loads
                // may extend outside the circle; generators and existing wireless supplies count.
                nativeWirelessWork.Clear();
                var wireless = GameComp_NivarianGlobalPowerTransmitter.Instance;
                if (wireless != null)
                    foreach (var adapter in wireless.ActiveAdapters)
                    { var p = adapter.parent.GetComp<CompPowerTrader>(); if (p != null) nativeWirelessWork.Add(p); }
                nativeWirelessWork.Sort(ByPowerId);
                foreach (var net in nets)
                {
                    var work = WorkFor(net);
                    float consumption = 0, generation = 0;
                    int key = int.MaxValue;
                    foreach (var transmitter in net.transmitters) key = Math.Min(key, transmitter.parent.thingIDNumber);
                    foreach (var comp in work.Ordered(net))
                    {
                        key = Math.Min(key, comp.parent.thingIDNumber);
                        if (comp.PowerOutput >= 0) { if (comp.PowerOn) generation += comp.PowerOutput; }
                        else if (FlickUtility.WantsToBeOn(comp.parent) && !comp.parent.IsBrokenDown() && !EMP(comp)) consumption -= comp.PowerOutput;
                    }
                    foreach (var p in nativeWirelessWork)
                        if (p.PowerNet == net && p.PowerOutput < 0 && NativeWireFunded(p)) generation -= p.PowerOutput;
                    var demand = work.demand;
                    demand.net = net; demand.id = key; demand.missing = Mathf.Max(0, consumption - generation);
                    demand.battery = net.batteryComps.Any(b => !b.StunnedByEMP && b.AmountCanAccept > 0f);
                    requests.Add(demand);
                }
            }
            requests.Sort((a, b) => a.id.CompareTo(b.id));
            offGrid.Sort((a, b) => a.parent.thingIDNumber.CompareTo(b.parent.thingIDNumber));
            float directDemand = offGrid.Sum(p => -p.PowerOutput);
            float totalDemand = requests.Sum(r => r.missing) + directDemand;
            float scale = totalDemand > Budget ? Budget / totalDemand : 1f;
            foreach (var r in requests) r.watts = r.missing * scale;
            // Off-grid loads are indivisible. Use their group's proportional allocation
            // in a stable order and return unused allocation to chargeable networks.
            float directRemaining = directDemand * scale;
            foreach (var p in offGrid)
            { float need = -p.PowerOutput; if (need <= directRemaining + .0001f) { directWatts[p] = need; directRemaining -= need; } }
            float spare = Mathf.Max(0, Budget - requests.Sum(r => r.watts) - directWatts.Values.Sum());
            int batteryCount = requests.Count(r => r.battery);
            foreach (var r in requests)
            { if (r.battery && batteryCount > 0) r.watts += spare / batteryCount; if (r.watts > 0) netWatts[r.net] = r.watts; }
            // All native setter callbacks execute in this map's simulation context.
            // Replacing a field on another map never runs callbacks in the old map here.
            restoring = true;
            for (int i = leases.Count - 1; i >= 0; i--)
            {
                var l = leases[i]; var p = l.target?.GetComp<CompPowerTrader>();
                if (p != null && directWatts.ContainsKey(p)) continue;
                if (p != null && l.target.Spawned && l.target.Map == map && p.PowerNet == null) p.PowerOn = RestorePrevious(p);
                RemoveLeaseAt(i);
            }
            restoring = false;
            foreach (var pair in directWatts)
            {
                var p = pair.Key;
                if (!HasLease(p)) AddLease(new FieldPowerLease { target = p.parent, previous = p.PowerOn, previousWireless = p.parent.GetComp<Comp_NivarianWirelessPowerAdapter>()?.WirelessMode == true });
                p.PowerOn = true;
            }
            // Queries only mask lost field power. Emit native stop callbacks here, in
            // the owning map's next tick, and leave existing generators/batteries alone.
            for (int i = gridLeases.Count - 1; i >= 0; i--)
            {
                var t = gridLeases[i]; var p = t?.GetComp<CompPowerTrader>();
                // A projected transmitter may share the lamp network, but borrows
                // power only from its original. Drop legacy leases without callbacks.
                if (ProjectionRegistry.IsProjection(t)) { RemoveGridLeaseAt(i); continue; }
                if (p != null && t.Spawned && t.Map == map && p.PowerNet != null && netWatts.ContainsKey(p.PowerNet)) continue;
                if (p != null && t.Spawned && t.Map == map && GridSupplyLost(p)) p.PowerOn = false;
                RemoveGridLeaseAt(i);
            }
            foreach (var net in netWatts.Keys)
                foreach (var p in WorkFor(net).Ordered(net))
                    if (!ProjectionRegistry.IsProjection(p.parent) && p.Props.PowerConsumption > 0f && gridLeaseIndex.Add(p.parent)) gridLeases.Add(p.parent);
            var ownedWireless = nextWireless; ownedWireless.Clear(); ownedWirelessWork.Clear();
            suppliedPowerWork.Clear(); suppliedPowerWork.AddRange(directWatts.Keys);
            foreach (var net in netWatts.Keys) suppliedPowerWork.AddRange(net.powerComps);
            suppliedPowerWork.Sort(ByPowerId);
            foreach (var p in suppliedPowerWork)
                if (p.PowerOutput < 0f && p.parent.GetComp<Comp_NivarianWirelessPowerAdapter>() != null && !NativeWireFunded(p) && ownedWirelessWork.Add(p.parent)) ownedWireless.Add(p.parent);
            nextWireless = wirelessOverrides;
            wirelessOverrides = ownedWireless;
            wirelessIndex.Clear();
            foreach (var target in wirelessOverrides) wirelessIndex.Add(target);
        }
        public void PreTick()
        {
            if (!Active) return;
            if (!CacheCurrent) Rebuild();
            if (chargeClock > 0) { chargeClock--; return; }
            chargeClock = 14;
            CollectFieldThings(true);
            var currentField = CommanderField.Current;
            foreach (var t in nearbyChargers)
            {
                if (!currentField.Contains(t)) continue;
                if (t is Pawn p && !p.Dead && p.needs != null)
                {
                    // Native defs can attach this Need to vanilla mechs too. Its native
                    // ShowOnNeedList gate checks the actual Nira race and disabled state.
                    var nira = p.needs.AllNeeds.OfType<Need_NiraEnergy>().FirstOrDefault(n => n.ShowOnNeedList);
                    if (nira != null) { nira.CurLevel = Mathf.Min(nira.MaxLevel, nira.CurLevel + .05f * nira.MaxLevel); UpdateNira(nira); continue; }
                    var mech = p.needs.AllNeeds.OfType<Need_MechEnergy>().FirstOrDefault();
                    if (mech != null)
                    {
                        mech.CurLevel = Mathf.Min(mech.MaxLevel, mech.CurLevel + .05f * mech.MaxLevel);
                        if (mech.CurLevel >= 15f && (bool)MechShutdown.GetValue(mech))
                        {
                            MechShutdown.SetValue(mech, false);
                            var h = p.health.hediffSet.GetFirstHediffOfDef(HediffDefOf.SelfShutdown);
                            if (h != null) { p.health.RemoveHediff(h); p.jobs?.CheckForJobOverride(); }
                        }
                        continue;
                    }
                    if (p.RaceProps.IsMechanoid || p.needs.AllNeeds.Any(n => !(n is Need_NiraEnergy) && n.ShowOnNeedList && n.def.defName.IndexOf("Energy", StringComparison.OrdinalIgnoreCase) >= 0)) Report(t, "no supported mechanical energy Need");
                }
                else if (t is Nivarian.NivarianDrones.NivarianDroneBase drone)
                {
                    var battery = drone.GetComp<NivarianDroneComp_BatteryBase>()?.GetBattery;
                    if (battery != null) battery.Add(.05f * battery.Size);
                    else Report(t, "no native battery component");
                }
            }
        }
        void Report(Thing t, string reason)
        { string key = t.ThingID + ":" + t.GetType().FullName; if (unsupported.Add(key)) Log.Warning("[NMC] Field energy backend unsupported: " + key + " def=" + t.def.defName + " " + reason); }
        public override void ExposeData()
        {
            Scribe_Collections.Look(ref leases, "nmcFieldPowerLeases", LookMode.Deep);
            Scribe_Collections.Look(ref gridLeases, "nmcFieldGridLeases", LookMode.Reference);
            Scribe_Collections.Look(ref wirelessOverrides, "nmcFieldWirelessOverrides", LookMode.Reference);
            Scribe_Values.Look(ref chargeClock, "nmcFieldChargeClock");
            if (Scribe.mode == LoadSaveMode.PostLoadInit) { leases = leases ?? new List<FieldPowerLease>(); gridLeases = gridLeases ?? new List<ThingWithComps>(); wirelessOverrides = wirelessOverrides ?? new List<ThingWithComps>(); RebuildLeaseIndexes(); netWatts.Clear(); directWatts.Clear(); cacheRevision = -1; }
        }
        public override void MapComponentUpdate()
        {
            if (CommanderField.Current?.Exists != true || CommanderField.Current.fieldMap != map)
            { groundOverlay.Clear(); return; }
            if (Find.CurrentMap == map) groundOverlay.Draw(map, CommanderField.Current.center);
        }
        public override void MapRemoved() => groundOverlay.Clear();
    }
    [HarmonyPatch(typeof(Map), nameof(Map.MapPreTick))]
    public static class FieldChargingTick { static void Prefix(Map __instance) => FieldEnergy.For(__instance).PreTick(); }
    [HarmonyPatch(typeof(Map), nameof(Map.MapPostTick))]
    public static class FieldPowerTick { static void Prefix(Map __instance) => FieldEnergy.For(__instance).Rebuild(); }
    [HarmonyPatch(typeof(PowerNet), nameof(PowerNet.CurrentEnergyGainRate))]
    public static class FieldGridPower
    {
        [HarmonyPriority(Priority.Normal)]
        static void Postfix(PowerNet __instance, ref float __result) => __result += FieldEnergy.For(__instance.Map).NetWatts(__instance) * CompPower.WattsToWattDaysPerTick;
    }
    [HarmonyPatch(typeof(CompPowerTrader), nameof(CompPowerTrader.PowerOn), MethodType.Setter)]
    public static class FieldHoldOffGridPower
    {
        static bool Prefix(CompPowerTrader __instance, bool value)
            => value || FieldEnergy.For(__instance.parent.Map)?.DirectGrant(__instance) != true;
    }
    [HarmonyPatch(typeof(CompPowerTrader), nameof(CompPowerTrader.PowerOn), MethodType.Getter)]
    public static class FieldLogicalPower
    {
        static bool Prefix(CompPowerTrader __instance, ref bool __result)
        {
            var energy = FieldEnergy.For(__instance.parent.Map);
            if (energy == null || !energy.HasPowerLeases) return true;
            if (energy.GridSupplyLost(__instance)) { __result = false; return false; }
            if (__instance.PowerNet != null || !energy.HasLease(__instance) || energy.DirectGrant(__instance)) return true;
            __result = energy.RestorePrevious(__instance);
            return false;
        }
    }
    [HarmonyPatch]
    public static class FieldWirelessPowerOn
    {
        static MethodBase TargetMethod() => FieldWirelessTargets.Resolve("Patch_CompPowerTrader_PowerOnSetter_WirelessPower", "Prefix");
        static bool Prefix([HarmonyArgument(0)] CompPowerTrader trader, [HarmonyArgument(1)] bool value, ref bool __result)
        {
            if (!value) return true;
            var energy = FieldEnergy.For(trader.parent.Map);
            // The native prefix normally requires the wireless gauge to fund startup.
            // A real field allocation is a second source, with the same native setter
            // and callbacks. Do not bypass the native guard for unfunded consumers.
            if (energy == null || (!energy.DirectGrant(trader) && !energy.OwnWirelessSupply(trader))) return true;
            __result = true;
            return false;
        }
    }
    [HarmonyPatch]
    public static class FieldWirelessPayment
    {
        static MethodBase TargetMethod() => AccessTools.Method(typeof(Comp_NivarianWirelessPowerAdapter), "TickWireless");
        static bool Prefix(Comp_NivarianWirelessPowerAdapter __instance, ref float __result)
        {
            var p = __instance.parent.GetComp<CompPowerTrader>();
            if (FieldEnergy.For(__instance.parent.Map)?.OwnWirelessSupply(p) != true) return true;
            __result = 0f;
            return false;
        }
    }
    [HarmonyPatch]
    public static class FieldWirelessNetCredit
    {
        static MethodBase TargetMethod() => FieldWirelessTargets.Resolve("Patch_PowerNet_CurrentEnergyGainRate_WirelessPower", "Postfix");
        public static bool CreditPowered(CompPowerTrader p) => p.PowerOn && FieldEnergy.For(p.parent.Map)?.OwnWirelessSupply(p) != true;
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            int count = 0;
            var getter = AccessTools.PropertyGetter(typeof(CompPowerTrader), nameof(CompPowerTrader.PowerOn));
            foreach (var instruction in instructions)
            {
                if (instruction.Calls(getter)) { instruction.opcode = OpCodes.Call; instruction.operand = AccessTools.Method(typeof(FieldWirelessNetCredit), nameof(CreditPowered)); count++; }
                yield return instruction;
            }
            if (count != 1) throw new InvalidOperationException("[NMC] Native wireless credit target changed: expected one PowerOn getter, found " + count);
        }
    }
    static class FieldWirelessTargets
    {
        internal static MethodBase Resolve(string name, string method)
        {
            var type = AccessTools.TypeByName("Nivarian_Race.Code.Features.WirelessPower.Patches." + name)
                ?? AccessTools.TypeByName("Nivarian_Race.Code.Patches." + name);
            return AccessTools.Method(type, method)
                ?? throw new MissingMethodException("[NMC] Native wireless target unresolved: " + name + "." + method);
        }
    }
}
