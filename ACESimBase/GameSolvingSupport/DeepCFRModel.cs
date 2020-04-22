﻿using ACESim;
using ACESimBase.Util;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static alglib;

namespace ACESimBase.GameSolvingSupport
{
    public class DeepCFRModel
    {
        /// <summary>
        /// The players to whom this model belongs. 
        /// </summary>
        public List<byte> PlayerNumbers;
        /// <summary>
        /// The decision type(s)
        /// </summary>
        public List<byte> DecisionByteCodes;
        /// <summary>
        /// The decision indices.
        /// </summary>
        public List<byte> DecisionIndices;
        /// <summary>
        /// The model names
        /// </summary>
        public List<string> ModelNames;
        /// <summary>
        /// Observations used to create the most recent version of the model.
        /// </summary>
        public Reservoir<DeepCFRObservation> Observations;
        /// <summary>
        /// A copy of the observations at some point in time.
        /// </summary>
        public Reservoir<DeepCFRObservation> RememberedObservations;
        /// <summary>
        /// The number of iterations already processed.
        /// </summary>
        public int IterationsProcessed;
        /// <summary>
        /// The discount rate (the proportion of observations to keep at each iteration, before the replacement inversely proportional to the iteration number).
        /// </summary>
        public double DiscountRate;
        /// <summary>
        /// Observations to be added to the model at the end of the current iteration. Note that this is not thread-safe, so if using parallelism, we add in a consumer
        /// thread.
        /// </summary>
        public List<DeepCFRObservation> PendingObservations;
        /// <summary>
        /// The trained neural network.
        /// </summary>
        RegressionController Regression;
        /// <summary>
        /// The decision indices that are included in the independent variables of the regression
        /// </summary>
        List<byte> IncludedDecisionIndices;
        /// <summary>
        /// The number of additional observations that we would like to target in an iteration.
        /// </summary>
        int TargetToAdd;
        /// <summary>
        /// When the state is frozen, we adjust the target so that we will add no observations
        /// </summary>
        bool StateFrozen;
        /// <summary>
        /// Always seek to replace as many observations as possible in each iteration. This is useful 
        /// </summary>
        bool FullReservoirReplacement;
        /// <summary>
        /// Factory to create a regression processor
        /// </summary>
        Func<IRegression> RegressionFactory;
        /// <summary>
        /// The proportion of the data to be reserved for test data
        /// </summary>
        double TestDataProportion = 0.05;

        public DeepCFRModel(List<byte> playerNumbers, List<string> modelNames, List<byte> decisionByteCodes, List<byte> decisionIndices, int reservoirCapacity, long reservoirSeed, double discountRate, Func<IRegression> regressionFactory)
        {
            PlayerNumbers = playerNumbers;
            ModelNames = modelNames;
            DecisionByteCodes = decisionByteCodes;
            DecisionIndices = decisionIndices;
            DiscountRate = discountRate;
            Observations = new Reservoir<DeepCFRObservation>(reservoirCapacity, reservoirSeed);
            PendingObservations = new List<DeepCFRObservation>();
            TargetToAdd = reservoirCapacity;
            RegressionFactory = regressionFactory;
        }

        public DeepCFRModel DeepCopyForPlaybackOnly()
        {
            return new DeepCFRModel(PlayerNumbers, ModelNames, DecisionByteCodes, DecisionIndices, Observations.Capacity, Observations.Seed, DiscountRate, null)
            {
                Regression = Regression.DeepCopyExceptRegressionItself(),
                IterationsProcessed = IterationsProcessed,
                IncludedDecisionIndices = IncludedDecisionIndices,
                TargetToAdd = TargetToAdd,
                StateFrozen = StateFrozen,
                FullReservoirReplacement = FullReservoirReplacement,
                TestDataProportion = TestDataProportion
            };
        }

        public IRegressionMachine GetRegressionMachine() => Regression?.GetRegressionMachine();
        public void ReturnRegressionMachine(IRegressionMachine regressionMachine) => Regression?.ReturnRegressionMachine(regressionMachine);

        public void AddPendingObservation(DeepCFRObservation observation)
        {
            if (ObservationsNeeded())
                PendingObservations.Add(observation);
        }

        public bool ObservationsNeeded()
        {
            return !StateFrozen && PendingObservations.Count() < TargetToAdd;
        }

        public int UpdateAndCountPendingObservationsTarget(int iteration)
        {
            if (StateFrozen)
                return 0;
            TargetToAdd = FullReservoirReplacement ? Observations.Capacity : Observations.CountTotalNumberToAddAtIteration(DiscountRate, iteration);
            return TargetToAdd;
        }

