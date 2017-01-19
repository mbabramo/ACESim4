using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using ACESim.Util;

namespace ACESim
{
    [Serializable]
    public class DirectScoreEstimation : IStrategyComponent
    {
        public Decision Decision { get; set; }
        public int Dimensions { get; set; }
        public EvolutionSettings EvolutionSettings { get { return OverallStrategy.EvolutionSettings; } set { OverallStrategy.EvolutionSettings = value; } }
        public bool InitialDevelopmentCompleted { get; set; }
        internal bool _isCurrentlyBeingDeveloped = false;
        public bool IsCurrentlyBeingDeveloped { get { return _isCurrentlyBeingDeveloped; } set { _isCurrentlyBeingDeveloped = value; } }
        public string Name { get; set; }
        public Strategy OverallStrategy { get; set; }
        public DirectScoreEstimationSmoothingOptions DirectScoreEstimationSmoothingOptions 
        { 
            get 
            { 
                if (EvolutionSettings.SmoothingOptions is DirectScoreEstimationSmoothingOptions)
                    return (DirectScoreEstimationSmoothingOptions)(EvolutionSettings.SmoothingOptions);
                return (DirectScoreEstimationSmoothingOptions)(EvolutionSettings.DecisionRepresentsCorrectAnswerSmoothingOptions);
            } 
        }

        internal int PassNumber;
        internal double CurrentWeightOnRandomStrategy;
        internal double LowerBound, UpperBound;

        TrainingTechnique TrainingTechniqueToUse = TrainingTechnique.Backpropagation;
        const bool continueTrainingFromWhereLeftOff = true;
        const bool alwaysKeepSameValidationSet = false;
        const double probabilityUseTotallyRandomOutput = 0.5;

        internal class DatumForScoreEstimationNetwork
        {
            public List<double> DecisionInputs;
            public double OutputAttempted;
            public double Score;

            public double[] GetInputsForScoreEstimationNetwork(bool scoreRepresentsCorrectAnswer)
            {
                List<double> temp = DecisionInputs.ToList();
                if (!scoreRepresentsCorrectAnswer)
                    temp.Add(OutputAttempted);
                return temp.ToArray();
            }
        }
        internal ConcurrentBag<DatumForScoreEstimationNetwork> DataForScoreEstimationNetwork;
        internal class DatumForOutputEstimationNetwork
        {
            public List<double> DecisionInputs;
            public double OptimalOutput;

            public double[] GetInputsForOutputEstimationNetwork()
            {
                return DecisionInputs.ToArray();
            }
        }
        internal ConcurrentBag<DatumForOutputEstimationNetwork> DataForOutputEstimationNetwork;

        public NeuralNetworkWrapper ScoreEstimationNetwork;
        public NeuralNetworkWrapper OutputEstimationNetwork;

        public DirectScoreEstimation(Strategy overallStrategy, int dimensions, EvolutionSettings evolutionSettings, Decision decision, string name)
        {
            OverallStrategy = overallStrategy;
            Dimensions = dimensions;
            EvolutionSettings = evolutionSettings;
            Decision = decision;
            InitialDevelopmentCompleted = false;
            Name = name;
        }

        public DirectScoreEstimation() // paramaterless constructor needed for deep copy
        {
        }

        public virtual IStrategyComponent DeepCopy()
        {
            DirectScoreEstimation copy = new DirectScoreEstimation()
            {
            };
            SetCopyFields(copy);
            return copy;
        }

        public virtual void SetCopyFields(IStrategyComponent copy)
        {
            DirectScoreEstimation copyCast = (DirectScoreEstimation)copy;

            copyCast.OverallStrategy = OverallStrategy;
            copyCast.Dimensions = Dimensions;
            copyCast.EvolutionSettings = EvolutionSettings;
            copyCast.Decision = Decision;
            copyCast.InitialDevelopmentCompleted = InitialDevelopmentCompleted;
            copyCast.IsCurrentlyBeingDeveloped = IsCurrentlyBeingDeveloped;
        }

