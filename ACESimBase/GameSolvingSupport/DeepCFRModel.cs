﻿using ACESim;
using ACESimBase.Util;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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
        public List<DeepCFRObservation> PendingObservations; 

        public DeepCFRModel(int reservoirCapacity, long reservoirSeed, double discountRate)
        {
            DiscountRate = discountRate;
            Observations = new Reservoir<DeepCFRObservation>(reservoirCapacity, reservoirSeed);
            PendingObservations = new List<DeepCFRObservation>();
        }

        public void CompleteIteration()
        {
            IterationsProcessed++;
            Observations.AddPotentialReplacementsAtIteration(PendingObservations, DiscountRate, IterationsProcessed);
            PendingObservations = new List<DeepCFRObservation>();
            BuildModel();
        }

        public void BuildModel()
        {
            asdf;
        }

        public double GetPredictedRegretForAction(DeepCFRIndependentVariables independentVariables, byte action)
        {
            if (IterationsProcessed == 0)
                throw new Exception();
        }

        public byte ChooseAction(int randomSeed, DeepCFRIndependentVariables independentVariables, byte maxActionValue, byte numActionsToSample)
        {
            if (IterationsProcessed == 0)
            {
                // no model yet, choose action at random
                byte actionToChoose = ChooseActionAtRandom(randomSeed, maxActionValue);
                return actionToChoose;
`            }
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
            return ChooseActionFromPositiveRegrets(regrets, sumPositiveRegrets, randomSeed);
        }

        private static byte ChooseActionAtRandom(int randomSeed, byte maxActionValue)
        {
            byte actionToChoose = (byte)(((double)randomSeed / (double)int.MaxValue) * maxActionValue);
            actionToChoose++; // one-base the actions
            if (actionToChoose > maxActionValue)
                actionToChoose = maxActionValue;
            return actionToChoose;
        }

        private byte ChooseActionFromPositiveRegrets(double[] positiveRegrets, double sumPositiveRegrets, int randomSeed)
        {
            if (sumPositiveRegrets == 0)
                return ChooseActionAtRandom(randomSeed, (byte) positiveRegrets.Length);
            double targetCumPosRegret = ((double) randomSeed / (double) int.MaxValue) * sumPositiveRegrets;
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