        public string GetModelName()
        {
            if (ModelNames.Count() == 1)
                return ModelNames.Single();
            string first = ModelNames.First();
            string prefix = first.Substring(0, first.Length - 1);
            if (ModelNames.All(x => x.Substring(0, x.Length - 1) == prefix))
            {
                return first + "*";
            }
            return String.Join(",", ModelNames);
        }

        public async Task<string> CompleteIteration()
        {
            IterationsProcessed++;
            if (!PendingObservations.Any())
                return $"No pending observations ({GetModelName()})";
            StringBuilder s = new StringBuilder();
            s.Append($"Pending observations: {PendingObservations.Count()} ");
            Observations.AddPotentialReplacementsAtIteration(PendingObservations.ToList(), DiscountRate, IterationsProcessed);
            PendingObservations = new List<DeepCFRObservation>();
            await BuildModel(s);
            string trainingResultString = Regression.GetTrainingResultString();
            s.AppendLine(trainingResultString + $" ({GetModelName()})");
            return s.ToString();
        }

        public async Task BuildModel(StringBuilder s)
        {
            if (!Observations.Any())
                throw new Exception("No observations available to build model.");
            IncludedDecisionIndices = DeepCFRIndependentVariables.GetIncludedDecisionIndices(Observations.Select(x => x.IndependentVariables));
            (float[], float)[] data = Observations.Select(x => (x.IndependentVariables.AsArray(IncludedDecisionIndices), (float) x.SampledRegret)).ToArray();
            (float[], float)[] testData = null;
            if (TestDataProportion != 0)
            {
                int numToSplitAway = (int)(TestDataProportion * data.Length);
                SplitData(data, numToSplitAway, out (float[], float)[] keep, out testData);
                data = keep;
            }
            Regression = new RegressionController(RegressionFactory);
            await Regression.Regress(data);

            bool printAverageRegrets = false;
            if (printAverageRegrets)
                PrintAverageRegrets(s);
            bool printAllData = false;
            if (printAllData)
                PrintData(data, s);
            if (TestDataProportion != 0)
                PrintTestDataResults(testData, s);
        }

        private void PrintAverageRegrets(StringBuilder s)
        {
            byte[] actionsChosen = Observations.Select(x => x.IndependentVariables.ActionChosen).Distinct().OrderBy(x => x).ToArray();
            var regrets = actionsChosen.Select(a => Observations.Where(x => x.IndependentVariables.ActionChosen == a).Average(x => x.SampledRegret)).ToArray();
            var positiveRegrets = regrets.Select(x => Math.Max(x, 0)).ToArray();
            var positiveRegretsSum = positiveRegrets.Sum();
            var relativePositiveRegrets = positiveRegrets.Select(x => x / positiveRegretsSum).ToArray();
            s.Append($"AvgRegrets {String.Join(", ", regrets.Select(x => x.ToSignificantFigures(4)))} RegretMatch {String.Join(", ", relativePositiveRegrets.Select(x => x.ToSignificantFigures(4)))}");
        }

        private void SplitData((float[], float)[] data, int numToSplitAway, out (float[], float)[] keep, out (float[], float)[] split)
        {
            ConsistentRandomSequenceProducer r = new ConsistentRandomSequenceProducer(0);
            int numToKeep = data.Length - numToSplitAway;
            keep = new (float[], float)[numToKeep];
            split = new (float[], float)[numToSplitAway];
            int overallIndex = 0, keepIndex = 0, splitIndex = 0;
            foreach (bool keepThisOne in RandomSubset.SampleExactly(numToKeep, data.Length, () => r.NextDouble()))
            {
                if (keepThisOne)
                    keep[keepIndex++] = data[overallIndex++];
                else
                    split[splitIndex++] = data[overallIndex++];
            }
            if (overallIndex != data.Length)
                throw new Exception();
        }

        private void PrintTestDataResults((float[], float)[] testData, StringBuilder s)
        {
            double loss = testData.Select(d => (Regression.GetResult(d.Item1, null), d.Item2)).Select(d => Math.Pow(d.Item1 - d.Item2, 2.0)).Average();
            s.AppendLine($"AvgLoss: {loss.ToSignificantFigures(4)} ");
        }

        private void PrintData((float[], float)[] data, StringBuilder s)
        {
            s.AppendLine("");
            var grouped = data.Select(x => (x, String.Join(",", x.Item1)))
                        .GroupBy(x => x.Item2)
                        .OrderBy(x => x.Key)
                        .ToList();
            foreach (var group in grouped)
            {
                (float[], float)[] items = group.Select(x => x.Item1).ToArray();
                float averageInData = items.Average(x => x.Item2);
                float prediction = Regression.GetResult(items.First().Item1, null);
                s.AppendLine($"{group.Key} => {averageInData} (in data) {prediction} (predicted)");
            }
        }

