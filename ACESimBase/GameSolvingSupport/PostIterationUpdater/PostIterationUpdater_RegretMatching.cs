using ACESimBase.GameSolvingSupport.GameTree;
using ACESimBase.GameSolvingSupport.Settings;
using System;
using System.Collections.Generic;
using System.Text;

namespace ACESimBase.GameSolvingSupport.PostIterationUpdater
{
    [Serializable]
    public class PostIterationUpdater_RegretMatching : PostIterationUpdaterBase
    {
        double Perturbation;
        bool UseCFRPlus;

        public override void PrepareForUpdating(int iteration, EvolutionSettings evolutionSettings)
        {
            Perturbation = evolutionSettings.Perturbation_BasedOnCurve(iteration, evolutionSettings.TotalIterations);
            UseCFRPlus = evolutionSettings.UseCFRPlusInRegretMatching;
        }

        public override void UpdateInformationSet(InformationSetNode node, bool weightResultByInversePiForIteration)
        {
            // normalize regrets to costs between 0 and 1. the key assumption is that each iteration takes into account ALL possible outcomes (as in a vanilla hedge CFR algorithm)
            double sumPositiveCumRegrets = 0;
            double[,] nodeInformation = node.NodeInformation.ToArrayCopy();
            int numPossibleActions = node.NumPossibleActions;
            for (int a = 1; a <= numPossibleActions; a++)
            {
                double normalizedRegret = GetNormalizedRegret(node, a, weightResultByInversePiForIteration, false);
                double cumulativeRegret = nodeInformation[InformationSetNode.cumulativeRegretDimension, a - 1];
                cumulativeRegret += normalizedRegret;
                if (cumulativeRegret < 0 && UseCFRPlus)
                    cumulativeRegret = 0; // with CFR+, the cumulative regret never falls below zero
                nodeInformation[InformationSetNode.cumulativeRegretDimension, a - 1] = cumulativeRegret;
                if (cumulativeRegret > 0)
                    sumPositiveCumRegrets += cumulativeRegret; // with regular CFR and CFR+, we only count total positive regrets in calculating current probabilities.
                nodeInformation[InformationSetNode.sumRegretTimesInversePiDimension, a - 1] = 0; // reset for next iteration
                nodeInformation[InformationSetNode.sumInversePiDimension, a - 1] = 0;
            }

            // now set the current probability using regret matching (or regret matching plus)

            double totalPerturbation = numPossibleActions * Perturbation;
            double remainingAfterPerturbation = 1.0 - totalPerturbation;

            for (int a = 1; a <= numPossibleActions; a++)
            {
                double currentProbability = Perturbation; // start with the perturbation, if any
                double cumulativeRegret = nodeInformation[InformationSetNode.cumulativeRegretDimension, a - 1];
                double positiveCumulativeRegret = cumulativeRegret > 0 ? cumulativeRegret : 0;
                double ratio = sumPositiveCumRegrets == 0 ? 0 : positiveCumulativeRegret / sumPositiveCumRegrets;
                double remainingRatio = remainingAfterPerturbation * ratio;
                currentProbability += remainingRatio;
                nodeInformation[InformationSetNode.currentProbabilityDimension, a - 1] = currentProbability;
                if (double.IsNaN(currentProbability))
                    throw new Exception();
            }
        }
    }
}
