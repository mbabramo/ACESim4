// ACESimBase/GameSolvingSupport/GpuCFR/GpuCFRBuilder.cs
using System;
using ACESimBase.GameSolvingSupport.GameTree;
using ACESimBase.GameSolvingSupport.Settings;

namespace ACESimBase.GameSolvingSupport.GpuCFR
{
    /// <summary>
    /// Standalone CFR builder that mirrors vanilla CFR semantics on CPU.
    /// It traverses the game tree, computes regrets and average-strategy
    /// increments directly on backing InformationSetNode objects, and
    /// returns utilities for the current sweep. A later GPU path can
    /// replace the inner recursion without changing public surface.
    /// </summary>
    public sealed class GpuCFRBuilder
    {
        public sealed class RootNode
        {
            private readonly GpuCFRBuilder _owner;

            internal RootNode(GpuCFRBuilder owner) => _owner = owner;

            public NodeResult Go(ref IterationContext ctx)
            {
                // Initialize reach vectors for current-policy and average-strategy streams.
                var pi = new double[_owner.NumNonChancePlayers];
                var avgPi = new double[_owner.NumNonChancePlayers];
                for (byte p = 0; p < _owner.NumNonChancePlayers; p++)
                {
                    pi[p] = 1.0;
                    avgPi[p] = 1.0;
                }

                var hp = _owner._rootFactory();
                var u = _owner.Visit(in hp, ref ctx, pi, avgPi);
                return new NodeResult(u);
            }
        }

        public readonly struct NodeResult
        {
            public NodeResult(double[] utilities) => Utilities = utilities;
            public double[] Utilities { get; }
        }

        public struct IterationContext
        {
            public int IterationNumber;
            public byte OptimizedPlayerIndex;
            public int ScenarioIndex;
            public double ReachSelf;
            public double ReachOpp;
            public double ReachChance;
            public double SamplingCorrection;
            public bool SuppressMath;
        }

        public sealed class GpuCFRBuilderOptions
        {
            public bool UseDynamicChanceProbabilities { get; set; } = true;
            public byte NumNonChancePlayers { get; set; } = 2;
        }

        private readonly HistoryNavigationInfo _navigation;
        private readonly Func<HistoryPoint> _rootFactory;
        private readonly bool _useFloat; // reserved for future GPU precision choice
        private readonly GpuCFRBuilderOptions _options;

        public RootNode Root { get; }
        public bool IsAvailable { get; }
        public byte NumNonChancePlayers { get; }

        public GpuCFRBuilder(
            HistoryNavigationInfo navigation,
            Func<HistoryPoint> rootFactory,
            bool useFloat,
            GpuCFRBuilderOptions options)
        {
            _navigation = navigation;
            _rootFactory = rootFactory ?? throw new ArgumentNullException(nameof(rootFactory));
            _useFloat = useFloat;
            _options = options ?? new GpuCFRBuilderOptions();
            NumNonChancePlayers = _options.NumNonChancePlayers;

            Root = new RootNode(this);

            // CPU path is always available; a later GPU path can set this via device probe.
            IsAvailable = true;
        }

        public IterationContext InitializeIteration(byte optimizedPlayerIndex, int scenarioIndex, Func<int, double> rand01ForDecision)
        {
            return new IterationContext
            {
                IterationNumber = 0,
                OptimizedPlayerIndex = optimizedPlayerIndex,
                ScenarioIndex = scenarioIndex,
                ReachSelf = 1.0,
                ReachOpp = 1.0,
                ReachChance = 1.0,
                SamplingCorrection = 1.0,
                SuppressMath = false
            };
        }

        public void CopyTalliesIntoBackingNodes()
        {
            // CPU path writes directly to backing nodes (InformationSetNode.Increment*).
            // When a real GPU path is added, this will map or copy device tallies into backing nodes.
        }

        #region Recursive traversal (CPU reference)

        private double[] Visit(in HistoryPoint hp, ref IterationContext ctx, double[] pi, double[] avgPi)
        {
            var state = hp.GetGameStatePrerecorded(_navigation) ?? _navigation.GetGameState(in hp);

            if (state is FinalUtilitiesNode fu)
            {
                // Return the utilities at terminal.
                // (Scenario selection is already applied in FinalUtilitiesNode initialization.)
                var u = new double[NumNonChancePlayers];
                for (int i = 0; i < NumNonChancePlayers; i++)
                    u[i] = fu.Utilities[i];
                return u;
            }
            else if (state is ChanceNode cn)
            {
                return VisitChance(in hp, cn, ref ctx, pi, avgPi);
            }
            else // InformationSetNode
            {
                return VisitDecision(in hp, (InformationSetNode)state, ref ctx, pi, avgPi);
            }
        }

        private double[] VisitChance(in HistoryPoint hp, ChanceNode cn, ref IterationContext ctx, double[] pi, double[] avgPi)
        {
            var decision = _navigation.GameDefinition.DecisionsExecutionOrder[cn.DecisionIndex];
            byte numOutcomes = decision.NumPossibleActions;

            var expected = new double[NumNonChancePlayers];

            for (byte a = 1; a <= numOutcomes; a++)
            {
                double p = cn.GetActionProbability(a);
                if (p == 0.0)
                    continue;

                var nextPi = new double[NumNonChancePlayers];
                var nextAvgPi = new double[NumNonChancePlayers];
                GetNextPiValues(pi, ctx.OptimizedPlayerIndex, p, changeOtherPlayers: true, dest: nextPi);
                GetNextPiValues(avgPi, ctx.OptimizedPlayerIndex, p, changeOtherPlayers: true, dest: nextAvgPi);

                HistoryPoint nextHp;
                if (_navigation.LookupApproach != InformationSetLookupApproach.PlayGameDirectly && cn.Decision.IsReversible)
                    nextHp = hp.SwitchToBranch(_navigation, a, cn.Decision, cn.DecisionIndex);
                else
                    nextHp = hp.GetBranch(_navigation, a, cn.Decision, cn.DecisionIndex);

                var childU = Visit(in nextHp, ref ctx, nextPi, nextAvgPi);

                for (int i = 0; i < NumNonChancePlayers; i++)
                    expected[i] += p * childU[i];

                if (_navigation.LookupApproach != InformationSetLookupApproach.PlayGameDirectly && cn.Decision.IsReversible)
                    _navigation.GameDefinition.ReverseSwitchToBranchEffects(cn.Decision, in nextHp);
            }

            return expected;
        }

