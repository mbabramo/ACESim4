using ACESimBase.GameSolvingSupport.GameTree;
using ACESimBase.GameSolvingSupport.Settings;
using System;
using System.Collections.Generic;
using System.Text;

namespace ACESimBase.GameSolvingSupport.PostIterationUpdater
{
    [Serializable]
    public abstract class PostIterationUpdaterBase
    {
        public virtual void PrepareForUpdating(int iteration, EvolutionSettings evolutionSettings)
        {

        }

        public abstract void UpdateInformationSet(InformationSetNode node, bool weightResultByInversePiForIteration);

        public double GetNormalizedRegret(InformationSetNode node, int a, bool weightResultByInversePiForIteration, bool makeStrictlyPositive)
        {
            double[,] nodeInformation = node.NodeInformation;
            double denominator = nodeInformation[InformationSetNode.sumInversePiDimension, a - 1];
            double regretUnnormalized = denominator == 0 ? 0.5 * (node.MaxPossibleThisPlayer - node.MinPossibleThisPlayer) : nodeInformation[InformationSetNode.sumRegretTimesInversePiDimension, a - 1] / denominator;
            double normalizedRegret = node.NormalizeRegret(regretUnnormalized, makeStrictlyPositive); // bad moves are now close to 0 and good moves are close to 1
            if (double.IsInfinity(normalizedRegret))
                return 0; // could be the case where the max possible and min possible are always equal
            if (weightResultByInversePiForIteration)
                return normalizedRegret * denominator; // we divided by denominator so that we could normalize, but now we'll multiply by denominator again
            return normalizedRegret;
        }
    }
}
