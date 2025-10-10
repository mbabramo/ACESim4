// ACESimBase/GameSolvingSupport/FastCFR/FastCFR.cs
using System;
using System.Buffers;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using ACESimBase.GameSolvingSupport.GameTree;
using ACESimBase.Util.Collections;

namespace ACESimBase.GameSolvingSupport.FastCFR
{
    // ----------------------------------------------------------------------
    // Result & iteration context
    // ----------------------------------------------------------------------
    public readonly struct FastCFRNodeResult
    {
        public readonly double[] Utilities; // per non-chance player
        public readonly FloatSet Custom;
        public FastCFRNodeResult(double[] utilities, FloatSet custom)
        {
            Utilities = utilities;
            Custom = custom;
        }
        public static FastCFRNodeResult Zero(int numPlayers) => new FastCFRNodeResult(new double[numPlayers], default);
    }

    public ref struct FastCFRIterationContext
    {
        public int IterationNumber;
        public byte OptimizedPlayerIndex; // whose regrets we update this iteration
        public double ReachSelf;   // π_i for optimized player
        public double ReachOpp;    // product of other (non-chance) players' reach
        public double ReachChance; // π_c
        public double SamplingCorrection; // importance weight
        public int ScenarioIndex;  // scenario for finals
        public bool SuppressMath;  // walk structure but ignore math when true
        public Func<byte, double> Rand01ForDecision;
    }

    // ----------------------------------------------------------------------
    // Public node API
    // ----------------------------------------------------------------------
    public delegate FastCFRNodeResult FastCFRCall(ref FastCFRIterationContext ctx);
    public delegate double FastCFRProbProvider(ref FastCFRIterationContext ctx);

    public interface IFastCFRNode
    {
        void InitializeIteration(ReadOnlySpan<double> ownerCurrentPolicy,
                                 ReadOnlySpan<double> opponentTraversalPolicy);
        FastCFRNodeResult Go(ref FastCFRIterationContext ctx);
    }

    public interface IFastCFRInformationSet : IFastCFRNode
    {
        ReadOnlySpan<double> SumRegretTimesInversePi { get; }
        ReadOnlySpan<double> SumInversePi { get; }
        ReadOnlySpan<double> LastCumulativeStrategyIncrements { get; }
    }

    public abstract class FastCFRInformationSetBase : IFastCFRInformationSet
    {
        public abstract void InitializeIteration(ReadOnlySpan<double> ownerCurrentPolicy, ReadOnlySpan<double> opponentTraversalPolicy);
        public abstract FastCFRNodeResult Go(ref FastCFRIterationContext ctx);
        internal abstract void BindChildrenAfterFinalize();

        public abstract ReadOnlySpan<double> SumRegretTimesInversePi { get; }
        public abstract ReadOnlySpan<double> SumInversePi { get; }
        public abstract ReadOnlySpan<double> LastCumulativeStrategyIncrements { get; }
    }

    public abstract class FastCFRInformationSet : FastCFRInformationSetBase { }

    // ----------------------------------------------------------------------
    // Visit programs
    // ----------------------------------------------------------------------
    public enum FastCFRVisitStepKind : byte { ChildForAction }

    public readonly struct FastCFRVisitStep
    {
        public readonly FastCFRVisitStepKind Kind;
        public readonly byte ActionIndex; // 0..NumActions-1
        public readonly Func<IFastCFRNode> ChildAccessor; // bound after finalize
        public FastCFRVisitStep(FastCFRVisitStepKind kind, byte actionIndex, Func<IFastCFRNode> childAccessor)
        {
            Kind = kind;
            ActionIndex = actionIndex;
            ChildAccessor = childAccessor;
        }
    }

    public readonly struct FastCFRVisitProgram
    {
        public readonly FastCFRVisitStep[] Steps;
        public readonly int NumPlayers;
        public FastCFRVisitProgram(FastCFRVisitStep[] steps, int numPlayers)
        {
            Steps = steps;
            NumPlayers = numPlayers;
        }
    }

