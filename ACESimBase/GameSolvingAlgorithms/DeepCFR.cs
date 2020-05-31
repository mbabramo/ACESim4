using ACESim.Util;
using ACESimBase;
using ACESimBase.GameSolvingSupport;
using ACESimBase.Util;
using JetBrains.Annotations;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

namespace ACESim
{

    // TODO
    // 1. Principal components analysis. We can reduce a player's strategy to a few principal components. To do this, we need strategies at various times (or with various initial settings, such as utility to share). We need a common reservoir of observations for each of various decisions. We might create that reservoir in an initial iteration, when all decisions are equally likely to occur, but on the other hand it probably makes sense to specialize the reservoir to the decisions most likely to come up in game play (taking some random observations from each of the strategies). Note that we'll need to be able to reverse the PCA to generate a strategy; this will occur first by generating the actions taken for particular strategies and then re-generating the strategy. The PCA may be interesting in and of itself (showing the basic aspects of strategy), but also might be used as part of a technique to minimize best response improvement. That is, we might create a neural network with pairs of players' strategies (again, from different times and/or from different initial settings) and then calculate best response improvement sums. Then, we could optimize the input to this neural network by minimizing this. That's essentially what we tried before without PCA, but it should be much more manageable with just a few principal components. 
    // 2. Correlated equilibrium. With or without PCA, we need to be able to try to build a correlated equilibrium, adapting the code that we used with regret matching etc. The challenge here is that we don't want to check every strategy against every other strategy, because there are too many possibilities. The problem is that we need to measure exploitability (for example, using our proxy measure) with every PAIR of strategies. One possibility is that we can combine strategies as they have occurred together or use PCA with both strategies together, with either approach giving us a natural way of combining strategies. Alternatively, with PCA, we might start with only the first principal component of each party's strategy and generate exploitability numbers for each pair. Finally, we might model exploitability, by randomly choosing pairs of strategies and then using some form of regression to get predicted exploitability. Whichever approach is chosen, once we have the exploitability measures, then our lowest-exploitability strategy pair becomes the first element of our correlated equilibrium. Then we need to find another pair that can be the second element of our correlated equilibrium, i.e. where neither player would defect from either pair to the other based on playing the game a certain number of times. We might test in order of our initial exploitability measure. Each new candidate must be tested against all of the strategies already in the correlated equilibrium. We might define a stopping condition, such as after some number of consecutive failures.
    // A different approach might be to create a regression model predicting utility matching different pairs of strategies. For this, PCA would be necessary. Our regression might be based on a few hundred examples. Thus, we can generate a prediction for many pairs of strategies (e.g., 100 x 100 strategies or even 1,000 x 1,000). Initially, we could look to see whether there exist any Nash equilibria. In principle, we could try to zoom in near the most promising spot for a Nash equilibrium (i.e., where there is the lowest possible benefit for defection) and test with higher resolution, possibly using actual game play instead of the model; but even after we zoom, the best alternative candidate might be outside the zoomed area, so we might need to do some actual game play with other possibilities. Regardless of the result of this, we could try to build a correlated equilibrium, solely by considering this matrix of predictions. We might try to generate a number of different correlated equilibria, starting with different parts of the matrix. Given a candidate correlated equilibrium, we might then figure out the actual utility; we then repeat the analysis used to build a correlated equilibrium, but with this much smaller matrix, thus eliminating some items from the correlated equilibrium. We might then try to expand this set by looking at strategy pairs that the model suggests might be close to working. That is, we would order all excluded pairs by how close they are to being capable of added to the correlated equilibrium. We would then try the best one and keep going until we could add one; if we add one successfully, then we would reorder.
    // 3. Symmetry. Does symmetry make things easier? Suppose we have 100 strategies. We still need to determine whether a player would have an incentive to defect from a symmetric strategy to a nonsymmetric strategy (by playing the player's part of some other symmetric strategy, while the opponent hypothetically sticks with the symmetric strategy). So, for each of the 100 possible strategies, we still need to measure the profit from defecting to any of the other strategies, so we still need to be able to figure out scores for 100 x 100 strategies. (Again, we could approximate with a regression, as above.) There is some benefit, in that we don't have to consider defection possibilities separately for both players. That is, if and only if one player would defect, the other player will defect as well. Meanwhile, it should take half as much time to generate the models. (NOTE: The basic symmetry is implemented, but not the application to PCA.)


    [Serializable]
    public partial class DeepCFR : CounterfactualRegretMinimization
    {
        DeepCFRMultiModel MultiModel;

        DeepCFRMultiModel GenotypingBaselineMultiModel;
        public List<float[][]> SavedGenotypes; // saved genotypes by time, player, and observation index from baseline multimodel
        public PCAResultsForPlayer[] PCAResultsForEachPlayer;
        public List<DeepCFRMultiModel>[] PCAStrategiesForEachPlayer;
        public RegressionController[] ModelsToPredictUtilitiesFromPrincipalComponents;

        int ApproximateBestResponse_CurrentIterationsTotal;
        int ApproximateBestResponse_CurrentIterationsIndex;
        byte ApproximateBestResponse_CurrentPlayer;

        bool TakeShortcutInSymmetricGames = true;
        bool UsingShortcutForSymmetricGames;

        GameProgressTree[] GameProgressTrees; // used internally, kept as a field to facilitate disposal (and thus array pooling)

        #region Initialization

        public DeepCFR(List<Strategy> existingStrategyState, EvolutionSettings evolutionSettings, GameDefinition gameDefinition) : base(existingStrategyState, evolutionSettings, gameDefinition)
        {
            UsingShortcutForSymmetricGames = TakeShortcutInSymmetricGames && gameDefinition.GameIsSymmetric();

            int[] reservoirCapacity = GameDefinition.DecisionsExecutionOrder.Select(x =>
            {
                if (x.IsChance)
                    return 0; // this is ignored, but we keep it in the list so that capacity is associated with decision index
                else if (UsingShortcutForSymmetricGames && x.PlayerIndex == 1)
                    return 0;
                else if (EvolutionSettings.DeepCFR_UseGameProgressTreeToGenerateObservations)
                    return x.NumPossibleActions * EvolutionSettings.DeepCFR_BaseReservoirCapacity;
                else
                    return EvolutionSettings.DeepCFR_BaseReservoirCapacity;
            }).ToArray();
            MultiModel = new DeepCFRMultiModel(GameDefinition, EvolutionSettings.DeepCFR_MultiModelMode, reservoirCapacity, 0, EvolutionSettings.DeepCFR_DiscountRate, UsingShortcutForSymmetricGames, EvolutionSettings.RegressionFactory());
        }

        public override IStrategiesDeveloper DeepCopy()
        {
            var created = new DeepCFR(Strategies, EvolutionSettings, GameDefinition);
            DeepCopyHelper(created);
            // Note: Not currently copying Model. 
            return created;
        }

        public override Task Initialize()
        {
            return Task.CompletedTask;
        }

        #endregion

        #region Run algorithm

        public override async Task<ReportCollection> RunAlgorithm(string optionSetName)
        {
            ReportCollection reportCollection = new ReportCollection();
            for (int iteration = 1; iteration <= EvolutionSettings.TotalIterations; iteration++)
            {
                var result = await PerformDeepCFRIteration(iteration, false);
                reportCollection.Add(result);
            }
            if (EvolutionSettings.DeepCFR_ApproximateBestResponse)
            {
                await DoApproximateBestResponse();
            }
            return reportCollection;
        }

        private async Task<ReportCollection> PerformDeepCFRIteration(int iteration, bool isBestResponseIteration)
        {
            Status.IterationNumDouble = iteration;

            Stopwatch localStopwatch = new Stopwatch();
            localStopwatch.Start();
            StrategiesDeveloperStopwatch.Start();
            ReportIteration(iteration, isBestResponseIteration);

            await DeepCFR_GenerateObservations(iteration, isBestResponseIteration);

            localStopwatch.Stop();
            StrategiesDeveloperStopwatch.Stop();
            TabbedText.WriteLine($" time {localStopwatch.ElapsedMilliseconds} ms");

            TabbedText.TabIndent();
            localStopwatch = new Stopwatch();
            localStopwatch.Start();
            await MultiModel.CompleteIteration(EvolutionSettings.ParallelOptimization);
            TabbedText.TabUnindent();
            TabbedText.WriteLine($"All models computed, time {localStopwatch.ElapsedMilliseconds} ms");
            localStopwatch.Stop();

            await Genotyping(iteration);

            double[] exploitabilityProxy;
            if (EvolutionSettings.DeepCFR_ExploitabilityProxy)
                exploitabilityProxy = await DeepCFR_ExploitabilityProxy(iteration, isBestResponseIteration);

            ReportCollection reportCollection = new ReportCollection();
            if (!isBestResponseIteration)
            {
                var result = await GenerateReports(iteration,
                    () =>
                        $"{GameDefinition.OptionSetName} Iteration {iteration} Overall milliseconds per iteration {StrategiesDeveloperStopwatch.ElapsedMilliseconds / (double)iteration}");
                reportCollection.Add(result);
            }

            return reportCollection;

        }

        private async Task DeepCFR_GenerateObservations(int iteration, bool isBestResponseIteration)
        {
            if (EvolutionSettings.DeepCFR_UseGameProgressTreeToGenerateObservations)
                await DeepCFR_GenerateObservations_WithGameProgressTree(iteration, isBestResponseIteration);
            else
                await DeepCFR_GenerateObservations_WithRandomPlay(iteration, isBestResponseIteration);
        }

