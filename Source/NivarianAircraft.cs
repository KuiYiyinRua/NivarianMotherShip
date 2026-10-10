using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace NivarianCommander
{
    public enum AircraftFlightState { Grounded, TakingOff, Flying, Landing }

    // A normal native passenger shuttle owns cross-map travel and its real cargo.
    // Local flight is a saved map simulation; rendering never drives movement.
    public sealed class Building_NivarianAircraft : Building_PassengerShuttle, IAttackTarget
    {
        public const int FighterCapacity = 8;
        public const int FighterSteelCost = 10;
        public const int FighterManufactureTicks = 15 * 60;
        public AircraftFlightState flightState;
        public Vector3 flightPosition;
        public IntVec3 destination = IntVec3.Invalid;
        public int transition, fuelClock, launchClock, manufactureTicks, dragonSteel;
        public int weaponClock, gravityCooldown, beamTicks, volleys;
        public float heading;
        public Thing beamTarget;
        public Vector3 beamEnd;
        public ThingOwner<CarrierInterceptor> hangar;
        readonly AircraftHangarHolder hangarHolder;
        internal IThingHolder HangarHolder => hangarHolder;
        public List<CarrierInterceptor> deployed = new List<CarrierInterceptor>();
        public bool manufactureEnabled = true, automaticGravity = true;
        public bool starshipUpgradeRequested, starshipBatteryInstalled;
        public int starshipBatteriesDelivered;
        public int StarshipBatteryCost => IsCarrier ? 10 : 2;
        public int TakeoffFuelCost => IsCarrier ? 200 : 100;
        public bool StarshipPowered => starshipBatteryInstalled && AircraftStarshipUpgrade.Available;
        public bool CanTakeoffWithFuel => StarshipPowered ? FuelLevel >= TakeoffFuelCost : RefuelableComp.HasFuel;
        public bool IsCarrier => def.defName == "NMC_Carrier";
        public bool Airborne => flightState != AircraftFlightState.Grounded;
        public bool CombatFlight => flightState == AircraftFlightState.Flying;
        public int FighterCount => hangar.Count + deployed.Count(p => p != null && !p.Destroyed);
        public Thing Thing => this;
        public LocalTargetInfo TargetCurrentlyAimingAt => beamTarget;
        public float TargetPriorityFactor => 1f;
        public bool ThreatDisabled(IAttackTargetSearcher searcher)
            => !Spawned || Destroyed || (Airborne && searcher?.CurrentEffectiveVerb?.IsMeleeAttack == true);
        public Building_NivarianAircraft()
        {
            hangarHolder = new AircraftHangarHolder(this);
            hangar = new ThingOwner<CarrierInterceptor>(hangarHolder);
        }
        public override Vector3 DrawPos => Airborne
            ? new Vector3(flightPosition.x, AltitudeLayer.Skyfaller.AltitudeFor(), flightPosition.z + Lift)
            : base.DrawPos;
        float Lift => flightState == AircraftFlightState.TakingOff ? transition / 60f * 1.2f
            : flightState == AircraftFlightState.Landing ? (1f - transition / 60f) * 1.2f : 1.2f;

        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);
            if (!respawningAfterLoad) { flightPosition = Position.ToVector3Shifted(); heading = Rotation.AsAngle; }
            if (!destination.IsValid) destination = Position;
        }
        protected override void Tick()
        {
            base.Tick();
            if (!Spawned || Destroyed) return;
            deployed.RemoveAll(p => p == null || p.Destroyed);
            if (IsCarrier) Manufacture();
            if (gravityCooldown > 0) gravityCooldown--;
            if (beamTicks > 0) beamTicks--;
            if (weaponClock > 0) weaponClock--;
            if (launchClock > 0) launchClock--;
            if (!Airborne) return;
            if (!StarshipPowered && ++fuelClock >= (IsCarrier ? 60 : 300))
            {
                fuelClock = 0;
                RefuelableComp.ConsumeFuel(1f);
            }
            if (!StarshipPowered && !RefuelableComp.HasFuel)
            {
                RecallAll(true);
                EmergencyLand();
                return;
            }
            if (flightState == AircraftFlightState.TakingOff)
            {
                if (++transition >= 60) { flightState = AircraftFlightState.Flying; transition = 0; }
            }
            else if (flightState == AircraftFlightState.Landing)
            {
                // All squadrons return concurrently. Descent starts after the last arrives.
                if (deployed.Count == 0 && ++transition >= 60) FinishLanding();
            }
            else
            {
                MoveFlight();
                if (IsCarrier) LaunchDue(); else FrigateCombat();
            }
        }
        void MoveFlight()
        {
            if (!destination.IsValid || destination == Position &&
                (destination.ToVector3Shifted() - flightPosition).sqrMagnitude < .0001f) return;
            Vector3 next = destination.ToVector3Shifted();
            var delta = next - flightPosition; delta.y = 0;
            if (delta.sqrMagnitude > .001f) heading = Mathf.MoveTowardsAngle(heading, delta.AngleFlat(), 3f);
            flightPosition = Vector3.MoveTowards(flightPosition, next, IsCarrier ? .055f : .115f);
            var cell = flightPosition.ToIntVec3();
            if (cell != Position && cell.InBounds(Map)) Position = cell;
        }
        internal void Manufacture()
        {
            // The native transport loading UI and hauling jobs fill the metal buffer.
            // Overflow stays real cargo; manufacture also runs while airborne.
            foreach (var steel in TransporterComp.innerContainer.Where(t => t.def.defName == "Nivarian_DragonfrostSteel").ToList())
            {
                int count = Math.Min(1000 - dragonSteel, steel.stackCount);
                if (count <= 0) break;
                steel.SplitOff(count).Destroy(); dragonSteel += count;
            }
            if (!manufactureEnabled || FighterCount >= FighterCapacity) return;
            if (manufactureTicks == 0)
            {
                if (dragonSteel < FighterSteelCost) return;
                dragonSteel -= FighterSteelCost; manufactureTicks = FighterManufactureTicks;
            }
            if (--manufactureTicks > 0) return;
            var fighter = (CarrierInterceptor)ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("NMC_CarrierInterceptor"));
            fighter.carrier = this; fighter.SetFaction(Faction);
            hangar.TryAdd(fighter);
        }
        void LaunchDue()
        {
            if (launchClock > 0 || hangar.Count == 0) return;
            Thing target = AircraftTargeting.Nearest(this, 60f, false);
            if (target == null) return;
            var fighter = hangar[0]; hangar.Remove(fighter);
            fighter.returning = false; fighter.target = target; fighter.realPosition = flightPosition;
            GenSpawn.Spawn(fighter, Position, Map, WipeMode.Vanish);
            deployed.Add(fighter); launchClock = 60;
        }
        void FrigateCombat()
        {
            if (automaticGravity && gravityCooldown == 0)
            {
                var ground = AircraftTargeting.Nearest(this, 16f, false, groundPawnOnly: true);
                if (ground != null) UseGravity(ground);
            }
            if (weaponClock > 0) return;
            var target = AircraftTargeting.Nearest(this, 16f, true);
            if (target == null) return;
            weaponClock = 60; beamTicks = 24; volleys++; beamTarget = target;
            beamEnd = target.Position.ToVector3Shifted();
            Map.GetComponent<AircraftPrismVisuals>().Show(this);
            var damage = DefDatabase<DamageDef>.GetNamed("NMC_FrigateBeamDamage");
            for (int i = 0; i < 2 && !target.Destroyed; i++)
                target.TakeDamage(new DamageInfo(damage, 12f, .5f, (target.Position - Position).ToVector3().AngleFlat(), this));
        }
        internal void UseGravity(Thing target)
        {
            if (!CombatFlight || IsCarrier || gravityCooldown > 0 || !(target is Pawn pawn)
                || !AircraftTargeting.Valid(this, target, 16f) || AircraftTargeting.Airborne(target)) return;
            Map.GetComponent<AircraftGravity>().Lift(this, pawn);
            gravityCooldown = 480;
        }
        internal void RecallAll(bool instant)
        {
            foreach (var fighter in deployed.ToList())
            {
                if (fighter == null || fighter.Destroyed) continue;
                fighter.returning = true; fighter.target = null;
                if (instant && fighter.Spawned && fighter.Map == Map) Recover(fighter);
            }
        }
        internal void Recover(CarrierInterceptor fighter)
        {
            if (fighter == null || fighter.Destroyed || hangar.Count >= FighterCapacity) return;
            if (fighter.Spawned) fighter.DeSpawn();
            deployed.Remove(fighter); fighter.target = null; fighter.returning = false;
            hangar.TryAdd(fighter);
        }
        internal void BeginTakeoff()
        {
            if (Airborne || !CanTakeoffWithFuel || this.OccupiedRect().Cells.Any(c => c.Roofed(Map))) return;
            if (StarshipPowered) { RefuelableComp.ConsumeFuel(TakeoffFuelCost); fuelClock = 0; }
            flightPosition = Position.ToVector3Shifted(); destination = Position;
            transition = 0; flightState = AircraftFlightState.TakingOff;
            foreach (var pawn in Map.mapPawns.AllPawnsSpawned)
            {
                var job = pawn.CurJob;
                if (job != null && (job.def == JobDefOf.HaulToTransporter || job.def == JobDefOf.EnterTransporter
                    || job.def == JobDefOf.Refuel || job.def == JobDefOf.RefuelAtomic)
                    && (job.targetA.Thing == this || job.targetB.Thing == this || job.targetC.Thing == this))
                    pawn.jobs.EndCurrentJob(JobCondition.Incompletable);
            }
        }
        internal void BeginLanding()
        {
            if (!Airborne || !AircraftTargeting.LandingClear(this, Position)) return;
            destination = Position; flightPosition = Position.ToVector3Shifted();
            flightState = AircraftFlightState.Landing; transition = 0; beamTicks = 0;
            RecallAll(false);
        }
        void EmergencyLand()
        {
            // Fuel loss never deletes cargo or a squadron. Find the nearest legal footprint.
            var cell = GenRadial.RadialCellsAround(Position, 40f, true)
                .FirstOrDefault(c => AircraftTargeting.LandingClear(this, c));
            if (!AircraftTargeting.LandingClear(this, cell))
            {
                destination = Position; flightState = AircraftFlightState.Flying;
                return;
            }
            Position = cell; flightPosition = cell.ToVector3Shifted(); FinishLanding();
            Messages.Message(NmcText.Get("NMC_Text0288", LabelCap), this, MessageTypeDefOf.CautionInput, false);
        }
        void FinishLanding()
        {
            flightState = AircraftFlightState.Grounded; transition = 0; beamTicks = 0;
            destination = Position; flightPosition = Position.ToVector3Shifted();
            Rotation = Rot4.North; heading = 0f;
        }
        public override void DeSpawn(DestroyMode mode = DestroyMode.Vanish)
        {
            // Native cross-map launch carries this exact shuttle and all held fighters.
            RecallAll(true); flightState = AircraftFlightState.Grounded; beamTicks = 0;
            base.DeSpawn(mode);
        }
        public override void Destroy(DestroyMode mode = DestroyMode.Vanish)
        {
            // Detached interceptors cannot survive as immortal orphan units.
            foreach (var fighter in deployed.ToList()) if (fighter != null && !fighter.Destroyed) fighter.Destroy(mode);
            deployed.Clear(); hangar.ClearAndDestroyContents();
            base.Destroy(mode);
        }
        protected override void DrawAt(Vector3 drawLoc, bool flip = false)
        {
            if (!Airborne) { base.DrawAt(drawLoc, flip); return; }
            var scale = new Vector3(def.graphicData.drawSize.x, 1f, def.graphicData.drawSize.y);
            Graphics.DrawMesh(MeshPool.plane10, Matrix4x4.TRS(DrawPos, Quaternion.AngleAxis(heading, Vector3.up), scale), Graphic.MatSingle, 0);
            if (beamTicks > 0) Map.GetComponent<AircraftPrismVisuals>().Show(this);
        }
        public override IEnumerable<Gizmo> GetGizmos()
        {
            if (!Airborne) foreach (var gizmo in base.GetGizmos()) yield return gizmo;
            if (Faction != Faction.OfPlayer) yield break;
            foreach (var gizmo in AircraftActions.Gizmos(this)) yield return gizmo;
        }
        public override string GetInspectString()
        {
            string text = base.GetInspectString() + "\n" + (Airborne ? NmcText.Get("NMC_Text0001") : NmcText.Get("NMC_Text0289"))
                + (StarshipPowered ? NmcText.Get("NMC_Text0290", TakeoffFuelCost)
                    : NmcText.Get("NMC_Text0293", (IsCarrier ? NmcText.Get("NMC_Text0291") : NmcText.Get("NMC_Text0292"))));
            if (starshipUpgradeRequested && !starshipBatteryInstalled)
                text += NmcText.Get("NMC_Text0294", starshipBatteriesDelivered, StarshipBatteryCost);
            if (IsCarrier) text += NmcText.Get("NMC_Text0296", FighterCount, FighterCapacity, hangar.Count, deployed.Count, dragonSteel, (manufactureTicks > 0 ? NmcText.Get("NMC_Text0295", (manufactureTicks / 60f).ToString("F1")) : ""));
            else text += NmcText.Get("NMC_Text0297", (gravityCooldown / 60f).ToString("F1"));
            return text;
        }
        // The hull deliberately leaves IThingHolder to the native transporter comp.
        // Native hauling therefore resolves its real cargo, never the typed fighter bay.
        sealed class AircraftHangarHolder : IThingHolder
        {
            readonly Building_NivarianAircraft aircraft;
            public AircraftHangarHolder(Building_NivarianAircraft aircraft) { this.aircraft = aircraft; }
            public IThingHolder ParentHolder => aircraft.TransporterComp;
            public ThingOwner GetDirectlyHeldThings() => aircraft.hangar;
            public void GetChildHolders(List<IThingHolder> outChildren)
                => ThingOwnerUtility.AppendThingHoldersFromThings(outChildren, aircraft.hangar);
        }
        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref flightState, "nmcFlight"); Scribe_Values.Look(ref flightPosition, "nmcFlightPosition");
            Scribe_Values.Look(ref destination, "nmcDestination", IntVec3.Invalid); Scribe_Values.Look(ref heading, "nmcHeading");
            Scribe_Values.Look(ref transition, "nmcTransition"); Scribe_Values.Look(ref fuelClock, "nmcFuelClock");
            Scribe_Values.Look(ref launchClock, "nmcLaunchClock"); Scribe_Values.Look(ref manufactureTicks, "nmcManufactureTicks");
            Scribe_Values.Look(ref dragonSteel, "nmcDragonSteel"); Scribe_Values.Look(ref weaponClock, "nmcWeaponClock");
            Scribe_Values.Look(ref gravityCooldown, "nmcGravityCooldown"); Scribe_Values.Look(ref beamTicks, "nmcBeamTicks");
            Scribe_Values.Look(ref beamEnd, "nmcBeamEnd"); Scribe_Values.Look(ref volleys, "nmcVolleys");
            Scribe_Values.Look(ref manufactureEnabled, "nmcManufactureEnabled", true);
            Scribe_Values.Look(ref automaticGravity, "nmcAutomaticGravity", true);
            Scribe_Values.Look(ref starshipUpgradeRequested, "nmcStarshipUpgradeRequested");
            Scribe_Values.Look(ref starshipBatteryInstalled, "nmcStarshipBatteryInstalled");
            Scribe_Values.Look(ref starshipBatteriesDelivered, "nmcStarshipBatteriesDelivered");
            Scribe_References.Look(ref beamTarget, "nmcBeamTarget");
            // Retain the old save label and content type; only the runtime owner changes.
            Scribe_Deep.Look(ref hangar, "nmcHangar", hangarHolder);
            Scribe_Collections.Look(ref deployed, "nmcDeployed", LookMode.Reference);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (hangar == null) hangar = new ThingOwner<CarrierInterceptor>(hangarHolder);
                if (deployed == null) deployed = new List<CarrierInterceptor>();
                deployed.RemoveAll(p => p == null || p.Destroyed);
            }
        }
    }

    public sealed class CompProperties_AircraftLaunchable : CompProperties_Launchable
    { public CompProperties_AircraftLaunchable() { compClass = typeof(CompAircraftLaunchable); } }
    public sealed class CompAircraftLaunchable : CompLaunchable
    {
        public override bool RequiresFuelingPort => false;
        public override AcceptanceReport CanLaunch(float? overrideFuelLevel = null)
        {
            if (parent is Building_NivarianAircraft ship && ship.Airborne) return NmcText.Get("NMC_Text0298");
            if (parent is Building_NivarianAircraft grounded && grounded.StarshipPowered
                && (overrideFuelLevel ?? FuelLevel) < grounded.TakeoffFuelCost)
                return NmcText.Get("NMC_Text0299", grounded.TakeoffFuelCost);
            return base.CanLaunch(overrideFuelLevel);
        }
    }
    public sealed class CompProperties_AircraftTransporter : CompProperties_Transporter
    { public CompProperties_AircraftTransporter() { compClass = typeof(CompAircraftTransporter); } }
    public sealed class CompAircraftTransporter : CompTransporter, IThingHolder
    {
        public override bool RequiresFuelingPort => false;
        void IThingHolder.GetChildHolders(List<IThingHolder> outChildren)
        {
            base.GetChildHolders(outChildren);
            // One holder tree exposes native cargo and the separately saved fighter bay
            // exactly once, including while the aircraft is inside a travelling shuttle.
            if (parent is Building_NivarianAircraft aircraft) outChildren.Add(aircraft.HangarHolder);
        }
    }
}
