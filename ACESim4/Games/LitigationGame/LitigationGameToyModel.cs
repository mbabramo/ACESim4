using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESim
{
    
    public class LitigationGameToyModel
    {

        public void RepeatedlyFindDropCutoff()
        {
            for (double d = 0; d <= 1.0; d += 0.01)
                TabbedText.WriteLine(CalculateProbabilityProxyWouldBeGreaterThan0Point5.GetProbability(d, 0.15, 1000000000));
            StatCollector sc = new StatCollector();
            for (int i = 0; i < 100; i++)
            {
                double cutoff = FindDropCutoff();
                TabbedText.WriteLine("Cutoff: " + cutoff);
                sc.Add(cutoff);
            }
            TabbedText.WriteLine("Cutoff average: " + sc.Average() + " sd: " + sc.StandardDeviation());
        }

        public double FindDropCutoff()
        {
            long numIterations = 5000000;
            return StochasticCutoffFinder.FindCutoff(true, 0.0, 1.0, numIterations, 99999999, true, 10, 0.10, true, true, GetNetScoreFromDropping);
        }

        public StochasticCutoffFinderOutputs GetNetScoreFromDropping(StochasticCutoffFinderInputs scfInputs, long iteration)
        {
            StochasticCutoffFinderOutputs scfOutputs = new StochasticCutoffFinderOutputs() { Weight = 1.0 };
            double actualLitigationQuality = RandomGenerator.NextDouble();
            double standardDeviationOfObfuscation = 0.15;
            double pNoise = standardDeviationOfObfuscation * alglib.normaldistr.invnormaldistribution(RandomGenerator.NextDouble());
            double dNoise = standardDeviationOfObfuscation * alglib.normaldistr.invnormaldistribution(RandomGenerator.NextDouble());
            double jNoise = standardDeviationOfObfuscation * alglib.normaldistr.invnormaldistribution(RandomGenerator.NextDouble());
            double pSignal = actualLitigationQuality + pNoise;
            double dSignal = actualLitigationQuality + dNoise;
            double jSignal = actualLitigationQuality + jNoise;

            double pEstimateStrengthLiability = ACESim.ObfuscationGame.ObfuscationCorrectAnswer.Calculate(standardDeviationOfObfuscation, pSignal);
            double dEstimateStrengthLiability = ACESim.ObfuscationGame.ObfuscationCorrectAnswer.Calculate(standardDeviationOfObfuscation, dSignal);

            double pEstimatePWins = CalculateProbabilityProxyWouldBeGreaterThan0Point5.GetProbability(pEstimateStrengthLiability, standardDeviationOfObfuscation);
            double dEstimatePWins = CalculateProbabilityProxyWouldBeGreaterThan0Point5.GetProbability(dEstimateStrengthLiability, standardDeviationOfObfuscation);
            double pEstimateDWins = 1.0 - pEstimatePWins;
            double dEstimateDWins = 1.0 - dEstimatePWins;

            scfOutputs.InputVariable = pEstimatePWins;
            if (scfInputs.TentativeCutoff != null && Math.Abs(pSignal - (double)scfInputs.TentativeCutoff) > (double)scfInputs.MaxRangeFromCutoff)
                return null; // not close enough so we're not going to consider it

            double pEstimatePDeltaUtility = -250.0 + 1000.0 * pEstimatePWins;
            double pEstimateDDeltaUtility = -1250.0 + 1000.0 * pEstimateDWins;
            double dEstimatePDeltaUtility = -250.0 + 1000.0 * dEstimatePWins;
            double dEstimateDDeltaUtility = -1250.0 + 1000.0 * dEstimateDWins;
            double pBargainingRangeInsists = RandomGenerator.NextDouble(0.0, 0.40);
            double dBargainingRangeInsists = RandomGenerator.NextDouble(0.0, 0.40);
            double bargainingRangeSize = 500.0;
            double pOffer = pEstimatePDeltaUtility + pBargainingRangeInsists * bargainingRangeSize;
            double dOffer = dEstimatePDeltaUtility + (1.0 - dBargainingRangeInsists) * bargainingRangeSize;
            double pStartWealth = 100000;
            double dStartWealth = 100000;
            double pScore = 0;
            if (pOffer < dOffer)
            {
                double pExpensesWithSettlement = 75.0;
                double settlementAmount = (pOffer + dOffer) / 2.0;
                pScore = pStartWealth + settlementAmount - pExpensesWithSettlement;
            }
            else
            {
                double pExpensesWithTrial = 250.0;
                double damagesPayment = 0.0;
                if (jSignal >= 0.5)
                    damagesPayment = 1000.0;
                pScore = pStartWealth + damagesPayment - pExpensesWithTrial;
            }
            double pScoreIfDropped = pStartWealth;
            scfOutputs.Score = pScoreIfDropped - pScore;
            return scfOutputs;
        }
    }
}
