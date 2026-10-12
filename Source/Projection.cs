using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Nivarian_Race.Code.Comps.ThingComps;
using RimWorld;
using RimWorld.Planet;
using Verse;
using UnityEngine;

namespace NivarianCommander
{
    public sealed class ProjectionLink : IExposable
    {
        public Thing source, projection;
        public Map sourceMap, targetMap;
        public int initialHP, remaining = 3900, bornLocalTick, damageReturned;
        internal bool retiring, destroying;
        internal int damageDepth;
        public void ExposeData()
        {
            Scribe_References.Look(ref source, "source"); Scribe_References.Look(ref projection, "projection");
            Scribe_References.Look(ref sourceMap, "sourceMap"); Scribe_References.Look(ref targetMap, "targetMap");
            Scribe_Values.Look(ref initialHP, "initialHP"); Scribe_Values.Look(ref remaining, "remainingLocalTicks", 3900);
            Scribe_Values.Look(ref bornLocalTick, "bornLocalTick"); Scribe_Values.Look(ref damageReturned, "damageReturned");
        }
    }
    public static class ProjectionContext
    {
        // The optional MP assembly installs a narrow native map-context scope.
        // This never advances a source-map tick or queues damage until resume.
        public static Action<Map, Action> Mutation = (map, action) => action();
        public static void Run(Map map, Action action) => Mutation(map, action);
    }
    public sealed class ProjectionRegistry : WorldComponent
    {
        public List<Thing> registered = new List<Thing>();
        public List<ProjectionLink> links = new List<ProjectionLink>();
        readonly Dictionary<Thing, ProjectionLink> bySource = new Dictionary<Thing, ProjectionLink>();
        readonly Dictionary<Thing, ProjectionLink> byProjection = new Dictionary<Thing, ProjectionLink>();
        readonly Dictionary<Map, List<ProjectionLink>> byTargetMap = new Dictionary<Map, List<ProjectionLink>>();
        // Weak identity indexes keep ordinary Things out of world-component scans.
        // A positive lookup still verifies the current world, including during load.
        sealed class IndexedLink
        {
            internal readonly ProjectionRegistry owner;
            internal readonly ProjectionLink link;
            internal IndexedLink(ProjectionRegistry owner, ProjectionLink link) { this.owner = owner; this.link = link; }
        }
        static readonly ConditionalWeakTable<World, ProjectionRegistry> worlds = new ConditionalWeakTable<World, ProjectionRegistry>();
        static readonly ConditionalWeakTable<Thing, IndexedLink> sources = new ConditionalWeakTable<Thing, IndexedLink>();
        static readonly ConditionalWeakTable<Thing, IndexedLink> projections = new ConditionalWeakTable<Thing, IndexedLink>();
        readonly World ownerWorld;
        bool loadValidationPending;
        static readonly System.Reflection.FieldInfo Gun = AccessTools.Field(typeof(Building_TurretGun), "gun");
        static readonly System.Reflection.FieldInfo AttachedGun = AccessTools.Field(typeof(CompAttachTurret), "_mainWeapon");
        public ProjectionRegistry(World world) : base(world)
        { ownerWorld = world; if (world != null) { worlds.Remove(world); worlds.Add(world, this); } }
        public static ProjectionRegistry Current
        {
            get
            {
                var world = Find.World;
                return world == null ? null : worlds.TryGetValue(world, out var registry) ? registry : world.GetComponent<ProjectionRegistry>();
            }
        }
        public static ProjectionLink Projected(Thing thing)
            => thing != null && projections.TryGetValue(thing, out var entry) && entry.owner.ownerWorld == Find.World ? entry.link : null;
        public static ProjectionLink Source(Thing thing)
            => thing != null && sources.TryGetValue(thing, out var entry) && entry.owner.ownerWorld == Find.World ? entry.link : null;
        public static bool IsProjection(Thing thing)
            => thing != null && projections.TryGetValue(thing, out var entry) && entry.owner.ownerWorld == Find.World;
        public static bool BlocksFire(Thing thing)
        {
            if (Source(thing) is ProjectionLink original && !original.retiring) return true;
            var link = Projected(thing);
            return link != null && (link.retiring || link.remaining > 3600 || !ValidSource(link));
        }
        internal static bool BlocksKnownProjection(Thing thing, ProjectionLink link)
            => link.retiring || link.remaining > 3600 || !ValidSource(link)
                || (Source(thing) is ProjectionLink original && !original.retiring);
        public static bool ValidSource(ProjectionLink link)
            => link.source?.Spawned == true && !link.source.Destroyed && link.source.Map == link.sourceMap
                && link.source.Faction == Faction.OfPlayer && Find.Maps.Contains(link.sourceMap);
        // Preserve component slots, including null weapons during native setup.
        // A filtered list would shift a later gun onto a different source slot.
        public static List<Thing> Weapons(Thing thing)
        {
            var result = new List<Thing>();
            if (thing is Building_TurretGun turret) result.Add(Gun.GetValue(turret) as Thing);
            if (thing is ThingWithComps holder)
                foreach (var comp in holder.AllComps)
                    if (comp is CompAttachTurret attached) result.Add(Weapon(attached));
            return result;
        }
        public static Thing Weapon(Thing thing) => WeaponAt(thing, 0);
        public static Thing Weapon(CompAttachTurret attached) => AttachedGun.GetValue(attached) as Thing;
        static Thing WeaponAt(Thing thing, int slot)
        {
            if (thing is Building_TurretGun turret)
            { if (slot == 0) return Gun.GetValue(turret) as Thing; slot--; }
            if (thing is ThingWithComps holder)
                foreach (var comp in holder.AllComps)
                    if (comp is CompAttachTurret attached && slot-- == 0) return Weapon(attached);
            return null;
        }
        static int WeaponSlot(Thing thing, Thing gun)
        {
            int slot = 0;
            if (thing is Building_TurretGun turret)
            { if (Gun.GetValue(turret) == gun) return slot; slot++; }
            if (thing is ThingWithComps holder)
                foreach (var comp in holder.AllComps)
                    if (comp is CompAttachTurret attached)
                    { if (Weapon(attached) == gun) return slot; slot++; }
            return -1;
        }
        static Thing CorrespondingWeapon(ProjectionLink link, Thing copyGun)
        {
            int index = WeaponSlot(link.projection, copyGun);
            if (index < 0) return null;
            var original = WeaponAt(link.source, index);
            if (original == null || original.def != copyGun.def)
                throw new InvalidOperationException("[NMC S19] Projection weapon slot has no matching source");
            return original;
        }
        public static Thing ResourceSource(Thing parent)
        {
            var direct = Projected(parent);
            if (direct != null) return direct.source;
            if (parent == null) return null;
            var registry = Current;
            if (registry == null || registry.links.Count == 0) return null;
            foreach (var link in registry.links)
            {
                var sourceGun = CorrespondingWeapon(link, parent);
                if (sourceGun != null) return sourceGun;
            }
            return null;
        }
        public static Map ResourceMap(Thing source)
        {
            var direct = Source(source);
            if (direct?.sourceMap != null) return direct.sourceMap;
            var registry = Current;
            if (registry != null)
                foreach (var link in registry.links)
                    if (WeaponSlot(link.source, source) >= 0) return link.sourceMap;
            return null;
        }
        public static bool Eligible(Thing thing)
        {
            if (!(thing is Building) || !thing.Spawned || thing.Destroyed || thing.Faction != Faction.OfPlayer
                || IsProjection(thing) || !thing.def.useHitPoints) return false;
            if (thing.TryGetComp<CompMonolithWeapon>() != null) return true;
            if (!(thing is Building_TurretGun) && thing.TryGetComp<CompAttachTurret>() == null) return false;
            string owner = thing.def.modContentPack?.PackageIdPlayerFacing ?? "";
            return owner.IndexOf("nivarian", StringComparison.OrdinalIgnoreCase) >= 0
                || thing.def.defName.IndexOf("Nivarian", StringComparison.OrdinalIgnoreCase) >= 0
                || thing.GetType().Namespace?.StartsWith("Nivarian", StringComparison.Ordinal) == true;
        }
        public void Add(ProjectionLink link)
        {
            links.Add(link); bySource.Add(link.source, link); byProjection.Add(link.projection, link);
            Index(link);
        }
        void Index(ProjectionLink link)
        {
            sources.Remove(link.source); sources.Add(link.source, new IndexedLink(this, link));
            projections.Remove(link.projection); projections.Add(link.projection, new IndexedLink(this, link));
            if (link.targetMap == null) return;
            if (!byTargetMap.TryGetValue(link.targetMap, out var bucket)) byTargetMap.Add(link.targetMap, bucket = new List<ProjectionLink>());
            bucket.Add(link);
        }
        void Unindex(ProjectionLink link)
        {
            if (link.source != null && sources.TryGetValue(link.source, out var source) && source.owner == this && source.link == link) sources.Remove(link.source);
            if (link.projection != null && projections.TryGetValue(link.projection, out var copy) && copy.owner == this && copy.link == link) projections.Remove(link.projection);
            if (link.targetMap != null && byTargetMap.TryGetValue(link.targetMap, out var bucket))
            { bucket.Remove(link); if (bucket.Count == 0) byTargetMap.Remove(link.targetMap); }
        }
        public void Retire(ProjectionLink link, bool destroyProjection = true)
        {
            if (link == null || link.retiring) return;
            link.retiring = true;
            var weapons = Weapons(link.projection).Where(w => w != null).Distinct().ToArray();
            try
            {
                if (destroyProjection && link.projection != null && !link.projection.Destroyed)
                    ProjectionContext.Run(link.targetMap, () => link.projection.Destroy(DestroyMode.Vanish));
                foreach (var weapon in weapons)
                    if (!weapon.Destroyed) ProjectionContext.Run(link.targetMap, () => weapon.Destroy(DestroyMode.Vanish));
            }
            finally
            {
                bySource.Remove(link.source); byProjection.Remove(link.projection); links.Remove(link);
                Unindex(link);
            }
        }
        public void RemoveThing(Thing thing)
        {
            var link = Source(thing) ?? Projected(thing);
            if (link != null) Retire(link, thing != link.projection);
            registered.Remove(thing);
        }
        public void RemoveMap(Map map)
        {
            foreach (var link in links.Where(l => l.sourceMap == map || l.targetMap == map).ToArray()) Retire(link);
            registered.RemoveAll(t => t == null || t.Map == map || !t.Spawned || t.Destroyed);
        }
        public void Tick(Map map)
        {
            if (!byTargetMap.TryGetValue(map, out var bucket)) return;
            // Native destruction can retire another link. Retain snapshot order.
            foreach (var link in bucket.ToArray())
            {
                if (!ValidSource(link) || link.projection?.Spawned != true || link.projection.Destroyed || link.projection.Map != map)
                { Retire(link); continue; }
                if (link.bornLocalTick != GenTicks.TicksGame && --link.remaining <= 0) Retire(link);
            }
        }
        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref registered, "nmcProjectionRegistered", LookMode.Reference);
            Scribe_Collections.Look(ref links, "nmcProjectionLinks", LookMode.Deep);
            if (Scribe.mode != LoadSaveMode.PostLoadInit) return;
            registered = registered ?? new List<Thing>(); links = links ?? new List<ProjectionLink>();
            // Scribe PostLoadInit resolves identities before Map.FinalizeLoading
            // spawns buildings. Rebuild indexes now; validate Spawned only later.
            loadValidationPending = true;
            foreach (var oldLink in byProjection.Values) Unindex(oldLink);
            bySource.Clear(); byProjection.Clear(); byTargetMap.Clear();
            foreach (var link in links.ToArray())
            {
                if (link.source == null || link.projection == null || bySource.ContainsKey(link.source) || byProjection.ContainsKey(link.projection))
                    throw new InvalidOperationException("[NMC S19] Invalid saved projection identity");
                bySource.Add(link.source, link); byProjection.Add(link.projection, link);
                Index(link);
            }
        }
        public void FinalizeAfterLoad()
        {
            if (!loadValidationPending) return;
            loadValidationPending = false;
            registered.RemoveAll(t => !Eligible(t));
            // GameComponent.LoadedGame runs after all maps finish native spawning.
            // Keep the original references and deadlines; never replace a source.
            foreach (var link in links.ToArray())
                if (!ValidSource(link) || link.projection.Destroyed || !link.projection.Spawned
                    || link.projection.Map != link.targetMap || link.remaining <= 0) Retire(link);
        }
        internal void Draw(Map map)
        {
            if (!byTargetMap.TryGetValue(map, out var bucket)) return;
            foreach (var link in bucket)
                if (link.projection?.Spawned == true && !link.retiring)
                {
                    GenDraw.DrawRadiusRing(link.projection.Position, link.remaining > 3600 ? 2.1f : 1.6f, Color.cyan);
                    ProjectionPowerOverlay.Refresh(link);
                }
        }
    }
    public sealed class ProjectionMapClock : MapComponent
    {
        public ProjectionMapClock(Map map) : base(map) { }
        public override void MapComponentTick() => ProjectionRegistry.Current.Tick(map);
        public override void MapRemoved() => ProjectionRegistry.Current.RemoveMap(map);
        public override void MapComponentUpdate()
        {
            if (Find.CurrentMap == map) ProjectionRegistry.Current?.Draw(map);
        }
    }
    public static class ProjectionActions
    {
        public static void Register(Map map, Thing source, bool participate)
        {
            if (map == null || !Find.Maps.Contains(map) || source?.Map != map || !ProjectionRegistry.Eligible(source)
                || CommanderResearch.For(DefenseActions.Ability(19))?.IsFinished != true) return;
            var registered = ProjectionRegistry.Current.registered;
            if (participate && !registered.Contains(source)) registered.Add(source);
            if (!participate) registered.Remove(source);
        }
        public static void RegisterAll(bool participate)
        {
            if (CommanderResearch.For(DefenseActions.Ability(19))?.IsFinished != true) return;
            foreach (var map in Find.Maps.OrderBy(m => m.uniqueID))
                foreach (var thing in map.listerThings.AllThings.Where(ProjectionRegistry.Eligible).OrderBy(t => t.thingIDNumber))
                {
                    if (participate && !ProjectionRegistry.Current.registered.Contains(thing)) ProjectionRegistry.Current.registered.Add(thing);
                    if (!participate) ProjectionRegistry.Current.registered.Remove(thing);
                }
        }
        public static string Unavailable(Map map)
        {
            string error = DefenseActions.Unavailable(map, 19);
            if (error != null) return error;
            return ProjectionRegistry.Current.registered.Any(t => ProjectionRegistry.Eligible(t) && t.HitPoints > 1
                && ProjectionRegistry.Source(t) == null) ? null : NmcText.Get("NMC_Text0407");
        }
        public static bool Legal(Map map, IntVec3 center, Thing source, ISet<IntVec3> reserved)
        {
            if (map == null || !Find.Maps.Contains(map) || source == null) return false;
            foreach (var cell in GenAdj.OccupiedRect(center, source.Rotation, source.def.Size))
            {
                if (!cell.InBounds(map) || cell.x <= 0 || cell.z <= 0 || cell.x >= map.Size.x - 1 || cell.z >= map.Size.z - 1
                    || cell.Fogged(map) || reserved.Contains(cell)) return false;
                if (source.def.terrainAffordanceNeeded != null && !cell.GetTerrain(map).affordances.Contains(source.def.terrainAffordanceNeeded)) return false;
                if (cell.GetThingList(map).Any(t => t.def.category == ThingCategory.Building || t.def.category == ThingCategory.Item
                    || t.def.category == ThingCategory.Pawn || t.def.passability == Traversability.Impassable)) return false;
            }
            return true; // Ordinary and thick roofs are explicitly allowed.
        }
        public sealed class Placement { public Thing source; public IntVec3 cell; }
        public static List<Placement> Plan(Map map, IntVec3 center, bool randomize)
        {
            var placements = new List<Placement>();
            if (map == null || !center.InBounds(map) || !Find.Maps.Contains(map)) return placements;
            var cells = GenRadial.RadialCellsAround(center, 20f, true).Where(c => c.InBounds(map)).ToList();
            // Shuffling before the stable distance sort randomizes only equally
            // compact positions. Preview never consumes the shared random stream.
            if (randomize) for (int i = cells.Count - 1; i > 0; --i) { int j = Rand.RangeInclusive(0, i); var swap = cells[i]; cells[i] = cells[j]; cells[j] = swap; }
            cells = cells.OrderBy(c => c.DistanceToSquared(center)).ToList();
            var occupied = new HashSet<IntVec3>();
            foreach (var source in ProjectionRegistry.Current.registered.Where(t => ProjectionRegistry.Eligible(t)
                && t.HitPoints > 1 && ProjectionRegistry.Source(t) == null).OrderByDescending(t => t.def.Size.x * t.def.Size.z).ThenBy(t => t.thingIDNumber))
            {
                var available = cells.Where(c => Legal(map, c, source, occupied)).Take(1).ToList();
                if (available.Count == 0) continue;
                placements.Add(new Placement { source = source, cell = available[0] });
                foreach (var c in GenAdj.OccupiedRect(available[0], source.Rotation, source.def.Size)) occupied.Add(c);
            }
            return placements;
        }
        static void Reject(string reason)
        { CommanderState.Current.requestsRejected++; Messages.Message(reason, MessageTypeDefOf.RejectInput, false); }
        public static void Start(Map map, IntVec3 center)
        {
            string error = Unavailable(map);
            if (error != null) { Reject(error); return; }
            var plan = Plan(map, center, true);
            if (plan.Count == 0) { Reject(NmcText.Get("NMC_Text0408")); return; }
            var ability = DefenseActions.Ability(19);
            int power = ability.powerCost, volatileCost = ability.volatileCost;
            if (NativeLedger.Balance("PowerCoin") < power || NativeLedger.Balance("VolatileReactive") < volatileCost)
            { Reject(NmcText.Get("NMC_Text0409", power, volatileCost)); return; }
            var created = new List<ProjectionLink>();
            try
            {
                foreach (var entry in plan)
                {
                    var copy = ThingMaker.MakeThing(entry.source.def, entry.source.Stuff);
                    copy.SetFaction(Faction.OfPlayer); copy.HitPoints = entry.source.HitPoints / 2;
                    var link = new ProjectionLink { source = entry.source, projection = copy, sourceMap = entry.source.Map,
                        targetMap = map, initialHP = copy.HitPoints, bornLocalTick = GenTicks.TicksGame };
                    ProjectionRegistry.Current.Add(link); created.Add(link);
                    ProjectionResources.ClearIndependentInventory(copy);
                    GenSpawn.Spawn(copy, entry.cell, map, entry.source.Rotation, WipeMode.Vanish);
                    var quality = entry.source.TryGetComp<CompQuality>();
                    if (quality != null) copy.TryGetComp<CompQuality>()?.SetQuality(quality.Quality, ArtGenerationContext.Outsider);
                    var sourceGuns = ProjectionRegistry.Weapons(entry.source); var copyGuns = ProjectionRegistry.Weapons(copy);
                    if (sourceGuns.Count != copyGuns.Count) throw new InvalidOperationException(NmcText.Get("NMC_Text0410"));
                    for (int i = 0; i < copyGuns.Count; ++i)
                    {
                        var sourceGun = sourceGuns[i]; var copyGun = copyGuns[i];
                        if (sourceGun == null || copyGun == null || sourceGun.def != copyGun.def)
                            throw new InvalidOperationException(NmcText.Get("NMC_Text0411"));
                        ProjectionResources.ClearIndependentInventory(copyGun);
                        if (sourceGun.TryGetComp<CompQuality>() is CompQuality gunQuality)
                            copyGun.TryGetComp<CompQuality>()?.SetQuality(gunQuality.Quality, ArtGenerationContext.Outsider);
                    }
                }
                if (!NativeLedger.TryPay(power, volatileCost)) throw new InvalidOperationException(NmcText.Get("NMC_Text0412"));
                CommanderState.Current.cooldowns[DefenseActions.Ability(19).defName] = 420000;
            }
            catch (Exception ex)
            {
                foreach (var link in created) ProjectionRegistry.Current.Retire(link);
                Reject(NmcText.Get("NMC_Text0413"));
                Log.Error("[NMC S19] Projection transaction failed: " + ex);
            }
        }
        public static void Cancel(Map map, Thing projection)
        {
            var link = ProjectionRegistry.Projected(projection);
            if (link != null && link.targetMap == map && Find.Maps.Contains(map) && projection.Faction == Faction.OfPlayer)
                ProjectionRegistry.Current.Retire(link);
        }
        public static void CancelSource(Map map, Thing source)
        {
            var link = ProjectionRegistry.Source(source);
            if (link != null && link.sourceMap == map && source.Faction == Faction.OfPlayer) ProjectionRegistry.Current.Retire(link);
        }
    }
}
