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
        DeepCFRMultiModel Models;

        public DeepCFR(List<Strategy> existingStrategyState, EvolutionSettings evolutionSettings, GameDefinition gameDefinition) : base(existingStrategyState, evolutionSettings, gameDefinition)
        {
            Models = new DeepCFRMultiModel(EvolutionSettings.DeepCFRMultiModelMode, EvolutionSettings.DeepCFR_ReservoirCapacity, 0, EvolutionSettings.DeepCFR_DiscountRate, EvolutionSettings.DeepCFR_Epochs, EvolutionSettings.DeepCFR_HiddenLayers);
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
            List<(byte decisionIndex, byte information)> informationSet = null;
            byte mainAction = GameDefinition.DecisionsExecutionOrder[decisionIndex].AlwaysDoAction ?? 0;
            if (mainAction == 0)
            {
                informationSet = gamePlayer.GetInformationSet(true);
                independentVariables = new DeepCFRIndependentVariables(playerMakingDecision, decisionIndex, informationSet, 0 /* placeholder */, null /* DEBUG */);
                mainAction = Models.ChooseAction(playerMakingDecision, currentDecision.DecisionByteCode, observationNum.GetRandomDouble(decisionIndex), independentVariables, numPossibleActions, numPossibleActions /* DEBUG */, 0 /* main action is always on policy */);
                independentVariables.ActionChosen = mainAction;
            }
            else if (traversalMode == DeepCFRTraversalMode.AddRegretObservations)
                throw new Exception("When adding regret observations, should not prespecify action");
            DirectGamePlayer mainActionPlayer = traversalMode == DeepCFRTraversalMode.PlaybackSinglePath ? gamePlayer : gamePlayer.DeepCopy();
            mainActionPlayer.PlayAction(mainAction);
            double[] mainValues = DeepCFRTraversal(mainActionPlayer, observationNum, traversalMode);
            if (traversalMode == DeepCFRTraversalMode.AddRegretObservations)
            {
                // We do a single probe. This allows us to compare the result from the main action.
                DeepCFRObservationNum probeIteration = observationNum.NextVariation();
                DirectGamePlayer probeGamePlayer = gamePlayer.DeepCopy();
                independentVariables.ActionChosen = 0; // not essential -- clarifies that no action has been chosen yet
                byte probeAction = Models.ChooseAction(playerMakingDecision, currentDecision.DecisionByteCode, probeIteration.GetRandomDouble(decisionIndex), independentVariables, numPossibleActions, numPossibleActions /* DEBUG */, EvolutionSettings.DeepCFR_Epsilon_OffPolicyProbabilityForProbe);
                independentVariables.ActionChosen = mainAction;
                probeGamePlayer.PlayAction(probeAction);
                double[] probeValues = DeepCFRTraversal(probeGamePlayer, observationNum, DeepCFRTraversalMode.ProbeForUtilities);
                double sampledRegret = probeValues[playerMakingDecision] - mainValues[playerMakingDecision];
                if (decisionIndex == 7 && probeAction != mainAction) // DEBUG
                {
                    //Debug.WriteLine($"Probe: {probeAction} main: {mainAction} probe value: {probeValues[playerMakingDecision]} regret: {sampledRegret}"); // DEBUG
                }
                DeepCFRObservation observation = new DeepCFRObservation()
                {
                    SampledRegret = sampledRegret,
                    IndependentVariables = new DeepCFRIndependentVariables(playerMakingDecision, decisionIndex, informationSet, probeAction, null /* DEBUG */)
                };
                Models.AddPendingObservation(playerMakingDecision, currentDecision.DecisionByteCode, observation);
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

            Stopwatch localStopwatch = new Stopwatch();
            localStopwatch.Start();
            StrategiesDeveloperStopwatch.Start();


            int[] numObservationsToAdd = Models.CountPendingObservationsTarget(iteration);
            if (numObservationsToAdd.Length == 0)
                numObservationsToAdd = null;
            int obsNum = 0;
            do
            {
                DeepCFRObservationNum observationNum = new DeepCFRObservationNum(obsNum, 0);
                finalUtilities = DeepCFRTraversal(observationNum, DeepCFRTraversalMode.AddRegretObservations).utilities;
                obsNum++;
            }
            while (!Models.AllMeetPendingObservationsTarget(numObservationsToAdd));

            localStopwatch.Stop();
            StrategiesDeveloperStopwatch.Stop();

            TabbedText.Write($"Iteration {iteration} of {EvolutionSettings.TotalIterations}: generating {numObservationsToAdd?.Sum() ?? EvolutionSettings.DeepCFR_ReservoirCapacity * NumNonChancePlayers} observations {localStopwatch.ElapsedMilliseconds} ms ");

            localStopwatch = new Stopwatch();
            localStopwatch.Start();
            await Models.CompleteIteration(EvolutionSettings.DeepCFR_Epochs);
            TabbedText.WriteLine($"generating model over {EvolutionSettings.DeepCFR_Epochs} epochs {localStopwatch.ElapsedMilliseconds} ms");


            ReportCollection reportCollection = new ReportCollection();
            var result = await GenerateReports(iteration,
                () =>
                    $"{GameDefinition.OptionSetName} Iteration {iteration} Overall milliseconds per iteration {((StrategiesDeveloperStopwatch.ElapsedMilliseconds / ((double)iteration)))}");
            reportCollection.Add(result);

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
                    RecallBestOverTime();
                    Br.eak.Remove("Report");
                }
                TabbedText.ShowConsoleProgressString();
            }

            return reportCollection;
        }

        public async override Task DeepCFRReportingPlayMultipleIterations(
            GamePlayer player,
            int numIterations,
            Func<Decision, GameProgress, byte> actionOverride,
            BufferBlock<Tuple<GameProgress, double>> bufferBlock) => await PlayMultipleIterationsAndProcess(numIterations, actionOverride, bufferBlock, Strategies, EvolutionSettings.ParallelOptimization, DeepCFRReportingPlayHelper);

        public GameProgress DeepCFRReportingPlayHelper(int iteration, List<Strategy> strategies, bool saveCompletedGameProgressInfos, IterationID[] iterationIDArray, List<GameProgress> preplayedGameProgressInfos, Func<Decision, GameProgress, byte> actionOverride)
        {
            GameProgress progress = DeepCFR_GetGameProgressByPlaying(new DeepCFRObservationNum(iteration, 1_000_000));

            return progress;
        }

        #endregion
    }
}