        private async Task DoApproximateBestResponse()
        {
            double[] baselineUtilities = await DeepCFR_UtilitiesAverage(EvolutionSettings.DeepCFR_ApproximateBestResponse_TraversalsForUtilityCalculation);
            TabbedText.WriteLine($"Baseline utilities {string.Join(",", baselineUtilities.Select(x => x.ToSignificantFigures(8)))}");
            double[] bestResponseImprovement = new double[NumNonChancePlayers];
            for (byte p = 0; p < NumNonChancePlayers; p++)
            {
                await MultiModel.PrepareForBestResponseIterations(EvolutionSettings.ParallelOptimization, EvolutionSettings.DeepCFR_ApproximateBestResponse_BackwardInduction_CapacityMultiplier);
                ApproximateBestResponse_CurrentPlayer = p;
                TabbedText.WriteLine($"Determining best response for player {p}");
                TabbedText.TabIndent();
                double[] bestResponseUtilities;
                if (EvolutionSettings.DeepCFR_ApproximateBestResponse_BackwardInduction)
                {
                    var decisionsForPlayer = GameDefinition.DecisionsExecutionOrder.Select((item, index) => (item, index)).Where(x => x.item.PlayerIndex == p).OrderByDescending(x => x.index).ToList();
                    int innerIterationsNeeded = decisionsForPlayer.Count();
                    ApproximateBestResponse_CurrentIterationsTotal = innerIterationsNeeded * EvolutionSettings.DeepCFR_ApproximateBestResponseIterations;
                    for (int outerIteration = 0; outerIteration < EvolutionSettings.DeepCFR_ApproximateBestResponseIterations; outerIteration++)
                    {
                        bestResponseUtilities = await DeepCFR_UtilitiesAverage(EvolutionSettings.DeepCFR_ApproximateBestResponse_TraversalsForUtilityCalculation);
                        for (int innerIteration = 1; innerIteration <= innerIterationsNeeded; innerIteration++)
                        {
                            ApproximateBestResponse_CurrentIterationsIndex = outerIteration * innerIterationsNeeded + innerIteration;
                            byte decisionIndex = (byte)decisionsForPlayer[innerIteration - 1].index; // this is the overall decision index, i.e. in GameDefinition.DecisionsExecutionOrder
                            MultiModel.TargetBestResponse(p, decisionIndex);
                            if (EvolutionSettings.DeepCFR_ApproximateBestResponse_BackwardInduction_AlwaysPickHighestRegret)
                                MultiModel.StopRegretMatching(p, decisionIndex);
                            var result = await PerformDeepCFRIteration(innerIteration, true);
                            bestResponseUtilities = await DeepCFR_UtilitiesAverage(EvolutionSettings.DeepCFR_ApproximateBestResponse_TraversalsForUtilityCalculation);
                            MultiModel.ConcludeTargetingBestResponse(p, decisionIndex);
                            bestResponseUtilities = await DeepCFR_UtilitiesAverage(EvolutionSettings.DeepCFR_ApproximateBestResponse_TraversalsForUtilityCalculation);
                            TabbedText.WriteLine($"Utilities for player {p}: {string.Join(",", bestResponseUtilities.Select(x => x.ToSignificantFigures(8)))}");
                        }
                    }
                    bestResponseUtilities = await DeepCFR_UtilitiesAverage(EvolutionSettings.DeepCFR_ApproximateBestResponse_TraversalsForUtilityCalculation);
                    if (EvolutionSettings.DeepCFR_ApproximateBestResponse_BackwardInduction_AlwaysPickHighestRegret)
                        MultiModel.ResumeRegretMatching();
                }
                else
                {
                    MultiModel.TargetBestResponse(p, null);
                    for (int iteration = 1; iteration <= EvolutionSettings.DeepCFR_ApproximateBestResponseIterations; iteration++)
                    {
                        var result = await PerformDeepCFRIteration(iteration, true);
                    }
                    bestResponseUtilities = await DeepCFR_UtilitiesAverage(EvolutionSettings.DeepCFR_ApproximateBestResponse_TraversalsForUtilityCalculation);
                    MultiModel.ConcludeTargetingBestResponse(p, null);
                }

                TabbedText.TabUnindent();
                TabbedText.WriteLine($"Concluding determining best response for player {p} (recreating earlier models)");
                TabbedText.TabIndent();
                TabbedText.TabUnindent();
                TabbedText.WriteLine($"Utilities with best response for player {p}: {string.Join(",", bestResponseUtilities.Select(x => x.ToSignificantFigures(8)))}");
                bestResponseImprovement[p] = bestResponseUtilities[p] - baselineUtilities[p];
                TabbedText.WriteLine($"Best response improvement for player {p}: {bestResponseImprovement[p].ToSignificantFigures(8)}");
                await MultiModel.ReturnToStateBeforeBestResponseIterations(EvolutionSettings.ParallelOptimization);
            }
            TabbedText.WriteLine($"Best response improvement for all players: {bestResponseImprovement.ToSignificantFigures(8)}");
        }

        #endregion

        #region Traversal

        private async Task DeepCFR_GenerateObservations_WithRandomPlay(int iteration, bool isBestResponseIteration)
        {
            int[] numObservationsToAdd = MultiModel.CountPendingObservationsTarget(iteration, isBestResponseIteration, false);
            int numObservationsToAddMax = numObservationsToAdd != null && numObservationsToAdd.Any() ? numObservationsToAdd.Max() : EvolutionSettings.DeepCFR_BaseReservoirCapacity;
            int numObservationsToDoPerThread = GetNumToDoPerThread(numObservationsToAddMax);

            bool separateDataEveryIteration = true;
            DeepCFRProbabilitiesCache probabilitiesCache = new DeepCFRProbabilitiesCache();
            ParallelConsecutive<List<DeepCFRObservationOfDecision>> runner = new ParallelConsecutive<List<DeepCFRObservationOfDecision>>(
                (numCompleted) => TargetMet(iteration, isBestResponseIteration, numCompleted * numObservationsToDoPerThread, numObservationsToAdd),
                i =>
                {
                    var regressionMachines = GetRegressionMachinesForLocalUse(); // note that everything within this block will be on same thread
                    DeepCFRPlaybackHelper playbackHelper = new DeepCFRPlaybackHelper(MultiModel, regressionMachines, probabilitiesCache);
                    var additionalRegretObservations = DeepCFR_AddingRegretObservations(playbackHelper, i, separateDataEveryIteration ? iteration * 1000 : 0, numObservationsToDoPerThread);
                    ReturnRegressionMachines(regressionMachines);
                    return additionalRegretObservations;
                },
                results =>
                {
                    foreach (var result in results)
                        MultiModel.AddPendingObservation(result.decision, result.decisionIndex, result.observation);
                }
                );
            await runner.Run(
                EvolutionSettings.ParallelOptimization);

            bool TargetMet(int iteration, bool isBestResponseIteration, int numberCompleted, int[] numObservationsToAdd)
            {
                bool targetMet;
                if (!(iteration == 1 && !isBestResponseIteration) && numberCompleted >= EvolutionSettings.DeepCFR_MaximumTotalObservationsPerIteration)
                    targetMet = true;
                else
                    targetMet = MultiModel.AllMeetPendingObservationsTarget(numObservationsToAdd);
                return targetMet;
            }
        }

        public List<DeepCFRObservationOfDecision> DeepCFR_AddingRegretObservations(DeepCFRPlaybackHelper playbackHelper, int observationIndex, int variationNum, int numToDoTogether)
        {
            int initialObservationNum = observationIndex * numToDoTogether;
            List<DeepCFRObservationOfDecision> result = new List<DeepCFRObservationOfDecision>();
            for (int i = 0; i < numToDoTogether; i++)
            {
                DeepCFRObservationNum observationNum = new DeepCFRObservationNum(initialObservationNum + i, variationNum);
                var traversalResult = DeepCFRTraversal(playbackHelper, observationNum, DeepCFRTraversalMode.AddRegretObservations).observations;
                result.AddRange(traversalResult);
            }
            return result;
        }

        public (double[] utilities, List<DeepCFRObservationOfDecision> observations) DeepCFRTraversal(DeepCFRPlaybackHelper playbackHelper, DeepCFRObservationNum observationNum, DeepCFRTraversalMode traversalMode)
        {
            List<DeepCFRObservationOfDecision> observations = new List<DeepCFRObservationOfDecision>();
            return (DeepCFRTraversal(playbackHelper, observationNum, traversalMode, observations).utilities, observations);
        }

        private (double[] utilities, GameProgress completedProgress) DeepCFRTraversal(DeepCFRPlaybackHelper playbackHelper, DeepCFRObservationNum observationNum, DeepCFRTraversalMode traversalMode, List<DeepCFRObservationOfDecision> observations)
        {
            double[] finalUtilities;
            DeepCFRDirectGamePlayer gamePlayer = new DeepCFRDirectGamePlayer(EvolutionSettings.DeepCFR_MultiModelMode, GameDefinition, GameFactory.CreateNewGameProgress(false, new IterationID(observationNum.ObservationNum)), true, UsingShortcutForSymmetricGames, playbackHelper);
            finalUtilities = DeepCFRTraversal(gamePlayer, observationNum, observations, traversalMode);
            return (finalUtilities, gamePlayer.GameProgress);
        }

        public double[] DeepCFRTraversal(DeepCFRDirectGamePlayer gamePlayer, DeepCFRObservationNum observationNum, List<DeepCFRObservationOfDecision> observations, DeepCFRTraversalMode traversalMode)
        {
            GameStateTypeEnum gameStateType = gamePlayer.GetGameStateType();
            if (gameStateType == GameStateTypeEnum.FinalUtilities)
            {
                return gamePlayer.GetFinalUtilities();
            }
            else if (gameStateType == GameStateTypeEnum.Chance)
            {
                return DeepCFR_ChanceNode(gamePlayer, observationNum, observations, traversalMode);
            }
            else
                return DeepCFR_DecisionNode(gamePlayer, observationNum, observations, traversalMode);
        }

        private double[] DeepCFR_DecisionNode(DeepCFRDirectGamePlayer gamePlayer, DeepCFRObservationNum observationNum, List<DeepCFRObservationOfDecision> observations, DeepCFRTraversalMode traversalMode)
        {
            Decision currentDecision = gamePlayer.CurrentDecision;
            var playbackHelper = gamePlayer.PlaybackHelper;
            byte decisionIndex = (byte)gamePlayer.CurrentDecisionIndex;
            byte adjustedDecisionIndex = UsingShortcutForSymmetricGames && currentDecision.PlayerIndex == 1 ? (byte)(decisionIndex - 1) : decisionIndex;
            IRegressionMachine regressionMachineForCurrentDecision = playbackHelper.GetRegressionMachineIfExists(adjustedDecisionIndex); 
            byte playerMakingDecision = gamePlayer.CurrentPlayer.PlayerIndex;
            byte numPossibleActions = NumPossibleActionsAtDecision(decisionIndex);
            DeepCFRIndependentVariables independentVariables = null;
            double[] onPolicyProbabilities;
            (independentVariables, onPolicyProbabilities) = gamePlayer.GetIndependentVariablesAndPlayerProbabilities(observationNum);
            byte mainAction = GameDefinition.DecisionsExecutionOrder[decisionIndex].AlwaysDoAction ?? gamePlayer.ChooseAction(observationNum, decisionIndex, onPolicyProbabilities);
            DeepCFRDirectGamePlayer mainActionPlayer = traversalMode == DeepCFRTraversalMode.PlaybackSinglePath ? gamePlayer : (DeepCFRDirectGamePlayer)gamePlayer.DeepCopy();
            mainActionPlayer.PlayAction(mainAction);
            double[] mainValues = DeepCFRTraversal(mainActionPlayer, observationNum, observations, traversalMode);
            if (traversalMode != DeepCFRTraversalMode.PlaybackSinglePath)
                mainActionPlayer.Dispose();
            if (traversalMode == DeepCFRTraversalMode.AddRegretObservations)
            {
                if (MultiModel.ObservationsNeeded(decisionIndex))
                {
                    // We do a single probe. This allows us to compare this result either to the result from the main action (fast, but high variance) or to the result from all of the other actions (slow, but low variance).
                    DeepCFRObservationNum probeIteration = observationNum.NextVariation();
                    byte probeAction = playbackHelper.MultiModel.ChooseAction(currentDecision, decisionIndex, regressionMachineForCurrentDecision, probeIteration.GetRandomDouble(decisionIndex), independentVariables /* note that action in this is ignored */, numPossibleActions, numPossibleActions /* TODO */, EvolutionSettings.DeepCFR_Epsilon_OffPolicyProbabilityForProbe /* doesn't matter if probing all actions */, ref onPolicyProbabilities);
                    // Note: probe action might be same as main action. That's OK, because this helps us estimate expected regret, which is probabilistic
                    double sampledRegret;
                    if (EvolutionSettings.DeepCFR_ProbeAllActions)
                    {
                        if (onPolicyProbabilities == null)
                            onPolicyProbabilities = playbackHelper.MultiModel.GetRegretMatchingProbabilities(currentDecision, decisionIndex, independentVariables, regressionMachineForCurrentDecision);
                        double utilityForProbeAction = 0, expectedUtility = 0;
                        for (byte a = 1; a <= currentDecision.NumPossibleActions; a++)
                        {
                            double[] utilitiesForAction;
                            if (a == mainAction)
                                utilitiesForAction = mainValues;
                            else
                                utilitiesForAction = DeepCFR_Probe_GetUtilitiesForPlayersForSingleAction(gamePlayer, observationNum, observations, a);
                            double utilityForAction = utilitiesForAction[playerMakingDecision];
                            if (a == probeAction)
                                utilityForProbeAction = utilityForAction;
                            expectedUtility += onPolicyProbabilities[a - 1] * utilityForAction;
                        }
                        if (EvolutionSettings.DeepCFR_PredictUtilitiesNotRegrets)
                            sampledRegret = utilityForProbeAction; // not really sampled regret
                        else
                            sampledRegret = utilityForProbeAction - expectedUtility; 
                    }
                    else
                    {
                        double[] probeValues = DeepCFR_Probe_GetUtilitiesForPlayersForSingleAction(gamePlayer, observationNum, observations, probeAction);
                        sampledRegret = probeValues[playerMakingDecision] - mainValues[playerMakingDecision];
                    }
                    DeepCFRObservation observation = new DeepCFRObservation()
                    {
                        SampledRegret = sampledRegret,
                        IndependentVariables = new DeepCFRIndependentVariables(playerMakingDecision, decisionIndex, independentVariables.InformationSet, probeAction, null /* TODO */),
                        Weight = 1.0
                    };
                    observations.Add((currentDecision, decisionIndex, observation));
                }
            }
            return mainValues;
        }

