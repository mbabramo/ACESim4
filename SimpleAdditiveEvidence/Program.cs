using ACESim;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SimpleAdditiveEvidence
{


    class Program
    {
        static double[] allQualities = new double[] { 0, 0.20, 0.40, 0.60, 0.80, 1.0 };
        static double[] lowCosts = new double[] { 0, 0.10, 0.20, 0.30, 0.40 };
        static double[] allCosts = new double[] { 0, 0.15, 0.30, 0.45, 0.60 };
        static double[] allFeeShifting = new double[] { 0, 0.25, 0.50, 0.75, 1.0 };
        static double?[] allRegretAversions = new double?[] { 0, 0.25, 0.5 };
        static double?[] allRiskAversions = new double?[] { null, 8, 4, 2, 1 };

        static void Main(string[] args)
        {
            //DMSMonteCarlo mc = new DMSMonteCarlo() { c = 0.2, q = 0.5 };
            //mc.SimpleMonteCarlo();


            //ExperimentWithDifferentSignals();

            Stopwatch s = new Stopwatch();
            s.Start();
            StringBuilder b = new StringBuilder();

            // Uncomment to play specific value(s)
            //allCosts = new double[] { 0.45 };
            //allQualities = new double[] { 0.4 };
            //allFeeShifting = new double[] { 1.0 };

            //bool useRegretAversion = false;
            //string headerRow = "CostCat,RiskAverseCat,SignalCat,Cost,RiskAverse," + DMSApproximatorOutcome.GetHeaderStringForSpecificSignal();
            //b.AppendLine(headerRow);
            //VaryRiskAversionTogether_FriedmanWittman(b, useRegretAversion, true, false, true); // no truncations, but include correct values in case useful

            bool useRegretAversion = false;
            string headerRow = "CostCat,RiskAverseCat,SignalCat,Cost,RiskAverse," + DMSApproximatorOutcome.GetHeaderStringForSpecificSignal();
            b.AppendLine(headerRow);
            VaryRiskAversionTogether_FriedmanWittman(b, useRegretAversion, false, true, true); // just 0 risk aversion, with truncations

            //string headerRow = "CostCat,PRiskAverseCat,DRiskAverseCat,Cost,PRiskAverse,DRiskAverse," + DMSApproximatorOutcome.GetHeaderString();
            //b.AppendLine(headerRow);
            //VaryRiskAversionBoth_FriedmanWittman(b);

            //string headerRow = "CostCat,FinalOfferRuleCat,SignalCat,Cost,FinalOfferRule," + DMSApproximatorOutcome.GetHeaderStringForSpecificSignal();
            //b.AppendLine(headerRow);
            //VaryFinalOfferRule_FriedmanWittman(b);





            //string headerRow = "Cost,Quality,Threshold," + DMSApproximatorOutcome.GetHeaderString();
            //b.AppendLine(headerRow);
            //VaryFeeShifting(b);

            TabbedText.WriteLine($"Overall results");
            TabbedText.WriteLine(b.ToString());
            TabbedText.WriteLine($"Time {s.ElapsedMilliseconds}");


            TextCopy.Clipboard.SetText(TabbedText.AccumulatedText.ToString());
        }

        private static void VaryFeeShifting(StringBuilder b)
        {
            foreach (double c in allCosts)
            {
                foreach (double q in allQualities)
                {
                    foreach (double t in allFeeShifting)
                    {
                        DMSApproximator e = new DMSApproximator(q, c, t, null, null, false, null, false, false, true);
                        string rowPrefix = $"{c},{q},{t},";
                        string row = rowPrefix + e.TheOutcome.ToString();
                        b.AppendLine(row);
                        TabbedText.WriteLine("Output: " + row);
                        TabbedText.WriteLine();
                    }
                }
                TabbedText.WriteLine($"Results for costs {c}");
                TabbedText.WriteLine(b.ToString());
            }
        }

        private static void VaryRiskAversionTogether_FriedmanWittman(StringBuilder b, bool useRegretAversion, bool allLevels, bool useTruncations, bool includeCorrectValues)
        {
            double stepSize = (1.0 / ((double)DMSApproximator.NumSignalsPerPlayer)); // must use same number of signals for calculations to work
            double[] signalsToInclude = Enumerable.Range(0, DMSApproximator.NumSignalsPerPlayer).Select(x => 0.5 * stepSize + x * stepSize).ToArray();
            Dictionary<(int, int, bool, bool), List<(double, double)>> coordinates = new Dictionary<(int, int, bool, bool), List<(double, double)>>();
            for (int cCat = 0; cCat < lowCosts.Length; cCat++)
            {
                double c = lowCosts[cCat];
                double?[] rAversions = useRegretAversion ? allRegretAversions : allRiskAversions;
                for (int rAverseCat = 0; rAverseCat < (allLevels ? rAversions.Length : 1); rAverseCat++)
                {
                    double? rAverse = rAversions[rAverseCat];
                    DMSApproximator e = new DMSApproximator(0.5, c, 0, rAverse, rAverse, useRegretAversion, null, true, useTruncations, true);
                    //e.CalculateResultsForOfferRanges(true, (-.1, .566666), (0.43333, 1.1), out bool atLeastOneSettlement, out double pUtility, out double dUtility, out double trialRate, out double accuracySq, out double accuracyHypoSq, out double accuracyForP, out double accuracyForD);
                    //e.CalculateResultsForOfferRanges(true, (-.1, .566666), (0, 12.706), out atLeastOneSettlement, out pUtility, out dUtility, out trialRate, out accuracySq, out accuracyHypoSq, out accuracyForP, out accuracyForD);
                    for (int signalCat = 0; signalCat < signalsToInclude.Length; signalCat++)
                    {
                        if (signalCat % 5 == 2)
                        { // we don't need all 100 signals (and it seems to cause tex to take an inordinate amount of time to process)
                            double signal = (double)signalsToInclude[signalCat];
                            string rowPrefix = $"{cCat + 1},{rAverseCat + 1},{signalCat + 1},{c},{rAverse},";
                            var offers = e.GetOffersForSignalWithOptimalStrategies(signalCat);
                            string pOfferString = offers.pOffer >= 0 && offers.pOffer <= 1 ? offers.pOffer.ToString() : "";
                            string dOfferString = offers.dOffer >= 0 && offers.dOffer <= 1 ? offers.dOffer.ToString() : "";
                            var offers2 = e.GetOffersForSignalWithFriedmanWittmanStrategies(signalCat);
                            string pOfferStringCorrect = includeCorrectValues ? (offers2.pOffer >= 0 && offers2.pOffer <= 1 ? offers2.pOffer.ToString() : "") : "";
                            string dOfferStringCorrect = includeCorrectValues ? (offers2.dOffer >= 0 && offers2.dOffer <= 1 ? offers2.dOffer.ToString() : "") : "";
                            string row = rowPrefix + e.TheOutcome.ToStringForSpecificSignal(signal, pOfferString, dOfferString, pOfferStringCorrect, dOfferStringCorrect);
                            b.AppendLine(row);
                        }
                    }
                }
                TabbedText.WriteLine($"Results for costs {c}");
                TabbedText.WriteLine(b.ToString());
            }
        }

        private static void VaryRiskAversionBoth_FriedmanWittman(StringBuilder b)
        {
            bool useRegretAversion = true; // DEBUG
            double?[] rAversions = useRegretAversion ? allRegretAversions : allRiskAversions;
            for (int cCat = 0; cCat < lowCosts.Length; cCat++)
            {
                double c = lowCosts[cCat];
                for (int pRiskAversionCat = 0; pRiskAversionCat < rAversions.Length; pRiskAversionCat++)
                {
                    double? pRiskAverse = rAversions[pRiskAversionCat];
                    for (int dRiskAversionCat = 0; dRiskAversionCat < rAversions.Length; dRiskAversionCat++)
                    {
                        double? dRiskAverse = rAversions[dRiskAversionCat];
                        DMSApproximator e = new DMSApproximator(0.5, c, 0, pRiskAverse, dRiskAverse, useRegretAversion, null, true, false, true);
                        string rowPrefix = $"{cCat + 1},{pRiskAversionCat + 1},{dRiskAversionCat + 1},{c},{pRiskAverse},{dRiskAverse},";
                        string row = rowPrefix + e.TheOutcome.ToString();
                        b.AppendLine(row);
                        TabbedText.WriteLine("Output: " + row);
                        TabbedText.WriteLine();
                    }
                }
                TabbedText.WriteLine($"Results for costs {c}");
                TabbedText.WriteLine(b.ToString());
            }
        }


        private static void VaryFinalOfferRule_FriedmanWittman(StringBuilder b)
        {
            double stepSize = (1.0 / ((double)DMSApproximator.NumSignalsPerPlayer)); // must use same number of signals for calculations to work
            double[] signalsToInclude = Enumerable.Range(0, DMSApproximator.NumSignalsPerPlayer).Select(x => 0.5 * stepSize + x * stepSize).ToArray();
            for (int cCat = 0; cCat < lowCosts.Length; cCat++)
            {
                double c = lowCosts[cCat];
                double[] fOfferValues = new double[] { 0, 0.25, 0.5, 0.75, 1.0 };
                for (int finalOfferCat = 0; finalOfferCat < fOfferValues.Length; finalOfferCat++)
                {
                    double? finalOfferValue = fOfferValues[finalOfferCat];
                    DMSApproximator e = new DMSApproximator(0.5, c, 0, null, null, false, finalOfferValue, true, false, true);
                    for (int signalCat = 0; signalCat < signalsToInclude.Length; signalCat++)
                    {
                        if (signalCat % 5 == 2)
                        { // we don't need all 100 signals (and it seems to cause tex to take an inordinate amount of time to process)
                            double signal = (double)signalsToInclude[signalCat];
                            string rowPrefix = $"{cCat + 1},{finalOfferCat + 1},{signalCat + 1},{c},{finalOfferValue},";
                            var offers = e.GetOffersForSignalWithOptimalStrategies(signalCat);
                            string pOfferString = offers.pOffer >= 0 && offers.pOffer <= 1 ? offers.pOffer.ToString() : "";
                            string dOfferString = offers.dOffer >= 0 && offers.dOffer <= 1 ? offers.dOffer.ToString() : "";
                            var offers2 = e.GetOffersForSignalWithFriedmanWittmanStrategies(signalCat);
                            string pOfferStringCorrect = offers2.pOffer >= 0 && offers2.pOffer <= 1 ? offers2.pOffer.ToString() : "";
                            string dOfferStringCorrect = offers2.dOffer >= 0 && offers2.dOffer <= 1 ? offers2.dOffer.ToString() : "";
                            string row = rowPrefix + e.TheOutcome.ToStringForSpecificSignal(signal, pOfferString, dOfferString, pOfferStringCorrect, dOfferStringCorrect);
                            b.AppendLine(row);
                        }
                    }
                }
                TabbedText.WriteLine($"Results for costs {c}");
                TabbedText.WriteLine(b.ToString());
            }
        }

        private static void ExperimentWithDifferentSignals()
        {
            double t = 1;
            foreach (double c in new double[] { 0.45 })
            {
                foreach (double q in new double[] { 0.4 })
                {
                    double pUtilityCum = 0, dUtilityCum = 0;
                    DMSApproximator tester = new DMSApproximator(q, c, t, null, null, false, null, false, false, true);
                    double stepSize = 0.20;
                    double numCases = 0;
                    for (double zp = stepSize; zp < 1; zp += stepSize)
                    {
                        for (double zd = stepSize; zd < 1; zd += stepSize)
                        {
                            numCases += 1.0;
                            double theta_p = zp * q;
                            double theta_d = q + zd * (1 - q); // follows from zd = (theta_d - q) / (1 - q)
                            bool atLeastOneSettlement = false;
                            double pUtility = 0;
                            double dUtility = 0;
                            double trialRate = 0;
                            double accuracySq = 0;
                            double accuracyHypoSq = 0;
                            double accuracyForP = 0;
                            double accuracyForD = 0;
                            tester.ProcessCaseGivenParticularSignals_NormalizedSignals(1.0, 0, q, zp, zd, ref atLeastOneSettlement, ref pUtility, ref dUtility, ref trialRate, ref accuracySq, ref accuracyHypoSq, ref accuracyForP, ref accuracyForD);
                            TabbedText.WriteLine($"zp {zp.ToSignificantFigures(3)} (thetap {theta_p.ToSignificantFigures(3)}) zd {zd.ToSignificantFigures(3)} (thetad {theta_d.ToSignificantFigures(3)}) ==> j { (0.5 * (theta_p + theta_d)).ToSignificantFigures(3) } pUtility {pUtility.ToSignificantFigures(3)} dUtility {dUtility.ToSignificantFigures(3)} trialRate {trialRate.ToSignificantFigures(3)} absAccuracy {Math.Sqrt(accuracySq).ToSignificantFigures(3)} accuracySq {accuracySq.ToSignificantFigures(3)} accuracyHypoSq {accuracyHypoSq.ToSignificantFigures(3)} accuracyForP {accuracyForP.ToSignificantFigures(3)} accuracyForD {accuracyForD.ToSignificantFigures(3)}");
                            pUtilityCum += pUtility;
                            dUtilityCum += dUtility;
                        };
                    }
                    pUtilityCum /= numCases;
                    dUtilityCum /= numCases;
                    double utilityTotal = pUtilityCum + dUtilityCum;
                    TabbedText.WriteLine($"Average utility ({pUtilityCum}, {dUtilityCum})");
                }
            }
        }


    }
}
