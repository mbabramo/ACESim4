using ACESimBase.GameSolvingSupport;
using ACESimBase.Util;
using ACESimBase.Util.ArrayProcessing;
using NeuralNetworkNET.APIs;
using NeuralNetworkNET.APIs.Enums;
using NeuralNetworkNET.APIs.Interfaces;
using NeuralNetworkNET.APIs.Interfaces.Data;
using NeuralNetworkNET.APIs.Results;
using NeuralNetworkNET.APIs.Structs;
using NeuralNetworkNET.Networks.Cost;
using NeuralNetworkNET.SupervisedLearning.Progress;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ACESim
{
    [Serializable]
    public partial class FictitiousPlay : StrategiesDeveloperBase
    {
        public bool AddNoiseToBestResponses = false;
        public bool ReportOnBestResponse = false; // not yet implemented

        public FictitiousPlay(List<Strategy> existingStrategyState, EvolutionSettings evolutionSettings, GameDefinition gameDefinition) : base(existingStrategyState, evolutionSettings, gameDefinition)
        {

        }

        public override IStrategiesDeveloper DeepCopy()
        {
            var created = new FictitiousPlay(Strategies, EvolutionSettings, GameDefinition);
            DeepCopyHelper(created);
            return created;
        }


        public override int? IterationsForWarmupScenario()
        {
            return EvolutionSettings.IterationsForWarmupScenario; // warmup is supported
        }

        public override async Task<ReportCollection> RunAlgorithm(string optionSetName)
        {
            ReportCollection reportCollection = new ReportCollection();
            StrategiesDeveloperStopwatch.Reset();
            InitializeInformationSets();
            int iterationToReturnToBaselineScenario = EvolutionSettings.IterationsForWarmupScenario ?? - 1;
            int startingIteration = 2;
            if (iterationToReturnToBaselineScenario < startingIteration)
                iterationToReturnToBaselineScenario = startingIteration;
            bool targetMet = false;
            for (int iteration = startingIteration; iteration <= EvolutionSettings.TotalIterations && !targetMet; iteration++)
            {
                if (iteration % 50 == 1 && EvolutionSettings.DynamicSetParallel)
                    DynamicallySetParallel();
                IterationNum = iteration;
                var result = await FictitiousPlayIteration(iteration);
                reportCollection.Add(result);
                targetMet = BestResponseTargetMet;
                if (iteration == iterationToReturnToBaselineScenario)
                {
                    ReinitializeForScenario(GameDefinition.BaselineScenarioIndex, false);
                }
            }
            return reportCollection;
        }

        private async Task<ReportCollection> FictitiousPlayIteration(int iteration)
        {
            StrategiesDeveloperStopwatch.Start();

            IterationNumDouble = iteration;
            IterationNum = iteration;

            //double lambda2 = 1.0 / IterationNumDouble;

            ReportCollection reportCollection = new ReportCollection();
            double[] lastUtilities = new double[NumNonChancePlayers];

            CalculateBestResponse(true);
            RememberBestResponseExploitabilityValues(iteration);

            if (AddNoiseToBestResponses)
                Parallel.ForEach(InformationSets, informationSet => informationSet.AddNoiseToBestResponse(0.10, iteration));

            double perturbation = 0; // NOTE: 0 perturbation seems necessary for fictitious play EvolutionSettings.Perturbation_BasedOnCurve(iteration, EvolutionSettings.TotalIterations);

            if (IterationNum == 10)
            {
                // DEBUG
                int numSamples = 7000;
                int numSamplesForTraining = 4000;
                int numSamplesForValidation = 2000;
                const double changeSizeScale = 0.0001;
                (float[] X, float[] Y)[] data = new (float[] X, float[] Y)[numSamples];
                InformationSetNodesMutationPrep p = new InformationSetNodesMutationPrep(InformationSets, changeSizeScale);
                InformationSets.ForEach(x => x.ZeroLowProbabilities(InformationSetNodesMutationPrep.MinValueToKeep)); 
                InformationSets.ForEach(x => x.CreateBackup());
                CalculateBestResponse(false);
                var original = BestResponseImprovementAdjAvg;
                for (int s = 0; s < numSamples; s++)
                {
                    ConsistentRandomSequenceProducer r = new ConsistentRandomSequenceProducer((long)s * (long)100_000);
                    float[] conciseMutationForm = p.PrepareMutations(r);
                    data[s].X = conciseMutationForm;
                    //for (int i = 0; i < 5; i++)
                    //    Debug.WriteLine(i + ": " + String.Join(",", InformationSets[i].GetAverageStrategyProbabilities()));
                    p.ImplementMutations(conciseMutationForm);
                    //for (int i = 0; i < 5; i++)
                    //    Debug.WriteLine(i + ": " + String.Join(",", InformationSets[i].GetAverageStrategyProbabilities()));
                    CalculateBestResponse(false);
                    data[s].Y = new float[] { (float) BestResponseImprovementAdjAvg };
                }
                await TestNeuralNetwork(numSamples, numSamplesForTraining, numSamplesForValidation, data, CostFunctionType.Quadratic, 0.5f, TrainingAlgorithms.Momentum(0.01f, 0));
                //foreach (CostFunctionType costFunctionType in new CostFunctionType[] { CostFunctionType.CrossEntropy, CostFunctionType.Quadratic })
                //    foreach (float dropout in new float[] { 0, (float) 0.5})
                //        foreach (ITrainingAlgorithmInfo trainingAlgorithm in new ITrainingAlgorithmInfo[] { TrainingAlgorithms.AdaDelta(), TrainingAlgorithms.AdaGrad(), TrainingAlgorithms.Adam(), TrainingAlgorithms.AdaMax(), TrainingAlgorithms.Momentum(), TrainingAlgorithms.RMSProp(), TrainingAlgorithms.StochasticGradientDescent() })
                //            await TestNeuralNetwork(numSamples, numSamplesInTraining, data, costFunctionType, dropout, trainingAlgorithm);

                InformationSets.ForEach(x => x.RestoreBackup());
            }

            if (EvolutionSettings.BestResponseDynamics)
            {
                if (!EvolutionSettings.ParallelOptimization)
                    foreach (var informationSet in InformationSets)
                        informationSet.SetAverageStrategyToBestResponse(perturbation);
                else
                    Parallel.ForEach(InformationSets, informationSet => informationSet.SetAverageStrategyToBestResponse(perturbation));
            }
            else
            {
                if (!EvolutionSettings.ParallelOptimization)
                    foreach (var informationSet in InformationSets)
                        informationSet.MoveAverageStrategyTowardBestResponse(iteration, EvolutionSettings.TotalIterations, perturbation);
                else
                    Parallel.ForEach(InformationSets, informationSet => informationSet.MoveAverageStrategyTowardBestResponse(iteration, EvolutionSettings.TotalIterations, perturbation));
            }

            StrategiesDeveloperStopwatch.Stop();

            var result = await GenerateReports(iteration,
                () =>
                    $"{GameDefinition.OptionSetName} Iteration {iteration} Overall milliseconds per iteration {((StrategiesDeveloperStopwatch.ElapsedMilliseconds / ((double)iteration)))}");
            reportCollection.Add(result);

            return reportCollection;
        }

        private static async Task TestNeuralNetwork(int numSamples, int numSamplesForTraining, int numSamplesForValidation, (float[] X, float[] Y)[] data, CostFunctionType costFunctionType, float dropout, ITrainingAlgorithmInfo trainingAlgorithm)
        {
            INeuralNetwork network = NetworkManager.NewSequential(TensorInfo.Linear(data.First().X.Length),
                NetworkLayers.FullyConnected(100, ActivationType.Sigmoid),
                NetworkLayers.FullyConnected(1, ActivationType.Sigmoid, costFunctionType));
            ITrainingDataset trainingData = DatasetLoader.Training(data.Take(numSamplesForTraining), numSamplesForTraining);
            var validationData = DatasetLoader.Validation(data.Skip(numSamplesForTraining).Take(numSamplesForValidation), 0.005f, 10);
            ITestDataset testData = DatasetLoader.Test(data.Skip(numSamplesForTraining + numSamplesForValidation));
            void TrackBatchProgress(BatchProgress progress)
            {
            }
            TrainingSessionResult trainingResult = await NetworkManager.TrainNetworkAsync(network,
                trainingData,
                trainingAlgorithm,
                500, dropout,
                TrackBatchProgress,
                testDataset: testData);
            var lastTrainingReport = trainingResult.TestReports.Last();
            var testDataResults = data.Skip(numSamplesForTraining).Select(d => (network.Forward(d.X).First(), d.Y.First())).ToList();
            float ComputeCoeff(float[] values1, float[] values2)
            {
                if (values1.Length != values2.Length)
                    throw new ArgumentException("values must be the same length");

                var avg1 = values1.Average();
                var avg2 = values2.Average();

                var sum1 = values1.Zip(values2, (x1, y1) => (x1 - avg1) * (y1 - avg2)).Sum();

                var sumSqr1 = values1.Sum(x => Math.Pow((x - avg1), 2.0));
                var sumSqr2 = values2.Sum(y => Math.Pow((y - avg2), 2.0));

                var result = sum1 / Math.Sqrt(sumSqr1 * sumSqr2);

                return (float)result;
            }
            var correlation = ComputeCoeff(testDataResults.Select(d => d.Item1).ToArray(), testDataResults.Select(d => d.Item2).ToArray());
            Debug.WriteLine($"Cost function {costFunctionType} dropout {dropout} trainingAlgorithm {trainingAlgorithm} correlation {correlation}");
        }

        private void ZeroLowPorbabilities()
        {
            Parallel.ForEach(InformationSets, informationSet =>
            {
                informationSet.ZeroLowProbabilities(0.01);
            });
        }
    }
}
