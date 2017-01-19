using System;
using System.Text;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ACESim;
using System.Diagnostics;

namespace ACESimTest
{
    public class FakeGenome
    {
        public double Number;
        public double Score;
        public int? SurvivedToRound;
        public int OrderingGroup;

        public void Mutate(double MutationSize)
        {
            double change = RandomGenerator.NextDouble() * MutationSize;
            if (RandomGenerator.NextDouble() < 0.5)
                Number += change;
            else
                Number -= change;
        }

        public void CalcScore(double realNumber, double noise)
        {
            double obfuscation = noise * (double)alglib.normaldistr.invnormaldistribution(RandomGenerator.NextDouble());
            double obfuscated = realNumber + obfuscation;
            Score = Math.Abs(Number - obfuscated);
        }
    }

    [TestClass]
    public class MiniGA
    {
        const int startingGenomes = 500;
        FakeGenome[] genomes = new FakeGenome[startingGenomes];
        const int numGenerations = 1000;
        int virtualIterations = 0;

        internal double GetCurve(double fromVal, double toVal, double curvature, double input)
        {
            double adjustedProportion = (double)Math.Pow(input, curvature);
            return fromVal + (toVal - fromVal) * adjustedProportion;
        }

        internal void MutateUnsuccessful(int numToSkip, double mutationSize)
        {
            for (int i = numToSkip; i < startingGenomes; i++)
            {
                genomes[i].Number = genomes[0].Number;
                genomes[i].Mutate(mutationSize);
            }
        }

        internal void ExecuteNarrowDownPlan(int numberRemaining, NarrowDownPlanOld plan, double realNumber, int maxIterations)
        {
            double noise = GetCurve(2.0, 0.01, 1.0, ((double)plan.NumberIterations) / (double) maxIterations);
            Score(numberRemaining, realNumber, noise );
            virtualIterations += numberRemaining * plan.NumberIterations;
            Order(plan.NumberSurvivors);
        }

        internal void Score(int numToScore, double realNumber, double noise)
        {
            for (int i = 0; i < numToScore; i++)
                genomes[i].CalcScore(realNumber, noise);
        }

        int? lastNarrowDownTo = null;
        internal void Order(int narrowDownTo)
        {
            foreach (var genome in genomes)
            {
                // anything that in the last generation survived to a number smaller than this one goes first, with those 
                // that survived longer placed earlier than those that did not survive as long
                if (genome.SurvivedToRound != null && genome.SurvivedToRound < narrowDownTo)
                    genome.OrderingGroup = (int)genome.SurvivedToRound;
                else
                    genome.OrderingGroup = 999999;
            }
            // We order by this, and within group, by score (lowest score is best).
            genomes = genomes.OrderBy(x => x.OrderingGroup).ThenBy(x => x.Score).ToArray();
            for (int i = 0; i < narrowDownTo; i++)
                if (genomes[i].SurvivedToRound == null || genomes[i].SurvivedToRound > narrowDownTo)
                    genomes[i].SurvivedToRound = narrowDownTo;
            if (lastNarrowDownTo == null || lastNarrowDownTo < narrowDownTo)
                lastNarrowDownTo = genomes.Length;
            for (int i = narrowDownTo; i < genomes.Length; i++)
                if (genomes[i].SurvivedToRound == null || genomes[i].SurvivedToRound < narrowDownTo)
                    genomes[i].SurvivedToRound = lastNarrowDownTo;
            lastNarrowDownTo = narrowDownTo;
        }

        [TestMethod]
        public void TestMethod1()
        {
            for (int i = 0; i < genomes.Length; i++)
                genomes[i] = new FakeGenome() { Number = RandomGenerator.NextDouble(), Score = 0, SurvivedToRound = null };

            List<NarrowDownPlanOld> plans = new List<NarrowDownPlanOld>()
            {
                new NarrowDownPlanOld() { NumberIterations = 100, NumberSurvivors = 50 },
                new NarrowDownPlanOld() { NumberIterations = 1000, NumberSurvivors = 20 },
                new NarrowDownPlanOld() { NumberIterations = 10000, NumberSurvivors = 10 },
                new NarrowDownPlanOld() { NumberIterations = 100000, NumberSurvivors = 4 },
                new NarrowDownPlanOld() { NumberIterations = 1000000, NumberSurvivors = 1 }
            };

            double mutationSize = 1.0;
            FakeGenome lastBest = null;
            for (int g = 0; g < numGenerations; g++)
            {
                int numberRemaining = genomes.Length;

                foreach (var plan in plans)
                {
                    ExecuteNarrowDownPlan(numberRemaining, plan, 0.123456789, plans.Last().NumberIterations);
                    numberRemaining = plan.NumberSurvivors;
                    MutateUnsuccessful(numberRemaining, mutationSize);
                }

                if (lastBest == genomes[0])
                    mutationSize *= 0.6;
                lastBest = genomes[0];
            }

            Debug.WriteLine("Virtual iterations: " + virtualIterations + " Final result: " + genomes[0].Number);
        }

        [TestMethod]
        public void TestMethod2()
        {
            int repetitions = 1000;
            int numTimesWithinConfidenceInterval = 0;
            for (int i = 0; i < repetitions; i++)
            {
                StatCollector coll = new StatCollector();

                //int[] tmp = new int[] { 60, 72, 64, 67, 70, 68, 71, 68, 73, 59 };
                //foreach (int t in tmp)
                //    coll.Add((double) t);

                int multiplier = 5;
                int lastDone = 0;
                for (int numObs = 10; numObs <= 100000; numObs *= multiplier)
                {
                    for (; lastDone < numObs; lastDone++)
                    {
                        double item = 100.0 * (double)alglib.normaldistr.invnormaldistribution(RandomGenerator.NextDouble());
                        coll.Add(item);
                    }
                    Debug.WriteLine(String.Format("Obs {0} Avg {1} StDev {2} ConfInterval {3}", numObs, coll.Average(), coll.StandardDeviation(), coll.ConfInterval()));
                }
                if (Math.Abs(coll.Average()) < Math.Abs(coll.ConfInterval()))
                    numTimesWithinConfidenceInterval++;
            }
            Debug.WriteLine("Pct of time within 95% confidence interval: " + (double) numTimesWithinConfidenceInterval / (double) repetitions);
        }
    }
}
