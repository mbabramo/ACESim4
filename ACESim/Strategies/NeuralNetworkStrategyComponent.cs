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
using Encog.Util.Normalize;
using Encog.Util.Normalize.Input;
using Encog.Util.Normalize.Output;
using Encog.Neural.NeuralData;
using System.Threading;
using Encog.Neural.Networks.Training.Genetic;
using Encog.Mathutil.Randomize;
using Encog.MathUtil.Randomize;
using Encog.Engine.Network.Activation;

namespace ACESim
{
    [Serializable]
    public class NeuralNetworkStrategyComponent : IStrategyComponent
    {

        public Decision Decision
        {
            get;
            set;
        }

        public int Dimensions
        {
            get;
            set;
        }

        public EvolutionSettings EvolutionSettings
        {
            get;
            set;
        }

        public bool InitialDevelopmentCompleted
        {
            get;
            set;
        }

        public string Name
        {
            get;
            set;
        }

        public Strategy OverallStrategy
        {
            get;
            set;
        }

        public NeuralNetworkWithoutPresmoothingOptions NeuralNetworkWithoutPresmoothingOptions
        {
            get { return (NeuralNetworkWithoutPresmoothingOptions) EvolutionSettings.SmoothingOptions; }
        }


        public bool _isCurrentlyBeingSmoothed = false;
        public bool IsCurrentlyBeingDeveloped { get { return _isCurrentlyBeingSmoothed; } set { _isCurrentlyBeingSmoothed = value; } }
        
        public List<double> InputAverages, InputStdevs, InputMin, InputMax;
        public int CurrentIteration;
        public int NumberPlaysPerScore = 10000;
        public NeuralNetworkDynamicDataProcessor DataProcessor;
        public NeuralNetworkWrapper Wrapper;
        [NonSerialized]
		[System.Xml.Serialization.XmlIgnore]
        public NeuralDecisionScorer Scorer;
        public TrainingInfoForSimulatedAnnealing TrainingInfo;
        const int theoreticalTotalIterations = 100000000;


        public NeuralNetworkStrategyComponent(Strategy overallStrategy, int dimensions, EvolutionSettings evolutionSettings, Decision decision, string name)
        {
            OverallStrategy = overallStrategy;
            Dimensions = dimensions;
            EvolutionSettings = evolutionSettings;
            Decision = decision;
            InitialDevelopmentCompleted = false;
            Name = name;
        }

        private void CalculateInputStatistics()
        {
            List<double>[] inputsArray = new List<double>[1000];
            StatCollectorArray sca = new StatCollectorArray();
            for (int i = 0; i < 1000; i++)
            {
                bool decisionReached;
                GameProgress preplayedGameProgressInfo;
                inputsArray[i] = OverallStrategy.GetDecisionInputsForIteration(new IterationID(i), 1000, null, out decisionReached, out preplayedGameProgressInfo);
                sca.Add(inputsArray[i].ToArray());
            }
            InputAverages = sca.Average();
            InputStdevs = sca.StandardDeviation();
            InputMin = new List<double>();
            InputMax = new List<double>();
            for (int d = 0; d < Dimensions; d++)
            {
                InputMin.Add(InputAverages[d] - 6.0 * InputStdevs[d]); // we'll only calculate up to 6 standard deviations; beyond that will be set to the edge
                InputMax.Add(InputAverages[d] + 6.0 * InputStdevs[d]);
            }
        }

        public IStrategyComponent DeepCopy()
        {
            NeuralNetworkStrategyComponent copy = new NeuralNetworkStrategyComponent(OverallStrategy, Dimensions, EvolutionSettings, Decision, Name);
            copy.InputAverages = InputAverages.ToList();
            copy.InputStdevs = InputStdevs.ToList();
            copy.InputMin = InputMin.ToList();
            copy.InputMax = InputMax.ToList();
            copy.CurrentIteration = 0;
            copy.NumberPlaysPerScore = NumberPlaysPerScore;
            copy.DataProcessor = (NeuralNetworkDynamicDataProcessor) DataProcessor.DeepCopy();
            copy.Wrapper = Wrapper.DeepCopy();
            copy.TrainingInfo = null; /* we don't copy the training info */

            return copy;
        }

        public void DevelopStrategyComponent()
        {
            IsCurrentlyBeingDeveloped = true;
            CurrentIteration = 0;
            CalculateInputStatistics();
            SimulatedAnnealing();
            InitialDevelopmentCompleted = true;
            IsCurrentlyBeingDeveloped = false;
        }

        private void SimulatedAnnealing()
        {
            RandomizeIterations();
            Scorer = new NeuralDecisionScorer(Decision.HighestIsBest, this);
            DataProcessor = new NeuralNetworkDynamicDataProcessor(Dimensions, InputMin.ToArray(), InputMax.ToArray(), Decision.StrategyBounds.LowerBound, Decision.StrategyBounds.UpperBound, Scorer);
            TrainingInfo = new TrainingInfoForSimulatedAnnealing(10.0, 2.0, ((NeuralNetworkWithoutPresmoothingOptions)EvolutionSettings.SmoothingOptions).Epochs);
            Wrapper = new NeuralNetworkWrapper(DataProcessor, null, NeuralNetworkWithoutPresmoothingOptions.FirstHiddenLayerNeurons, NeuralNetworkWithoutPresmoothingOptions.SecondHiddenLayerNeurons, TrainingInfo);
        }


