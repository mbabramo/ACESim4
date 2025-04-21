using ACESimBase.Util.Debugging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SimpleAdditiveEvidence
{
    public class DMSMonteCarlo
    {
        public double c, q;

        public void SimpleMonteCarlo()
        {
            // this confirms that the formula in DMS provides the optimal strategy (with t = 0)
            // but it also shows that with 10,000 uniform draws from a distribution to estimate the value of the
            // strategy, we need the deviation from the recommendation to be at least 0.010 before the optimal
            // point will look better than the deviation. So, this suggests that we need at least 10,000 
            // measurements per matrix entry to avoid errors.
            const int n_per_party = 10;
            const int n = n_per_party * n_per_party;
            double distribute(int discreteSignal) => ((double)(discreteSignal + 1)) / ((double)(n_per_party + 1));
            double[] evenlyDistributed = Enumerable.Range(0, n_per_party).Select(x => distribute(x)).ToArray();
            bool useRandom = false;
            Random r = new Random();
            double p(double signal) => 0.5 - 3.0 * ((5.0 / 6.0) - q) * c + (1.0 / 3.0) * signal;
            double d(double signal) => 1.0 / 6.0 + 3 * (q - 1.0 / 6.0) * c + (1.0 / 3.0) * signal;
            double pMinRecommended = p(0);
            double dMinRecommended = d(0);
            double pMaxRecommended = p(1);
            double dMaxRecommended = d(1);
            // the following possibility doesn't seem to be considered by DMS, which 
            if (dMinRecommended > pMaxRecommended)
                dMinRecommended = pMaxRecommended = (dMinRecommended + pMaxRecommended) / 2.0;
            double pTotalUtil_Formula = 0, pTotalUtil_Higher = 0, pTotalUtil_Lower = 0;
            for (int i = 0; i < n; i++)
            {
                double pSignal = useRandom ? r.NextDouble() : evenlyDistributed[i / n_per_party];
                double pRecommended = p(pSignal);
                double pRecommendedInit = pRecommended;
                double dSignal = useRandom ? r.NextDouble() : evenlyDistributed[i % n_per_party];
                double dRecommended = d(dSignal);
                double dRecommendedInit = dRecommended;

                // p wants to get as much as possible. So, p will never ask for an amount lower than the least that d will give.
                if (pRecommendedInit < dMinRecommended)
                    pRecommended = dMinRecommended;
                // d wants as little as possible. So, d will never ask for an amount lower than the most that p 
                if (dRecommendedInit > pMaxRecommended)
                    dRecommended = pMaxRecommended;

                // problem: The truncations cancel each other out. If p increases p's bid

                double increment = 0.02;
                pTotalUtil_Formula += SimpleMonteCarlo_Helper(n, pSignal, pRecommended, dSignal, dRecommended);
                pTotalUtil_Higher += SimpleMonteCarlo_Helper(n, pSignal, pRecommended + increment, dSignal, dRecommended);
                pTotalUtil_Lower += SimpleMonteCarlo_Helper(n, pSignal, pRecommended - increment, dSignal, dRecommended);
            }
            TabbedText.WriteLine($"{pTotalUtil_Formula} {pTotalUtil_Higher} {pTotalUtil_Lower} {(pTotalUtil_Higher < pTotalUtil_Formula && pTotalUtil_Lower < pTotalUtil_Formula ? "Good" : "Bad")}");
        }

        private double SimpleMonteCarlo_Helper(int n, double pSignal, double pRecommended, double dSignal, double dRecommended)
        {
            double pResult = 0;
            double theta_p = pSignal * q;
            double theta_d = q + dSignal * (1 - q);
            if (dRecommended >= pRecommended)
                pResult = (pRecommended + dRecommended) / 2.0;
            else
                pResult = (theta_p + theta_d) / 2.0 - 0.5 * c;
            return pResult;
        }
    }
}
