using ACESimBase.GameSolvingSupport;
using ACESimBase.GameSolvingSupport.DeepCFR;
using ACESimBase.Util.Collections;
using ACESimBase.Util.Debugging;
using ACESimBase.Util.Parallelization;
using ACESimBase.Util.Reporting;
using ACESimBase.Util.Statistical;
using ACESimBase.Util.TaskManagement;
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
    // 1. Once we have a PCA, see what happens to the Nash equilibria as we change settings. 
    // We can also give further thought to the correlated equilibrium strategies and study whether these are consistent enough to be worth exploring. 
    // 2. Consider also the possibility of a meta-equilibrium on randomness based on distance from Nash equilibria. That is the probability that each player would play
    // a particular strategy would be be (1 / (k + d)), where d is the sum of square distances from Nash equilibria. The question is whether there is a value of k
    // from which neither player would have an incentive to defect in a meta-game determining the degree of randomness. Choosing very small positive k would be equivalent
    // to each player seeking to play a Nash equilibrium (but not always succeeding if there are multiple Nash equilibria). So, each entry in the bimatrix metagame would
    // represent the average strategy of the players with the requisite degree of randomness. To compute this, every cell of the original game matrix must be considered
    // for every cell of the metagame. One question is whether the metagame might produce a different equilibrium if both players recognize that they are estimating utilities
    // with some error, so what one thinks will be the Nash equilibrium in fact might not be. As currently constructed, even though the model is imperfect, once we get to 
    // the bimatrix game, the players play without error. 
    // 3. Quantal Response Equilibrium might be another way to get at this, using the quantal response function. 

    [Serializable]
    public partial class DeepCFR : CounterfactualRegretMinimization
    {
        DeepCFRMultiModel MultiModel;

        /// <summary>
        /// This is a model that is used to provide a baseline for generating model data with DeepCFR. Each datum is a 
        /// regret value for a particular information set and action combination. The baseline multimodel includes for
        /// each decision a collection of such information-set-and-action combinations, so the strategies are interpreted
        /// as lists of regret values in these combinations. Principal components analysis (PCA) can then be used to
        /// reduce model data into a few principal components.
        /// </summary>
        DeepCFRMultiModel BaselineMultiModelForPCA;
        /// <summary>
        /// A collection of different strategies derived by principal component analysis for each player, using a fixed
        /// number of variations for each principal component. For example, there might be five variations for each first
        /// principal component multiplied by three for each second, for a total of fifteen different strategies. 
        /// We are not currently using this approach, instead using approach that allows building on the fly a large number
        /// of different strategies quickly.
        /// </summary>
        public List<DeepCFRMultiModel>[] PCAStrategiesForEachPlayer_Obsolete;

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
            ReportCollection reportCollection = new ReportCollection();
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

            await PostIterationWorkForPrincipalComponentsAnalysis(iteration, reportCollection);

            double[] exploitabilityProxy;
            if (EvolutionSettings.DeepCFR_ExploitabilityProxy)
                exploitabilityProxy = await DeepCFR_ExploitabilityProxy(iteration, isBestResponseIteration);

            if (!isBestResponseIteration)
            {
                var result = await ConsiderGeneratingReports(iteration,
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
            (double[] baselineUtilities, double[] customStats) = await DeepCFR_UtilitiesAndCustomResultAverage(EvolutionSettings.DeepCFR_ApproximateBestResponse_TraversalsForUtilityCalculation);
            TabbedText.WriteLine($"Baseline utilities {string.Join(",", baselineUtilities.Select(x => x.ToSignificantFigures(8)))}");
            double[] bestResponseImprovement = new double[NumNonChancePlayers];
            for (byte p = 0; p < NumNonChancePlayers; p++)
            {
                await MultiModel.PrepareForBestResponseIterations(EvolutionSettings.ParallelOptimization, EvolutionSettings.DeepCFR_ApproximateBestResponse_BackwardInduction_CapacityMultiplier);
                ApproximateBestResponse_CurrentPlayer = p;
                TabbedText.WriteLine($"Determining best response for player {p}");
                TabbedText.TabIndent();
                double[] bestResponseUtilities, bestResponseStats;
                if (EvolutionSettings.DeepCFR_ApproximateBestResponse_BackwardInduction)
                {
                    var decisionsForPlayer = GameDefinition.DecisionsExecutionOrder.Select((item, index) => (item, index)).Where(x => x.item.PlayerIndex == p).OrderByDescending(x => x.index).ToList();
                    int innerIterationsNeeded = decisionsForPlayer.Count();
                    ApproximateBestResponse_CurrentIterationsTotal = innerIterationsNeeded * EvolutionSettings.DeepCFR_ApproximateBestResponseIterations;
                    for (int outerIteration = 0; outerIteration < EvolutionSettings.DeepCFR_ApproximateBestResponseIterations; outerIteration++)
                    {
                        (bestResponseUtilities, bestResponseStats) = await DeepCFR_UtilitiesAndCustomResultAverage(EvolutionSettings.DeepCFR_ApproximateBestResponse_TraversalsForUtilityCalculation);
                        for (int innerIteration = 1; innerIteration <= innerIterationsNeeded; innerIteration++)
                        {
                            ApproximateBestResponse_CurrentIterationsIndex = outerIteration * innerIterationsNeeded + innerIteration;
                            byte decisionIndex = (byte)decisionsForPlayer[innerIteration - 1].index; // this is the overall decision index, i.e. in GameDefinition.DecisionsExecutionOrder
                            MultiModel.TargetBestResponse(p, decisionIndex);
                            if (EvolutionSettings.DeepCFR_ApproximateBestResponse_BackwardInduction_AlwaysPickHighestRegret)
                                MultiModel.StopRegretMatching(p, decisionIndex);
                            var result = await PerformDeepCFRIteration(innerIteration, true);
                            (bestResponseUtilities, bestResponseStats) = await DeepCFR_UtilitiesAndCustomResultAverage(EvolutionSettings.DeepCFR_ApproximateBestResponse_TraversalsForUtilityCalculation);
                            MultiModel.ConcludeTargetingBestResponse(p, decisionIndex);
                            (bestResponseUtilities, bestResponseStats) = await DeepCFR_UtilitiesAndCustomResultAverage(EvolutionSettings.DeepCFR_ApproximateBestResponse_TraversalsForUtilityCalculation);
                            TabbedText.WriteLine($"Utilities for player {p}: {string.Join(",", bestResponseUtilities.Select(x => x.ToSignificantFigures(8)))}");
                        }
                    }
                    (bestResponseUtilities, bestResponseStats) = await DeepCFR_UtilitiesAndCustomResultAverage(EvolutionSettings.DeepCFR_ApproximateBestResponse_TraversalsForUtilityCalculation);
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
                    (bestResponseUtilities, bestResponseStats) = await DeepCFR_UtilitiesAndCustomResultAverage(EvolutionSettings.DeepCFR_ApproximateBestResponse_TraversalsForUtilityCalculation);
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
        public override Task<(double[] compoundUtilities, double[] customStats)> PCA_UtilitiesAndCustomResultAverage(int randSeed, bool reportTime = false)
        {
            return DeepCFR_UtilitiesAndCustomResultAverage(EvolutionSettings.PCA_NumGamesToPlayToEstimateEachUtilityWhileBuildingModel, randSeed, true, reportTime);
        }

        public async Task<(double[] utilities, double[] customResults)> DeepCFR_UtilitiesAndCustomResultAverage(int totalNumberObservations, int observationOffset = 0, bool useGameProgressTree = true, bool reportTime = true)
        {
            if (reportTime)
                TabbedText.Write($"Calculating utilities from {totalNumberObservations}");
            Stopwatch s = new Stopwatch();
            s.Start();
            StatCollectorArray utilityStats = new StatCollectorArray();
            StatCollectorArray customResultStats = new StatCollectorArray();
            if (useGameProgressTree)
                await DeepCFR_UtilitiesAndCustomResultAverage_WithTree(totalNumberObservations, observationOffset, utilityStats, customResultStats);
            else
                await DeepCFR_UtilitiesAndCustomResultsAverage_IndependentPlays(totalNumberObservations, observationOffset, utilityStats, customResultStats);
            double[] averageUtilities = utilityStats.Average().ToArray();
            double[] customResults = customResultStats.Average().ToArray();
            if (reportTime)
                TabbedText.WriteLine($"({averageUtilities.ToSignificantFigures(4)}; customResults: {customResults.ToSignificantFigures(4)} time {s.ElapsedMilliseconds} ms");
            return (averageUtilities, customResults);
        }

        public async Task DeepCFR_UtilitiesAndCustomResultAverage_WithTree(int totalNumberObservations, int randSeed, StatCollectorArray utilityStats, StatCollectorArray customResultStats)
        {
            using (GameProgressTree gameProgressTree = await DeepCFR_BuildGameProgressTree(EvolutionSettings.DeepCFR_ApproximateBestResponse_TraversalsForUtilityCalculation, randSeed, false))
            {
                foreach (GameProgress progress in gameProgressTree)
                {
                    utilityStats.Add(progress.GetNonChancePlayerUtilities());
                    customResultStats.Add(progress.GetCustomResult().AsDoubleArray());
                }
            }
        }

        public Task DeepCFR_UtilitiesAndCustomResultsAverage_IndependentPlays(int totalNumberObservations, int observationOffset, StatCollectorArray utilityStats, StatCollectorArray customResultStats)
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
                utilityStats.Add(utilities, numToPlaybackThisThread);
                customResultStats.Add(new double[] { 0, 0, 0, 0 }); // TODO: Implement
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

        public override async Task<ReportCollection> ConsiderGeneratingReports(int iteration, Func<string> prefaceFn, bool suppressPrintTree = false, string manualReportsSupplementalString = "")
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
            
            Eak.Add("Report");
            bool useGameProgressTree = true;
            if (useGameProgressTree)
            {
                using (var gameProgressTree = await DeepCFR_BuildGameProgressTree(EvolutionSettings.NumRandomIterationsForSummaryTable, 0, false))
                {
                    var gameProgresses = gameProgressTree.AsEnumerable();
                    var gameProgressesArray = gameProgresses.ToArray();
                    reportCollection = GenerateReportsFromGameProgressEnumeration(gameProgressesArray);
                    PrintReportsToScreenIfNotSuppressed(reportCollection);
                }
            }
            else
            {
                reportCollection = await GenerateReportsByPlaying(true);
            }
            //CalculateUtilitiesOverall();
            //TabbedText.WriteLine($"Utilities: {String.Join(",", Status.UtilitiesOverall.Select(x => x.ToSignificantFigures(4)))}");
            Eak.Remove("Report");
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

        public override float[][] GetCurrentModelVariablesForPCA()
        {
            return MultiModel.GetModelVariables(BaselineMultiModelForPCA, NumNonChancePlayers);
        }

        public override void InitializePreparationForPCA()
        {
            BaselineMultiModelForPCA = MultiModel.DeepCopyObservationsOnly(null);
            ModelDataSavedForPCA = new List<float[][]>();
        }

        public override async Task SetModelToPrincipalComponentWeights(List<double>[] principalComponentWeightsForEachPlayer)
        {
            // Note: We are using the compound regression machines container. The logic is considerably more complicated, but
            // this allows us to very quickly update the model for a new set of weights, by weighting separate models for each
            // principal component in each decision and adding them to a baseline model. The alternative approach is included
            // here for comparison. In this case, we change the regrets in the existing multi model to match the regrets 
            // generated from the principal component weights for each player.
            bool useCompoundRegressionMachinesContainer = true;
            if (useCompoundRegressionMachinesContainer)
                CompoundRegressionMachinesContainer.SpecifyWeightOnSupplementalMachines(principalComponentWeightsForEachPlayer, InverseNumStandardDeviationsForPrincipalComponentStrategy);
            else
                await GetAllPlayerStrategiesBasedOnPrincipalComponents(principalComponentWeightsForEachPlayer, EvolutionSettings.ParallelOptimization);
        }

        public override async Task ProcessPrincipalComponentsResults()
        {
            var strategies = await GetSeparateStrategiesForPrincipalComponents(EvolutionSettings.ParallelOptimization);
            DeepCFRCompoundRegressionMachinesContainer container = new DeepCFRCompoundRegressionMachinesContainer(strategies, GameDefinition, NumNonChancePlayers);
            CompoundRegressionMachinesContainer = container;
        }

        private async Task<List<DeepCFRMultiModel>> GetSeparateStrategiesForPrincipalComponents(bool parallel)
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

        private async Task<List<DeepCFRMultiModel>> GetSeparateStrategiesForPrincipalComponents(byte playerIndex, bool parallel)
        {
            List<DeepCFRMultiModel> result = new List<DeepCFRMultiModel>();
            double[] allZeros = Enumerable.Range(0, EvolutionSettings.PCA_NumPrincipalComponents).Select(x => (double)0).ToArray();
            TabbedText.WriteLine("");
            TabbedText.WriteLine($"Generating strategy for baseline for player {playerIndex}");
            DeepCFRMultiModel baselineStrategy = await GetSinglePlayerStrategyBasedOnPrincipalComponents(playerIndex, allZeros, parallel, false);
            result.Add(baselineStrategy);
            for (int i = 0; i < EvolutionSettings.PCA_NumPrincipalComponents; i++)
            {
                TabbedText.WriteLine($"Generating strategy for principal component {i} for player {playerIndex}");
                double[] principalComponentForThisStrategyOnly = allZeros.ToArray();
                principalComponentForThisStrategyOnly[i] = NumStandardDeviationsForPrincipalComponentStrategy;
                var strategyForPrincipalComponent = await GetSinglePlayerStrategyBasedOnPrincipalComponents(playerIndex, principalComponentForThisStrategyOnly, parallel, true);
                result.Add(strategyForPrincipalComponent);
            }
            return result;
        }

        private async Task<DeepCFRMultiModel> GetAllPlayerStrategiesBasedOnPrincipalComponents(List<double>[] principalComponentScoresForEachPlayer, bool parallel)
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

        private async Task<DeepCFRMultiModel> GetSinglePlayerStrategyBasedOnPrincipalComponents(byte playerIndex, double[] principalComponentScoresForPlayer, bool parallel, bool getBoostedModel)
        {
            DeepCFRMultiModel targetModel = BaselineMultiModelForPCA.DeepCopyObservationsOnly(playerIndex);
            ChangeModelBasedOnPlayerPrincipalComponents(targetModel, playerIndex, principalComponentScoresForPlayer, getBoostedModel);
            await targetModel.ProcessObservations(false, parallel);
            return targetModel;
        }

        private void ChangeModelBasedOnPlayerPrincipalComponents(DeepCFRMultiModel targetModel, byte playerIndex, double[] principalComponentScoresForPlayer, bool getBoostedModel)
        {
            double[] elementsForPlayers = PCAResultsForEachPlayer[playerIndex].PrincipalComponentsToVariable(principalComponentScoresForPlayer);
            if (elementsForPlayers.Any(x => double.IsNaN(x) || double.IsInfinity(x)))
                throw new Exception("Invalid player element.");
            if (getBoostedModel)
            {
                // With a boosted model, we start with a baseline where each principal component is zero. We then subtract the elements of 
                // this baseline from the elements corresponding to the desired principal components. Thus, one can determine a strategy 
                // by first using the baseline model and then the other model, adding them together. 
                double[] baselineElements = PCAResultsForEachPlayer[playerIndex].PrincipalComponentsToVariable(principalComponentScoresForPlayer.Select(x => (double) 0).ToArray()); // i.e., baseline is result with all 0 principal components
                elementsForPlayers = elementsForPlayers.Zip(baselineElements, (first, second) => first - second).ToArray();
            }
            targetModel.SetExpectedRegretsForObservations(playerIndex, elementsForPlayers);
        }

        private async Task AssessCompoundStrategyAccuracy()
        {
            // This method compares utilities with a compound strategy (i.e., where each principal component has a separate strategy for each decision) and a PC-specific strategy (i.e., where there is a separate strategy for each decision and that strategy takes into account all of the principal components). The compound strategy can be created just once (but estimation must occur for every principal component), while the PC-specific strategy must be estimated separately for each set of principal component weights. RESULT: Running this method confirms that the compound strategy is a VERY close approximation for the PC-specific strategy.
            var copyContainer = CompoundRegressionMachinesContainer;
            var copyMultiModel = MultiModel;
            StatCollectorArray compoundStats = new StatCollectorArray(), pcSpecificStats = new StatCollectorArray();
            for (int i = 0; i < 100; i++)
            {
                List<double>[] principalComponentWeightsForEachPlayer = GetRandomPrincipalComponentWeightsForEachPlayer(i * 737, 1.0);
                string principalComponentWeightsString = String.Join(";", Enumerable.Range(0, NumNonChancePlayers).Select(x => x.ToString() + ": " + principalComponentWeightsForEachPlayer[x].ToSignificantFigures(3)));
                await SetModelToPrincipalComponentWeights(principalComponentWeightsForEachPlayer);
                (double[] compoundUtilities, double[] compoundCustomStats) = await DeepCFR_UtilitiesAndCustomResultAverage(1_000_000);
                compoundStats.Add(compoundUtilities);
                CompoundRegressionMachinesContainer = null;
                var pcSpecificStrategy = await GetAllPlayerStrategiesBasedOnPrincipalComponents(principalComponentWeightsForEachPlayer, EvolutionSettings.ParallelOptimization);
                MultiModel = pcSpecificStrategy;
                (double[] pcSpecificUtilities, double[] pcSpecificCustomStats) = await DeepCFR_UtilitiesAndCustomResultAverage(1_000_000);
                pcSpecificStats.Add(pcSpecificUtilities);
                MultiModel = copyMultiModel;
                CompoundRegressionMachinesContainer = copyContainer;
                TabbedText.WriteLine($"principal component weights: {principalComponentWeightsString} compound strategy utilities: {compoundUtilities.ToSignificantFigures(7)} regular strategy utilities: {pcSpecificUtilities.ToSignificantFigures(7)}");
            }
            TabbedText.WriteLine($"Compound stats mean {compoundStats.Average().ToSignificantFigures(3)} stdev {compoundStats.StandardDeviation().ToSignificantFigures(3)} ");
            TabbedText.WriteLine($"PC-specific stats mean {pcSpecificStats.Average().ToSignificantFigures(3)} stdev {pcSpecificStats.StandardDeviation().ToSignificantFigures(3)} ");

        }

        public async Task LoadReducedFormStrategies_Obsolete()
        {
            PCAStrategiesForEachPlayer_Obsolete = new List<DeepCFRMultiModel>[NumNonChancePlayers];
            for (byte p = 0; p < NumNonChancePlayers; p++)
                PCAStrategiesForEachPlayer_Obsolete[p] = await GetSinglePlayerReducedFormStrategies_Obsolete(p);
        }

        public async Task<List<DeepCFRMultiModel>> GetSinglePlayerReducedFormStrategies_Obsolete(byte playerIndex)
        {
            var stats = PCAResultsForEachPlayer[playerIndex];
            int[] numVariationsPerPrincipalComponent = EvolutionSettings.PCA_NumVariationsPerPrincipalComponent_Obsolete;
            int numPrincipalComponents = EvolutionSettings.PCA_NumPrincipalComponents;
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

        #endregion
    }
}