using ACESim;
using System;
using System.Collections.Generic;
using System.Diagnostics;
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

            var extremeStrategies = nashStrategies.Where(nashStrategy => TrialRate[nashStrategy.pStrategy, nashStrategy.dStrategy] > 0.99999).ToHashSet(); // take into account rounding errors
            if (extremeStrategies.Any())
            {
                TabbedText.WriteLine("Always trial");
                nashStrategies = nashStrategies.Where(x => !extremeStrategies.Contains(x)).ToList();
            }

            extremeStrategies = nashStrategies.Where(nashStrategy => TrialRate[nashStrategy.pStrategy, nashStrategy.dStrategy] == 0).ToHashSet(); 
            if (extremeStrategies.Any())
            {
                TabbedText.WriteLine("Always settle (somewhere in bargaining range)");
                nashStrategies = nashStrategies.Where(x => !extremeStrategies.Contains(x)).ToList();
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

        const int NumValuesEachSideOfLine = 15;
        const int NumNormalizedSignalsPerPlayer = 25;

        const int NumStrategiesPerPlayer = NumValuesEachSideOfLine * NumValuesEachSideOfLine;
        static double ContributionEachSetOfSignals = 1.0 / ((double)(NumNormalizedSignalsPerPlayer * NumNormalizedSignalsPerPlayer));
        static int ConvertMinMaxToStrategy(int minSignalStrategy, int maxSignalStrategy) => NumValuesEachSideOfLine * minSignalStrategy + maxSignalStrategy;
        static (int minSignalStrategy, int maxSignalStrategy) ConvertStrategyToMinMaxOffers(int strategy) => (strategy / NumValuesEachSideOfLine, strategy % NumValuesEachSideOfLine);
        static (double minSignalStrategy, double maxSignalStrategy) ConvertStrategyToMinMaxContinuousOffers(int strategy)
        {
            var minMax = ConvertStrategyToMinMaxOffers(strategy);
            return (ContinuousOffer(minMax.minSignalStrategy), ContinuousOffer(minMax.maxSignalStrategy));
        }

        const double MinOffer = -2.0;
        const double OfferRange = 4.0;
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
                            bool equality = Math.Abs(pOffer - dOffer) < 1E-10; /* rounding error */
                            bool dGreater = dOffer > pOffer;
                            if (dGreater || equality)
                            {
                                double settlement = (pOffer + dOffer) / 2.0;
                                double overallContribution = ContributionEachSetOfSignals * settlement;
                                if (equality)
                                    overallContribution *= 0.5; // with same bid, we assume 50% likelihood of each result -- this make sense as an approximation of a continuous equilibrium
                                PUtilities[p, d] += overallContribution;
                                DUtilities[p, d] += 0 - overallContribution;
                            }
                            if (!dGreater || equality)
                            {
                                // trial

                                double dPortionOfCosts = 0.5; //TODO
                                double dCosts = dPortionOfCosts * c;
                                double pCosts = c - dCosts;

                                double theta_p = continuousSignals[zp] / q;
                                double theta_d = continuousSignals[zd] / (1 - q);
                                double j = (theta_p + theta_d) / 2.0;

                                double pEffect = (j - pCosts);
                                PUtilities[p, d] += (equality ? 0.5 : 1.0) * ContributionEachSetOfSignals * pEffect;
                                double dEffect = (0 - j - dCosts);
                                DUtilities[p, d] += (equality ? 0.5 : 1.0) * ContributionEachSetOfSignals * dEffect;

                                TrialRate[p, d] += (equality ? 0.5 : 1.0) * ContributionEachSetOfSignals;
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
            Stopwatch s = new Stopwatch();
            s.Start();
            foreach (double c in new double[] { 0, 0.2, 0.35, 0.4, 0.45, 0.5, 0.55, 0.6, 0.8, 1.0 })
                foreach (double q in new double[] { 0.35, 0.40, 0.45, 0.5, 0.55, 0.60, 0.65 })
                {
                    TabbedText.WriteLine($"Cost: {c} quality {q}");
                    EqFinder e = new EqFinder(q, c);
                    TabbedText.WriteLine();
                }
            TabbedText.WriteLine($"Time {s.ElapsedMilliseconds}");
        }

        

    }
}