        public void DevelopStrategyComponent()
        {
            IsCurrentlyBeingDeveloped = true;
            ScoreEstimationNetwork = null;
            OutputEstimationNetwork = null;
            CurrentWeightOnRandomStrategy = 1.0;
            for (PassNumber = 1; PassNumber <= DirectScoreEstimationSmoothingOptions.NumRefinementPasses; PassNumber++ )
            {
                if (PassNumber == 2)
                    CurrentWeightOnRandomStrategy = DirectScoreEstimationSmoothingOptions.InitialWeightOnRandomStrategy;
                else if (PassNumber > 2)
                    CurrentWeightOnRandomStrategy *= DirectScoreEstimationSmoothingOptions.WeightOnRandomStrategyMultiplier;
                int totalRepetitions = 1;
                const double maxWeightToPlaceOnIndividualScoreEstimate = 1.0;
                const double minWeightToPlaceOnIndividualScoreEstimate = 0.50;
                for (int repetition = 0; repetition < totalRepetitions; repetition++)
                {
                    double weightToPlaceOnIndividualScoreEstimate = (totalRepetitions == 1) ? 0 : maxWeightToPlaceOnIndividualScoreEstimate - (repetition / (totalRepetitions - 1)) * (maxWeightToPlaceOnIndividualScoreEstimate - minWeightToPlaceOnIndividualScoreEstimate);
                    CreateScoreEstimationNetwork(weightToPlaceOnIndividualScoreEstimate);
                    if (Decision.Name == "ObfuscationDecision")
                        AssessAccuracyForObfuscationGame_ScoreEstimationNetwork();
                }
                if (!OverallStrategy.Decision.ScoreRepresentsCorrectAnswer)
                   CreateOutputEstimationNetwork();
                if (Decision.Name == "ObfuscationDecision")
                    AssessAccuracyForObfuscationGame();
            }
            InitialDevelopmentCompleted = true;
            IsCurrentlyBeingDeveloped = false;
        }

        private void GetDataForScoreEstimationNetwork(double weightToPlaceOnIndividualScoreEstimate)
        {
            LowerBound = OverallStrategy.Decision.StrategyBounds.LowerBound;
            UpperBound = OverallStrategy.Decision.StrategyBounds.UpperBound;
            bool useThreadLocalScoresOriginal = OverallStrategy.UseThreadLocalScores;
            OverallStrategy.UseThreadLocalScores = true;
            OversamplingInfo oversamplingInfo = new OversamplingInfo() { OversamplingPlan = OverallStrategy.OversamplingPlanDuringOptimization, StoreInputSeedsForImprovementOfOversamplingPlan = false, StoreWeightsForAdjustmentOfScoreAverages = false }; // we don't need the input seeds or weights
            DataForScoreEstimationNetwork = new ConcurrentBag<DatumForScoreEstimationNetwork>();
            int combinedIterations = DirectScoreEstimationSmoothingOptions.NumIterationsScoreEstimationNetworkMainSet + DirectScoreEstimationSmoothingOptions.NumIterationsScoreEstimationNetworkValidationSet;
            Parallelizer.GoForSpecifiedNumberOfSuccesses(EvolutionSettings.ParallelOptimization, combinedIterations, (successNumber, iterationNumber) =>
            {
                bool decisionReached;
                GameProgress preplayedGameProgressInfo;
                IterationID id = OverallStrategy.GenerateIterationID(iterationNumber);
                List<double> inputs = OverallStrategy.GetDecisionInputsForIteration(id, combinedIterations, oversamplingInfo, out decisionReached, out preplayedGameProgressInfo);
                if (decisionReached)
                {
                    double outputToUse = GetQuasiRandomOutput(inputs, iterationNumber);
                    if (OverallStrategy.Decision.ScoreRepresentsCorrectAnswer)
                        outputToUse = (LowerBound + UpperBound) / 2.0; // we're not really using an output here (this will be ignored when creating the score estimation network).
                    // Note: The follow is inefficient because we're going to replay the game. If we start using this regularly, we should add support at the Strategy level for getting decision inputs along with preplayed games and then here we can complete a game by just playing one value. One complication is that this may be just one input group, but we don't need to support multiple input groups for direct score estimation, so we could add a check for that.
                    //double correctAnswer = ObfuscationGame.ObfuscationCorrectAnswer.Calculate(inputs[1], inputs[0]);
                    //double noise = 0.2 * (RandomGenerator.NextDouble() - 0.5);
                    //double score = Math.Abs(correctAnswer - outputToUse) + noise;
                    double score = OverallStrategy.PlaySpecificValueForSomeIterations(outputToUse, new List<IterationID>() { id }, combinedIterations, oversamplingInfo);

                    if (ScoreEstimationNetwork != null && weightToPlaceOnIndividualScoreEstimate != 1.0)
                    { // produce what we think will be the score on average by relying mostly on the existing network and adjusting only slightly based on this one
                        List<double> inputsPlusOutput = inputs.ToList();
                        if (!OverallStrategy.Decision.ScoreRepresentsCorrectAnswer)
                            inputsPlusOutput.Add(outputToUse);
                        double scoreBasedOnPreviousIncarnationOfNetwork = ScoreEstimationNetwork.CalculateResult(inputsPlusOutput);
                        if (weightToPlaceOnIndividualScoreEstimate == 0)
                            score = scoreBasedOnPreviousIncarnationOfNetwork;
                        else if (weightToPlaceOnIndividualScoreEstimate == 1.0)
                            ;
                        else
                            score = (1.0 - weightToPlaceOnIndividualScoreEstimate) * scoreBasedOnPreviousIncarnationOfNetwork + weightToPlaceOnIndividualScoreEstimate * score;
                    }
                    DatumForScoreEstimationNetwork datum = new DatumForScoreEstimationNetwork() { DecisionInputs = inputs, OutputAttempted = outputToUse, Score = score };
                    DataForScoreEstimationNetwork.Add(datum);
                    return true;
                }
                return false;
            });
            OverallStrategy.UseThreadLocalScores = useThreadLocalScoresOriginal;
        }

