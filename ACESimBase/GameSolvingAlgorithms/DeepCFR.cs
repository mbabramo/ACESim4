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


        /// <summary>
        /// Traverses the game tree for DeepCFR. It performs this either in 
        /// </summary>
        /// <param name="gamePlayer">The game being played</param>
        /// <param name="iteration">The iteration being played</param>
        /// <returns></returns>
        public double[] DeepCFRTraversal(DirectGamePlayer gamePlayer, DeepCFRIterationNum iteration, DeepCFRTraversalMode traversalMode)
        {
            GameStateTypeEnum gameStateType = gamePlayer.GetGameStateType();
            if (gameStateType == GameStateTypeEnum.FinalUtilities)
            {
                return gamePlayer.GetFinalUtilities();
            }
            else if (gameStateType == GameStateTypeEnum.Chance)
            {
                return DeepCFR_ChanceNode(gamePlayer, iteration, traversalMode);
            }
            else
                return DeepCFR_DecisionNode(gamePlayer, iteration, traversalMode);
        }

        private double[] DeepCFR_DecisionNode(DirectGamePlayer gamePlayer, DeepCFRIterationNum iteration, DeepCFRTraversalMode traversalMode)
        {
            byte decisionByteCode = gamePlayer.CurrentDecision.DecisionByteCode;
            byte playerMakingDecision = gamePlayer.CurrentPlayer.PlayerIndex;
            byte numPossibleActions = NumPossibleActionsAtDecision(decisionByteCode);
            DeepCFRIndependentVariables independentVariables = null;
            List<byte> informationSet = null;
            byte mainAction = GameDefinition.DecisionsExecutionOrder[decisionByteCode].AlwaysDoAction ?? 0;
            if (mainAction == 0)
            {
                informationSet = gamePlayer.GetInformationSet();
                independentVariables = new DeepCFRIndependentVariables(playerMakingDecision, decisionByteCode, informationSet, null /* DEBUG */);
                mainAction = Model.ChooseAction(iteration.GetRandomDouble(decisionByteCode), independentVariables, numPossibleActions, numPossibleActions /* DEBUG */);
            }
            else if (traversalMode == DeepCFRTraversalMode.AddRegretObservations)
                throw new Exception("When adding regret observations, should not prespecify action");
            double[] mainValues = DeepCFRTraversal(gamePlayer, iteration, traversalMode);
            if (traversalMode == DeepCFRTraversalMode.AddRegretObservations)
            {
                // We do a single probe. This allows us to compare the result from the main action.
                DeepCFRIterationNum probeIteration = iteration.NextVariation();
                DirectGamePlayer probeGamePlayer = gamePlayer.DeepCopy();
                byte probeAction = Model.ChooseAction(probeIteration.GetRandomDouble(decisionByteCode), independentVariables, numPossibleActions, numPossibleActions /* DEBUG */);
                double[] probeValues = DeepCFRTraversal(probeGamePlayer, iteration, DeepCFRTraversalMode.ProbeForUtilities);
                double sampledRegret = probeValues[playerMakingDecision] - mainValues[playerMakingDecision];
                DeepCFRObservation observation = new DeepCFRObservation()
                {
                    SampledRegret = sampledRegret,
                    IndependentVariables = new DeepCFRIndependentVariables(playerMakingDecision, decisionByteCode, informationSet, null /* DEBUG */)
                };
                Model.AddPendingObservation(observation);
            }
            gamePlayer.PlayAction(mainAction);
            return mainValues;
        }

        private double[] DeepCFR_ChanceNode(DirectGamePlayer gamePlayer, DeepCFRIterationNum iteration, DeepCFRTraversalMode traversalMode)
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
                    double[] utilities = DeepCFRTraversal(copyPlayer, iteration, traversalMode);
                    for (int i = 0; i < NumNonChancePlayers; i++)
                        weightedResults[i] = probabilitiesForActions[a - 1] * utilities[i];
                }
                return weightedResults;
            }
            else
            {
                byte actionToChoose = gamePlayer.ChooseChanceAction(iteration.GetRandomDouble(gamePlayer.CurrentDecision.DecisionByteCode));
                gamePlayer.PlayAction(actionToChoose);
                return DeepCFRTraversal(gamePlayer, iteration, traversalMode);
            }
        }

        public override async Task<ReportCollection> RunAlgorithm(string optionSetName)
        {
            if (Navigation.LookupApproach != InformationSetLookupApproach.PlayGameDirectly)
                throw new Exception("Only play underlying game is supported.");

            ReportCollection reportCollection = new ReportCollection();
            for (int iteration = 0; iteration < EvolutionSettings.TotalIterations; iteration++)
            {
                var result = await PerformDeepCFRIteration(new DeepCFRIterationNum(iteration, 0));
                reportCollection.Add(result);
            }
            return reportCollection;
        }
        private async Task<ReportCollection> PerformDeepCFRIteration(DeepCFRIterationNum iteration)
        {
            Status.IterationNumDouble = iteration.IterationNum;

            double[] finalUtilities = new double[NumNonChancePlayers];

            StrategiesDeveloperStopwatch.Start();
            finalUtilities = DeepCFRTraversal(iteration, DeepCFRTraversalMode.AddRegretObservations).utilities;
            StrategiesDeveloperStopwatch.Stop();

            ReportCollection reportCollection = new ReportCollection();
            var result = await GenerateReports(iteration.IterationNum,
                () =>
                    $"{GameDefinition.OptionSetName} Iteration {iteration} Overall milliseconds per iteration {((StrategiesDeveloperStopwatch.ElapsedMilliseconds / ((double)iteration.IterationNum + 1.0)))}");
            reportCollection.Add(result);

            await Model.CompleteIteration();

            return reportCollection;
        }

        public GameProgress DeepCFR_GetGameProgressByPlaying(DeepCFRIterationNum iteration) => DeepCFRTraversal(iteration, DeepCFRTraversalMode.PlaybackSinglePath).completedProgress;

        public (double[] utilities, GameProgress completedProgress) DeepCFRTraversal(DeepCFRIterationNum iteration, DeepCFRTraversalMode traversalMode)
        {
            double[] finalUtilities;
            DirectGamePlayer gamePlayer = new DirectGamePlayer(GameDefinition, GameFactory.CreateNewGameProgress(new IterationID(iteration.IterationNum)));
            finalUtilities = DeepCFRTraversal(gamePlayer, iteration, traversalMode);
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
            GameProgress progress = DeepCFR_GetGameProgressByPlaying(new DeepCFRIterationNum(iteration, 1_000_000));

            return progress;
        }
    }
}