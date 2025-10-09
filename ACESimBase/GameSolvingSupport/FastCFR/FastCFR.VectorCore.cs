#nullable enable

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using ACESimBase.GameSolvingSupport.GameTree;
using ACESimBase.Util.Collections;

namespace ACESimBase.GameSolvingSupport.FastCFR
{
    public sealed class FastCFRVectorRegionOptions
    {
        public bool EnableVectorRegion { get; set; } = true;
        public int PreferredVectorWidth { get; set; } = 0; // 0 = pick best available
        public bool EnableVectorProbabilityProviders { get; set; } = false; // stub
    }

    public static class FastCFRVecCapabilities
    {
        public static int EffectiveVectorWidth(FastCFRVectorRegionOptions? options)
        {
            int preferred = options?.PreferredVectorWidth ?? 0;
            int hw = Avx.IsSupported ? 4 : (Sse2.IsSupported ? 2 : 1);
            if (preferred <= 0) return hw;
            return Math.Max(1, Math.Min(preferred, hw));
        }
    }

    public delegate void FastCFRProbProviderVec(ref FastCFRVecContext ctx, byte outcomeIndexOneBased, Span<double> pLane);

    public interface IFastCFRNodeVec
    {
        void InitializeIterationVec(
            double[][] ownerCurrentPolicyByLane,
            double[][] opponentTraversalPolicyByLane);

        FastCFRNodeVecResult GoVec(ref FastCFRVecContext ctx);
    }

    public readonly struct FastCFRNodeVecResult
    {
        public readonly double[][] UtilitiesByPlayerByLane; // [player][lane]
        public readonly FloatSet[] CustomByLane;            // [lane]
        public FastCFRNodeVecResult(double[][] utilitiesByPlayerByLane, FloatSet[] customByLane)
        {
            UtilitiesByPlayerByLane = utilitiesByPlayerByLane;
            CustomByLane = customByLane;
        }
    }

    public ref struct FastCFRVecContext
    {
        public int IterationNumber;
        public byte OptimizedPlayerIndex;
        public double[] ReachSelf;   // [lane]
        public double[] ReachOpp;    // [lane]
        public double[] ReachChance; // [lane]
        public byte[] ActiveMask;    // [lane] 0/1
        public int[] ScenarioIndex;  // [lane] (must be single-valued across lanes)
        public double SamplingCorrection;
        public Func<byte, double>? Rand01ForDecision;
    }

    public readonly struct FastCFRVisitStepVec
    {
        public readonly byte ActionIndex; // 0..NumActions-1
        public readonly Func<IFastCFRNodeVec> ChildAccessor;
        public FastCFRVisitStepVec(byte actionIndex, Func<IFastCFRNodeVec> childAccessor)
        {
            ActionIndex = actionIndex;
            ChildAccessor = childAccessor;
        }
    }

    public readonly struct FastCFRVisitProgramVec
    {
        public readonly FastCFRVisitStepVec[] Steps;
        public readonly int NumPlayers;
        public FastCFRVisitProgramVec(FastCFRVisitStepVec[] steps, int numPlayers)
        {
            Steps = steps;
            NumPlayers = numPlayers;
        }
    }

