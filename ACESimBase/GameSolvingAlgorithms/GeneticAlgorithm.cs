using ACESimBase.Util;
using ACESimBase.Util.ArrayProcessing;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ACESim
{
    public partial class GeneticAlgorithm : StrategiesDeveloperBase
    {

        public class PopulationMember
        {
            public double[][] AverageStrategyValues;
            public byte[] BestResponseActions;

            public double MutationWeightOnBestResponse;

            public double Fitness;
            private List<InformationSetNode> InformationSets;
            private int InformationSetsCount;
            Func<double> CalculateBestResponseAction;

            public PopulationMember(List<InformationSetNode> informationSets, Func<double> calculateBestResponseAction)
            {
                InformationSets = informationSets;
                CalculateBestResponseAction = calculateBestResponseAction;
                Initialize();
            }

            public void ReplicateOther(PopulationMember memberToReplicate)
            {
                foreach (InformationSetNode informationSet in InformationSets)
                {
                    int node = informationSet.InformationSetNodeNumber;
                    BestResponseActions[node] = memberToReplicate.BestResponseActions[node];
                    for (byte action = 1; action <= informationSet.NumPossibleActions; action++)
                    {
                        AverageStrategyValues[node][action - 1] = memberToReplicate.AverageStrategyValues[node][action - 1];
                    }
                }
                Fitness = memberToReplicate.Fitness;
                MutationWeightOnBestResponse = memberToReplicate.MutationWeightOnBestResponse;
            }

            public void Initialize()
            {
                InformationSetsCount = InformationSets.Count();
                AverageStrategyValues = new double[InformationSetsCount][];
                BestResponseActions = new byte[InformationSetsCount];
                foreach (InformationSetNode informationSet in InformationSets)
                {
                    int node = informationSet.InformationSetNodeNumber;
                    AverageStrategyValues[node] = new double[informationSet.NumPossibleActions];
                }
                CopyFromInformationSets(true);
                MutationWeightOnBestResponse = 1.0;
            }

            public void CopyFromInformationSets(bool copyAverageStrategies)
            {
                foreach (InformationSetNode informationSet in InformationSets)
                {
                    int node = informationSet.InformationSetNodeNumber;
                    BestResponseActions[node] = informationSet.LastBestResponseAction;
                    if (copyAverageStrategies)
                        for (byte action = 1; action <= informationSet.NumPossibleActions; action++)
                        {
                            AverageStrategyValues[node][action - 1] = informationSet.GetAverageStrategy(action);
                        }
                }
            }

            public void CopyToInformationSets()
            {
                foreach (InformationSetNode informationSet in InformationSets)
                {
                    int node = informationSet.InformationSetNodeNumber;
                    informationSet.LastBestResponseAction = BestResponseActions[node];
                    var averageStrategies = AverageStrategyValues[node];
                    for (byte action = 1; action <= informationSet.NumPossibleActions; action++)
                    {
                        informationSet.NodeInformation[InformationSetNode.averageStrategyProbabilityDimension, action - 1] = averageStrategies[action - 1];
                    }
                }
            }

            public void MeasureFitness()
            {
                CopyToInformationSets();
                Fitness = CalculateBestResponseAction();
                CopyFromInformationSets(false); // copy best response info
            }

            public void MutateAndMeasureFitness(double? weightOnBestResponse, int repetitions)
            {
                for (int i = 0; i < repetitions; i++)
                {
                    Mutate(weightOnBestResponse);
                    MeasureFitness();
                }
            }


            public void Mutate(double? weightOnBestResponse = null)
            {
                // First, mutate by changing the weight on the best response

                if (weightOnBestResponse is double w)
                    MutationWeightOnBestResponse = w;
                else
                    ChangeWeightOnBestResponse();

                // Second, apply that weight to move that proportion of the way in the direction of the best response

                MoveTowardBestResponse();
            }

            private void ChangeWeightOnBestResponse()
            {
                // Suppose MutationWeightOnBestResponse = 1/1000. We will allow that denominator to go up or down some percent, but in some cases, we will pick a random weight. 
                //var r = RandomGenerator.NextDouble();
                //if (r < 0.1)
                //{
                //    MutationWeightOnBestResponse = 10.0 * r; // weight from 0 to 1
                //}
                //else
                {
                    double inverse = 1.0 / MutationWeightOnBestResponse;
                    inverse *= RandomGenerator.NextDouble(0.90, 1.10);
                    if (inverse < 1.0)
                        inverse = 1.0;
                    MutationWeightOnBestResponse = 1.0 / inverse;
                }
            }

            private void MoveTowardBestResponse()
            {
                for (int i = 0; i < InformationSetsCount; i++)
                {
                    var node = InformationSets[i];
                    var averageStrategies = AverageStrategyValues[i];
                    var bestResponse = BestResponseActions[i];
                    for (byte action = 1; action <= node.NumPossibleActions; action++)
                    {
                        double bestResponseProbability = (bestResponse == action) ? 1.0 : 0.0;
                        // double difference = bestResponseProbability - currentAverageStrategyProbability;
                        double successorValue = (1.0 - MutationWeightOnBestResponse) * averageStrategies[action - 1] + MutationWeightOnBestResponse * bestResponseProbability;
                        averageStrategies[action - 1] = successorValue;
                    }
                }
            }
        }

        public GeneticAlgorithm(List<Strategy> existingStrategyState, EvolutionSettings evolutionSettings, GameDefinition gameDefinition) : base(existingStrategyState, evolutionSettings, gameDefinition)
        {

        }

        const int NumPopulationMembers = 10;
        private const int NumProtectedMembers = NumPopulationMembers / 10;
        PopulationMember[] PopulationMembers = new PopulationMember[NumPopulationMembers];

        public override IStrategiesDeveloper DeepCopy()
        {
            var created = new GeneticAlgorithm(Strategies, EvolutionSettings, GameDefinition);
            DeepCopyHelper(created);
            return created;
        }

        public double CalculateBestResponseAndGetFitness()
        {
            CalculateBestResponse();
            return BestResponseImprovement.Sum();
        }

        public override Task<string> RunAlgorithm(string reportName)
        {
            string reportString = null;
            StrategiesDeveloperStopwatch.Reset();
            CalculateBestResponseAndGetFitness();
            for (int i = 0; i < NumPopulationMembers; i++)
            {
                PopulationMembers[i] = new PopulationMember(InformationSets, CalculateBestResponseAndGetFitness);
            }

            for (int iteration = 1; iteration <= 5000000; iteration++)
            {
                reportString = GeneticAlgorithmIteration(iteration);
            }

            return Task.FromResult(reportString);
        }

        bool LastWasImprovement = true;

        private string GeneticAlgorithmIteration(int iteration)
        {
            StrategiesDeveloperStopwatch.Start();

            if (LastWasImprovement)
                PopulationMembers = PopulationMembers
                    .OrderBy(x => x.Fitness) // lower values better and thus first
                    .ToArray();

            double originalBest = PopulationMembers[0].Fitness;

            double baselineWeight = PopulationMembers[0].MutationWeightOnBestResponse;

            double mutationSizeDenominator = 2.0;
            for (int i = 0; i < PopulationMembers.Length; i++)
            {
                PopulationMember populationMember = PopulationMembers[i];
                if (iteration == 1 || i > 0)
                {
                    populationMember.MutateAndMeasureFitness(1.0 / mutationSizeDenominator, 1); // try mutating at various sizes
                    mutationSizeDenominator *= 2.0;
                }
            }

            var after = PopulationMembers
                .OrderBy(x => x.Fitness) // lower values better and thus first
                .ToArray();

            double revisedOriginalBest = after[0].Fitness;
            LastWasImprovement = revisedOriginalBest < originalBest;
            if (LastWasImprovement)
            {
                PopulationMembers = after;
                for (int i = 1; i < NumPopulationMembers; i++)
                    PopulationMembers[i].ReplicateOther(PopulationMembers[0]);
            }

            //int numTries = 0;
            //while (revisedOriginalBest >= originalBest && originalBest != 0 && numTries++ < int.MaxValue)
            //{
            //    // continue to do small mutations until we have an improvement.
            //    PopulationMembers[0].Mutate(baselineWeight);
            //    PopulationMembers[0].MeasureFitness();
            //    revisedOriginalBest = PopulationMembers[0].Fitness; // may not be bessed
            //}

            //for (int i = 1; i < NumPopulationMembers; i++)
            //{
            //    PopulationMembers[i].ReplicateOther(PopulationMembers[0]);
            //    //double deathProbability = MonotonicCurve.CalculateValueBasedOnProportionOfWayBetweenValues(0, 0.05, 2.0, i / (double)NumPopulationMembers - 1);
            //    //if (RandomGenerator.NextDouble() < deathProbability)
            //    //    PopulationMembers[i].ReplicateOther(PopulationMembers[0]); // DEBUG RandomGenerator.Next(NumProtectedMembers - 1)]);
            //}


            StrategiesDeveloperStopwatch.Stop();

            string reportString = $"{iteration}: {PopulationMembers[0].Fitness} mutation weight: {PopulationMembers[0].MutationWeightOnBestResponse}";
            Console.WriteLine(reportString);

            //reportString = await GenerateReports(iteration,
            //    () =>
            //        $"Iteration {iteration} Overall milliseconds per iteration {((StrategiesDeveloperStopwatch.ElapsedMilliseconds / ((double)iteration)))}");
            return reportString;
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
