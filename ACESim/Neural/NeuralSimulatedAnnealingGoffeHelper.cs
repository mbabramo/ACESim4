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
using System;
using Encog.ML.Anneal;
using Encog.MathUtil;
using ACESim;

namespace Encog.Neural.Networks.Training.Anneal
{
    /// <summary>
    /// Simple class used by the neural simulated annealing. This class is a subclass
    /// of the basic SimulatedAnnealingGoffe class. The It is used by the actual
    /// NeuralSimulatedAnnealingGoffe class, which subclasses BasicTraining. This class is
    /// mostly necessary due to the fact that NeuralSimulatedAnnealingGoffe can't subclass
    /// BOTH SimulatedAnnealingGoffe and Train, because multiple inheritance is not
    /// supported.
    /// </summary>
    ///
    public class NeuralSimulatedAnnealingGoffeHelper : SimulatedAnnealingGoffe<Double>
    {
        /// <summary>
        /// The class that this class should report to.
        /// </summary>
        ///
        private readonly NeuralSimulatedAnnealingGoffe _owner;

        /// <summary>
        /// Constructs this object.
        /// </summary>
        ///
        /// <param name="owner">The owner of this class, that recieves all messages.</param>
        public NeuralSimulatedAnnealingGoffeHelper(NeuralSimulatedAnnealingGoffe owner)
        {
            _owner = owner;
            ShouldMinimize = _owner.CalculateScore.ShouldMinimize;
        }

        /// <summary>
        /// Used to pass the getArray call on to the parent object.
        /// </summary>
        public override double[] Array
        {
            get { return _owner.Array; }
        }

        /// <summary>
        /// Used to pass the getArrayCopy call on to the parent object.
        /// </summary>
        ///
        /// <value>The array copy created by the owner.</value>
        public override double[] ArrayCopy
        {
            get { return _owner.ArrayCopy; }
        }

        public override void PerformCalculateScore(int dataSample, StatCollector s)
        {
             if (_owner.CalculateScore is ICalculateScoreForSpecificIteration)
            {
                ICalculateScoreForSpecificIteration specificIterationScorer = (ICalculateScoreForSpecificIteration)_owner.CalculateScore;
                specificIterationScorer.CalculateScoreForSpecificDataSample(((BasicNetwork)_owner.Method), dataSample, s);
            }
            else
                 throw new Exception();
        }


        /// <summary>
        /// Used to pass the putArray call on to the parent object.
        /// </summary>
        ///
        /// <param name="array">The array.</param>
        public override sealed void PutArray(double[] array)
        {
            _owner.PutArray(array);
        }


        public override double[] GetArray()
        {
            return _owner.GetArray();
        }

        /// <summary>
        /// Call the owner's randomize method.
        /// </summary>
        ///
        public override sealed double Randomize(int i, double lowerBound, double upperBound)
        {
            return _owner.Randomize(i, lowerBound, upperBound);
        }

        public override sealed void UndoLastRandomize(bool oneValueOnly)
        {
            base.UndoLastRandomize(oneValueOnly);
            _owner.UndoLastRandomize(oneValueOnly);
        }
    }
}
