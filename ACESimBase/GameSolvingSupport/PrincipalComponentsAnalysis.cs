using ACESimBase.Util.Mathematics;
using ACESimBase.Util.Statistical;
using System;
using System.Linq;

namespace ACESim
{
    /// <summary>
    /// Performs principal components analysis. The constructor converts observations (one per row, typically containing many variables in columns)
    /// into a variety of statistics, including principal component loadings. These statistics can then be used to reconstruct variable values
    /// from a set of principal components.
    /// </summary>
    [Serializable]
    public class PrincipalComponentsAnalysis
    {
        public double[] meanOfOriginalElements;
        public double[] stdevOfOriginalElements;
        public double[] sigma_squared;
        public double[,] v_principalComponentLoadings;
        public double[,] vTranspose => v_principalComponentLoadings.Transpose();
        public double[] proportionOfAccountedVariance;
        public double[] principalComponentStdevs; // NOTE: Square these to calculate variance. Then proportionOfAccountedVariance is the proportion of the sum of the squares for each one. 

        public PrincipalComponentsAnalysis(double[,] sourceData, int numPrincipalComponents, double precision)
        {
            int numObservations = sourceData.GetLength(0);
            int dataPerObservation = sourceData.GetLength(1);
            double[,] meanCentered = null;
            (meanCentered, meanOfOriginalElements, stdevOfOriginalElements) = sourceData.ZScored();
            alglib.pcatruncatedsubspace(meanCentered, numObservations, dataPerObservation, numPrincipalComponents, precision, 0, out sigma_squared, out  v_principalComponentLoadings);
            proportionOfAccountedVariance = sigma_squared.Select(x => x / sigma_squared.Sum()).ToArray();
            double[,] u_principalComponentScores = meanCentered.Multiply(v_principalComponentLoadings);
            // Calculate stats on principal component scores. Mean will be zero. Standard deviations will
            // be such that their squares (i.e., variances) will be in proportion with proportionOfAccountedVariance.
            // So, we don't really need this, but the standard deviations are useful.
            StatCollectorArray principalComponentScoresDistribution = new StatCollectorArray();
            foreach (double[] row in u_principalComponentScores.GetRows())
                principalComponentScoresDistribution.Add(row);
            double[] firstDimensionOnly = u_principalComponentScores.GetColumn(0);
            double[,] vTranspose = v_principalComponentLoadings.Transpose();
            double[,] backProjectedMeanCentered = u_principalComponentScores.Multiply(vTranspose);
            double[,] backProjected = backProjectedMeanCentered.ReverseZScored(meanOfOriginalElements, stdevOfOriginalElements);
            principalComponentStdevs = principalComponentScoresDistribution.StandardDeviation().ToArray();
        }

        public double[] PrincipalComponentsToVariable(double[] principalComponentScores)
        {
            double[] backProjectedMeanCentered = principalComponentScores.Multiply(vTranspose);
            double[] backProjected = backProjectedMeanCentered.ReverseZScored(meanOfOriginalElements, stdevOfOriginalElements);
            return backProjected;
        }
    }
}