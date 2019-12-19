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

        const int NumValuesEachSideOfLine = 15;
        const int NumNormalizedSignalsPerPlayer = 30;

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
                nashStrategies = nashStrategies.Where(x => !extremeStrategies.Contains(x) || x == extremeStrategies.Last()).ToList();
            }

            extremeStrategies = nashStrategies.Where(nashStrategy => TrialRate[nashStrategy.pStrategy, nashStrategy.dStrategy] == 0).ToHashSet();
            if (extremeStrategies.Count() > 1)
            {
                TabbedText.WriteLine("Always settle (somewhere in bargaining range) [utility numbers not representative]");
                nashStrategies = nashStrategies.Where(x => !extremeStrategies.Contains(x) || x == extremeStrategies.First()).ToList();
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

        const int EvaluationsPerStrategyCombination = NumNormalizedSignalsPerPlayer * NumNormalizedSignalsPerPlayer;

        const int NumStrategiesPerPlayer = NumValuesEachSideOfLine * NumValuesEachSideOfLine;
        static int ConvertMinMaxToStrategy(int minSignalStrategy, int maxSignalStrategy) => NumValuesEachSideOfLine * minSignalStrategy + maxSignalStrategy;
        static (int minSignalStrategy, int maxSignalStrategy) ConvertStrategyToMinMaxOffers(int strategy) => (strategy / NumValuesEachSideOfLine, strategy % NumValuesEachSideOfLine);
        static (double minSignalStrategy, double maxSignalStrategy) ConvertStrategyToMinMaxContinuousOffers(int strategy)
        {
            var minMax = ConvertStrategyToMinMaxOffers(strategy);
            return (ContinuousOffer(minMax.minSignalStrategy), ContinuousOffer(minMax.maxSignalStrategy));
        }
        static double ContinuousSignal(int discreteSignal) => ((double)(discreteSignal + 1)) / ((double)(NumNormalizedSignalsPerPlayer + 1));
        static double ContinuousOffer(int discreteOffer)
        {
            double continuousZeroToOne = (double)(discreteOffer + 0.5) / ((double)(NumValuesEachSideOfLine));
            bool alwaysUseSimpleMinMaxRange = false;
            if (alwaysUseSimpleMinMaxRange || (continuousZeroToOne > 0.25 && continuousZeroToOne < 0.75))
                return continuousZeroToOne;
            if (continuousZeroToOne < 0.25)
                return 0.25 - 1.0 / (0.25 - continuousZeroToOne);
            else
                return 0.75 + 1.0 / (continuousZeroToOne - 0.75);
        }

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

                    double pUtility = 0, dUtility = 0, trialRate = 0;

                    for (int zp = 0; zp < NumNormalizedSignalsPerPlayer; zp++)
                    {
                        for (int zd = 0; zd < NumNormalizedSignalsPerPlayer; zd++)
                        {
                            double pOffer = pOffers[zp];
                            double dOffer = dOffers[zd];
                            bool equality = Math.Abs(pOffer - dOffer) < 1E-10; /* rounding error */
                            bool treatEqualityAsEquallyLikelyToProduceSettlementOrTrial = true; // with same bid, we assume 50% likelihood of each result -- this make sense as an approximation of a continuous equilibrium
                            double equalityMultiplier = treatEqualityAsEquallyLikelyToProduceSettlementOrTrial && equality ? 0.5 : 1.0;
                            bool dGreater = dOffer > pOffer || equality; // always count equality with the settlement
                            if (dGreater || equality)
                            {
                                double settlement = (pOffer + dOffer) / 2.0;
                                double pEffect = settlement;
                                double dEffect = 1.0 - settlement;
                                //Debug.WriteLine($"({p},{d}) settle ({zp},{zd}) => {pEffect}, {dEffect} "); // DEBUG
                                pUtility += equalityMultiplier * pEffect;
                                dUtility += equalityMultiplier * (1.0 - pEffect);
                            }
                            bool dLess = (dOffer < pOffer && !equality) || (equality && treatEqualityAsEquallyLikelyToProduceSettlementOrTrial);
                            if (dLess)
                            {
                                // trial

                                double dPortionOfCosts = 0.5; //TODO
                                double dCosts = dPortionOfCosts * c;
                                double pCosts = c - dCosts;

                                double theta_p = continuousSignals[zp] * q;
                                double theta_d = q + continuousSignals[zd] * (1 - q); // follows from zd = (theta_d - q) / (1 - q)
                                double j = (theta_p + theta_d) / 2.0;

                                double pEffect = (j - pCosts);
                                double dEffect = ((1.0 - j) - dCosts);
                                pUtility += equalityMultiplier * pEffect;
                                dUtility += equalityMultiplier * dEffect;

                                trialRate += equalityMultiplier;
                                //Debug.WriteLine($"({p},{d}) trial ({zp},{zd}) => {pEffect}, {dEffect} [based on {theta_p}, {theta_d}"); // DEBUG
                            }
                        }
                    }
                    PUtilities[p, d] = pUtility / (double) EvaluationsPerStrategyCombination;
                    DUtilities[p, d] = dUtility / (double) EvaluationsPerStrategyCombination;
                    TrialRate[p, d] = trialRate / (double) EvaluationsPerStrategyCombination;

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
            foreach (double c in new double[] { 0, 0.05, 0.1, 0.15, 0.2, 0.25 }) //, 0.3, 0.35, 0.4, 0.45, 0.5, 0.55, 0.6, 0.8, 1.0 })
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
