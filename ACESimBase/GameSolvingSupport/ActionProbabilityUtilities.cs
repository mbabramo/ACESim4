﻿using System;
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

        public static unsafe void GetActionProbabilitiesAtHistoryPoint(IGameState gameStateForCurrentPlayer, ActionStrategies actionStrategy, double randomNumberToChooseIteration, double* probabilities, byte numPossibleActions, byte? alwaysDoAction, HistoryNavigationInfo navigation)
        {
            if (gameStateForCurrentPlayer is ChanceNodeSettings chanceNodeSettings)
            {
                byte decisionIndex = chanceNodeSettings.DecisionIndex;
                for (byte action = 1; action <= numPossibleActions; action++)
                    probabilities[action - 1] = chanceNodeSettings.GetActionProbability(action);
            }
            else
            { // not a chance node or a leaf node
                InformationSetNodeTally nodeTally = (InformationSetNodeTally)gameStateForCurrentPlayer;
                if (alwaysDoAction != null)
                    SetProbabilitiesToAlwaysDoParticularAction(numPossibleActions, probabilities, (byte)alwaysDoAction);
                else
                    switch (actionStrategy)
                    {
                        case ActionStrategies.RegretMatching:
                            nodeTally.GetRegretMatchingProbabilities(probabilities);
                            break;
                        case ActionStrategies.AverageStrategy:
                            nodeTally.GetAverageStrategies(probabilities);
                            break;
                        case ActionStrategies.BestResponse:
                            nodeTally.GetBestResponseProbabilities(probabilities);
                            break;
                        case ActionStrategies.RegretMatchingWithPruning:
                            nodeTally.GetRegretMatchingProbabilities_WithPruning(probabilities);
                            break;
                        case ActionStrategies.NormalizedHedge:
                            nodeTally.GetNormalizedHedgeProbabilities(probabilities);
                            break;
                        case ActionStrategies.Hedge:
                            nodeTally.GetHedgeProbabilities(probabilities);
                            break;
                        case ActionStrategies.CorrelatedEquilibrium:
                            nodeTally.GetNormalizedHedgeCorrelatedEquilibriumStrategyProbabilities(randomNumberToChooseIteration, probabilities);
                            break;
                        case ActionStrategies.BestResponseVsCorrelatedEquilibrium:
                            if (nodeTally.PlayerIndex == 0)
                                nodeTally.GetBestResponseProbabilities(probabilities);
                            else
                                nodeTally.GetNormalizedHedgeCorrelatedEquilibriumStrategyProbabilities(randomNumberToChooseIteration, probabilities);
                            break;
                        case ActionStrategies.CorrelatedEquilibriumVsBestResponse:
                            if (nodeTally.PlayerIndex == 0)
                                nodeTally.GetNormalizedHedgeCorrelatedEquilibriumStrategyProbabilities(randomNumberToChooseIteration, probabilities);
                            else
                                nodeTally.GetBestResponseProbabilities(probabilities);
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