        private double[] DeepCFR_Probe_GetUtilitiesEachAction(DeepCFRDirectGamePlayer gamePlayer, DeepCFRObservationNum observationNum, int numProbes)
        {
            byte numPossibleActions = gamePlayer.CurrentDecision.NumPossibleActions;
            byte currentPlayerIndex = gamePlayer.CurrentPlayer.PlayerIndex;
            double[] results = new double[numPossibleActions];
            for (int probe = 0; probe < numProbes; probe++)
            {
                for (byte probeAction = 1; probeAction <= numPossibleActions; probeAction++)
                {
                    double[] utilitiesBothPlayers = DeepCFR_Probe_GetUtilitiesForPlayersForSingleAction(gamePlayer, observationNum, null, probeAction);
                    results[probeAction - 1] += utilitiesBothPlayers[currentPlayerIndex];
                }
                observationNum = observationNum.NextVariation();
            }
            if (numProbes > 1)
                for (byte probeAction = 1; probeAction <= numPossibleActions; probeAction++)
                    results[probeAction - 1] /= (double)numProbes;
            return results;
        }

        private double[] DeepCFR_Probe_GetUtilitiesForPlayersForSingleAction(DeepCFRDirectGamePlayer gamePlayer, DeepCFRObservationNum observationNum, List<DeepCFRObservationOfDecision> observations, byte probeAction)
        {
            double[] probeValues = null;
            using (DeepCFRDirectGamePlayer probeGamePlayer = (DeepCFRDirectGamePlayer)gamePlayer.DeepCopy())
            {
                probeGamePlayer.PlayAction(probeAction);
                probeValues = DeepCFRTraversal(probeGamePlayer, observationNum, observations, DeepCFRTraversalMode.ProbeForUtilities);
            }
            return probeValues;
        }

        private double[] DeepCFR_ChanceNode(DeepCFRDirectGamePlayer gamePlayer, DeepCFRObservationNum observationNum, List<DeepCFRObservationOfDecision> observations, DeepCFRTraversalMode traversalMode)
        {
            Decision currentDecision = gamePlayer.CurrentDecision;
            if (currentDecision.CriticalNode && traversalMode != DeepCFRTraversalMode.PlaybackSinglePath)
            {
                // At a critical node, we take all paths and weight them by probability.
                double[] weightedResults = new double[NumNonChancePlayers];
                double[] probabilitiesForActions = gamePlayer.GetChanceProbabilities();
                for (byte a = 1; a <= currentDecision.NumPossibleActions; a++)
                {
                    using (DeepCFRDirectGamePlayer copyPlayer = (DeepCFRDirectGamePlayer)gamePlayer.DeepCopy())
                    {
                        copyPlayer.PlayAction(a);
                        double[] utilities = DeepCFRTraversal(copyPlayer, observationNum, observations, traversalMode);
                        for (int i = 0; i < NumNonChancePlayers; i++)
                            weightedResults[i] += probabilitiesForActions[a - 1] * utilities[i];
                    }
                }
                return weightedResults;
            }
            else
            {
                byte actionToChoose = gamePlayer.ChooseChanceAction(observationNum.GetRandomDouble((byte)gamePlayer.CurrentDecisionIndex));
                gamePlayer.PlayAction(actionToChoose);
                return DeepCFRTraversal(gamePlayer, observationNum, observations, traversalMode);
            }
        }

        #endregion

        #region Game progress trees

        public async Task<GameProgressTree> DeepCFR_BuildGameProgressTree(int totalNumberObservations, int randSeed, bool oversampling, double explorationValue = 0, byte? limitToPlayer = null)
        {
            Dictionary<byte, IRegressionMachine> regressionMachines = null;
            if (CompoundRegressionMachinesContainer != null)
                regressionMachines = CompoundRegressionMachinesContainer.GetRegressionMachinesForLocalUse();
            DeepCFRPlaybackHelper playbackHelper = new DeepCFRPlaybackHelper(MultiModel, regressionMachines, null);
            bool doParallel = false; // TODO: Make it so that we can do parallel at least when: EvolutionSettings.ParallelOptimization && regressionMachines == null; // Note: GameProgressTree does not have a way of requesting new regression machines (and a new playbackHelper) every time it wants to create a new thread.
            GameProgress initialGameProgress = GameFactory.CreateNewGameProgress(false, new IterationID(1));
            DeepCFRDirectGamePlayer directGamePlayer = new DeepCFRDirectGamePlayer(EvolutionSettings.DeepCFR_MultiModelMode, GameDefinition, initialGameProgress, true, UsingShortcutForSymmetricGames, playbackHelper);
            double[] explorationValues = explorationValue == 0 ? null /* no exploration */ : Enumerable.Range(0, NumNonChancePlayers).Select(x => x == limitToPlayer ? explorationValue : 0).ToArray();
            GameProgressTree gameProgressTree = new GameProgressTree(
                randSeed, // rand seed
                totalNumberObservations,
                directGamePlayer,
                explorationValues,
                NumNonChancePlayers,
                GameDefinition.DecisionsExecutionOrder,
                limitToPlayer
                );
            await gameProgressTree.CompleteTree(doParallel, explorationValues, oversampling);
            if (CompoundRegressionMachinesContainer != null)
                CompoundRegressionMachinesContainer.ReturnRegressionMachines(regressionMachines);
            return gameProgressTree;
        }

        private async Task DeepCFR_GenerateObservations_WithGameProgressTree(int iteration, bool isBestResponseIteration) => await DeepCFR_GenerateObservations_WithGameProgressTree_AddingPendingObservations(iteration, isBestResponseIteration, true);

        private async Task DeepCFR_GenerateObservations_WithGameProgressTree_AddingPendingObservations(int iteration, bool isBestResponseIteration, bool oversampling)
        {
            var gamesToComplete = await DeepCFR_GetGamesToComplete(iteration, isBestResponseIteration, oversampling);
            Stopwatch stopwatch = new Stopwatch();
            stopwatch.Start();
            DeepCFR_CompleteGames_FromGameProgressTree_AddPendingObservations(gamesToComplete);
            stopwatch.Stop();
            foreach (GameProgressTree tree in GameProgressTrees)
                tree.Dispose();
            //TabbedText.Write($"(Finishing games time {stopwatch.ElapsedMilliseconds} ms) ");
        }

        private async Task<List<(Decision currentDecision, int decisionIndex, byte currentPlayer, DeepCFRDirectGamePlayer gamePlayer, DeepCFRObservationNum observationNum, int numObservations)>> DeepCFR_GetGamesToComplete(int iteration, bool isBestResponseIteration, bool oversampling)
        {
            int[] numObservationsNeeded = MultiModel.CountPendingObservationsTarget(iteration, isBestResponseIteration, true);
            int DivideRoundingUp(int a, int b) => a / b + (a % b != 0 ? 1 : 0);
            int[] numDirectGamePlayersNeeded = numObservationsNeeded.Select((item, index) => DivideRoundingUp(item, GameDefinition.DecisionsExecutionOrder[index].NumPossibleActions)).ToArray();
            int maxDirectGamePlayersNeeded = numDirectGamePlayersNeeded.Max();
            List<(Decision currentDecision, int decisionIndex, byte currentPlayer, DeepCFRDirectGamePlayer gamePlayer, DeepCFRObservationNum observationNum, int numObservations)> gamesToComplete = await DeepCFR_GetGamesToComplete(iteration, isBestResponseIteration, oversampling, numObservationsNeeded, maxDirectGamePlayersNeeded);
            return gamesToComplete;
        }

        private async Task<List<(Decision currentDecision, int decisionIndex, byte currentPlayer, DeepCFRDirectGamePlayer gamePlayer, DeepCFRObservationNum observationNum, int numObservations)>> DeepCFR_GetGamesToComplete(int iteration, bool isBestResponseIteration, bool oversampling, int numGamesPerNonChancePlayer)
        {
            int numDecisions = GameDefinition.DecisionsExecutionOrder.Count();
            int[] numObservationsNeeded = new int[numDecisions];
            for (byte p = 0; p < NumNonChancePlayers; p++)
            {
                int firstDecisionIndexForPlayer = GameDefinition.DecisionsExecutionOrder.Select((item, index) => (item, index)).First(x => x.item.PlayerIndex == p).index;
                numObservationsNeeded[firstDecisionIndexForPlayer] = numGamesPerNonChancePlayer;
            }
            return await DeepCFR_GetGamesToComplete(iteration, isBestResponseIteration, oversampling, numObservationsNeeded, numGamesPerNonChancePlayer);
        }

