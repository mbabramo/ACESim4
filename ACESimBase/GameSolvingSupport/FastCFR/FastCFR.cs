// ACESimBase/GameSolvingSupport/FastCFR/FastCFR.cs
using System;
using ACESimBase.GameSolvingSupport.GameTree; // InformationSetNode.SmallestProbabilityRepresented
using ACESimBase.Util.Collections; // FloatSet

namespace ACESimBase.GameSolvingSupport.FastCFR
{
    // Shared result wrapper --------------------------------------------------
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

    // Iteration context ------------------------------------------------------
    public ref struct FastCFRIterationContext
    {
        public int IterationNumber;
        public byte OptimizedPlayerIndex; // whose regrets we update this iteration
        public double ReachSelf;   // π_i for optimized player
        public double ReachOpp;    // product of other (non-chance) players' reach
        public double ReachChance; // π_c (maintained for completeness / diagnostics)
        public double SamplingCorrection; // importance weight (1.0 in full CFR)
        public int ScenarioIndex;  // scenario for finals
        public bool SuppressMath;  // when true: walk structure but ignore math
        public Func<byte, double> Rand01ForDecision; // optional deterministic RNG
    }

    // Delegates kept for compatibility (also used by the global program)
    public delegate FastCFRNodeResult FastCFRCall(ref FastCFRIterationContext ctx);
    public delegate double FastCFRProbProvider(ref FastCFRIterationContext ctx);

    public interface IFastCFRNode
    {
        void InitializeIteration(ReadOnlySpan<double> ownerCurrentPolicy,
                                 ReadOnlySpan<double> opponentTraversalPolicy);
        FastCFRNodeResult Go(ref FastCFRIterationContext ctx);
    }

    // Visit program data (scalar path retained for compatibility) -----------
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

    // Chance (scalar compatibility path) ------------------------------------
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

    // Information set node (scalar compatibility path + tally storage) ------
    public sealed class FastCFRInformationSet : IFastCFRNode
    {
        public readonly byte PlayerIndex;
        public readonly byte DecisionIndex;
        public readonly byte NumActions;

        private readonly FastCFRVisitProgram[] _visits;
        private int _visitCounter;

        // Frozen policies per iteration
        internal readonly double[] _pSelf; // owner policy frozen for this sweep
        internal readonly double[] _pOpp;  // opponent traversal policy for this sweep

        // Tallies collected during traversal
        internal readonly double[] _sumRegretTimesInversePi;
        internal readonly double[] _sumInversePi;
        internal readonly double[] _lastCumulativeStrategyInc;

        // Bound child arrays per visit (after finalize)
        private IFastCFRNode[][] _childrenByVisit;

        // Small reusable buffers
        private readonly int _numPlayers;
        private readonly double[] _zeroUtilities;
        private readonly double[] _workUtilities;

        public FastCFRInformationSet(byte playerIndex, byte decisionIndex, byte numActions, FastCFRVisitProgram[] visits)
        {
            PlayerIndex = playerIndex;
            DecisionIndex = decisionIndex;
            NumActions = numActions;
            _visits = visits ?? Array.Empty<FastCFRVisitProgram>();
            _pSelf = new double[numActions];
            _pOpp = new double[numActions];
            _sumRegretTimesInversePi = new double[numActions];
            _sumInversePi = new double[numActions];
            _lastCumulativeStrategyInc = new double[numActions];
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

        public void InitializeIteration(ReadOnlySpan<double> ownerCurrentPolicy, ReadOnlySpan<double> opponentTraversalPolicy)
        {
            if (ownerCurrentPolicy.Length != NumActions || opponentTraversalPolicy.Length != NumActions)
                throw new ArgumentException("Policy length mismatch for infoset initialization.");
            for (int a = 0; a < NumActions; a++)
            {
                _pSelf[a] = ownerCurrentPolicy[a];
                _pOpp[a] = opponentTraversalPolicy[a];
                _sumRegretTimesInversePi[a] = 0;
                _sumInversePi[a] = 0;
                _lastCumulativeStrategyInc[a] = 0;
            }
            _visitCounter = 0;
        }

        // Scalar compatibility path (not used by the global runner, but kept for tests/tools)
        public FastCFRNodeResult Go(ref FastCFRIterationContext ctx)
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

            var p = ownerIsOptimized ? _pSelf : _pOpp;
            var expectedU = _workUtilities;
            for (int i = 0; i < expectedU.Length; i++) expectedU[i] = 0.0;
            FloatSet expectedCustom = default;
            Span<double> Qa = ownerIsOptimized ? stackalloc double[NumActions] : Span<double>.Empty;

            for (int i = 0; i < children.Length; i++)
            {
                ref readonly var step = ref visit.Steps[i];
                int ai = step.ActionIndex;
                double w = p[ai];

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
                double V = 0.0;
                for (int a = 0; a < NumActions; a++) V += _pSelf[a] * Qa[a];
                double inversePi = ctx.ReachOpp;
                double piSelf = ctx.ReachSelf;
                for (int a = 0; a < NumActions; a++)
                {
                    double regret = Qa[a] - V;
                    _sumRegretTimesInversePi[a] += regret * inversePi;
                    _sumInversePi[a] += inversePi;
                    _lastCumulativeStrategyInc[a] += piSelf * _pSelf[a];
                }
            }
            return new FastCFRNodeResult(expectedU, expectedCustom);
        }

        public ReadOnlySpan<double> SumRegretTimesInversePi => _sumRegretTimesInversePi;
        public ReadOnlySpan<double> SumInversePi => _sumInversePi;
        public ReadOnlySpan<double> LastCumulativeStrategyIncrements => _lastCumulativeStrategyInc;
    }

    // Chance node (scalar compatibility path) --------------------------------
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

    // Final node -------------------------------------------------------------
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
