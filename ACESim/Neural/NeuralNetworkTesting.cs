using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Encog.ML.Anneal;
using Encog.Neural.Networks.Training.Anneal;
using Encog.MathUtil;
using Encog.ML.Data;
using Encog.Neural.Networks;
using Encog.ML.Data.Basic;
using Encog.MathUtil.Randomize;
using Encog.Neural.Networks.Training;
using System.Diagnostics;
using Encog.Engine.Network.Activation;
using Encog.Neural.Networks.Layers;

namespace ACESim
{
    public class NeuralNetworkTesting
    {
        public void TestSin2(TrainingTechnique technique)
        {
            if (technique == TrainingTechnique.Backpropagation)
                Debug.WriteLine("Backpropagation test.");
            else if (technique == TrainingTechnique.ResilientPropagation)
                    Debug.WriteLine("Backpropagation (resilient) test.");
            else if (technique == TrainingTechnique.SimulatedAnnealing)
                Debug.WriteLine("Simulated annealing test.");
            else if (technique == TrainingTechnique.SimulatedAnnealingGoffe)
                Debug.WriteLine("Simulated annealing (Goffe approach) test.");
            else if (technique == TrainingTechnique.GeneticAlgorithm)
                Debug.WriteLine("Genetic algorithm test.");
            double[][] SinInput, SinIdeal;
            bool usePreNormalizedData = false;
            bool useDynamicData = false;
            NeuralNetworkData sinData;
            if (useDynamicData)
            {
                SinScorer scorer = new SinScorer();
                sinData = new NeuralNetworkDynamicDataProcessor(1, new double[] { 0 }, new double[] { 6.0 }, -1, 1, scorer);
                scorer.Data = (NeuralNetworkDynamicDataProcessor)sinData;
            }
            else
            {
                if (usePreNormalizedData)
                {
                    SinInput = Enumerable.Range(1, 1000).Select(x => new[] { RandomGenerator.NextDouble() }).ToArray();
                    SinIdeal = SinInput.Select(x => x.Select(y => 0.5 + Math.Sin(y) / 2.0).ToArray()).ToArray();
                }
                else
                {
                    // This should increase error, since we're increasing the range of the sin curve.
                    SinInput = Enumerable.Range(1, 100).Select(x => new[] { RandomGenerator.NextDouble() * 6.0 }).ToArray();
                    SinIdeal = SinInput.Select(x => x.Select(y => Math.Sin(y)).ToArray()).ToArray();
                }

                sinData = new NeuralNetworkTrainingData(1, SinInput, SinIdeal);
            }
            TrainingInfo trainingInfo = null;
            
            int neurons = 40;

            if (technique == TrainingTechnique.Backpropagation)
                trainingInfo = new TrainingInfo() { Technique = TrainingTechnique.Backpropagation, Epochs = 100000 };
            else if (technique == TrainingTechnique.ResilientPropagation)
                trainingInfo = new TrainingInfo() { Technique = TrainingTechnique.ResilientPropagation, Epochs = 100000 };
            //else if (technique == TrainingTechnique.SVD)
            //    trainingInfo = new TrainingInfoForRBF() { Technique = TrainingTechnique.SVD, Epochs = 100000 };
            else if (technique == TrainingTechnique.SimulatedAnnealing)
                trainingInfo = new TrainingInfoForSimulatedAnnealing(10000, 20, 1000);
            else if (technique == TrainingTechnique.SimulatedAnnealingGoffe)
            {
                // neurons = 5;
                trainingInfo = new TrainingInfoForSimulatedAnnealingWithPresetMoveSize(1, 0.0000001, 0.0003, 10000);
                //trainingInfo = new TrainingInfoForSimulatedAnnealingGoffe(1.5, 0.999, 10000);
            }
            else if (technique == TrainingTechnique.GeneticAlgorithm)
                trainingInfo = new TrainingInfoForGeneticAlgorithm(500, 0.1, 0.25, 1000000);
            NeuralNetworkWrapper Wrapper = new NeuralNetworkWrapper(sinData, null, neurons, 0, trainingInfo, true);
        }