        //private void GeneticAlgorithmTraining()
        //{
        //    bool useThreadLocalScoresOriginal = OverallStrategy.UseThreadLocalScores;
        //    OverallStrategy.UseThreadLocalScores = true; // we need to keep track of scores separately for each thread, since we will be simultaneously calculating scores for different smoothing points
            
        //    //NeuralGeneticAlgorithm train = new NeuralGeneticAlgorithm(Network, new RangeRandomizer(-1, 1) /* FanInRandomizer() */, debugScoring /* new NeuralScore(Decision.HighestIsBest, this) */, 500, 0.1, 0.25);
        //    ClearEpochInformation();
        //    OverallStrategy.UseThreadLocalScores = useThreadLocalScoresOriginal;
        //}

        [NonSerialized]
		[System.Xml.Serialization.XmlIgnore]
        private List<long> IterationsForEpoch;
        [NonSerialized]
		[System.Xml.Serialization.XmlIgnore]
        private List<GameInputs> GameInputsSet;
        [NonSerialized]
		[System.Xml.Serialization.XmlIgnore]
        private List<GameProgress> PreplayedGames;
        [NonSerialized]
		[System.Xml.Serialization.XmlIgnore]
        private List<List<double>> DecisionInputs;
        public void RandomizeIterations()
        {
            RandomizeIterationsHelper(RandomGeneratorInstanceManager.Instance);
        }

        int LastSetOfIterationsNumber = -1;
        public void RandomizeIterationsInRepeatableWay(int setOfIterationsNumber)
        {
            if (setOfIterationsNumber != LastSetOfIterationsNumber)
            {
                Random newRandomInstance = new Random(setOfIterationsNumber);
                RandomizeIterationsHelper(newRandomInstance);
                LastSetOfIterationsNumber = setOfIterationsNumber;
            }
        }

        private void RandomizeIterationsHelper(Random instanceOfRandomToUse)
        {
            IterationsForEpoch = new List<long>();
            for (int i = 0; i < NumberPlaysPerScore; i++)
                IterationsForEpoch.Add(instanceOfRandomToUse.Next(theoreticalTotalIterations));
            OverallStrategy.GetListOfGameInputsAndPreplayedGamesForSpecificIterations(theoreticalTotalIterations, IterationsForEpoch.Select(x => new IterationID(x)).ToList(), out GameInputsSet, out PreplayedGames);
            DecisionInputs = OverallStrategy.GetDecisionInputsForSpecificIterations(IterationsForEpoch.Select(x => new IterationID(x)).ToList(), theoreticalTotalIterations);
            foreach (var inputs in DecisionInputs)
                MakeSureInputsAreInBounds(inputs);
        }

        public void ClearDecisionInputsInfo()
        {
            IterationsForEpoch = null;
            GameInputsSet = null;
            PreplayedGames = null;
            DecisionInputs = null;
        }

        int timesCalled = 0;
        public void CalculateScoreForNetworkBeingDeveloped(int setOfIterations, StatCollector stat)
        {
            RandomizeIterationsInRepeatableWay(setOfIterations); 
            List<double> valuesToPlay = new List<double>();
            for (int i = 0; i < NumberPlaysPerScore; i++)
            {
                double networkValue = DataProcessor.ComputeNetworkBeingDeveloped(DecisionInputs[i]);
                valuesToPlay.Add(networkValue);
            }
            double score = OverallStrategy.PlaySpecificValuesForSpecificIterations(IterationsForEpoch.Select(x => new IterationID(x)).ToList(), valuesToPlay, theoreticalTotalIterations, GameInputsSet, PreplayedGames, null);
            stat.Add(score);
        }

        public double CalculateOutputForInputs(List<double> inputs)
        {
            MakeSureInputsAreInBounds(inputs);
            return Wrapper.CalculateResult(inputs);
        }

        private void MakeSureInputsAreInBounds(List<double> inputs)
        {
            for (int d = 0; d < Dimensions; d++)
            {
                if (inputs[d] < InputMin[d])
                    inputs[d] = InputMin[d];
                if (inputs[d] > InputMax[d])
                    inputs[d] = InputMax[d];
            }
        }


        public virtual void PreSerialize()
        {
        }

        public virtual void UndoPreSerialize()
        {
        }
    }

    [Serializable]
    public class NeuralDecisionScorer : ICalculateScore, ICalculateScoreForSpecificIteration
    {
        public NeuralNetworkStrategyComponent StrategyComponent;

        public NeuralDecisionScorer(bool highestIsBest, NeuralNetworkStrategyComponent strategyComponent)
        {
            ShouldMinimize = !highestIsBest;
            StrategyComponent = strategyComponent;
        }

        bool _shouldMinimize;
        public bool ShouldMinimize
        {
            get { return _shouldMinimize; } set { _shouldMinimize = value; }
        }

        static int iterationsSet = 0;
        public double CalculateScore(IMLRegression network)
        {
            StatCollector stat = new StatCollector();
            Interlocked.Increment(ref iterationsSet);
            StrategyComponent.CalculateScoreForNetworkBeingDeveloped(iterationsSet, stat);
            return stat.Average();
        }

        public void CalculateScoreForSpecificDataSample(BasicNetwork network, int dataSample, StatCollector stats)
        {
            StrategyComponent.CalculateScoreForNetworkBeingDeveloped(dataSample, stats);
        }
    }
}