        private async Task<List<(Decision currentDecision, int decisionIndex, byte currentPlayer, DeepCFRDirectGamePlayer gamePlayer, DeepCFRObservationNum observationNum, int numObservations)>> DeepCFR_GetGamesToComplete(int iteration, bool isBestResponseIteration, bool oversampling, int[] numObservationsNeeded, int maxDirectGamePlayersNeeded)
        {
            GameProgressTrees = new GameProgressTree[NumNonChancePlayers];
            double offPolicyProbabilityForProbe = isBestResponseIteration ? 0 : EvolutionSettings.DeepCFR_Epsilon_OffPolicyProbabilityForProbe;
            if (offPolicyProbabilityForProbe == 0)
            {
                GameProgressTrees[0] = await DeepCFR_BuildGameProgressTree(maxDirectGamePlayersNeeded, iteration * numObservationsNeeded.Max(), oversampling, 0, null);
                for (int p = 1; p < NumNonChancePlayers; p++)
                    GameProgressTrees[p] = GameProgressTrees[0];
            }
            else
            {
                for (byte p = 0; p < NumNonChancePlayers; p++)
                    GameProgressTrees[p] = await DeepCFR_BuildGameProgressTree(maxDirectGamePlayersNeeded, iteration * numObservationsNeeded.Max(), oversampling, offPolicyProbabilityForProbe, p);
            }
            var directGamePlayersWithCountsForDecisions = GameProgressTree.GetDirectGamePlayersForEachDecision(GameProgressTrees, offPolicyProbabilityForProbe, numObservationsNeeded, oversampling);
            // Identify the games to complete (we complete them afterward to allow parallelization)
            List<(Decision currentDecision, int decisionIndex, byte currentPlayer, DeepCFRDirectGamePlayer gamePlayer, DeepCFRObservationNum observationNum, int numObservations)> gamesToComplete = new List<(Decision currentDecision, int decisionIndex, byte currentPlayer, DeepCFRDirectGamePlayer gamePlayer, DeepCFRObservationNum observationNum, int numObservations)>();
            for (int decisionIndex = 0; decisionIndex < directGamePlayersWithCountsForDecisions.Length; decisionIndex++)
            {
                Decision currentDecision = GameDefinition.DecisionsExecutionOrder[decisionIndex];
                byte currentPlayer = currentDecision.PlayerIndex;
                var directGamePlayersWithCountsForDecision = directGamePlayersWithCountsForDecisions[decisionIndex];
                if (directGamePlayersWithCountsForDecision == null)
                    continue;
                for (int i = 0; i < directGamePlayersWithCountsForDecision.Length; i++)
                {
                    var directGamePlayerWithCount = directGamePlayersWithCountsForDecision[i];
                    DeepCFRDirectGamePlayer gamePlayer = (DeepCFRDirectGamePlayer)directGamePlayerWithCount.gamePlayer;
                    DeepCFRObservationNum observationNum = new DeepCFRObservationNum(iteration, decisionIndex * 5_000_000 + i);
                    gamesToComplete.Add((currentDecision, decisionIndex, currentPlayer, gamePlayer, observationNum, directGamePlayerWithCount.numObservations));
                }
            }

            return gamesToComplete;
        }

        private void DeepCFR_CompleteGames_FromGameProgressTree_AddPendingObservations(List<(Decision currentDecision, int decisionIndex, byte currentPlayer, DeepCFRDirectGamePlayer gamePlayer, DeepCFRObservationNum observationNum, int numObservations)> gamesToComplete)
        {
            List<(Decision currentDecision, byte decisionIndex, DeepCFRObservation observation)> observations = DeepCFR_CompleteGames_FromGameProgressTree_GetObservations(gamesToComplete);
            foreach (var observationToAdd in observations)
                MultiModel.AddPendingObservation(observationToAdd.currentDecision, observationToAdd.decisionIndex, observationToAdd.observation);
        }

        private List<(Decision currentDecision, byte decisionIndex, DeepCFRObservation observation)> DeepCFR_CompleteGames_FromGameProgressTree_GetObservations(List<(Decision currentDecision, int decisionIndex, byte currentPlayer, DeepCFRDirectGamePlayer gamePlayer, DeepCFRObservationNum observationNum, int numObservations)> gamesToComplete)
        {
            int numGamesToComplete = gamesToComplete.Count();
            if (numGamesToComplete == 0)
                return new List<(Decision currentDecision, byte decisionIndex, DeepCFRObservation observation)>();
            int numGamesToCompleteOnSingleThread = GetNumToDoPerThread(numGamesToComplete);
            int numThreads = numGamesToComplete / numGamesToCompleteOnSingleThread;
            int numGamesToCompleteLastThread = numGamesToComplete - (numThreads - 1) * numGamesToCompleteOnSingleThread;
            List<(Decision currentDecision, byte decisionIndex, DeepCFRObservation observation)>[] observationsByThread = new List<(Decision currentDecision, byte decisionIndex, DeepCFRObservation observation)>[numThreads]; // we don't actually add observations to model until we have completed parallel loop
            DeepCFRProbabilitiesCache probabilitiesCache = new DeepCFRProbabilitiesCache();
            Parallelizer.Go(EvolutionSettings.ParallelOptimization, 0, numThreads, o =>
            {
                var regressionMachines = GetRegressionMachinesForLocalUse(); // note that everything within this block will be on same thread
                DeepCFRPlaybackHelper playbackHelper = new DeepCFRPlaybackHelper(MultiModel, regressionMachines, probabilitiesCache);
                int numGamesToCompleteThisThread = o == numThreads - 1 ? numGamesToCompleteLastThread : numGamesToCompleteOnSingleThread;
                int initialObservation = o * numGamesToCompleteOnSingleThread;
                List<(Decision currentDecision, byte decisionIndex, DeepCFRObservation observation)> observationsToAddForThread = new List<(Decision currentDecision, byte decisionIndex, DeepCFRObservation observation)>();
                for (int i = 0; i < numGamesToCompleteThisThread; i++)
                {
                    var gameToComplete = gamesToComplete[initialObservation + i];
                    gameToComplete.gamePlayer.PlaybackHelper = playbackHelper;
                    var results = DeepCFR_CompleteGame_FromGameProgressTree(gameToComplete.currentDecision, gameToComplete.decisionIndex, gameToComplete.currentPlayer, gameToComplete.gamePlayer, gameToComplete.observationNum, gameToComplete.numObservations);
                    if (EvolutionSettings.DeepCFR_UseWeightedData)
                    {
                        foreach (var result in results)
                            result.observation.Weight = gameToComplete.numObservations;
                        observationsToAddForThread.AddRange(results);
                    }
                    else
                    {
                        // instead of using weighted data, we can just add multiple copies of the same data, 
                        // or we can just add a single datum if that's disabled. 
                        for (int j = 0; j < (EvolutionSettings.DeepCFR_SeparateObservationsForIdenticalGameProgressTreeItems ? gameToComplete.numObservations : 1); j++)
                            observationsToAddForThread.AddRange(results);
                    }
                }
                observationsByThread[o] = observationsToAddForThread;
                ReturnRegressionMachines(regressionMachines);
            });
            List<(Decision currentDecision, byte decisionIndex, DeepCFRObservation observation)> observations = new List<(Decision currentDecision, byte decisionIndex, DeepCFRObservation observation)>();
            for (int i = 0; i < numThreads; i++)
                if (observationsByThread[i] != null)
                    foreach (var observationToAdd in observationsByThread[i])
                        observations.Add(observationToAdd);
            return observations;
        }

        private List<(Decision currentDecision, byte decisionIndex, DeepCFRObservation observation)> DeepCFR_CompleteGame_FromGameProgressTree(Decision currentDecision, int decisionIndex, byte currentPlayer, DeepCFRDirectGamePlayer gamePlayer, DeepCFRObservationNum observationNum, int numObservations)
        {
            DeepCFR_ProbeForUtilitiesAndRegrets(gamePlayer, observationNum, numObservations, false, out double expectedValue, out double[] utilities, out double[] regrets, out List<(byte decisionIndex, byte information)> informationSet);

            List<(Decision currentDecision, byte decisionIndex, DeepCFRObservation observation)> observationsToAdd = new List<(Decision currentDecision, byte decisionIndex, DeepCFRObservation observation)>();
            if (utilities == null || utilities.Length == 0)
                return observationsToAdd;
            for (int j = 0; j < utilities.Length; j++)
            {
                DeepCFRObservation observation = new DeepCFRObservation()
                {
                    IndependentVariables = new DeepCFRIndependentVariables(currentPlayer, (byte)decisionIndex, informationSet, (byte)(j + 1), null),
                    SampledRegret = regrets[j],
                    Weight = 1.0
                };
                observationsToAdd.Add((currentDecision, (byte)decisionIndex, observation));
            }

            return observationsToAdd;
        }

        private void DeepCFR_ProbeForUtilitiesAndRegrets(DeepCFRDirectGamePlayer gamePlayer, DeepCFRObservationNum observationNum, int numObservations, bool calculatingExploitabilityProxy, out double expectedValue, out double[] utilities, out double[] regrets, out List<(byte decisionIndex, byte information)> informationSet)
        {
            if (calculatingExploitabilityProxy)
                utilities = DeepCFR_Probe_GetUtilitiesEachAction(gamePlayer, observationNum, EvolutionSettings.DeepCFR_NumProbesPerGameProgressTreeObservation_Exploitability * (EvolutionSettings.DeepCFR_MultiplyProbesForEachIdenticalIteration_Exploitability ? numObservations : 1));
            else
                utilities = DeepCFR_Probe_GetUtilitiesEachAction(gamePlayer, observationNum, EvolutionSettings.DeepCFR_NumProbesPerGameProgressTreeObservation * (EvolutionSettings.DeepCFR_MultiplyProbesForEachIdenticalIteration ? numObservations : 1));
            double[] actionProbabilities = gamePlayer.GetActionProbabilities();
            expectedValue = 0;
            for (int j = 0; j < utilities.Length; j++)
                expectedValue += actionProbabilities[j] * utilities[j];
            regrets = new double[utilities.Length];
            for (int j = 0; j < utilities.Length; j++)
                regrets[j] = utilities[j] - expectedValue;
            informationSet = gamePlayer.GetInformationSet();
        }

        private double DeepCFR_GetExploitabilityAtDecision(DeepCFRDirectGamePlayer gamePlayer, DeepCFRObservationNum observationNum, int numObservations)
        {
            DeepCFR_ProbeForUtilitiesAndRegrets(gamePlayer, observationNum, numObservations, true, out double expectedValue, out double[] utilities, out double[] regrets, out List<(byte decisionIndex, byte information)> informationSet);
            double maxUtility = utilities.Max();
            return maxUtility - expectedValue;
        }

        #endregion

        #region Cached regression machines

        public const double NumStandardDeviationsForPrincipalComponentStrategy = 0.0001;
        public const double InverseNumStandardDeviationsForPrincipalComponentStrategy = 1.0 / NumStandardDeviationsForPrincipalComponentStrategy;
        DeepCFRCompoundRegressionMachinesContainer CompoundRegressionMachinesContainer;

        private Dictionary<byte, IRegressionMachine> GetRegressionMachinesForLocalUse()
        {
            if (CompoundRegressionMachinesContainer == null)
                return MultiModel.GetRegressionMachinesForLocalUse();
            else
                return CompoundRegressionMachinesContainer.GetRegressionMachinesForLocalUse();
        }

        private void ReturnRegressionMachines(Dictionary<byte, IRegressionMachine> regressionMachines)
        {
            if (CompoundRegressionMachinesContainer == null)
                MultiModel.ReturnRegressionMachines(regressionMachines);
            else
                CompoundRegressionMachinesContainer.ReturnRegressionMachines(regressionMachines);
        }

        #endregion

        #region Utilities calculation

        public GameProgress DeepCFR_GetGameProgressByPlaying(DeepCFRPlaybackHelper playbackHelper, DeepCFRObservationNum observationNum) => DeepCFRTraversal(playbackHelper, observationNum, DeepCFRTraversalMode.PlaybackSinglePath, null).completedProgress;

