//
// Encog(tm) Core v3.0 - .Net Version
// http://www.heatonresearch.com/encog/
//
// Copyright 2008-2011 Heaton Research, Inc.
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//  http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
//   
// For more information on Heaton Research copyrights, licenses 
// and trademarks visit:
// http://www.heatonresearch.com/copyright
//
using System;
using Encog.MathUtil;
using System.Collections.Generic;
using System.Linq;
using ACESim;

namespace Encog.Neural.Networks.Training.Anneal
{
    /// <summary>
    /// Simulated annealing is a common training method. This class implements a
    /// simulated annealing algorithm that can be used both for neural networks, as
    /// well as more general cases. This class is abstract, so a more specialized
    /// simulated annealing subclass will need to be created for each intended use.
    /// This book demonstrates how to use the simulated annealing algorithm to train
    /// feedforward neural networks, as well as find a solution to the traveling
    /// salesman problem.
    /// The name and inspiration come from annealing in metallurgy, a technique
    /// involving heating and controlled cooling of a material to increase the size
    /// of its crystals and reduce their defects. The heat causes the atoms to become
    /// unstuck from their initial positions (a local minimum of the internal energy)
    /// and wander randomly through states of higher energy; the slow cooling gives
    /// them more chances of finding configurations with lower internal energy than
    /// the initial one.
    /// </summary>
    ///
    /// <typeparam name="TUnitType">What type of data makes up the solution.</typeparam>
    public abstract class SimulatedAnnealingGoffe<TUnitType> where TUnitType : IComparable
    {
        /// <summary>
        /// The number of cycles that will be used.
        /// </summary>
        ///
        private int _cycles;

        /// <summary>
        /// Should the score be minimized.
        /// </summary>
        ///
        private bool _shouldMinimize;

        /// <summary>
        /// The current temperature.
        /// </summary>
        ///
        private double _temperature;

        /// <summary>
        /// Construct the object.  Default ShouldMinimize to true.
        /// </summary>
        protected SimulatedAnnealingGoffe()
        {
            _shouldMinimize = true;
        }

        /// <summary>
        /// Subclasses must provide access to an array that makes up the solution.
        /// </summary>
        public abstract TUnitType[] Array { 
            get; }


        /// <summary>
        /// Get a copy of the array.
        /// </summary>
        public abstract TUnitType[] ArrayCopy {
            get; }


        /// <value>the cycles to set</value>
        public int Cycles
        {
            get { return _cycles; }
            set { _cycles = value; }
        }

        /// <value>the temperature to set</value>
        public double Temperature
        {
            get { return _temperature; }
            set { _temperature = value; }
        }

        /// <value>the startTemperature to set</value>
        public double StartTemperature { get; set; }


        /// <value>the stopTemperature to set</value>
        public double TemperatureReductionFactor { get; set; }

        /// <summary>
        /// Only relevant if dynamically adjusting move size. 
        /// </summary>
        public double StartMoveSize { get; set; }
        public double EndMoveSize { get; set; }
        public double CurvatureFromStartToEndMoveSize { get; set; }


        public double LowerBound = -10.0; // default lower bound setting
        public double UpperBound = 10.0;

        /// <summary>
        /// Should the score be minimized.
        /// </summary>
        public bool ShouldMinimize
        {
            get { return _shouldMinimize; }
            set { _shouldMinimize = value; }
        }

        public bool RandomizeOnlyOneValueAtATime = false; // in original paper, value was true
        public bool ResetCurrentArrayToBestEachCycle = true; // int original paper, value was false // should be true with noisy data, so we don't get too far off course
        public bool ConsiderMovingToInferiorLocation = true; // in original paper, value was true
        public bool DynamicallyAdjustTemperatureInsteadOfMoveSize = true; // in original paper, value was false
        public bool TemperatureRepresentsScoreCutoff = true; // in original paper, value was false
        public bool BlockMovesToLocationWithIdenticalScore = true; // in original paper, value was false

        public bool DataIsNoisy = true; // if data is noisy, we will need to repeatedly call the PerformCalculateScore function on different data samples to refine our estimates of the best score and the current score