        NeuralNetworkTrainingData validationDataScoreEstimationNetwork = null;
        private void CreateScoreEstimationNetwork(double weightToPlaceOnIndividualScoreEstimate)
        {
            bool scoreRepresentsCorrectAnswer = OverallStrategy.Decision.ScoreRepresentsCorrectAnswer;
            GetDataForScoreEstimationNetwork(weightToPlaceOnIndividualScoreEstimate);
            List<DatumForScoreEstimationNetwork> dataScoreEstimationList = DataForScoreEstimationNetwork.ToList();
            DataForScoreEstimationNetwork = null;
            List<DatumForScoreEstimationNetwork> dataScoreMainSet = dataScoreEstimationList.Take(DirectScoreEstimationSmoothingOptions.NumIterationsScoreEstimationNetworkMainSet).ToList();
            List<DatumForScoreEstimationNetwork> dataScoreValidationSet = dataScoreEstimationList.Skip(DirectScoreEstimationSmoothingOptions.NumIterationsScoreEstimationNetworkMainSet).ToList();
            dataScoreEstimationList = null;
            double[][] arrayFormInputsScoreEstimationNetwork = dataScoreMainSet.Select(x => x.GetInputsForScoreEstimationNetwork(scoreRepresentsCorrectAnswer)).ToArray();
            double[][] arrayFormOutputsForScoreEstimationNetwork = dataScoreMainSet.Select(x => new double[] { x.Score }).ToArray();
            dataScoreMainSet = null;
            NeuralNetworkTrainingData trainingData = new NeuralNetworkTrainingData(arrayFormInputsScoreEstimationNetwork[0].Length, arrayFormInputsScoreEstimationNetwork, arrayFormOutputsForScoreEstimationNetwork);
            if (validationDataScoreEstimationNetwork == null || (!continueTrainingFromWhereLeftOff && !alwaysKeepSameValidationSet))
            {
                double[][] arrayFormInputsScoreEstimationNetworkValidation = dataScoreValidationSet.Select(x => x.GetInputsForScoreEstimationNetwork(scoreRepresentsCorrectAnswer)).ToArray();
                double[][] arrayFormOutputsForScoreEstimationNetworkValidation = dataScoreValidationSet.Select(x => new double[] { x.Score }).ToArray();
                dataScoreValidationSet = null;
                validationDataScoreEstimationNetwork = new NeuralNetworkTrainingData(arrayFormInputsScoreEstimationNetworkValidation[0].Length, arrayFormInputsScoreEstimationNetworkValidation, arrayFormOutputsForScoreEstimationNetworkValidation);
            }
            TrainingInfo trainingInfo = new TrainingInfo() { Technique = TrainingTechniqueToUse, Epochs = DirectScoreEstimationSmoothingOptions.EpochsScoreEstimationNetwork, ValidateEveryNEpochs = DirectScoreEstimationSmoothingOptions.ValidateEveryNEpochsScoreEstimationNetwork };
            ScoreEstimationNetwork = new NeuralNetworkWrapper(trainingData, validationDataScoreEstimationNetwork, DirectScoreEstimationSmoothingOptions.HiddenLayerSizeScoreEstimationNetwork, 0, trainingInfo, false); 
            if (ScoreEstimationNetwork == null || !continueTrainingFromWhereLeftOff)
                ScoreEstimationNetwork = new NeuralNetworkWrapper(trainingData, validationDataScoreEstimationNetwork, DirectScoreEstimationSmoothingOptions.HiddenLayerSizeScoreEstimationNetwork, 0, trainingInfo);
            else
                ScoreEstimationNetwork.ContinueTrainingWithNewData(arrayFormInputsScoreEstimationNetwork, arrayFormOutputsForScoreEstimationNetwork);
            
            // TODO: Generalize this so it will work after everything is evolved by generating new points
            if (scoreRepresentsCorrectAnswer && arrayFormInputsScoreEstimationNetwork[0].Length == 1)
            {
                bool automaticallyGeneratePlotsDefaultSetting = DirectScoreEstimationSmoothingOptions.AutomaticallyGeneratePlots;
                bool automaticallyGeneratePlots = (automaticallyGeneratePlotsDefaultSetting && !OverallStrategy.Decision.DontAutomaticallyGeneratePlotRegardlessOfGeneralSetting) || (!automaticallyGeneratePlotsDefaultSetting && OverallStrategy.Decision.AutomaticallyGeneratePlotRegardlessOfGeneralSetting);
                if (automaticallyGeneratePlots)
                {
                    List<double[]> points = new List<double[]>();
                    for (int i = 0; i < arrayFormInputsScoreEstimationNetwork.Length; i++)
                    {
                        double xVal = arrayFormInputsScoreEstimationNetwork[i][0];
                        double yVal = CalculateOutputForInputs(new List<double> { arrayFormInputsScoreEstimationNetwork[i][0] });
                        points.Add(new double[] { xVal, yVal });
                    }
                    OverallStrategy.SimulationInteraction.Create2DPlot(points.OrderBy(x => x[0]).ToList(), new Graph2DSettings() { graphName = OverallStrategy.Decision.Name, seriesName = "Direct estimation", xAxisLabel ="Input into decision", yAxisLabel="Decision output" }, "");
                }
            }
        }

