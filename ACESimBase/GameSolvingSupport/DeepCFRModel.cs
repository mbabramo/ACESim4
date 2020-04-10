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
        /// A name for the model.
        /// </summary>
        public string ModelName;
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
        /// The number of hidden layers in the regression
        /// </summary>
        public int HiddenLayers;
        /// <summary>
        /// The number of neurons in each hidden layer.
        /// </summary>
        public int NeuronsPerHiddenLayer;
        /// <summary>
        /// The number of epochs in neural network optimization.
        /// </summary>
        public int Epochs;
        /// <summary>
        /// Observations to be added to the model at the end of the current iteration.
        /// </summary>
        public ConcurrentBag<DeepCFRObservation> PendingObservations;
        /// <summary>
        /// The trained neural network.
        /// </summary>
        NeuralNetworkController Regression;
        /// <summary>
        /// The decision indices that are included in the independent variables of the regression
        /// </summary>
        List<byte> IncludedDecisionIndices;
        /// <summary>
        /// The number of additional observations that we would like to target in an iteration.
        /// </summary>
        int TargetToAdd;

        public DeepCFRModel(string modelName, int reservoirCapacity, long reservoirSeed, double discountRate, int hiddenLayers, int neuronsPerHiddenLayer, int epochs)
        {
            ModelName = modelName;
            DiscountRate = discountRate;
            HiddenLayers = hiddenLayers;
            NeuronsPerHiddenLayer = neuronsPerHiddenLayer;
            Epochs = epochs;
            Observations = new Reservoir<DeepCFRObservation>(reservoirCapacity, reservoirSeed);
            PendingObservations = new ConcurrentBag<DeepCFRObservation>();
            TargetToAdd = reservoirCapacity;
        }

        public void AddPendingObservation(DeepCFRObservation observation)
        {
            if (PendingObservations.Count() < TargetToAdd)
                PendingObservations.Add(observation);
        }

        public int CountPendingObservationsTarget(int iteration)
        {
            TargetToAdd = Observations.CountTotalNumberToAddAtIteration(DiscountRate, iteration);
            return TargetToAdd;
        }

        public async Task CompleteIteration()
        {
            IterationsProcessed++;
            TabbedText.Write($"Pending observations: {PendingObservations.Count()} ");
            Observations.AddPotentialReplacementsAtIteration(PendingObservations.ToList(), DiscountRate, IterationsProcessed);
            PendingObservations = new ConcurrentBag<DeepCFRObservation>();
            await BuildModel();
            string trainingResultString = Regression.GetTrainingResultString();
            TabbedText.WriteLine(trainingResultString + $" ({ModelName})");
        }

        private async Task BuildModel()
        {
            if (!Observations.Any())
                throw new Exception("No observations available to build model.");
            IncludedDecisionIndices = DeepCFRIndependentVariables.GetIncludedDecisionIndices(Observations.Select(x => x.IndependentVariables));
            var data = Observations.Select(x => (x.IndependentVariables.AsArray(IncludedDecisionIndices), (float) x.SampledRegret)).ToArray();
            byte[] actionsChosen = Observations.Select(x => x.IndependentVariables.ActionChosen).Distinct().OrderBy(x => x).ToArray();
            var regrets = actionsChosen.Select(a => Observations.Where(x => x.IndependentVariables.ActionChosen == a).Average(x => x.SampledRegret)).ToArray();
            TabbedText.Write($"AvgRegrets {String.Join(", ", regrets)} ");
            Regression = new NeuralNetworkController();
            Regression.SpecifySettings(Epochs, HiddenLayers, NeuronsPerHiddenLayer);
            await Regression.Regress(data);
        }

        public double GetPredictedRegretForAction(DeepCFRIndependentVariables independentVariables, byte action)
        {
            if (IterationsProcessed == 0)
                throw new Exception();
            independentVariables.ActionChosen = action;
            return Regression.GetResult(independentVariables.AsArray(IncludedDecisionIndices));
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
            double[] regrets = new double[maxActionValue];
            double sumPositiveRegrets = 0;
            for (byte a = 1; a <= maxActionValue; a++)
            {
                double predictedRegret = maxActionValue == numActionsToSample ? GetPredictedRegretForAction(independentVariables, a) : spline1dcalc(spline_interpolant, (double) a);
                double positiveRegretForAction = Math.Max(0, predictedRegret);
                regrets[a - 1] = positiveRegretForAction;
                sumPositiveRegrets += positiveRegretForAction;
            }
            return ChooseActionFromPositiveRegrets(regrets, sumPositiveRegrets, randomValue);
        }

        public static byte ChooseActionAtRandom(double randomValue, byte maxActionValue)
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
                towardTarget += positiveRegrets[a - 1];
                if (towardTarget > targetCumPosRegret)
                    return a;
            }
            return (byte) (positiveRegrets.Length);
        }
    }
}