        // We will continue to refine scores until the maximum possible distance between the current score and the best score, taking into account the confidence interval of the current score and the best score, is less than the current precision desired, which is set to one tenth the average absolute change in score with each move.
        public StatCollector currentScore; // the score for the current point being evaluated, based on one or more calls to PerformCalculateScore
        public StatCollector bestScore;

        public virtual double PerformCalculateScore()
        {
            StatCollector s = new StatCollector();
            PerformCalculateScore(0, s);
            return s.Average();
        }

        public virtual void PerformCalculateScore(int dataSample, StatCollector s)
        {
        }

        int numberProcessed = 0;
        int numberMovesAccepted = 0;
        int numberTimesBestReplaced = 0;
        int numberTimesCompleteNewBest = 0;

        TUnitType[] currentArray = null;
        TUnitType[] preRandomizedValue = null;
        int H = 0;
        int N;
        public double[] MoveSizeVM;
        double[] MoveSizeVMAtTimeOfBest;
        TUnitType[] bestArray;
        int[] NACP;
        double avgAccepted = -1;
        bool reportEachCycle = false;
        bool reportPeriodically = true;


        private void InitialProcessing()
        {
            currentScore = new StatCollector();
            PerformCalculateScore(0, currentScore);
            bestScore = currentScore.DeepCopy();
            bestArray = ArrayCopy.ToArray();
            currentArray = ArrayCopy.ToArray();
            N = bestArray.Count();
        }


        private void SetCurrentToBest()
        {
            currentArray = bestArray.ToArray();
            PutArray(currentArray);
            currentScore = bestScore.DeepCopy();

            if (MoveSizeVMAtTimeOfBest != null)
                MoveSizeVM = MoveSizeVMAtTimeOfBest.ToArray(); // maybe we've wandered off from the best. let's return VM to what it was, but note that temperature will still go down.
        }

        public void Iteration()
        {
            InitialProcessing();
            MoveSizeVM = new double[N];
            for (int d = 0; d < N; d++)
            {
                MoveSizeVM[d] = 1.0;
            }

            _temperature = StartTemperature;

            SetCurrentToBest();
            int NT = 10; // alternative: (int)Math.Max(100, 5 * N);
            const int NS = 20;

            for (int i = 0; i < _cycles; i++)
            {
                if (ResetCurrentArrayToBestEachCycle)
                    SetCurrentToBest();
                for (int M = 0; M < NT; M++) // number of times before updating temperature
                {
                    NACP = new int[N];
                    for (int nacp = 0; nacp < N; nacp++)
                        NACP[nacp] = 0;
                    for (int J = 0; J < NS; J++) // number of times inner loop
                    {
                        preRandomizedValue = new TUnitType[N];
                        for (H = 0; H < N; H++)
                        {

                            TUnitType randomizedValue = Randomize(H, LowerBound, UpperBound);
                            preRandomizedValue[H] = currentArray[H];
                            currentArray[H] = randomizedValue;

                            if (RandomizeOnlyOneValueAtATime)
                                DetermineWhetherToAcceptNewLocation();
                        } // H


                        if (!RandomizeOnlyOneValueAtATime)
                            DetermineWhetherToAcceptNewLocation();
                    } // J
                    for (int I = 0; I < N; I++)
                    {
                        double proportionAccepted = (double)NACP[I] / (double)NS;
                        MakeDynamicAdjustments(i, I, proportionAccepted);
                    }
                } // M

                double previousTemperature = _temperature;
                if (!DynamicallyAdjustTemperatureInsteadOfMoveSize)
                    _temperature *= TemperatureReductionFactor;
                Report(i, previousTemperature);
                if (_temperature == 0 && !DynamicallyAdjustTemperatureInsteadOfMoveSize) // early abort -- can't get any better
                    i = Cycles + 1;
                NarrowDownCandidateBestToSingleBest();
            } // i (cycles)
            System.Diagnostics.Debug.WriteLine("Best score: " + bestScore);
            PutArray(bestArray);
        }