        private void GetDataForOutputEstimationNetwork()
        {
            DataForOutputEstimationNetwork = new ConcurrentBag<DatumForOutputEstimationNetwork>();
            int combinedIterations = DirectScoreEstimationSmoothingOptions.NumIterationsOutputEstimationNetworkMainSet + DirectScoreEstimationSmoothingOptions.NumIterationsOutputEstimationNetworkValidationSet;
            OversamplingInfo oversamplingInfo = new OversamplingInfo() { OversamplingPlan = OverallStrategy.OversamplingPlanDuringOptimization, StoreInputSeedsForImprovementOfOversamplingPlan = false, StoreWeightsForAdjustmentOfScoreAverages = false }; // we don't need the input seeds or weights
            double precision = (UpperBound - LowerBound) / 100000.0;
            bool highestIsBest = OverallStrategy.Decision.HighestIsBest;
            Parallelizer.GoForSpecifiedNumberOfSuccesses(EvolutionSettings.ParallelOptimization, combinedIterations, (successNumber, iterationNumber) =>
            {
                // We want to have realistic decision inputs, so we do play the game up to the decision inputs, but after getting them, we can't optimize just by repeatedly playing the game, since we have just one iteration, so we use the score estimation network to figure out what seems to be an optimal score.
                bool decisionReached;
                GameProgress preplayedGameProgressInfo;
                IterationID id = OverallStrategy.GenerateIterationID(iterationNumber);
                List<double> inputs = OverallStrategy.GetDecisionInputsForIteration(id, combinedIterations, oversamplingInfo, out decisionReached, out preplayedGameProgressInfo);
                if (decisionReached)
                {
                    double lowerBound, upperBound;
                    double optimalOutputBasedOnScoreEstimationNetwork;
                    //if (OverallStrategy.Decision.ScoreRepresentsCorrectAnswer)
                    //{
                    //    List<double> inputsPlusPossibleOutput = inputs.ToList();
                    //    inputsPlusPossibleOutput.Add((LowerBound + UpperBound) / 2.0); // this will be the same for all observations and thus ignored
                    //    optimalOutputBasedOnScoreEstimationNetwork = ScoreEstimationNetwork.CalculateResult(inputsPlusPossibleOutput);
                    //}
                    //else 
                    if (OverallStrategy.Decision.Bipolar)
                    {
                        List<double> inputsPlusPossibleOutput = inputs.ToList();
                        inputsPlusPossibleOutput.Add(1.0);
                        double resultForPositive = ScoreEstimationNetwork.CalculateResult(inputsPlusPossibleOutput);
                        inputsPlusPossibleOutput = inputs.ToList();
                        inputsPlusPossibleOutput.Add(-1.0);
                        double resultForNegative = ScoreEstimationNetwork.CalculateResult(inputsPlusPossibleOutput);
                        if (highestIsBest)
                            optimalOutputBasedOnScoreEstimationNetwork = (resultForPositive > resultForNegative) ? 1.0 : -1.0;
                        else
                            optimalOutputBasedOnScoreEstimationNetwork = (resultForPositive > resultForNegative) ? -1.0 : 1.0;
                    }
                    else
                    {
                        GetRangeOfOutputValuesToConsider(inputs, out lowerBound, out upperBound);
                        optimalOutputBasedOnScoreEstimationNetwork = FindOptimalPoint.OptimizeByNarrowingRanges(lowerBound, upperBound, precision, possibleOutput =>
                        {
                            List<double> inputsPlusPossibleOutput = inputs.ToList();
                            inputsPlusPossibleOutput.Add(possibleOutput);
                            return ScoreEstimationNetwork.CalculateResult(inputsPlusPossibleOutput);
                        }, highestIsBest, 10, 10);
                    }
                    // optimalOutputBasedOnScoreEstimationNetwork = ObfuscationGame.ObfuscationCorrectAnswer.Calculate(inputs[1], inputs[0]); // uncomment to develop this based on correct answer for obfuscation game
                    DatumForOutputEstimationNetwork datum = new DatumForOutputEstimationNetwork() { DecisionInputs = inputs, OptimalOutput = optimalOutputBasedOnScoreEstimationNetwork };
                    DataForOutputEstimationNetwork.Add(datum);
                    return true;
                }
                return false;
            });
        }

