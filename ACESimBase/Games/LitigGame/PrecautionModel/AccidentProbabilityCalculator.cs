using System;
using System.Collections.Generic;

namespace ACESimBase.Games.LitigGame.PrecautionModel
{
    /// <summary>
    /// Continuous accident-probability surface P(t,p).
    /// </summary>
    public sealed class AccidentProbabilityCalculator
    {
        readonly double _pMax, _pMinLow, _pMinHigh, _alphaLow, _alphaHigh;

        public AccidentProbabilityCalculator(
            double pMax,
            double pMinLow,
            double pMinHigh,
            double alphaLow,
            double alphaHigh)
        {
            if (pMinLow < 0 || pMinHigh < 0 || pMax <= 0)
                throw new ArgumentOutOfRangeException();
            if (pMinHigh > pMinLow)
                throw new ArgumentException();
            if (alphaLow <= 0 || alphaHigh <= 0)
                throw new ArgumentOutOfRangeException();

            _pMax = pMax;
            _pMinLow = pMinLow;
            _pMinHigh = pMinHigh;
            _alphaLow = alphaLow;
            _alphaHigh = alphaHigh;
        }

        static double PMin(double power, double minLow, double minHigh) =>
            minLow + (minHigh - minLow) * power;

        static double Alpha(double power, double aLow, double aHigh) =>
            aLow + (aHigh - aLow) * power;

        public double AccidentProbability(double t, double power)
        {
            if (t < 0 || t > 1) throw new ArgumentOutOfRangeException(nameof(t));
            if (power < 0 || power > 1) throw new ArgumentOutOfRangeException(nameof(power));

            double pMin = PMin(power, _pMinLow, _pMinHigh);
            double alpha = Alpha(power, _alphaLow, _alphaHigh);
            return pMin + (_pMax - pMin) * Math.Pow(1.0 - t, alpha);
        }

        public static double ToContinuousPower(int k, int powerLevels)
        {
            if (powerLevels < 1) throw new ArgumentOutOfRangeException(nameof(powerLevels));
            if (k < 1 || k > powerLevels) throw new ArgumentOutOfRangeException(nameof(k));
            return k / (double)(powerLevels + 1);
        }

        public static IReadOnlyList<double> ContinuousPowers(int powerLevels)
        {
            var list = new List<double>(powerLevels);
            for (int k = 1; k <= powerLevels; k++)
                list.Add(ToContinuousPower(k, powerLevels));
            return list;
        }
    }
}
