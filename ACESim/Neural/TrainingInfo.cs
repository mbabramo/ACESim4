using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Encog.Util.Arrayutil;
using Encog.Neural.Networks;
using Encog.Neural.Networks.Layers;
using Encog.ML.Factory;
using Encog.ML.Data;
using Encog.ML.Data.Basic;
using Encog.ML.Train;
using Encog.Neural.Networks.Training.Propagation;
using Encog.Neural.Networks.Training.Propagation.Resilient;
using System.Diagnostics;
using Encog.Neural.Networks.Training.Anneal;
using Encog.Neural.Networks.Training;
using Encog.Neural.Pattern;
using Encog.Neural.RBF;
using Encog.Neural.RBF.Training;
using Encog.ML.Factory.Train;
using Encog.ML;
using Encog.Neural.Rbf.Training;
using Encog.Engine.Network.Activation;
using Encog.MathUtil.Randomize;
using Encog.Neural.Networks.Training.Genetic;
using Encog.Neural.Networks.Training.Propagation.Back;

namespace ACESim
{
    public enum TrainingTechnique
    {
        Backpropagation,
        ResilientPropagation,
        SVD,
        SimulatedAnnealing,
        SimulatedAnnealingGoffe,
        GeneticAlgorithm
    }

    [Serializable]
    public class TrainingInfo
    {
        public TrainingTechnique Technique;
        public int Epochs;
        public BasicNetwork Network;
        public int ValidateEveryNEpochs;

        public virtual IActivationFunction GetActivationFunction()
        {
            return new ActivationTANH();
        }

        public virtual void GetNormalizationRanges(out double hiInputsToActivationFunction, out double loInputsToActivationFunction, out double hiOutputsFromActivationFunction, out double loOutputsFromActivationFunction)
        {
            // we could have inputs from - to + infinity, but because the hyperbolic tangent function becomes relatively flat, this won't work well
            hiInputsToActivationFunction = 1.5;
            loInputsToActivationFunction = -1.5;
            // outputs will always be between 0 and 1.0 for the sigmoid function, -1.0 and 1.0 for tanh function
            hiOutputsFromActivationFunction = 1.0;
            loOutputsFromActivationFunction = -1.0;
        }

        public virtual IMLTrain GetTrainingMethodForTrainingSet(BasicNetwork network, BasicMLDataSet trainingSet)
        {
            Network = network;
            if (Technique == TrainingTechnique.Backpropagation)
            {
                var bp = new Backpropagation(Network, trainingSet);
                bp.LearningRate = 0.00001;
                return bp;
            }
            else
                return new ResilientPropagation(Network, trainingSet);
        }

        public virtual IMLTrain GetTrainingMethodForScoreCalculator(BasicNetwork network, ICalculateScore trainer)
        {
            throw new Exception("A score calculator cannot be used with this training method.");
        }

        public virtual BasicNetwork GetNetworkAfterTraining()
        {
            return Network;
        }

        public virtual void Train(IMLTrain train, Func<bool> validationOK)
        {
            bool lastValidationFailed = !validationOK();
            int epoch = 1;
            do
            {
                train.Iteration();
                if (epoch == 1 || epoch % (Epochs / 10) == 0)
                    Debug.WriteLine(@"Epoch #" + epoch + @" Error:" + train.Error);
                epoch++;
                if (ValidateEveryNEpochs == 0)
                    lastValidationFailed = false;
                else if (epoch % ValidateEveryNEpochs == 0)
                    lastValidationFailed = !validationOK();
            }
            while (epoch < Epochs && !lastValidationFailed);
        }
    }

    //[Serializable]
    //public class TrainingInfoForRBF : TrainingInfo
    //{

    //    public TrainingInfoForRBF()
    //    {
    //    }

    //    public override IActivationFunction GetActivationFunction()
    //    {
    //        return new ActivationGaussian();
    //    }


    //    public override void GetNormalizationRanges(out double hiInputsToActivationFunction, out double loInputsToActivationFunction, out double hiOutputsFromActivationFunction, out double loOutputsFromActivationFunction)
    //    {
    //        // we could have inputs from - to + infinity, but because the hyperbolic tangent function becomes relatively flat, this won't work well
    //        hiInputsToActivationFunction = 1.5;
    //        loInputsToActivationFunction = -1.5;
    //        // outputs will always be between 0 and 1.0 for the sigmoid function, -1.0 and 1.0 for tanh function
    //        hiOutputsFromActivationFunction = 1.0;
    //        loOutputsFromActivationFunction = 0;
    //    }


    //    public override IMLTrain GetTrainingMethodForTrainingSet(BasicNetwork network, BasicMLDataSet trainingSet)
    //    {
    //        Network = network;
    //        return new RBFSVDFactory().Create(network, trainingSet, "");
    //    }

