using ACESim;
using System;
using System.Diagnostics;
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


            ExperimentWithDifferentSignals();

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

            string headerRow = "Cost,Quality,Aversion," + DMSApproximatorOutcome.GetHeaderString();
            b.AppendLine(headerRow);
            VaryRiskAversion(b);

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
                        DMSApproximator e = new DMSApproximator(q, c, t, null, null);
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

        private static void VaryRiskAversion(StringBuilder b)
        {
            foreach (double c in allCosts)
            {
                foreach (double q in allQualities)
                {
                    foreach (double r in allRiskAversions)
                    {
                        DMSApproximator e = new DMSApproximator(q, c, 0, r, r);
                        string rowPrefix = $"{c},{q},{r},";
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
                    DMSApproximator tester = new DMSApproximator(q, c, t, execute: false);
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
