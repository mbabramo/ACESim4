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
        static double[] allCosts = new double[] { 0, 0.15, 0.30, 0.45, 0.60 };
        static double[] allFeeShifting = new double[] { 0, 0.25, 0.50, 0.75, 1.0 };
        static double?[] allRiskAversions = new double?[] { null, 16, 8, 4, 2, 1 };

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


            //string headerRow = "Cost,Quality,Threshold," + DMSApproximatorOutcome.GetHeaderString();
            //b.AppendLine(headerRow);
            //VaryFeeShifting(b);

            string headerRow = "CostCat,RiskAverseCat,SignalCat,Cost,RiskAverse," + DMSApproximatorOutcome.GetHeaderStringForSpecificSignal();
            b.AppendLine(headerRow);
            VaryRiskAversionTogether_FriedmanWittman(b);


            //string headerRow = "CostCat,PRiskAverseCat,DRiskAverseCat,Cost,PRiskAverse,DRiskAverse," + DMSApproximatorOutcome.GetHeaderString();
            //b.AppendLine(headerRow);
            //VaryRiskAversionBoth_FriedmanWittman(b);

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
                        DMSApproximator e = new DMSApproximator(q, c, t, null, null, false);
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

        private static void VaryRiskAversionTogether_FriedmanWittman(StringBuilder b)
        {
            double stepSize = (1.0 / ((double)DMSApproximator.NumSignalsPerPlayer)); // must use same number of signals for calculations to work
            double[] signalsToInclude = Enumerable.Range(0, DMSApproximator.NumSignalsPerPlayer).Select(x => 0.5 * stepSize + x * stepSize).ToArray();
            Dictionary<(int, int, bool, bool), List<(double, double)>> coordinates = new Dictionary<(int, int, bool, bool), List<(double, double)>>();
            for (int cCat = 2 /* DEBUG */; cCat < 3 /* DEBUG allCosts.Length */; cCat++)
            {
                double c = allCosts[cCat];
                for (int riskAverseCat = 0; riskAverseCat < 1 /* DEBUG allRiskAversions.Length */; riskAverseCat++)
                {
                    double? riskAverse = allRiskAversions[riskAverseCat];
                    DMSApproximator e = new DMSApproximator(0.5, c, 0, riskAverse, riskAverse, true);
                    for (int signalCat = 0; signalCat < signalsToInclude.Length; signalCat++)
                    {
                        if (signalCat % 5 == 3)
                        { // we don't need all 100 signals (and it seems to cause tex to take an inordinate amount of time to process)
                            double signal = (double)signalsToInclude[signalCat];
                            string rowPrefix = $"{cCat + 1},{riskAverseCat + 1},{signalCat + 1},{c},{riskAverse},";
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

        private static void VaryRiskAversionBoth_FriedmanWittman(StringBuilder b)
        {
            for (int cCat = 0; cCat < allCosts.Length; cCat++)
            {
                double c = allCosts[cCat];
                for (int pRiskAversionCat = 0; pRiskAversionCat < allRiskAversions.Length; pRiskAversionCat++)
                {
                    double? pRiskAverse = allRiskAversions[pRiskAversionCat];
                    for (int dRiskAversionCat = 0; dRiskAversionCat < allRiskAversions.Length; dRiskAversionCat++)
                    {
                        double? dRiskAverse = allRiskAversions[dRiskAversionCat];
                        DMSApproximator e = new DMSApproximator(0.5, c, 0, pRiskAverse, dRiskAverse, true);
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

        private static void ExperimentWithDifferentSignals()
        {
            double t = 1;
            foreach (double c in new double[] { 0.45 })
            {
                foreach (double q in new double[] { 0.4 })
                {
                    double pUtilityCum = 0, dUtilityCum = 0;
                    DMSApproximator tester = new DMSApproximator(q, c, t, null, null, false, execute: false);
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
