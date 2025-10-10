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
    public readonly struct FastCFRNodeResult
    {
        public readonly double[] Utilities;
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
        public byte OptimizedPlayerIndex;
        public double ReachSelf;
        public double ReachOpp;
        public double ReachChance;
        public double SamplingCorrection;
        public int ScenarioIndex;
        public bool SuppressMath;
        public Func<byte, double> Rand01ForDecision;
    }

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

    public enum FastCFRVisitStepKind : byte { ChildForAction }

    public readonly struct FastCFRVisitStep
    {
        public readonly FastCFRVisitStepKind Kind;
        public readonly byte ActionIndex;
        public readonly Func<IFastCFRNode> ChildAccessor;
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

    public readonly struct FastCFRChanceStep
    {
        public readonly Func<IFastCFRNode> ChildAccessor;
        public readonly double StaticProbability;
        public readonly FastCFRProbProvider ProbabilityProvider;
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

    public abstract class FastCFRInformationSetGeneric<TScalar> : FastCFRInformationSet
        where TScalar : struct
    {
        public readonly byte PlayerIndex;
        public readonly byte DecisionIndex;
        public readonly byte NumActions;

        private readonly FastCFRVisitProgram[] _visits;
        private int _visitCounter;

        protected readonly TScalar[] _pSelf;
        protected readonly TScalar[] _pOpp;

        protected readonly TScalar[] _sumRegretTimesInversePi;
        protected readonly TScalar[] _sumInversePi;
        protected readonly TScalar[] _lastCumulativeStrategyInc;

        private IFastCFRNode[][] _childrenByVisit;

        private readonly int _numPlayers;
        private readonly double[] _zeroUtilities;
        private readonly double[] _workUtilities;

        private readonly double[] _bufferRegretTimesInvPi;
        private readonly double[] _bufferInvPi;
        private readonly double[] _bufferLastCumStrat;

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
                _pOpp[a] = FromDouble(opponentTraversalPolicy[a]);
                _sumRegretTimesInversePi[a] = FromDouble(0.0);
                _sumInversePi[a] = FromDouble(0.0);
                _lastCumulativeStrategyInc[a] = FromDouble(0.0);
            }
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

            double[] qaArray = null;
            Span<double> Qa = Span<double>.Empty;
            if (ownerIsOptimized)
            {
                qaArray = ArrayPool<double>.Shared.Rent(NumActions);
                Qa = qaArray.AsSpan(0, NumActions);
                Qa.Clear();
            }

            try
            {
                for (int i = 0; i < children.Length; i++)
                {
                    ref readonly var step = ref visit.Steps[i];
                    int ai = step.ActionIndex;

                    double w = ownerIsOptimized ? ToDouble(_pSelf[ai]) : ToDouble(_pOpp[ai]);

                    if (!ownerIsOptimized && w <= double.Epsilon)
                    {
                        bool prior = ctx.SuppressMath; ctx.SuppressMath = true;
                        children[i].Go(ref ctx);
                        ctx.SuppressMath = prior;
                        continue;
                    }

                    FastCFRNodeResult child;
                    if (ownerIsOptimized)
                    {
                        double oldSelf = ctx.ReachSelf;
                        ctx.ReachSelf = oldSelf * w;
                        child = children[i].Go(ref ctx);
                        ctx.ReachSelf = oldSelf;
                        Qa[ai] = child.Utilities[PlayerIndex];
                    }
                    else
                    {
                        double oldOpp = ctx.ReachOpp;
                        ctx.ReachOpp = oldOpp * w;
                        child = children[i].Go(ref ctx);
                        ctx.ReachOpp = oldOpp;
                    }

                    if (ownerIsOptimized || w > double.Epsilon)
                    {
                        var cu = child.Utilities;
                        for (int pl = 0; pl < expectedU.Length; pl++)
                            expectedU[pl] += w * cu[pl];
                        expectedCustom = expectedCustom.Plus(child.Custom.Times((float)w));
                    }
                }

                if (ownerIsOptimized)
                {
                    double inversePi = ctx.ReachOpp;
                    double piSelf = ctx.ReachSelf;

                    double V;
                    if (Vector.IsHardwareAccelerated)
                    {
                        if (typeof(TScalar) == typeof(double))
                        {
                            var pSelfD = MemoryMarshal.Cast<TScalar, double>(_pSelf.AsSpan());
                            V = DotProductDouble(pSelfD, Qa);
                            SimdUpdateDouble(Qa, V, inversePi, piSelf);
                        }
                        else if (typeof(TScalar) == typeof(float))
                        {
                            Span<float> QaF = stackalloc float[Vector<float>.Count];
                            // We will reuse QaF chunk-by-chunk to avoid large stack usage.
                            V = DotProductFloatChunked(Qa, QaF);
                            SimdUpdateFloatChunked(Qa, QaF, (float)V, (float)inversePi, (float)piSelf);
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
                }
            }
            finally
            {
                if (qaArray != null)
                    ArrayPool<double>.Shared.Return(qaArray, clearArray: true);
            }

            return new FastCFRNodeResult(expectedU, expectedCustom);
        }

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
            var sumI = MemoryMarshal.Cast<TScalar, double>(_sumInversePi.AsSpan());
            var last = MemoryMarshal.Cast<TScalar, double>(_lastCumulativeStrategyInc.AsSpan());
            var p = MemoryMarshal.Cast<TScalar, double>(_pSelf.AsSpan());

            int n = NumActions;
            int vc = Vector<double>.Count;
            int i = 0;

            var vV = new Vector<double>(V);
            var vInv = new Vector<double>(inversePi);
            var vPiS = new Vector<double>(piSelf);

            while (i <= n - vc)
            {
                var vQa = new Vector<double>(Qa.Slice(i, vc));
                var vP = new Vector<double>(p.Slice(i, vc));

                var vRegAdd = (vQa - vV) * vInv;

                (new Vector<double>(sumR.Slice(i, vc)) + vRegAdd).CopyTo(sumR.Slice(i, vc));
                (new Vector<double>(sumI.Slice(i, vc)) + vInv).CopyTo(sumI.Slice(i, vc));
                (new Vector<double>(last.Slice(i, vc)) + (vP * vPiS)).CopyTo(last.Slice(i, vc));

                i += vc;
            }

            for (; i < n; i++)
            {
                double r = Qa[i] - V;
                sumR[i] += r * inversePi;
                sumI[i] += inversePi;
                last[i] += piSelf * p[i];
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private double DotProductFloatChunked(ReadOnlySpan<double> QaD, Span<float> chunkBuffer)
        {
            var pSelfF = MemoryMarshal.Cast<TScalar, float>(_pSelf.AsSpan());
            int n = NumActions;
            int vc = Vector<float>.Count;
            int i = 0;
            Vector<float> acc = Vector<float>.Zero;

            while (i <= n - vc)
            {
                for (int k = 0; k < vc; k++) chunkBuffer[k] = (float)QaD[i + k];
                var va = new Vector<float>(pSelfF.Slice(i, vc));
                var vb = new Vector<float>(chunkBuffer);
                acc += va * vb;
                i += vc;
            }

            double sum = 0.0;
            for (int lane = 0; lane < vc; lane++) sum += acc[lane];
            for (; i < n; i++) sum += pSelfF[i] * (float)QaD[i];
            return sum;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SimdUpdateFloatChunked(ReadOnlySpan<double> QaD, Span<float> chunkBuffer, float V, float inversePi, float piSelf)
        {
            var sumR = MemoryMarshal.Cast<TScalar, float>(_sumRegretTimesInversePi.AsSpan());
            var sumI = MemoryMarshal.Cast<TScalar, float>(_sumInversePi.AsSpan());
            var last = MemoryMarshal.Cast<TScalar, float>(_lastCumulativeStrategyInc.AsSpan());
            var p = MemoryMarshal.Cast<TScalar, float>(_pSelf.AsSpan());

            int n = NumActions;
            int vc = Vector<float>.Count;
            int i = 0;

            var vV = new Vector<float>(V);
            var vInv = new Vector<float>(inversePi);
            var vPiS = new Vector<float>(piSelf);

            while (i <= n - vc)
            {
                for (int k = 0; k < vc; k++) chunkBuffer[k] = (float)QaD[i + k];

                var vQa = new Vector<float>(chunkBuffer);
                var vP = new Vector<float>(p.Slice(i, vc));

                (new Vector<float>(sumR.Slice(i, vc)) + ((vQa - vV) * vInv)).CopyTo(sumR.Slice(i, vc));
                (new Vector<float>(sumI.Slice(i, vc)) + vInv).CopyTo(sumI.Slice(i, vc));
                (new Vector<float>(last.Slice(i, vc)) + (vP * vPiS)).CopyTo(last.Slice(i, vc));

                i += vc;
            }

            for (; i < n; i++)
            {
                float r = (float)QaD[i] - V;
                sumR[i] += r * inversePi;
                sumI[i] += inversePi;
                last[i] += piSelf * p[i];
            }
        }

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
                _sumInversePi[a] = FromDouble(ToDouble(_sumInversePi[a]) + inversePi);
                _lastCumulativeStrategyInc[a] = FromDouble(ToDouble(_lastCumulativeStrategyInc[a]) + piSelf * ToDouble(_pSelf[a]));
            }
        }

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
                for (int a = 0; a < NumActions; a++)
                    _bufferInvPi[a] = ToDouble(_sumInversePi[a]);
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

        public FastCFRChance(byte decisionIndex, byte numOutcomes, FastCFRChanceVisitProgram[] visits)
        {
            DecisionIndex = decisionIndex;
            NumOutcomes = numOutcomes;
            _visits = visits ?? Array.Empty<FastCFRChanceVisitProgram>();
            _numPlayers = _visits.Length == 0 ? 0 : _visits[0].NumPlayers;
            _zeroUtilities = _numPlayers == 0 ? Array.Empty<double>() : new double[_numPlayers];
            _workUtilities = _numPlayers == 0 ? Array.Empty<double>() : new double[_numPlayers];
        }

        internal void BindChildrenAfterFinalize()
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

        public void InitializeIteration(ReadOnlySpan<double> _ownerPolicy, ReadOnlySpan<double> _opponentTraversal)
        {
            _visitCounter = 0;
        }

        public FastCFRNodeResult Go(ref FastCFRIterationContext ctx)
        {
            var visitIndex = _visitCounter++;
            var visit = _visits[visitIndex];
            var children = _childrenByVisit[visitIndex];
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
                ref readonly var step = ref visit.Steps[k];
                double p = step.GetProbability(ref ctx);
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
                for (int i = 0; i < expectedU.Length; i++) expectedU[i] += p * cu[i];
                expectedCustom = expectedCustom.Plus(child.Custom.Times((float)p));
            }
            return new FastCFRNodeResult(expectedU, expectedCustom);
        }
    }

    public sealed class FastCFRFinal : IFastCFRNode
    {
        private readonly double[][] _utilitiesByScenario;
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
