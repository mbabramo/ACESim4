using ACESim.Util;
using ACESimBase;
using ACESimBase.GameSolvingSupport;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;

namespace ACESim
{
    [Serializable]
    public partial class DeepCFR : CounterfactualRegretMinimization
    {
        DeepCFRModel Model;
        bool ReportingMode = true;

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
        public double[] DeepCFRTraversal(DirectGamePlayer gamePlayer, DeepCFRIterationNum iteration)
        {
            GameStateTypeEnum gameStateType = gamePlayer.GetGameStateType();
            if (gameStateType == GameStateTypeEnum.FinalUtilities)
            {
                return gamePlayer.GetFinalUtilities();
            }
            else if (gameStateType == GameStateTypeEnum.Chance)
            {
                return DeepCFR_ChanceNode(gamePlayer, iteration);
            }
            else
                return DeepCFR_DecisionNode(gamePlayer, iteration);
        }

        private double[] DeepCFR_DecisionNode(DirectGamePlayer gamePlayer, DeepCFRIterationNum iteration)
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
            else if (ReportingMode == false)
                throw new Exception("Should only use AlwaysDoAction during game playback.");
            double[] mainValues = DeepCFRTraversal(gamePlayer, iteration);
            if (!ReportingMode)
            {
                // We do a single probe. This allows us to compare the result from the main action.
                DeepCFRIterationNum probeIteration = iteration.NextVariation();
                DirectGamePlayer probeGamePlayer = gamePlayer.DeepCopy();
                byte probeAction = Model.ChooseAction(probeIteration.GetRandomDouble(decisionByteCode), independentVariables, numPossibleActions, numPossibleActions /* DEBUG */);
                double[] probeValues = DeepCFRTraversal(probeGamePlayer, iteration);
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

        private double[] DeepCFR_ChanceNode(DirectGamePlayer gamePlayer, DeepCFRIterationNum iteration)
        {
            // DEBUG TODO: Handle critical chance decisions.
            byte actionToChoose = gamePlayer.ChooseChanceAction(iteration.GetRandomDouble(gamePlayer.CurrentDecision.DecisionByteCode));
            gamePlayer.PlayAction(actionToChoose);
            return DeepCFRTraversal(gamePlayer, iteration);
        }

        public override async Task<ReportCollection> RunAlgorithm(string optionSetName)
        {
            if (Navigation.LookupApproach != InformationSetLookupApproach.PlayGameDirectly)
                throw new Exception("Only play underlying game is supported.");

            ReportCollection reportCollection = new ReportCollection();
            for (int iteration = 0; iteration < EvolutionSettings.TotalIterations; iteration++)
            {
                var result = await CompleteDeepCFRIteration(new DeepCFRIterationNum(iteration, 0));
                reportCollection.Add(result);
            }
            return reportCollection;
        }
        private async Task<ReportCollection> CompleteDeepCFRIteration(DeepCFRIterationNum iteration)
        {
            Status.IterationNumDouble = iteration.IterationNum;

            double[] finalUtilities = new double[NumNonChancePlayers];

            StrategiesDeveloperStopwatch.Start();

            ReportingMode = false;
            DirectGamePlayer gamePlayer = new DirectGamePlayer(GameDefinition, GameFactory.CreateNewGameProgress(new IterationID(iteration.IterationNum)));
            finalUtilities = DeepCFRTraversal(gamePlayer, iteration);

            StrategiesDeveloperStopwatch.Stop();

            ReportingMode = true;
            ReportCollection reportCollection = new ReportCollection();
            var result = await GenerateReports(iteration.IterationNum,
                () =>
                    $"{GameDefinition.OptionSetName} Iteration {iteration} Overall milliseconds per iteration {((StrategiesDeveloperStopwatch.ElapsedMilliseconds / ((double)iteration.IterationNum + 1.0)))}");
            reportCollection.Add(result);

            await Model.CompleteIteration();

            return reportCollection;
        }
    }
}