    // ----------------------------------------------------------------------
    // Chance programs
    // ----------------------------------------------------------------------
    public readonly struct FastCFRChanceStep
    {
        public readonly Func<IFastCFRNode> ChildAccessor;
        public readonly double StaticProbability; // >=0 if static, else <0 meaning dynamic
        public readonly FastCFRProbProvider ProbabilityProvider; // null if static
        public FastCFRChanceStep(Func<IFastCFRNode> childAccessor, double staticProbability)
        {
            ChildAccessor = childAccessor;
            StaticProbability = staticProbability;
            ProbabilityProvider = null;
        }
        public FastCFRChanceStep(Func<IFastCFRNode> childAccessor, FastCFRProbProvider provider)
        {
            ChildAccessor = childAccessor;
            StaticProbability = -1.0;
            ProbabilityProvider = provider;
        }
        public double GetProbability(ref FastCFRIterationContext ctx)
            => ProbabilityProvider == null ? StaticProbability : ProbabilityProvider(ref ctx);
    }

    public readonly struct FastCFRChanceVisitProgram
    {
        public readonly FastCFRChanceStep[] Steps;
        public readonly int NumPlayers;
        public FastCFRChanceVisitProgram(FastCFRChanceStep[] steps, int numPlayers)
        {
            Steps = steps;
            NumPlayers = numPlayers;
        }
    }

