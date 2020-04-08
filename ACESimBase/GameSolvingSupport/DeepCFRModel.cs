using ACESim;
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
        /// Observations used to create the most recent version of the model.
        /// </summary>
        public Reservoir<DeepCFRObservation> Observations;
        /// <summary>
        /// The number of iterations already processed.
        /// </summary>
        public int IterationsProcessed;
        /// <summary>
        /// The discount rate (the proportion of observations to keep at each iteration, before the replacement inversely proportional to the iteration number).
        /// </summary>
        public double DiscountRate;
        /// <summary>
        /// Observations to be added to the model at the end of the current iteration.
        /// </summary>
        public ConcurrentBag<DeepCFRObservation> PendingObservations;
        /// <summary>
        /// The trained neural network.
        /// </summary>
        NeuralNetworkController Regression;
        // The following explain how we format our independent variables.
        bool PlayerSameForAll;
        bool DecisionByteCodeSameForAll;
        int MaxInformationSetLength;

        public DeepCFRModel(int reservoirCapacity, long reservoirSeed, double discountRate)
        {
            DiscountRate = discountRate;
            Observations = new Reservoir<DeepCFRObservation>(reservoirCapacity, reservoirSeed);
            PendingObservations = new ConcurrentBag<DeepCFRObservation>();
        }

        public void AddPendingObservation(DeepCFRObservation observation)
        {
            PendingObservations.Add(observation);
        }

        public int CountPendingObservationsTarget(int iteration) => Observations.CountTotalNumberToAddAtIteration(DiscountRate, iteration);

        public async Task CompleteIteration(int deepCFREpochs)
        {
            IterationsProcessed++;
            Observations.AddPotentialReplacementsAtIteration(PendingObservations.ToList(), DiscountRate, IterationsProcessed);
            PendingObservations = new ConcurrentBag<DeepCFRObservation>();
            await BuildModel(deepCFREpochs);
        }

        private async Task BuildModel(int deepCFREpochs)
        {
            if (!Observations.Any())
                throw new Exception("No observations available to build model.");
            byte firstPlayer = Observations.First().IndependentVariables.Player;
            PlayerSameForAll = Observations.All(x => firstPlayer == x.IndependentVariables.Player);
            byte firstDecisionByteCode = Observations.First().IndependentVariables.DecisionIndex;
            DecisionByteCodeSameForAll = Observations.All(x => firstDecisionByteCode == x.IndependentVariables.DecisionIndex);
            MaxInformationSetLength = Observations.Max(x => x.IndependentVariables.InformationSet?.Count() ?? 0);
            var data = Observations.Select(x => (x.IndependentVariables.AsArray(!PlayerSameForAll, !DecisionByteCodeSameForAll, MaxInformationSetLength), (float) x.SampledRegret)).ToArray();
            Regression = new NeuralNetworkController();
            await Regression.TrainNeuralNetwork(data, NeuralNetworkNET.Networks.Cost.CostFunctionType.Quadratic, deepCFREpochs, 2);
        }

        public double GetPredictedRegretForAction(DeepCFRIndependentVariables independentVariables, byte action)
        {
            if (IterationsProcessed == 0)
                throw new Exception();
            return Regression.GetResult(independentVariables.AsArray(!PlayerSameForAll, !DecisionByteCodeSameForAll, MaxInformationSetLength));
        }

        public byte ChooseAction(double randomValue, DeepCFRIndependentVariables independentVariables, byte maxActionValue, byte numActionsToSample)
        {
            if (IterationsProcessed == 0)
            {
                // no model yet, choose action at random
                byte actionToChoose = ChooseActionAtRandom(randomValue, maxActionValue);
                return actionToChoose;
            }
            spline1dinterpolant spline_interpolant = null;
            if (maxActionValue != numActionsToSample)
            {
                spline_interpolant = new spline1dinterpolant();
                double[] x = EquallySpaced.GetEquallySpacedPoints(numActionsToSample, false, 1, maxActionValue).Select(a => Math.Floor(a)).ToArray();
                double[] y = x.Select(a => GetPredictedRegretForAction(independentVariables, (byte) a)).ToArray();
                spline1dbuildcatmullrom(x, y, out spline_interpolant);
            }
            double[] regrets = new double[maxActionValue + 1]; // no action 0
            double sumPositiveRegrets = 0;
            for (byte a = 1; a <= maxActionValue; a++)
            {
                double predictedRegret = maxActionValue == numActionsToSample ? GetPredictedRegretForAction(independentVariables, a) : spline1dcalc(spline_interpolant, (double) a);
                double positiveRegretForAction = Math.Max(0, predictedRegret);
                regrets[a] = positiveRegretForAction;
                sumPositiveRegrets += positiveRegretForAction;
            }
            return ChooseActionFromPositiveRegrets(regrets, sumPositiveRegrets, randomValue);
        }

        private static byte ChooseActionAtRandom(double randomValue, byte maxActionValue)
        {
            byte actionToChoose = (byte)(randomValue * maxActionValue);
            actionToChoose++; // one-base the actions
            if (actionToChoose > maxActionValue)
                actionToChoose = maxActionValue;
            return actionToChoose;
        }

        private byte ChooseActionFromPositiveRegrets(double[] positiveRegrets, double sumPositiveRegrets, double randomValue)
        {
            if (sumPositiveRegrets == 0)
                return ChooseActionAtRandom(randomValue, (byte) positiveRegrets.Length);
            double targetCumPosRegret = randomValue * sumPositiveRegrets;
            double towardTarget = 0;
            for (byte a = 1; a < positiveRegrets.Length; a++)
            {
                towardTarget += positiveRegrets[a];
                if (towardTarget > targetCumPosRegret)
                    return a;
            }
            return (byte) (positiveRegrets.Length - 1);
        }
    }
}