        private void Report(int i, double previousTemperature)
        {
            int numReports = 100;
            if (reportEachCycle || (reportPeriodically &&
                (i <= 1 || i == _cycles - 1 || i % (_cycles / numReports) == 0)))
            {
                System.Diagnostics.Debug.WriteLine("Cycle " + i + " of " + _cycles);
                System.Diagnostics.Debug.WriteLine("curr: " + currentScore.Average() + " based on " + currentScore.Num() + " " + String.Join(",", currentArray.Select(y => y.ToString()).ToArray()));
                System.Diagnostics.Debug.WriteLine("best: " + bestScore.Average() + " based on " + bestScore.Num() + " " + String.Join(",", bestArray.Select(y => y.ToString()).ToArray()));
                double scoreDistanceThatProducesMoveHalfOfTime = 0 - Math.Log(0.5) * _temperature;
                System.Diagnostics.Debug.WriteLine("Temperature was: " + previousTemperature);
                if (!DynamicallyAdjustTemperatureInsteadOfMoveSize)
                    System.Diagnostics.Debug.WriteLine("Distance a score can be inferior to current score and still produce moves half of time: " + scoreDistanceThatProducesMoveHalfOfTime);
                avgAccepted = (double)NACP.Average() / (double)20;
                System.Diagnostics.Debug.Write("Average accepted " + avgAccepted);
                System.Diagnostics.Debug.Write(" Average move size " + (double)MoveSizeVM.Average());
                System.Diagnostics.Debug.WriteLine(" Number processed " + numberProcessed + " number moves accepted " + numberMovesAccepted + " number times best replaced " + numberTimesBestReplaced + " with brand new best " + numberTimesCompleteNewBest);
            }
        }

        double temperatureAsCutoffDelta = 0.5;
        const double tooManyAcceptedThreshold = 0.05;
        const double tooFewAcceptedThreshold = 0.03;
        bool lastTimeTooManyAccepted = true;
        private void MakeDynamicAdjustments(int currentCycle, int currentIndex, double proportionAccepted)
        {
            if (DynamicallyAdjustTemperatureInsteadOfMoveSize)
            {
                if (currentIndex == 0)
                {
                    double adjustedProportion = Math.Pow((double)(currentCycle + 1) / (double)Cycles, CurvatureFromStartToEndMoveSize);
                    MoveSizeVM[currentIndex] = StartMoveSize + (EndMoveSize - StartMoveSize) * adjustedProportion;
                    if (TemperatureRepresentsScoreCutoff)
                    { // temperature can either be positive or negative, and it is added to the score achieved to determine whether a new move will be allowed.
                        bool makingChange = proportionAccepted > tooManyAcceptedThreshold || proportionAccepted < tooFewAcceptedThreshold;
                        if (makingChange)
                        {
                            // make the appropriate change to the temperature
                            // first, assume that we're maximizing the score
                            bool increaseTemperature = proportionAccepted < tooFewAcceptedThreshold; // since we're maximizing, we need to boost the score received more so that more will be accepted
                            // now reverse this if we're minimizing
                            if (_shouldMinimize)
                                increaseTemperature = !increaseTemperature;

                            if (increaseTemperature)
                                _temperature += temperatureAsCutoffDelta;
                            else
                                _temperature -= temperatureAsCutoffDelta;

                            // change the size of the delta to keep things in equilibrium
                            if (proportionAccepted > tooManyAcceptedThreshold == lastTimeTooManyAccepted)
                                temperatureAsCutoffDelta *= 1.01; // ratchet up the delta so that we get there faster
                            else
                                temperatureAsCutoffDelta *= 0.99; // we're in approximate equilibrium, so we don't need as big a change


                            lastTimeTooManyAccepted = proportionAccepted > tooManyAcceptedThreshold;
                        }
                    }
                    else
                    { // temperature should always be a positive number, which effectively determines whether inferior scores can still be accepted
                        if (proportionAccepted > tooManyAcceptedThreshold)
                            _temperature *= 0.99;
                        else if (proportionAccepted < tooFewAcceptedThreshold)
                            _temperature *= 1.01;
                    }
                }
                else
                    MoveSizeVM[currentIndex] = MoveSizeVM[0];
            }
            else
            { // dynamically adjust move size based on temperature
                const double increaseThreshold = 0.60; // set to 0.6 in Golfe
                const double decreaseThreshold = 0.40; // set to 0.4 in Golfe
                if (proportionAccepted > increaseThreshold)
                    MoveSizeVM[currentIndex] *= (1 + 2.0 * (proportionAccepted - increaseThreshold) / decreaseThreshold);
                else if (proportionAccepted < .4)
                    MoveSizeVM[currentIndex] /= (1 + 2.0 * (decreaseThreshold - proportionAccepted) / decreaseThreshold);
                if (MoveSizeVM[currentIndex] > UpperBound - LowerBound)
                    MoveSizeVM[currentIndex] = UpperBound - LowerBound;
            }
        }

