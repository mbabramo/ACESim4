using ACESim;
using ACESimBase.Util;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ACESimBase.GameSolvingAlgorithms
{
        public class GeneticPopulation
        {
            const int popSize = 30;
            const int numToKeep = 5;
            const double probMutation = 0.1;
            public GeneticPopulationMember[] Members = new GeneticPopulationMember[popSize];
            double[,] Similarity = new double[popSize, popSize];
            private List<InformationSetNode> InformationSets;
            private int InformationSetsCount;
            Func<(double exploitability, double[] utilities)> CalculateBestResponseAction;

            public double WeightOnDiversityInitial = 0.5;
            public double WeightOnDiversityFinal = 0.0;
            public double WeightOnDiversityCurvature = 0.5;
            public double WeightOnDiversity_BasedOnCurve(int iteration, int maxIteration) => MonotonicCurve.CalculateValueBasedOnProportionOfWayBetweenValues(WeightOnDiversityInitial, WeightOnDiversityFinal, WeightOnDiversityCurvature, ((double)(iteration - 1)) / (double)maxIteration);

            public GeneticPopulation(List<InformationSetNode> informationSets, Func<(double exploitability, double[] utilities)> calculateBestResponseAction, bool randomize)
            {
                InformationSets = informationSets;
                InformationSetsCount = informationSets.Count();
                CalculateBestResponseAction = calculateBestResponseAction;
                for (int i = 0; i < popSize; i++)
                {
                    Members[i] = new GeneticPopulationMember(InformationSets, CalculateBestResponseAction, i, randomize);
                    Similarity[i, i] = 1.0; // each population member always identical to self
                }
            }

            public void Generation(int iteration, int maxIteration)
            {
                CalculateGameFitness(iteration == 1);
                Rank(WeightOnDiversity_BasedOnCurve(iteration, maxIteration));
                Reproduce(iteration);
                Members[0].CopyMixedStrategyToInformationSets();
            }

            public void CalculateGameFitness(bool all)
            {
                for (int i = 0; i < popSize; i++)
                    if (all || !Members[i].Keep) // if less than, it hasn't changed
                        Members[i].MeasureGameFitness(); 
            }

            public void Rank(double weightOnDiversity)
            {

                Members = Members.OrderBy(x => x.GameFitness).ToArray();

                bool countAllInComputingSimilarity = false;

                if (countAllInComputingSimilarity)
                {
                    for (int i = 1; i < popSize; i++)
                    {
                        for (int j = 0; j < i; j++)
                        {
                            double similarity = Members[i].CalculateSimilarity(Members[j]);
                            Similarity[i, j] = Similarity[j, i] = similarity;
                        }
                        Members[i].Keep = false; // default
                    }
                    for (int i = 0; i < popSize; i++)
                    {
                        double sum = 0;
                        for (int j = 0; j < popSize; j++)
                            sum += Similarity[i, j];
                        Members[i].DiversityFitness = sum;
                        Members[i].OverallFitness = Members[i].GameFitness * (1.0 - weightOnDiversity) + Members[i].DiversityFitness * weightOnDiversity;
                    }

                    var ranked = Enumerable.Range(0, popSize).OrderBy(x => Members[x].OverallFitness).ToArray();
                    for (int i = 0; i < popSize; i++)
                        Members[ranked[i]].OverallRank = i;
                    ranked = Enumerable.Range(0, popSize).OrderBy(x => Members[x].GameFitness).ToArray();
                    for (int i = 0; i < popSize; i++)
                        Members[ranked[i]].GameFitnessRank = i;
                    Members = Members.OrderByDescending(x => x.GameFitnessRank == 0).ThenBy(x => x.OverallRank).ToArray();
                    for (int i = 0; i < numToKeep; i++)
                        Members[i].Keep = true;
                }
                else
                {
                    // use kmeans to cluster based on scores, then keep best in each cluster
                    int[] clusters = KMeans.Cluster(Members.Select(x => x.Utilities).ToArray(), numToKeep);
                    for (int clusterNum = 0; clusterNum < numToKeep; clusterNum++)
                    {
                        int indexOfBestInCluster = -1;
                        double bestFitnessInCluster;
                        for (int i = 0; i < popSize; i++)
                        {
                            if (clusters[i] == clusterNum)
                            {
                                Members[i].Keep = false;
                                Members[i].OverallFitness = Members[i].GameFitness; // don't take diversity into account separate from this
                                if (indexOfBestInCluster == -1 || Members[i].GameFitness < Members[indexOfBestInCluster].GameFitness)
                                {
                                    indexOfBestInCluster = i;
                                    bestFitnessInCluster = Members[i].GameFitness;
                                }
                            }
                        }
                        Members[indexOfBestInCluster].Keep = true;
                    }
                    Members = Members.OrderByDescending(x => x.Keep).ThenBy(x => x.GameFitness).ToArray();
                }
                Members[0].IncrementSuccessfulMutations();
            }

            private ConsistentRandomSequenceProducer GetRandomProducer(int iteration)
            {
                long seed = 0;
                unchecked
                {
                    seed = 9845623 * iteration;
                }
                ConsistentRandomSequenceProducer r = new ConsistentRandomSequenceProducer(seed);
                return r;
            }

            public void Reproduce(int iteration)
            {
                var r = GetRandomProducer(iteration);
                for (int i = 0; i < popSize; i++)
                    Members[i].InformationSetsToMutate = null;
                for (int i = numToKeep; i < popSize; i++)
                {
                    int momIndex = r.NextInt(numToKeep);
                    int dadIndex = momIndex;
                    while (dadIndex == momIndex)
                        dadIndex = r.NextInt(numToKeep);
                    Members[i].ReplaceWith(Members[momIndex], Members[dadIndex], iteration);
                    const int MaxMutationsMinus1 = 3; 
                    if (r.NextDouble() < probMutation)
                        Members[i].Mutate(iteration, 1 + r.NextInt(MaxMutationsMinus1));
                }
            }
        }
}