        public async Task<double[]> DeepCFR_UtilitiesAverage(int totalNumberObservations, int observationOffset = 0, bool useGameProgressTree = true, bool reportTime = true)
        {
            if (reportTime)
                TabbedText.Write($"Calculating utilities from {totalNumberObservations}");
            Stopwatch s = new Stopwatch();
            s.Start();
            StatCollectorArray stats = new StatCollectorArray();
            if (useGameProgressTree)
                await DeepCFR_UtilitiesAverage_WithTree(totalNumberObservations, observationOffset, stats);
            else
                await DeepCFR_UtilitiesAverage_IndependentPlays(totalNumberObservations, observationOffset, stats);
            if (reportTime)
                TabbedText.WriteLine($" time {s.ElapsedMilliseconds} ms");
            double[] averageUtilities = stats.Average().ToArray();
            return averageUtilities;
        }

        public async Task DeepCFR_UtilitiesAverage_WithTree(int totalNumberObservations, int randSeed, StatCollectorArray stats)
        {
            using (GameProgressTree gameProgressTree = await DeepCFR_BuildGameProgressTree(EvolutionSettings.DeepCFR_ApproximateBestResponse_TraversalsForUtilityCalculation, randSeed, false))
            {
                foreach (GameProgress progress in gameProgressTree)
                    stats.Add(progress.GetNonChancePlayerUtilities());
            }
        }

        public Task DeepCFR_UtilitiesAverage_IndependentPlays(int totalNumberObservations, int observationOffset, StatCollectorArray stats)
        {
            int numObservationsToDoPerThread = GetNumToDoPerThread(totalNumberObservations);
            int numThreads = totalNumberObservations / numObservationsToDoPerThread;
            int numObservationsToDoTogetherLastThread = totalNumberObservations - (numThreads - 1) * numObservationsToDoPerThread;
            DeepCFRProbabilitiesCache probabilitiesCache = new DeepCFRProbabilitiesCache();
            Parallelizer.Go(EvolutionSettings.ParallelOptimization, 0, numThreads, o =>
            {
                var regressionMachines = GetRegressionMachinesForLocalUse();
                DeepCFRPlaybackHelper playbackHelper = new DeepCFRPlaybackHelper(MultiModel.DeepCopyForPlaybackOnly(), regressionMachines, probabilitiesCache);
                int numToPlaybackThisThread = o == numThreads - 1 ? numObservationsToDoTogetherLastThread : numObservationsToDoPerThread;
                var utilities = DeepCFR_UtilitiesFromMultiplePlaybacks(o + observationOffset, numToPlaybackThisThread, playbackHelper).ToArray();
                stats.Add(utilities, numToPlaybackThisThread);
                ReturnRegressionMachines(regressionMachines);
            });
            return Task.CompletedTask;
        }

        private int GetNumToDoPerThread(int totalNumberObservations)
        {
            return EvolutionSettings.ParallelOptimization ? 1 + totalNumberObservations / (Environment.ProcessorCount * 5) : totalNumberObservations;
        }

        public double[] DeepCFR_UtilitiesFromMultiplePlaybacks(int observation, int numToPlaybackTogether, DeepCFRPlaybackHelper playbackHelper)
        {
            int initialObservation = observation * numToPlaybackTogether;
            double[][] results = Enumerable.Range(initialObservation, initialObservation + numToPlaybackTogether).Select(x => DeepCFR_UtilitiesFromSinglePlayback(playbackHelper, new DeepCFRObservationNum(x, 10_000_000))).ToArray();
            StatCollectorArray s = new StatCollectorArray();
            foreach (double[] result in results)
                s.Add(result);
            double[] averageResults = s.Average().ToArray();
            return averageResults;
        }

        public double[] DeepCFR_UtilitiesFromSinglePlayback(DeepCFRPlaybackHelper playbackHelper, DeepCFRObservationNum observationNum)
        {
            return DeepCFRTraversal(playbackHelper, observationNum, DeepCFRTraversalMode.PlaybackSinglePath).utilities;
        }

        public async Task<double[]> DeepCFR_ExploitabilityProxy(int iteration, bool isBestResponseIteration)
        {
            Stopwatch s = new Stopwatch();
            s.Start();
            List<(Decision currentDecision, int decisionIndex, byte currentPlayer, DeepCFRDirectGamePlayer gamePlayer, DeepCFRObservationNum observationNum, int numObservations)> gamesToComplete = await DeepCFR_GetGamesToComplete(iteration, true /* regardless of whether it really is a best response iteration */, false, EvolutionSettings.DeepCFR_GamesForExploitabilityProxy);
            int lowestDecision = gamesToComplete.Min(x => x.decisionIndex);
            double numGamesAtLowestDecision = (double) gamesToComplete.Where(x => x.decisionIndex == lowestDecision).Sum(x => x.numObservations);

            double[] averageSumExploitabilities = new double[NumNonChancePlayers];

            int numGamesToComplete = gamesToComplete.Count();
            int numGamesToCompleteOnSingleThread = GetNumToDoPerThread(numGamesToComplete);
            int numThreads = numGamesToComplete / numGamesToCompleteOnSingleThread;
            int numGamesToCompleteLastThread = numGamesToComplete - (numThreads - 1) * numGamesToCompleteOnSingleThread;

            DeepCFRProbabilitiesCache probabilitiesCache = new DeepCFRProbabilitiesCache();
            Parallelizer.Go(EvolutionSettings.ParallelOptimization, 0, numThreads, o =>
            {
                var regressionMachines = GetRegressionMachinesForLocalUse();
                DeepCFRPlaybackHelper playbackHelper = new DeepCFRPlaybackHelper(MultiModel.DeepCopyForPlaybackOnly(), regressionMachines, probabilitiesCache);
                double[] averageSumExploitabilitiesContribution = new double[NumNonChancePlayers];
                int numGamesToCompleteThisThread = o == numThreads - 1 ? numGamesToCompleteLastThread : numGamesToCompleteOnSingleThread;
                int initialObservation = (int)(o * numGamesToCompleteOnSingleThread);
                for (int i = 0; i < numGamesToCompleteThisThread; i++)
                {
                    int observationIndex = initialObservation + i;
                    (Decision currentDecision, int decisionIndex, byte currentPlayer, DeepCFRDirectGamePlayer gamePlayer, DeepCFRObservationNum observationNum, int numObservations) gameToComplete = gamesToComplete[observationIndex];
                    double exploitabilityForDecision = DeepCFR_GetExploitabilityAtDecision(gameToComplete.gamePlayer, gameToComplete.observationNum, gameToComplete.numObservations);
                    averageSumExploitabilitiesContribution[gameToComplete.currentPlayer] += ((double)gameToComplete.numObservations) * exploitabilityForDecision / numGamesAtLowestDecision;
                }
                for (int i = 0; i < averageSumExploitabilitiesContribution.Length; i++)
                    Interlocking.Add(ref averageSumExploitabilities[i], averageSumExploitabilitiesContribution[i]);
                ReturnRegressionMachines(regressionMachines);
            });
            TabbedText.WriteLine($"Exploitability proxy: {averageSumExploitabilities.ToSignificantFigures(4)} time {s.ElapsedMilliseconds} ms");
            return averageSumExploitabilities;
        }

        #endregion

        #region Reporting

        public override async Task<ReportCollection> GenerateReports(int iteration, Func<string> prefaceFn)
        {
            ReportCollection reportCollection = null;
            bool doReports = EvolutionSettings.ReportEveryNIterations != null && (iteration % EvolutionSettings.ReportEveryNIterations == 0 || Status.BestResponseTargetMet(EvolutionSettings.BestResponseTarget));
            if (doReports)
            {
                reportCollection = await DeepCFR_GenerateReports(prefaceFn);
            }

            return reportCollection ?? new ReportCollection();
        }

        private async Task<ReportCollection> DeepCFR_GenerateReports(Func<string> prefaceFn)
        {
            TabbedText.HideConsoleProgressString();
            TabbedText.WriteLine("");
            TabbedText.WriteLine(prefaceFn());

            ReportCollection reportCollection = new ReportCollection();
            
            Br.eak.Add("Report");
            bool useGameProgressTree = true;
            if (useGameProgressTree)
            {
                using (var gameProgressTree = await DeepCFR_BuildGameProgressTree(EvolutionSettings.NumRandomIterationsForSummaryTable, 0, false))
                {
                    var gameProgresses = gameProgressTree.AsEnumerable();
                    var gameProgressesArray = gameProgresses.ToArray();
                    reportCollection = GenerateReportsFromGameProgressEnumeration(gameProgressesArray);
                }
            }
            else
                reportCollection = await GenerateReportsByPlaying(true);
            //CalculateUtilitiesOverall();
            //TabbedText.WriteLine($"Utilities: {String.Join(",", Status.UtilitiesOverall.Select(x => x.ToSignificantFigures(4)))}");
            Br.eak.Remove("Report");
            TabbedText.ShowConsoleProgressString();
            return reportCollection;
        }

        public async override Task PlayMultipleIterationsForReporting(
            GamePlayer player,
            int numIterations,
            Func<Decision, GameProgress, byte> actionOverride,
            BufferBlock<Tuple<GameProgress, double>> bufferBlock) => await PlayMultipleIterationsAndProcess(numIterations, actionOverride, bufferBlock, Strategies, EvolutionSettings.ParallelOptimization, DeepCFRReportingPlayHelper);

        public GameProgress DeepCFRReportingPlayHelper(int iteration, List<Strategy> strategies, bool saveCompletedGameProgressInfos, IterationID[] iterationIDArray, List<GameProgress> preplayedGameProgressInfos, Func<Decision, GameProgress, byte> actionOverride)
        {
            DeepCFRPlaybackHelper playbackHelper = new DeepCFRPlaybackHelper(MultiModel, null, null); // this will be slow
            GameProgress progress = DeepCFR_GetGameProgressByPlaying(playbackHelper, new DeepCFRObservationNum(iteration, 1_000_000));
            progress.IterationID = new IterationID(iteration);

            return progress;
        }

        private void ReportIteration(int iteration, bool isBestResponseIteration)
        {
            if (isBestResponseIteration)
            {
                if (EvolutionSettings.DeepCFR_ApproximateBestResponse_BackwardInduction)
                    TabbedText.Write($"Best response iteration {ApproximateBestResponse_CurrentIterationsIndex} of {ApproximateBestResponse_CurrentIterationsTotal} for player {ApproximateBestResponse_CurrentPlayer}");
                else
                    TabbedText.Write($"Best response iteration {iteration} of {EvolutionSettings.DeepCFR_ApproximateBestResponseIterations} ");
            }
            else
                TabbedText.Write($"Iteration {iteration} of {EvolutionSettings.TotalIterations} ");
        }

        #endregion

        #region Principal component analysis

