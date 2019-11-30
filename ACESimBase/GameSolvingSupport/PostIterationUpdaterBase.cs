using ACESim;
using System;
using System.Collections.Generic;
using System.Text;

namespace ACESimBase.GameSolvingSupport
{
    [Serializable]
    public abstract class PostIterationUpdaterBase
    {
        public virtual void PrepareForUpdating(int iteration, EvolutionSettings evolutionSettings)
        {

        }

        public abstract void UpdateInformationSet(InformationSetNode node);

        public double GetNormalizedRegret(InformationSetNode node, int a, bool weightResultByInversePiForIteration, bool makeStrictlyPositive)
        {
            // DEBUG -- we should do the following only if we're using opponent sampling.
            weightResultByInversePiForIteration = true; // DEBUG SUPERDEBUG // right now, we're doing this each iteration. But with regret matching, we are then accumulating the NORMALIZED REGRET. The problem
            // is that when we're using monte carlo, we really need to accumulate both the numerator and the denominator over many iterations. Then, after that, we can normalize (based on the range of outcomes for the player). 

            double[,] nodeInformation = node.NodeInformation;
            double denominator = nodeInformation[InformationSetNode.sumInversePiDimension, a - 1];
            double regretUnnormalized = (denominator == 0) ? 0.5 * (node.MaxPossibleThisPlayer - node.MinPossibleThisPlayer) : nodeInformation[InformationSetNode.sumRegretTimesInversePiDimension, a - 1] / denominator;
            double normalizedRegret = node.NormalizeRegret(regretUnnormalized, makeStrictlyPositive); // bad moves are now close to 0 and good moves are close to 1
            if (weightResultByInversePiForIteration)
                return normalizedRegret * denominator; // we divided by denominator so that we could normalize, but now we'll multiply by denominator again
            return normalizedRegret;
        }
    }
}
