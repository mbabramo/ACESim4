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
        DeepCFRMultiModel Models;

        #region Initialization

        public DeepCFR(List<Strategy> existingStrategyState, EvolutionSettings evolutionSettings, GameDefinition gameDefinition) : base(existingStrategyState, evolutionSettings, gameDefinition)
        {
            Models = new DeepCFRMultiModel(EvolutionSettings.DeepCFRMultiModelMode, EvolutionSettings.DeepCFR_ReservoirCapacity, 0, EvolutionSettings.DeepCFR_DiscountRate, EvolutionSettings.RegressionFactory());
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

        public GameProgress DeepCFR_GetGameProgressByPlaying(Dictionary<byte, IRegressionMachine> regressionMachines, DeepCFRObservationNum observationNum) => DeepCFRTraversal(regressionMachines, observationNum, DeepCFRTraversalMode.PlaybackSinglePath, null).completedProgress;

        public double[] DeepCFR_UtilitiesAverage(int totalNumberObservations)
        {
            TabbedText.WriteLine($"Calculating utilities from {totalNumberObservations}");
            StatCollectorArray s = new StatCollectorArray();
            int numPlaybacks = totalNumberObservations / EvolutionSettings.DeepCFR_NumObservationsToDoTogether;
            int extraObservationsDueToRounding = (numPlaybacks * EvolutionSettings.DeepCFR_NumObservationsToDoTogether - totalNumberObservations);
            int numPlaybacksLastIteration = numPlaybacks - extraObservationsDueToRounding;
            Parallelizer.Go(EvolutionSettings.ParallelOptimization, 0, numPlaybacks, o =>
            {
                int numToPlaybackTogetherThisIteration = o == totalNumberObservations - 1 ? numPlaybacksLastIteration : EvolutionSettings.DeepCFR_NumObservationsToDoTogether;
                var utilities = DeepCFR_UtilitiesFromMultiplePlaybacks(o, numToPlaybackTogetherThisIteration).ToArray();
                s.Add(utilities, numToPlaybackTogetherThisIteration);
            });
            double[] averageUtilities = s.Average().ToArray();
            return averageUtilities;
        }

        public double[] DeepCFR_UtilitiesFromMultiplePlaybacks(int observation, int numToPlaybackTogether)
        {
            int initialObservation = observation * numToPlaybackTogether;
            Dictionary<byte, IRegressionMachine> regressionMachines = GetRegressionMachinesForLocalUse(); // regression machines will be used locally
            double[][] results = Enumerable.Range(initialObservation, initialObservation + numToPlaybackTogether).Select(x => DeepCFR_UtilitiesFromSinglePlayback(regressionMachines, new DeepCFRObservationNum(x, 10_000_000))).ToArray();
            ReturnRegressionMachines(regressionMachines);
            StatCollectorArray s = new StatCollectorArray();
            foreach (double[] result in results)
                s.Add(result);
            double[] averageResults = s.Average().ToArray();
            return averageResults;
        }

        public double[] DeepCFR_UtilitiesFromSinglePlayback(Dictionary<byte, IRegressionMachine> regressionMachines, DeepCFRObservationNum observationNum)
        {
            return DeepCFRTraversal(regressionMachines, observationNum, DeepCFRTraversalMode.PlaybackSinglePath).utilities;
        }

        public List<(Decision decision, DeepCFRObservation observation)> DeepCFR_AddingRegretObservations(Dictionary<byte, IRegressionMachine> regressionMachines, int observationIndex, int variationNum, int numToDoTogether)
        {
            int initialObservationNum = observationIndex * numToDoTogether;
            List<(Decision decision, DeepCFRObservation observation)> result = new List<(Decision decision, DeepCFRObservation observation)>();
            for (int i = 0; i < numToDoTogether; i++)
            {
                DeepCFRObservationNum observationNum = new DeepCFRObservationNum(initialObservationNum + i, variationNum);
                var traversalResult = DeepCFRTraversal(regressionMachines, observationNum, DeepCFRTraversalMode.AddRegretObservations).observations;
                result.AddRange(traversalResult);
            }
            return result;
        }

        public (double[] utilities, List<(Decision decision, DeepCFRObservation observation)> observations) DeepCFRTraversal(Dictionary<byte, IRegressionMachine> regressionMachines, DeepCFRObservationNum observationNum, DeepCFRTraversalMode traversalMode)
        {
            List<(Decision decision, DeepCFRObservation observation)> observations = new List<(Decision decision, DeepCFRObservation observation)>();
            return (DeepCFRTraversal(regressionMachines, observationNum, traversalMode, observations).utilities, observations);
        }

        private (double[] utilities, GameProgress completedProgress) DeepCFRTraversal(Dictionary<byte, IRegressionMachine> regressionMachines, DeepCFRObservationNum observationNum, DeepCFRTraversalMode traversalMode, List<(Decision decision, DeepCFRObservation observation)> observations)
        {
            double[] finalUtilities;
            DirectGamePlayer gamePlayer = new DirectGamePlayer(GameDefinition, GameFactory.CreateNewGameProgress(new IterationID(observationNum.ObservationNum)));
            finalUtilities = DeepCFRTraversal(regressionMachines, gamePlayer, observationNum, observations, traversalMode);
            return (finalUtilities, gamePlayer.GameProgress);
        }

        /// <summary>
        /// Traverses the game tree for DeepCFR. It performs this either in 
        /// </summary>
        /// <param name="gamePlayer">The game being played</param>
        /// <param name="observationNum">The iteration being played</param>
        /// <returns></returns>
        public double[] DeepCFRTraversal(Dictionary<byte, IRegressionMachine> regressionMachines, DirectGamePlayer gamePlayer, DeepCFRObservationNum observationNum, List<(Decision decision, DeepCFRObservation observation)> observations, DeepCFRTraversalMode traversalMode)
        {
            GameStateTypeEnum gameStateType = gamePlayer.GetGameStateType();
            if (gameStateType == GameStateTypeEnum.FinalUtilities)
            {
                return gamePlayer.GetFinalUtilities();
            }
            else if (gameStateType == GameStateTypeEnum.Chance)
            {
                return DeepCFR_ChanceNode(regressionMachines, gamePlayer, observationNum, observations, traversalMode);
            }
            else
                return DeepCFR_DecisionNode(regressionMachines, gamePlayer, observationNum, observations, traversalMode);
        }

        private double[] DeepCFR_DecisionNode(Dictionary<byte, IRegressionMachine> regressionMachines, DirectGamePlayer gamePlayer, DeepCFRObservationNum observationNum, List<(Decision decision, DeepCFRObservation observation)> observations, DeepCFRTraversalMode traversalMode)
        {
            Decision currentDecision = gamePlayer.CurrentDecision;
            IRegressionMachine regressionMachineForCurrentDecision = regressionMachines?.GetValueOrDefault(currentDecision.DecisionByteCode);
            byte decisionIndex = (byte) gamePlayer.CurrentDecisionIndex;
            byte playerMakingDecision = gamePlayer.CurrentPlayer.PlayerIndex;
            byte numPossibleActions = NumPossibleActionsAtDecision(decisionIndex);
            DeepCFRIndependentVariables independentVariables = null;
            List<(byte decisionIndex, byte information)> informationSet = null;
            byte mainAction = GameDefinition.DecisionsExecutionOrder[decisionIndex].AlwaysDoAction ?? 0;
            if (mainAction == 0)
            {
                informationSet = gamePlayer.GetInformationSet(true);
                independentVariables = new DeepCFRIndependentVariables(playerMakingDecision, decisionIndex, informationSet, 0 /* placeholder */, null /* TODO */);
                mainAction = Models.ChooseAction(currentDecision, regressionMachineForCurrentDecision, observationNum.GetRandomDouble(decisionIndex), independentVariables, numPossibleActions, numPossibleActions /* TODO */, 0 /* main action is always on policy */);
                independentVariables.ActionChosen = mainAction;
            }
            else if (traversalMode == DeepCFRTraversalMode.AddRegretObservations)
                throw new Exception("When adding regret observations, should not prespecify action");
            DirectGamePlayer mainActionPlayer = traversalMode == DeepCFRTraversalMode.PlaybackSinglePath ? gamePlayer : gamePlayer.DeepCopy();
            mainActionPlayer.PlayAction(mainAction);
            double[] mainValues = DeepCFRTraversal(regressionMachines, mainActionPlayer, observationNum, observations, traversalMode);
            if (traversalMode == DeepCFRTraversalMode.AddRegretObservations)
            {
                // We do a single probe. This allows us to compare the result from the main action.
                DeepCFRObservationNum probeIteration = observationNum.NextVariation();
                DirectGamePlayer probeGamePlayer = gamePlayer.DeepCopy();
                independentVariables.ActionChosen = 0; // not essential -- clarifies that no action has been chosen yet
                byte probeAction = Models.ChooseAction(currentDecision, regressionMachineForCurrentDecision, probeIteration.GetRandomDouble(decisionIndex), independentVariables, numPossibleActions, numPossibleActions /* TODO */, EvolutionSettings.DeepCFR_Epsilon_OffPolicyProbabilityForProbe);
                // Note: probe action might be same as main action. That's OK, because this helps us estimate expected regret, which is probabilistic
                independentVariables.ActionChosen = mainAction;
                probeGamePlayer.PlayAction(probeAction);
                double[] probeValues = DeepCFRTraversal(regressionMachines, probeGamePlayer, observationNum, observations, DeepCFRTraversalMode.ProbeForUtilities);
                double sampledRegret = probeValues[playerMakingDecision] - mainValues[playerMakingDecision];
                DeepCFRObservation observation = new DeepCFRObservation()
                {
                    SampledRegret = sampledRegret,
                    IndependentVariables = new DeepCFRIndependentVariables(playerMakingDecision, decisionIndex, informationSet, probeAction, null /* TODO */)
                };
                observations.Add((currentDecision, observation));
            }
            return mainValues;
        }

        private double[] DeepCFR_ChanceNode(Dictionary<byte, IRegressionMachine> regressionMachines, DirectGamePlayer gamePlayer, DeepCFRObservationNum observationNum, List<(Decision decision, DeepCFRObservation observation)> observations, DeepCFRTraversalMode traversalMode)
        {
            Decision currentDecision = gamePlayer.CurrentDecision;
            if (currentDecision.CriticalNode && traversalMode != DeepCFRTraversalMode.PlaybackSinglePath)
            {
                // At a critical node, we take all paths and weight them by probability.
                double[] weightedResults = new double[NumNonChancePlayers];
                double[] probabilitiesForActions = gamePlayer.GetChanceProbabilities();
                for (byte a = 1; a <= currentDecision.NumPossibleActions; a++)
                {
                    DirectGamePlayer copyPlayer = gamePlayer.DeepCopy();
                    copyPlayer.PlayAction(a);
                    double[] utilities = DeepCFRTraversal(regressionMachines, copyPlayer, observationNum, observations, traversalMode);
                    for (int i = 0; i < NumNonChancePlayers; i++)
                        weightedResults[i] += probabilitiesForActions[a - 1] * utilities[i];
                }
                return weightedResults;
            }
            else
            {
                byte actionToChoose = gamePlayer.ChooseChanceAction(observationNum.GetRandomDouble((byte) gamePlayer.CurrentDecisionIndex));
                gamePlayer.PlayAction(actionToChoose);
                return DeepCFRTraversal(regressionMachines, gamePlayer, observationNum, observations, traversalMode);
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
                double[] baselineUtilities = DeepCFR_UtilitiesAverage(EvolutionSettings.DeepCFR_ApproximateBestResponse_TraversalsForUtilityCalculation);
                TabbedText.WriteLine($"Baseline utilities {String.Join(",", baselineUtilities.Select(x => x.ToSignificantFigures(4)))}");
                for (byte p = 0; p < NumNonChancePlayers; p++)
                {
                    TabbedText.WriteLine($"Determining best response for player {p}");
                    TabbedText.TabIndent();
                    Models.StartDeterminingBestResponse(p);
                    for (int iteration = 1; iteration <= EvolutionSettings.DeepCFR_ApproximateBestResponseIterations; iteration++)
                    {
                        var result = await PerformDeepCFRIteration(iteration, true);
                    }
                    double[] bestResponseUtilities = DeepCFR_UtilitiesAverage(EvolutionSettings.DeepCFR_ApproximateBestResponse_TraversalsForUtilityCalculation);
                    TabbedText.TabUnindent();

                    TabbedText.WriteLine($"Concluding determining best response for player {p} (recreating earlier models)");
                    TabbedText.TabIndent();
                    await Models.EndDeterminingBestResponse(p);
                    TabbedText.TabUnindent();
                    TabbedText.WriteLine($"Utilities with best response for player {p}: {String.Join(",", bestResponseUtilities.Select(x => x.ToSignificantFigures(4)))}");
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

            int[] numObservationsToAdd = Models.CountPendingObservationsTarget(iteration);
            bool separateDataEveryIteration = true;
            ParallelConsecutive<List<(Decision decision, DeepCFRObservation observation)>> runner = new ACESimBase.Util.ParallelConsecutive<List<(Decision decision, DeepCFRObservation observation)>>(
                (int numCompleted) => TargetMet(iteration, isBestResponseIteration, numCompleted, numObservationsToAdd),
                i =>
                {
                    var regressionMachines = GetRegressionMachinesForLocalUse(); // note that everything within this block will be on same thread
                    var additionalRegretObservations = DeepCFR_AddingRegretObservations(regressionMachines, i, separateDataEveryIteration ? iteration * 1000 : 0, EvolutionSettings.DeepCFR_NumObservationsToDoTogether);
                    ReturnRegressionMachines(regressionMachines);
                    return additionalRegretObservations;
                },
                results =>
                {
                    foreach (var result in results)
                        Models.AddPendingObservation(result.decision, result.observation);
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
            await Models.CompleteIteration(EvolutionSettings.ParallelOptimization);
            TabbedText.TabUnindent();
            TabbedText.WriteLine($"All models completed over {EvolutionSettings.DeepCFR_NeuralNetwork_Epochs} epochs, total time {localStopwatch.ElapsedMilliseconds} ms");
            localStopwatch.Stop();

            ReportCollection reportCollection = new ReportCollection();
            if (!isBestResponseIteration)
            {
                var result = await GenerateReports(iteration,
                    () =>
                        $"{GameDefinition.OptionSetName} Iteration {iteration} Overall milliseconds per iteration {((StrategiesDeveloperStopwatch.ElapsedMilliseconds / ((double)iteration)))}");
                reportCollection.Add(result);
            }

            return (reportCollection, finalUtilities);

            bool TargetMet(int iteration, bool isBestResponseIteration, int numberCompleted, int[] numObservationsToAdd)
            {
                bool targetMet;
                if (iteration == 1 && !isBestResponseIteration)
                    targetMet = Models.AllMeetInitialPendingObservationsTarget(EvolutionSettings.DeepCFR_ReservoirCapacity); // must fill all reservoirs in first iteration
                else if (numberCompleted >= EvolutionSettings.DeepCFR_MaximumTotalObservationsPerIteration)
                    targetMet = true;
                else
                    targetMet = Models.AllMeetPendingObservationsTarget(numObservationsToAdd);
                return targetMet;
            }
        }

        private void ReturnRegressionMachines(Dictionary<byte, IRegressionMachine> regressionMachines)
        {
            Models.ReturnRegressionMachines(GameDefinition.DecisionsExecutionOrder, regressionMachines);
        }

        private Dictionary<byte, IRegressionMachine> GetRegressionMachinesForLocalUse()
        {
            return Models.GetRegressionMachinesForLocalUse(GameDefinition.DecisionsExecutionOrder);
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
            GameProgress progress = DeepCFR_GetGameProgressByPlaying(null, new DeepCFRObservationNum(iteration, 1_000_000));
            progress.IterationID = new IterationID(iteration);

            return progress;
        }

        #endregion
    }
}