        // A candidate best score is a point that has been displaced as best score but still has the best possible score for as many observations as it has processed.
        internal class CandidateBestScore
        {
            public StatCollector stat;
            public TUnitType[] candidateArray;
            public int num;
            public double avg;
            public int hash;
        }
        internal List<CandidateBestScore> bestScoreCandidates = new List<CandidateBestScore>();

        private void CheckBestScoreCandidates()
        {
            if (bestScoreCandidates.Any())
            {
                if (bestScore.Average() > bestScoreCandidates[0].avg == _shouldMinimize)
                { // the best score is now worse than one of the old best scores that had more observations, so we need to switch the old best score to the best score
                    bool report = false;
                    if (report)
                    {
                        foreach (var x in bestScoreCandidates)
                            System.Diagnostics.Debug.WriteLine(x.avg + " " + x.num + " " + x.hash);
                    }
                    StatCollector newBestScore = bestScoreCandidates[0].stat;
                    TUnitType[] newBestArray = bestScoreCandidates[0].candidateArray.ToArray();
                    bestScoreCandidates.RemoveAt(0);
                    ReplaceBestScore(newBestScore, newBestArray);
                }
                while (bestScoreCandidates.Count > 0 && bestScoreCandidates[0].num < bestScore.Num())
                    bestScoreCandidates.RemoveAt(0);
            }
        }

        private void ReplaceBestScore(StatCollector newBestScore, TUnitType[] newBestArray)
        {
            double previousBestScoreAvg = bestScore.Average();
            int previousBestScoreNum = (int) bestScore.Num();
            int previousBestHash = bestArray.GetHashCode();
            CandidateBestScore possibleNewCandidate = new CandidateBestScore() { stat = bestScore, candidateArray = bestArray.ToArray(), num = previousBestScoreNum, avg = previousBestScoreAvg, hash = previousBestHash };

            // should we still track the now displaced best score, as it could become best score again?
            // We should if it has a better score than anything on the list
            bool stillTrack = bestScoreCandidates.Any(x => (previousBestScoreAvg < x.avg == _shouldMinimize) && previousBestScoreNum < x.num);
            if (stillTrack)
                bestScoreCandidates = bestScoreCandidates
                    .Where(x => /* (x.hash != previousBestHash) && */ x.avg < previousBestScoreAvg || x.num > previousBestScoreNum)
                    .Concat(new List<CandidateBestScore>() { possibleNewCandidate })
                    .OrderBy(x => x.avg).ToList();

            if (bestScoreCandidates.Count(x => x.hash == previousBestHash) == 2)
                throw new Exception("Here.");

            // We also should if it has more observations than anything on the list
            if (!stillTrack && ((!bestScoreCandidates.Any() && previousBestScoreNum > newBestScore.Num() ) || ( bestScoreCandidates.Any() && previousBestScoreNum > bestScoreCandidates[bestScoreCandidates.Count - 1].num)))
            {
                stillTrack = true;
                bestScoreCandidates.Add(possibleNewCandidate);
            }

            // make the actual change
            bestScore = newBestScore;
            bestArray = newBestArray;
            numberTimesBestReplaced++;
            if (!stillTrack)
                numberTimesCompleteNewBest++;
            MoveSizeVMAtTimeOfBest = MoveSizeVM.ToArray();
        }

        private void NarrowDownCandidateBestToSingleBest()
        {
            while (bestScoreCandidates.Any())
            {
                PerformCalculateScore((int)bestScore.Num(), bestScore);
                CheckBestScoreCandidates();
            }
        }

