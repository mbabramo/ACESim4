using System;
using System.Collections.Generic;
using System.Linq;

namespace ACESim
{
    public partial class MyGameDefinition
    {
        public override List<SimpleReportDefinition> GetSimpleReportDefinitions()
        {
            var reports = new List<SimpleReportDefinition>
            {
                GetOverallReport()
            };
            if (Options.AdditionalTableOverrides != null && Options.AdditionalTableOverrides.Any())
            {
                foreach (var overrideWithName in Options.AdditionalTableOverrides)
                {
                    var report = GetOverallReport();
                    report.ActionsOverride = overrideWithName.Item1;
                    report.Name += $" ({overrideWithName.Item2})";
                    reports.Add(report);
                }
            }
            
            if (Options.IncludeSignalsReport)
            {
                for (int b = 1; b <= Options.NumPotentialBargainingRounds; b++)
                {
                    reports.Add(GetStrategyReport(b, false));
                    reports.Add(GetStrategyReport(b, true));
                }
            }
            return reports;
        }

        private SimpleReportDefinition GetOverallReport()
        {
            var colItems = new List<SimpleReportColumnItem>()
            {
                new SimpleReportColumnFilter("All", (GameProgress gp) => true, true),
                new SimpleReportColumnVariable("LitigQuality", (GameProgress gp) => MyGP(gp).LitigationQualityUniform),
                new SimpleReportColumnFilter("PFiles", (GameProgress gp) => MyGP(gp).PFiles, false),
                new SimpleReportColumnFilter("DAnswers", (GameProgress gp) => MyGP(gp).DAnswers, false),
                new SimpleReportColumnVariable("PFirstOffer", (GameProgress gp) => MyGP(gp).PFirstOffer),
                new SimpleReportColumnVariable("DFirstOffer", (GameProgress gp) => MyGP(gp).DFirstOffer),
                new SimpleReportColumnVariable("PLastOffer", (GameProgress gp) => MyGP(gp).PLastOffer),
                new SimpleReportColumnVariable("DLastOffer", (GameProgress gp) => MyGP(gp).DLastOffer),
                new SimpleReportColumnFilter("Settles", (GameProgress gp) => MyGP(gp).SettlementValue != null, false),
                new SimpleReportColumnVariable("ValIfSettled", (GameProgress gp) => MyGP(gp).SettlementValue),
                new SimpleReportColumnFilter("PAbandons", (GameProgress gp) => MyGP(gp).PAbandons, false),
                new SimpleReportColumnFilter("DDefaults", (GameProgress gp) => MyGP(gp).DDefaults, false),
                new SimpleReportColumnFilter("BothReadyToGiveUp", (GameProgress gp) => MyGP(gp).BothReadyToGiveUp, false),
                new SimpleReportColumnFilter("Trial", (GameProgress gp) => MyGP(gp).TrialOccurs, false),
                new SimpleReportColumnFilter("PWinsAtTrial", (GameProgress gp) => MyGP(gp).TrialOccurs && MyGP(gp).PWinsAtTrial == true, false),
                new SimpleReportColumnFilter("DWinsAtTrial", (GameProgress gp) => MyGP(gp).TrialOccurs && MyGP(gp).PWinsAtTrial == false, false),
                new SimpleReportColumnVariable("PWelfare", (GameProgress gp) => MyGP(gp).PWelfare),
                new SimpleReportColumnVariable("DWelfare", (GameProgress gp) => MyGP(gp).DWelfare),
            };
            for (int b = 1; b <= Options.NumPotentialBargainingRounds; b++)
            {
                int bargainingRoundNum = b; // needed for closure -- otherwise b below will always be max value.
                colItems.Add(
                    new SimpleReportColumnFilter($"Settles{b}",
                        (GameProgress gp) => MyGP(gp).SettlementValue != null &&
                                             MyGP(gp).BargainingRoundsComplete == bargainingRoundNum, false)
                );
            }
            return new SimpleReportDefinition(
                "MyGameReport",
                null,
                new List<SimpleReportFilter>()
                {
                    new SimpleReportFilter("All", (GameProgress gp) => true),
                    new SimpleReportFilter("Litigated", (GameProgress gp) => MyGP(gp).PFiles && MyGP(gp).DAnswers),
                    new SimpleReportFilter("Settles", (GameProgress gp) => MyGP(gp).CaseSettles),
                    new SimpleReportFilter("Tried", (GameProgress gp) => !MyGP(gp).CaseSettles),
                    new SimpleReportFilter("LowQuality",
                        (GameProgress gp) => MyGP(gp).LitigationQualityUniform <= 0.25),
                    new SimpleReportFilter("MediumQuality",
                        (GameProgress gp) => MyGP(gp).LitigationQualityUniform > 0.25 &&
                                             MyGP(gp).LitigationQualityUniform < 0.75),
                    new SimpleReportFilter("HighQuality",
                        (GameProgress gp) => MyGP(gp).LitigationQualityUniform >= 0.75),
                    new SimpleReportFilter("LowPSignal", (GameProgress gp) => MyGP(gp).PSignalUniform <= 0.25),
                    new SimpleReportFilter("LowDSignal", (GameProgress gp) => MyGP(gp).DSignalUniform <= 0.25),
                    new SimpleReportFilter("MedPSignal",
                        (GameProgress gp) => MyGP(gp).PSignalUniform > 0.25 && MyGP(gp).PSignalUniform < 0.75),
                    new SimpleReportFilter("MedDSignal",
                        (GameProgress gp) => MyGP(gp).DSignalUniform > 0.25 && MyGP(gp).DSignalUniform < 0.75),
                    new SimpleReportFilter("HiPSignal", (GameProgress gp) => MyGP(gp).PSignalUniform >= 0.75),
                    new SimpleReportFilter("HiDSignal", (GameProgress gp) => MyGP(gp).DSignalUniform >= 0.75),
                    new SimpleReportFilter("Custom", (GameProgress gp) => MyGP(gp).PSignalDiscrete == 9),
                },
                colItems
            );
        }

