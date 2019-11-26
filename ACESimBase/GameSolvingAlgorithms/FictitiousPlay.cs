using ACESimBase.GameSolvingSupport;
using ACESimBase.Util;
using ACESimBase.Util.ArrayProcessing;
using NeuralNetworkNet.Minimizer;
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

            if (IterationNum == 25_000)
            {
                // DEBUG
                int numSamples = 25_000;
                const double changeSizeScale = 0.01;
                (float[] X, float[] Y)[] data = new (float[] X, float[] Y)[numSamples];
                InformationSetNodesMutationPrep p = new InformationSetNodesMutationPrep(InformationSets, changeSizeScale);
                CalculateBestResponse(false);
                var original = BestResponseImprovementAdjAvg;
                InformationSets.ForEach(x => x.CreateBackup());
                InformationSets.ForEach(x => x.ZeroLowProbabilities(InformationSetNodesMutationPrep.MinValueToKeep)); 
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
                float[] result2 = await NeuralNetworkMinimization(data);
                p.ImplementMutations(result2);

                CalculateBestResponse(false);
                var revised = BestResponseImprovementAdjAvg;
                Console.WriteLine($"best response orig: {original} revised: {revised}");

                if (revised > original) // didn't improve things
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

        private static async Task<float[]> NeuralNetworkMinimization((float[] X, float[] Y)[] data)
        {
            // DEBUG
            int numSamplesForTraining = (int) (data.Length * 0.6f);
            int numSamplesForValidation = (int)(data.Length * 0.01f); ;
            int batchSize = 200;
            int fullyConnectedLayerSize = 500;
            int epochs = 100; // DEBUG
            Random r = new Random();
            INeuralNetwork network = await Minimizer.BuildNeuralNetwork(numSamplesForTraining, numSamplesForValidation, batchSize, fullyConnectedLayerSize, data, CostFunctionType.Quadratic, 0, TrainingAlgorithms.RMSProp(), epochs);
            var minimized = await Minimizer.MinimizeInput(batchSize, data, CostFunctionType.Quadratic, TrainingAlgorithms.RMSProp(), 500, network);
            return minimized;
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
