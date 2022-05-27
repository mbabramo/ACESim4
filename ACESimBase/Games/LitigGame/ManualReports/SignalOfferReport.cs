using ACESim;
using ACESim.Util;
using ACESimBase.Games.AdditiveEvidenceGame;
using ACESimBase.Util.Tikz;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESimBase.Games.LitigGame.ManualReports
{
    public class SignalOfferReport
    {
        public enum TypeOfReport
        {
            Offers,
            FileAndAnswer
        }

        public static List<string> GenerateReport(AdditiveEvidenceGameDefinition gameDefinition, List<(GameProgress theProgress, double weight)> gameProgresses, TypeOfReport reportType)
        {
            List<(AdditiveEvidenceGameProgress theProgress, double weight)> additiveEvidenceProgresses = gameProgresses.Select(x => ((AdditiveEvidenceGameProgress)x.theProgress, x.weight)).ToList();
            List<(ISignalOfferReportGameProgress theProgress, double weight)> signalOfferReportGameProgresses = gameProgresses.Select(x => ((ISignalOfferReportGameProgress)x.theProgress, x.weight)).ToList();
            Func<ISignalOfferReportGameProgress, double> pDamagesSignalFunc = x => ((AdditiveEvidenceGameProgress)x).Chance_Plaintiff_Bias_Continuous;
            Func<ISignalOfferReportGameProgress, double> dDamagesSignalFunc = x => ((AdditiveEvidenceGameProgress)x).Chance_Defendant_Bias_Continuous;

            List<double> pDamagesSignals = additiveEvidenceProgresses.Select(x => x.theProgress.Chance_Plaintiff_Bias_Continuous).Distinct().OrderByDescending(x => x).ToList();
            List<double> dDamagesSignals = additiveEvidenceProgresses.Select(x => x.theProgress.Chance_Defendant_Bias_Continuous).Distinct().OrderByDescending(x => x).ToList();

            AdditiveEvidenceGameOptions options = gameDefinition.Options;
            bool useLiabilitySignals = false;
            int numSignals = options.NumQualityAndBiasLevels_PrivateInfo;
            int numOffers = options.NumOffers;
            bool includeEndpointsForOffers = false;

            DMSCalc calc = new DMSCalc(options.Evidence_Both_Quality, options.TrialCost, options.FeeShiftingThreshold);
            List<(double p, double d)> dmsBids = Enumerable.Range(0, 99).Select(x => 0.005 + (double)x / 100.0).Select(x => calc.GetBids(x, x)).ToList();

            return CompleteReport(reportType, signalOfferReportGameProgresses, null, null, pDamagesSignalFunc, dDamagesSignalFunc, null, null, pDamagesSignals, dDamagesSignals, useLiabilitySignals, numSignals, numOffers, includeEndpointsForOffers, dmsBids);
        }

        public static List<string> GenerateReport(LitigGameDefinition gameDefinition, List<(GameProgress theProgress, double weight)> gameProgresses, TypeOfReport reportType)
        {
            List<(LitigGameProgress theProgress, double weight)> litigProgresses = gameProgresses.Select(x => ((LitigGameProgress)x.theProgress, x.weight)).ToList();
            List<(ISignalOfferReportGameProgress theProgress, double weight)> signalOfferReportGameProgresses = gameProgresses.Select(x => ((ISignalOfferReportGameProgress)x.theProgress, x.weight)).ToList();
            Func<ISignalOfferReportGameProgress, double> pLiabilitySignalFunc = x => ((LitigGameProgress)x).PLiabilitySignalUniform;
            Func<ISignalOfferReportGameProgress, double> dLiabilitySignalFunc = x => ((LitigGameProgress)x).DLiabilitySignalUniform;
            Func<ISignalOfferReportGameProgress, double> pDamagesSignalFunc = x => ((LitigGameProgress)x).PDamagesSignalUniform;
            Func<ISignalOfferReportGameProgress, double> dDamagesSignalFunc = x => ((LitigGameProgress)x).DDamagesSignalUniform;

            List<double> pLiabilitySignals = litigProgresses.Select(x => x.theProgress.PLiabilitySignalUniform).Distinct().OrderByDescending(x => x).ToList();
            List<double> dLiabilitySignals = litigProgresses.Select(x => x.theProgress.DLiabilitySignalUniform).Distinct().OrderByDescending(x => x).ToList();
            List<double> pDamagesSignals = litigProgresses.Select(x => x.theProgress.PDamagesSignalUniform).Distinct().OrderByDescending(x => x).ToList();
            List<double> dDamagesSignals = litigProgresses.Select(x => x.theProgress.DDamagesSignalUniform).Distinct().OrderByDescending(x => x).ToList();

            LitigGameOptions options = gameDefinition.Options;
            bool useLiabilitySignals = options.NumLiabilitySignals > 1;
            int numSignals = useLiabilitySignals ? options.NumLiabilitySignals : options.NumDamagesSignals;
            int numOffers = options.NumOffers;
            bool includeEndpointsForOffers = options.IncludeEndpointsForOffers;

            return CompleteReport(reportType, signalOfferReportGameProgresses, pLiabilitySignalFunc, dLiabilitySignalFunc, pDamagesSignalFunc, dDamagesSignalFunc, pLiabilitySignals, dLiabilitySignals, pDamagesSignals, dDamagesSignals, useLiabilitySignals, numSignals, numOffers, includeEndpointsForOffers, null);
        }

        private static List<string> CompleteReport(TypeOfReport reportType, List<(ISignalOfferReportGameProgress theProgress, double weight)> litigProgresses, Func<ISignalOfferReportGameProgress, double> pLiabilitySignalFunc, Func<ISignalOfferReportGameProgress, double> dLiabilitySignalFunc, Func<ISignalOfferReportGameProgress, double> pDamagesSignalFunc, Func<ISignalOfferReportGameProgress, double> dDamagesSignalFunc, List<double> pLiabilitySignals, List<double> dLiabilitySignals, List<double> pDamagesSignals, List<double> dDamagesSignals, bool useLiabilitySignals, int numSignals, int numOffers, bool includeEndpointsForOffers, List<(double p, double d)> superimposedLines)
        {
            double[] offers = EquallySpaced.GetEquallySpacedPoints(numOffers, includeEndpointsForOffers);
            string[] fileActionStrings = new string[] { "No Suit", "File" };
            string[] answerActionStrings = new string[] { "Default", "Answer" };

            bool transpose = true; // move offers to y axis
            if (transpose)
            {
                if (pLiabilitySignals != null)
                    pLiabilitySignals.Reverse();
                if (dLiabilitySignals != null)
                    dLiabilitySignals.Reverse();
                if (pDamagesSignals != null)
                    pDamagesSignals.Reverse();
                if (dDamagesSignals != null)
                    dDamagesSignals.Reverse();
                offers = offers.Reverse().ToArray();
                fileActionStrings = fileActionStrings.Reverse().ToArray();
                answerActionStrings = answerActionStrings.Reverse().ToArray();
            }

            (var pFunction, var pSignals) = useLiabilitySignals ? (pLiabilitySignalFunc, pLiabilitySignals) : (pDamagesSignalFunc, pDamagesSignals);
            (var dFunction, var dSignals) = useLiabilitySignals ? (dLiabilitySignalFunc, dLiabilitySignals) : (dDamagesSignalFunc, dDamagesSignals);

            List<List<(string text, double darkness)>> pContents = new List<List<(string text, double darkness)>>(), dContents = new List<List<(string text, double darkness)>>();

            List<(string text, double darkness)> pRow = new List<(string text, double darkness)>();
            List<(string text, double darkness)> dRow = new List<(string text, double darkness)>();
            // header row
            pRow.Add(("", 0));
            dRow.Add(("", 0));
            if (reportType == TypeOfReport.Offers)
            {
                for (int offerIndex = 0; offerIndex < numOffers; offerIndex++)
                {
                    string offerValueString = offers[offerIndex].ToSignificantFigures(2).ToString();
                    pRow.Add((offerValueString, 0));
                    dRow.Add((offerValueString, 0));
                }
            }
            else
            {
                pRow.Add((fileActionStrings[0], 0));
                pRow.Add((fileActionStrings[1], 0));
                dRow.Add((answerActionStrings[0], 0));
                dRow.Add((answerActionStrings[1], 0));
            }
            pContents.Add(pRow);
            dContents.Add(dRow);
            // body rows
            for (int signalIndex = 0; signalIndex < numSignals; signalIndex++)
            {
                // header column
                pRow = new List<(string text, double darkness)>() { (pSignals[signalIndex].ToSignificantFigures(2), 0) };
                dRow = new List<(string text, double darkness)>() { (dSignals[signalIndex].ToSignificantFigures(2), 0) };
                double pSignal = pSignals[signalIndex];
                double dSignal = dSignals[signalIndex];


                (string representation, double darknessValue) GetProportionString(Func<ISignalOfferReportGameProgress, bool> numeratorFunction, Func<ISignalOfferReportGameProgress, bool> denominatorFunction)
                {
                    var numerator = litigProgresses.Where(x => numeratorFunction(x.theProgress) && denominatorFunction(x.theProgress)).Sum(x => x.weight);
                    var denominator = litigProgresses.Where(x => denominatorFunction(x.theProgress)).Sum(x => x.weight);
                    if (denominator == 0)
                        return ("N/A", 0);
                    double proportion = numerator / denominator;
                    return (((int)(Math.Round(Math.Round(proportion, 2) * 100))).ToString() + "\\%", Math.Round(Math.Sqrt(proportion), 2));
                }

                // inner contents
                if (reportType == TypeOfReport.Offers)
                {
                    for (int offerIndex = 0; offerIndex < numOffers; offerIndex++)
                    {
                        double offerValue = offers[offerIndex];
                        Func<ISignalOfferReportGameProgress, bool> pNumeratorFn = x => x.PFirstOffer == offerValue;
                        Func<ISignalOfferReportGameProgress, bool> pDenominatorFn = x => pFunction(x) == pSignal && (x.POffers?.Any() ?? false);
                        Func<ISignalOfferReportGameProgress, bool> dNumeratorFn = x => x.DFirstOffer == offerValue;
                        Func<ISignalOfferReportGameProgress, bool> dDenominatorFn = x => dFunction(x) == dSignal && (x.DOffers?.Any() ?? false);

                        pRow.Add(GetProportionString(pNumeratorFn, pDenominatorFn));
                        dRow.Add(GetProportionString(dNumeratorFn, dDenominatorFn));
                    }
                }
                else
                {
                    Func<ISignalOfferReportGameProgress, bool> pDenominatorFn = x => pFunction(x) == pSignal;
                    Func<ISignalOfferReportGameProgress, bool> dDenominatorFn = x => dFunction(x) == dSignal && x.PFiles;
                    Func<ISignalOfferReportGameProgress, bool> pFilesFn = x => x.PFiles;
                    Func<ISignalOfferReportGameProgress, bool> pNoSuitFn = x => !x.PFiles;
                    Func<ISignalOfferReportGameProgress, bool> dAnswersFn = x => x.DAnswers;
                    Func<ISignalOfferReportGameProgress, bool> dDefaultsFn = x => !x.DAnswers;

                    pRow.Add(GetProportionString(pFilesFn, pDenominatorFn));
                    dRow.Add(GetProportionString(dAnswersFn, dDenominatorFn));
                    pRow.Add(GetProportionString(pNoSuitFn, pDenominatorFn));
                    dRow.Add(GetProportionString(dDefaultsFn, dDenominatorFn));
                }
                pContents.Add(pRow);
                dContents.Add(dRow);
            }

            int numXAxisItems = transpose ? numSignals : numOffers;
            List<double> relativeWidths = Enumerable.Range(0, numXAxisItems + 1).Select(x => 1.0 / (double)(numXAxisItems + 1)).ToList();
            TikzRectangle overallRectangle = new TikzRectangle(0, 0, numXAxisItems <= 5 ? 12 : 12 * ((double)numXAxisItems / 5.0), 5);
            List<TikzRectangle> separateRectangles = overallRectangle.DivideLeftToRight(new double[] { 0.49, 0.02, 0.49 });
            TikzRectangle pRect = separateRectangles[0];
            TikzRectangle dRect = separateRectangles[2];

            string xAxisWordP = reportType == TypeOfReport.Offers ? "P Offer" : "File Decision";
            string xAxisWordD = reportType == TypeOfReport.Offers ? "D Offer" : "Answer Decision";
            string yAxisWordP = "P Signal";
            string yAxisWordD = "D Signal";
            if (transpose)
            {
                var temp = xAxisWordP;
                xAxisWordP = yAxisWordP;
                yAxisWordP = temp;

                temp = xAxisWordD;
                xAxisWordD = yAxisWordD;
                yAxisWordD = temp;

                pContents = pContents.Transpose();
                var horizontalAxis = pContents[0];
                pContents.RemoveAt(0);
                pContents.Add(horizontalAxis);
                dContents = dContents.Transpose();
                horizontalAxis = dContents[0];
                dContents.RemoveAt(0);
                dContents.Add(horizontalAxis);
            }

            string numberAttributes = null;
            if (numXAxisItems > 5)
                numberAttributes = "font=\\tiny";
            TikzHeatMap pHeatMap = new TikzHeatMap(xAxisWordP, yAxisWordP, !transpose, numberAttributes, pRect, "blue", relativeWidths, pContents);
            TikzHeatMap dHeatMap = new TikzHeatMap(xAxisWordD, yAxisWordD, !transpose, numberAttributes, dRect, "orange", relativeWidths, dContents);

            StringBuilder b = new StringBuilder();
            b.AppendLine(pHeatMap.DrawCommands());
            b.AppendLine(dHeatMap.DrawCommands());

            if (superimposedLines != null)
            {
                TikzLineGraphData pLineGraphData = new TikzLineGraphData(new List<List<double?>> { superimposedLines.Select(x => (double?)x.p).ToList() }, new List<string>() { "red, opacity=0.70, line width=1mm, dashed" }, new List<string>() { "DMS" });
                TikzLineGraphData dLineGraphData = new TikzLineGraphData(new List<List<double?>> { superimposedLines.Select(x => (double?)x.d).ToList() }, new List<string>() { "red, opacity=0.70, line width=1mm, dashed" }, new List<string>() { "DMS" });
                var xValNames = Enumerable.Range(0, superimposedLines.Count()).Select(x => (x + 1.0) / (superimposedLines.Count() + 1)).Select(x => x.ToString()).ToList();
                TikzAxisSet pAxisSet = new TikzAxisSet(xValNames, null, null, null, pHeatMap.MainRectangleWithoutAxes, yAxisSpace: 0, xAxisSpace: 0, lineGraphData: pLineGraphData);
                TikzAxisSet dAxisSet = new TikzAxisSet(xValNames, null, null, null, dHeatMap.MainRectangleWithoutAxes, yAxisSpace: 0, xAxisSpace: 0, lineGraphData: dLineGraphData);
                b.AppendLine(pAxisSet.GetDrawLineGraphCommands());
                b.AppendLine(dAxisSet.GetDrawLineGraphCommands());


            }

            string doc = TikzHelper.GetStandaloneDocument(b.ToString(), new List<string>() { "xcolor" });
            return new List<string>() { doc };
        }
    }
}