        private void DoMoreScoreCalculating(StatCollector scoreForExistingPoint, StatCollector scoreForPossiblePoint)
        {
            if (scoreForExistingPoint.Num() == 0)
                PerformCalculateScore((int)scoreForExistingPoint.Num(), scoreForExistingPoint);
            if (scoreForPossiblePoint.Num() == 0)
                PerformCalculateScore((int)scoreForPossiblePoint.Num(), scoreForPossiblePoint);
            if (scoreForExistingPoint.Num() <= scoreForPossiblePoint.Num())
                PerformCalculateScore((int)scoreForExistingPoint.Num(), scoreForExistingPoint);
            else
                PerformCalculateScore((int)scoreForPossiblePoint.Num(), scoreForPossiblePoint);
        }

        private void ContinueCalculatingScoreUntilPerformanceIsClearEnough(StatCollector scoreForExistingPoint, StatCollector scoreForPossiblePoint, double scoreBiasForPossiblePoint, double accuracyThreshold, bool checkKeepBestScore)
        {
            if (!DataIsNoisy)
            {
                if (scoreForExistingPoint.Num() == 0)
                    PerformCalculateScore((int)scoreForExistingPoint.Num(), scoreForExistingPoint);
                if (scoreForPossiblePoint.Num() == 0)
                    PerformCalculateScore((int)scoreForPossiblePoint.Num(), scoreForPossiblePoint);
                return;
            }
            const int minRepetitions = 1;
            const int maxRepetitions = 1000000;
            int repetition = 0;
            bool sufficientAccuracyAchieved = false;
            while ((!sufficientAccuracyAchieved || repetition < minRepetitions) && repetition < maxRepetitions)
            {
                repetition++;
                DoMoreScoreCalculating(scoreForExistingPoint, scoreForPossiblePoint);
                if (checkKeepBestScore)
                    CheckBestScoreCandidates();
                double currentScoreForExistingPoint = scoreForExistingPoint.Average();
                double currentScoreForPossiblePoint = scoreForPossiblePoint.Average() + scoreBiasForPossiblePoint;
                double confInvForExistingPoint = scoreForExistingPoint.ConfInterval();
                double confInvForPossiblePoint = scoreForPossiblePoint.ConfInterval();
                double confidenceIntervalsOverlap;
                if (currentScoreForExistingPoint > currentScoreForPossiblePoint)
                {
                    confidenceIntervalsOverlap = (currentScoreForPossiblePoint + confInvForPossiblePoint) /* highest possible value */ - (currentScoreForExistingPoint - confInvForExistingPoint) /* lowest possible value */;
                    if (confidenceIntervalsOverlap < 0)
                        confidenceIntervalsOverlap = 0; // no overlap at all
                }
                else
                {
                    confidenceIntervalsOverlap = (currentScoreForExistingPoint + confInvForExistingPoint) /* highest possible value */ - (currentScoreForPossiblePoint - confInvForPossiblePoint) /* lowest possible value */;
                    if (confidenceIntervalsOverlap < 0)
                        confidenceIntervalsOverlap = 0; // no overlap at all
                }
                // check out the distance -- it doesn't matter whether we are minimizing or maximizing here
                sufficientAccuracyAchieved = confidenceIntervalsOverlap < accuracyThreshold || (confInvForExistingPoint < accuracyThreshold && confInvForPossiblePoint < accuracyThreshold) || accuracyThreshold == 0;
            }
        }

        List<double> scoreMoveDistance = new List<double>();
        double totalScoreMoveDistance = 0;
        double averageDistance;
        private void UpdateAverageDistanceBetweenScores(double previousScore, double newScore)
        {
            double newDistance = Math.Abs(previousScore - newScore);
            scoreMoveDistance.Add(newDistance);
            totalScoreMoveDistance += newDistance;
            if (scoreMoveDistance.Count > 1000)
            {
                totalScoreMoveDistance -= scoreMoveDistance[0];
                scoreMoveDistance.RemoveAt(0);
            }
            averageDistance = totalScoreMoveDistance / (double)(scoreMoveDistance.Count);
        }

