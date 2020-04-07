using ACESim.Util;
using ACESimBase;
using ACESimBase.GameSolvingSupport;
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
        DeepCFRModel Model;

        public DeepCFR(List<Strategy> existingStrategyState, EvolutionSettings evolutionSettings, GameDefinition gameDefinition) : base(existingStrategyState, evolutionSettings, gameDefinition)
        {
            Model = new DeepCFRModel(EvolutionSettings.DeepCFR_ReservoirCapacity, 0, EvolutionSettings.DeepCFR_DiscountRate);
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


        /// <summary>
        /// Traverses the game tree for DeepCFR. It performs this either in 
        /// </summary>
        /// <param name="gamePlayer">The game being played</param>
        /// <param name="observationNum">The iteration being played</param>
        /// <returns></returns>
        public double[] DeepCFRTraversal(DirectGamePlayer gamePlayer, DeepCFRObservationNum observationNum, DeepCFRTraversalMode traversalMode)
        {
            GameStateTypeEnum gameStateType = gamePlayer.GetGameStateType();
            if (gameStateType == GameStateTypeEnum.FinalUtilities)
            {
                return gamePlayer.GetFinalUtilities();
            }
            else if (gameStateType == GameStateTypeEnum.Chance)
            {
                return DeepCFR_ChanceNode(gamePlayer, observationNum, traversalMode);
            }
            else
                return DeepCFR_DecisionNode(gamePlayer, observationNum, traversalMode);
        }

        private double[] DeepCFR_DecisionNode(DirectGamePlayer gamePlayer, DeepCFRObservationNum observationNum, DeepCFRTraversalMode traversalMode)
        {
            Decision currentDecision = gamePlayer.CurrentDecision;
            byte decisionIndex = (byte) gamePlayer.CurrentDecisionIndex;
            byte playerMakingDecision = gamePlayer.CurrentPlayer.PlayerIndex;
            byte numPossibleActions = NumPossibleActionsAtDecision(decisionIndex);
            DeepCFRIndependentVariables independentVariables = null;
            List<byte> informationSet = null;
            byte mainAction = GameDefinition.DecisionsExecutionOrder[decisionIndex].AlwaysDoAction ?? 0;
            if (mainAction == 0)
            {
                informationSet = gamePlayer.GetInformationSet();
                independentVariables = new DeepCFRIndependentVariables(playerMakingDecision, decisionIndex, informationSet, null /* DEBUG */);
                mainAction = Model.ChooseAction(observationNum.GetRandomDouble(decisionIndex), independentVariables, numPossibleActions, numPossibleActions /* DEBUG */);
            }
            else if (traversalMode == DeepCFRTraversalMode.AddRegretObservations)
                throw new Exception("When adding regret observations, should not prespecify action");
            DirectGamePlayer mainActionPlayer = gamePlayer.DeepCopy();
            mainActionPlayer.PlayAction(mainAction);
            double[] mainValues = DeepCFRTraversal(mainActionPlayer, observationNum, traversalMode);
            if (traversalMode == DeepCFRTraversalMode.AddRegretObservations)
            {
                // We do a single probe. This allows us to compare the result from the main action.
                DeepCFRObservationNum probeIteration = observationNum.NextVariation();
                DirectGamePlayer probeGamePlayer = gamePlayer.DeepCopy();
                byte probeAction = Model.ChooseAction(probeIteration.GetRandomDouble(decisionIndex), independentVariables, numPossibleActions, numPossibleActions /* DEBUG */);
                probeGamePlayer.PlayAction(probeAction);
                double[] probeValues = DeepCFRTraversal(probeGamePlayer, observationNum, DeepCFRTraversalMode.ProbeForUtilities);
                double sampledRegret = probeValues[playerMakingDecision] - mainValues[playerMakingDecision];
                DeepCFRObservation observation = new DeepCFRObservation()
                {
                    SampledRegret = sampledRegret,
                    IndependentVariables = new DeepCFRIndependentVariables(playerMakingDecision, decisionIndex, informationSet, null /* DEBUG */)
                };
                Model.AddPendingObservation(observation);
            }
            return mainValues;
        }

        private double[] DeepCFR_ChanceNode(DirectGamePlayer gamePlayer, DeepCFRObservationNum observationNum, DeepCFRTraversalMode traversalMode)
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
                    double[] utilities = DeepCFRTraversal(copyPlayer, observationNum, traversalMode);
                    for (int i = 0; i < NumNonChancePlayers; i++)
                        weightedResults[i] = probabilitiesForActions[a - 1] * utilities[i];
                }
                return weightedResults;
            }
            else
            {
                byte actionToChoose = gamePlayer.ChooseChanceAction(observationNum.GetRandomDouble((byte) gamePlayer.CurrentDecisionIndex));
                gamePlayer.PlayAction(actionToChoose);
                return DeepCFRTraversal(gamePlayer, observationNum, traversalMode);
            }
        }

        public override async Task<ReportCollection> RunAlgorithm(string optionSetName)
        {
            ReportCollection reportCollection = new ReportCollection();
            for (int iteration = 1; iteration <= EvolutionSettings.TotalIterations; iteration++)
            {
                var result = await PerformDeepCFRIteration(iteration);
                reportCollection.Add(result);
            }
            return reportCollection;
        }
        private async Task<ReportCollection> PerformDeepCFRIteration(int iteration)
        {
            Status.IterationNumDouble = iteration;

            double[] finalUtilities = new double[NumNonChancePlayers];

            StrategiesDeveloperStopwatch.Start();

            for (int i = 0; i < EvolutionSettings.DeepCFR_TraversalsPerIteration; i++)
            {
                DeepCFRObservationNum observationNum = new DeepCFRObservationNum(i, 0);
                finalUtilities = DeepCFRTraversal(observationNum, DeepCFRTraversalMode.AddRegretObservations).utilities;
            }
            StrategiesDeveloperStopwatch.Stop();

            ReportCollection reportCollection = new ReportCollection();
            var result = await GenerateReports(iteration,
                () =>
                    $"{GameDefinition.OptionSetName} Iteration {iteration} Overall milliseconds per iteration {((StrategiesDeveloperStopwatch.ElapsedMilliseconds / ((double)iteration)))}");
            reportCollection.Add(result);

            await Model.CompleteIteration();

            return reportCollection;
        }

        public GameProgress DeepCFR_GetGameProgressByPlaying(DeepCFRObservationNum observationNum) => DeepCFRTraversal(observationNum, DeepCFRTraversalMode.PlaybackSinglePath).completedProgress;

        public (double[] utilities, GameProgress completedProgress) DeepCFRTraversal(DeepCFRObservationNum observationNum, DeepCFRTraversalMode traversalMode)
        {
            double[] finalUtilities;
            DirectGamePlayer gamePlayer = new DirectGamePlayer(GameDefinition, GameFactory.CreateNewGameProgress(new IterationID(observationNum.ObservationNum)));
            finalUtilities = DeepCFRTraversal(gamePlayer, observationNum, traversalMode);
            return (finalUtilities, gamePlayer.GameProgress);
        }

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
                    RecallBestOverTime();
                    Br.eak.Remove("Report");
                }
                TabbedText.ShowConsoleProgressString();
            }

            return reportCollection;
        }

        public async override Task PlayMultipleIterationsAndProcess(
            GamePlayer player,
            int numIterations,
            Func<Decision, GameProgress, byte> actionOverride,
            BufferBlock<Tuple<GameProgress, double>> bufferBlock) => await PlayMultipleIterationsAndProcess(numIterations, actionOverride, bufferBlock, Strategies, player.DoParallelIfNotDisabled, DeepCFRPlayHelper);

        public GameProgress DeepCFRPlayHelper(int iteration, List<Strategy> strategies, bool saveCompletedGameProgressInfos, IterationID[] iterationIDArray, List<GameProgress> preplayedGameProgressInfos, Func<Decision, GameProgress, byte> actionOverride)
        {
            GameProgress progress = DeepCFR_GetGameProgressByPlaying(new DeepCFRObservationNum(iteration, 1_000_000));

            return progress;
        }
    }
}