        private SimpleReportDefinition GetStrategyReport(int bargainingRound, bool reportResponseToOffer)
        {
            (bool plaintiffMakesOffer, int offerNumber, bool isSimultaneous) =
                GetOfferorAndNumber(bargainingRound, ref reportResponseToOffer);
            string reportName =
                $"Round {bargainingRound} {(reportResponseToOffer ? "ResponseTo" : "")}{(plaintiffMakesOffer ? "P" : "D")} {offerNumber}";
            List<SimpleReportFilter> metaFilters = new List<SimpleReportFilter>
            {
                new SimpleReportFilter("RoundOccurs",
                    (GameProgress gp) => MyGP(gp).BargainingRoundsComplete >= bargainingRound)
            };
            List<SimpleReportFilter> rowFilters = new List<SimpleReportFilter>()
            {
                new SimpleReportFilter("All", (GameProgress gp) => true)
            };
            Tuple<double, double>[] signalRegions = EquallySpaced.GetRegions(Options.NumSignals);
            for (int i = 0; i < Options.NumSignals; i++)
            {
                double regionStart = signalRegions[i].Item1;
                double regionEnd = signalRegions[i].Item2;
                if (plaintiffMakesOffer)
                {
                    byte j = (byte) (i + 1);
                    if (Options.ActionIsNoiseNotSignal)
                        rowFilters.Add(new SimpleReportFilter(
                        $"PSignal {j}",
                        (GameProgress gp) => MyGP(gp).PSignalDiscrete == j));
                    else
                        rowFilters.Add(new SimpleReportFilter(
                        $"PSignal {regionStart.ToSignificantFigures(2)}-{regionEnd.ToSignificantFigures(2)}",
                        (GameProgress gp) => MyGP(gp).PSignalUniform >= regionStart &&
                                             MyGP(gp).PSignalUniform < regionEnd));
                }
                else
                {
                    byte j = (byte)(i + 1);
                    if (Options.ActionIsNoiseNotSignal)
                        rowFilters.Add(new SimpleReportFilter(
                            $"DSignal {j}",
                            (GameProgress gp) => MyGP(gp).DSignalDiscrete == j));
                    else
                        rowFilters.Add(new SimpleReportFilter(
                        $"DSignal {regionStart.ToSignificantFigures(2)}-{regionEnd.ToSignificantFigures(2)}",
                        (GameProgress gp) => MyGP(gp).DSignalUniform >= regionStart &&
                                             MyGP(gp).DSignalUniform < regionEnd));
                }
            }
            List<SimpleReportColumnItem> columnItems = new List<SimpleReportColumnItem>()
            {
                new SimpleReportColumnFilter("All", (GameProgress gp) => true, true)
            };
            Tuple<double, double>[] offerRegions = EquallySpaced.GetRegions(Options.NumOffers);
            for (int i = 0; i < Options.NumOffers; i++)
            {
                double regionStart = offerRegions[i].Item1;
                double regionEnd = offerRegions[i].Item2;
                columnItems.Add(new SimpleReportColumnFilter(
                        $"{(reportResponseToOffer ? "To" : "")}{(plaintiffMakesOffer ? "P" : "D")}{offerNumber} {regionStart.ToSignificantFigures(2)}-{regionEnd.ToSignificantFigures(2)}{(reportResponseToOffer ? "" : "  ")}",
                        GetOfferOrResponseFilter(plaintiffMakesOffer, offerNumber, reportResponseToOffer,
                            offerRegions[i]),
                        false
                    )
                );
            }
            return new SimpleReportDefinition(
                reportName,
                metaFilters,
                rowFilters,
                columnItems,
                reportResponseToOffer
            );
        }