        public void TestSin()
        {
            double[][] SinInput = Enumerable.Range(1, 100).Select(x => new[] { Encog.MathUtil.ThreadSafeRandom.NextDouble() }).ToArray();
            double[][] SinIdeal = SinInput.Select(x => x.Select(y => 0.5 + Math.Sin(y) / 2.0).ToArray()).ToArray();
            IMLDataSet trainingData = new BasicMLDataSet(SinInput, SinIdeal);
            BasicNetwork network = new BasicNetwork();
            network.AddLayer(new BasicLayer(null, true, 1));
            network.AddLayer(new BasicLayer(new ActivationSigmoid(), true, 4));
            network.AddLayer(new BasicLayer(new ActivationSigmoid(), false, 1));
            network.Structure.FinalizeStructure();
            (new ConsistentRandomizer(-1, 1)).Randomize(network);
            ICalculateScore score = new TrainingSetScore(trainingData);
            NeuralSimulatedAnnealing anneal = new NeuralSimulatedAnnealing(network, score, 10, 2, 100);
            anneal.Iteration();
            Debug.WriteLine("Annealing error: " + anneal.Error);
            if (anneal.Error > 0.0001)
                throw new Exception("Insufficient performance.");
        }

        public void TestSimulatedAnnealingOutsideNeuralNetworkContext()
        {
            var sa = new SimulatedAnnealingGeneralTester() { StartTemperature = 5.0, Cycles = 1000, ShouldMinimize = true, TemperatureReductionFactor = 0.5 };
            sa.theArray = new double[] { 2.354471, -0.319186 };
            sa.Iteration();
            if (!(sa.theArray[0] > 0.85 && sa.theArray[0] < 0.87 && sa.theArray[1] > 1.22 && sa.theArray[1] < 1.24))
                throw new Exception("Basic Annealing test failed.");
        }
    }
   
    public class SimulatedAnnealingGeneralTester : SimulatedAnnealingGoffe<Double>
    {
        int dimensions = 2;
        public double[] theArray;
        

        /// <summary>
        /// Constructs this object.
        /// </summary>
        ///
        public SimulatedAnnealingGeneralTester()
        {
            theArray = new double[dimensions];
            ShouldMinimize = true;
        }

        /// <summary>
        /// Used to pass the getArray call on to the parent object.
        /// </summary>
        public override double[] Array
        {
            get { return theArray; }
        }


        /// <summary>
        /// Used to pass the getArrayCopy call on to the parent object.
        /// </summary>
        ///
        /// <value>The array copy created by the owner.</value>
        public override double[] ArrayCopy
        {
            get { return theArray.ToArray(); }
        }

        /// <summary>
        /// Used to pass the determineError call on to the parent object.
        /// </summary>
        ///
        /// <returns>The error returned by the owner.</returns>
        public override sealed double PerformCalculateScore()
        {
            return FCN(theArray);
        }


        /// <summary>
        /// Used to pass the putArray call on to the parent object.
        /// </summary>
        ///
        /// <param name="array">The array.</param>
        public override sealed void PutArray(double[] array)
        {
            theArray = array;
        }


        public override double[] GetArray()
        {
            return theArray.ToArray();
        }

        double[] ValueBeforeRandomization;
        int LastD;

        public override double Randomize(int d, double lowerBound, double upperBound)
        {
            if (ValueBeforeRandomization == null)
                ValueBeforeRandomization = new double[theArray.Length];
            ValueBeforeRandomization[d] = theArray[d];
            double add = MoveSizeVM[d] * (2.0 * ThreadSafeRandom.NextDouble() - 1.0);
            double returnVal = theArray[d] = ValueBeforeRandomization[d] + add;

            if (returnVal < lowerBound || returnVal > upperBound)
                returnVal = lowerBound + ThreadSafeRandom.NextDouble() * (upperBound - lowerBound);
            LastD = d;
            return returnVal;
        }


