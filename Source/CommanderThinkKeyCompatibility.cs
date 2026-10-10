using System;
using HarmonyLib;
using Verse;
using Verse.AI;

namespace NivarianCommander
{
    // Keep the one-child priority behavior, with a name that cannot reuse the
    // vanilla priority-node key when this mod is first added to an old save.
    public sealed class ThinkNode_CommanderDuty : ThinkNode_Priority { }

    internal static class CommanderThinkKeyCompatibility
    {
        public static void Install()
        {
            // Def reference resolution runs before ExecuteWhenFinished. This
            // narrowly scoped hook must therefore be installed in Mod's constructor.
            new Harmony("local.nivarian.mothership.commander.thinkkeys").Patch(
                AccessTools.Method(typeof(ThinkTreeKeyAssigner), "NextUnusedKeyFor"),
                prefix: new HarmonyMethod(typeof(CommanderThinkKeyCompatibility), nameof(Before)),
                finalizer: new HarmonyMethod(typeof(CommanderThinkKeyCompatibility), nameof(After)));
        }

        static void Before(ThinkNode node, out bool __state)
        {
            __state = false;
            if (node == null || node.GetType().Assembly != typeof(CommanderMod).Assembly) return;
            // Native ThinkNode_Duty embeds every DutyDef into several trees. Its
            // collision resolver consumes the tree's Rand sequence; extra duties
            // would otherwise change later vanilla job-giver keys on cold load.
            // Still let vanilla allocate/check the key, but isolate our own draws.
            Rand.PushState(GenText.StableStringHash("NMC/ThinkSaveKey/" + node.GetType().FullName));
            __state = true;
        }

        static Exception After(Exception __exception, bool __state)
        {
            if (__state) Rand.PopState();
            return __exception;
        }
    }
}
