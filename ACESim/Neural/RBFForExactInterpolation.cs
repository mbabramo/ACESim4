using System;
using System.Collections.Generic;
using System.Linq;
using Encog.MathUtil.Matrices;
using System.Diagnostics;

namespace ACESim
{
    [Serializable]
    public class RBFForExactInterpolation
    {
        List<double[]> BasisFunctionCenters;
        List<double> TrainingSetOutputs;
        List<double> WidthParameterSigma;

        int NumberHiddenLayerNeurons;
        int Dimensions;
        double[] WeightsToOutputLayer;
        List<double> TwoSigmaSquared;

        public RBFForExactInterpolation(List<double[]> basisFunctionCenters, List<double> targetOutputs, List<double> widthParameterSigma = null)
        {
            BasisFunctionCenters = basisFunctionCenters.Select(x => x.ToArray()).ToList(); // copy the data
            NumberHiddenLayerNeurons = BasisFunctionCenters.Count;
            Dimensions = BasisFunctionCenters.First().Length;
            TrainingSetOutputs = targetOutputs.ToList();
            CalculateWidthParameters(widthParameterSigma);
            CalculateWeightsToOutputLayer();
        }

        public void CalculateWidthParameters(List<double> widthParameterSigma)
        {
            if (widthParameterSigma == null)
            {
                int P = 2;
                WidthParameterSigma =
                    BasisFunctionCenters // for each basis function center
                    .Select(x =>
                        Math.Sqrt( // square root of average of nearest P squared distances relative to this basis function center
                            BasisFunctionCenters
                            .Select((item, index) => new { Distance = CalculateEuclideanDistance(x, item), Index = index }) // calculate distances
                            .OrderBy(y => y.Distance).Skip(1).Take(P) // nearest P
                            .Sum(z => z.Distance * z.Distance) // squared distances
                            / (double)P // averaged
                            )).ToList();
            }
            else
                WidthParameterSigma = widthParameterSigma;

            TwoSigmaSquared = WidthParameterSigma.Select(x => 2.0 * x * x).ToList();
        }

        public void CalculateWeightsToOutputLayer()
        {
            // Define the phi matrix
            Matrix phi = new Matrix(NumberHiddenLayerNeurons, NumberHiddenLayerNeurons);
            for (int rowP = 0; rowP < NumberHiddenLayerNeurons; rowP++)
                for (int colQ = 0; colQ < NumberHiddenLayerNeurons; colQ++)
                {
                    double euclidDistance = CalculateEuclideanDistance(BasisFunctionCenters[rowP], colQ);
                    double afterApplyingRadialBasis = ApplyRadialBasisFunction(euclidDistance, colQ);
                    phi[rowP, colQ] = afterApplyingRadialBasis;
                }
            // Define the target matrix
            Matrix target = Matrix.CreateColumnMatrix(TrainingSetOutputs.ToArray());
            // Calculate the weight matrix
            Matrix weights = MatrixMath.Multiply(phi.Inverse(), target);
            // Copy the weights
            WeightsToOutputLayer = new double[NumberHiddenLayerNeurons];
            for (int rowP = 0; rowP < NumberHiddenLayerNeurons; rowP++)
                WeightsToOutputLayer[rowP] = weights[rowP, 0];
        }

        public double CalculateEuclideanDistance(double[] newInputSet, int basisFunctionIndex)
        {
            double total = 0.0;
            for (int i = 0; i < newInputSet.Length; i++)
            {
                double valueToSquare = newInputSet[i] - BasisFunctionCenters[basisFunctionIndex][i];
                total += valueToSquare * valueToSquare;
            }
            return Math.Sqrt(total);
        }

        internal static double CalculateEuclideanDistance(double[] pointOne, double[] pointTwo)
        {
            double total = 0.0;
            for (int i = 0; i < pointOne.Length; i++)
            {
                double valueToSquare = pointOne[i] - pointTwo[i];
                total += valueToSquare * valueToSquare;
            }
            return Math.Sqrt(total);
        }

        public double ApplyRadialBasisFunction(double r, int basisFunctionIndex)
        {
            return Math.Exp(0 - (r * r) / TwoSigmaSquared[basisFunctionIndex]);
        }


        public double CalculateOutput(double[] newInputSet)
        {
            double total = 0.0;
            for (int basisFunctionIndex = 0; basisFunctionIndex < NumberHiddenLayerNeurons; basisFunctionIndex++)
                total += WeightsToOutputLayer[basisFunctionIndex] * ApplyRadialBasisFunction(CalculateEuclideanDistance(newInputSet, basisFunctionIndex), basisFunctionIndex);
            return total;
        }
    }

    public class RBFTester
    {
        List<double[]> Inputs;
        List<double> Outputs;
        RBFForExactInterpolation RBFNet;

        public void SinTest()
        {
            int numberClusters = 500;
            bool useKMeansClustering = false;
            if (useKMeansClustering)
            {
                int numberInKMeansSourceSet = 1000;
                List<double[]> kMeansSourceSet = new List<double[]>();
                for (int i = 0; i < numberInKMeansSourceSet; i++)
                {
                    double[] trainingInput = new double[1];
                    trainingInput[0] = RandomGenerator.NextDouble() * 6.0;
                    kMeansSourceSet.Add(trainingInput);
                }
                Inputs = KMeansClustering.GetClusters(kMeansSourceSet, numberClusters).OrderBy(x => x[0]).ToList();
            }
            else
                Inputs = new List<double[]>();
            Outputs = new List<double>(numberClusters);
            for (int i = 0; i < numberClusters; i++)
            {
                double trainingInput;
                if (useKMeansClustering)
                    trainingInput = Inputs[i][0];
                else
                {
                    bool useEvenSpacing = true;
                    if (useEvenSpacing)
                        trainingInput = 6.0 * (i + 1) / (numberClusters + 1);
                    else
                        trainingInput = RandomGenerator.NextDouble() * 6.0;
                    Inputs.Add(new double[] { trainingInput });
                }
                double trainingOutput = Math.Sin(trainingInput);
                Outputs.Add(trainingOutput);
            }
            for (int j = 0; j < 10; j++)
            {
                try
                {
                    RBFNet = new RBFForExactInterpolation(Inputs, Outputs);
                    double randTestPoints = 1000;
                    double totalError = 0;
                    for (int i = 0; i < randTestPoints; i++)
                    {
                        double testInput = RandomGenerator.NextDouble() * 6.0; // Inputs[5][0] + 0.000001;
                        double testOutput = RBFNet.CalculateOutput(new double[] { testInput });
                        double error = Math.Abs(testOutput - Math.Sin(testInput));
                        totalError += error;
                    }
                    double avgError = totalError / (double)randTestPoints;
                    Debug.WriteLine(" Error: " + avgError);
                }
                catch
                {
                }
            }
        }
        
    }
}