        private void CreateOutputEstimationNetwork()
        {
            GetDataForOutputEstimationNetwork();
            List<DatumForOutputEstimationNetwork> dataOutputEstimationList = DataForOutputEstimationNetwork.ToList();
            DataForOutputEstimationNetwork = null;
            List<DatumForOutputEstimationNetwork> dataScoreMainSet = dataOutputEstimationList.Take(DirectScoreEstimationSmoothingOptions.NumIterationsOutputEstimationNetworkMainSet).ToList();
            List<DatumForOutputEstimationNetwork> dataScoreValidationSet = dataOutputEstimationList.Skip(DirectScoreEstimationSmoothingOptions.NumIterationsOutputEstimationNetworkMainSet).ToList();
            dataOutputEstimationList = null;
            double[][] arrayFormInputsOutputEstimationNetwork = dataScoreMainSet.Select(x => x.GetInputsForOutputEstimationNetwork()).ToArray();
            double[][] arrayFormOutputsForOutputEstimationNetwork = dataScoreMainSet.Select(x => new double[] { x.OptimalOutput }).ToArray();
            dataScoreMainSet = null;
            NeuralNetworkTrainingData trainingData = new NeuralNetworkTrainingData(arrayFormInputsOutputEstimationNetwork[0].Length, arrayFormInputsOutputEstimationNetwork, arrayFormOutputsForOutputEstimationNetwork);
            NeuralNetworkTrainingData validationData = null;
            if (ScoreEstimationNetwork == null || !continueTrainingFromWhereLeftOff)
            {
                arrayFormInputsOutputEstimationNetwork = dataScoreValidationSet.Select(x => x.GetInputsForOutputEstimationNetwork()).ToArray();
                arrayFormOutputsForOutputEstimationNetwork = dataScoreValidationSet.Select(x => new double[] { x.OptimalOutput }).ToArray();
                dataScoreValidationSet = null;
                validationData = new NeuralNetworkTrainingData(arrayFormInputsOutputEstimationNetwork[0].Length, arrayFormInputsOutputEstimationNetwork, arrayFormOutputsForOutputEstimationNetwork);
            }
            TrainingInfo trainingInfo = new TrainingInfo() { Technique = TrainingTechniqueToUse, Epochs = DirectScoreEstimationSmoothingOptions.EpochsOutputEstimationNetwork, ValidateEveryNEpochs = DirectScoreEstimationSmoothingOptions.ValidateEveryNEpochsOutputEstimationNetwork };
            if (OutputEstimationNetwork == null || !continueTrainingFromWhereLeftOff)
                OutputEstimationNetwork = new NeuralNetworkWrapper(trainingData, validationData, DirectScoreEstimationSmoothingOptions.HiddenLayerSizeOutputEstimationNetwork, 0, trainingInfo);
            else
                OutputEstimationNetwork.ContinueTrainingWithNewData(arrayFormInputsOutputEstimationNetwork, arrayFormOutputsForOutputEstimationNetwork);
        }

