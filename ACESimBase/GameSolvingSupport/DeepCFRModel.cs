using ACESim;
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
        /// Observations to be added to the model.
        /// </summary>
        public List<DeepCFRObservation> PendingObservations; 

        public double GetPredictedRegretForAction(DeepCFRIndependentVariables independentVariables, byte action)
        {
            return 0;
        }

        public byte ChooseAction(double randomSeed, DeepCFRIndependentVariables independentVariables, byte maxNumActions, byte numActionsToSample)
        {
            spline1dinterpolant spline_interpolant = null;
            if (maxNumActions != numActionsToSample)
            {
                spline_interpolant = new spline1dinterpolant();
                double[] x = EquallySpaced.GetEquallySpacedPoints(numActionsToSample, false, 1, maxNumActions).Select(a => Math.Floor(a)).ToArray();
                double[] y = x.Select(a => GetPredictedRegretForAction(independentVariables, (byte) a)).ToArray();
                spline1dbuildcatmullrom(x, y, out spline_interpolant);
            }
            double[] regrets = new double[maxNumActions + 1]; // no action 0
            double sumPositiveRegrets = 0;
            for (byte a = 1; a <= maxNumActions; a++)
            {
                double predictedRegret = maxNumActions == numActionsToSample ? GetPredictedRegretForAction(independentVariables, a) : spline1dcalc(spline_interpolant, (double) a);
                double positiveRegretForAction = Math.Max(0, predictedRegret);
                regrets[a] = positiveRegretForAction;
                sumPositiveRegrets += positiveRegretForAction;
            }
            return ChooseActionFromPositiveRegrets(regrets, sumPositiveRegrets, randomSeed);
        }

        private byte ChooseActionFromPositiveRegrets(double[] positiveRegrets, double sumPositiveRegrets, double randomSeed)
        {
            double targetCumPosRegret = randomSeed * sumPositiveRegrets;
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