    public readonly struct FastCFRChanceStepVec
    {
        public readonly byte OutcomeIndexOneBased;
        public readonly Func<IFastCFRNodeVec> ChildAccessor;
        public readonly double StaticProbability; // >=0 means static; else use provider
        public readonly FastCFRProbProviderVec? ProbabilityProvider;

        public FastCFRChanceStepVec(byte outcomeIndexOneBased, Func<IFastCFRNodeVec> childAccessor, double staticProbability)
        {
            OutcomeIndexOneBased = outcomeIndexOneBased;
            ChildAccessor = childAccessor;
            StaticProbability = staticProbability;
            ProbabilityProvider = null;
        }

        public FastCFRChanceStepVec(byte outcomeIndexOneBased, Func<IFastCFRNodeVec> childAccessor, FastCFRProbProviderVec provider)
        {
            OutcomeIndexOneBased = outcomeIndexOneBased;
            ChildAccessor = childAccessor;
            StaticProbability = -1.0;
            ProbabilityProvider = provider;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void FillProbabilities(ref FastCFRVecContext ctx, Span<double> pLane)
        {
            if (ProbabilityProvider is null)
            {
                double p = StaticProbability;
                for (int k = 0; k < pLane.Length; k++)
                    pLane[k] = p;
            }
            else
            {
                ProbabilityProvider(ref ctx, OutcomeIndexOneBased, pLane);
            }
        }
    }

    public readonly struct FastCFRChanceVisitProgramVec
    {
        public readonly FastCFRChanceStepVec[] Steps;
        public readonly int NumPlayers;
        public FastCFRChanceVisitProgramVec(FastCFRChanceStepVec[] steps, int numPlayers)
        {
            Steps = steps;
            NumPlayers = numPlayers;
        }
    }

    internal static class FastCFRVecMath
    {
        // Existing helper (kept for compatibility)
        public static void DotPerLane(
            ReadOnlySpan<double> weights,
            double[][] perActionLaneValues,
            ReadOnlySpan<byte> mask,
            Span<double> resultPerLane)
        {
            int lanes = resultPerLane.Length;
            if (lanes == 0)
                return;

            for (int k = 0; k < lanes; k++)
                resultPerLane[k] = 0.0;

            if (Avx.IsSupported)
            {
                int stride = Vector256<double>.Count;
                Span<double> maskAsDouble = stackalloc double[lanes];
                for (int i = 0; i < lanes; i++)
                    maskAsDouble[i] = mask[i] != 0 ? 1.0 : 0.0;

                for (int a = 0; a < weights.Length; a++)
                {
                    double w = weights[a];
                    var wVec = Vector256.Create(w);
                    ReadOnlySpan<double> laneVals = perActionLaneValues[a];

                    int k = 0;
                    while (k <= lanes - stride)
                    {
                        var v = Vector256.LoadUnsafe(ref MemoryMarshal.GetReference(laneVals), (nuint)k);
                        var r = Vector256.LoadUnsafe(ref MemoryMarshal.GetReference(resultPerLane), (nuint)k);
                        var m = Vector256.LoadUnsafe(ref MemoryMarshal.GetReference(maskAsDouble), (nuint)k);

                        var term = Avx.Multiply(v, wVec);
                        term = Avx.Multiply(term, m);
                        r = Avx.Add(r, term);

                        r.StoreUnsafe(ref MemoryMarshal.GetReference(resultPerLane), (nuint)k);
                        k += stride;
                    }
                    for (; k < lanes; k++)
                        if (mask[k] != 0)
                            resultPerLane[k] += laneVals[k] * w;
                }
                return;
            }

            if (Sse2.IsSupported)
            {
                int stride = Vector128<double>.Count;
                Span<double> maskAsDouble = stackalloc double[lanes];
                for (int i = 0; i < lanes; i++)
                    maskAsDouble[i] = mask[i] != 0 ? 1.0 : 0.0;

                for (int a = 0; a < weights.Length; a++)
                {
                    double w = weights[a];
                    var wVec = Vector128.Create(w);
                    ReadOnlySpan<double> laneVals = perActionLaneValues[a];

                    int k = 0;
                    while (k <= lanes - stride)
                    {
                        var v = Vector128.LoadUnsafe(ref MemoryMarshal.GetReference(laneVals), (nuint)k);
                        var r = Vector128.LoadUnsafe(ref MemoryMarshal.GetReference(resultPerLane), (nuint)k);
                        var m = Vector128.LoadUnsafe(ref MemoryMarshal.GetReference(maskAsDouble), (nuint)k);

                        var term = Sse2.Multiply(v, wVec);
                        term = Sse2.Multiply(term, m);
                        r = Sse2.Add(r, term);

                        r.StoreUnsafe(ref MemoryMarshal.GetReference(resultPerLane), (nuint)k);
                        k += stride;
                    }
                    for (; k < lanes; k++)
                        if (mask[k] != 0)
                            resultPerLane[k] += laneVals[k] * w;
                }
                return;
            }

            for (int a = 0; a < weights.Length; a++)
            {
                var laneVals = perActionLaneValues[a];
                double w = weights[a];
                for (int k = 0; k < lanes; k++)
                    if (mask[k] != 0)
                        resultPerLane[k] += laneVals[k] * w;
            }
        }

        // New SIMD-friendly masked ops on contiguous buffers -----------------

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void AddMasked(Span<double> dst, ReadOnlySpan<double> src, ReadOnlySpan<byte> mask)
        {
            int n = dst.Length;
            if (n == 0) return;

            if (Avx.IsSupported)
            {
                int stride = Vector256<double>.Count;
                Span<double> mD = stackalloc double[n];
                for (int i = 0; i < n; i++) mD[i] = mask[i] != 0 ? 1.0 : 0.0;

                int i2 = 0;
                while (i2 <= n - stride)
                {
                    var d = Vector256.LoadUnsafe(ref MemoryMarshal.GetReference(dst), (nuint)i2);
                    var s = Vector256.LoadUnsafe(ref MemoryMarshal.GetReference(src), (nuint)i2);
                    var m = Vector256.LoadUnsafe(ref MemoryMarshal.GetReference(mD), (nuint)i2);
                    d = Avx.Add(d, Avx.Multiply(s, m));
                    d.StoreUnsafe(ref MemoryMarshal.GetReference(dst), (nuint)i2);
                    i2 += stride;
                }
                for (; i2 < n; i2++)
                    if (mask[i2] != 0) dst[i2] += src[i2];
                return;
            }

            if (Sse2.IsSupported)
            {
                int stride = Vector128<double>.Count;
                Span<double> mD = stackalloc double[n];
                for (int i = 0; i < n; i++) mD[i] = mask[i] != 0 ? 1.0 : 0.0;

                int i2 = 0;
                while (i2 <= n - stride)
                {
                    var d = Vector128.LoadUnsafe(ref MemoryMarshal.GetReference(dst), (nuint)i2);
                    var s = Vector128.LoadUnsafe(ref MemoryMarshal.GetReference(src), (nuint)i2);
                    var m = Vector128.LoadUnsafe(ref MemoryMarshal.GetReference(mD), (nuint)i2);
                    d = Sse2.Add(d, Sse2.Multiply(s, m));
                    d.StoreUnsafe(ref MemoryMarshal.GetReference(dst), (nuint)i2);
                    i2 += stride;
                }
                for (; i2 < n; i2++)
                    if (mask[i2] != 0) dst[i2] += src[i2];
                return;
            }

            for (int i = 0; i < n; i++)
                if (mask[i] != 0) dst[i] += src[i];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void MulAddMasked(Span<double> dst, ReadOnlySpan<double> src, ReadOnlySpan<double> weights, ReadOnlySpan<byte> mask)
        {
            int n = dst.Length;
            if (n == 0) return;

            if (Avx.IsSupported)
            {
                int stride = Vector256<double>.Count;
                Span<double> mD = stackalloc double[n];
                for (int i = 0; i < n; i++) mD[i] = mask[i] != 0 ? 1.0 : 0.0;

                int i2 = 0;
                while (i2 <= n - stride)
                {
                    var d = Vector256.LoadUnsafe(ref MemoryMarshal.GetReference(dst), (nuint)i2);
                    var s = Vector256.LoadUnsafe(ref MemoryMarshal.GetReference(src), (nuint)i2);
                    var w = Vector256.LoadUnsafe(ref MemoryMarshal.GetReference(weights), (nuint)i2);
                    var m = Vector256.LoadUnsafe(ref MemoryMarshal.GetReference(mD), (nuint)i2);

                    var term = Avx.Multiply(s, w);
                    term = Avx.Multiply(term, m);
                    d = Avx.Add(d, term);

                    d.StoreUnsafe(ref MemoryMarshal.GetReference(dst), (nuint)i2);
                    i2 += stride;
                }
                for (; i2 < n; i2++)
                    if (mask[i2] != 0) dst[i2] += src[i2] * weights[i2];
                return;
            }

            if (Sse2.IsSupported)
            {
                int stride = Vector128<double>.Count;
                Span<double> mD = stackalloc double[n];
                for (int i = 0; i < n; i++) mD[i] = mask[i] != 0 ? 1.0 : 0.0;

                int i2 = 0;
                while (i2 <= n - stride)
                {
                    var d = Vector128.LoadUnsafe(ref MemoryMarshal.GetReference(dst), (nuint)i2);
                    var s = Vector128.LoadUnsafe(ref MemoryMarshal.GetReference(src), (nuint)i2);
                    var w = Vector128.LoadUnsafe(ref MemoryMarshal.GetReference(weights), (nuint)i2);
                    var m = Vector128.LoadUnsafe(ref MemoryMarshal.GetReference(mD), (nuint)i2);

                    var term = Sse2.Multiply(s, w);
                    term = Sse2.Multiply(term, m);
                    d = Sse2.Add(d, term);

                    d.StoreUnsafe(ref MemoryMarshal.GetReference(dst), (nuint)i2);
                    i2 += stride;
                }
                for (; i2 < n; i2++)
                    if (mask[i2] != 0) dst[i2] += src[i2] * weights[i2];
                return;
            }

            for (int i = 0; i < n; i++)
                if (mask[i] != 0) dst[i] += src[i] * weights[i];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void SubMaskedInto(Span<double> dst, ReadOnlySpan<double> x, ReadOnlySpan<double> y, ReadOnlySpan<byte> mask)
        {
            int n = dst.Length;
            if (n == 0) return;

            if (Avx.IsSupported)
            {
                int stride = Vector256<double>.Count;
                Span<double> mD = stackalloc double[n];
                for (int i = 0; i < n; i++) mD[i] = mask[i] != 0 ? 1.0 : 0.0;

                int i2 = 0;
                while (i2 <= n - stride)
                {
                    var xv = Vector256.LoadUnsafe(ref MemoryMarshal.GetReference(x), (nuint)i2);
                    var yv = Vector256.LoadUnsafe(ref MemoryMarshal.GetReference(y), (nuint)i2);
                    var m = Vector256.LoadUnsafe(ref MemoryMarshal.GetReference(mD), (nuint)i2);
                    var r = Avx.Multiply(Avx.Subtract(xv, yv), m);
                    r.StoreUnsafe(ref MemoryMarshal.GetReference(dst), (nuint)i2);
                    i2 += stride;
                }
                for (; i2 < n; i2++)
                    dst[i2] = mask[i2] != 0 ? (x[i2] - y[i2]) : 0.0;
                return;
            }

            if (Sse2.IsSupported)
            {
                int stride = Vector128<double>.Count;
                Span<double> mD = stackalloc double[n];
                for (int i = 0; i < n; i++) mD[i] = mask[i] != 0 ? 1.0 : 0.0;

                int i2 = 0;
                while (i2 <= n - stride)
                {
                    var xv = Vector128.LoadUnsafe(ref MemoryMarshal.GetReference(x), (nuint)i2);
                    var yv = Vector128.LoadUnsafe(ref MemoryMarshal.GetReference(y), (nuint)i2);
                    var m = Vector128.LoadUnsafe(ref MemoryMarshal.GetReference(mD), (nuint)i2);
                    var r = Sse2.Multiply(Sse2.Subtract(xv, yv), m);
                    r.StoreUnsafe(ref MemoryMarshal.GetReference(dst), (nuint)i2);
                    i2 += stride;
                }
                for (; i2 < n; i2++)
                    dst[i2] = mask[i2] != 0 ? (x[i2] - y[i2]) : 0.0;
                return;
            }

            for (int i = 0; i < n; i++)
                dst[i] = mask[i] != 0 ? (x[i] - y[i]) : 0.0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double[][] AllocateJagged(int outer, int inner)
        {
            var arr = new double[outer][];
            for (int i = 0; i < outer; i++)
                arr[i] = new double[inner];
            return arr;
        }
    }
}
#nullable restore
