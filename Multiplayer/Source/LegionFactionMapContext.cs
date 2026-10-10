using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse.AI;
using Verse.AI.Group;

namespace NivarianCommander.MultiplayerAdapter
{
    public static class LegionFactionMapContext
    {
        static readonly MethodInfo EndJob = AccessTools.DeclaredMethod(typeof(Pawn_JobTracker),
            nameof(Pawn_JobTracker.EndCurrentJob), new[] { typeof(JobCondition), typeof(bool), typeof(bool) });
        static readonly MethodInfo ScopedEndJob = AccessTools.DeclaredMethod(typeof(LegionFactionMapContext), nameof(EndInLordMap));

        public static void Install()
        {
            var notify = AccessTools.DeclaredMethod(typeof(Lord), nameof(Lord.Notify_FactionRelationsChanged),
                new[] { typeof(Faction), typeof(FactionRelationKind) });
            if (notify == null || EndJob == null || ScopedEndJob == null)
                throw new MissingMethodException("[NMC-MP] Native legion faction-notification callsite unresolved");
            new Harmony("local.nivarian.commander.legion-faction-map").Patch(notify,
                transpiler: new HarmonyMethod(typeof(LegionFactionMapContext), nameof(Transpiler)));
        }

        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var source = instructions.ToList();
            if (source.Count(i => i.Calls(EndJob)) != 1)
                throw new InvalidOperationException("[NMC-MP] Expected exactly one native Lord EndCurrentJob callsite");
            foreach (var instruction in source)
            {
                if (!instruction.Calls(EndJob)) { yield return instruction; continue; }
                // Native call has tracker + the three unchanged arguments on the stack.
                // Add this Lord without re-entering Notify or duplicating its Harmony chain.
                if (instruction.blocks.Count != 0)
                    throw new InvalidOperationException("[NMC-MP] Unexpected exception boundary at native Lord job restart");
                var call = new CodeInstruction(instruction) { opcode = OpCodes.Call, operand = ScopedEndJob };
                var owner = new CodeInstruction(OpCodes.Ldarg_0);
                owner.labels.AddRange(call.labels); call.labels.Clear();
                yield return owner;
                yield return call;
            }
        }

        static void EndInLordMap(Pawn_JobTracker tracker, JobCondition condition,
            bool startNewJob, bool canReturnToPool, Lord lord)
        {
            if (!MP.IsInMultiplayer || !(lord.LordJob is LordJob_LegionAssault))
            { tracker.EndCurrentJob(condition, startNewJob, canReturnToPool); return; }
            EndWithMapContext(tracker, condition, startNewJob, canReturnToPool, lord);
        }

        static void EndWithMapContext(Pawn_JobTracker tracker, JobCondition condition,
            bool startNewJob, bool canReturnToPool, Lord lord)
        {
            // The shared one-toil NMC legion graph has no transitions. Only its
            // synchronous job restart needs the owning map's clock and random scope.
            ProjectionContext.Run(lord.Map,
                () => tracker.EndCurrentJob(condition, startNewJob, canReturnToPool));
        }
    }
}
