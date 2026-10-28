using ACESim;
using ACESimBase.GameSolvingSupport.GameTree;
using ACESimBase.GameSolvingSupport.Settings;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Runtime.Versioning;

namespace ACESimBase.GameSolvingSupport.Policies;

/// <summary>Adapter for historical flat equilibrium vectors. Preserves player/node ordering,
/// zero-vector uniform completion and the old report-average calculation. It is not a population selector.</summary>
public static class LegacyEquilibriumPolicies
{
    public static void ApplyCurrent(StrategiesDeveloperBase developer, double[] actionProbabilities)
    {
        var infoSets = developer.InformationSets.OrderBy(x => x.PlayerIndex).ThenBy(x => x.InformationSetNodeNumber).ToList();
        int totalNumbersProcessed = 0;
        for (int i = 0; i < infoSets.Count(); i++)
        {
            double total = 0;
            InformationSetNode infoSet = infoSets[i];
            for (byte a = 1; a <= infoSet.Decision.NumPossibleActions; a++)
            {
                double v = actionProbabilities[totalNumbersProcessed++];
                total += v;
                infoSet.SetCurrentStrategyValue(a, v, true);
            }
            if (total == 0)
            {
                // This is a redundant check for ECTA.
                // This information set cannot be reached. Use even probabilities.
                double v = 1.0 / (double)infoSet.Decision.NumPossibleActions;
                for (byte a = 1; a <= infoSet.Decision.NumPossibleActions; a++)
                {
                    infoSet.SetCurrentStrategyValue(a, v, true);
                }
            }
        }
    }

    public static double[] CaptureCurrent(StrategiesDeveloperBase developer)
    {
        var infoSets = developer.InformationSets.OrderBy(x => x.PlayerIndex).ThenBy(x => x.InformationSetNodeNumber).ToList();
        double[] actionProbabilities = new double[infoSets.Sum(x => x.Decision.NumPossibleActions)];
        int totalNumbersProcessed = 0;
        for (int i = 0; i < infoSets.Count(); i++)
        {
            InformationSetNode infoSet = infoSets[i];
            for (byte a = 1; a <= infoSet.Decision.NumPossibleActions; a++)
            {
                actionProbabilities[totalNumbersProcessed++] = infoSet.GetAverageOrCurrentStrategy(a, false);
            }
        }
        return actionProbabilities;
    }

    public static void InstallAndAccumulateReportAverage(List<InformationSetNode> infoSets, double[] actionProbabilities, int eqNum, bool recordProbabilitiesAsPastValues)
    {
        int totalNumbersProcessed = 0;
        double weightToGivePastValueInAverage = eqNum / (eqNum + 1.0);
        double weightToGiveNewValueInAverage = 1.0 - weightToGivePastValueInAverage;
        for (int i = 0; i < infoSets.Count(); i++)
        {
            double total = 0;
            InformationSetNode infoSet = infoSets[i];
            for (byte a = 1; a <= infoSet.Decision.NumPossibleActions; a++)
            {
                double v = actionProbabilities[totalNumbersProcessed++];
                total += v;
                double weightedCurrentAverageValue = infoSet.GetAverageStrategiesAsArray()[a - 1] * weightToGivePastValueInAverage;
                double revisedAverage = weightedCurrentAverageValue + v * weightToGiveNewValueInAverage;
                infoSet.SetCurrentAndAverageStrategyValues(a, v, revisedAverage);
            }
            if (total == 0)
            {
                // This is a redundant check for ECTA.
                // This information set cannot be reached. Use even probabilities.
                double v = 1.0 / (double)infoSet.Decision.NumPossibleActions;
                for (byte a = 1; a <= infoSet.Decision.NumPossibleActions; a++)
                {
                    double weightedCurrentAverageValue = infoSet.GetAverageStrategiesAsArray()[a - 1] * weightToGivePastValueInAverage;
                    double revisedAverage = weightedCurrentAverageValue + v * weightToGiveNewValueInAverage;
                    infoSet.SetCurrentAndAverageStrategyValues(a, v, revisedAverage);
                }
            }
            if (recordProbabilitiesAsPastValues)
                infoSet.RecordProbabilitiesAsPastValues();
        }
    }
}
