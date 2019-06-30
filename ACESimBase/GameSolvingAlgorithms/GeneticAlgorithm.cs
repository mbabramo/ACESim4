using ACESim;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESimBase.GameSolvingAlgorithms
{
    public class GeneticAlgorithm : StrategiesDeveloperBase
    {

        Population Pop;

        public GeneticAlgorithm(List<Strategy> existingStrategyState, EvolutionSettings evolutionSettings, GameDefinition gameDefinition) : base(existingStrategyState, evolutionSettings, gameDefinition)
        {

        }

        public override IStrategiesDeveloper DeepCopy()
        {
            var created = new GeneticAlgorithm(Strategies, EvolutionSettings, GameDefinition);
            DeepCopyHelper(created);
            return created;
        }

        public double CalculateBestResponseAndGetFitness()
        {
            // gets the best response for whichever population member's actions have been copied to information set
            CalculateBestResponse(false);
            return BestResponseImprovement.Sum();
        }

        public override Task<string> RunAlgorithm(string reportName)
        {
            string reportString = null;
            Pop = new Population(InformationSets, CalculateBestResponseAndGetFitness);
            StrategiesDeveloperStopwatch.Reset();

            for (int iteration = 1; iteration <= EvolutionSettings.TotalVanillaCFRIterations; iteration++)
            {
                reportString = GeneticIteration(iteration, EvolutionSettings.TotalVanillaCFRIterations);
            }

            return Task.FromResult(reportString);
        }



        private string GeneticIteration(int iteration, int maxIteration)
        {
            Pop.Generation(iteration, maxIteration);

            string reportString = $"{iteration}: {Pop.Members[0].GameFitness}";
            TabbedText.WriteLine(reportString);

            //reportString = await GenerateReports(iteration,
            //    () =>
            //        $"Iteration {iteration} Overall milliseconds per iteration {((StrategiesDeveloperStopwatch.ElapsedMilliseconds / ((double)iteration)))}");
            return reportString;
        }

        public class PopulationMember
        {
            public byte[] PureStrategies;

            public int MemberID;
            public double GameFitness; // lower (exploitability) is better
            public double DiversityFitness; // lower (less similar) is better
            public double OverallFitness;
            public int GameFitnessRank;
            public int DiversityRank;
            public int OverallRank;

            private List<InformationSetNode> InformationSets;
            private int InformationSetsCount;
            Func<double> CalculateBestResponseAction;

            public PopulationMember(List<InformationSetNode> informationSets, Func<double> calculateBestResponseAction, int memberID)
            {
                InformationSets = informationSets;
                InformationSetsCount = informationSets.Count();
                CalculateBestResponseAction = calculateBestResponseAction;
                PureStrategies = new byte[InformationSetsCount];
                MemberID++;
                Randomize();
            }

            public void Randomize()
            {
                ConsistentRandomSequenceProducer r = new ConsistentRandomSequenceProducer(MemberID);
                for (int i = 0; i < InformationSetsCount; i++)
                {
                    InformationSetNode informationSet = InformationSets[i];
                    byte action = (byte) (r.NextInt(informationSet.NumPossibleActions + 1));
                    PureStrategies[i] = action;
                }
            }

            public void MeasureGameFitness()
            {
                CopyToInformationSets();
                GameFitness = CalculateBestResponseAction();
            }

            public double CalculateSimilarity(PopulationMember other)
            {
                int matches = 0;
                for (int i = 0; i < InformationSetsCount; i++)
                    if (PureStrategies[i] == other.PureStrategies[i])
                        matches++;
                return (double)matches / (double)InformationSetsCount;
            }

            public void Mutate(int iteration, int numMutations)
            {
                ConsistentRandomSequenceProducer r = GetRandomProducer(iteration);
                for (int m = 0; m < numMutations; m++)
                {
                    int i = 0;
                    InformationSetNode informationSet = null;
                    while (informationSet == null || informationSet.NumPossibleActions == 1)
                    {
                        i = r.NextInt(InformationSetsCount);
                        informationSet = InformationSets[m];
                    }
                    byte current = PureStrategies[i];
                    bool up;
                    if (current == 1)
                        up = true;
                    else if (current == informationSet.NumPossibleActions)
                        up = false;
                    else
                        up = r.NextInt(2) == 0;
                    if (up)
                        PureStrategies[i]++;
                    else
                        PureStrategies[i]--;
                }
            }

            private ConsistentRandomSequenceProducer GetRandomProducer(int iteration)
            {
                long seed = 0;
                unchecked
                {
                    seed = 234235847 * iteration + MemberID;
                }
                ConsistentRandomSequenceProducer r = new ConsistentRandomSequenceProducer(seed);
                return r;
            }

            public void Crossover(PopulationMember other, int iteration, int numCrossovers)
            {
                ConsistentRandomSequenceProducer r = GetRandomProducer(iteration);
                for (int crossover = 0; crossover < numCrossovers; crossover++)
                {
                    int i = 0;
                    InformationSetNode informationSet = null;
                    while (informationSet == null || informationSet.NumPossibleActions == 1)
                    {
                        i = r.NextInt(InformationSetsCount);
                        informationSet = InformationSets[crossover];
                    }
                    if (r.NextInt(2) == 0)
                        other.PureStrategies[i] = PureStrategies[i];
                    else
                        PureStrategies[i] = other.PureStrategies[i];
                }
            }

            public void ReplaceWith(PopulationMember mom, PopulationMember dad, int iteration)
            {
                ConsistentRandomSequenceProducer r = GetRandomProducer(iteration);
                for (int i = 0; i < InformationSetsCount; i++)
                {
                    InformationSetNode informationSet = InformationSets[i];
                    if (r.NextInt(2) == 0)
                        PureStrategies[i] = mom.PureStrategies[i];
                    else
                        PureStrategies[i] = dad.PureStrategies[i];
                }
            }

            public void CopyToInformationSets()
            {
                for (int i = 0; i < InformationSetsCount; i++)
                {
                    InformationSets[i].SetToPureStrategy(PureStrategies[i]);
                }
            }
        }

        public class Population
        {
            const int popSize = 30;
            const int numToKeep = 10;
            const double probMutation = 0.05;
            public PopulationMember[] Members = new PopulationMember[popSize];
            double[,] Similarity = new double[popSize, popSize];
            private List<InformationSetNode> InformationSets;
            private int InformationSetsCount;
            Func<double> CalculateBestResponseAction;

            public double WeightOnDiversityInitial = 0.5;
            public double WeightOnDiversityFinal = 0.0;
            public double WeightOnDiversityCurvature = 0.5;
            public double WeightOnDiversity_BasedOnCurve(int iteration, int maxIteration) => MonotonicCurve.CalculateValueBasedOnProportionOfWayBetweenValues(WeightOnDiversityInitial, WeightOnDiversityFinal, WeightOnDiversityCurvature, ((double)(iteration - 1)) / (double)maxIteration);

            public Population(List<InformationSetNode> informationSets, Func<double> calculateBestResponseAction)
            {
                InformationSets = informationSets;
                InformationSetsCount = informationSets.Count();
                CalculateBestResponseAction = calculateBestResponseAction;
                for (int i = 0; i < popSize; i++)
                {
                    Members[i] = new PopulationMember(InformationSets, CalculateBestResponseAction, i);
                    Similarity[i, i] = 1.0; // each population member always identical to self
                }
            }

            public void Generation(int iteration, int maxIteration)
            {
                CalculateGameFitness(iteration == 1);
                Rank(WeightOnDiversity_BasedOnCurve(iteration, maxIteration));
                Reproduce(iteration);
                Members[0].CopyToInformationSets();
            }

            public void CalculateGameFitness(bool all)
            {
                for (int i = 0; i < popSize; i++)
                    if (all || Members[i].OverallRank >= numToKeep)
                        Members[i].MeasureGameFitness(); // if less than, it hasn't changed
            }

            public void Rank(double weightOnDiversity)
            {
                for (int i = 1; i < popSize; i++)
                    for (int j = 0; j < i; j++)
                    {
                        double similarity = Members[i].CalculateSimilarity(Members[j]);
                        Similarity[i, j] = Similarity[j, i] = similarity;
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
            }

            private ConsistentRandomSequenceProducer GetRandomProducer(int iteration)
            {
                long seed = 0;
                unchecked
                {
                    seed = 123523523 * iteration;
                }
                ConsistentRandomSequenceProducer r = new ConsistentRandomSequenceProducer(seed);
                return r;
            }

            public void Reproduce(int iteration)
            {
                var r = GetRandomProducer(iteration);
                for (int i = numToKeep; i < popSize; i++)
                {
                    int momIndex = r.NextInt(numToKeep);
                    int dadIndex = momIndex;
                    while (dadIndex == momIndex)
                        dadIndex = r.NextInt(numToKeep);
                    Members[i].ReplaceWith(Members[momIndex], Members[dadIndex], iteration);
                    if (r.NextDouble() < probMutation)
                        Members[i].Mutate(iteration, r.NextInt(3));
                }
            }
        }
    }
}
