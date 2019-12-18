using ACESim;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SimpleAdditiveEvidence
{
    public class EqFinder
    {
        double q, c;

        public EqFinder(double q, double c)
        {
            this.q = q;
            this.c = c;
            CalculateUtilities();
            List<(int pStrategy, int dStrategy)> nashStrategies = PureStrategiesFinder.ComputeNashEquilibria(PUtilities, DUtilities);

            var alwaysTrialStrategies = nashStrategies.Where(nashStrategy => TrialRate[nashStrategy.pStrategy, nashStrategy.dStrategy] > 0.99999).ToHashSet(); // take into account rounding errors
            if (alwaysTrialStrategies.Any())
            {
                TabbedText.WriteLine("Always trial");
                nashStrategies = nashStrategies.Where(x => !alwaysTrialStrategies.Contains(x)).ToList();
            }

            var grouped = nashStrategies.GroupBy(x => x.pStrategy);
            foreach (var pStrategyGroup in grouped)
            {
                var aNashStrategy = pStrategyGroup.First();
                var pLine = ConvertStrategyToMinMaxContinuousOffers(aNashStrategy.pStrategy);
                TabbedText.WriteLine($"P offers from {pLine.minSignalStrategy} to {pLine.maxSignalStrategy} (utility {PUtilities[aNashStrategy.pStrategy, aNashStrategy.dStrategy]})");
                TabbedText.TabIndent();
                foreach (var nashStrategy in pStrategyGroup)
                {
                    var dLine = ConvertStrategyToMinMaxContinuousOffers(nashStrategy.dStrategy);
                    TabbedText.WriteLine($"D offers from {dLine.minSignalStrategy} to {dLine.maxSignalStrategy} (utility {DUtilities[nashStrategy.pStrategy, nashStrategy.dStrategy]})");
                    TabbedText.WriteLine($"--> Trial rate {TrialRate[nashStrategy.pStrategy, nashStrategy.dStrategy].ToSignificantFigures(3)}");
                }
                TabbedText.TabUnindent();
            }
        }

        const int NumValuesEachSideOfLine = 10;
        const int NumNormalizedSignalsPerPlayer = 50;

        const int NumStrategiesPerPlayer = NumValuesEachSideOfLine * NumValuesEachSideOfLine;
        static double ContributionEachSetOfSignals = 1.0 / ((double)(NumNormalizedSignalsPerPlayer * NumNormalizedSignalsPerPlayer));
        static int ConvertMinMaxToStrategy(int minSignalStrategy, int maxSignalStrategy) => NumValuesEachSideOfLine * minSignalStrategy + maxSignalStrategy;
        static (int minSignalStrategy, int maxSignalStrategy) ConvertStrategyToMinMaxOffers(int strategy) => (strategy / NumValuesEachSideOfLine, strategy % NumValuesEachSideOfLine);
        static (double minSignalStrategy, double maxSignalStrategy) ConvertStrategyToMinMaxContinuousOffers(int strategy)
        {
            var minMax = ConvertStrategyToMinMaxOffers(strategy);
            return (ContinuousOffer(minMax.minSignalStrategy), ContinuousOffer(minMax.maxSignalStrategy));
        }

        const double MinOffer = -0.25;
        const double OfferRange = 1.5;
        const double MaxOffer = MinOffer + OfferRange;
        static double ContinuousSignal(int discreteSignal) => ((double)(discreteSignal + 1)) / ((double)(NumNormalizedSignalsPerPlayer + 1));
        static double ContinuousOffer(int discreteOffer) => MinOffer + OfferRange * (double)discreteOffer / ((double)(NumValuesEachSideOfLine - 1));

        double[,] PUtilities = new double[NumStrategiesPerPlayer, NumStrategiesPerPlayer];
        double[,] DUtilities = new double[NumStrategiesPerPlayer, NumStrategiesPerPlayer];
        double[,] TrialRate = new double[NumStrategiesPerPlayer, NumStrategiesPerPlayer];

        void CalculateUtilities()
        {
            double[] continuousSignals = Enumerable.Range(0, NumNormalizedSignalsPerPlayer).Select(x => ContinuousSignal(x)).ToArray();
            for (int p = 0; p < NumStrategiesPerPlayer; p++)
            {
                var pOfferRange = ConvertStrategyToMinMaxContinuousOffers(p);
                double[] pOffers = Enumerable.Range(0, NumNormalizedSignalsPerPlayer).Select(x => pOfferRange.minSignalStrategy * (1.0 - continuousSignals[x]) + pOfferRange.maxSignalStrategy * continuousSignals[x]).ToArray();
                for (int d = 0; d < NumStrategiesPerPlayer; d++)
                {
                    var dOfferRange = ConvertStrategyToMinMaxContinuousOffers(d);
                    double[] dOffers = Enumerable.Range(0, NumNormalizedSignalsPerPlayer).Select(x => dOfferRange.minSignalStrategy * (1.0 - continuousSignals[x]) + dOfferRange.maxSignalStrategy * continuousSignals[x]).ToArray();
                    for (int zp = 0; zp < NumNormalizedSignalsPerPlayer; zp++)
                        for (int zd = 0; zd < NumNormalizedSignalsPerPlayer; zd++)
                        {
                            double pOffer = pOffers[zp];
                            double dOffer = dOffers[zd];
                            if (dOffer >= pOffer || Math.Abs(pOffer - dOffer) < 1E-10 /* rounding error */)
                            {
                                double settlement = (pOffer + dOffer) / 2.0;
                                PUtilities[p, d] = settlement;
                                DUtilities[p, d] = 0 - settlement;
                            }
                            else
                            {
                                // trial

                                double dPortionOfCosts = 0.5; //TODO
                                double dCosts = dPortionOfCosts * c;
                                double pCosts = c - dCosts;

                                double theta_p = continuousSignals[zp] / q;
                                double theta_d = continuousSignals[zd] / (1 - q);
                                double j = (theta_p + theta_d) / 2.0;

                                PUtilities[p, d] += ContributionEachSetOfSignals * (j - pCosts);
                                DUtilities[p, d] += ContributionEachSetOfSignals * (0 - j - dCosts);

                                TrialRate[p, d] += ContributionEachSetOfSignals;
                            }
                        }
                }
            }
        }
    }


    class Program
    {
        static void Main(string[] args)
        {
            EqFinder e = new EqFinder(0.5, 0.5);
        }

        

    }
}