        private async Task Genotyping(int iteration)
        {
            if (EvolutionSettings.DeepCFR_PCA_PerformPrincipalComponentAnalysis)
            {
                if (iteration == 1)
                {
                    GenotypingBaselineMultiModel = MultiModel.DeepCopyObservationsOnly(null);
                    SavedGenotypes = new List<float[][]>();
                }
                if (iteration >= EvolutionSettings.DeepCFR_PCA_FirstIterationToSaveGenotypes && iteration % EvolutionSettings.DeepCFR_PCA_SaveGenotypeEveryNIterationsAfterFirst == 0)
                {
                    float[][] genotype = MultiModel.GetGenotypes(GenotypingBaselineMultiModel, NumNonChancePlayers);
                    SavedGenotypes.Add(genotype);
                }
                if (iteration == EvolutionSettings.TotalIterations)
                {
                    Stopwatch s = new Stopwatch();
                    s.Start();
                    PCAResultsForEachPlayer = new PCAResultsForPlayer[NumNonChancePlayers];
                    for (byte p = 0; p < NumNonChancePlayers; p++)
                        PCAResultsForEachPlayer[p] = PerformPrincipalComponentAnalysis(p);
                    //await LoadReducedFormStrategies();
                    await SetCompoundStrategyUsingPrincipalComponents();
                    TabbedText.WriteLine($"Total time performing PCA and creating compound strategy {s.ElapsedMilliseconds} ms");
                    for (int DEBUG = 0; DEBUG < 15; DEBUG++)
                    {
                        ((MyGameDefinition)GameDefinition).Options.CostsMultiplier = 0.05 * DEBUG;
                        TabbedText.WriteLine($"Trying CostsMultiplier {0.05 * DEBUG}");
                        // DEBUG -- keep the following line
                        await BuildModelPredictingUtilitiesBasedOnPrincipalComponents();
                        await DeepCFR_GenerateReports(() => $"CM{0.05 * DEBUG}"); // DEBUG
                    }
                }
            }
        }

        private class ModelPredictingUtilitiesDatum
        {
            public List<float>[] PrincipalComponentsWeightForEachPlayer;
            public float[] UtilitiesForEachPlayer;
            int NumPrincipalComponentsPerPlayer;
            int NumPlayers;

            public ModelPredictingUtilitiesDatum(List<double>[] principalComponentsWeightForEachPlayer, double[] utilitiesForEachPlayer)
            {
                PrincipalComponentsWeightForEachPlayer = principalComponentsWeightForEachPlayer.Select(x => x.Select(y => (float)y).ToList()).ToArray();
                UtilitiesForEachPlayer = utilitiesForEachPlayer?.Select(x => (float) x).ToArray();
                NumPrincipalComponentsPerPlayer = principalComponentsWeightForEachPlayer.First().Count();
                NumPlayers = PrincipalComponentsWeightForEachPlayer.Length;
            }


            public (float[] X, float Y, float W) Convert(byte playerIndex = 0)
            {
                float[] X = new float[NumPlayers * NumPrincipalComponentsPerPlayer];
                int index = 0;
                for (int p = 0; p < NumPlayers; p++)
                    for (int pc = 0; pc < NumPrincipalComponentsPerPlayer; pc++)
                    {
                        X[index++] = (float) PrincipalComponentsWeightForEachPlayer[p][pc];
                    }
                float Y = UtilitiesForEachPlayer == null ? 0 : UtilitiesForEachPlayer[playerIndex];
                return (X, Y, 1.0F);
            }
        }

        private async Task BuildModelPredictingUtilitiesBasedOnPrincipalComponents()
        {
            if (EvolutionSettings.DeepCFR_PCA_BuildModelToPredictUtilitiesBasedOnPrincipalComponents)
            {
                int numUtilitiesToCalculateToBuildModel = EvolutionSettings.DeepCFR_PCA_NumUtilitiesToCalculateToBuildModel;
                int numGamesToPlayToEstimateEachUtilityWhileBuildingModel = EvolutionSettings.DeepCFR_PCA_NumGamesToPlayToEstimateEachUtilityWhileBuildingModel;
                Stopwatch outerStopwatch = new Stopwatch();
                outerStopwatch.Start();
                StatCollector timeStats = new StatCollector();
                List<ModelPredictingUtilitiesDatum> data = new List<ModelPredictingUtilitiesDatum>();
                for (int i = 0; i < numUtilitiesToCalculateToBuildModel; i++)
                {
                    Stopwatch innerStopwatch = new Stopwatch();
                    innerStopwatch.Start();
                    List<double>[] principalComponentWeightsForEachPlayer = GetRandomPrincipalComponentWeightsForEachPlayer(i * 737, 1.5 /* DEBUG */);
                    string principalComponentWeightsString = String.Join("; ", Enumerable.Range(0, NumNonChancePlayers).Select(x => x.ToString() + ": " + principalComponentWeightsForEachPlayer[x].ToSignificantFigures(3)));
                    CompoundRegressionMachinesContainer.SpecifyWeightOnSupplementalMachines(principalComponentWeightsForEachPlayer, InverseNumStandardDeviationsForPrincipalComponentStrategy);
                    double[] compoundUtilities = await DeepCFR_UtilitiesAverage(numGamesToPlayToEstimateEachUtilityWhileBuildingModel, i * numUtilitiesToCalculateToBuildModel, true);
                    data.Add(new ModelPredictingUtilitiesDatum(principalComponentWeightsForEachPlayer, compoundUtilities));
                    innerStopwatch.Stop();
                    timeStats.Add(innerStopwatch.ElapsedMilliseconds);
                    TabbedText.WriteLine($"Data input {i}: {principalComponentWeightsString} utilities: {compoundUtilities.ToSignificantFigures(4)}");
                }
                TabbedText.WriteLine($"Average ms per each of {numUtilitiesToCalculateToBuildModel} utilities to calculate: {timeStats.Average().ToSignificantFigures(3)} (total time: {outerStopwatch.ElapsedMilliseconds})");
                outerStopwatch.Reset();
                outerStopwatch.Start();
                ModelsToPredictUtilitiesFromPrincipalComponents = new RegressionController[NumNonChancePlayers];
                for (byte p = 0; p < NumNonChancePlayers; p++)
                {
                    ModelsToPredictUtilitiesFromPrincipalComponents[p] = new RegressionController(EvolutionSettings.RegressionFactory());
                    var dataConverted = data.Select(x => x.Convert(p)).ToArray();
                    await ModelsToPredictUtilitiesFromPrincipalComponents[p].Regress(dataConverted);
                }
                outerStopwatch.Stop();
                TabbedText.WriteLine($"Utilities model build in {outerStopwatch.ElapsedMilliseconds} ms");
                bool assessModels = false;
                if (assessModels)
                    await AssessModelsToPredictUtilitiesFromPrincipalComponents();
                UsePCAToEvaluateEquilibria();
            }
        }

        public void UsePCAToEvaluateEquilibria()
        {
            if (NumNonChancePlayers != 2)
                throw new NotSupportedException();
            TabbedText.WriteLine($"Search for equilibria with PCA");
            int numStrategyChoicesPerPlayer = EvolutionSettings.DeepCFR_PCA_NumStrategyChoicesPerPlayer;
            double[,] player0Utilities, player1Utilities;
            List<double>[] principalComponentsWeightsForPlayer0, principalComponentsWeightsForPlayer1;
            GetEstimatedUtilitiesForStrategyChoices(numStrategyChoicesPerPlayer, out player0Utilities, out player1Utilities, out principalComponentsWeightsForPlayer0, out principalComponentsWeightsForPlayer1);

            TabbedText.WriteLine($"Nash equilibria");
            List<List<double>[]> nashEquilibria = PureStrategiesFinder.ComputeNashEquilibria(player0Utilities, player1Utilities, false)
                .Select(x => new List<double>[2] { principalComponentsWeightsForPlayer0[x.player0Strategy], principalComponentsWeightsForPlayer1[x.player1Strategy] })
                .ToList();
            if (!nashEquilibria.Any())
            {
                TabbedText.WriteLine($"No Nash equilibrium found. Finding best approximate Nash equilibrium.");
                var result = PureStrategiesFinder.GetApproximateNashEquilibrium(player0Utilities, player1Utilities);
                nashEquilibria.Add(new List<double>[2] { principalComponentsWeightsForPlayer0[result.player0Strategy], principalComponentsWeightsForPlayer1[result.player1Strategy] });
            }
            if (nashEquilibria.FirstOrDefault() is List<double>[] equilibrium)
                CompoundRegressionMachinesContainer.SpecifyWeightOnSupplementalMachines(equilibrium, InverseNumStandardDeviationsForPrincipalComponentStrategy);
            ReportEquilibria(nashEquilibria);

            TabbedText.WriteLine($"Correlated equilibria");
            var correlatedEquilibria = PureStrategiesFinder.GetCorrelatedEquilibrium_OrderingByApproxNashValue(player0Utilities, player1Utilities)
                .Select(x => new List<double>[2] { principalComponentsWeightsForPlayer0[x.player0Strategy], principalComponentsWeightsForPlayer1[x.player1Strategy] })
                .ToList();
            ReportEquilibria(correlatedEquilibria);
            ConsistentRandomSequenceProducer randomSequenceProducer = new ConsistentRandomSequenceProducer(6_000_234);
            for (int i = 0; i < 1; i++)
            {
                TabbedText.WriteLine($"correlated eq random start {i}");
                correlatedEquilibria = PureStrategiesFinder.GetCorrelatedEquilibrium_OrderingByFarthestDistanceFromAdmittees_StartingWithRandomStrategy(player0Utilities, player1Utilities, randomSequenceProducer)
                    .Select(x => new List<double>[2] { principalComponentsWeightsForPlayer0[x.player0Strategy], principalComponentsWeightsForPlayer1[x.player1Strategy] })
                    .ToList();
                ReportEquilibria(correlatedEquilibria);

            }
        }

        private void ReportEquilibria(List<List<double>[]> equilibria)
        {
            foreach (var equilibrium in equilibria)
            {
                var datum = new ModelPredictingUtilitiesDatum(equilibrium, null);
                var model = ModelsToPredictUtilitiesFromPrincipalComponents[0];
                double player0Utility = model.GetResult(datum.Convert().X, null, null);
                model = ModelsToPredictUtilitiesFromPrincipalComponents[1];
                double player1Utility = model.GetResult(datum.Convert().X, null, null);
                string principalComponentWeightsString = string.Join("; ", Enumerable.Range(0, NumNonChancePlayers).Select(x => x.ToString() + ": " + equilibrium[x].ToSignificantFigures(3)));
                TabbedText.WriteLine($"eq for principal components {principalComponentWeightsString}; utility {player0Utility.ToSignificantFigures(5)}, {player1Utility.ToSignificantFigures(5)}");
            }
        }

