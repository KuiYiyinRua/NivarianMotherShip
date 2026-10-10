using System;
using HarmonyLib;
using RimWorld;
using Verse;

namespace NivarianCommander
{
    // Books/art store narrative data, not their temporary author pawn. A story author must
    // not take a real world pawn or create live family links that cannot survive saving.
    internal static class PointShopNarrativeSafety
    {
        [ThreadStatic] static int stockDepth;
        [ThreadStatic] static int narrativeDepth;
        internal static bool GeneratingStock => stockDepth > 0;
        internal static bool GeneratingNarrative => stockDepth > 0 && narrativeDepth > 0;
        internal static IDisposable EnterStockGeneration() => new StockScope();
        internal static void EnterNarrative() => narrativeDepth++;
        internal static void ExitNarrative() => narrativeDepth--;

        sealed class StockScope : IDisposable
        {
            bool disposed;
            public StockScope() { stockDepth++; }
            public void Dispose()
            {
                if (disposed) return;
                disposed = true;
                stockDepth--;
            }
        }
    }

    [HarmonyPatch(typeof(TaleData_Pawn), nameof(TaleData_Pawn.GenerateRandom))]
    internal static class PointShopNarrativeScopePatch
    {
        static void Prefix(out bool __state)
        {
            __state = PointShopNarrativeSafety.GeneratingStock;
            if (__state) PointShopNarrativeSafety.EnterNarrative();
        }

        static Exception Finalizer(Exception __exception, bool __state)
        {
            if (__state) PointShopNarrativeSafety.ExitNarrative();
            return __exception;
        }
    }

    [HarmonyPatch(typeof(PawnGenerator), nameof(PawnGenerator.GeneratePawn), new[] { typeof(PawnGenerationRequest) })]
    internal static class PointShopNarrativePawnPatch
    {
        static void Prefix(ref PawnGenerationRequest request)
        {
            if (!PointShopNarrativeSafety.GeneratingNarrative) return;
            request.ForceGenerateNewPawn = true;
            request.CanGeneratePawnRelations = false;
        }
    }
}