        public double GetPredictedRegretForAction(DeepCFRIndependentVariables independentVariables, byte action, IRegressionMachine regressionMachine)
        {
            if (IterationsProcessed == 0)
                throw new Exception();
            byte originalValue = independentVariables.ActionChosen;
            independentVariables.ActionChosen = action;
            double result = Regression.GetResult(independentVariables.AsArray(IncludedDecisionIndices), regressionMachine);
            independentVariables.ActionChosen = originalValue;
            return result;
        }

        public byte ChooseAction(double randomValue, DeepCFRIndependentVariables independentVariables, byte maxActionValue, byte numActionsToSample, IRegressionMachine regressionMachine, ref double[] probabilities)
        {
            if (IterationsProcessed == 0)
            {
                // no model yet, choose action at random
                byte actionToChoose = ChooseActionAtRandom(randomValue, maxActionValue);
                return actionToChoose;
            }
            if (probabilities == null)
            {
                spline1dinterpolant spline_interpolant = null;
                if (maxActionValue != numActionsToSample)
                {
                    spline_interpolant = new spline1dinterpolant();
                    double[] x = EquallySpaced.GetEquallySpacedPoints(numActionsToSample, false, 1, maxActionValue).Select(a => Math.Floor(a)).ToArray();
                    double[] y = x.Select(a => GetPredictedRegretForAction(independentVariables, (byte)a, regressionMachine)).ToArray();
                    spline1dbuildcatmullrom(x, y, out spline_interpolant);
                }
                double[] positiveRegrets = new double[maxActionValue];
                double sumPositiveRegrets = 0;
                for (byte a = 1; a <= maxActionValue; a++)
                {
                    double predictedRegret = maxActionValue == numActionsToSample ? GetPredictedRegretForAction(independentVariables, a, regressionMachine) : spline1dcalc(spline_interpolant, (double)a);
                    double positiveRegretForAction = Math.Max(0, predictedRegret);
                    positiveRegrets[a - 1] = positiveRegretForAction;
                    sumPositiveRegrets += positiveRegretForAction;
                }
                if (sumPositiveRegrets == 0)
                    return ChooseActionAtRandom(randomValue, (byte)positiveRegrets.Length);
                probabilities = positiveRegrets.Select(x => x / sumPositiveRegrets).ToArray();
            }
            return ChooseActionFromProbabilities(probabilities, randomValue);
        }

        public static byte ChooseActionAtRandom(double randomValue, byte maxActionValue)
        {
            byte actionToChoose = (byte)(randomValue * maxActionValue);
            actionToChoose++; // one-base the actions
            if (actionToChoose > maxActionValue)
                actionToChoose = maxActionValue;
            return actionToChoose;
        }

        private byte ChooseActionFromProbabilities(double[] probabilities, double randomValue)
        {
            double towardTarget = 0;
            for (byte a = 1; a < probabilities.Length; a++)
            {
                towardTarget += probabilities[a - 1];
                if (towardTarget > randomValue)
                    return a;
            }
            return (byte)(probabilities.Length);
        }

        private byte ChooseActionFromPositiveRegrets(double[] positiveRegrets, double sumPositiveRegrets, double randomValue)
        {
            if (sumPositiveRegrets == 0)
                return ChooseActionAtRandom(randomValue, (byte) positiveRegrets.Length);
            double targetCumPosRegret = randomValue * sumPositiveRegrets;
            double towardTarget = 0;
            for (byte a = 1; a < positiveRegrets.Length; a++)
            {
                towardTarget += positiveRegrets[a - 1];
                if (towardTarget > targetCumPosRegret)
                    return a;
            }
            return (byte) (positiveRegrets.Length);
        }

        #region Freezing/remembering state (for approximate best response determination)

        /// <summary>
        /// Freeze the state (for players for whom the best response is not being determined)
        /// </summary>
        public void FreezeState()
        {
            StateFrozen = true;
        }

        /// <summary>
        /// Unfreeze the state (for players for whom the best response was not being determined)
        /// </summary>
        public void UnfreezeState()
        {
            if (!StateFrozen)
                throw new NotSupportedException();
            StateFrozen = false;
        }

        /// <summary>
        /// Prepare to start determining the best response, by remembering the current state and targeting full
        /// replacement of the reservoir.
        /// </summary>
        public void StartDeterminingBestResponse()
        {
            RememberedObservations = Observations.DeepCopy(o => o.DeepCopy());
            FullReservoirReplacement = true;
        }

        /// <summary>
        /// Conclude determining the best response, recalling the previous state.
        /// </summary>
        /// <returns></returns>
        public async Task EndDeterminingBestResponse()
        {
            Observations = RememberedObservations;
            RememberedObservations = null;
            FullReservoirReplacement = false;
            await BuildModel(new StringBuilder()); // must rebuild model (alternative would be to have a deep copy of the model) -- won't print results of that
        }

        #endregion
    }
}
