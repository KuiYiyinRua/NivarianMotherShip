using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace NivarianCommander.MultiplayerAdapter
{
    // mp2diag1: equal map/tick/Rand, unequal Unity idle angles; after the
    // black-hole lease ends, rotations reach the next Rand draw on different ticks.
    public static class NativeTurretIdleRandom
    {
        static MethodInfo unityRange;
        static FieldInfo turretId;
        static Func<float, float, float> originalUnityRange;
        public static void Install()
        {
            var type = AccessTools.TypeByName("Nivarian_Race.Code.Comps.ThingComps.CompAttachTurret");
            var target = AccessTools.DeclaredMethod(type, "CompTick", Type.EmptyTypes);
            unityRange = AccessTools.Method(AccessTools.TypeByName("UnityEngine.Random"), "Range", new[] { typeof(float), typeof(float) });
            if (target == null || unityRange == null || unityRange.ReturnType != typeof(float))
                throw new MissingMethodException("[NMC-MP] Native turret idle tick/Unity float Range target unresolved");
            originalUnityRange = (Func<float, float, float>)Delegate.CreateDelegate(typeof(Func<float, float, float>), unityRange);
            var harmony = new Harmony("local.nivarian.commander.native-turret-idle-random");
            harmony.Patch(target,
                transpiler: new HarmonyMethod(typeof(NativeTurretIdleRandom), nameof(Transpiler)) { after = NativeRandomCompatibility.AfterOwners });
            var expose = AccessTools.DeclaredMethod(type, "PostExposeData", Type.EmptyTypes);
            turretId = AccessTools.Field(AccessTools.TypeByName("Nivarian_Race.Code.Comps.ThingComps.CompProperties_AttachTurret"), "turretID");
            if (expose == null || turretId == null) throw new MissingMemberException("[NMC-MP] Native turret save/identity target unresolved");
            harmony.Patch(expose, postfix: new HarmonyMethod(typeof(NativeTurretIdleRandom), nameof(Expose)));
            Log.Message("[NMC-MP] native turret two idle Unity float draws routed to Verse Rand only during multiplayer simulation.");
            Log.Message("[NMC-MP] native turret rotation/target/recoil preserved through multiplayer save and reload.");
        }
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var result = new List<CodeInstruction>(instructions);
            int count = 0;
            foreach (var i in result)
                if (i.opcode == OpCodes.Call && (Equals(i.operand, unityRange) || NativeRandomCompatibility.KnownFloat(i.operand as MethodInfo, "Range", 2)))
                { i.operand = AccessTools.Method(typeof(NativeTurretIdleRandom), nameof(Range)); count++; }
            if (count != 2) throw new InvalidOperationException("[NMC-MP] Native turret expected exactly two Unity float Range calls; found " + count + "; only original Unity or reviewed Meow assembly/type/signature/MVID helper accepted");
            return result;
        }
        static float Range(float min, float max)
            => MP.IsInMultiplayer && !MP.InInterface ? Rand.Range(min, max) : originalUnityRange(min, max);
        static void Expose(ThingComp __instance, ref float ____curRotation, ref float ____targetRotation, ref float ____curRecoilDistance)
        {
            if (!MP.IsInMultiplayer) return;
            // Multiple weapons share the parent Scribe node; use the native ID
            // exactly as its target/cooldown/weapon keys do, avoiding collisions.
            string key = "Turret_" + (turretId.GetValue(__instance.props) as string ?? "anon_") + "_nmcMP";
            Scribe_Values.Look(ref ____curRotation, key + "CurrentRotation");
            Scribe_Values.Look(ref ____targetRotation, key + "TargetRotation");
            Scribe_Values.Look(ref ____curRecoilDistance, key + "RecoilDistance");
        }
    }
}