    //    //public override IMLTrain GetTrainingMethodForTrainingSet(BasicNetwork network, BasicMLDataSet trainingSet)
    //    //{
    //    //    Network = network;
    //    //    ICalculateScore scorer2 = new TrainingSetScore(trainingSet);
    //    //    return new NeuralSimulatedAnnealingGoffe(network, scorer2, StartTemperature, TemperatureReductionEachCycle, Epochs);
    //    //}


    //    //public override IMLTrain GetTrainingMethodForScoreCalculator(BasicNetwork network, ICalculateScore trainer)
    //    //{
    //    //    Network = network;
    //    //    return new NeuralSimulatedAnnealingGoffe(network, trainer, StartTemperature, TemperatureReductionEachCycle, Epochs);
    //    //}

    //}


    [Serializable]
    public class TrainingInfoForSimulatedAnnealingGoffe : TrainingInfo
    {
        public double StartTemperature;
        public double TemperatureReductionEachCycle;

        public TrainingInfoForSimulatedAnnealingGoffe(double startTemperature, double temperatureReductionEachCycle, int cycles)
        {
            Technique = TrainingTechnique.SimulatedAnnealingGoffe;
            StartTemperature = startTemperature;
            TemperatureReductionEachCycle = temperatureReductionEachCycle;
            Epochs = cycles;
        }

        public override IActivationFunction GetActivationFunction()
        {
            return new ActivationSigmoid(); // ActivationTANH(); // ActivationLinear();
        }


        public override void GetNormalizationRanges(out double hiInputsToActivationFunction, out double loInputsToActivationFunction, out double hiOutputsFromActivationFunction, out double loOutputsFromActivationFunction)
        {
            // we could have inputs from - to + infinity, but because the hyperbolic tangent function becomes relatively flat, this won't work well
            hiInputsToActivationFunction = 1.5;
            loInputsToActivationFunction = -1.5;
            // outputs will always be between 0 and 1.0 for the sigmoid function, -1.0 and 1.0 for tanh function
            hiOutputsFromActivationFunction = 1.0;
            loOutputsFromActivationFunction = 0;
        }

        public override IMLTrain GetTrainingMethodForTrainingSet(BasicNetwork network, BasicMLDataSet trainingSet)
        {
            Network = network;
            ICalculateScore scorer2 = new TrainingSetScore(trainingSet);
            return new NeuralSimulatedAnnealingGoffe(network, scorer2, StartTemperature, TemperatureReductionEachCycle, Epochs);
        }


        public override IMLTrain GetTrainingMethodForScoreCalculator(BasicNetwork network, ICalculateScore trainer)
        {
            Network = network;
            return new NeuralSimulatedAnnealingGoffe(network, trainer, StartTemperature, TemperatureReductionEachCycle, Epochs);
        }

        public override void Train(IMLTrain train, Func<bool> validationOK)
        {
            train.Iteration(); // just one iteration since we are doing epochs within the iteration algorithm.
        }
    }


    [Serializable]
    public class TrainingInfoForSimulatedAnnealingWithPresetMoveSize : TrainingInfo
    {
        public double StartMoveSize;
        public double StopMoveSize;
        public double CurvatureToEndMoveSize;

        public TrainingInfoForSimulatedAnnealingWithPresetMoveSize(double startingMoveSize, double endingMoveSize, double curvatureToEndMoveSize, int cycles)
        {
            Technique = TrainingTechnique.SimulatedAnnealingGoffe;
            StartMoveSize = startingMoveSize;
            StopMoveSize = endingMoveSize;
            CurvatureToEndMoveSize = curvatureToEndMoveSize;
            Epochs = cycles;
        }

        public override IActivationFunction GetActivationFunction()
        {
            return new ActivationSigmoid(); // ActivationTANH(); // ActivationLinear();
        }


        public override void GetNormalizationRanges(out double hiInputsToActivationFunction, out double loInputsToActivationFunction, out double hiOutputsFromActivationFunction, out double loOutputsFromActivationFunction)
        {
            // we could have inputs from - to + infinity, but because the hyperbolic tangent function becomes relatively flat, this won't work well
            hiInputsToActivationFunction = 1.5;
            loInputsToActivationFunction = -1.5;
            // outputs will always be between 0 and 1.0 for the sigmoid function, -1.0 and 1.0 for tanh function
            hiOutputsFromActivationFunction = 1.0;
            loOutputsFromActivationFunction = 0;
        }

        public override IMLTrain GetTrainingMethodForTrainingSet(BasicNetwork network, BasicMLDataSet trainingSet)
        {
            Network = network;
            ICalculateScore scorer2 = new TrainingSetScore(trainingSet);
            return new NeuralSimulatedAnnealingGoffe(network, scorer2, StartMoveSize, StopMoveSize, CurvatureToEndMoveSize, Epochs);
        }


