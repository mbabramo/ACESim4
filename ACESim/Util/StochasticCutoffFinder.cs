using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ACESim
{
    // The way stochastic cutoff finder works is it calls a game specifying what the tentative cutoff is. The game looks at the variable that would be 
    // compared with the cutoff, and if they're not within some set range, the game aborts. Otherwise, the game returns a score. The game must be called
    // both with a value that makes it honor the cutoff and with a value that makes it not honor the cutoff, so that we can see what the difference is.
    [Serializable]
    public class StochasticCutoffFinderInputs
    {
        public double? TentativeCutoff;
        public double? MaxRangeFromCutoff;
    }

    [Serializable]
    public class StochasticCutoffFinderOutputs
    {
        public double InputVariable; // we are looking to make a cutoff among different values of input variable, but must play game to see where input variable is
        public double Score;
        public double Weight;
    }

    // RemoteCutoffExecutorBase implements most of this interface. See OptimizePointsAndSmoothRemoteCutoffExecutor for an example of a class inheriting from that.
    public interface IRemoteCutoffExecutor
    {
        void Reset(int numRepetitionsToDo, StochasticCutoffFinderInputs scfi, long numIterations, int chunkSizeForRemoting, int numSeparateStatCollectors, double lowerBound, double upperBound);
        void RecoverState(ref Dictionary<string, Strategy> alreadyDeserializedStrategies, ref Tuple<string, OptimizePointsAndSmooth> lastOptimizePointsAndSmooth);
        StatCollector[] PlayCycleWhenExecutedRemotely(int indexNum, CancellationToken ct);
        StochasticCutoffFinderOutputs PlaySingleIterationIfNearEnoughCutoff(StochasticCutoffFinderInputs scfi, long iter);
        bool ProcessCompletedTask(object outputObject, int index);
        object GetCompletionInformation();
    }


    // This is a base class for executing the cutoff function remotely. The class that inherits from this must define a function playing a specific iteration.
    [Serializable]
    public abstract class RemoteCutoffExecutorBase : IRemoteCutoffExecutor
    {
        int NumRepetitionsRemaining;
        StatCollector[] StatCollectors;
        StochasticCutoffFinderInputs Scfi;
        long NumIterations;
        int ChunkSizeForRemoting;
        int NumSeparateStatCollectors;
        double LowerBound;
        double UpperBound;

        public object GetCompletionInformation()
        {
            return StatCollectors;
        }

        public virtual void Reset(int numRepetitionsToDo, StochasticCutoffFinderInputs scfi, long numIterations, int chunkSizeForRemoting, int numSeparateStatCollectors, double lowerBound, double upperBound)
        {
            StatCollectors = null;
            NumRepetitionsRemaining = numRepetitionsToDo;
            Scfi = scfi;
            NumIterations = numIterations;
            ChunkSizeForRemoting = chunkSizeForRemoting;
            NumSeparateStatCollectors = numSeparateStatCollectors;
            LowerBound = lowerBound;
            UpperBound = upperBound;
        }

        public virtual void RecoverState(ref Dictionary<string, Strategy> alreadyDeserializedStrategies, ref Tuple<string, OptimizePointsAndSmooth> lastOptimizePointsAndSmooth)
        {
        }

        public StatCollector[] PlayCycleWhenExecutedRemotely(int indexNum, CancellationToken ct)
        {
            StatCollector[] localStatCollectors = new StatCollector[NumSeparateStatCollectors];
            for (int i = 0; i < NumSeparateStatCollectors; i++)
                localStatCollectors[i] = new StatCollector();

            Func<double, int> cutoffToStatCollectorIndex = x => (int)((double)NumSeparateStatCollectors * (x - LowerBound) / (UpperBound - LowerBound));
            StochasticCutoffFinder.DetermineOptimalCutoff_PlayIterationsForCycle(false, localStatCollectors, ChunkSizeForRemoting, indexNum * ChunkSizeForRemoting, PlaySingleIterationIfNearEnoughCutoff, this, Scfi, NumSeparateStatCollectors, cutoffToStatCollectorIndex, ct);
            return localStatCollectors;
        }

        public abstract StochasticCutoffFinderOutputs PlaySingleIterationIfNearEnoughCutoff(StochasticCutoffFinderInputs scfi, long iter);

        public bool ProcessCompletedTask(object outputObject, int index)
        {
            StatCollector[] scs = (StatCollector[])outputObject;
            if (StatCollectors == null)
                StatCollectors = scs;
            else
            {
                for (int i = 0; i < scs.Length; i++)
                    StatCollectors[i].Aggregate(scs[i]);
            }

            NumRepetitionsRemaining--;
            if (NumRepetitionsRemaining <= 0)
                return true;
            else
                return false;
        }
    }

    [Serializable]
    public static class StochasticCutoffFinder
    {

        public static double FindCutoff(bool doParallel, double lowerBound, double upperBound, long numIterations, long maxPossibleIterationNum, bool improveOptimizationOfCloseCases, int improveOptimizationOfCloseCasesForBipolarDecisionMultiplier, double improveOptimizationOfCloseCasesForBipolarDecisionProportionToScrutinize, bool highestIsBest, bool positiveScoresAreToLeftOfCutoff /* Note that in optimizepointsandsmooth, the "score" is actually the score from dropping - the score from not dropping, so this should be true */, Func<StochasticCutoffFinderInputs, long, StochasticCutoffFinderOutputs> playIteration, IRemoteCutoffExecutor remoteCutoffExecutor = null, bool useAzureWorkerRole = false, int chunkSizeForRemoting = 10000, double? previousCutoff = null)
        {
            const int numSeparateStatCollectors = 1000;
            StatCollector[] statCollectors = new StatCollector[numSeparateStatCollectors];
            for (int i = 0; i < numSeparateStatCollectors; i++)
                statCollectors[i] = new StatCollector();

            // start considering everything
            int startAtIteration = 0;
            StochasticCutoffFinderInputs scfInputs = new StochasticCutoffFinderInputs() { TentativeCutoff = null, MaxRangeFromCutoff = null };
            bool usedPreviousBest;
            double optimalCutoff = DetermineOptimalCutoff_OneCycle(doParallel, statCollectors, numIterations, startAtIteration, playIteration, lowerBound, upperBound, highestIsBest, positiveScoresAreToLeftOfCutoff, scfInputs, remoteCutoffExecutor, useAzureWorkerRole, chunkSizeForRemoting, previousCutoff, out usedPreviousBest);


            if (improveOptimizationOfCloseCases && !usedPreviousBest) // if we used the previous best value, that's because the data that we now have is based on a range not including the previous best, so there is no sense in trying to optimize
            {
                // do many more iterations, but ignore those not close to the apparent optimal cutoff
                startAtIteration += (int)numIterations;
                numIterations *= improveOptimizationOfCloseCasesForBipolarDecisionMultiplier;
                double rangeToScrutinize = improveOptimizationOfCloseCasesForBipolarDecisionProportionToScrutinize * (upperBound - lowerBound);
                scfInputs = new StochasticCutoffFinderInputs() { TentativeCutoff = optimalCutoff, MaxRangeFromCutoff = rangeToScrutinize / 2.0 };

                optimalCutoff = DetermineOptimalCutoff_OneCycle(doParallel, statCollectors, numIterations, startAtIteration, playIteration, lowerBound, upperBound, highestIsBest, positiveScoresAreToLeftOfCutoff, scfInputs, remoteCutoffExecutor, useAzureWorkerRole, chunkSizeForRemoting * improveOptimizationOfCloseCasesForBipolarDecisionMultiplier, null, out usedPreviousBest);
            }

            return optimalCutoff;
        }

        private static double DetermineOptimalCutoff_OneCycle(bool doParallel, StatCollector[] statCollectors, long numIterations, int startAtIteration, Func<StochasticCutoffFinderInputs, long, StochasticCutoffFinderOutputs> playIteration, double lowerBound, double upperBound, bool highestIsBest, bool positiveScoresAreToLeftOfCutoff, StochasticCutoffFinderInputs scfInputs, IRemoteCutoffExecutor remoteCutoffExecutor, bool useAzureWorkerRole, int chunkSizeForRemoting, double? previousBestValue, out bool usedPreviousValue)
        {
            int numSeparateStatCollectors = statCollectors.Length;
            Func<double, int> cutoffToStatCollectorIndex = x => (int)((double)numSeparateStatCollectors * (x - lowerBound) / (upperBound - lowerBound));
            int? previousBestIndex = null;
            if (previousBestValue != null)
                previousBestIndex = cutoffToStatCollectorIndex((double) previousBestValue);

            if (useAzureWorkerRole)
            {
                ProcessInAzureWorkerRole p = new ProcessInAzureWorkerRole();
                int numChunks = (int) Math.Ceiling(((double) numIterations) / (double) chunkSizeForRemoting);
                remoteCutoffExecutor.Reset(numChunks, scfInputs, numIterations, chunkSizeForRemoting, numSeparateStatCollectors, lowerBound, upperBound);
                p.ExecuteTask(remoteCutoffExecutor, "CutoffFinder", numChunks, true, remoteCutoffExecutor.ProcessCompletedTask);
                statCollectors = (StatCollector[]) remoteCutoffExecutor.GetCompletionInformation();
            }
            else
                DetermineOptimalCutoff_PlayIterationsForCycle(doParallel, statCollectors, numIterations, startAtIteration, playIteration, remoteCutoffExecutor, scfInputs, numSeparateStatCollectors, cutoffToStatCollectorIndex, new CancellationToken());

            int bestStatCollectorIndex = DetermineMostFavorableStatCollectorIndex(statCollectors, highestIsBest, positiveScoresAreToLeftOfCutoff, previousBestIndex, out usedPreviousValue);
            double optimalCutoff = lowerBound + (((double)bestStatCollectorIndex + 0.5) / (double)numSeparateStatCollectors) * (upperBound - lowerBound);
            return optimalCutoff;
        }

        public static void DetermineOptimalCutoff_PlayIterationsForCycle(bool doParallel, StatCollector[] statCollectors, long numIterations, int startAtIteration, Func<StochasticCutoffFinderInputs, long, StochasticCutoffFinderOutputs> playIteration, IRemoteCutoffExecutor remoteCutoffExecutor, StochasticCutoffFinderInputs scfInputs, int numSeparateStatCollectors, Func<double, int> cutoffToStatCollectorIndex, CancellationToken ct)
        {
            Parallelizer.Go(doParallel, startAtIteration, startAtIteration + numIterations, iteration =>
            {
                if (!ct.IsCancellationRequested)
                {
                    StochasticCutoffFinderOutputs scfOutputs;
                    if (remoteCutoffExecutor == null)
                        scfOutputs = playIteration(scfInputs, iteration);
                    else
                        scfOutputs = remoteCutoffExecutor.PlaySingleIterationIfNearEnoughCutoff(scfInputs, iteration);
                    if (scfOutputs != null)
                    {
                        int statCollectorIndex = cutoffToStatCollectorIndex(scfOutputs.InputVariable);
                        if (statCollectorIndex >= numSeparateStatCollectors)
                            statCollectorIndex = numSeparateStatCollectors - 1;
                        else if (statCollectorIndex < 0)
                            statCollectorIndex = 0;
                        statCollectors[statCollectorIndex].Add(scfOutputs.Score, scfOutputs.Weight);
                    }
                }
            }
            );
        }

        private static int DetermineMostFavorableStatCollectorIndex(StatCollector[] statCollectors, bool highestIsBest, bool positiveScoresAreToLeftOfCutoff, int? previousBestIndex, out bool usedPreviousBest)
        {
            usedPreviousBest = false;

            int firstStatCollectorWithData = 0;
            while (statCollectors[firstStatCollectorWithData].Num() == 0 && firstStatCollectorWithData < statCollectors.Length - 1)
                firstStatCollectorWithData++; 
            int lastStatCollectorWithData = statCollectors.Length - 1;
            while (statCollectors[lastStatCollectorWithData].Num() == 0 && lastStatCollectorWithData > 0)
                lastStatCollectorWithData--;
            if (lastStatCollectorWithData <= firstStatCollectorWithData)
                return statCollectors.Length / 2;


            double[] scores = new double[statCollectors.Length];
            double[] weights = new double[statCollectors.Length];
            for (int i = firstStatCollectorWithData; i <= lastStatCollectorWithData; i++)
            {
                scores[i] = statCollectors[i].Average();
                weights[i] = statCollectors[i].sumOfWeights;
            }

            bool printOutScores = false;
            if (printOutScores)
            {
                string scoreString = String.Join(", ", scores.Select((item, index) => index.ToString() + ": " + item.ToString()).ToArray());
                Debug.WriteLine(scoreString);
            }

            double weightedNetScoresToRightOfItem = 0;
            double sumWeightsToRightOfItem = 0;
            for (int i = firstStatCollectorWithData; i <= lastStatCollectorWithData; i++)
            {
                weightedNetScoresToRightOfItem += scores[i] * weights[i];
                sumWeightsToRightOfItem += weights[i];
            }
            double weightedNetScoresToLeftOfItem = 0;
            double sumWeightsToLeftOfItem = 0;

            int? bestIndexSoFar = null;
            double? bestSoFar = null;
            for (int i = firstStatCollectorWithData; i <= lastStatCollectorWithData; i++)
            {
                // i is the new item number. We will count the item number as being to the right of the cutoff.
                if (i > firstStatCollectorWithData)
                {
                    double weightedNetScoreToMoveFromRightToLeft = scores[i - 1] * weights[i - 1]; // the scores are already weighted within the stat collector, but now we weight across stat collectors
                    double sumWeightsToMoveFromRightToLeft = weights[i - 1]; // to do this weighting, we need the sum of the weights
                    weightedNetScoresToRightOfItem -= weightedNetScoreToMoveFromRightToLeft;
                    sumWeightsToRightOfItem -= sumWeightsToMoveFromRightToLeft;
                    weightedNetScoresToLeftOfItem += weightedNetScoreToMoveFromRightToLeft;
                    sumWeightsToLeftOfItem += sumWeightsToMoveFromRightToLeft;
                }

                double scoresToRightOfItem = (sumWeightsToRightOfItem == 0) ? 0 : weightedNetScoresToRightOfItem / sumWeightsToRightOfItem;
                double scoresToLeftOfItem = (sumWeightsToLeftOfItem == 0) ? 0 : weightedNetScoresToLeftOfItem / sumWeightsToLeftOfItem;
                double overallNetScoreIfThisIsCutoff = (scoresToRightOfItem * sumWeightsToRightOfItem - scoresToLeftOfItem * sumWeightsToLeftOfItem) / (sumWeightsToRightOfItem + sumWeightsToLeftOfItem);
                if (positiveScoresAreToLeftOfCutoff)
                    overallNetScoreIfThisIsCutoff = 0 - overallNetScoreIfThisIsCutoff; // since positive one is played to the left of the item, then we must subtract out the positiveOneMinusNegativeOneToRightOfItem, because we will be playing the negative one strategy to the right of the item. 

                if (bestIndexSoFar == null || (overallNetScoreIfThisIsCutoff > (double)bestSoFar == highestIsBest))
                {
                    bestIndexSoFar = i;
                    bestSoFar = overallNetScoreIfThisIsCutoff;
                }
            }

            // if we don't have data within the range of the previous cutoff and the best index appears to be very near the end of the range being searched, then use the old previous best
            if ((previousBestIndex != null && bestIndexSoFar < firstStatCollectorWithData + 2 && previousBestIndex < firstStatCollectorWithData)
                || (previousBestIndex != null && bestIndexSoFar > lastStatCollectorWithData - 2 && previousBestIndex > lastStatCollectorWithData))
            {
                TabbedText.WriteLine("Using previous best cutoff because cutoff appears to be out of range");
                usedPreviousBest = true;
                return (int)previousBestIndex;
            }

            return (int)bestIndexSoFar;
        }
    }

    [Serializable]
    public class RemoteCutoffTester
    {
        public StochasticCutoffFinderOutputs PlaySingleIterationIfNearEnoughCutoff(StochasticCutoffFinderInputs scfi, long iter)
        {
            // The function will take an input, subtract .12345, and then add some noise. Then add a negative sign (so that positive scores will be to the left of the cutoff). 
            // So, .345 is the optimal cutoff, if we want to include all inputs with expectation > 0.
            
            // We only apply the function if we are close enough to the cutoff.
            Func<double, bool> shouldApplyFunction = x =>
            {
                if (scfi.TentativeCutoff == null)
                    return true;
                return Math.Abs(x - (double)scfi.TentativeCutoff) < (double)scfi.MaxRangeFromCutoff;
            };
            
            const double optimalCutoff = 0.345;
            const double standardDeviationOfNoise = 6.0; // make it really large to make it stochastic
            double input, noiseSeed, noise, functionResult;
            input = FastPseudoRandom.GetRandom(iter, 0);
            noiseSeed = FastPseudoRandom.GetRandom(iter, 1);
            noise = standardDeviationOfNoise * alglib.normaldistr.invnormaldistribution(noiseSeed);
            functionResult = 0 - (input - optimalCutoff + noise);

            return new StochasticCutoffFinderOutputs() { InputVariable = input, Score = functionResult, Weight = 1.0 };
        }

        public void DoTest()
        {
            int numIterations = 1000000;
            // First, without Azure.
            double result = StochasticCutoffFinder.FindCutoff(true, 0.0, 1.0, numIterations, 999999999, true, 10, 0.1, true, true /* dropping if we're to left of here */, PlaySingleIterationIfNearEnoughCutoff);
            Debug.WriteLine("Cutoff finder test result without Azure: " + result);
            RemoteCutoffExecutorTester r = new RemoteCutoffExecutorTester(this);
            result = StochasticCutoffFinder.FindCutoff(true, 0.0, 1.0, numIterations, 999999999, true, 10, 0.1, true, true /* dropping if we're to left of here */, null, r, true, 10000);
            Debug.WriteLine("Cutoff finder test result with Azure: " + result);
        }
    }

    [Serializable]
    public class RemoteCutoffExecutorTester : RemoteCutoffExecutorBase
    {
        public RemoteCutoffTester RemoteCutoffTester;

        public RemoteCutoffExecutorTester(RemoteCutoffTester remoteCutoffTester)
        {
            RemoteCutoffTester = remoteCutoffTester;
        }

        public override StochasticCutoffFinderOutputs PlaySingleIterationIfNearEnoughCutoff(StochasticCutoffFinderInputs scfi, long iter)
        {
            return RemoteCutoffTester.PlaySingleIterationIfNearEnoughCutoff(scfi, iter);
        }

        public override void RecoverState(ref Dictionary<string, Strategy> alreadyDeserializedStrategies, ref Tuple<string, OptimizePointsAndSmooth> lastOptimizePointsAndSmooth)
        {
        }

    }
}
