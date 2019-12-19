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

        // Each player's strategy is a line, represented by a minimum strategy value and a maximum strategy value. This class seeks to optimize that line
        // by playing a range of strategies. To ensure that it considers very high slopes (positive or negative), as well as intermediate ones, it converts
        // relatively high and low strategy values nonlinearly. 
        // Zoom in feature: We can choose to what value the 25th percentile and the 75th of what each player's strategy corresponds to. Initially, we start at 0.25 and 0.75

        const int NumValuesEachSideOfLine = 15;
        const int NumNormalizedSignalsPerPlayer = 30;

        public EqFinder(double q, double c, double t)
        {
            this.q = q;
            this.c = c;
            this.t = c;
            Execute();
        }

        private void Execute()
        {
            for (int i = 0; i < 5; i++)
            {
                TabbedText.WriteLine($"Cycle {i}");
                ResetUtilities();
                CalculateUtilities();
                EvaluateEquilibria();
                AdjustLowerAndUpperCutoffsBasedOnPerformance();
            }
        }

        private void EvaluateEquilibria()
        {
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
            TheOutcome = new Outcome(PUtilities[optimalPStrategy, optimalDStrategy], DUtilities[optimalPStrategy, optimalDStrategy], TrialRate[optimalPStrategy, optimalDStrategy], AccuracySq[optimalPStrategy, optimalDStrategy], AccuracyHypoSq[optimalPStrategy, optimalDStrategy], AccuracyForP[optimalPStrategy, optimalDStrategy], AccuracyForD[optimalPStrategy, optimalDStrategy], ConvertStrategyToMinMaxContinuousOffers(optimalPStrategy, true), ConvertStrategyToMinMaxContinuousOffers(optimalDStrategy, false));
            var grouped = nashStrategies.GroupBy(x => x.pStrategy);
            foreach (var pStrategyGroup in grouped)
            {
                var aNashStrategy = pStrategyGroup.First();
                var pLine = ConvertStrategyToMinMaxContinuousOffers(aNashStrategy.pStrategy, true);
                TabbedText.WriteLine($"P offers from {pLine.minSignalStrategy} to {pLine.maxSignalStrategy} (utility {PUtilities[aNashStrategy.pStrategy, aNashStrategy.dStrategy]})");
                TabbedText.TabIndent();
                foreach (var nashStrategy in pStrategyGroup)
                {
                    var dLine = ConvertStrategyToMinMaxContinuousOffers(nashStrategy.dStrategy, false);
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
        (double minSignalStrategy, double maxSignalStrategy) ConvertStrategyToMinMaxContinuousOffers(int strategy, bool plaintiff)
        {
            var minMax = ConvertStrategyToMinMaxOffers(strategy);
            return (GetMappedOfferValue(minMax.minSignalStrategy, plaintiff), GetMappedOfferValue(minMax.maxSignalStrategy, plaintiff));
        }
        static double ContinuousSignal(int discreteSignal) => ((double)(discreteSignal + 1)) / ((double)(NumNormalizedSignalsPerPlayer + 1));

        // The min/max becomes nonlinear in the input strategy for extreme strategies (i.e., below first variable below or above second).
        // For example, if minMaxNonlinearBelowThreshold = 0.25, then when the discrete signal is less than 25% of the maximum discrete signal,
        // we use a nonlinear function to determine the continuous value.
        // NOTE: Of course, we are still trying to find a strategy that represents a line. This just means that we are going to try some 
        // very steep lines, since the optimal strategy may be an almost vertical line (with a very low minimum or a very high maximum). 
        // Note that flat lines are easier -- because the min and max just have to be equal.
        static double minMaxNonlinearBelowThreshold = 0.25;
        static double minMaxNonlinearAboveThreshold = 0.75;
        // The following specifies the mapping from a discrete representation of a min or max of a line and the continuous representation.
        // Initially, we set this to 0.25 to 0.75 to capture the broad middle of the probability spectrum.
        // But based on the outcome, one can zoom into the result by moving this near the initially successful strategy.
        // For example, suppose the optimal min value for p's strategy corresponds to the 35th percentile discrete value (not necessarily
        // a continuous value of 0.35, though it would be that after the first iteration). At that point, it looks like our strategy is in
        // fact within the (25% to 75%) range, so we might zoom into the (0.30, 0.55) area. If we're outside the range, then we zoom out
        // so that our value will be in the range. For example, if we're at the 20% discrete value, we then might make the range
        // from the 10% value to the 30% value.
        double pValueAtLowerNonlinearCutoff = 0.25;
        double pValueAtUpperNonlinearCutoff = 0.75;
        double dValueAtLowerNonlinearCutoff = 0.25;
        double dValueAtUpperNonlinearCutoff = 0.75;

        void AdjustLowerAndUpperCutoffsBasedOnPerformance()
        {
            var pOptimum = ConvertStrategyToMinMaxContinuousOffers(optimalPStrategy, true);
            (pValueAtLowerNonlinearCutoff, pValueAtUpperNonlinearCutoff) = AdjustLowerAndUpperCutoffsBasedOnPerformance_Helper(pOptimum, true);
            double dOptimum = GetUnmappedOfferValue(optimalDStrategy);
            (dValueAtLowerNonlinearCutoff, dValueAtUpperNonlinearCutoff) = AdjustLowerAndUpperCutoffsBasedOnPerformance_Helper(dOptimum, true);
            TabbedText.WriteLine($"Adjusting 25% to 75% ranges: P {(pValueAtLowerNonlinearCutoff.ToSignificantFigures(3), pValueAtUpperNonlinearCutoff.ToSignificantFigures(3))} D {(dValueAtLowerNonlinearCutoff.ToSignificantFigures(3), dValueAtUpperNonlinearCutoff.ToSignificantFigures(3))}");
        }

        (double, double) AdjustLowerAndUpperCutoffsBasedOnPerformance_Helper(double percentileOfOptimum, bool plaintiff)
        {
            double revisedLowerPercentile, revisedUpperPercentile;
            // comments assume that our below and above threshold values are 0.25 and 0.75
            if (percentileOfOptimum < minMaxNonlinearBelowThreshold)
            {
                revisedLowerPercentile = 0 + percentileOfOptimum / 2.0; // e.g., 20% -> 10%
                revisedUpperPercentile = minMaxNonlinearBelowThreshold + (minMaxNonlinearBelowThreshold - percentileOfOptimum); // e.g., 20% -> 30%
            }
            else if (percentileOfOptimum < (minMaxNonlinearBelowThreshold + minMaxNonlinearAboveThreshold) / 2.0)
            {
                revisedLowerPercentile = minMaxNonlinearBelowThreshold + (percentileOfOptimum - minMaxNonlinearBelowThreshold) / 2.0; // e.g, 35% -> 30%
                revisedUpperPercentile = minMaxNonlinearAboveThreshold - (minMaxNonlinearAboveThreshold - percentileOfOptimum) / 2.0; // e.g., 35% -> 55%
            }
            else if (percentileOfOptimum < minMaxNonlinearAboveThreshold)
            {
                // same code
                revisedLowerPercentile = minMaxNonlinearBelowThreshold + (percentileOfOptimum - minMaxNonlinearBelowThreshold) / 2.0; // e.g, 65% -> 45%
                revisedUpperPercentile = minMaxNonlinearAboveThreshold - (minMaxNonlinearAboveThreshold - percentileOfOptimum) / 2.0; // e.g., 65% -> 70%
            }
            else
            {
                revisedLowerPercentile = minMaxNonlinearAboveThreshold - (percentileOfOptimum - minMaxNonlinearAboveThreshold); // e.g., 80% -> 70%
                revisedUpperPercentile = 1.0 - (1.0 - percentileOfOptimum) / 2.0; // e.g., 80% -> 90%
            }
            return (MapFromZeroOneRangeToOffer(revisedLowerPercentile, plaintiff), MapFromZeroOneRangeToOffer(revisedUpperPercentile, plaintiff));
        }

        double GetMappedOfferValue(int discreteOffer, bool plaintiff)
        {
            double continuousOfferWithLinearSlopeUnadjusted = GetUnmappedOfferValue(discreteOffer);
            return MapFromZeroOneRangeToOffer(continuousOfferWithLinearSlopeUnadjusted, plaintiff);
        }

        private static double GetUnmappedOfferValue(int discreteOffer)
        {
            return (double)(discreteOffer + 0.5) / ((double)(NumValuesEachSideOfLine));
        }

        private double MapFromZeroOneRangeToOffer(double continuousOfferWithLinearSlopeUnadjusted, bool plaintiff)
        {
            if ((continuousOfferWithLinearSlopeUnadjusted > minMaxNonlinearBelowThreshold && continuousOfferWithLinearSlopeUnadjusted < minMaxNonlinearAboveThreshold))
            {
                double proportionOfWayBetweenCutoffs = (continuousOfferWithLinearSlopeUnadjusted - minMaxNonlinearBelowThreshold) / (minMaxNonlinearAboveThreshold - minMaxNonlinearBelowThreshold);
                double adjusted;
                if (plaintiff)
                    adjusted = pValueAtLowerNonlinearCutoff + proportionOfWayBetweenCutoffs * (pValueAtUpperNonlinearCutoff - pValueAtLowerNonlinearCutoff);
                else
                    adjusted = dValueAtLowerNonlinearCutoff + proportionOfWayBetweenCutoffs * (dValueAtUpperNonlinearCutoff - dValueAtLowerNonlinearCutoff);
                return adjusted;
            }
            if (continuousOfferWithLinearSlopeUnadjusted < minMaxNonlinearBelowThreshold)
            {
                double cutoffValue = plaintiff ? pValueAtLowerNonlinearCutoff : dValueAtLowerNonlinearCutoff;
                return cutoffValue - 1.0 / (minMaxNonlinearBelowThreshold - continuousOfferWithLinearSlopeUnadjusted);
            }
            else
            {
                double cutoffValue = plaintiff ? pValueAtUpperNonlinearCutoff : dValueAtUpperNonlinearCutoff;
                return minMaxNonlinearAboveThreshold + 1.0 / (continuousOfferWithLinearSlopeUnadjusted - minMaxNonlinearAboveThreshold);
            }
        }

        public double[,] PUtilities;
        public double[,] DUtilities;
        public double[,] TrialRate;
        public double[,] AccuracyHypoSq;
        public double[,] AccuracySq;
        public double[,] AccuracyForP;
        public double[,] AccuracyForD;

        void ResetUtilities()
        {
            PUtilities = new double[NumStrategiesPerPlayer, NumStrategiesPerPlayer];
            DUtilities = new double[NumStrategiesPerPlayer, NumStrategiesPerPlayer];
            TrialRate = new double[NumStrategiesPerPlayer, NumStrategiesPerPlayer];
            AccuracyHypoSq = new double[NumStrategiesPerPlayer, NumStrategiesPerPlayer];
            AccuracySq = new double[NumStrategiesPerPlayer, NumStrategiesPerPlayer];
            AccuracyForP = new double[NumStrategiesPerPlayer, NumStrategiesPerPlayer];
            AccuracyForD = new double[NumStrategiesPerPlayer, NumStrategiesPerPlayer];
        }

        void CalculateUtilities()
        {
            double[] continuousSignals = Enumerable.Range(0, NumNormalizedSignalsPerPlayer).Select(x => ContinuousSignal(x)).ToArray();
            for (int p = 0; p < NumStrategiesPerPlayer; p++)
            {
                var pOfferRange = ConvertStrategyToMinMaxContinuousOffers(p, true);
                double[] pOffers = Enumerable.Range(0, NumNormalizedSignalsPerPlayer).Select(x => pOfferRange.minSignalStrategy * (1.0 - continuousSignals[x]) + pOfferRange.maxSignalStrategy * continuousSignals[x]).ToArray();
                for (int d = 0; d < NumStrategiesPerPlayer; d++)
                {
                    var dOfferRange = ConvertStrategyToMinMaxContinuousOffers(d, false);
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
            foreach (double c in new double[] { /* DEBUG 0, 0.05, 0.1, */ 0.15, 0.2, 0.25, 0.3, 0.35, 0.4 }) // 0.35, 0.4, 0.45, 0.5, 0.55, 0.6, 0.8, 1.0 })
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
