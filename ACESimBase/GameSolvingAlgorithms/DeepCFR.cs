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

    // DEBUG -- TODO
    // 0. Why is best response sometimes negative? This is true even with a number of best response iterations. (done, if separating out decisions by index, this does not happen anymore).
    // 0.5. Make it so that we can do a single best response iteration, moving backward across decisions. (done)
    // 0.6. Best response could just pick the single best answer rather than using regret matching. This would be in accord with the usual approach to best response. It especially may make sense if using backward induction. (Done as option -- but seems better with regret matching)
    // 1. Use previous prediction as input into next one. A challenge here, though, is that we might want the relevant previous prediction to be of utilities; otherwise, the prediction is really about the different actions from the previous decision. Can we make the prediction to be utility, instead of regrets? Not easily. We need to be predicting regrets so that we can accumulate regrets over iterations. We could try to forecast utilities and see what happens. So, we would need to include an additional regression. The question then becomes whether that is worthwhile, particularly when forecasting regrets rather than utilities. (Added option to work with utilities only, but not very useful)
    // The previous prediction will be the previous prediction while playing the game, so in iteration i, the previous prediction will be based on a model from iteration i - 1. In principle, we could generate the models seriatim and then use the prediction from that same iteration, but that would prevent parallel development of the models.
    // 2. Decision index-specific prediction. Right now, we are grouping all decisions of a particular type. We should at least have an option for customizing by decision index, instead of by decision byte code. (Done)
    // 3. Game parameters. The GameDefinition needs to have a way of randomizing game parameters, so that we can randomize some set of parameters at the beginning of each iteration. Then, we need to be able to generate separate reports (including best response, if applicable) for each set of parameters. We also need to allow for initial and final value parameters (including for caring about utility of opponent), so that we can see the effect of starting with a particular set of parameters, as a way of randomizing where we end up. (May not do -- won't work with GameProgressTree, below.)
    // 4. Principal components analysis. We can reduce a player's strategy to a few principal components. To do this, we need strategies at various times (or with various initial settings, such as utility to share). We need a common reservoir of observations for each of various decisions. We might create that reservoir in an initial iteration, when all decisions are equally likely to occur, but on the other hand it probably makes sense to specialize the reservoir to the decisions most likely to come up in game play (taking some random observations from each of the strategies). Note that we'll need to be able to reverse the PCA to generate a strategy; this will occur first by generating the actions taken for particular strategies and then re-generating the strategy. The PCA may be interesting in and of itself (showing the basic aspects of strategy), but also might be used as part of a technique to minimize best response improvement. That is, we might create a neural network with pairs of players' strategies (again, from different times and/or from different initial settings) and then calculate best response improvement sums. Then, we could optimize the input to this neural network by minimizing this. That's essentially what we tried before without PCA, but it should be much more manageable with just a few principal components. 
    // 5. Correlated equilibrium. With or without PCA, we need to be able to try to build a correlated equilibrium, adapting the code that we used with regret matching etc. 
    // 6. GameProgressTree. Instead of randomly playing the game, use a tree to store the desired number of observations for each decision index. For indices that don't initially produce enough iterations, we use the iterations that are produced as the beginning of a sequence that produces more iterations. Especially with long games, this should be much faster than using random play to get deep into the game, especially when most cases will settle early. Meanwhile, it's more systematic, since we allocate observations based on the action probabilities. (initial step done)
    // Now we need to be able to get regrets from the GameProgressTree. For each GameProgress in each allocation, we can walk back up the tree and put in the average utility. Each iteration gets equal weight in the calculus (even if some have lower reach probability than others), because the GameProgressTree is designed to assign observations in proportion to probability (even while leaving some paths out altogether). But we then have a dilemma if we don't have utilities for every action. Also, utilities across actions may not be comparable -- that is, we are using a different random number generator on different probes. In principle, we could use the same random number generator by randomizing based on the path to the node corresponding to the decision index for the allocation and then the decision index at which a decision is being made; note that the other decision indices will all be hit with certainty as well. But we still have the dilemma that some paths won't be taken at all, where the action probabilities are low.
    // Another approach would be to do one or more probes, using the existing technique. (Note that we would still need to have the completed game progresses, at least for the purpose of generating more observations for the next allocation, although in principle we could stop at the next allocation.) If most of the effort that we exert is getting deep into the game tree, then doing these extra probes probably isn't very costly. Note that we don't need to keep duplicating GameProgress. If we want to do multiple probes, then in principle we could allocate the number we are doing so that we don't have to take an action more than once, but that may not be worthwhile.
    // How many observations? We have a pending observations target, which each decision index must meet. This pending observations target will fall from one iteration to the next. We might create our GameProgressTree so that the number of game progresses is equal to this. But that will include some GameProgresses included more than once, and so unique GameProgresses will be lower. Unique nodes for any decision index will be lower stil, because the same node can lead to multiple GameProgresses. Still, we could generate the required number of observations for each node -- just making each such observation identical (reflecting the same probes from that node). We might have more probes for observations being included more than once. 
    // Meanwhile, if we calculate utilities from each node, then we can calculate regrets for every action from that node, so that should give us many more observations. So, maybe we should think of the observations required as observations per node action, and we will have a larger target for decisions with more observations per node action.


    [Serializable]
    public partial class DeepCFR : CounterfactualRegretMinimization
    {
        DeepCFRMultiModel MultiModel;

        int ApproximateBestResponse_CurrentIterationsTotal;
        int ApproximateBestResponse_CurrentIterationsIndex;
        byte ApproximateBestResponse_CurrentPlayer;

        #region Initialization

        public DeepCFR(List<Strategy> existingStrategyState, EvolutionSettings evolutionSettings, GameDefinition gameDefinition) : base(existingStrategyState, evolutionSettings, gameDefinition)
        {
            int[] reservoirCapacity = GameDefinition.DecisionsExecutionOrder.Select(x =>
            {
                if (x.IsChance)
                    return 0; // this is ignored, but we keep it in the list so that capacity is associated with decision index
                else if (EvolutionSettings.DeepCFR_UseGameProgressTreeToGenerateObservations)
                    return x.NumPossibleActions * EvolutionSettings.DeepCFR_BaseReservoirCapacity;
                else
                    return EvolutionSettings.DeepCFR_BaseReservoirCapacity;
            }).ToArray();
            MultiModel = new DeepCFRMultiModel(GameDefinition.DecisionsExecutionOrder, EvolutionSettings.DeepCFR_MultiModelMode, reservoirCapacity, 0, EvolutionSettings.DeepCFR_DiscountRate, EvolutionSettings.RegressionFactory());
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
            DeepCFRDirectGamePlayer gamePlayer = new DeepCFRDirectGamePlayer(EvolutionSettings.DeepCFR_MultiModelMode, GameDefinition, GameFactory.CreateNewGameProgress(new IterationID(observationNum.ObservationNum)), true, playbackHelper, null /* we will be playing back only this observation for now, so we don't have to combine */);
            finalUtilities = DeepCFRTraversal(gamePlayer, observationNum, observations, traversalMode);
            return (finalUtilities, gamePlayer.GameProgress);
        }

        /// <summary>
        /// Traverses the game tree for DeepCFR. It performs this either in 
        /// </summary>
        /// <param name="gamePlayer">The game being played</param>
        /// <param name="observationNum">The iteration being played</param>
        /// <returns></returns>
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
            var playbackHelper = gamePlayer.InitialPlaybackHelper;
            byte decisionIndex = (byte)gamePlayer.CurrentDecisionIndex;
            IRegressionMachine regressionMachineForCurrentDecision = playbackHelper.GetRegressionMachineIfExists(decisionIndex);
            byte playerMakingDecision = gamePlayer.CurrentPlayer.PlayerIndex;
            byte numPossibleActions = NumPossibleActionsAtDecision(decisionIndex);
            DeepCFRIndependentVariables independentVariables = null;
            double[] onPolicyProbabilities;
            (independentVariables, onPolicyProbabilities) = gamePlayer.GetIndependentVariablesAndPlayerProbabilities(observationNum);
            byte mainAction = GameDefinition.DecisionsExecutionOrder[decisionIndex].AlwaysDoAction ?? gamePlayer.ChooseAction(observationNum, decisionIndex, onPolicyProbabilities);
            DeepCFRDirectGamePlayer mainActionPlayer = traversalMode == DeepCFRTraversalMode.PlaybackSinglePath ? gamePlayer : (DeepCFRDirectGamePlayer)gamePlayer.DeepCopy();
            mainActionPlayer.PlayAction(mainAction);
            double[] mainValues = DeepCFRTraversal(mainActionPlayer, observationNum, observations, traversalMode);
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
                                utilitiesForAction = DeepCFR_ProbeAction(gamePlayer, observationNum, observations, a);
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
                        double[] probeValues = DeepCFR_ProbeAction(gamePlayer, observationNum, observations, probeAction);
                        sampledRegret = probeValues[playerMakingDecision] - mainValues[playerMakingDecision];
                    }
                    DeepCFRObservation observation = new DeepCFRObservation()
                    {
                        SampledRegret = sampledRegret,
                        IndependentVariables = new DeepCFRIndependentVariables(playerMakingDecision, decisionIndex, independentVariables.InformationSet, probeAction, null /* TODO */)
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
                    double[] utilitiesBothPlayers = DeepCFR_ProbeAction(gamePlayer, observationNum, null, probeAction);
                    results[probeAction - 1] += utilitiesBothPlayers[currentPlayerIndex];
                }
                observationNum = observationNum.NextVariation();
            }
            if (numProbes > 1)
                for (byte probeAction = 1; probeAction <= numPossibleActions; probeAction++)
                    results[probeAction - 1] /= (double)numProbes;
            return results;
        }

        private double[] DeepCFR_ProbeAction(DeepCFRDirectGamePlayer gamePlayer, DeepCFRObservationNum observationNum, List<DeepCFRObservationOfDecision> observations, byte probeAction)
        {
            DeepCFRDirectGamePlayer probeGamePlayer = (DeepCFRDirectGamePlayer) gamePlayer.DeepCopy();
            probeGamePlayer.PlayAction(probeAction);
            double[] probeValues = DeepCFRTraversal(probeGamePlayer, observationNum, observations, DeepCFRTraversalMode.ProbeForUtilities);
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
                reportCollection.Add(result);
            }
            if (EvolutionSettings.DeepCFR_ApproximateBestResponse)
            {
                await DoApproximateBestResponse();
            }
            return reportCollection;
        }

        private async Task DoApproximateBestResponse()
        {
            double[] baselineUtilities = await DeepCFR_UtilitiesAverage(EvolutionSettings.DeepCFR_ApproximateBestResponse_TraversalsForUtilityCalculation);
            TabbedText.WriteLine($"Baseline utilities {string.Join(",", baselineUtilities.Select(x => x.ToSignificantFigures(8)))}");
            for (byte p = 0; p < NumNonChancePlayers; p++)
            {
                await MultiModel.PrepareForBestResponseIterations(EvolutionSettings.ParallelOptimization);
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
                        TabbedText.WriteLine($"Utilities for player {p}: {string.Join(",", bestResponseUtilities.Select(x => x.ToSignificantFigures(8)))}"); // DEBUG
                        for (int innerIteration = 1; innerIteration <= innerIterationsNeeded; innerIteration++)
                        {
                            ApproximateBestResponse_CurrentIterationsIndex = outerIteration * innerIterationsNeeded + innerIteration;
                            byte decisionIndex = (byte)decisionsForPlayer[innerIteration - 1].index; // this is the overall decision index, i.e. in GameDefinition.DecisionsExecutionOrder
                            MultiModel.TargetBestResponse(p, decisionIndex);
                            if (EvolutionSettings.DeepCFR_ApproximateBestResponse_BackwardInduction_AlwaysPickHighestRegret)
                                MultiModel.StopRegretMatching(p, decisionIndex);
                            var result = await PerformDeepCFRIteration(innerIteration, true);
                            bestResponseUtilities = await DeepCFR_UtilitiesAverage(EvolutionSettings.DeepCFR_ApproximateBestResponse_TraversalsForUtilityCalculation);
                            TabbedText.WriteLine($"Utilities for player {p}: {string.Join(",", bestResponseUtilities.Select(x => x.ToSignificantFigures(8)))}"); // DEBUG
                            MultiModel.ConcludeTargetingBestResponse(p, decisionIndex);
                            bestResponseUtilities = await DeepCFR_UtilitiesAverage(EvolutionSettings.DeepCFR_ApproximateBestResponse_TraversalsForUtilityCalculation);
                            TabbedText.WriteLine($"Utilities for player {p}: {string.Join(",", bestResponseUtilities.Select(x => x.ToSignificantFigures(8)))}"); // DEBUG
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
                double bestResponseImprovement = bestResponseUtilities[p] - baselineUtilities[p];
                TabbedText.WriteLine($"Best response improvement for player {p}: {bestResponseImprovement.ToSignificantFigures(8)}");
                await MultiModel.ReturnToStateBeforeBestResponseIterations(EvolutionSettings.ParallelOptimization);
            }
        }

        private async Task GenerateDeepCFRObservations_WithGameProgressTree(int iteration, bool isBestResponseIteration)
        {
            int[] numObservationsNeeded = MultiModel.CountPendingObservationsTarget(iteration, isBestResponseIteration, true);
            int DivideRoundingUp(int a, int b) => a / b + (a % b != 0 ? 1 : 0);
            int[] numDirectGamePlayersNeeded = numObservationsNeeded.Select((item, index) => DivideRoundingUp(item, GameDefinition.DecisionsExecutionOrder[index].NumPossibleActions)).ToArray();
            GameProgressTree[] gameProgressTrees = new GameProgressTree[NumNonChancePlayers];
            double offPolicyProbabilityForProbe = isBestResponseIteration ? 0 : EvolutionSettings.DeepCFR_Epsilon_OffPolicyProbabilityForProbe;
            if (offPolicyProbabilityForProbe == 0)
            {
                gameProgressTrees[0] = await BuildGameProgressTree(numDirectGamePlayersNeeded.Max(), true, 0, null);
                for (int p = 1; p < NumNonChancePlayers; p++)
                    gameProgressTrees[p] = gameProgressTrees[0];
            }
            else
            {
                for (byte p = 0; p < NumNonChancePlayers; p++)
                    gameProgressTrees[p] = await BuildGameProgressTree(numDirectGamePlayersNeeded.Max(), true, offPolicyProbabilityForProbe, p);
            }
            var directGamePlayersWithCountsForDecisions = GameProgressTree.GetDirectGamePlayersForEachDecision(gameProgressTrees, offPolicyProbabilityForProbe, numObservationsNeeded);
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
                    double[] utilities = DeepCFR_Probe_GetUtilitiesEachAction((DeepCFRDirectGamePlayer)gamePlayer, observationNum, EvolutionSettings.DeepCFR_NumProbesPerGameProgressTreeObservation);
                    double[] actionProbabilities = gamePlayer.GetActionProbabilities();
                    double expectedValue = 0;
                    for (int j = 0; j < utilities.Length; j++)
                        expectedValue += actionProbabilities[j] * utilities[j];
                    double[] regrets = new double[utilities.Length];
                    for (int j = 0; j < utilities.Length; j++)
                        regrets[j] = utilities[j] - expectedValue;
                    var informationSet = gamePlayer.GetInformationSet(true);

                    for (int j = 0; j < utilities.Length; j++)
                    {
                        DeepCFRObservation observation = new DeepCFRObservation()
                        {
                            IndependentVariables = new DeepCFRIndependentVariables(currentPlayer, (byte)decisionIndex, informationSet, (byte)(j + 1), null),
                            SampledRegret = regrets[j]
                        };
                        for (int k = 0; k < directGamePlayerWithCount.numObservations; k++)
                            MultiModel.AddPendingObservation(currentDecision, (byte) decisionIndex, observation);
                    }
                }

            }
        }

        private async Task GenerateDeepCFRObservations_WithRandomPlay(int iteration, bool isBestResponseIteration)
        {
            int[] numObservationsToAdd = MultiModel.CountPendingObservationsTarget(iteration, isBestResponseIteration, false);
            int numObservationsToAddMax = numObservationsToAdd != null && numObservationsToAdd.Any() ? numObservationsToAdd.Max() : EvolutionSettings.DeepCFR_BaseReservoirCapacity;
            int numObservationsToDoTogether = GetNumObservationsToDoTogether(numObservationsToAddMax);
            bool separateDataEveryIteration = true;
            DeepCFRProbabilitiesCache probabilitiesCache = new DeepCFRProbabilitiesCache();
            ParallelConsecutive<List<DeepCFRObservationOfDecision>> runner = new ParallelConsecutive<List<DeepCFRObservationOfDecision>>(
                (numCompleted) => TargetMet(iteration, isBestResponseIteration, numCompleted * numObservationsToDoTogether, numObservationsToAdd),
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

        private async Task<ReportCollection> PerformDeepCFRIteration(int iteration, bool isBestResponseIteration)
        {
            Status.IterationNumDouble = iteration;

            Stopwatch localStopwatch = new Stopwatch();
            localStopwatch.Start();
            StrategiesDeveloperStopwatch.Start();
            ReportIteration(iteration, isBestResponseIteration);

            if (EvolutionSettings.DeepCFR_UseGameProgressTreeToGenerateObservations)
                await GenerateDeepCFRObservations_WithGameProgressTree(iteration, isBestResponseIteration);
            else
                await GenerateDeepCFRObservations_WithRandomPlay(iteration, isBestResponseIteration);

            localStopwatch.Stop();
            StrategiesDeveloperStopwatch.Stop();
            TabbedText.WriteLine($" time {localStopwatch.ElapsedMilliseconds} ms");

            TabbedText.TabIndent();
            localStopwatch = new Stopwatch();
            localStopwatch.Start();
            await MultiModel.CompleteIteration(EvolutionSettings.ParallelOptimization);
            TabbedText.TabUnindent();
            TabbedText.WriteLine($"All models computed, total time {localStopwatch.ElapsedMilliseconds} ms");
            localStopwatch.Stop();

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

        private void ReturnRegressionMachines(Dictionary<byte, IRegressionMachine> regressionMachines)
        {
            MultiModel.ReturnRegressionMachines(regressionMachines);
        }

        private Dictionary<byte, IRegressionMachine> GetRegressionMachinesForLocalUse()
        {
            return MultiModel.GetRegressionMachinesForLocalUse();
        }

        #endregion

        #region Utilities calculation



        public async Task<GameProgressTree> BuildGameProgressTree(int totalNumberObservations, bool oversampling, double explorationValue = 0, byte? limitToPlayer = null)
        {
            DeepCFRPlaybackHelper playbackHelper = new DeepCFRPlaybackHelper(MultiModel, null, null); // ideally should figure out a way to create a separate object for each thread, but problem is we don't break it down by thread.
            GameProgress initialGameProgress = GameFactory.CreateNewGameProgress(new IterationID(1));
            DeepCFRDirectGamePlayer directGamePlayer = new DeepCFRDirectGamePlayer(EvolutionSettings.DeepCFR_MultiModelMode, GameDefinition, initialGameProgress, true, playbackHelper, () => new DeepCFRPlaybackHelper(MultiModel.DeepCopyForPlaybackOnly(), GetRegressionMachinesForLocalUse(), null));
            double[] explorationValues = explorationValue == 0 ? null /* no exploration */ : Enumerable.Range(0, NumNonChancePlayers).Select(x => x == limitToPlayer ? explorationValue : 0).ToArray();
            GameProgressTree gameProgressTree = new GameProgressTree(
                0, // rand seed
                totalNumberObservations,
                directGamePlayer,
                explorationValues,
                NumNonChancePlayers,
                GameDefinition.DecisionsExecutionOrder,
                limitToPlayer
                );
            await gameProgressTree.CompleteTree(false, explorationValues, oversampling);
            return gameProgressTree;
        }

        public GameProgress DeepCFR_GetGameProgressByPlaying(DeepCFRPlaybackHelper playbackHelper, DeepCFRObservationNum observationNum) => DeepCFRTraversal(playbackHelper, observationNum, DeepCFRTraversalMode.PlaybackSinglePath, null).completedProgress;

        public async Task<double[]> DeepCFR_UtilitiesAverage(int totalNumberObservations)
        {
            TabbedText.Write($"Calculating utilities from {totalNumberObservations}");
            Stopwatch s = new Stopwatch();
            s.Start();
            StatCollectorArray stats = new StatCollectorArray();
            bool useGameProgressTree = true;
            if (useGameProgressTree)
                await DeepCFR_UtilitiesAverage_WithTree(totalNumberObservations, stats);
            else
                await DeepCFR_UtilitiesAverage_IndependentPlays(totalNumberObservations, stats);
            TabbedText.WriteLine($" time {s.ElapsedMilliseconds} ms");
            double[] averageUtilities = stats.Average().ToArray();
            return averageUtilities;
        }

        public async Task DeepCFR_UtilitiesAverage_WithTree(int totalNumberObservations, StatCollectorArray stats)
        {
            GameProgressTree gameProgressTree = await BuildGameProgressTree(EvolutionSettings.DeepCFR_ApproximateBestResponse_TraversalsForUtilityCalculation, false) ;
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
                        var gameProgressTree = await BuildGameProgressTree(EvolutionSettings.NumRandomIterationsForSummaryTable, false);
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
            DeepCFRPlaybackHelper playbackHelper = new DeepCFRPlaybackHelper(MultiModel, null, null); // this will be slow
            GameProgress progress = DeepCFR_GetGameProgressByPlaying(playbackHelper, new DeepCFRObservationNum(iteration, 1_000_000));
            progress.IterationID = new IterationID(iteration);

            return progress;
        }

        #endregion
    }
}