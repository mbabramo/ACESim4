using System;
using ACESimBase.GameSolvingSupport.GameTree; // InformationSetNode.SmallestProbabilityRepresented
using ACESimBase.Util.Collections; // FloatSet

namespace ACESimBase.GameSolvingSupport.FastCFR
{
    // ─────────────────────────────────────────────────────────────────────────────
    // Shared contracts (all FastCFR-prefixed)
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>Vector + custom payload returned by every node.</summary>
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

    /// <summary>Per-iteration traversal state. Driver and nodes update these in place.</summary>
    public ref struct FastCFRIterationContext
    {
        public int IterationNumber;

        // Which player's regrets are being updated this sweep (alternating CFR).
        public byte OptimizedPlayerIndex;

        // Reach probabilities at the current node (split the optimized player from "others").
        public double ReachSelf;   // π_i for OptimizedPlayerIndex
        public double ReachOpp;    // product of other non-chance players' reach
        public double ReachChance; // π_c (not used in regret weighting once chance is folded into ReachOpp)

        // 1.0 for full CFR; sampling variants can set importance correction here.
        public double SamplingCorrection;

        // For terminals with alternate scenarios (warmup/post-warmup, etc.).
        public int ScenarioIndex;

        // PRUNING PARITY: when true, all math is suppressed but visit counters still advance.
        public bool SuppressMath;

        // Optional deterministic RNG (key by decision index if needed).
        public Func<byte, double> Rand01ForDecision;
    }

    // Delegates that support ref parameters (use these instead of Func<ref,...>).
    public delegate FastCFRNodeResult FastCFRCall(ref FastCFRIterationContext ctx);
    public delegate double FastCFRProbProvider(ref FastCFRIterationContext ctx);

    public interface IFastCFRNode
    {
        /// <summary>
        /// Freeze per-iteration policies for this node and clear per-iteration tallies.
        /// For chance/final, spans are ignored; they still reset internal counters.
        /// </summary>
        void InitializeIteration(ReadOnlySpan<double> ownerCurrentPolicy,
                                 ReadOnlySpan<double> opponentTraversalPolicy);

        /// <summary>Execute exactly one recorded visit of this node.</summary>
        FastCFRNodeResult Go(ref FastCFRIterationContext ctx);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Visit programs (pre-recorded itinerary data)
    // ─────────────────────────────────────────────────────────────────────────────

    public enum FastCFRVisitStepKind : byte { ChildForAction }

    public readonly struct FastCFRVisitStep
    {
        public readonly FastCFRVisitStepKind Kind;
        public readonly byte ActionIndex; // 0..NumActions-1 (infoset-local)
        public readonly FastCFRCall Call;
        public FastCFRVisitStep(FastCFRVisitStepKind kind, byte actionIndex, FastCFRCall call)
        {
            Kind = kind;
            ActionIndex = actionIndex;
            Call = call;
        }
    }

    public readonly struct FastCFRVisitProgram
    {
        public readonly FastCFRVisitStep[] Steps; // full CFR: one per action; sampling: one step
        public readonly int NumPlayers;
        public FastCFRVisitProgram(FastCFRVisitStep[] steps, int numPlayers)
        {
            Steps = steps;
            NumPlayers = numPlayers;
        }
    }

    public readonly struct FastCFRChanceStep
    {
        public readonly FastCFRCall Call;

        // Either use a static p (>=0) or provide a dynamic provider (StaticProbability < 0).
        public readonly double StaticProbability;
        public readonly FastCFRProbProvider ProbabilityProvider;

        public FastCFRChanceStep(FastCFRCall call, double staticProbability)
        {
            Call = call;
            StaticProbability = staticProbability;
            ProbabilityProvider = null;
        }
        public FastCFRChanceStep(FastCFRCall call, FastCFRProbProvider provider)
        {
            Call = call;
            StaticProbability = -1.0;
            ProbabilityProvider = provider;
        }
        public double GetProbability(ref FastCFRIterationContext ctx)
            => ProbabilityProvider == null ? StaticProbability : ProbabilityProvider(ref ctx);
    }