        public override void UndoLastRandomize(bool oneValueOnly)
        {
            if (oneValueOnly)
                theArray[LastD] = ValueBeforeRandomization[LastD];
            else for (int d = 0; d < theArray.Length; d++)
                theArray[d] = ValueBeforeRandomization[d];
        }

        // See http://www.netlib.org/opt/simann.f
        public double FCN(double[] THETA)
        {
            double H;
            double[] Y = new double[21], X2 = new double[21], X3 = new double[21];
            Y[1] = 4.284;
            Y[2] = 4.149;
            Y[3] = 3.877;
            Y[4] = 0.533;
            Y[5] = 2.211;
            Y[6] = 2.389;
            Y[7] = 2.145;
            Y[8] = 3.231;
            Y[9] = 1.998;
            Y[10] = 1.379;
            Y[11] = 2.106;
            Y[12] = 1.428;
            Y[13] = 1.011;
            Y[14] = 2.179;
            Y[15] = 2.858;
            Y[16] = 1.388;
            Y[17] = 1.651;
            Y[18] = 1.593;
            Y[19] = 1.046;
            Y[20] = 2.152;

            X2[1] = .286;
            X2[2] = .973;
            X2[3] = .384;
            X2[4] = .276;
            X2[5] = .973;
            X2[6] = .543;
            X2[7] = .957;
            X2[8] = .948;
            X2[9] = .543;
            X2[10] = .797;
            X2[11] = .936;
            X2[12] = .889;
            X2[13] = .006;
            X2[14] = .828;
            X2[15] = .399;
            X2[16] = .617;
            X2[17] = .939;
            X2[18] = .784;
            X2[19] = .072;
            X2[20] = .889;

            X3[1] = .645;
            X3[2] = .585;
            X3[3] = .310;
            X3[4] = .058;
            X3[5] = .455;
            X3[6] = .779;
            X3[7] = .259;
            X3[8] = .202;
            X3[9] = .028;
            X3[10] = .099;
            X3[11] = .142;
            X3[12] = .296;
            X3[13] = .175;
            X3[14] = .180;
            X3[15] = .842;
            X3[16] = .039;
            X3[17] = .103;
            X3[18] = .620;
            X3[19] = .158;
            X3[20] = .704;

            H = 0.0;
            for (int I = 1; I <= 20; I++)
            {
                H += Math.Pow((THETA[0] + THETA[1] * X2[I] + Math.Pow(THETA[1], 2) * X3[I] - Y[I]), 2.0);
            }
            return H;
        }
    }

    [Serializable]
    public class SinScorer : ICalculateScore, ICalculateScoreForSpecificIteration
    {
        public NeuralNetworkDynamicDataProcessor Data;
        public SinScorer()
        {
            ShouldMinimize = true;
        }

        bool _shouldMinimize;
        public bool ShouldMinimize
        {
            get { return _shouldMinimize; }
            set { _shouldMinimize = value; }
        }

        public double CalculateScore(Encog.ML.IMLRegression network)
        {
            StatCollector s = new StatCollector();
            for (int i = 0; i < 1000; i++)
                CalculateScoreForSpecificDataSample((BasicNetwork)network, i, s);
            return s.Average();
        }

        static double min = 100, max = -100;
        public void CalculateScoreForSpecificDataSample(BasicNetwork network, int dataSample, StatCollector stat)
        {
            Random theRandomGenerator = new Random(dataSample);
            double total = 0;
            const int numberTimesForEachCall = 5;
            for (int i = 0; i < numberTimesForEachCall; i++)
            {
                double valueToTest = theRandomGenerator.NextDouble() * 6.0;
                double calculatedResult = Data.ComputeNetworkBeingDeveloped(new List<double> { valueToTest });
                if (calculatedResult < min) min = calculatedResult;
                if (calculatedResult > max) max = calculatedResult;
                double addNoise = theRandomGenerator.NextDouble() * 0.01 - 0.005;
                double score = Math.Abs(addNoise + Math.Sin(valueToTest) - calculatedResult);
                stat.Add(score);
            }
        }
    }
}
