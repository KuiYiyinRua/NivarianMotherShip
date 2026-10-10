using System;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace NivarianCommander.MultiplayerAdapter
{
    public static class FieldRecallMapContext
    {
        static Type pawnCompType;
        static FieldInfo removedFromWorldTick;
        static Action<Pawn, Map, Map> fixPawn;

        public static void Install()
        {
            pawnCompType = AccessTools.TypeByName("Multiplayer.Client.MultiplayerPawnComp");
            var fixerType = AccessTools.TypeByName("Multiplayer.Client.Patches.TimestampFixer");
            var method = AccessTools.DeclaredMethod(fixerType, "FixPawn", new[] { typeof(Pawn), typeof(Map), typeof(Map) });
            removedFromWorldTick = AccessTools.Field(pawnCompType, "worldPawnRemoveTick");
            if (pawnCompType == null || method == null || removedFromWorldTick == null || removedFromWorldTick.FieldType != typeof(int))
                throw new MissingMemberException("[NMC-MP] Native cross-map pawn timestamp conversion unresolved");
            fixPawn = (Action<Pawn, Map, Map>)Delegate.CreateDelegate(typeof(Action<Pawn, Map, Map>), method);
            FieldRecallContext.SpawnAcrossMaps = SpawnAcrossMaps;
            Log.Message("[NMC-MP] Field recall synchronized; native map scopes and pawn timestamp conversion resolved.");
        }

        static void SpawnAcrossMaps(Pawn pawn, Map source, Map destination, Action spawn)
        {
            if (!MP.IsInMultiplayer) { spawn(); return; }
            object component = null;
            foreach (var comp in pawn.AllComps)
                if (comp.GetType() == pawnCompType) { component = comp; break; }
            if (component == null) throw new InvalidOperationException("[NMC-MP] Recalled pawn has no native MultiplayerPawnComp");
            int oldRemovalTick = (int)removedFromWorldTick.GetValue(component);
            // Direct map-to-map transfers do not pass through WorldPawns. Suppress only
            // the vanilla MP world-to-map correction for this synchronous spawn, including
            // a colonist who happened to leave a caravan earlier in the same world tick.
            removedFromWorldTick.SetValue(component, -1);
            try { spawn(); }
            finally
            {
                try
                {
                    if (pawn.Spawned && pawn.Map == destination) fixPawn(pawn, source, destination);
                }
                finally { removedFromWorldTick.SetValue(component, oldRemovalTick); }
            }
        }
    }
}
