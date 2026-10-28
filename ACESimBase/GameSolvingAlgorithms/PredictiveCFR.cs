using ACESimBase.GameSolvingSupport.GameTree;
using ACESimBase.GameSolvingSupport.Settings;
using ACESimBase.GameSolvingSupport.SolverSpecificSupport;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ACESim
{
    /// <summary>Full-tree CFR+ family. Regrets are aggregated over every history of an
    /// information set before clipping. A player sweep uses a frozen policy, with
    /// alternating updates by default. Legacy CFR enhancements are not applied.</summary>
    [Serializable]
    public sealed class PredictiveCFR : CounterfactualRegretMinimization
    {
        public int CompletedIterations { get; private set; }
        [field: NonSerialized]
        public event Action<PredictiveCFR, int> IterationCompleted;

        public PredictiveCFR(List<Strategy> strategies, EvolutionSettings settings, GameDefinition gameDefinition)
            : base(strategies, settings, gameDefinition)
        {
            settings.PredictiveCFR ??= new PredictiveCFRSettings();
            settings.PredictiveCFR.Validate();
            // Populate the complete tree for exact full-tree updates and reporting.
            AllowSkipEveryPermutationInitialization = false;
            BestBecomesResult = false;
        }

        public override async Task Initialize()
        {
            await base.Initialize();
            ActionStrategy = EvolutionSettings.PredictiveCFR.ReportCurrentStrategy ? ActionStrategies.CurrentProbability : ActionStrategies.AverageStrategy;
        }

        /// <summary>Runs one complete iteration without reporting or file I/O.
        /// GeneralizedVanillaFlavor (including Fast/GPU/unrolled) is intentionally
        /// not used: this family currently supports only its full-tree traversal.</summary>
        public void RunIteration()
        {
            var settings = EvolutionSettings.PredictiveCFR;
            settings.Validate();
            if (InformationSets == null) throw new InvalidOperationException("Initialize the solver before iterating.");
            int iteration = checked(CompletedIterations + 1);
            Status.IterationNum = iteration;
            Status.IterationNumDouble = iteration;
            StrategiesDeveloperStopwatch ??= new System.Diagnostics.Stopwatch();
            StrategiesDeveloperStopwatch.Start();
            try
            {
                for (byte player = 0; player < NumNonChancePlayers; player++)
                {
                    var ownReaches = new Dictionary<InformationSetNode, double>();
                    var start = GetStartOfGameHistoryPoint();
                    Traverse(in start, player, 1.0, 1.0, ownReaches);
                    if (settings.AlternatingUpdates)
                        UpdatePlayer(player, iteration, settings);
                }
                if (!settings.AlternatingUpdates)
                    for (byte player = 0; player < NumNonChancePlayers; player++)
                        UpdatePlayer(player, iteration, settings);
                CompletedIterations = iteration;
            }
            finally { StrategiesDeveloperStopwatch.Stop(); }
        }

        private void UpdatePlayer(byte player, int iteration, PredictiveCFRSettings settings)
        {
            foreach (var informationSet in InformationSets)
                if (informationSet.PlayerIndex == player)
                    UpdateInformationSet(informationSet, iteration, settings);
        }

        private double Traverse(in HistoryPoint history, byte player, double ownReach,
            double counterfactualReach, Dictionary<InformationSetNode, double> ownReaches)
        {
            var state = GetGameState(in history);
            if (state is FinalUtilitiesNode terminal) return terminal.Utilities[player];
            if (state is ChanceNode chance)
            {
                double value = 0;
                for (byte action = 1; action <= chance.Decision.NumPossibleActions; action++)
                {
                    double probability = chance.GetActionProbability(action);
                    var child = history.GetBranch(Navigation, action, chance.Decision, chance.DecisionIndex);
                    value += probability * Traverse(in child, player, ownReach, counterfactualReach * probability, ownReaches);
                }
                return value;
            }

            var node = (InformationSetNode)state;
            int count = node.NumPossibleActions;
            Span<double> probabilities = stackalloc double[count];
            node.GetCurrentProbabilities(probabilities, false);
            if (node.Decision.AlwaysDoAction is byte forcedAction)
                ActionProbabilityUtilities.SetProbabilitiesToAlwaysDoParticularAction((byte)count, probabilities, forcedAction);
            Span<double> actionValues = stackalloc double[count];
            bool owner = node.PlayerIndex == player;
            double expected = 0;
            for (byte action = 1; action <= count; action++)
            {
                double probability = probabilities[action - 1];
                var child = history.GetBranch(Navigation, action, node.Decision, node.DecisionIndex);
                actionValues[action - 1] = Traverse(in child, player,
                    owner ? ownReach * probability : ownReach,
                    owner ? counterfactualReach : counterfactualReach * probability, ownReaches);
                expected += probability * actionValues[action - 1];
            }
            if (owner)
            {
                // Perfect recall gives the same own reach at each history of this
                // information set. Average once per set; do not weight by chance/opponents.
                if (!ownReaches.TryGetValue(node, out double previousOwnReach))
                {
                    ownReaches.Add(node, ownReach);
                    for (int action = 0; action < count; action++)
                        node.NodeInformation[InformationSetNode.lastCumulativeStrategyIncrementsDimension, action] = ownReach * probabilities[action];
                }
                else if (Math.Abs(previousOwnReach - ownReach) > 1E-10)
                    throw new NotSupportedException("PredictiveCFR requires perfect recall: own reach differs across histories of an information set.");
                for (int action = 0; action < count; action++)
                    node.NodeInformation[InformationSetNode.sumRegretTimesInversePiDimension, action] += counterfactualReach * (actionValues[action] - expected);
            }
            return expected;
        }

        /// <summary>Applies one complete information-set update to accumulated raw
        /// counterfactual regrets and old-policy own-reach contributions. Public for
        /// numerical diagnostics; call once per information set per iteration.</summary>
        public static void UpdateInformationSet(InformationSetNode node, int iteration, PredictiveCFRSettings settings)
        {
            if (iteration < 1) throw new ArgumentOutOfRangeException(nameof(iteration));
            settings.Validate();
            double regretDiscount = settings.Discounted ? settings.RegretDiscount(iteration - 1) : 1;
            double predictionDiscount = settings.Discounted ? settings.RegretDiscount(iteration) : 1;
            double averageDiscount = settings.Discounted ? Math.Pow((iteration - 1.0) / iteration, settings.EffectiveGamma) : 1;
            double averageWeight = settings.Discounted ? 1 : Math.Pow(iteration, settings.EffectiveAveragingPower);
            if (!double.IsFinite(averageWeight))
                throw new ArgumentOutOfRangeException(nameof(settings), "The averaging exponent produces a nonfinite iteration weight.");
            double policySum = 0, averageSum = 0;
            var values = node.NodeInformation;
            for (int action = 0; action < node.NumPossibleActions; action++)
            {
                double regret = values[InformationSetNode.sumRegretTimesInversePiDimension, action];
                double baseRegret = Math.Max(0, regretDiscount * values[InformationSetNode.cumulativeRegretDimension, action] + regret);
                values[InformationSetNode.cumulativeRegretDimension, action] = baseRegret;
                double prediction = settings.Predictive ? Math.Max(0, predictionDiscount * baseRegret + regret) : baseRegret;
                values[InformationSetNode.currentProbabilityDimension, action] = prediction;
                policySum += prediction;
                double average = averageDiscount * values[InformationSetNode.cumulativeStrategyDimension, action]
                    + averageWeight * values[InformationSetNode.lastCumulativeStrategyIncrementsDimension, action];
                if (!double.IsFinite(baseRegret) || !double.IsFinite(prediction) || !double.IsFinite(average))
                    throw new InvalidOperationException("Predictive CFR received nonfinite regrets or averaging mass.");
                values[InformationSetNode.cumulativeStrategyDimension, action] = average;
                averageSum += average;
                values[InformationSetNode.sumRegretTimesInversePiDimension, action] = 0;
                values[InformationSetNode.sumInversePiDimension, action] = 0;
                values[InformationSetNode.lastCumulativeStrategyIncrementsDimension, action] = 0;
            }
            if (!double.IsFinite(policySum) || !double.IsFinite(averageSum))
                throw new InvalidOperationException("Predictive CFR accumulation exceeded finite numeric range.");
            for (int action = 0; action < node.NumPossibleActions; action++)
            {
                double probability = policySum > 0 ? values[InformationSetNode.currentProbabilityDimension, action] / policySum : 1.0 / node.NumPossibleActions;
                values[InformationSetNode.currentProbabilityDimension, action] = probability;
                values[InformationSetNode.currentProbabilityForOpponentDimension, action] = probability;
                values[InformationSetNode.averageStrategyProbabilityDimension, action] = averageSum > 0 ? values[InformationSetNode.cumulativeStrategyDimension, action] / averageSum : 1.0 / node.NumPossibleActions;
            }
        }

        public override async Task<ReportCollection> RunAlgorithm(string optionSetName)
        {
            var reports = new ReportCollection();
            while (CompletedIterations < EvolutionSettings.TotalIterations)
            {
                RunIteration();
                IterationCompleted?.Invoke(this, CompletedIterations);
                reports.Add(await ConsiderGeneratingReports(CompletedIterations,
                    () => $"{GameDefinition.OptionSetName} {EvolutionSettings.PredictiveCFR.Variant} iteration {CompletedIterations}"));
                if (Status.BestResponseTargetMet(EvolutionSettings.BestResponseTarget)) break;
            }
            return reports;
        }

        /// <summary>Report the exact average policy by default. Reporting must not
        /// round live policies or replace the learning state with an earlier best
        /// checkpoint, since the regret state and iteration clock must stay aligned.</summary>
        public override async Task<ReportCollection> ConsiderGeneratingReports(int iteration, Func<string> prefaceFn,
            string reportSuffix = "", bool suppressPrintTree = false)
        {
            var settings = EvolutionSettings;
            var priorProfiles = settings.ActionStrategiesToUseInReporting;
            bool priorCurrent = settings.UseCurrentStrategyForBestResponse;
            bool priorReportingRounding = settings.RoundOffLowProbabilitiesBeforeReporting;
            bool priorBestResponseRounding = settings.RoundOffLowProbabilitiesBeforeAcceleratedBestResponse;
            bool priorBestBecomesResult = BestBecomesResult;
            try
            {
                bool current = settings.PredictiveCFR.ReportCurrentStrategy;
                settings.ActionStrategiesToUseInReporting = new List<ActionStrategies>
                    { current ? ActionStrategies.CurrentProbability : ActionStrategies.AverageStrategy };
                settings.UseCurrentStrategyForBestResponse = current;
                settings.RoundOffLowProbabilitiesBeforeReporting = false;
                settings.RoundOffLowProbabilitiesBeforeAcceleratedBestResponse = false;
                BestBecomesResult = false;
                return await base.ConsiderGeneratingReports(iteration, prefaceFn, reportSuffix, suppressPrintTree);
            }
            finally
            {
                settings.ActionStrategiesToUseInReporting = priorProfiles;
                settings.UseCurrentStrategyForBestResponse = priorCurrent;
                settings.RoundOffLowProbabilitiesBeforeReporting = priorReportingRounding;
                settings.RoundOffLowProbabilitiesBeforeAcceleratedBestResponse = priorBestResponseRounding;
                BestBecomesResult = priorBestBecomesResult;
            }
        }

        public PredictiveCFRState CaptureState() => new PredictiveCFRState
        {
            CompletedIterations = CompletedIterations,
            Settings = EvolutionSettings.PredictiveCFR.DeepCopy(),
            InformationSetKeys = InformationSets.Select(StateKey).ToArray(),
            NodeInformation = InformationSets.Select(x => (double[,])x.NodeInformation.Clone()).ToArray()
        };

        /// <summary>Restores learning state into the same game parameterization and
        /// scenario. Node keys validate information-set structure; the caller must
        /// retain the original payoffs and chance probabilities.</summary>
        public void RestoreState(PredictiveCFRState state)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (state.CompletedIterations < 0 || state.Settings == null || state.InformationSetKeys == null || state.NodeInformation == null)
                throw new ArgumentException("Incomplete predictive CFR state.", nameof(state));
            state.Settings.Validate();
            if (InformationSets == null || state.NodeInformation.Length != InformationSets.Count || state.InformationSetKeys.Length != InformationSets.Count)
                throw new ArgumentException("State does not match the initialized game.", nameof(state));
            var savedNodes = new Dictionary<string, double[,]>(StringComparer.Ordinal);
            for (int index = 0; index < state.InformationSetKeys.Length; index++)
                if (state.InformationSetKeys[index] == null || !savedNodes.TryAdd(state.InformationSetKeys[index], state.NodeInformation[index]))
                    throw new ArgumentException("State contains duplicate or missing information-set keys.", nameof(state));
            // Validate every node before mutating any live state.
            foreach (var node in InformationSets)
                if (!savedNodes.TryGetValue(StateKey(node), out var information) || information == null
                    || information.GetLength(0) != InformationSetNode.totalDimensions
                    || information.GetLength(1) != node.NumPossibleActions)
                    throw new ArgumentException("State information sets do not match the initialized game.", nameof(state));
            EvolutionSettings.PredictiveCFR = state.Settings.DeepCopy();
            foreach (var node in InformationSets)
                node.NodeInformation = (double[,])savedNodes[StateKey(node)].Clone();
            CompletedIterations = state.CompletedIterations;
            Status.IterationNum = CompletedIterations;
            Status.IterationNumDouble = CompletedIterations;
        }

        private static string StateKey(InformationSetNode node) => $"{node.PlayerIndex}:{node.DecisionIndex}:{node.NumPossibleActions}:{string.Join(",", node.InformationSetContents ?? Array.Empty<byte>())}";

        public override IStrategiesDeveloper DeepCopy()
        {
            var copy = new PredictiveCFR(Strategy.GetStarterStrategies(GameDefinition), EvolutionSettings.CopyForPredictiveCFR(), GameDefinition);
            copy.Initialize().GetAwaiter().GetResult();
            copy.RestoreState(CaptureState());
            copy.ActionStrategy = ActionStrategy;
            return copy;
        }

        /// <summary>A fresh repetition resets the predictive clock as well as node learning state.
        /// RestoreState remains the explicit resume operation.</summary>
        public override void Reinitialize() => ResetLearningState();

        public void ResetLearningState()
        {
            ReinitializeInformationSets();
            CompletedIterations = 0;
            Status.IterationNum = 0;
            Status.IterationNumDouble = 0;
        }

        public override void CompleteReinitializeForScenario(bool warmupVersion, int previousScenarioIndex, double previousWeightOnOpponentP0,
            double previousWeightOnOpponentOtherPlayers, int updatedScenarioIndex, bool alwaysReinitialize)
        {
            if (warmupVersion || !GameDefinition.UseDifferentWarmup || alwaysReinitialize) ResetLearningState();
            if (FinalUtilitiesNodes != null)
                foreach (var node in FinalUtilitiesNodes)
                {
                    node.CurrentInitializedScenarioIndex = updatedScenarioIndex;
                    node.WeightOnOpponentsUtilityP0 = GameDefinition.CurrentWeightOnOpponentP0;
                    node.WeightOnOpponentsUtilityOtherPlayers = GameDefinition.CurrentWeightOnOpponentOtherPlayers;
                }
            CalculateMinMax();
        }
    }

    /// <summary>Checkpoint includes base regrets, current prediction policy, average
    /// accumulators, settings, and iteration clock. Arrays are detached from the solver.</summary>
    [Serializable]
    public sealed class PredictiveCFRState
    {
        public int CompletedIterations;
        public PredictiveCFRSettings Settings;
        public string[] InformationSetKeys;
        public double[][,] NodeInformation;
    }
}