    // ----------------------------------------------------------------------
    // Information set node (generic core + sealed specializations)
    // ----------------------------------------------------------------------
    public abstract class FastCFRInformationSetGeneric<TScalar> : FastCFRInformationSet
        where TScalar : struct
    {
        private const int StackallocQaMaxActions = 64; // hybrid scratch: small arities stay on stack

        public readonly byte PlayerIndex;
        public readonly byte DecisionIndex;
        public readonly byte NumActions;

        private readonly FastCFRVisitProgram[] _visits;
        private int _visitCounter;

        // Frozen policies per iteration (stored as TScalar)
        protected readonly TScalar[] _pSelf; // owner policy for this sweep
        protected readonly TScalar[] _pOpp;  // opponent traversal policy for this sweep

        // Tallies collected during traversal (stored as TScalar)
        protected readonly TScalar[] _sumRegretTimesInversePi;
        protected readonly TScalar[] _sumInversePi; // retained for compatibility; not updated in hot loop
        protected readonly TScalar[] _lastCumulativeStrategyInc;

        // Denominator is action-invariant -> keep scalar and expand on demand
        private double _sumInversePiShared;

        // Bound child arrays per visit (after finalize)
        private IFastCFRNode[][] _childrenByVisit;

        // Small reusable buffers
        private readonly int _numPlayers;
        private readonly double[] _zeroUtilities;
        private readonly double[] _workUtilities;

        // Reusable views for exporting tallies
        private readonly double[] _bufferRegretTimesInvPi;
        private readonly double[] _bufferInvPi;
        private readonly double[] _bufferLastCumStrat;

        // Persistent small vector scratch for float path (avoids per-iteration stackalloc)
        private readonly float[] _chunkF;

        protected FastCFRInformationSetGeneric(byte playerIndex, byte decisionIndex, byte numActions, FastCFRVisitProgram[] visits)
        {
            PlayerIndex = playerIndex;
            DecisionIndex = decisionIndex;
            NumActions = numActions;
            _visits = visits ?? Array.Empty<FastCFRVisitProgram>();
            _pSelf = new TScalar[numActions];
            _pOpp = new TScalar[numActions];
            _sumRegretTimesInversePi = new TScalar[numActions];
            _sumInversePi = new TScalar[numActions];
            _lastCumulativeStrategyInc = new TScalar[numActions];

            _numPlayers = _visits.Length == 0 ? 0 : _visits[0].NumPlayers;
            _zeroUtilities = _numPlayers == 0 ? Array.Empty<double>() : new double[_numPlayers];
            _workUtilities = _numPlayers == 0 ? Array.Empty<double>() : new double[_numPlayers];

            _bufferRegretTimesInvPi = new double[numActions];
            _bufferInvPi = new double[numActions];
            _bufferLastCumStrat = new double[numActions];

            _chunkF = new float[Math.Max(1, Vector<float>.Count)];
        }

        internal override void BindChildrenAfterFinalize()
        {
            _childrenByVisit = new IFastCFRNode[_visits.Length][];
            for (int v = 0; v < _visits.Length; v++)
            {
                var steps = _visits[v].Steps;
                var arr = new IFastCFRNode[steps.Length];
                for (int i = 0; i < steps.Length; i++)
                    arr[i] = steps[i].ChildAccessor();
                _childrenByVisit[v] = arr;
            }
        }

        public override void InitializeIteration(ReadOnlySpan<double> ownerCurrentPolicy, ReadOnlySpan<double> opponentTraversalPolicy)
        {
            if (ownerCurrentPolicy.Length != NumActions || opponentTraversalPolicy.Length != NumActions)
                throw new ArgumentException("Policy length mismatch for infoset initialization.");

            for (int a = 0; a < NumActions; a++)
            {
                _pSelf[a] = FromDouble(ownerCurrentPolicy[a]);
                _pOpp[a]  = FromDouble(opponentTraversalPolicy[a]);
                _sumRegretTimesInversePi[a] = FromDouble(0.0);
                _sumInversePi[a]            = FromDouble(0.0); // not used in accumulation; kept for compatibility
                _lastCumulativeStrategyInc[a] = FromDouble(0.0);
            }
            _sumInversePiShared = 0.0;
            _visitCounter = 0;
        }

        public override FastCFRNodeResult Go(ref FastCFRIterationContext ctx)
        {
            var visitIndex = _visitCounter++;
            var visit = _visits[visitIndex];
            var children = _childrenByVisit[visitIndex];
            bool ownerIsOptimized = PlayerIndex == ctx.OptimizedPlayerIndex;

            if (ctx.SuppressMath)
            {
                bool prior = ctx.SuppressMath;
                for (int i = 0; i < children.Length; i++)
                    children[i].Go(ref ctx);
                ctx.SuppressMath = prior;
                return new FastCFRNodeResult(_zeroUtilities, default);
            }

            var expectedU = _workUtilities;
            for (int i = 0; i < expectedU.Length; i++) expectedU[i] = 0.0;
            FloatSet expectedCustom = default;

            // Fast path when this infoset is NOT the owner being optimized:
            if (!ownerIsOptimized)
            {
                for (int i = 0; i < children.Length; i++)
                {
                    ref readonly var step = ref visit.Steps[i];
                    int ai = step.ActionIndex;

                    double w = ToDouble(_pOpp[ai]);

                    if (w <= double.Epsilon)
                    {
                        bool prior = ctx.SuppressMath; ctx.SuppressMath = true;
                        children[i].Go(ref ctx);
                        ctx.SuppressMath = prior;
                        continue;
                    }

                    double oldOpp = ctx.ReachOpp;
                    ctx.ReachOpp = oldOpp * w;
                    var child = children[i].Go(ref ctx);
                    ctx.ReachOpp = oldOpp;

                    var cu = child.Utilities;
                    for (int pl = 0; pl < expectedU.Length; pl++)
                        expectedU[pl] += w * cu[pl];
                    expectedCustom = expectedCustom.Plus(child.Custom.Times((float)w));
                }

                return new FastCFRNodeResult(expectedU, expectedCustom);
            }

            // Owner path: needs Qa. Use stackalloc for small arities, array-pool for large.
            if (NumActions <= StackallocQaMaxActions)
            {
                Span<double> qaStack = stackalloc double[StackallocQaMaxActions];
                var Qa = qaStack.Slice(0, NumActions);
                Qa.Clear();

                // Traverse children, fill Qa
                for (int i = 0; i < children.Length; i++)
                {
                    ref readonly var step = ref visit.Steps[i];
                    int ai = step.ActionIndex;

                    double w = ToDouble(_pSelf[ai]);

                    double oldSelf = ctx.ReachSelf;
                    ctx.ReachSelf = oldSelf * w;
                    var child = children[i].Go(ref ctx);
                    ctx.ReachSelf = oldSelf;

                    Qa[ai] = child.Utilities[PlayerIndex];

                    var cu = child.Utilities;
                    for (int pl = 0; pl < expectedU.Length; pl++)
                        expectedU[pl] += w * cu[pl];
                    expectedCustom = expectedCustom.Plus(child.Custom.Times((float)w));
                }

                // Owner update
                {
                    double inversePi = ctx.ReachOpp;
                    double piSelf = ctx.ReachSelf;

                    double V;
                    if (Vector.IsHardwareAccelerated)
                    {
                        if (typeof(TScalar) == typeof(double) && NumActions >= Vector<double>.Count)
                        {
                            var pSelfD = MemoryMarshal.Cast<TScalar, double>(_pSelf.AsSpan());
                            V = DotProductDouble(pSelfD, Qa);
                            SimdUpdateDouble(Qa, V, inversePi, piSelf);
                        }
                        else if (typeof(TScalar) == typeof(float) && NumActions >= Vector<float>.Count)
                        {
                            V = DotProductFloatChunked(Qa);
                            SimdUpdateFloatChunked(Qa, (float)V, (float)inversePi, (float)piSelf);
                        }
                        else
                        {
                            V = DotProductScalar(Qa);
                            ScalarUpdateTallies(Qa, V, inversePi, piSelf);
                        }
                    }
                    else
                    {
                        V = DotProductScalar(Qa);
                        ScalarUpdateTallies(Qa, V, inversePi, piSelf);
                    }

                    _sumInversePiShared += inversePi; // denominator is action-invariant
                }

                return new FastCFRNodeResult(expectedU, expectedCustom);
            }
            else
            {
                double[] qaRented = ArrayPool<double>.Shared.Rent(NumActions);
                var Qa = qaRented.AsSpan(0, NumActions);
                Qa.Clear();

                try
                {
                    // Traverse children, fill Qa
                    for (int i = 0; i < children.Length; i++)
                    {
                        ref readonly var step = ref visit.Steps[i];
                        int ai = step.ActionIndex;

                        double w = ToDouble(_pSelf[ai]);

                        double oldSelf = ctx.ReachSelf;
                        ctx.ReachSelf = oldSelf * w;
                        var child = children[i].Go(ref ctx);
                        ctx.ReachSelf = oldSelf;

                        Qa[ai] = child.Utilities[PlayerIndex];

                        var cu = child.Utilities;
                        for (int pl = 0; pl < expectedU.Length; pl++)
                            expectedU[pl] += w * cu[pl];
                        expectedCustom = expectedCustom.Plus(child.Custom.Times((float)w));
                    }

                    // Owner update
                    {
                        double inversePi = ctx.ReachOpp;
                        double piSelf = ctx.ReachSelf;

                        double V;
                        if (Vector.IsHardwareAccelerated)
                        {
                            if (typeof(TScalar) == typeof(double) && NumActions >= Vector<double>.Count)
                            {
                                var pSelfD = MemoryMarshal.Cast<TScalar, double>(_pSelf.AsSpan());
                                V = DotProductDouble(pSelfD, Qa);
                                SimdUpdateDouble(Qa, V, inversePi, piSelf);
                            }
                            else if (typeof(TScalar) == typeof(float) && NumActions >= Vector<float>.Count)
                            {
                                V = DotProductFloatChunked(Qa);
                                SimdUpdateFloatChunked(Qa, (float)V, (float)inversePi, (float)piSelf);
                            }
                            else
                            {
                                V = DotProductScalar(Qa);
                                ScalarUpdateTallies(Qa, V, inversePi, piSelf);
                            }
                        }
                        else
                        {
                            V = DotProductScalar(Qa);
                            ScalarUpdateTallies(Qa, V, inversePi, piSelf);
                        }

                        _sumInversePiShared += inversePi; // denominator is action-invariant
                    }

                    return new FastCFRNodeResult(expectedU, expectedCustom);
                }
                finally
                {
                    ArrayPool<double>.Shared.Return(qaRented, clearArray: false);
                }
            }
        }

        // ------------------------------------------------------------------
        // SIMD helpers (double)
        // ------------------------------------------------------------------
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private double DotProductDouble(ReadOnlySpan<double> a, ReadOnlySpan<double> b)
        {
            int n = NumActions;
            int vc = Vector<double>.Count;
            int i = 0;
            Vector<double> acc = Vector<double>.Zero;

            while (i <= n - vc)
            {
                var va = new Vector<double>(a.Slice(i, vc));
                var vb = new Vector<double>(b.Slice(i, vc));
                acc += va * vb;
                i += vc;
            }

            double sum = 0.0;
            for (int lane = 0; lane < vc; lane++) sum += acc[lane];
            for (; i < n; i++) sum += a[i] * b[i];
            return sum;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SimdUpdateDouble(ReadOnlySpan<double> Qa, double V, double inversePi, double piSelf)
        {
            var sumR = MemoryMarshal.Cast<TScalar, double>(_sumRegretTimesInversePi.AsSpan());
            var last = MemoryMarshal.Cast<TScalar, double>(_lastCumulativeStrategyInc.AsSpan());
            var p    = MemoryMarshal.Cast<TScalar, double>(_pSelf.AsSpan());

            int n = NumActions;
            int vc = Vector<double>.Count;
            int i = 0;

            var vV   = new Vector<double>(V);
            var vInv = new Vector<double>(inversePi);
            var vPiS = new Vector<double>(piSelf);

            while (i <= n - vc)
            {
                var vQa  = new Vector<double>(Qa.Slice(i, vc));
                var vP   = new Vector<double>(p.Slice(i, vc));

                var vRegAdd = (vQa - vV) * vInv;

                var vSumR = new Vector<double>(sumR.Slice(i, vc)) + vRegAdd;
                vSumR.CopyTo(sumR.Slice(i, vc));

                var vLast = new Vector<double>(last.Slice(i, vc)) + (vP * vPiS);
                vLast.CopyTo(last.Slice(i, vc));

                i += vc;
            }

            for (; i < n; i++)
            {
                double r = Qa[i] - V;
                sumR[i] += r * inversePi;
                last[i] += piSelf * p[i];
            }
        }

        // ------------------------------------------------------------------
        // SIMD helpers (float)
        // ------------------------------------------------------------------
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private double DotProductFloatChunked(ReadOnlySpan<double> QaD)
        {
            var pSelfF = MemoryMarshal.Cast<TScalar, float>(_pSelf.AsSpan());
            int n = NumActions;
            int vc = Vector<float>.Count;

            // For very small arities, scalar is cheaper than staging
            if (n < vc)
            {
                double s = 0.0;
                for (int i = 0; i < n; i++)
                    s += pSelfF[i] * (float)QaD[i];
                return s;
            }

            int iChunk = 0;
            Vector<float> acc = Vector<float>.Zero;

            while (iChunk <= n - vc)
            {
                for (int k = 0; k < vc; k++) _chunkF[k] = (float)QaD[iChunk + k];
                var va = new Vector<float>(pSelfF.Slice(iChunk, vc));
                var vb = new Vector<float>(_chunkF);
                acc += va * vb;
                iChunk += vc;
            }

            double sum = 0.0;
            for (int lane = 0; lane < vc; lane++) sum += acc[lane];
            for (; iChunk < n; iChunk++) sum += pSelfF[iChunk] * (float)QaD[iChunk];
            return sum;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SimdUpdateFloatChunked(ReadOnlySpan<double> QaD, float V, float inversePi, float piSelf)
        {
            var sumR = MemoryMarshal.Cast<TScalar, float>(_sumRegretTimesInversePi.AsSpan());
            var last = MemoryMarshal.Cast<TScalar, float>(_lastCumulativeStrategyInc.AsSpan());
            var p    = MemoryMarshal.Cast<TScalar, float>(_pSelf.AsSpan());

            int n = NumActions;
            int vc = Vector<float>.Count;

            if (n < vc)
            {
                for (int i = 0; i < n; i++)
                {
                    float r = (float)QaD[i] - V;
                    sumR[i] += r * inversePi;
                    last[i] += piSelf * p[i];
                }
                return;
            }

            int iChunk = 0;

            var vV   = new Vector<float>(V);
            var vInv = new Vector<float>(inversePi);
            var vPiS = new Vector<float>(piSelf);

            while (iChunk <= n - vc)
            {
                for (int k = 0; k < vc; k++) _chunkF[k] = (float)QaD[iChunk + k];

                var vQa  = new Vector<float>(_chunkF);
                var vP   = new Vector<float>(p.Slice(iChunk, vc));

                var vSumR = new Vector<float>(sumR.Slice(iChunk, vc)) + ((vQa - vV) * vInv);
                vSumR.CopyTo(sumR.Slice(iChunk, vc));

                var vLast = new Vector<float>(last.Slice(iChunk, vc)) + (vP * vPiS);
                vLast.CopyTo(last.Slice(iChunk, vc));

                iChunk += vc;
            }

            for (; iChunk < n; iChunk++)
            {
                float r = (float)QaD[iChunk] - V;
                sumR[iChunk] += r * inversePi;
                last[iChunk] += piSelf * p[iChunk];
            }
        }

        // ------------------------------------------------------------------
        // Scalar fallback
        // ------------------------------------------------------------------
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private double DotProductScalar(ReadOnlySpan<double> Qa)
        {
            double sum = 0.0;
            for (int a = 0; a < NumActions; a++)
                sum += ToDouble(_pSelf[a]) * Qa[a];
            return sum;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ScalarUpdateTallies(ReadOnlySpan<double> Qa, double V, double inversePi, double piSelf)
        {
            for (int a = 0; a < NumActions; a++)
            {
                double r = Qa[a] - V;
                _sumRegretTimesInversePi[a] = FromDouble(ToDouble(_sumRegretTimesInversePi[a]) + r * inversePi);
                _lastCumulativeStrategyInc[a] = FromDouble(ToDouble(_lastCumulativeStrategyInc[a]) + piSelf * ToDouble(_pSelf[a]));
            }
        }

        // ------------------------------------------------------------------
        // Exporters
        // ------------------------------------------------------------------
        public override ReadOnlySpan<double> SumRegretTimesInversePi
        {
            get
            {
                for (int a = 0; a < NumActions; a++)
                    _bufferRegretTimesInvPi[a] = ToDouble(_sumRegretTimesInversePi[a]);
                return _bufferRegretTimesInvPi;
            }
        }

        public override ReadOnlySpan<double> SumInversePi
        {
            get
            {
                // Expand scalar denominator to per-action view on demand
                for (int a = 0; a < NumActions; a++)
                    _bufferInvPi[a] = _sumInversePiShared;
                return _bufferInvPi;
            }
        }

        public override ReadOnlySpan<double> LastCumulativeStrategyIncrements
        {
            get
            {
                for (int a = 0; a < NumActions; a++)
                    _bufferLastCumStrat[a] = ToDouble(_lastCumulativeStrategyInc[a]);
                return _bufferLastCumStrat;
            }
        }

        // Conversion hooks specialized per TScalar closure
        protected abstract double ToDouble(TScalar v);
        protected abstract TScalar FromDouble(double v);
    }

    public sealed class FastCFRInformationSetDouble : FastCFRInformationSetGeneric<double>
    {
        public FastCFRInformationSetDouble(byte playerIndex, byte decisionIndex, byte numActions, FastCFRVisitProgram[] visits)
            : base(playerIndex, decisionIndex, numActions, visits) { }

        protected override double ToDouble(double v) => v;
        protected override double FromDouble(double v) => v;
    }

    public sealed class FastCFRInformationSetFloat : FastCFRInformationSetGeneric<float>
    {
        public FastCFRInformationSetFloat(byte playerIndex, byte decisionIndex, byte numActions, FastCFRVisitProgram[] visits)
            : base(playerIndex, decisionIndex, numActions, visits) { }

        protected override double ToDouble(float v) => v;
        protected override float FromDouble(double v) => (float)v;
    }

    // ----------------------------------------------------------------------
    // Chance node (lazy caches dynamic probabilities on first visit/iteration)
    // ----------------------------------------------------------------------
    public sealed class FastCFRChance : IFastCFRNode
    {
        public readonly byte DecisionIndex;
        public readonly byte NumOutcomes;

        private readonly FastCFRChanceVisitProgram[] _visits;
        private int _visitCounter;
        private IFastCFRNode[][] _childrenByVisit;

        private readonly int _numPlayers;
        private readonly double[] _zeroUtilities;
        private readonly double[] _workUtilities;

        // Probability caches per visit
        private bool[] _visitHasDynamic;
        private double[][] _staticProbs;     // filled once (purely static visits)
        private double[][] _cachedProbs;     // allocated once, filled lazily per iteration
        private bool[] _cachedFilled;        // mark when cachedProbs[v] is valid for this iteration

        public FastCFRChance(byte decisionIndex, byte numOutcomes, FastCFRChanceVisitProgram[] visits)
        {
            DecisionIndex = decisionIndex;
            NumOutcomes = numOutcomes;
            _visits = visits ?? Array.Empty<FastCFRChanceVisitProgram>();
            _numPlayers = _visits.Length == 0 ? 0 : _visits[0].NumPlayers;
            _zeroUtilities = _numPlayers == 0 ? Array.Empty<double>() : new double[_numPlayers];
            _workUtilities = _numPlayers == 0 ? Array.Empty<double>() : new double[_numPlayers];

            _visitHasDynamic = new bool[_visits.Length];
            _staticProbs = new double[_visits.Length][];
            _cachedProbs = new double[_visits.Length][];
            _cachedFilled = new bool[_visits.Length];
        }

        internal void BindChildrenAfterFinalize()
        {
            _childrenByVisit = new IFastCFRNode[_visits.Length][];
            for (int v = 0; v < _visits.Length; v++)
            {
                var steps = _visits[v].Steps;
                var arr = new IFastCFRNode[steps.Length];
                bool dynamic = false;
                for (int i = 0; i < steps.Length; i++)
                {
                    arr[i] = steps[i].ChildAccessor();
                    if (steps[i].ProbabilityProvider != null)
                        dynamic = true;
                }
                _childrenByVisit[v] = arr;
                _visitHasDynamic[v] = dynamic;

                if (!dynamic)
                {
                    var p = new double[steps.Length];
                    for (int i = 0; i < steps.Length; i++)
                        p[i] = steps[i].StaticProbability;
                    _staticProbs[v] = p;
                }
                else
                {
                    _cachedProbs[v] = new double[steps.Length]; // allocated once, filled lazily each iteration
                }
            }
        }

        public void InitializeIteration(ReadOnlySpan<double> _ownerPolicy, ReadOnlySpan<double> _opponentTraversal)
        {
            // For dynamic visits, mark caches invalid; compute lazily on first visit
            for (int v = 0; v < _visits.Length; v++)
                _cachedFilled[v] = false;

            _visitCounter = 0;
        }

        public FastCFRNodeResult Go(ref FastCFRIterationContext ctx)
        {
            var visitIndex = _visitCounter++;
            var visit = _visits[visitIndex];
            var children = _childrenByVisit[visitIndex];

            double[] probs;
            if (_visitHasDynamic[visitIndex])
            {
                if (!_cachedFilled[visitIndex])
                {
                    var dest = _cachedProbs[visitIndex];
                    var steps = visit.Steps;
                    for (int i = 0; i < steps.Length; i++)
                        dest[i] = steps[i].GetProbability(ref ctx);
                    _cachedFilled[visitIndex] = true;
                }
                probs = _cachedProbs[visitIndex];
            }
            else
            {
                probs = _staticProbs[visitIndex];
            }

            if (ctx.SuppressMath)
            {
                for (int k = 0; k < children.Length; k++)
                    children[k].Go(ref ctx);
                return new FastCFRNodeResult(_zeroUtilities, default);
            }

            var expectedU = _workUtilities;
            for (int i = 0; i < expectedU.Length; i++) expectedU[i] = 0.0;
            FloatSet expectedCustom = default;

            for (int k = 0; k < visit.Steps.Length; k++)
            {
                double p = probs[k];
                if (p == 0.0)
                {
                    bool prior = ctx.SuppressMath; ctx.SuppressMath = true;
                    children[k].Go(ref ctx);
                    ctx.SuppressMath = prior;
                    continue;
                }

                double oldOpp = ctx.ReachOpp;
                double oldChance = ctx.ReachChance;
                ctx.ReachOpp = oldOpp * p;
                ctx.ReachChance = oldChance * p;

                var child = children[k].Go(ref ctx);

                ctx.ReachOpp = oldOpp;
                ctx.ReachChance = oldChance;

                var cu = child.Utilities;
                for (int i = 0; i < expectedU.Length; i++)
                    expectedU[i] += p * cu[i];
                expectedCustom = expectedCustom.Plus(child.Custom.Times((float)p));
            }

            return new FastCFRNodeResult(expectedU, expectedCustom);
        }
    }

    // ----------------------------------------------------------------------
    // Final node
    // ----------------------------------------------------------------------
    public sealed class FastCFRFinal : IFastCFRNode
    {
        private readonly double[][] _utilitiesByScenario; // [scenario][player]
        private readonly FloatSet[] _customByScenario;
        private readonly int _numPlayers;
        public FastCFRFinal(double[][] utilitiesByScenario, FloatSet[] customByScenario)
        {
            _utilitiesByScenario = utilitiesByScenario ?? throw new ArgumentNullException(nameof(utilitiesByScenario));
            _customByScenario = customByScenario ?? throw new ArgumentNullException(nameof(customByScenario));
            if (_utilitiesByScenario.Length != _customByScenario.Length)
                throw new ArgumentException("Scenario counts mismatch.");
            _numPlayers = _utilitiesByScenario.Length == 0 ? 0 : _utilitiesByScenario[0].Length;
        }
        public void InitializeIteration(ReadOnlySpan<double> _ownerPolicy, ReadOnlySpan<double> _opponentTraversal) { }
        public FastCFRNodeResult Go(ref FastCFRIterationContext ctx)
        {
            if (ctx.SuppressMath)
                return FastCFRNodeResult.Zero(_numPlayers);
            int s = (uint)ctx.ScenarioIndex < (uint)_utilitiesByScenario.Length ? ctx.ScenarioIndex : 0;
            return new FastCFRNodeResult(_utilitiesByScenario[s], _customByScenario[s]);
        }
    }
}
