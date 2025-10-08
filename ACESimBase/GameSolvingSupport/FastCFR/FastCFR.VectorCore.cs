#nullable enable
using System;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using System.Runtime.InteropServices;
using ACESimBase.Util.Collections;

namespace ACESimBase.GameSolvingSupport.FastCFR
{
    public sealed class FastCFRVectorRegionOptions
    {
        public bool EnableVectorRegion { get; set; } = false;
        public int VectorWidth { get; set; } = 4;
        public bool EnableVectorProbabilityProviders { get; set; } = true;
    }

    public delegate void FastCFRProbProviderVec(
        ref FastCFRVecContext ctx,
        byte outcomeIndexOneBased,
        Span<double> probabilitiesByLane);

    public readonly struct FastCFRNodeVecResult
    {
        public readonly double[][] UtilitiesByPlayerByLane;
        public readonly FloatSet[] CustomByLane;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public FastCFRNodeVecResult(double[][] utilitiesByPlayerByLane, FloatSet[] customByLane)
        {
            UtilitiesByPlayerByLane = utilitiesByPlayerByLane ?? Array.Empty<double[]>();
            CustomByLane = customByLane ?? Array.Empty<FloatSet>();
        }

        public int NumPlayers => UtilitiesByPlayerByLane.Length;
        public int NumLanes => UtilitiesByPlayerByLane.Length == 0 ? 0 : UtilitiesByPlayerByLane[0].Length;

        public static FastCFRNodeVecResult Zero(int numPlayers, int lanes)
        {
            var u = new double[numPlayers][];
            for (int p = 0; p < numPlayers; p++)
                u[p] = new double[lanes];
            var c = new FloatSet[lanes];
            return new FastCFRNodeVecResult(u, c);
        }
    }