        private void GetEstimatedUtilitiesForStrategyChoices(int numStrategyChoicesPerPlayer, out double[,] player0Utilities, out double[,] player1Utilities, out List<double>[] principalComponentsWeightsForPlayer0, out List<double>[] principalComponentsWeightsForPlayer1)
        {
            player0Utilities = new double[numStrategyChoicesPerPlayer, numStrategyChoicesPerPlayer];
            player1Utilities = new double[numStrategyChoicesPerPlayer, numStrategyChoicesPerPlayer];
            // Determine principal component weights for each strategy choice, making sure they are slightly spaced out. There is no deterministic algorithm for the "sphere packing problem" in n-dimensional space, but the SpacedOutPoints class should work in an approximate way.
            principalComponentsWeightsForPlayer0 = new List<double>[numStrategyChoicesPerPlayer];
            principalComponentsWeightsForPlayer1 = new List<double>[numStrategyChoicesPerPlayer];
            principalComponentsWeightsForPlayer0 = GetRandomPrincipalComponentWeights(0, new SpacedOutPoints(numStrategyChoicesPerPlayer, numStrategyChoicesPerPlayer * 10, Enumerable.Range(0, EvolutionSettings.DeepCFR_PCA_NumPrincipalComponents).Select(x => 1.0).ToArray(), new ConsistentRandomSequenceProducer(5_000_001)).CalculatePoints(), 1.0);
            principalComponentsWeightsForPlayer1 = GetRandomPrincipalComponentWeights(1, new SpacedOutPoints(numStrategyChoicesPerPlayer, numStrategyChoicesPerPlayer * 10, Enumerable.Range(0, EvolutionSettings.DeepCFR_PCA_NumPrincipalComponents).Select(x => 1.0).ToArray(), new ConsistentRandomSequenceProducer(5_000_002)).CalculatePoints(), 1.0);
            for (int i = 0; i < numStrategyChoicesPerPlayer; i++)
            {
                for (int j = 0; j < numStrategyChoicesPerPlayer; j++)
                {
                    List<double>[] principalComponentWeightsForEachPlayer = new List<double>[2] { principalComponentsWeightsForPlayer0[i], principalComponentsWeightsForPlayer1[j] };
                    var datum = new ModelPredictingUtilitiesDatum(principalComponentWeightsForEachPlayer, null);
                    var model = ModelsToPredictUtilitiesFromPrincipalComponents[0];
                    player0Utilities[i, j] = model.GetResult(datum.Convert().X, null, null);
                    model = ModelsToPredictUtilitiesFromPrincipalComponents[1];
                    player1Utilities[i, j] = model.GetResult(datum.Convert().X, null, null);
                    //CompoundRegressionMachinesContainer.SpecifyWeightOnSupplementalMachines(principalComponentWeightsForEachPlayer, InverseNumStandardDeviationsForPrincipalComponentStrategy);
                }
            }
        }

        private async Task AssessModelsToPredictUtilitiesFromPrincipalComponents()
        {
            StatCollector s = new StatCollector();
            const int numToCompare = 100;
            double[][] actualArray = new double[NumNonChancePlayers][], predictedArray = new double[NumNonChancePlayers][];
            for (int p = 0; p < NumNonChancePlayers; p++)
            {
                actualArray[p] = new double[numToCompare];
                predictedArray[p] = new double[numToCompare];
            }
            await Parallelizer.GoAsync(EvolutionSettings.ParallelOptimization, 0, numToCompare, async j =>
            {
                List<double>[] principalComponentWeightsForEachPlayer = GetRandomPrincipalComponentWeightsForEachPlayer(((int)j) * 2011, 1.0);
                string principalComponentWeightsString = string.Join("; ", Enumerable.Range(0, NumNonChancePlayers).Select(x => x.ToString() + ": " + principalComponentWeightsForEachPlayer[x].ToSignificantFigures(3)));
                ModelPredictingUtilitiesDatum datum = new ModelPredictingUtilitiesDatum(principalComponentWeightsForEachPlayer, null);
                CompoundRegressionMachinesContainer.SpecifyWeightOnSupplementalMachines(principalComponentWeightsForEachPlayer, InverseNumStandardDeviationsForPrincipalComponentStrategy);
                double[] actual = await DeepCFR_UtilitiesAverage(1_000_000, reportTime: false);
                double[] predicted = new double[NumNonChancePlayers];
                for (byte p = 0; p < NumNonChancePlayers; p++)
                {
                    var model = ModelsToPredictUtilitiesFromPrincipalComponents[p];
                    predicted[p] = model.GetResult(datum.Convert().X, null, null);
                    predictedArray[p][j] = predicted[p];
                    actualArray[p][j] = actual[p];
                }
                double[] absoluteDifference = actual.Zip(predicted, (first, second) => Math.Abs(first - second)).ToArray();
                double avgAbsoluteDifference = absoluteDifference.Average();
                s.Add(avgAbsoluteDifference);
                TabbedText.WriteLine($"Principal components {j}: {principalComponentWeightsString} ==> Prediction {predicted.ToSignificantFigures(6)} actual {actual.ToSignificantFigures(6)} avg_abs_diff {avgAbsoluteDifference.ToSignificantFigures(3)} ");
            });
            TabbedText.WriteLine($"Overall average absolute difference: " + s.Average().ToSignificantFigures(3));
            for (int p = 0; p < NumNonChancePlayers; p++)
            {
                double coef = ComputeCoeff(actualArray[p], predictedArray[p]);
                TabbedText.WriteLine($"Correlation coefficient player {p}: {coef}");
            }
        }

        public double ComputeCoeff(double[] values1, double[] values2)
        {
            if (values1.Length != values2.Length)
                throw new ArgumentException("values must be the same length");

            var avg1 = values1.Average();
            var avg2 = values2.Average();

            var sum1 = values1.Zip(values2, (x1, y1) => (x1 - avg1) * (y1 - avg2)).Sum();

            var sumSqr1 = values1.Sum(x => Math.Pow((x - avg1), 2.0));
            var sumSqr2 = values2.Sum(y => Math.Pow((y - avg2), 2.0));

            var result = sum1 / Math.Sqrt(sumSqr1 * sumSqr2);

            return result;
        }

        private async Task AssessCompoundStrategyAccuracy()
        {
            // This method compares utilities with a compound strategy (i.e., where each principal component has a separate strategy for each decision) and a PC-specific strategy (i.e., where there is a separate strategy for each decision and that strategy takes into account all of the principal components). The compound strategy can be created just once (but estimation must occur for every principal component), while the PC-specific strategy must be estimated separately for each set of principal component weights. RESULT: Running this method confirms that the compound strategy is a close approximation for the PC-specific strategy.
            var copyContainer = CompoundRegressionMachinesContainer;
            var copyMultiModel = MultiModel;
            StatCollectorArray compoundStats = new StatCollectorArray(), pcSpecificStats = new StatCollectorArray();
            for (int i = 0; i < 100; i++)
            {
                List<double>[] principalComponentWeightsForEachPlayer = GetRandomPrincipalComponentWeightsForEachPlayer(i * 737, 1.0);
                string principalComponentWeightsString = String.Join(";", Enumerable.Range(0, NumNonChancePlayers).Select(x => x.ToString() + ": " + principalComponentWeightsForEachPlayer[x].ToSignificantFigures(3)));
                CompoundRegressionMachinesContainer.SpecifyWeightOnSupplementalMachines(principalComponentWeightsForEachPlayer, InverseNumStandardDeviationsForPrincipalComponentStrategy);
                double[] compoundUtilities = await DeepCFR_UtilitiesAverage(1_000_000);
                compoundStats.Add(compoundUtilities);
                CompoundRegressionMachinesContainer = null;
                var pcSpecificStrategy = await GetIntegratedStrategyBasedOnPrincipalComponents(principalComponentWeightsForEachPlayer, EvolutionSettings.ParallelOptimization);
                MultiModel = pcSpecificStrategy;
                double[] pcSpecificUtilities = await DeepCFR_UtilitiesAverage(1_000_000);
                pcSpecificStats.Add(pcSpecificUtilities);
                MultiModel = copyMultiModel;
                CompoundRegressionMachinesContainer = copyContainer;
                TabbedText.WriteLine($"principal component weights: {principalComponentWeightsString} compound strategy utilities: {compoundUtilities.ToSignificantFigures(7)} regular strategy utilities: {pcSpecificUtilities.ToSignificantFigures(7)}");
            }
            TabbedText.WriteLine($"Compound stats mean {compoundStats.Average().ToSignificantFigures(3)} stdev {compoundStats.StandardDeviation().ToSignificantFigures(3)} ");
            TabbedText.WriteLine($"PC-specific stats mean {pcSpecificStats.Average().ToSignificantFigures(3)} stdev {pcSpecificStats.StandardDeviation().ToSignificantFigures(3)} ");

        }

        public PCAResultsForPlayer PerformPrincipalComponentAnalysis(byte playerIndex)
        {
            int numElementsInGenotype = SavedGenotypes.First()[playerIndex].Length;
            int numGenotypes = SavedGenotypes.Count();
            double[,] originalGenotypes = new double[numGenotypes, numElementsInGenotype];
            for (int i = 0; i < numGenotypes; i++)
                for (int j = 0; j < numElementsInGenotype; j++)
                    originalGenotypes[i, j] = SavedGenotypes[i][playerIndex][j];
            (double[,] meanCentered, double[] mean, double[] stdev) = originalGenotypes.ZScored();
            alglib.pcatruncatedsubspace(meanCentered, numGenotypes, numElementsInGenotype, EvolutionSettings.DeepCFR_PCA_NumPrincipalComponents, EvolutionSettings.DeepCFR_PCA_Precision, 0, out double[] sigma_squared, out double[,] v_principalComponentLoadings);
            double[] proportionOfAccountedVariance = sigma_squared.Select(x => x / sigma_squared.Sum()).ToArray();
            double[,] u_principalComponentScores = meanCentered.Multiply(v_principalComponentLoadings);
            // Calculate stats on principal component scores. Mean will be zero. Standard deviations will
            // be such that their squares (i.e., variances) will be in proportion with proportionOfAccountedVariance.
            // So, we don't really need this, but the standard deviations are useful.
            StatCollectorArray principalComponentScoresDistribution = new StatCollectorArray();
            foreach (double[] row in u_principalComponentScores.GetRows())
                principalComponentScoresDistribution.Add(row);
            double[] firstDimensionOnly = u_principalComponentScores.GetColumn(0);
            double[,] vTranspose = v_principalComponentLoadings.Transpose();
            double[,] backProjectedMeanCentered = u_principalComponentScores.Multiply(vTranspose);
            double[,] backProjected = backProjectedMeanCentered.ReverseZScored(mean, stdev);
            PCAResultsForPlayer stats = new PCAResultsForPlayer()
            {
                playerIndex = playerIndex,
                meanOfOriginalElements = mean,
                stdevOfOriginalElements = stdev,
                sigma_squared = sigma_squared,
                v_principalComponentLoadings = v_principalComponentLoadings,
                proportionOfAccountedVariance = proportionOfAccountedVariance,
                principalComponentStdevs = principalComponentScoresDistribution.StandardDeviation().ToArray(),
            };
            return stats;
        }

        //public async Task<DeepCFRMultiModel> GenerateModelFromPlayerPrincipalComponents(DeepCFRMultiModel baselineModel, double[][] principalComponentScoresForPlayers, bool parallel)
        //{
        //    DeepCFRMultiModel targetModel = baselineModel.DeepCopyObservationsOnly(null);
        //    int numPlayers = StatsForPlayer.Length;
        //    for (byte playerIndex = 0; playerIndex < numPlayers; playerIndex++)
        //    {
        //        double[] principalComponentScoresForPlayer = principalComponentScoresForPlayers[playerIndex];
        //        ChangeModelBasedOnPlayerPrincipalComponents(targetModel, playerIndex, principalComponentScoresForPlayer);
        //    }
        //    await targetModel.ProcessObservations(false, parallel);
        //    return targetModel;
        //}

