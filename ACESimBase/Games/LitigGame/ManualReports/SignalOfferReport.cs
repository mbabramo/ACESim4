﻿using ACESim;
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
        public static List<string> GenerateReport(LitigGameDefinition gameDefinition, List<(GameProgress theProgress, double weight)> gameProgresses)
        {
            List<(LitigGameProgress theProgress, double weight)> litigProgresses = gameProgresses.Select(x => ((LitigGameProgress)x.theProgress, x.weight)).ToList();
            Func<LitigGameProgress, double> pLiabilitySignalFunc = x => x.PLiabilitySignalUniform;
            Func<LitigGameProgress, double> dLiabilitySignalFunc = x => x.DLiabilitySignalUniform;
            Func<LitigGameProgress, double> pDamagesSignalFunc = x => x.PDamagesSignalUniform;
            Func<LitigGameProgress, double> dDamagesSignalFunc = x => x.DDamagesSignalUniform;

            List<double> pLiabilitySignals = litigProgresses.Select(x => x.theProgress.PLiabilitySignalUniform).Distinct().OrderByDescending(x => x).ToList();
            List<double> dLiabilitySignals = litigProgresses.Select(x => x.theProgress.DLiabilitySignalUniform).Distinct().OrderByDescending(x => x).ToList();
            List<double> pDamagesSignals = litigProgresses.Select(x => x.theProgress.PDamagesSignalUniform).Distinct().OrderByDescending(x => x).ToList();
            List<double> dDamagesSignals = litigProgresses.Select(x => x.theProgress.DDamagesSignalUniform).Distinct().OrderByDescending(x => x).ToList();

            LitigGameOptions options = gameDefinition.Options;
            bool useLiabilitySignals = options.NumLiabilitySignals > 1;

            int numSignals = useLiabilitySignals ? options.NumLiabilitySignals : options.NumDamagesSignals;
            int numOffers = options.NumOffers;
            double[] offers = EquallySpaced.GetEquallySpacedPoints(options.NumOffers, options.IncludeEndpointsForOffers);
            (var pFunction, var pSignals) = useLiabilitySignals ? (pLiabilitySignalFunc, pLiabilitySignals) : (pDamagesSignalFunc, pDamagesSignals);
            (var dFunction, var dSignals) = useLiabilitySignals ? (dLiabilitySignalFunc, dLiabilitySignals) : (dDamagesSignalFunc, dDamagesSignals);

            List<List<(string text, double darkness)>> pContents = new List<List<(string text, double darkness)>>(), dContents = new List<List<(string text, double darkness)>>();

            List<(string text, double darkness)> pRow = new List<(string text, double darkness)>();
            List<(string text, double darkness)> dRow = new List<(string text, double darkness)>();
            // header row
            pRow.Add(("", 0));
            dRow.Add(("", 0));
            for (int offerIndex = 0; offerIndex < numOffers; offerIndex++)
            {
                string offerValueString = offers[offerIndex].ToSignificantFigures(2).ToString();
                pRow.Add((offerValueString, 0));
                dRow.Add((offerValueString, 0));
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
                // inner contents
                for (int offerIndex = 0; offerIndex < numOffers; offerIndex++)
                {
                    double offerValue = offers[offerIndex];
                    Func<LitigGameProgress, bool> pNumeratorFn = x => x.PFirstOffer == offerValue;
                    Func<LitigGameProgress, bool> pDenominatorFn = x => pFunction(x) == pSignal && (x.POffers?.Any() ?? false);
                    Func<LitigGameProgress, bool> dNumeratorFn = x => x.DFirstOffer == offerValue;
                    Func<LitigGameProgress, bool> dDenominatorFn = x => dFunction(x) == dSignal && (x.DOffers?.Any() ?? false);

                    (string representation, double darknessValue) GetProportionString(Func<LitigGameProgress, bool> numeratorFunction, Func<LitigGameProgress, bool> denominatorFunction)
                    {
                        var numerator = litigProgresses.Where(x => numeratorFunction(x.theProgress) && denominatorFunction(x.theProgress)).Sum(x => x.weight);
                        var denominator = litigProgresses.Where(x => denominatorFunction(x.theProgress)).Sum(x => x.weight);
                        if (denominator == 0)
                            return ("N/A", 0);
                        double proportion = numerator / denominator;
                        return (((int)(Math.Round(proportion, 2) * 100)).ToString() + "\\%", Math.Round(Math.Sqrt(proportion), 2));
                    }

                    pRow.Add(GetProportionString(pNumeratorFn, pDenominatorFn));
                    dRow.Add(GetProportionString(dNumeratorFn, dDenominatorFn));
                }
                pContents.Add(pRow);
                dContents.Add(dRow);
            }
            List<double> relativeWidths = Enumerable.Range(0, numOffers + 1).Select(x => 1.0 / (double)(numOffers + 1)).ToList();
            TikzRectangle overallRectangle = new TikzRectangle(0, 0, numOffers <= 5 ? 12 : 12 * ((double) numOffers / 5.0), 5);
            List<TikzRectangle> separateRectangles = overallRectangle.DivideLeftToRight(new double[] { 0.47, 0.06, 0.47 });
            TikzRectangle pRect = separateRectangles[0];
            TikzRectangle dRect = separateRectangles[2];

            string numberAttributes = null;
            if (numOffers > 5)
                numberAttributes = "font=\\tiny";
            TikzHeatMap pHeatMap = new TikzHeatMap("P Offer", "P Signal", numberAttributes, pRect, "blue", relativeWidths, pContents);
            TikzHeatMap dHeatMap = new TikzHeatMap("D Offer", "D Signal", numberAttributes, dRect, "orange", relativeWidths, dContents);

            StringBuilder b = new StringBuilder();
            b.AppendLine(pHeatMap.DrawCommands());
            b.AppendLine(dHeatMap.DrawCommands());

            string doc = TikzHelper.GetStandaloneDocument(b.ToString(), new List<string>() { "xcolor" });
            return new List<string>() { doc };
        }
    }
}