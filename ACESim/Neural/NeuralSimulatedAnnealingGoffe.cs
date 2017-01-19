//
// Encog(tm) Core v3.0 - .Net Version
// http://www.heatonresearch.com/encog/
//
// Copyright 2008-2011 Heaton Research, Inc.
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//  http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
//   
// For more information on Heaton Research copyrights, licenses 
// and trademarks visit:
// http://www.heatonresearch.com/copyright
//

using Encog.ML;
using Encog.ML.Train;
using Encog.Neural.Networks.Structure;
using Encog.Neural.Networks.Training.Propagation;
using Encog.Util.Logging;
using Encog.MathUtil;

namespace Encog.Neural.Networks.Training.Anneal
{
    /// <summary>
    /// This class implements a simulated annealing training algorithm for neural
    /// networks. It is based on the generic SimulatedAnnealingGoffe class. It is used in
    /// the same manner as any other training class that implements the Train
    /// interface. There are essentially two ways you can make use of this class.
    /// Either way, you will need a score object. The score object tells the
    /// simulated annealing algorithm how well suited a neural network is.
    /// If you would like to use simulated annealing with a training set you should
    /// make use TrainingSetScore class. This score object uses a training set to
    /// score your neural network.
    /// If you would like to be more abstract, and not use a training set, you can
    /// create your own implementation of the CalculateScore method. This class can
    /// then score the networks any way that you like.
    /// </summary>
    ///
    public class NeuralSimulatedAnnealingGoffe : BasicTraining
    {
        /// <summary>
        /// The cutoff for random data.
        /// </summary>
        ///
        public const double Cut = 0.5d;

        /// <summary>
        /// This class actually performs the training.
        /// </summary>
        ///
        private readonly NeuralSimulatedAnnealingGoffeHelper _anneal;

        /// <summary>
        /// Used to calculate the score.
        /// </summary>
        ///
        private readonly ICalculateScore _calculateScore;

        /// <summary>
        /// The neural network that is to be trained.
        /// </summary>
        ///
        private readonly BasicNetwork _network;

        /// <summary>
        /// Construct a simulated annleaing trainer for a feedforward neural network.
        /// </summary>
        public NeuralSimulatedAnnealingGoffe(BasicNetwork network,
                                        ICalculateScore calculateScore, double startTemp,
                                        double tempReductionFactor, int cycles) : base(TrainingImplementationType.Iterative)
        {
            _network = network;
            _calculateScore = calculateScore;
            _anneal = new NeuralSimulatedAnnealingGoffeHelper(this)
                          {
                              StartTemperature = startTemp,
                              TemperatureReductionFactor = tempReductionFactor,
                              Cycles = cycles,
                              DynamicallyAdjustTemperatureInsteadOfMoveSize = false
                          };
        }

        public NeuralSimulatedAnnealingGoffe(BasicNetwork network,
                                        ICalculateScore calculateScore, double startMoveSize,
                                        double endMoveSize, double curvature, int cycles)
            : base(TrainingImplementationType.Iterative)
        {
            _network = network;
            _calculateScore = calculateScore;
            _anneal = new NeuralSimulatedAnnealingGoffeHelper(this)
            {
                StartTemperature = 1.0,
                StartMoveSize = startMoveSize,
                EndMoveSize = endMoveSize,
                CurvatureFromStartToEndMoveSize = curvature,
                Cycles = cycles,
                DynamicallyAdjustTemperatureInsteadOfMoveSize = true
            };
        }

        /// <inheritdoc />
        public override sealed bool CanContinue
        {
            get { return false; }
        }

        /// <summary>
        /// Get the network as an array of doubles.
        /// </summary>
        public double[] Array
        {
            get
            {
                return NetworkCODEC
                    .NetworkToArray(_network);
            }
        }


        /// <value>A copy of the annealing array.</value>
        public double[] ArrayCopy
        {
            get { return Array; }
        }


        /// <value>The object used to calculate the score.</value>
        public ICalculateScore CalculateScore
        {
            get { return _calculateScore; }
        }


        /// <inheritdoc/>
        public override IMLMethod Method
        {
            get { return _network; }
        }


        /// <summary>
        /// Perform one iteration of simulated annealing.
        /// </summary>
        ///
        public override sealed void Iteration()
        {
            EncogLogging.Log(EncogLogging.LevelInfo,
                             "Performing Simulated Annealing iteration.");
            PreIteration();
            _anneal.Iteration();
            Error = _anneal.PerformCalculateScore();
            PostIteration();
        }

        /// <inheritdoc/>
        public override TrainingContinuation Pause()
        {
            return null;
        }

        /// <summary>
        /// Convert an array of doubles to the current best network.
        /// </summary>
        ///
        /// <param name="array">An array.</param>
        public void PutArray(double[] array)
        {
            NetworkCODEC.ArrayToNetwork(array,
                                        _network);
        }


        public double[] GetArray()
        {
            return NetworkCODEC.NetworkToArray(_network);
        }

        double[] ValueBeforeRandomization;
        int LastD;
        /// <summary>
        /// Randomize the weights and bias values. This function does most of the
        /// work of the class. Each call to this class will randomize the data
        /// according to the current temperature. The higher the temperature the more
        /// randomness.
        /// </summary>
        ///
        public double Randomize(int d, double lowerBound, double upperBound)
        {
            //double[] array = NetworkCODEC
            //    .NetworkToArray(_network);

            //for (int i = 0; i < array.Length; i++)
            //{
            //    double add = _anneal.CurrentStepSize * (2.0 * ThreadSafeRandom.NextDouble() - 1.0);
            //    array[i] = array[i] + add;
            //}

            //NetworkCODEC.ArrayToNetwork(array,
            //                            _network);
            if (ValueBeforeRandomization == null)
                ValueBeforeRandomization = new double[((BasicNetwork)_network).Structure.Flat.Weights.Length];
            ValueBeforeRandomization[d] = ((BasicNetwork)_network).Structure.Flat.Weights[d];
            double add = _anneal.MoveSizeVM[d] * (2.0 * ThreadSafeRandom.NextDouble() - 1.0);
            double newVal = ((BasicNetwork)_network).Structure.Flat.Weights[d] = ValueBeforeRandomization[d] + add;
            if (newVal < lowerBound || newVal > upperBound)
                newVal = ((BasicNetwork)_network).Structure.Flat.Weights[d] = lowerBound + ThreadSafeRandom.NextDouble() * (upperBound - lowerBound);
            LastD = d;
            return newVal;
        }


        public void UndoLastRandomize(bool oneValueOnly)
        {
            if (oneValueOnly)
                ((BasicNetwork)_network).Structure.Flat.Weights[LastD] = ValueBeforeRandomization[LastD];
            else for (int d = 0; d < ValueBeforeRandomization.Length; d++)
                    ((BasicNetwork)_network).Structure.Flat.Weights[d] = ValueBeforeRandomization[d];
        }

        /// <inheritdoc/>
        public override void Resume(TrainingContinuation state)
        {
        }
    }
}