    public ref struct FastCFRVecContext
    {
        public int IterationNumber;
        public byte OptimizedPlayerIndex;
        public double SamplingCorrection;

        public Span<double> ReachSelf;
        public Span<double> ReachOpp;
        public Span<double> ReachChance;

        public Span<byte> ActiveMask;
        public Span<int> ScenarioIndex;

        public Func<byte, double>? Rand01ForDecision;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool AnyActive()
        {
            var m = ActiveMask;
            for (int i = 0; i < m.Length; i++)
                if (m[i] != 0) return true;
            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int ActiveCount()
        {
            int n = 0;
            var m = ActiveMask;
            for (int i = 0; i < m.Length; i++)
                if (m[i] != 0) n++;
            return n;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void ClearInactiveReaches()
        {
            var m = ActiveMask;
            for (int i = 0; i < m.Length; i++)
            {
                if (m[i] == 0)
                {
                    ReachSelf[i] = 0.0;
                    ReachOpp[i] = 0.0;
                    ReachChance[i] = 0.0;
                }
            }
        }
    }

    public interface IFastCFRNodeVec
    {
        // Freeze per-lane policies for this iteration.
        // Each inner array is the action-probability vector for one lane.
        void InitializeIterationVec(
            double[][] ownerCurrentPolicyByLane,
            double[][] opponentTraversalPolicyByLane);

        FastCFRNodeVecResult GoVec(ref FastCFRVecContext ctx);
    }


    public readonly struct FastCFRVisitStepVec
    {
        public readonly byte ActionIndex;
        public readonly Func<IFastCFRNodeVec> ChildAccessor;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public FastCFRVisitStepVec(byte actionIndex, Func<IFastCFRNodeVec> childAccessor)
        {
            ActionIndex = actionIndex;
            ChildAccessor = childAccessor ?? throw new ArgumentNullException(nameof(childAccessor));
        }
    }

    public readonly struct FastCFRVisitProgramVec
    {
        public readonly FastCFRVisitStepVec[] Steps;
        public readonly int NumPlayers;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public FastCFRVisitProgramVec(FastCFRVisitStepVec[] steps, int numPlayers)
        {
            Steps = steps ?? Array.Empty<FastCFRVisitStepVec>();
            NumPlayers = numPlayers;
        }
    }

    public readonly struct FastCFRChanceStepVec
    {
        public readonly byte OutcomeIndexOneBased;
        public readonly Func<IFastCFRNodeVec> ChildAccessor;
        public readonly double StaticProbability;
        public readonly FastCFRProbProviderVec? ProbabilityProvider;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public FastCFRChanceStepVec(byte outcomeIndexOneBased, Func<IFastCFRNodeVec> childAccessor, double staticProbability)
        {
            OutcomeIndexOneBased = outcomeIndexOneBased;
            ChildAccessor = childAccessor ?? throw new ArgumentNullException(nameof(childAccessor));
            StaticProbability = staticProbability;
            ProbabilityProvider = null;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public FastCFRChanceStepVec(byte outcomeIndexOneBased, Func<IFastCFRNodeVec> childAccessor, FastCFRProbProviderVec probabilityProvider)
        {
            OutcomeIndexOneBased = outcomeIndexOneBased;
            ChildAccessor = childAccessor ?? throw new ArgumentNullException(nameof(childAccessor));
            StaticProbability = -1.0;
            ProbabilityProvider = probabilityProvider ?? throw new ArgumentNullException(nameof(probabilityProvider));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void FillProbabilities(ref FastCFRVecContext ctx, Span<double> perLaneProbabilities)
        {
            if (ProbabilityProvider is null)
                perLaneProbabilities.Fill(StaticProbability);
            else
                ProbabilityProvider(ref ctx, OutcomeIndexOneBased, perLaneProbabilities);
        }
    }

    public readonly struct FastCFRChanceVisitProgramVec
    {
        public readonly FastCFRChanceStepVec[] Steps;
        public readonly int NumPlayers;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public FastCFRChanceVisitProgramVec(FastCFRChanceStepVec[] steps, int numPlayers)
        {
            Steps = steps ?? Array.Empty<FastCFRChanceStepVec>();
            NumPlayers = numPlayers;
        }
    }

    internal static class FastCFRVecCapabilities
    {
        public static bool AvxAvailable => Avx.IsSupported;
        public static bool Sse2Available => Sse2.IsSupported;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int EffectiveVectorWidth(FastCFRVectorRegionOptions? opts)
        {
            int requested = Math.Clamp(opts?.VectorWidth ?? 4, 1, 8);

            if (AvxAvailable)
                return Math.Min(4, requested);

            if (Sse2Available)
                return Math.Min(2, requested);

            return 1;
        }
    }

    internal static class FastCFRVecMath
    {
        public static void MulAccumulateMasked(ReadOnlySpan<double> a, ReadOnlySpan<double> b, ReadOnlySpan<byte> mask, Span<double> dst)
        {
            int n = a.Length;
            if (n == 0) return;

            if (Avx.IsSupported)
            {
                int stride = Vector256<double>.Count;
                int i = 0;
                while (i <= n - stride)
                {
                    var va = Vector256.LoadUnsafe(ref MemoryMarshal.GetReference(a), (nuint)i);
                    var vb = Vector256.LoadUnsafe(ref MemoryMarshal.GetReference(b), (nuint)i);
                    var vm = LoadMaskSegmentAsDouble256(mask, i);

                    var mul = Avx.Multiply(va, vb);
                    var masked = Avx.Multiply(mul, vm);

                    var vdst = Vector256.LoadUnsafe(ref MemoryMarshal.GetReference(dst), (nuint)i);
                    vdst = Avx.Add(vdst, masked);
                    vdst.StoreUnsafe(ref MemoryMarshal.GetReference(dst), (nuint)i);

                    i += stride;
                }
                for (; i < n; i++)
                    if (mask[i] != 0) dst[i] += a[i] * b[i];

                return;
            }

            if (Sse2.IsSupported)
            {
                int stride = Vector128<double>.Count;
                int i = 0;
                while (i <= n - stride)
                {
                    var va = Vector128.LoadUnsafe(ref MemoryMarshal.GetReference(a), (nuint)i);
                    var vb = Vector128.LoadUnsafe(ref MemoryMarshal.GetReference(b), (nuint)i);
                    var vm = LoadMaskSegmentAsDouble128(mask, i);

                    var mul = Sse2.Multiply(va, vb);
                    var masked = Sse2.Multiply(mul, vm);

                    var vdst = Vector128.LoadUnsafe(ref MemoryMarshal.GetReference(dst), (nuint)i);
                    vdst = Sse2.Add(vdst, masked);
                    vdst.StoreUnsafe(ref MemoryMarshal.GetReference(dst), (nuint)i);

                    i += stride;
                }
                for (; i < n; i++)
                    if (mask[i] != 0) dst[i] += a[i] * b[i];

                return;
            }

            for (int i = 0; i < n; i++)
                if (mask[i] != 0) dst[i] += a[i] * b[i];
        }

        public static void ScaleInPlaceMasked(Span<double> x, ReadOnlySpan<double> factor, ReadOnlySpan<byte> mask)
        {
            int n = x.Length;
            if (n == 0) return;

            if (Avx.IsSupported)
            {
                int stride = Vector256<double>.Count;
                int i = 0;
                while (i <= n - stride)
                {
                    var vx = Vector256.LoadUnsafe(ref MemoryMarshal.GetReference(x), (nuint)i);
                    var vf = Vector256.LoadUnsafe(ref MemoryMarshal.GetReference(factor), (nuint)i);
                    var vm = LoadMaskSegmentAsDouble256(mask, i);

                    var scaled = Avx.Multiply(vx, vf);
                    var delta = Avx.Subtract(scaled, vx);
                    var maskedDelta = Avx.Multiply(delta, vm);
                    vx = Avx.Add(vx, maskedDelta);

                    vx.StoreUnsafe(ref MemoryMarshal.GetReference(x), (nuint)i);
                    i += stride;
                }
                for (; i < n; i++)
                    if (mask[i] != 0) x[i] *= factor[i];

                return;
            }

            if (Sse2.IsSupported)
            {
                int stride = Vector128<double>.Count;
                int i = 0;
                while (i <= n - stride)
                {
                    var vx = Vector128.LoadUnsafe(ref MemoryMarshal.GetReference(x), (nuint)i);
                    var vf = Vector128.LoadUnsafe(ref MemoryMarshal.GetReference(factor), (nuint)i);
                    var vm = LoadMaskSegmentAsDouble128(mask, i);

                    var scaled = Sse2.Multiply(vx, vf);
                    var delta = Sse2.Subtract(scaled, vx);
                    var maskedDelta = Sse2.Multiply(delta, vm);
                    vx = Sse2.Add(vx, maskedDelta);

                    vx.StoreUnsafe(ref MemoryMarshal.GetReference(x), (nuint)i);
                    i += stride;
                }
                for (; i < n; i++)
                    if (mask[i] != 0) x[i] *= factor[i];

                return;
            }

            for (int i = 0; i < n; i++)
                if (mask[i] != 0) x[i] *= factor[i];
        }

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

            // Fallback
            for (int a = 0; a < weights.Length; a++)
            {
                var laneVals = perActionLaneValues[a];
                double w = weights[a];
                for (int k = 0; k < lanes; k++)
                    if (mask[k] != 0)
                        resultPerLane[k] += laneVals[k] * w;
            }
        }


        public static double ReduceSumMasked(ReadOnlySpan<double> x, ReadOnlySpan<byte> mask)
        {
            int n = x.Length;
            double sum = 0.0;

            if (Avx.IsSupported)
            {
                int stride = Vector256<double>.Count;
                var acc = Vector256<double>.Zero;
                int i = 0;
                while (i <= n - stride)
                {
                    var vx = Vector256.LoadUnsafe(ref MemoryMarshal.GetReference(x), (nuint)i);
                    var vm = LoadMaskSegmentAsDouble256(mask, i);
                    var vmasked = Avx.Multiply(vx, vm);
                    acc = Avx.Add(acc, vmasked);
                    i += stride;
                }
                sum += HorizontalAdd256(acc);
                for (; i < n; i++)
                    if (mask[i] != 0) sum += x[i];

                return sum;
            }

            if (Sse2.IsSupported)
            {
                int stride = Vector128<double>.Count;
                var acc = Vector128<double>.Zero;
                int i = 0;
                while (i <= n - stride)
                {
                    var vx = Vector128.LoadUnsafe(ref MemoryMarshal.GetReference(x), (nuint)i);
                    var vm = LoadMaskSegmentAsDouble128(mask, i);
                    var vmasked = Sse2.Multiply(vx, vm);
                    acc = Sse2.Add(acc, vmasked);
                    i += stride;
                }
                sum += HorizontalAdd128(acc);
                for (; i < n; i++)
                    if (mask[i] != 0) sum += x[i];

                return sum;
            }

            for (int i = 0; i < n; i++)
                if (mask[i] != 0) sum += x[i];
            return sum;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Vector256<double> LoadMaskSegmentAsDouble256(ReadOnlySpan<byte> mask, int offset)
        {
            double d0 = mask[offset + 0] != 0 ? 1.0 : 0.0;
            double d1 = mask[offset + 1] != 0 ? 1.0 : 0.0;
            double d2 = mask[offset + 2] != 0 ? 1.0 : 0.0;
            double d3 = mask[offset + 3] != 0 ? 1.0 : 0.0;
            return Vector256.Create(d0, d1, d2, d3);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Vector128<double> LoadMaskSegmentAsDouble128(ReadOnlySpan<byte> mask, int offset)
        {
            double d0 = mask[offset + 0] != 0 ? 1.0 : 0.0;
            double d1 = mask[offset + 1] != 0 ? 1.0 : 0.0;
            return Vector128.Create(d0, d1);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static double HorizontalAdd256(Vector256<double> v)
        {
            var hi = Avx.ExtractVector128(v, 1);
            var lo = v.GetLower();
            var sum2 = Sse2.Add(hi, lo);
            return sum2.GetElement(0) + sum2.GetElement(1);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static double HorizontalAdd128(Vector128<double> v)
        {
            return v.GetElement(0) + v.GetElement(1);
        }
    }
}