        private Func<GameProgress, bool> GetOfferOrResponseFilter(bool plaintiffMakesOffer, int offerNumber,
            bool reportResponseToOffer, Tuple<double, double> offerRange)
        {
            if (reportResponseToOffer)
                return (GameProgress gp) => IsInOfferRange(MyGP(gp).GetOffer(plaintiffMakesOffer, offerNumber),
                                                offerRange) && MyGP(gp).GetResponse(!plaintiffMakesOffer, offerNumber);
            else
                return (GameProgress gp) => IsInOfferRange(MyGP(gp).GetOffer(plaintiffMakesOffer, offerNumber),
                    offerRange);
        }
        private (bool plaintiffMakesOffer, int offerNumber, bool isSimultaneous) GetOfferorAndNumber(int bargainingRound, ref bool reportResponseToOffer)
        {
            bool plaintiffMakesOffer = true;
            int offerNumber = 0;
            bool isSimultaneous = false;
            int earlierOffersPlaintiff = 0, earlierOffersDefendant = 0;
            for (int b = 1; b <= bargainingRound; b++)
            {
                if (b < bargainingRound)
                {
                    if (Options.BargainingRoundsSimultaneous)
                    {
                        earlierOffersPlaintiff++;
                        earlierOffersDefendant++;
                    }
                    else
                    {
                        if (Options.PGoesFirstIfNotSimultaneous[b - 1])
                            earlierOffersPlaintiff++;
                        else
                            earlierOffersDefendant++;
                    }
                }
                else
                {
                    if (Options.BargainingRoundsSimultaneous)
                    {
                        plaintiffMakesOffer = !reportResponseToOffer;
                        reportResponseToOffer = false; // we want to report the offer (which may be the defendant's).
                        isSimultaneous = false;
                    }
                    else
                        plaintiffMakesOffer = Options.PGoesFirstIfNotSimultaneous[b - 1];
                    offerNumber = plaintiffMakesOffer ? earlierOffersPlaintiff + 1 : earlierOffersDefendant + 1;
                }
            }
            return (plaintiffMakesOffer, offerNumber, isSimultaneous);
        }

        private bool IsInOfferRange(double? value, Tuple<double, double> offerRange)
        {
            return value != null && value >= offerRange.Item1 && value < offerRange.Item2;
        }
    }
}