        const double averageDistanceMultiplierToMoveToNewPoint = 1000.0;
        const double averageDistanceMultiplierToMoveToUpdateBest = 100.0;
        private void DetermineWhetherToAcceptNewLocation()
        {
            StatCollector possiblePointScore = new StatCollector();
            PerformCalculateScore(0, possiblePointScore);

            double scoreBias = 0;
            if (TemperatureRepresentsScoreCutoff)
                scoreBias = _temperature;
            
            ContinueCalculatingScoreUntilPerformanceIsClearEnough(currentScore, possiblePointScore, scoreBias, averageDistance * averageDistanceMultiplierToMoveToNewPoint, false);
            bool shouldRemainInNewLocation;
            if (_shouldMinimize)
                shouldRemainInNewLocation = possiblePointScore.Average() + scoreBias < currentScore.Average();
            else
                shouldRemainInNewLocation = possiblePointScore.Average() + scoreBias > currentScore.Average();

            if (!BlockMovesToLocationWithIdenticalScore || possiblePointScore.Average() != currentScore.Average() )
            {
                if (shouldRemainInNewLocation)
                {
                    if (RandomizeOnlyOneValueAtATime)
                        NACP[H]++;
                    else for (int h = 0; h < N; h++)
                            NACP[h]++;
                    numberMovesAccepted++;

                    ContinueCalculatingScoreUntilPerformanceIsClearEnough(bestScore, possiblePointScore, scoreBias, averageDistance * averageDistanceMultiplierToMoveToUpdateBest, true);
                    bool shouldUpdateBestArray;
                    if (_shouldMinimize)
                        shouldUpdateBestArray = possiblePointScore.Average() < bestScore.Average();
                    else
                        shouldUpdateBestArray = possiblePointScore.Average() > bestScore.Average();
                    if (shouldUpdateBestArray)
                    {
                        ReplaceBestScore(possiblePointScore.DeepCopy(), currentArray.ToArray());
                    }
                }
                else if (ConsiderMovingToInferiorLocation && !TemperatureRepresentsScoreCutoff)
                { // we should still keep the randomized current location (but not change the best location) if the Metropolis criterion is met
                    double metropolisCriterionP = Math.Exp((0 - Math.Abs(currentScore.Average() - possiblePointScore.Average())) / _temperature);
                    double pprime = ThreadSafeRandom.NextDouble();
                    shouldRemainInNewLocation = (metropolisCriterionP > pprime);
                    if (shouldRemainInNewLocation)
                    {
                        if (RandomizeOnlyOneValueAtATime)
                            NACP[H]++;
                        else for (int h = 0; h < N; h++)
                                NACP[h]++;
                        numberMovesAccepted++;
                    }
                }
            }

            UpdateAverageDistanceBetweenScores(currentScore.Average(), possiblePointScore.Average());

            if (shouldRemainInNewLocation)
                currentScore = possiblePointScore.DeepCopy();
            else
            { // move back
                UndoLastRandomize(RandomizeOnlyOneValueAtATime);
            }

            //PutArray(currentArray);

            numberProcessed++;
        }

        /// <summary>
        /// Store the array.
        /// </summary>
        ///
        /// <param name="array">The array to be stored.</param>
        public abstract void PutArray(TUnitType[] array);
        public abstract TUnitType[] GetArray();

        /// <summary>
        /// Randomize a particular index in the weight matrix.
        /// </summary>
        ///
        public abstract TUnitType Randomize(int i, double lowerBound, double upperBound);

        public virtual void UndoLastRandomize(bool oneValueOnly)
        {
            if (oneValueOnly)
                currentArray[H] = preRandomizedValue[H];
            else for (int h = 0; h < N; h++)
                currentArray[h] = preRandomizedValue[h];
        }
    }

    public class FixedSizedBoolQueue
    {
        Queue<bool> q = new Queue<bool>();
        public int numberEntries = 0;
        public int numberTrue = 0;
        public double ProportionTrue { get { return (double)numberTrue / (double)numberEntries; } }

        public int Limit { get; set; }
        public void Enqueue(bool item)
        {
            q.Enqueue(item);
            numberEntries++;
            if (item == true)
                numberTrue++;
            while (q.Count > Limit)
            {
                bool dequeued = q.Dequeue();
                numberEntries--;
                if (dequeued == true)
                    numberTrue--;
            }
        }

        public bool[] ToArray()
        {
            return q.ToArray();
        }
    }
}
