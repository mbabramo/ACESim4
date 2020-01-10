using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SimpleAdditiveEvidence
{
    public class FWCheck
    {
        public double c;
        const int NumSignalsPerPlayer = 100;
        double[] continuousSignals = Enumerable.Range(0, NumSignalsPerPlayer).Select(x => ContinuousSignal(x)).ToArray();
        static double ContinuousSignal(int discreteSignal) => ((double)(discreteSignal + 1)) / ((double)(NumSignalsPerPlayer + 1));

        public void Run()
        {
            TruncatedLine pLine = TruncatedLine.FromMinAndMax(-.1, .566666); // these is the optimum according to FW
            TruncatedLine dLine = TruncatedLine.FromMinAndMax(0.433333, 1.1);
            TruncatedLine pTruncatedByD = new TruncatedLine(pLine, dLine);
            TruncatedLine dTruncatedByP = new TruncatedLine(dLine, pLine);
            var result = GetUtilitiesAndTrialRate(pTruncatedByD, dTruncatedByP);

            TruncatedLine dLine2 = TruncatedLine.FromMinAndMax(0, 12.06); // this was identified as the optimum
            TruncatedLine pTruncatedByD2 = new TruncatedLine(pLine, dLine2);
            TruncatedLine d2TruncatedByP = new TruncatedLine(dLine2, pLine);
            var resultChangingDLineAndTruncations = GetUtilitiesAndTrialRate(pTruncatedByD2, d2TruncatedByP);

            var resultChangingDLineKeepingPTruncations = GetUtilitiesAndTrialRate(pTruncatedByD, d2TruncatedByP);

            var altered = pTruncatedByD2;
            altered.lowTrunc = pTruncatedByD.lowTrunc;
            var resultChangingDLineKeepingPLowTrunc = GetUtilitiesAndTrialRate(altered, d2TruncatedByP);

            altered = pTruncatedByD2;
            altered.highTrunc = pTruncatedByD.highTrunc;
            var resultChangingDLineKeepingPHighTrunc = GetUtilitiesAndTrialRate(altered, d2TruncatedByP);

            // Bottom Line: The truncations, specifically the low truncation, are what saves P from exploitation. If D can change its line expecting P to change truncations, then D will do much better, because instead of demanding at least .4333, P will in some cases be demanding much less. But in this case, D does just a little bit worse. The reason is that the trial rate increases, with P retaining its high demands. 
        }

        public struct TruncatedLine
        {
            public double intercept;
            public double slope;
            public bool hasTruncations;
            public double lowTrunc;
            public double highTrunc;

            public TruncatedLine(double intercept, double slope)
            {
                this.intercept = intercept;
                this.slope = slope;
                hasTruncations = false;
                lowTrunc = 0;
                highTrunc = 1;
            }

            public TruncatedLine(TruncatedLine selfWithoutTruncations, TruncatedLine otherWithoutTruncations)
            {
                intercept = selfWithoutTruncations.intercept;
                slope = selfWithoutTruncations.slope;
                lowTrunc = Math.Max(0, otherWithoutTruncations.GetValue(0));
                highTrunc = Math.Min(1, otherWithoutTruncations.GetValue(1));
                hasTruncations = true;
            }

            public static TruncatedLine FromMinAndMax(double min, double max)
            {
                return new TruncatedLine(min, max - min);
            }

            public double GetValue(double signal)
            {
                var result = GetValue_Helper(signal);
                if (hasTruncations)
                {
                    if (result < lowTrunc)
                        result = lowTrunc;
                    if (result > highTrunc)
                        result = highTrunc;
                }
                return result;
            }

            public double GetValue_Helper(double signal)
            {
                return intercept + signal * slope;
            }
        }

        public (double, double, double) GetUtilitiesAndTrialRate(TruncatedLine strategyP, TruncatedLine strategyD)
        {
            double pUtility = 0, dUtility = 0, trialRate = 0, cases = 0;
            foreach (double pSignal in continuousSignals)
            {
                double pOffer, dOffer;
                pOffer = strategyP.GetValue(pSignal);
                foreach (double dSignal in continuousSignals)
                {
                    cases += 1.0;
                    dOffer = strategyD.GetValue(dSignal);
                    if (pOffer > dOffer)
                    {
                        double resolution = (pSignal + dSignal) / 2.0;
                        pUtility += resolution - c * 0.5;
                        dUtility += (1.0 - resolution) - c * 0.5;
                        trialRate += 1.0;
                    }
                    else
                    {
                        double resolution = (pOffer + dOffer) / 2.0;
                        pUtility += resolution;
                        dUtility += (1.0 - resolution);
                    }
                }
            }
            return (pUtility / cases, dUtility / cases, trialRate / cases);
        }

    }
}