    public readonly struct FastCFRChanceVisitProgram
    {
        public readonly FastCFRChanceStep[] Steps; // sampling: one step
        public readonly int NumPlayers;
        public FastCFRChanceVisitProgram(FastCFRChanceStep[] steps, int numPlayers)
        {
            Steps = steps;
            NumPlayers = numPlayers;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // FastCFRInformationSet (pruning parity, owner/opponent policy freeze)
    // ─────────────────────────────────────────────────────────────────────────────

    public sealed class FastCFRInformationSet : IFastCFRNode
    {
        public readonly byte PlayerIndex;   // owner of this infoset
        public readonly byte DecisionIndex; // stable id
        public readonly byte NumActions;

        private readonly FastCFRVisitProgram[] _visits;
        private int _visitCounter;

        // Frozen per-iteration policies:
        // - _pSelf: owner’s current strategy (used when PlayerIndex == OptimizedPlayerIndex)
        // - _pOpp : opponent traversal strategy (used when PlayerIndex != OptimizedPlayerIndex)
        private readonly double[] _pSelf;
        private readonly double[] _pOpp;

        // Per-iteration tallies consumed by the existing post-iteration updater.
        private readonly double[] _sumRegretTimesInversePi;
        private readonly double[] _sumInversePi;
        private readonly double[] _lastCumulativeStrategyInc;

        public FastCFRInformationSet(byte playerIndex, byte decisionIndex, byte numActions, FastCFRVisitProgram[] visits)
        {
            PlayerIndex = playerIndex;
            DecisionIndex = decisionIndex;
            NumActions = numActions;
            _visits = visits ?? Array.Empty<FastCFRVisitProgram>();

            _pSelf = new double[numActions];
            _pOpp  = new double[numActions];

            _sumRegretTimesInversePi   = new double[numActions];
            _sumInversePi              = new double[numActions];
            _lastCumulativeStrategyInc = new double[numActions];
        }

        public void InitializeIteration(ReadOnlySpan<double> ownerCurrentPolicy,
                                        ReadOnlySpan<double> opponentTraversalPolicy)
        {
            if (ownerCurrentPolicy.Length != NumActions || opponentTraversalPolicy.Length != NumActions)
                throw new ArgumentException("Policy length mismatch for infoset initialization.");

            for (int a = 0; a < NumActions; a++)
            {
                _pSelf[a] = ownerCurrentPolicy[a];
                _pOpp[a]  = opponentTraversalPolicy[a];
                _sumRegretTimesInversePi[a]   = 0;
                _sumInversePi[a]              = 0;
                _lastCumulativeStrategyInc[a] = 0;
            }
            _visitCounter = 0;
        }

        public FastCFRNodeResult Go(ref FastCFRIterationContext ctx)
        {
            var visit = _visits[_visitCounter++];
            bool ownerIsOptimized = PlayerIndex == ctx.OptimizedPlayerIndex;

            // PRUNING PARITY: suppressed subtrees still advance counters but contribute zero.
            if (ctx.SuppressMath)
            {
                bool wasSuppressed = ctx.SuppressMath;
                for (int i = 0; i < visit.Steps.Length; i++)
                    visit.Steps[i].Call(ref ctx);
                ctx.SuppressMath = wasSuppressed;
                return FastCFRNodeResult.Zero(visit.NumPlayers);
            }

            // Choose the frozen policy used for this sweep at this node.
            var p = ownerIsOptimized ? _pSelf : _pOpp;

            // Expected value returned to parent:
            double[] expectedU = new double[visit.NumPlayers];
            FloatSet expectedCustom = default;

            // Only needed when this node’s owner is the optimized player:
            Span<double> Qa = ownerIsOptimized ? stackalloc double[NumActions] : Span<double>.Empty;

            for (int i = 0; i < visit.Steps.Length; i++)
            {
                ref readonly var step = ref visit.Steps[i];
                int ai = step.ActionIndex; // 0..NumActions-1
                double w = p[ai];

                // Opponent pruning: if p[a]==0 at opponent node, walk suppressed to keep counters aligned.
                if (!ownerIsOptimized && w == 0.0)
                {
                    bool prior = ctx.SuppressMath;
                    ctx.SuppressMath = true;
                    step.Call(ref ctx);
                    ctx.SuppressMath = prior;
                    continue;
                }

                FastCFRNodeResult child;

                if (ownerIsOptimized)
                {
                    // Self decision: scale self reach only (matches baseline GetNextPiValues changeOtherPlayers:false).
                    double oldSelf = ctx.ReachSelf;
                    ctx.ReachSelf = oldSelf * w;
                    child = step.Call(ref ctx);
                    ctx.ReachSelf = oldSelf;

                    Qa[ai] = child.Utilities[PlayerIndex];
                }
                else
                {
                    // Opponent decision: do not scale reach here; builder-recorded call applies the
                    // changeOtherPlayers:true update for this branch (keeps responsibilities separated).
                    child = step.Call(ref ctx);
                }

                // Expectation under the frozen traversal policy p.
                if (w != 0.0)
                {
                    for (int pl = 0; pl < expectedU.Length; pl++)
                        expectedU[pl] += w * child.Utilities[pl];
                    expectedCustom = expectedCustom.Plus(child.Custom.Times((float)w));
                }
            }

            // Owner-only regret & average-strategy increments (baseline formulas).
            if (ownerIsOptimized)
            {
                double V = 0.0;
                for (int a = 0; a < NumActions; a++)
                    V += _pSelf[a] * Qa[a];

                // Baseline: inversePi = product of other non-chance players' reach (chance already
                // folded into "others" via chance nodes updating ReachOpp).
                double inversePi = ctx.ReachOpp;

                // Baseline clamp for average-strategy increments.
                double piSelf = ctx.ReachSelf;
                double piAdj = piSelf < InformationSetNode.SmallestProbabilityRepresented
                    ? InformationSetNode.SmallestProbabilityRepresented
                    : piSelf;

                for (int a = 0; a < NumActions; a++)
                {
                    double regret = Qa[a] - V;
                    _sumRegretTimesInversePi[a] += regret * inversePi;
                    _sumInversePi[a]            += inversePi;
                    _lastCumulativeStrategyInc[a] += piAdj * _pSelf[a];
                }
            }

            return new FastCFRNodeResult(expectedU, expectedCustom);
        }

        // Expose tallies to flush into the backing InformationSetNode before PostIterationUpdates(...)
        public ReadOnlySpan<double> SumRegretTimesInversePi => _sumRegretTimesInversePi;
        public ReadOnlySpan<double> SumInversePi            => _sumInversePi;
        public ReadOnlySpan<double> LastCumulativeStrategyIncrements => _lastCumulativeStrategyInc;
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // FastCFRChance (expectation or single sampled outcome; pruning parity)
    // ─────────────────────────────────────────────────────────────────────────────

    public sealed class FastCFRChance : IFastCFRNode
    {
        public readonly byte DecisionIndex;
        public readonly byte NumOutcomes;

        private readonly FastCFRChanceVisitProgram[] _visits;
        private int _visitCounter;

        public FastCFRChance(byte decisionIndex, byte numOutcomes, FastCFRChanceVisitProgram[] visits)
        {
            DecisionIndex = decisionIndex;
            NumOutcomes = numOutcomes;
            _visits = visits ?? Array.Empty<FastCFRChanceVisitProgram>();
        }

        public void InitializeIteration(ReadOnlySpan<double> _ownerPolicy,
                                        ReadOnlySpan<double> _opponentTraversal)
        {
            _visitCounter = 0;
        }

        public FastCFRNodeResult Go(ref FastCFRIterationContext ctx)
        {
            var visit = _visits[_visitCounter++];

            if (ctx.SuppressMath)
            {
                // Walk children to advance counters; discard results.
                for (int k = 0; k < visit.Steps.Length; k++)
                    visit.Steps[k].Call(ref ctx);
                return FastCFRNodeResult.Zero(visit.NumPlayers);
            }

            double[] expectedU = new double[visit.NumPlayers];
            FloatSet expectedCustom = default;

            for (int k = 0; k < visit.Steps.Length; k++)
            {
                ref readonly var step = ref visit.Steps[k];
                double p = step.GetProbability(ref ctx); // equal/uneven/path-dependent.
                if (p == 0.0)
                {
                    // Keep counters aligned but no math from this outcome.
                    bool prior = ctx.SuppressMath; ctx.SuppressMath = true;
                    step.Call(ref ctx);
                    ctx.SuppressMath = prior;
                    continue;
                }

                // Chance scales "others" reach (matches baseline GetNextPiValues changeOtherPlayers:true).
                double oldOpp = ctx.ReachOpp;
                ctx.ReachOpp = oldOpp * p;

                var child = step.Call(ref ctx);

                ctx.ReachOpp = oldOpp;

                for (int i = 0; i < expectedU.Length; i++)
                    expectedU[i] += p * child.Utilities[i];
                expectedCustom = expectedCustom.Plus(child.Custom.Times((float)p));
            }

            return new FastCFRNodeResult(expectedU, expectedCustom);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // FastCFRFinal (returns terminal utilities + custom; pruning parity)
    // ─────────────────────────────────────────────────────────────────────────────

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

        public void InitializeIteration(ReadOnlySpan<double> _ownerPolicy,
                                        ReadOnlySpan<double> _opponentTraversal)
        {
            // nothing
        }

        public FastCFRNodeResult Go(ref FastCFRIterationContext ctx)
        {
            if (ctx.SuppressMath)
                return FastCFRNodeResult.Zero(_numPlayers);

            int s = (uint)ctx.ScenarioIndex < (uint)_utilitiesByScenario.Length ? ctx.ScenarioIndex : 0;
            return new FastCFRNodeResult(_utilitiesByScenario[s], _customByScenario[s]);
        }
    }
}