        private double[] VisitDecision(in HistoryPoint hp, InformationSetNode isn, ref IterationContext ctx, double[] pi, double[] avgPi)
        {
            byte decisionIndex = isn.DecisionIndex;
            var decision = _navigation.GameDefinition.DecisionsExecutionOrder[decisionIndex];
            byte numActions = decision.NumPossibleActions;
            byte playerAtNode = isn.PlayerIndex;

            var pAction = new double[numActions];
            if (decision.AlwaysDoAction is byte always)
            {
                SetAlwaysAction(numActions, pAction, always);
            }
            else
            {
                // Use current policies (opponentTraversal for the opponent).
                bool opponentProbabilities = playerAtNode != ctx.OptimizedPlayerIndex;
                isn.GetCurrentProbabilities(pAction, opponentProbabilities);
            }

            // Also need the average-strategy probability for next-avgPi stream.
            // We fetch lazily in the loop via isn.GetAverageStrategy(a).

            // Regret math requires expected values for each action for the optimized player.
            var expectedOptimizedForAction = new double[numActions];
            double expectedOptimized = 0.0;

            var expected = new double[NumNonChancePlayers];

            // Precompute counterfactual inverse reach w.r.t. optimized player.
            double inversePi = GetInversePiValue(pi, ctx.OptimizedPlayerIndex);

            for (byte a = 1; a <= numActions; a++)
            {
                double prob = pAction[a - 1];
                if (prob <= 0.0 && playerAtNode != ctx.OptimizedPlayerIndex)
                    continue; // pruning of zero-prob opponent branches

                double avgProb = isn.GetAverageStrategy(a);

                var nextPi = new double[NumNonChancePlayers];
                var nextAvgPi = new double[NumNonChancePlayers];
                GetNextPiValues(pi,     playerAtNode, prob,    changeOtherPlayers: false, dest: nextPi);
                GetNextPiValues(avgPi,  playerAtNode, avgProb, changeOtherPlayers: false, dest: nextAvgPi);

                HistoryPoint nextHp;
                if (_navigation.LookupApproach != InformationSetLookupApproach.PlayGameDirectly && isn.Decision.IsReversible)
                    nextHp = hp.SwitchToBranch(_navigation, a, isn.Decision, isn.DecisionIndex);
                else
                    nextHp = hp.GetBranch(_navigation, a, isn.Decision, isn.DecisionIndex);

                var childU = Visit(in nextHp, ref ctx, nextPi, nextAvgPi);

                // For utilities under CURRENT policy, we weight by prob.
                for (int i = 0; i < NumNonChancePlayers; i++)
                    expected[i] += prob * childU[i];

                expectedOptimizedForAction[a - 1] = childU[ctx.OptimizedPlayerIndex];

                if (_navigation.LookupApproach != InformationSetLookupApproach.PlayGameDirectly && isn.Decision.IsReversible)
                    _navigation.GameDefinition.ReverseSwitchToBranchEffects(isn.Decision, in nextHp);
            }

            if (playerAtNode == ctx.OptimizedPlayerIndex)
            {
                // Compute expected value V for optimized player under CURRENT policy.
                for (byte a = 1; a <= numActions; a++)
                    expectedOptimized += pAction[a - 1] * expectedOptimizedForAction[a - 1];

                double piSelf = pi[ctx.OptimizedPlayerIndex];
                double piAdj = piSelf < InformationSetNode.SmallestProbabilityRepresented
                    ? InformationSetNode.SmallestProbabilityRepresented
                    : piSelf;

                for (byte a = 1; a <= numActions; a++)
                {
                    double regret = expectedOptimizedForAction[a - 1] - expectedOptimized;
                    double contributionToAverageStrategy = piAdj * pAction[a - 1];

                    // Write directly into backing infoset tallies.
                    isn.IncrementLastRegret(a, regret * inversePi, inversePi);
                    isn.IncrementLastCumulativeStrategyIncrements(a, contributionToAverageStrategy);
                }
            }

            return expected;
        }

        #endregion

        #region Small helpers (CPU path)

        private static void SetAlwaysAction(byte numActions, double[] dest, byte actionOneBased)
        {
            for (int i = 0; i < numActions; i++) dest[i] = 0.0;
            dest[actionOneBased - 1] = 1.0;
        }

        private static double GetInversePiValue(double[] pi, byte playerIndex)
        {
            double product = 1.0;
            for (byte p = 0; p < pi.Length; p++)
                if (p != playerIndex)
                    product *= pi[p];
            return product;
        }

        private static void GetNextPiValues(double[] current, byte playerIndex, double prob, bool changeOtherPlayers, double[] dest)
        {
            for (byte p = 0; p < current.Length; p++)
            {
                double cur = current[p];
                if (p == playerIndex)
                    dest[p] = changeOtherPlayers ? cur : cur * prob;
                else
                    dest[p] = changeOtherPlayers ? cur * prob : cur;
            }
        }

        #endregion
    }
}