        public async Task LoadReducedFormStrategies()
        {
            PCAStrategiesForEachPlayer = new List<DeepCFRMultiModel>[NumNonChancePlayers];
            for (byte p = 0; p < NumNonChancePlayers; p++)
                PCAStrategiesForEachPlayer[p] = await GetSinglePlayerReducedFormStrategies(p);
        }

        private async Task<DeepCFRMultiModel> GetDeepCFRMultiModelWithRandomPrincipalComponentWeights(int randomIndex)
        {
            List<double>[] principalComponentWeightsForEachPlayer = GetRandomPrincipalComponentWeightsForEachPlayer(randomIndex, 1.0);
            var result = await GetIntegratedStrategyBasedOnPrincipalComponents(principalComponentWeightsForEachPlayer, EvolutionSettings.ParallelOptimization);
            return result;
        }

        private List<double>[] GetRandomPrincipalComponentWeightsForEachPlayer(int randomIndex, double multiplier)
        {
            return Enumerable.Range(0, NumNonChancePlayers).Select(x => GetRandomPrincipalComponentWeights((byte)x, new ConsistentRandomSequenceProducer(randomIndex, 1_000_000 * x), multiplier)).ToArray();
        }

        private List<double> GetRandomPrincipalComponentWeights(byte playerIndex, ConsistentRandomSequenceProducer randomizer, double multiplier)
        {
            var stats = PCAResultsForEachPlayer[playerIndex];
            int numPrincipalComponents = EvolutionSettings.DeepCFR_PCA_NumPrincipalComponents;
            var result = Enumerable.Range(0, numPrincipalComponents).Select(principalComponent => multiplier * InvNormal.Calculate(randomizer.GetDoubleAtIndex(principalComponent)) * stats.principalComponentStdevs[principalComponent]).ToList();
            return result;
        }
        private List<double> GetRandomPrincipalComponentWeights(byte playerIndex, double[] randomizerValues, double multiplier)
        {
            var stats = PCAResultsForEachPlayer[playerIndex];
            int numPrincipalComponents = EvolutionSettings.DeepCFR_PCA_NumPrincipalComponents;
            var result = Enumerable.Range(0, numPrincipalComponents).Select(principalComponent => multiplier * InvNormal.Calculate(randomizerValues[principalComponent]) * stats.principalComponentStdevs[principalComponent]).ToList();
            return result;
        }

        private List<double>[] GetRandomPrincipalComponentWeights(byte playerIndex, double[][] randomizerValues, double multiplier)
        {
            int numToGet = randomizerValues.GetLength(0);
            return Enumerable.Range(0, numToGet).Select(x => GetRandomPrincipalComponentWeights(playerIndex, randomizerValues[x], multiplier)).ToArray();
        }

        public async Task<List<DeepCFRMultiModel>> GetSinglePlayerReducedFormStrategies(byte playerIndex)
        {
            var stats = PCAResultsForEachPlayer[playerIndex];
            int[] numVariationsPerPrincipalComponent = EvolutionSettings.DeepCFR_PCA_NumVariationsPerPrincipalComponent;
            int numPrincipalComponents = EvolutionSettings.DeepCFR_PCA_NumPrincipalComponents;
            // 1. Calculate the range of values for each principal component. This depends on the number of
            // variations per component and the standard deviation for that component. We determine number
            // of standard deviations for each variation by drawing from the inverse cumulative normal distribution.
            double[][] valuesForEachPrincipalComponent = new double[numPrincipalComponents][];
            for (int principalComponent = 0; principalComponent < numPrincipalComponents; principalComponent++)
            {
                double stdev = stats.principalComponentStdevs[principalComponent];
                int numVariations = numVariationsPerPrincipalComponent[principalComponent];
                valuesForEachPrincipalComponent[principalComponent] = Enumerable.Range(0, numVariations)
                    .Select(v => EquallySpaced.GetLocationOfEquallySpacedPoint(v, numVariations, false))
                    .Select(p => InvNormal.Calculate(p))
                    .Select(n => n * stdev)
                    .ToArray();
            }
            // 2. Calculate permutations of the value for each principal component, so for example if there are 
            // 3 values of PC0 and 4 values of PC1 (and nothing else), we would have 12 permutations. Each of
            // these 12 permutations would have a different value for each principal component.
            double[][] permutations = PermutationMaker.GetPermutations(numVariationsPerPrincipalComponent.ToList(), resultsZeroBased: true)
                .Select(permutation =>
                    permutation.Select((item, index) => (item, index))
                    .Select(p => valuesForEachPrincipalComponent[p.index][p.item])
                    .ToArray()
                    )
                .ToArray();
            int numPermutations = permutations.GetLength(0);
            // 3. Create a strategy from each set of principal components.
            List<DeepCFRMultiModel> strategiesForPlayer = new List<DeepCFRMultiModel>();
            for (int permutation = 0; permutation < numPermutations; permutation++)
            {
                TabbedText.WriteLine($"Getting reduced form strategy {permutation + 1} of {numPermutations} for player {playerIndex}");
                DeepCFRMultiModel strategy = await GetSinglePlayerStrategyBasedOnPrincipalComponents(playerIndex, permutations[permutation], EvolutionSettings.ParallelOptimization, false);
                strategiesForPlayer.Add(strategy);
            }
            // 4. Return result
            return strategiesForPlayer;
        }

        public async Task SetCompoundStrategyUsingPrincipalComponents()
        {
            var strategies = await GetSeparateStrategiesForPrincipalComponents(EvolutionSettings.ParallelOptimization);
            DeepCFRCompoundRegressionMachinesContainer container = new DeepCFRCompoundRegressionMachinesContainer(strategies, GameDefinition, NumNonChancePlayers);
            CompoundRegressionMachinesContainer = container;
        }

        public async Task<List<DeepCFRMultiModel>> GetSeparateStrategiesForPrincipalComponents(bool parallel)
        {
            List<DeepCFRMultiModel> integratedStrategies = null;
            for (byte p = 0; p < NumNonChancePlayers; p++)
            {
                List<DeepCFRMultiModel> playerStrategies = await GetSeparateStrategiesForPrincipalComponents(p, parallel);
                if (p == 0)
                    integratedStrategies = playerStrategies;
                else
                {
                    for (int i = 0; i < integratedStrategies.Count(); i++)
                        integratedStrategies[i].IntegrateOtherMultiModel(playerStrategies[i]);
                }
            }
            return integratedStrategies;
        }

        public async Task<List<DeepCFRMultiModel>> GetSeparateStrategiesForPrincipalComponents(byte playerIndex, bool parallel)
        {
            List<DeepCFRMultiModel> result = new List<DeepCFRMultiModel>();
            double[] allZeros = Enumerable.Range(0, EvolutionSettings.DeepCFR_PCA_NumPrincipalComponents).Select(x => (double)0).ToArray();
            TabbedText.WriteLine("");
            TabbedText.WriteLine($"Generating strategy for baseline for player {playerIndex}");
            var baselineStrategy = await GetSinglePlayerStrategyBasedOnPrincipalComponents(playerIndex, allZeros, parallel, false);
            result.Add(baselineStrategy);
            for (int i = 0; i < EvolutionSettings.DeepCFR_PCA_NumPrincipalComponents; i++)
            {
                TabbedText.WriteLine($"Generating strategy for principal component {i} for player {playerIndex}");
                double[] principalComponentForThisStrategyOnly = allZeros.ToArray();
                principalComponentForThisStrategyOnly[i] = NumStandardDeviationsForPrincipalComponentStrategy;
                var strategyForPrincipalComponent = await GetSinglePlayerStrategyBasedOnPrincipalComponents(playerIndex, principalComponentForThisStrategyOnly, parallel, true);
                result.Add(strategyForPrincipalComponent);
            }
            return result;
        }



        public async Task<DeepCFRMultiModel> GetIntegratedStrategyBasedOnPrincipalComponents(List<double>[] principalComponentScoresForEachPlayer, bool parallel)
        {
            DeepCFRMultiModel integratedStrategies = null;
            for (byte p = 0; p < NumNonChancePlayers; p++)
            {
                DeepCFRMultiModel playerStrategies = await GetSinglePlayerStrategyBasedOnPrincipalComponents(p, principalComponentScoresForEachPlayer[p].ToArray(), parallel, false);
                if (p == 0)
                    integratedStrategies = playerStrategies;
                else
                    integratedStrategies.IntegrateOtherMultiModel(playerStrategies);
            }
            return integratedStrategies;
        }

        public async Task<DeepCFRMultiModel> GetSinglePlayerStrategyBasedOnPrincipalComponents(byte playerIndex, double[] principalComponentScoresForPlayer, bool parallel, bool getBoostedModel)
        {
            DeepCFRMultiModel targetModel = GenotypingBaselineMultiModel.DeepCopyObservationsOnly(playerIndex);
            ChangeModelBasedOnPlayerPrincipalComponents(targetModel, playerIndex, principalComponentScoresForPlayer, getBoostedModel);
            await targetModel.ProcessObservations(false, parallel);
            return targetModel;
        }


        private void ChangeModelBasedOnPlayerPrincipalComponents(DeepCFRMultiModel targetModel, byte playerIndex, double[] principalComponentScoresForPlayer, bool getBoostedModel)
        {
            double[] elementsForPlayers = PCAResultsForEachPlayer[playerIndex].PrincipalComponentsToElements(principalComponentScoresForPlayer);
            if (elementsForPlayers.Any(x => double.IsNaN(x) || double.IsInfinity(x)))
                throw new Exception("Invalid player element.");
            if (getBoostedModel)
            {
                // With a boosted model, we start with a baseline where each principal component is zero. We then subtract the elements of 
                // this baseline from the elements corresponding to the desired principal components. Thus, one can determine a strategy 
                // by first using the baseline model and then the other model, adding them together. 
                double[] baselineElements = PCAResultsForEachPlayer[playerIndex].PrincipalComponentsToElements(principalComponentScoresForPlayer.Select(x => (double) 0).ToArray()); // i.e., baseline is result with all 0 principal components
                elementsForPlayers = elementsForPlayers.Zip(baselineElements, (first, second) => first - second).ToArray();
            }
            targetModel.SetExpectedRegretsForObservations(playerIndex, elementsForPlayers);
        }

        public class PCAResultsForPlayer
        {
            public byte playerIndex;
            public double[] meanOfOriginalElements;
            public double[] stdevOfOriginalElements;
            public double[] sigma_squared;
            public double[,] v_principalComponentLoadings;
            public double[,] vTranspose => v_principalComponentLoadings.Transpose();
            public double[] proportionOfAccountedVariance;
            public double[] principalComponentStdevs; // NOTE: Square these to calculate variance. Then proportionOfAccountedVariance is the proportion of the sum of the squares for each one. 

            public double[] PrincipalComponentsToElements(double[] principalComponentScoresForPlayer)
            {
                double[] backProjectedMeanCentered = principalComponentScoresForPlayer.Multiply(vTranspose);
                double[] backProjected = backProjectedMeanCentered.ReverseZScored(meanOfOriginalElements, stdevOfOriginalElements);
                return backProjected;
            }
        }

        #endregion
    }
}