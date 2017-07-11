﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESim
{
    [Serializable]
    public static class CRMActionProbabilities
    {
        public static byte ChooseActionBasedOnRandomNumber(GameProgress gameProgress, double randomNumber, ActionStrategies actionStrategy, byte numPossibleActions, byte? alwaysDoAction, HistoryNavigationInfo navigation)
        {
            HistoryPoint historyPoint = new HistoryPoint(null, gameProgress.GameHistory, gameProgress);
            double[] probabilities = GetActionProbabilitiesAtHistoryPoint(historyPoint, actionStrategy, numPossibleActions, alwaysDoAction, navigation);
            double cumTotal = 0;
            for (byte a = 0; a < numPossibleActions; a++)
            {
                cumTotal += probabilities[a];
                if (cumTotal >= randomNumber)
                    return (byte)(a + 1); // actions are one-based
            }
            return numPossibleActions;
        }

        public static  double[] GetActionProbabilitiesAtHistoryPoint(HistoryPoint historyPoint, ActionStrategies actionStrategy, byte numPossibleActions, byte? alwaysDoAction, HistoryNavigationInfo navigation)
        {
            double[] probabilities;
            GetActionProbabilitiesAtHistoryPoint_Helper(historyPoint, actionStrategy, numPossibleActions, alwaysDoAction, navigation, out probabilities);
            return probabilities.Take(numPossibleActions).ToArray();

        }

        public static unsafe void GetActionProbabilitiesAtHistoryPoint_Helper(HistoryPoint historyPoint, ActionStrategies actionStrategy, byte numPossibleActions, byte? alwaysDoAction, HistoryNavigationInfo navigation, out double[] probabilities)
        {
            probabilities = new double[GameHistory.MaxNumActions];
            double* probabilitiesBuffer = stackalloc double[GameHistory.MaxNumActions];
            GetActionProbabilitiesAtHistoryPoint(historyPoint, actionStrategy, probabilitiesBuffer, numPossibleActions, alwaysDoAction, navigation);
            for (byte a = 0; a < GameHistory.MaxNumActions; a++)
                probabilities[a] = probabilitiesBuffer[a];
        }

        public static unsafe void GetActionProbabilitiesAtHistoryPoint(HistoryPoint historyPoint, ActionStrategies actionStrategy, double* probabilities, byte numPossibleActions, byte? alwaysDoAction, HistoryNavigationInfo navigation)
        {
            if (historyPoint.NodeIsChanceNode(navigation))
            {
                CRMChanceNodeSettings chanceNodeSettings = historyPoint.GetInformationSetChanceSettings(navigation);
                byte decisionIndex = chanceNodeSettings.DecisionIndex;
                for (byte action = 1; action <= numPossibleActions; action++)
                    probabilities[action - 1] = chanceNodeSettings.GetActionProbability(action);
            }
            else
            { // not a chance node or a leaf node
                CRMInformationSetNodeTally nodeTally = historyPoint.GetInformationSetNodeTally(navigation);
                if (alwaysDoAction != null)
                    SetProbabilitiesToAlwaysDoParticularAction(numPossibleActions, probabilities, (byte)alwaysDoAction);
                else if (actionStrategy == ActionStrategies.RegretMatching)
                    nodeTally.GetRegretMatchingProbabilities(probabilities);
                else if (actionStrategy == ActionStrategies.RegretMatchingWithPruning)
                    nodeTally.GetRegretMatchingProbabilities_WithPruning(probabilities);
                else if (actionStrategy == ActionStrategies.AverageStrategy)
                    nodeTally.GetAverageStrategies(probabilities);
                else
                    throw new NotImplementedException();
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
