using ACESim;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SimpleAdditiveEvidence
{
    public class EqFinder
    {
        double q, c, t;
        int optimalPStrategy = -1, optimalDStrategy = -1;
        public Outcome TheOutcome;

        const int NumValuesEachSideOfLine = 32;
        const int NumNormalizedSignalsPerPlayer = 64;

        public EqFinder(double q, double c, double t)
        {
            this.q = q;
            this.c = c;
            this.t = c;
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

            var f1 = nashStrategies.First();
            optimalPStrategy = f1.pStrategy;
            optimalDStrategy = f1.dStrategy;
            TheOutcome = new Outcome(PUtilities[optimalPStrategy, optimalDStrategy], DUtilities[optimalPStrategy, optimalDStrategy], TrialRate[optimalPStrategy, optimalDStrategy], AccuracySq[optimalPStrategy, optimalDStrategy], AccuracyHypoSq[optimalPStrategy, optimalDStrategy], AccuracyForP[optimalPStrategy, optimalDStrategy], AccuracyForD[optimalPStrategy, optimalDStrategy], ConvertStrategyToMinMaxContinuousOffers(optimalPStrategy), ConvertStrategyToMinMaxContinuousOffers(optimalDStrategy));
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

        public readonly struct Outcome
        {
            public readonly double PUtility, DUtility, TrialRate, AccuracySq, AccuracyHypoSq, AccuracyForP, AccuracyForD, MinPOffer, MaxPOffer, MinDOffer, MaxDOffer;

            public Outcome(double PUtility, double DUtility, double TrialRate, double AccuracySq, double AccuracyHypoSq, double AccuracyForP, double AccuracyForD, (double, double) POffer, (double, double) DOffer)
            {
                this.PUtility = PUtility;
                this.DUtility = DUtility;
                this.TrialRate = TrialRate;
                this.AccuracySq = AccuracySq;
                this.AccuracyHypoSq = AccuracyHypoSq;
                this.AccuracyForP = AccuracyForP;
                this.AccuracyForD = AccuracyForD;
                this.MinPOffer = POffer.Item1;
                this.MaxPOffer = POffer.Item2;
                this.MinDOffer = DOffer.Item1;
                this.MaxDOffer = DOffer.Item2;
            }

            public override string ToString()
            {
                return $"{PUtility},{DUtility},{TrialRate},{AccuracySq},{AccuracyHypoSq},{AccuracyForP},{AccuracyForD},{MinPOffer},{MaxPOffer},{MinDOffer},{MaxDOffer}";
            }

            public static string GetHeaderString()
            {
                return "PUtility,DUtility,TrialRate,AccuracySq,AccuracyHypoSq,AccuracyForP,AccuracyForD,MinPOffer,MaxPOffer,MinDOffer,MaxDOffer";
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

        public double[,] PUtilities = new double[NumStrategiesPerPlayer, NumStrategiesPerPlayer];
        public double[,] DUtilities = new double[NumStrategiesPerPlayer, NumStrategiesPerPlayer];
        public double[,] TrialRate = new double[NumStrategiesPerPlayer, NumStrategiesPerPlayer];
        public double[,] AccuracyHypoSq = new double[NumStrategiesPerPlayer, NumStrategiesPerPlayer];
        public double[,] AccuracySq = new double[NumStrategiesPerPlayer, NumStrategiesPerPlayer];
        public double[,] AccuracyForP = new double[NumStrategiesPerPlayer, NumStrategiesPerPlayer];
        public double[,] AccuracyForD = new double[NumStrategiesPerPlayer, NumStrategiesPerPlayer];

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

                    double pUtility = 0, dUtility = 0, trialRate = 0, accuracySq = 0, accuracyHypoSq = 0, accuracyForP = 0, accuracyForD = 0;

                    for (int zp = 0; zp < NumNormalizedSignalsPerPlayer; zp++)
                    {
                        for (int zd = 0; zd < NumNormalizedSignalsPerPlayer; zd++)
                        {
                            double pOffer = pOffers[zp];
                            double dOffer = dOffers[zd];
                            double theta_p = continuousSignals[zp] * q;
                            double theta_d = q + continuousSignals[zd] * (1 - q); // follows from zd = (theta_d - q) / (1 - q)
                            double hypo_theta_p = 0.5 * q; // the theta expected before parties collect information
                            double hypo_theta_d = q + 0.5 * (1 - q); // the theta expected before parties collect information
                            double j = (theta_p + theta_d) / 2.0;
                            double hypo_j = (hypo_theta_p + hypo_theta_d) / 2.0;
                            bool equality = Math.Abs(pOffer - dOffer) < 1E-10; /* rounding error */
                            bool treatEqualityAsEquallyLikelyToProduceSettlementOrTrial = true; // with same bid, we assume 50% likelihood of each result -- this make sense as an approximation of a continuous equilibrium
                            double equalityMultiplier = treatEqualityAsEquallyLikelyToProduceSettlementOrTrial && equality ? 0.5 : 1.0;
                            bool dGreater = dOffer > pOffer || equality; // always count equality with the settlement
                            if (dGreater || equality)
                            {
                                double settlement = (pOffer + dOffer) / 2.0;
                                double pEffect = settlement;
                                double dEffect = 1.0 - settlement;
                                //Debug.WriteLine($"({p},{d}) settle ({zp},{zd}) => {pEffect}, {dEffect} "); 
                                pUtility += equalityMultiplier * pEffect;
                                dUtility += equalityMultiplier * (1.0 - pEffect);
                                double v = equalityMultiplier * (pEffect - q) * (pEffect - q);
                                accuracySq += v;
                                accuracyHypoSq += v;
                                accuracyForP += equalityMultiplier * Math.Abs(pEffect - j);
                                accuracyForD += equalityMultiplier * Math.Abs(dEffect - (1 - j));
                            }
                            bool dLess = (dOffer < pOffer && !equality) || (equality && treatEqualityAsEquallyLikelyToProduceSettlementOrTrial);
                            if (dLess)
                            {
                                // trial
                                double oneMinusTheta_d = 1.0 - theta_d;
                                double dPortionOfCosts = true switch
                                {
                                    _ when theta_p < oneMinusTheta_d && (theta_d < t) => 0,
                                    _ when Math.Abs(theta_p - oneMinusTheta_d) < 1E-10 /* i.e., equality but for rounding */ || (theta_d >= t && theta_p <= t) => 0.5,
                                    _ => 1.0
                                };
                                double hypo_oneMinusTheta_d = 1.0 - hypo_theta_d;
                                double hypo_dPortionOfCosts = true switch
                                {
                                    _ when hypo_theta_p < hypo_oneMinusTheta_d && (hypo_theta_d < t) => 0,
                                    _ when Math.Abs(hypo_theta_p - hypo_oneMinusTheta_d) < 1E-10 /* i.e., equality but for rounding */ || (hypo_theta_d >= t && hypo_theta_p <= t) => 0.5,
                                    _ => 1.0
                                };
                                double dCosts = dPortionOfCosts * c;
                                double pCosts = c - dCosts;

                                double pEffect = (j - pCosts);
                                double hypo_pEffect = (hypo_j - pCosts);
                                double dEffect = ((1.0 - j) - dCosts);
                                pUtility += equalityMultiplier * pEffect;
                                dUtility += equalityMultiplier * dEffect;

                                trialRate += equalityMultiplier;
                                double accuracyUnsquared = pEffect + 0.5 * c; // the idea here is that the party's own costs are considered relevant to accuracy. Because P paid 0.5 * c out of pocket and this was counted in pEffect, we add this back in. Note that if shifting to defendant has occurred, that means that we have that accuracyUnsquared == j + 0.5*C, with the latter part representing the fee shifting penalty imposed on the defendant.
                                double hypo_accuracyUnsquared = hypo_pEffect + 0.5 * c;
                                accuracySq += equalityMultiplier * accuracyUnsquared * accuracyUnsquared;
                                accuracyHypoSq += equalityMultiplier * hypo_accuracyUnsquared * hypo_accuracyUnsquared;
                                accuracyForP += equalityMultiplier * Math.Abs(pEffect - j);
                                accuracyForD += equalityMultiplier * Math.Abs(dEffect - (1 - j));
                                //Debug.WriteLine($"({p},{d}) trial ({zp},{zd}) => {pEffect}, {dEffect} [based on {theta_p}, {theta_d}");
                            }
                        }
                    }
                    PUtilities[p, d] = pUtility / (double) EvaluationsPerStrategyCombination;
                    DUtilities[p, d] = dUtility / (double) EvaluationsPerStrategyCombination;
                    TrialRate[p, d] = trialRate / (double) EvaluationsPerStrategyCombination;
                    AccuracySq[p, d] = accuracySq / (double)EvaluationsPerStrategyCombination;
                    AccuracyHypoSq[p, d] = accuracyHypoSq / (double)EvaluationsPerStrategyCombination;
                    AccuracyForP[p, d] = accuracyForP / (double)EvaluationsPerStrategyCombination;
                    AccuracyForD[p, d] = accuracyForD / (double)EvaluationsPerStrategyCombination;

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
            StringBuilder b = new StringBuilder();
            string headerRow = "Cost,Quality,Threshold," + EqFinder.Outcome.GetHeaderString();
            b.AppendLine(headerRow);
            foreach (double c in new double[] { 0, 0.05, 0.1, 0.15, 0.2, 0.25, 0.3, 0.35, 0.4 }) // 0.35, 0.4, 0.45, 0.5, 0.55, 0.6, 0.8, 1.0 })
            {
                foreach (double q in new double[] { 0, 0.1, 0.2, 0.3, 0.4, 0.5, 0.6, 0.7, 0.8, 0.9, 1.0 })
                {
                    foreach (double t in new double[] { 0, 0.2, 0.4, 0.6, 0.8, 1.0 })
                    {
                        TabbedText.WriteLine($"Cost: {c} quality {q} threshold {t}");
                        EqFinder e = new EqFinder(q, c, t);
                        string rowPrefix = $"{c},{q},{t},";
                        string row = rowPrefix + e.TheOutcome.ToString();
                        b.AppendLine(row);
                        TabbedText.WriteLine(row);
                        TabbedText.WriteLine();
                    }
                }
            }

            TabbedText.WriteLine(b.ToString());
            TabbedText.WriteLine($"Time {s.ElapsedMilliseconds}");
        }

        

    }
}