        public override IMLTrain GetTrainingMethodForScoreCalculator(BasicNetwork network, ICalculateScore trainer)
        {
            Network = network;
            return new NeuralSimulatedAnnealingGoffe(network, trainer, StartMoveSize, StopMoveSize, CurvatureToEndMoveSize, Epochs);
        }

        public override void Train(IMLTrain train, Func<bool> validationOK)
        {
            train.Iteration(); // just one iteration since we are doing epochs within the iteration algorithm.
            Debug.WriteLine("Annealing error: " + train.Error);
        }
    }

    [Serializable]
    public class TrainingInfoForSimulatedAnnealing : TrainingInfo
    {
        public double StartTemperature;
        public double StopTemperature;

        public TrainingInfoForSimulatedAnnealing(double startTemperature, double stopTemperature, int cycles)
        {
            Technique = TrainingTechnique.SimulatedAnnealing;
            StartTemperature = startTemperature;
            StopTemperature = stopTemperature;
            Epochs = cycles;
        }

        public override IActivationFunction GetActivationFunction()
        {
            return new ActivationSigmoid(); // ActivationTANH(); // ActivationLinear();
        }

        public override void GetNormalizationRanges(out double hiInputsToActivationFunction, out double loInputsToActivationFunction, out double hiOutputsFromActivationFunction, out double loOutputsFromActivationFunction)
        {
            // we could have inputs from - to + infinity, but because the hyperbolic tangent function becomes relatively flat, this won't work well
            hiInputsToActivationFunction = 1.5;
            loInputsToActivationFunction = -1.5;
            // outputs will always be between 0 and 1.0 for the sigmoid function, -1.0 and 1.0 for tanh function
            hiOutputsFromActivationFunction = 1.0;
            loOutputsFromActivationFunction = 0;
        }

        public override IMLTrain GetTrainingMethodForTrainingSet(BasicNetwork network, BasicMLDataSet trainingSet)
        {
            Network = network;
            ICalculateScore scorer2 = new TrainingSetScore(trainingSet); 
            return new NeuralSimulatedAnnealing(network, scorer2, StartTemperature, StopTemperature, Epochs);
        }

        public override IMLTrain GetTrainingMethodForScoreCalculator(BasicNetwork network, ICalculateScore trainer)
        {
            Network = network;
            return new NeuralSimulatedAnnealing(network, trainer, StartTemperature, StopTemperature, Epochs);
        }

        public override BasicNetwork GetNetworkAfterTraining()
        {
            return Network;
        }
        public override void Train(IMLTrain train, Func<bool> validationOK)
        {
            train.Iteration(); // just one iteration since we are doing epochs within the iteration algorithm.
            Debug.WriteLine("Annealing error: " + train.Error);
        }
    }

    [Serializable]
    public class TrainingInfoForGeneticAlgorithm : TrainingInfo
    {
        public int PopulationSize;
        public double MutationPercent;
        public double PercentToMate;
        NeuralGeneticAlgorithm Algorithm;

        public TrainingInfoForGeneticAlgorithm(int populationSize, double mutationPercent, double percentToMate, int epochs)
        {
            Technique = TrainingTechnique.GeneticAlgorithm;
            PopulationSize = populationSize;
            MutationPercent = mutationPercent;
            PercentToMate = percentToMate;
            Epochs = epochs;
        }

        public override IActivationFunction GetActivationFunction()
        {
            return new ActivationSigmoid(); // ActivationTANH(); // ActivationLinear();
        }

        public override void GetNormalizationRanges(out double hiInputsToActivationFunction, out double loInputsToActivationFunction, out double hiOutputsFromActivationFunction, out double loOutputsFromActivationFunction)
        {
            // we could have inputs from - to + infinity, but because the hyperbolic tangent function becomes relatively flat, this won't work well
            hiInputsToActivationFunction = 1.5;
            loInputsToActivationFunction = -1.5;
            // outputs will always be between 0 and 1.0 for this function
            hiOutputsFromActivationFunction = 1.0;
            loOutputsFromActivationFunction = 0;
        }

        public override IMLTrain GetTrainingMethodForTrainingSet(BasicNetwork network, BasicMLDataSet trainingSet)
        {
            ICalculateScore scorer3 = new TrainingSetScore(trainingSet);
            Algorithm = new NeuralGeneticAlgorithm(network, new RangeRandomizer(-1, 1), scorer3, PopulationSize, MutationPercent, PercentToMate);
            return Algorithm;
        }

        public override BasicNetwork GetNetworkAfterTraining()
        {
            return (BasicNetwork)Algorithm.Genetic.Population.Best.Organism;
        }

        public override void Train(IMLTrain train, Func<bool> validationOK)
        {
            base.Train(train, validationOK);
            Debug.WriteLine("Genetic algorithm error: " + train.Error);
        }
    }

}
