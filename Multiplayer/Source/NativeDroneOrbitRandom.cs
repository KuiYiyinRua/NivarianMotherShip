using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace NivarianCommander.MultiplayerAdapter
{
    // N4MCN2 proved equal inputs but different native orbit directions before
    // physics/projectile divergence. Replace only that simulation random draw.
    public static class NativeDroneOrbitRandom
    {
        static MethodInfo unityValue;
        static Func<float> originalUnityValue;
        public static void Install()
        {
            var type = AccessTools.TypeByName("Nivarian.NivarianDrones.NivarianDroneComps.NivarianDroneComp_MovingBase");
            var target = AccessTools.DeclaredMethod(type, "OrbitTarget", new[] { typeof(LocalTargetInfo), typeof(float) });
            unityValue = AccessTools.PropertyGetter(AccessTools.TypeByName("UnityEngine.Random"), "value");
            if (target == null || unityValue == null || unityValue.ReturnType != typeof(float))
                throw new MissingMethodException("[NMC-MP] Required original OrbitTarget/Unity value target unresolved");
            originalUnityValue = (Func<float>)Delegate.CreateDelegate(typeof(Func<float>), unityValue);
            new Harmony("local.nivarian.commander.native-orbit-random").Patch(target,
                transpiler: new HarmonyMethod(typeof(NativeDroneOrbitRandom), nameof(Transpiler)) { after = NativeRandomCompatibility.AfterOwners });
            Log.Message("[NMC-MP] native OrbitTarget single Unity random draw routed to Verse Rand only during multiplayer simulation.");
        }
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var result = new List<CodeInstruction>(instructions);
            var helper = AccessTools.Method(typeof(NativeDroneOrbitRandom), nameof(Value));
            int replaced = 0;
            foreach (var instruction in result)
                if (instruction.opcode == OpCodes.Call && (Equals(instruction.operand, unityValue) || NativeRandomCompatibility.KnownFloat(instruction.operand as MethodInfo, "Value", 0)))
                { instruction.operand = helper; replaced++; }
            if (replaced != 1) throw new InvalidOperationException("[NMC-MP] Native OrbitTarget expected exactly one Unity value call; found " + replaced + "; only original Unity or reviewed Meow assembly/type/signature/MVID helper accepted");
            return result;
        }
        static float Value() => MP.IsInMultiplayer && !MP.InInterface ? Rand.Value : originalUnityValue();
    }
}