        private double GetQuasiRandomOutput(List<double> decisionInputs, int iterationNumber)
        {
            //double randomNumber = RandomGenerator.NextDouble(); 
            //if (OverallStrategy.Decision.Bipolar)
            //    return (randomNumber < 0.5) ? 1.0 : -1.0;
            //double weightOnRandomStrategy = RandomGenerator.NextDouble() < probabilityUseTotallyRandomOutput ? 1.0 : CurrentWeightOnRandomStrategy;
            //return GetQuasiRandomOutputGivenRandomNumber(decisionInputs, randomNumber, weightOnRandomStrategy);
            double randomNumber = FastPseudoRandom.GetRandom(iterationNumber, 37, 1321); // these numbers are arbitrary
            if (OverallStrategy.Decision.Bipolar)
                return (randomNumber < 0.5) ? 1.0 : -1.0;
            double weightOnRandomStrategy = FastPseudoRandom.GetRandom(iterationNumber, 57, 6421) < probabilityUseTotallyRandomOutput ? 1.0 : CurrentWeightOnRandomStrategy;
            return GetQuasiRandomOutputGivenRandomNumber(decisionInputs, randomNumber, weightOnRandomStrategy);
        }

        private double GetQuasiRandomOutputGivenRandomNumber(List<double> decisionInputs, double randomNumber, double weightOnRandomStrategy)
        {
            double randomStrategy = LowerBound + (UpperBound - LowerBound) * randomNumber;

            if (weightOnRandomStrategy == 1.0)
                return randomStrategy;

            double optimalStrategyFromLastPass = OutputEstimationNetwork.CalculateResult(decisionInputs);
            double weightedStrategy = CurrentWeightOnRandomStrategy * randomStrategy + (1.0 - CurrentWeightOnRandomStrategy) * optimalStrategyFromLastPass;
            return weightedStrategy;
        }

        private void GetRangeOfOutputValuesToConsider(List<double> decisionInputs, out double lowerBound, out double upperBound)
        {
            lowerBound = GetQuasiRandomOutputGivenRandomNumber(decisionInputs, 0.0, 1.0);
            upperBound = GetQuasiRandomOutputGivenRandomNumber(decisionInputs, 1.0, 1.0);
            return;
            // Uncomment this to consider only a truncated range of output values.
            //lowerBound = GetQuasiRandomOutputGivenRandomNumber(decisionInputs, 0.0, CurrentWeightOnRandomStrategy);
            //upperBound = GetQuasiRandomOutputGivenRandomNumber(decisionInputs, 1.0, CurrentWeightOnRandomStrategy);
        }

        public double CalculateOutputForInputs(List<double> inputs)
        {
            if (OverallStrategy.Decision.ScoreRepresentsCorrectAnswer)
            {
                double correctAnswer = ScoreEstimationNetwork.CalculateResult(inputs);
                return correctAnswer;
            }
            double output = OutputEstimationNetwork.CalculateResult(inputs);
            return output;
        }


