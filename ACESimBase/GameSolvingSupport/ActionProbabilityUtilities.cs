using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESim
{
    [Serializable]
    public static class ActionProbabilityUtilities
    {
        public static byte ChooseActionBasedOnRandomNumber(GameProgress gameProgress, double randomNumberToChooseAction, double randomNumberToChooseIteration, ActionStrategies actionStrategy, byte numPossibleActions, byte? alwaysDoAction, in HistoryNavigationInfo navigation)
        {
            // We're not necessarily using the same navigation approach as is used during CFRDevelopment, because the game tree may not be set up at the time this is called.
            HistoryPoint historyPoint = new HistoryPoint(null, gameProgress.GameHistory, gameProgress);
            HistoryNavigationInfo navigateDuringActualGamePlay = navigation.WithLookupApproach(InformationSetLookupApproach.CachedGameHistoryOnly);
            IGameState gameStateForCurrentPlayer = navigateDuringActualGamePlay.GetGameState(ref historyPoint);
            if (gameStateForCurrentPlayer == null)
                throw new Exception("Internal error. This action has not been initialized.");
            double[] probabilities = GetActionProbabilitiesAtHistoryPoint(gameStateForCurrentPlayer, actionStrategy, randomNumberToChooseIteration, numPossibleActions, alwaysDoAction, navigateDuringActualGamePlay);

            if (gameProgress.ReportingMode)
            {
                StatCollector sc = new StatCollector();
                for (byte a = 0; a < numPossibleActions; a++)
                {
                    if (probabilities[a] > 0)
                    {
                        double value = EquallySpaced.GetLocationOfEquallySpacedPoint(a, probabilities.Length, false);
                        sc.Add(value, probabilities[a]);
                    }
                }
                gameProgress.Mixedness = sc.StandardDeviation();
            }

            //double highest = 0, second = 0;
            //for (byte a = 0; a < numPossibleActions; a++)
            //{
            //    if (probabilities[a] > highest)
            //    {
            //        second = highest;
            //        highest = probabilities[a];
            //    }
            //    else if (probabilities[a] > second)
            //        second = highest;
            //}
            //gameProgress.Mixedness = 1.0 - (highest - second); // e.g., 0, if highest is 100%. 1.0, if top two choices are equal

            double cumTotal = 0;
            for (byte a = 0; a < numPossibleActions; a++)
            {
                cumTotal += probabilities[a];
                if (cumTotal >= randomNumberToChooseAction)
                {
                    //Console.WriteLine($"DecisionCode {historyPoint.GetNextDecisionByteCode(navigation)}");
                    //Console.WriteLine($"Actions {historyPoint.GetActionsToHereString(navigation)}\n probabilities {String.Join(",", probabilities)} random number {randomNumber} result {(byte)(a + 1)}");
                    return (byte)(a + 1); // actions are one-based}
                }
            }
            return numPossibleActions; // indicates a rare rounding error
        }

        private static double[] GetActionProbabilitiesAtHistoryPoint(IGameState gameStateForCurrentPlayer, ActionStrategies actionStrategy, double randomNumberToChooseIteration, byte numPossibleActions, byte? alwaysDoAction, HistoryNavigationInfo navigation)
        {
            GetActionProbabilitiesAtHistoryPoint_Helper(gameStateForCurrentPlayer, actionStrategy, randomNumberToChooseIteration, numPossibleActions, alwaysDoAction, navigation, out double[] probabilities);
            return probabilities.Take(numPossibleActions).ToArray();

        }

        private static unsafe void GetActionProbabilitiesAtHistoryPoint_Helper(IGameState gameStateForCurrentPlayer, ActionStrategies actionStrategy, double randomNumberToChooseIteration, byte numPossibleActions, byte? alwaysDoAction, HistoryNavigationInfo navigation, out double[] probabilities)
        {
            probabilities = new double[GameFullHistory.MaxNumActions];
            double* probabilitiesBuffer = stackalloc double[GameFullHistory.MaxNumActions];
            GetActionProbabilitiesAtHistoryPoint(gameStateForCurrentPlayer, actionStrategy, randomNumberToChooseIteration, probabilitiesBuffer, numPossibleActions, alwaysDoAction, navigation);
            for (byte a = 0; a < GameFullHistory.MaxNumActions; a++)
                probabilities[a] = probabilitiesBuffer[a];
        }

        public static unsafe void GetActionProbabilitiesAtHistoryPoint(IGameState gameStateForCurrentPlayer, ActionStrategies actionStrategy, double randomNumberToChooseIteration, double[] probabilities, byte numPossibleActions, byte? alwaysDoAction, HistoryNavigationInfo navigation)
        {

            double* probabilities2 = stackalloc double[GameFullHistory.MaxNumActions];
            GetActionProbabilitiesAtHistoryPoint(gameStateForCurrentPlayer, actionStrategy, randomNumberToChooseIteration, probabilities2, numPossibleActions, alwaysDoAction, navigation);
            for (int i = 0; i < probabilities.Length; i++)
                probabilities[i] = probabilities2[i];
        }

        public static unsafe void GetActionProbabilitiesAtHistoryPoint(IGameState gameStateForCurrentPlayer, ActionStrategies actionStrategy, double randomNumberToChooseIteration, double* probabilities, byte numPossibleActions, byte? alwaysDoAction, HistoryNavigationInfo navigation)
        {
            if (gameStateForCurrentPlayer is ChanceNode chanceNode)
            {
                byte decisionIndex = chanceNode.DecisionIndex;
                for (byte action = 1; action <= numPossibleActions; action++)
                    probabilities[action - 1] = chanceNode.GetActionProbability(action);
            }
            else
            { // not a chance node or a leaf node
                InformationSetNode informationSetNode = (InformationSetNode)gameStateForCurrentPlayer;
                if (alwaysDoAction != null)
                    SetProbabilitiesToAlwaysDoParticularAction(numPossibleActions, probabilities, (byte)alwaysDoAction);
                else
                    switch (actionStrategy)
                    {
                        case ActionStrategies.RegretMatching:
                            informationSetNode.GetRegretMatchingProbabilities(probabilities);
                            break;
                        case ActionStrategies.AverageStrategy:
                            informationSetNode.GetAverageStrategies(probabilities);
                            break;
                        case ActionStrategies.BestResponse:
                            informationSetNode.GetBestResponseProbabilities(probabilities);
                            break;
                        case ActionStrategies.RegretMatchingWithPruning:
                            informationSetNode.GetRegretMatchingProbabilities_WithPruning(probabilities);
                            break;
                        case ActionStrategies.MultiplicativeWeights:
                            informationSetNode.GetMultiplicativeWeightsProbabilities(probabilities, false);
                            break;
                        case ActionStrategies.Hedge:
                            informationSetNode.GetCurrentProbabilities(probabilities);
                            break;
                        case ActionStrategies.CorrelatedEquilibrium:
                            informationSetNode.GetMultiplicativeWeightsCorrelatedEquilibriumStrategyProbabilities(randomNumberToChooseIteration, probabilities);
                            break;
                        case ActionStrategies.BestResponseVsCorrelatedEquilibrium:
                            if (informationSetNode.PlayerIndex == 0)
                                informationSetNode.GetBestResponseProbabilities(probabilities);
                            else
                                informationSetNode.GetMultiplicativeWeightsCorrelatedEquilibriumStrategyProbabilities(randomNumberToChooseIteration, probabilities);
                            break;
                        case ActionStrategies.CorrelatedEquilibriumVsBestResponse:
                            if (informationSetNode.PlayerIndex == 0)
                                informationSetNode.GetMultiplicativeWeightsCorrelatedEquilibriumStrategyProbabilities(randomNumberToChooseIteration, probabilities);
                            else
                                informationSetNode.GetBestResponseProbabilities(probabilities);
                            break;
                        default:
                            throw new NotImplementedException();
                    }
            }
        }

        public static unsafe void SetProbabilitiesToAlwaysDoParticularAction(byte numPossibleActions, double* actionProbabilities, byte alwaysDoAction)
        {
            for (byte action = 1; action <= numPossibleActions; action++)
                if (action == alwaysDoAction)
                    actionProbabilities[action - 1] = 1.0;
                else
                    actionProbabilities[action - 1] = 0;
        }

    }
}
