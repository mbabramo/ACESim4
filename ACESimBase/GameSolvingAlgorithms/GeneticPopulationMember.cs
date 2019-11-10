using ACESim;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESimBase.GameSolvingAlgorithms
{


    public class GeneticPopulationMember
    {
        public double[][] MixedStrategies;

        public int[] SuccessfulMutations;
        public int NumSuccessfulMutations;
        public int[] InformationSetsToMutate;

        public int MemberID;
        public double GameFitness; // lower (exploitability) is better
        public double[] Utilities;
        public double DiversityFitness; // lower (less similar) is better
        public double OverallFitness;
        public int GameFitnessRank;
        public int DiversityRank;
        public int OverallRank;
        public bool Keep;


        private List<InformationSetNode> InformationSets;
        private int InformationSetsCount;
        Func<(double exploitability, double[] utilities)> CalculateBestResponseAction;

        public GeneticPopulationMember(List<InformationSetNode> informationSets, Func<(double exploitability, double[] utilities)> calculateBestResponseAction, int memberID, bool randomize)
        {
            InformationSets = informationSets; // each population shares the same information sets, but we copy the mixed strategies from the information sets
            InformationSetsCount = informationSets.Count();
            CalculateBestResponseAction = calculateBestResponseAction;
            MixedStrategies = new double[InformationSetsCount][];
            SuccessfulMutations = new int[InformationSetsCount];
            MemberID = memberID;
            if (randomize)
                Randomize();
            else
                SetMixedStrategiesFromInformationSets();
        }

        public void Randomize()
        {
            ConsistentRandomSequenceProducer r = new ConsistentRandomSequenceProducer(MemberID);
            for (int i = 0; i < InformationSetsCount; i++)
            {
                InformationSetNode informationSet = InformationSets[i];
                // assign probabilities that necessarily add up to 1
                double[] probabilities = new double[informationSet.NumPossibleActions];
                double total = 0;
                for (int j = 0; j < probabilities.Length; j++)
                {
                    probabilities[j] = r.NextDouble();
                    total += probabilities[j];
                }
                for (int j = 0; j < probabilities.Length; j++)
                    probabilities[j] /= total;
                MixedStrategies[i] = probabilities;
            }
        }

        public void SetMixedStrategiesFromInformationSets()
        {
            for (int i = 0; i < InformationSetsCount; i++)
            {
                InformationSetNode informationSet = InformationSets[i];
                // assign probabilities that necessarily add up to 1
                double[] probabilities = informationSet.CalculateAverageStrategiesAsArray();
                //double[] currentProbabilities = informationSet.GetCurrentProbabilitiesAsArray();
                const double minProb = 0.03;
                double total = 0;
                for (int j = 0; j < probabilities.Length; j++)
                {
                    if (probabilities[j] < minProb)
                        probabilities[j] = 0;
                    total += probabilities[j];
                }
                for (int j = 0; j < probabilities.Length; j++)
                    probabilities[j] /= total;
                MixedStrategies[i] = probabilities;
                if (probabilities.Count(x => x > 0) > 1)
                {
                    // bias mutation in favor of this information set
                    SuccessfulMutations[i]++;
                    NumSuccessfulMutations++;
                }
            }
        }

        public void CopyMixedStrategyToInformationSets()
        {
            for (int i = 0; i < InformationSetsCount; i++)
            {
                InformationSets[i].SetToMixedStrategy(MixedStrategies[i], true /* best response relies on average strategy */);
            }
        }

        public void MeasureGameFitness()
        {
            CopyMixedStrategyToInformationSets();
            (GameFitness, Utilities) = CalculateBestResponseAction();
        }

        public double CalculateSimilarity(GeneticPopulationMember other)
        {
            bool useStructure = true;
            if (useStructure)
            {
                // Calculate the average difference in probability at all information sets, then do 1 - average difference, so if there is no difference, we get a similarity of 1.0
                double similarity = 1.0 - MixedStrategies
                    .Zip(other.MixedStrategies, (First, Second) => (First, Second))
                    .Select(x => x.First.Zip(x.Second).Average(z => Math.Abs(z.First - z.Second)))
                    .Average();
                return similarity;
            }
            else
            {
                double ratio = Utilities.Zip(other.Utilities, (First, Second) => (First, Second)).Select(x => Math.Min(x.First / x.Second, x.Second / x.First)).Average();
                return ratio;
            }
        }

        public void Mutate(int iteration, int numMutations)
        {
            ConsistentRandomSequenceProducer r = GetRandomProducer(iteration);
            ChooseInformationSetsToMutate(numMutations, r);

            for (int m = 0; m < numMutations; m++)
            {
                int i = InformationSetsToMutate[m];
                double[] probabilities = MixedStrategies[i];
                int j = r.NextInt(probabilities.Length);
                double valueAtIndex = probabilities[j];
                const double probZeroOutMutation = 0.5;
                if (r.NextDouble() < probZeroOutMutation && probabilities.Count(x => x > 0) > 1)
                {
                    probabilities[j] = 0;
                }
                else
                {
                    bool up = r.NextDouble() > 0.5;
                    double magnitude = r.NextDouble();
                    probabilities[j] = magnitude * valueAtIndex + (1.0 - magnitude) * (up ? 1.0 : 0.0);
                }
                double sum = probabilities.Sum();
                for (int k = 0; k < probabilities.Length; k++)
                    probabilities[k] = probabilities[k] / sum;
            }
        }

        public void ChooseInformationSetsToMutate(int numMutations, ConsistentRandomSequenceProducer r)
        {
            InformationSetsToMutate = new int[numMutations];
            for (int i = 0; i < numMutations; i++)
            {
                bool chooseFromSuccessful = r.NextDouble() > 0.5 && NumSuccessfulMutations > 0;
                int j = -1;
                if (chooseFromSuccessful)
                {
                    int target = r.NextInt(NumSuccessfulMutations) + 1;
                    int totalSoFar = 0;
                    int k = 0;
                    while (totalSoFar < target)
                        totalSoFar += SuccessfulMutations[k++];
                    j = k - 1;
                }
                else
                {
                    InformationSetNode informationSet = null;
                    while (informationSet == null || informationSet.NumPossibleActions == 1)
                    {
                        j = r.NextInt(InformationSetsCount);
                        informationSet = InformationSets[j];
                    }
                }
                InformationSetsToMutate[i] = j;
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

        public void Crossover(GeneticPopulationMember other, int iteration, int numCrossovers)
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
                    other.CopyInformationSetFromOtherPopulationMember(this, i);
                else
                    CopyInformationSetFromOtherPopulationMember(other, i);
            }
        }

        public void CopyInformationSetFromOtherPopulationMember(GeneticPopulationMember source, int informationSetIndex)
        {
            int numSuccessful = source.SuccessfulMutations[informationSetIndex];
            MixedStrategies[informationSetIndex] = source.MixedStrategies[informationSetIndex].ToArray();
            ChangeSuccessfulMutations(informationSetIndex, numSuccessful);
        }

        public void ChangeSuccessfulMutations(int index, int numSuccessful)
        {
            int previous = SuccessfulMutations[index];
            SuccessfulMutations[index] = numSuccessful;
            int change = numSuccessful - previous;
            NumSuccessfulMutations += change;
        }

        public void IncrementSuccessfulMutations()
        {
            if (InformationSetsToMutate != null)
                foreach (int i in InformationSetsToMutate)
                {
                    SuccessfulMutations[i]++;
                    NumSuccessfulMutations++;
                }
        }

        public void ReplaceWith(GeneticPopulationMember mom, GeneticPopulationMember dad, int iteration)
        {
            ConsistentRandomSequenceProducer r = GetRandomProducer(iteration);
            for (int i = 0; i < InformationSetsCount; i++)
            {
                InformationSetNode informationSet = InformationSets[i];
                if (r.NextInt(2) == 0)
                    CopyInformationSetFromOtherPopulationMember(mom, i);
                else
                    CopyInformationSetFromOtherPopulationMember(dad, i);
            }
        }
    }
}