        private void AssessAccuracyForObfuscationGame()
        {
            LowerBound = OverallStrategy.Decision.StrategyBounds.LowerBound;
            UpperBound = OverallStrategy.Decision.StrategyBounds.UpperBound;
            bool useThreadLocalScoresOriginal = OverallStrategy.UseThreadLocalScores;
            OverallStrategy.UseThreadLocalScores = true;
            OversamplingInfo oversamplingInfo = new OversamplingInfo() { OversamplingPlan = OverallStrategy.OversamplingPlanDuringOptimization, StoreInputSeedsForImprovementOfOversamplingPlan = false, StoreWeightsForAdjustmentOfScoreAverages = false }; // we don't need the input seeds or weights
            int combinedIterations = DirectScoreEstimationSmoothingOptions.NumIterationsScoreEstimationNetworkValidationSet;
            StatCollector s = new StatCollector();
            Parallelizer.GoForSpecifiedNumberOfSuccesses(EvolutionSettings.ParallelOptimization, combinedIterations, (successNumber, iterationNumber) =>
            {
                bool decisionReached;
                GameProgress preplayedGameProgressInfo;
                IterationID id = OverallStrategy.GenerateIterationID(iterationNumber);
                List<double> inputs = OverallStrategy.GetDecisionInputsForIteration(id, combinedIterations, oversamplingInfo, out decisionReached, out preplayedGameProgressInfo);
                if (decisionReached)
                {
                    double outputToUse = CalculateOutputForInputs(inputs);
                    double correctAnswer = ObfuscationGame.ObfuscationCorrectAnswer.Calculate(inputs[1], inputs[0]);
                    double score = Math.Abs(outputToUse - correctAnswer);
                    s.Add(score);
                    return true;
                }
                return false;
            });
            Debug.WriteLine("Accuracy of obfuscation game: " + s.Average());
            OverallStrategy.UseThreadLocalScores = useThreadLocalScoresOriginal;
        }

        private void AssessAccuracyForObfuscationGame_ScoreEstimationNetwork()
        {
            LowerBound = OverallStrategy.Decision.StrategyBounds.LowerBound;
            UpperBound = OverallStrategy.Decision.StrategyBounds.UpperBound;
            bool useThreadLocalScoresOriginal = OverallStrategy.UseThreadLocalScores;
            OverallStrategy.UseThreadLocalScores = true;
            OversamplingInfo oversamplingInfo = new OversamplingInfo() { OversamplingPlan = OverallStrategy.OversamplingPlanDuringOptimization, StoreInputSeedsForImprovementOfOversamplingPlan = false, StoreWeightsForAdjustmentOfScoreAverages = false }; // we don't need the input seeds or weights
            int combinedIterations = DirectScoreEstimationSmoothingOptions.NumIterationsScoreEstimationNetworkValidationSet;
            StatCollector s = new StatCollector(); 
            Parallelizer.GoForSpecifiedNumberOfSuccesses(false, combinedIterations, (successNumber, iterationNumber) =>
            {
                bool decisionReached;
                GameProgress preplayedGameProgressInfo;
                IterationID id = OverallStrategy.GenerateIterationID(iterationNumber);
                List<double> inputs = OverallStrategy.GetDecisionInputsForIteration(id, combinedIterations, oversamplingInfo, out decisionReached, out preplayedGameProgressInfo);
                if (decisionReached)
                {
                    double outputToUse = FastPseudoRandom.GetRandom(iterationNumber, 2341 /* arbitrary */); // RandomGenerator.NextDouble(0, 1);
                    double correctAnswer = ObfuscationGame.ObfuscationCorrectAnswer.Calculate(inputs[1], inputs[0]);
                    double expectedScoreGivenOutput = Math.Abs(outputToUse - correctAnswer) * Math.Abs(outputToUse - correctAnswer);
                    List<double> inputsPlusOutputs = inputs.ToList();
                    inputsPlusOutputs.Add(outputToUse);
                    double actualScoreGivenOutput = ScoreEstimationNetwork.CalculateResult(inputsPlusOutputs);
                    double accuracyOfScore = Math.Abs(expectedScoreGivenOutput - actualScoreGivenOutput);
                    s.Add(accuracyOfScore);
                    return true;
                }
                return false;
            });
            Debug.WriteLine("Accuracy of obfuscation game score estimation network: " + s.Average());
            OverallStrategy.UseThreadLocalScores = useThreadLocalScoresOriginal;
        }

        public void PreSerialize()
        {
        }

        public void UndoPreSerialize()
        {
        }
    }
}
