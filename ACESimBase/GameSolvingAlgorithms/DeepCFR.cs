using ACESim.Util;
using ACESimBase;
using ACESimBase.GameSolvingSupport;
using ACESimBase.Util;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

namespace ACESim
{


    [Serializable]
    public partial class DeepCFR : CounterfactualRegretMinimization
    {
        DeepCFRMultiModel MultiModel;

        #region Initialization

        public DeepCFR(List<Strategy> existingStrategyState, EvolutionSettings evolutionSettings, GameDefinition gameDefinition) : base(existingStrategyState, evolutionSettings, gameDefinition)
        {
            MultiModel = new DeepCFRMultiModel(EvolutionSettings.DeepCFRMultiModelMode, EvolutionSettings.DeepCFR_ReservoirCapacity, 0, EvolutionSettings.DeepCFR_DiscountRate, EvolutionSettings.RegressionFactory());
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

        #region Traversal

        public List<(Decision decision, DeepCFRObservation observation)> DeepCFR_AddingRegretObservations(DeepCFRPlaybackHelper playbackHelper, int observationIndex, int variationNum, int numToDoTogether)
        {
            int initialObservationNum = observationIndex * numToDoTogether;
            List<(Decision decision, DeepCFRObservation observation)> result = new List<(Decision decision, DeepCFRObservation observation)>();
            for (int i = 0; i < numToDoTogether; i++)
            {
                DeepCFRObservationNum observationNum = new DeepCFRObservationNum(initialObservationNum + i, variationNum);
                var traversalResult = DeepCFRTraversal(playbackHelper, observationNum, DeepCFRTraversalMode.AddRegretObservations).observations;
                result.AddRange(traversalResult);
            }
            return result;
        }

        public (double[] utilities, List<(Decision decision, DeepCFRObservation observation)> observations) DeepCFRTraversal(DeepCFRPlaybackHelper playbackHelper, DeepCFRObservationNum observationNum, DeepCFRTraversalMode traversalMode)
        {
            List<(Decision decision, DeepCFRObservation observation)> observations = new List<(Decision decision, DeepCFRObservation observation)>();
            return (DeepCFRTraversal(playbackHelper, observationNum, traversalMode, observations).utilities, observations);
        }

        private (double[] utilities, GameProgress completedProgress) DeepCFRTraversal(DeepCFRPlaybackHelper playbackHelper, DeepCFRObservationNum observationNum, DeepCFRTraversalMode traversalMode, List<(Decision decision, DeepCFRObservation observation)> observations)
        {
            double[] finalUtilities;
            DeepCFRDirectGamePlayer gamePlayer = new DeepCFRDirectGamePlayer(GameDefinition, GameFactory.CreateNewGameProgress(new IterationID(observationNum.ObservationNum)), true, playbackHelper);
            finalUtilities = DeepCFRTraversal(gamePlayer, observationNum, observations, traversalMode);
            return (finalUtilities, gamePlayer.GameProgress);
        }

        /// <summary>
        /// Traverses the game tree for DeepCFR. It performs this either in 
        /// </summary>
        /// <param name="gamePlayer">The game being played</param>
        /// <param name="observationNum">The iteration being played</param>
        /// <returns></returns>
        public double[] DeepCFRTraversal(DeepCFRDirectGamePlayer gamePlayer, DeepCFRObservationNum observationNum, List<(Decision decision, DeepCFRObservation observation)> observations, DeepCFRTraversalMode traversalMode)
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

        private double[] DeepCFR_DecisionNode(DeepCFRDirectGamePlayer gamePlayer, DeepCFRObservationNum observationNum, List<(Decision decision, DeepCFRObservation observation)> observations, DeepCFRTraversalMode traversalMode)
        {
            Decision currentDecision = gamePlayer.CurrentDecision;
            var playbackHelper = gamePlayer.PlaybackHelper;
            IRegressionMachine regressionMachineForCurrentDecision = playbackHelper.RegressionMachines?.GetValueOrDefault(currentDecision.DecisionByteCode);
            byte decisionIndex = (byte)gamePlayer.CurrentDecisionIndex;
            byte playerMakingDecision = gamePlayer.CurrentPlayer.PlayerIndex;
            byte numPossibleActions = NumPossibleActionsAtDecision(decisionIndex);
            DeepCFRIndependentVariables independentVariables = null;
            double[] onPolicyProbabilities = null;
            (independentVariables, onPolicyProbabilities) = gamePlayer.GetIndependentVariablesAndPlayerProbabilities(observationNum);
            byte mainAction = GameDefinition.DecisionsExecutionOrder[decisionIndex].AlwaysDoAction ?? gamePlayer.ChooseAction(observationNum, decisionIndex, onPolicyProbabilities);
            DeepCFRDirectGamePlayer mainActionPlayer = traversalMode == DeepCFRTraversalMode.PlaybackSinglePath ? gamePlayer : (DeepCFRDirectGamePlayer) gamePlayer.DeepCopy();
            mainActionPlayer.PlayAction(mainAction);
            double[] mainValues = DeepCFRTraversal(mainActionPlayer, observationNum, observations, traversalMode);
            if (traversalMode == DeepCFRTraversalMode.AddRegretObservations)
            {
                if (MultiModel.ObservationsNeeded(currentDecision))
                {
                    // We do a single probe. This allows us to compare this result either to the result from the main action (fast, but high variance) or to the result from all of the other actions (slow, but low variance).
                    DeepCFRObservationNum probeIteration = observationNum.NextVariation();
                    byte probeAction = playbackHelper.MultiModel.ChooseAction(currentDecision, regressionMachineForCurrentDecision, probeIteration.GetRandomDouble(decisionIndex), independentVariables /* note that action in this is ignored */, numPossibleActions, numPossibleActions /* TODO */, EvolutionSettings.DeepCFR_Epsilon_OffPolicyProbabilityForProbe, ref onPolicyProbabilities);
                    // Note: probe action might be same as main action. That's OK, because this helps us estimate expected regret, which is probabilistic
                    double sampledRegret;
                    if (EvolutionSettings.DeepCFR_ProbeAllActions)
                    {
                        if (onPolicyProbabilities == null)
                            onPolicyProbabilities = playbackHelper.MultiModel.GetRegretMatchingProbabilities(independentVariables, currentDecision, regressionMachineForCurrentDecision);
                        double utilityForProbeAction = 0, expectedUtility = 0;
                        for (byte a = 1; a <= currentDecision.NumPossibleActions; a++)
                        {
                            double[] utilitiesForAction = null;
                            if (a == mainAction)
                                utilitiesForAction = mainValues;
                            else
                                utilitiesForAction = DeepCFR_ProbeAction(gamePlayer, observationNum, observations, a);
                            double utilityForAction = utilitiesForAction[playerMakingDecision];
                            if (a == probeAction)
                                utilityForProbeAction = utilityForAction;
                            expectedUtility += onPolicyProbabilities[a - 1] * utilityForAction;
                        }
                        sampledRegret = utilityForProbeAction - expectedUtility;
                    }
                    else
                    {
                        double[] probeValues = DeepCFR_ProbeAction(gamePlayer, observationNum, observations, probeAction);
                        sampledRegret = probeValues[playerMakingDecision] - mainValues[playerMakingDecision];
                    }
                    DeepCFRObservation observation = new DeepCFRObservation()
                    {
                        SampledRegret = sampledRegret,
                        IndependentVariables = new DeepCFRIndependentVariables(playerMakingDecision, decisionIndex, independentVariables.InformationSet, probeAction, null /* TODO */)
                    };
                    observations.Add((currentDecision, observation));
                }
            }
            return mainValues;
        }

        private double[] DeepCFR_ProbeAction(DeepCFRDirectGamePlayer gamePlayer, DeepCFRObservationNum observationNum, List<(Decision decision, DeepCFRObservation observation)> observations, byte probeAction)
        {
            DeepCFRDirectGamePlayer probeGamePlayer = (DeepCFRDirectGamePlayer) gamePlayer.DeepCopy();
            probeGamePlayer.PlayAction(probeAction);
            double[] probeValues = DeepCFRTraversal(probeGamePlayer, observationNum, observations, DeepCFRTraversalMode.ProbeForUtilities);
            return probeValues;
        }

        private double[] DeepCFR_ChanceNode(DeepCFRDirectGamePlayer gamePlayer, DeepCFRObservationNum observationNum, List<(Decision decision, DeepCFRObservation observation)> observations, DeepCFRTraversalMode traversalMode)
        {
            Decision currentDecision = gamePlayer.CurrentDecision;
            if (currentDecision.CriticalNode && traversalMode != DeepCFRTraversalMode.PlaybackSinglePath)
            {
                // At a critical node, we take all paths and weight them by probability.
                double[] weightedResults = new double[NumNonChancePlayers];
                double[] probabilitiesForActions = gamePlayer.GetChanceProbabilities();
                for (byte a = 1; a <= currentDecision.NumPossibleActions; a++)
                {
                    DeepCFRDirectGamePlayer copyPlayer = (DeepCFRDirectGamePlayer)gamePlayer.DeepCopy();
                    copyPlayer.PlayAction(a);
                    double[] utilities = DeepCFRTraversal(copyPlayer, observationNum, observations, traversalMode);
                    for (int i = 0; i < NumNonChancePlayers; i++)
                        weightedResults[i] += probabilitiesForActions[a - 1] * utilities[i];
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

        #region Run algorithm

        public override async Task<ReportCollection> RunAlgorithm(string optionSetName)
        {
            ReportCollection reportCollection = new ReportCollection();
            for (int iteration = 1; iteration <= EvolutionSettings.TotalIterations; iteration++)
            {
                var result = await PerformDeepCFRIteration(iteration, false);
                reportCollection.Add(result.reports);
            }
            if (EvolutionSettings.DeepCFR_ApproximateBestResponse)
            {
                double[] baselineUtilities = await DeepCFR_UtilitiesAverage(EvolutionSettings.DeepCFR_ApproximateBestResponse_TraversalsForUtilityCalculation);
                TabbedText.WriteLine($"Baseline utilities {string.Join(",", baselineUtilities.Select(x => x.ToSignificantFigures(4)))}");
                for (byte p = 0; p < NumNonChancePlayers; p++)
                {
                    TabbedText.WriteLine($"Determining best response for player {p}");
                    TabbedText.TabIndent();
                    MultiModel.StartDeterminingBestResponse(p);
                    for (int iteration = 1; iteration <= EvolutionSettings.DeepCFR_ApproximateBestResponseIterations; iteration++)
                    {
                        var result = await PerformDeepCFRIteration(iteration, true);
                    }
                    double[] bestResponseUtilities = await DeepCFR_UtilitiesAverage(EvolutionSettings.DeepCFR_ApproximateBestResponse_TraversalsForUtilityCalculation);
                    TabbedText.TabUnindent();

                    TabbedText.WriteLine($"Concluding determining best response for player {p} (recreating earlier models)");
                    TabbedText.TabIndent();
                    await MultiModel.EndDeterminingBestResponse(p);
                    TabbedText.TabUnindent();
                    TabbedText.WriteLine($"Utilities with best response for player {p}: {string.Join(",", bestResponseUtilities.Select(x => x.ToSignificantFigures(4)))}");
                    double bestResponseImprovement = bestResponseUtilities[p] - baselineUtilities[p];
                    TabbedText.WriteLine($"Best response improvement for player {p}: {bestResponseImprovement.ToSignificantFigures(4)}");
                }
            }
            return reportCollection;
        }

        private async Task<(ReportCollection reports, double[] utilities)> PerformDeepCFRIteration(int iteration, bool isBestResponseIteration)
        {
            Status.IterationNumDouble = iteration;

            double[] finalUtilities = new double[NumNonChancePlayers];

            Stopwatch localStopwatch = new Stopwatch();
            localStopwatch.Start();
            StrategiesDeveloperStopwatch.Start();

            if (isBestResponseIteration)
                TabbedText.Write($"Best response iteration {iteration} of {EvolutionSettings.DeepCFR_ApproximateBestResponseIterations} ");
            else
                TabbedText.Write($"Iteration {iteration} of {EvolutionSettings.TotalIterations} ");

            int[] numObservationsToAdd = MultiModel.CountPendingObservationsTarget(iteration);
            int numObservationsToAddMax = numObservationsToAdd != null && numObservationsToAdd.Any() ? numObservationsToAdd.Max() : EvolutionSettings.DeepCFR_ReservoirCapacity;
            int numObservationsToDoTogether = GetNumObservationsToDoTogether(numObservationsToAddMax);
            bool separateDataEveryIteration = true;
            DeepCFRProbabilitiesCache probabilitiesCache = new DeepCFRProbabilitiesCache();
            ParallelConsecutive<List<(Decision decision, DeepCFRObservation observation)>> runner = new ParallelConsecutive<List<(Decision decision, DeepCFRObservation observation)>>(
                (numCompleted) => TargetMet(iteration, isBestResponseIteration, numCompleted, numObservationsToAdd),
                i =>
                {
                    var regressionMachines = GetRegressionMachinesForLocalUse(); // note that everything within this block will be on same thread
                    DeepCFRPlaybackHelper playbackHelper = new DeepCFRPlaybackHelper(MultiModel, regressionMachines, probabilitiesCache);
                    var additionalRegretObservations = DeepCFR_AddingRegretObservations(playbackHelper, i, separateDataEveryIteration ? iteration * 1000 : 0, numObservationsToDoTogether);
                    ReturnRegressionMachines(regressionMachines);
                    return additionalRegretObservations;
                },
                results =>
                {
                    foreach (var result in results)
                        MultiModel.AddPendingObservation(result.decision, result.observation);
                }
                );
            await runner.Run(
                EvolutionSettings.ParallelOptimization);

            localStopwatch.Stop();
            StrategiesDeveloperStopwatch.Stop();
            //TabbedText.Write($" utilities {String.Join(",", finalUtilities.Select(x => x.ToSignificantFigures(4)))}");
            TabbedText.WriteLine($" time {localStopwatch.ElapsedMilliseconds} ms");

            TabbedText.TabIndent();
            localStopwatch = new Stopwatch();
            localStopwatch.Start();
            await MultiModel.CompleteIteration(EvolutionSettings.ParallelOptimization);
            TabbedText.TabUnindent();
            TabbedText.WriteLine($"All models completed over {EvolutionSettings.DeepCFR_NeuralNetwork_Epochs} epochs, total time {localStopwatch.ElapsedMilliseconds} ms");
            localStopwatch.Stop();

            ReportCollection reportCollection = new ReportCollection();
            if (!isBestResponseIteration)
            {
                var result = await GenerateReports(iteration,
                    () =>
                        $"{GameDefinition.OptionSetName} Iteration {iteration} Overall milliseconds per iteration {StrategiesDeveloperStopwatch.ElapsedMilliseconds / (double)iteration}");
                reportCollection.Add(result);
            }

            return (reportCollection, finalUtilities);

            bool TargetMet(int iteration, bool isBestResponseIteration, int numberCompleted, int[] numObservationsToAdd)
            {
                bool targetMet;
                if (iteration == 1 && !isBestResponseIteration)
                    targetMet = MultiModel.AllMeetInitialPendingObservationsTarget(EvolutionSettings.DeepCFR_ReservoirCapacity); // must fill all reservoirs in first iteration
                else if (numberCompleted >= EvolutionSettings.DeepCFR_MaximumTotalObservationsPerIteration)
                    targetMet = true;
                else
                    targetMet = MultiModel.AllMeetPendingObservationsTarget(numObservationsToAdd);
                return targetMet;
            }
        }

        private void ReturnRegressionMachines(Dictionary<byte, IRegressionMachine> regressionMachines)
        {
            MultiModel.ReturnRegressionMachines(GameDefinition.DecisionsExecutionOrder, regressionMachines);
        }

        private Dictionary<byte, IRegressionMachine> GetRegressionMachinesForLocalUse()
        {
            return MultiModel.GetRegressionMachinesForLocalUse(GameDefinition.DecisionsExecutionOrder);
        }

        #endregion

        #region Utilities calculation

        public async Task<GameProgressTree> BuildGameProgressTree(int totalNumberObservations)
        {
            DeepCFRPlaybackHelper playbackHelper = new DeepCFRPlaybackHelper(MultiModel, null, null); // DEBUG -- must figure out a way to create a separate object for each thread, but problem is we don't break it down by thread.
            GameProgress initialGameProgress = GameFactory.CreateNewGameProgress(new IterationID(1));
            DeepCFRDirectGamePlayer directGamePlayer = new DeepCFRDirectGamePlayer(GameDefinition, initialGameProgress, true, playbackHelper);
            GameProgressTree gameProgressTree = new GameProgressTree(
                0, // rand seed
                totalNumberObservations,
                directGamePlayer
                );
            await gameProgressTree.CompleteTree(false);
            //string s = gameProgressTree.ToString();
            return gameProgressTree;
        }

        public GameProgress DeepCFR_GetGameProgressByPlaying(DeepCFRPlaybackHelper playbackHelper, DeepCFRObservationNum observationNum) => DeepCFRTraversal(playbackHelper, observationNum, DeepCFRTraversalMode.PlaybackSinglePath, null).completedProgress;

        public async Task<double[]> DeepCFR_UtilitiesAverage(int totalNumberObservations)
        {
            TabbedText.Write($"Calculating utilities from {totalNumberObservations}");
            Stopwatch s = new Stopwatch();
            s.Start();
            StatCollectorArray stats = new StatCollectorArray();
            bool useTree = true;
            if (useTree)
                await DeepCFR_UtilitiesAverage_WithTree(totalNumberObservations, stats);
            else
                await DeepCFR_UtilitiesAverage_IndependentPlays(totalNumberObservations, stats);
            TabbedText.WriteLine($" time {s.ElapsedMilliseconds} ms");
            double[] averageUtilities = stats.Average().ToArray();
            return averageUtilities;
        }

        public async Task DeepCFR_UtilitiesAverage_WithTree(int totalNumberObservations, StatCollectorArray stats)
        {
            GameProgressTree gameProgressTree = await BuildGameProgressTree(EvolutionSettings.DeepCFR_ApproximateBestResponse_TraversalsForUtilityCalculation);
            foreach (GameProgress progress in gameProgressTree)
                stats.Add(progress.GetNonChancePlayerUtilities());
        }

        public Task DeepCFR_UtilitiesAverage_IndependentPlays(int totalNumberObservations, StatCollectorArray stats)
        {
            int numObservationsToDoTogether = GetNumObservationsToDoTogether(totalNumberObservations);
            int numPlaybacks = totalNumberObservations / numObservationsToDoTogether;
            int extraObservationsDueToRounding = numPlaybacks * numObservationsToDoTogether - totalNumberObservations;
            int numPlaybacksLastIteration = numPlaybacks - extraObservationsDueToRounding;
            DeepCFRProbabilitiesCache probabilitiesCache = new DeepCFRProbabilitiesCache(); // shared across threads
            Parallelizer.Go(EvolutionSettings.ParallelOptimization, 0, numPlaybacks, o =>
            {
                DeepCFRPlaybackHelper playbackHelper = new DeepCFRPlaybackHelper(MultiModel.DeepCopyForPlaybackOnly(), GetRegressionMachinesForLocalUse(), probabilitiesCache);
                int numToPlaybackTogetherThisIteration = o == totalNumberObservations - 1 ? numPlaybacksLastIteration : numObservationsToDoTogether;
                var utilities = DeepCFR_UtilitiesFromMultiplePlaybacks(o, numToPlaybackTogetherThisIteration, playbackHelper).ToArray();
                stats.Add(utilities, numToPlaybackTogetherThisIteration);
            });
            return Task.CompletedTask;
        }

        private int GetNumObservationsToDoTogether(int totalNumberObservations)
        {
            return EvolutionSettings.ParallelOptimization ? 1 + totalNumberObservations / (Environment.ProcessorCount * 5) : totalNumberObservations;
        }

        public double[] DeepCFR_UtilitiesFromMultiplePlaybacks(int observation, int numToPlaybackTogether, DeepCFRPlaybackHelper playbackHelper)
        {
            int initialObservation = observation * numToPlaybackTogether;
            double[][] results = Enumerable.Range(initialObservation, initialObservation + numToPlaybackTogether).Select(x => DeepCFR_UtilitiesFromSinglePlayback(playbackHelper, new DeepCFRObservationNum(x, 10_000_000))).ToArray();
            ReturnRegressionMachines(playbackHelper.RegressionMachines);
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
        #endregion

        #region Reporting
        public override async Task<ReportCollection> GenerateReports(int iteration, Func<string> prefaceFn)
        {
            ReportCollection reportCollection = new ReportCollection();
            bool doReports = EvolutionSettings.ReportEveryNIterations != null && (iteration % EvolutionSettings.ReportEveryNIterations == 0 || Status.BestResponseTargetMet(EvolutionSettings.BestResponseTarget));
            if (doReports)
            {
                TabbedText.HideConsoleProgressString();
                TabbedText.WriteLine("");
                TabbedText.WriteLine(prefaceFn());

                if (doReports)
                {
                    Br.eak.Add("Report");
                    bool useGameProgressTree = true;
                    if (useGameProgressTree)
                    {
                        var gameProgressTree = await BuildGameProgressTree(EvolutionSettings.NumRandomIterationsForSummaryTable);
                        reportCollection = GenerateReportsFromGameProgressEnumeration(gameProgressTree);
                    }
                    else
                        reportCollection = await GenerateReportsByPlaying(true);
                    //CalculateUtilitiesOverall();
                    //TabbedText.WriteLine($"Utilities: {String.Join(",", Status.UtilitiesOverall.Select(x => x.ToSignificantFigures(4)))}");
                    Br.eak.Remove("Report");
                }
                TabbedText.ShowConsoleProgressString();
            }

            return reportCollection;
        }

        public async override Task PlayMultipleIterationsForReporting(
            GamePlayer player,
            int numIterations,
            Func<Decision, GameProgress, byte> actionOverride,
            BufferBlock<Tuple<GameProgress, double>> bufferBlock) => await PlayMultipleIterationsAndProcess(numIterations, actionOverride, bufferBlock, Strategies, EvolutionSettings.ParallelOptimization, DeepCFRReportingPlayHelper);

        public GameProgress DeepCFRReportingPlayHelper(int iteration, List<Strategy> strategies, bool saveCompletedGameProgressInfos, IterationID[] iterationIDArray, List<GameProgress> preplayedGameProgressInfos, Func<Decision, GameProgress, byte> actionOverride)
        {
            DeepCFRPlaybackHelper playbackHelper = new DeepCFRPlaybackHelper(MultiModel, null, null); // DEBUG -- no help, so this will be slow
            GameProgress progress = DeepCFR_GetGameProgressByPlaying(playbackHelper, new DeepCFRObservationNum(iteration, 1_000_000));
            progress.IterationID = new IterationID(iteration);

            return progress;
        }

        #endregion
    }
}