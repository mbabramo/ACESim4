﻿using System;
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
            if (Options.IncludeCourtSuccessReport)
            {
                var courtSuccessReport = GetCourtSuccessReport();
                courtSuccessReport.ActionsOverride = MyGameActionsGenerator.GamePlaysOutToTrial;
                reports.Add(courtSuccessReport);
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
                new SimpleReportColumnFilter("NoDispute", (GameProgress gp) => !MyGP(gp).DisputeArises, true),
                new SimpleReportColumnFilter("DisputeArises", (GameProgress gp) => MyGP(gp).DisputeArises, true),
                new SimpleReportColumnVariable("LitigQuality", (GameProgress gp) => MyGP(gp).LitigationQualityUniform),
                new SimpleReportColumnFilter("PFiles", (GameProgress gp) => MyGP(gp).PFiles, false),
                new SimpleReportColumnFilter("DAnswers", (GameProgress gp) => MyGP(gp).DAnswers, false),
            };
            for (byte b = 1; b <= Options.NumPotentialBargainingRounds; b++)
            {
                byte bargainingRoundNum = b; // needed for closure -- otherwise b below will always be max value.
                colItems.Add(
                    new SimpleReportColumnFilter($"PBargains{b}",
                        (GameProgress gp) => !Options.IncludeAgreementToBargainDecisions || ((MyGP(gp).PAgreesToBargain?.Count() ?? 0) >= bargainingRoundNum && MyGP(gp).PAgreesToBargain[bargainingRoundNum - 1]), false)
                );

                colItems.Add(
                    new SimpleReportColumnFilter($"DBargains{b}",
                        (GameProgress gp) => !Options.IncludeAgreementToBargainDecisions || ((MyGP(gp).DAgreesToBargain?.Count() ?? 0) >= bargainingRoundNum && MyGP(gp).DAgreesToBargain[bargainingRoundNum - 1]), false)
                );
                colItems.Add(
                    new SimpleReportColumnVariable($"POffer{b}",
                        (GameProgress gp) => MyGP(gp).GetOffer(true, bargainingRoundNum))
                );
                colItems.Add(
                    new SimpleReportColumnVariable($"DOffer{b}",
                        (GameProgress gp) => MyGP(gp).GetOffer(false, bargainingRoundNum))
                );
                colItems.Add(
                    new SimpleReportColumnFilter($"Settles{b}",
                        (GameProgress gp) => MyGP(gp).SettlementValue != null &&
                                             MyGP(gp).BargainingRoundsComplete == bargainingRoundNum, false)
                );

                colItems.Add(
                    new SimpleReportColumnVariable($"PBet{b}",
                        (GameProgress gp) => MyGP(gp).GetPlayerBet(true, bargainingRoundNum))
                );
                colItems.Add(
                    new SimpleReportColumnVariable($"DBet{b}",
                        (GameProgress gp) => MyGP(gp).GetPlayerBet(false, bargainingRoundNum))
                );
                colItems.Add(
                    new SimpleReportColumnFilter($"PAbandons{b}",
                        (GameProgress gp) => (MyGP(gp).PAbandons && bargainingRoundNum == MyGP(gp).BargainingRoundsComplete), false)
                );
                colItems.Add(
                    new SimpleReportColumnFilter($"DDefaults{b}",
                        (GameProgress gp) => (MyGP(gp).DDefaults && bargainingRoundNum == MyGP(gp).BargainingRoundsComplete), false)
                );
            }
            colItems.AddRange(new List<SimpleReportColumnItem>()
            {
                new SimpleReportColumnFilter("Trial", (GameProgress gp) => MyGP(gp).TrialOccurs, false),
                new SimpleReportColumnFilter("PWinsAtTrial", (GameProgress gp) => MyGP(gp).TrialOccurs && MyGP(gp).PWinsAtTrial == true, false),
                new SimpleReportColumnFilter("DWinsAtTrial", (GameProgress gp) => MyGP(gp).TrialOccurs && MyGP(gp).PWinsAtTrial == false, false),
                new SimpleReportColumnFilter("PWinPct", (GameProgress gp) => !MyGP(gp).TrialOccurs ? (bool?) null : MyGP(gp).PWinsAtTrial, false),
                new SimpleReportColumnVariable("PWealth", (GameProgress gp) => MyGP(gp).PFinalWealth),
                new SimpleReportColumnVariable("DWealth", (GameProgress gp) => MyGP(gp).DFinalWealth),
                new SimpleReportColumnVariable("PWelfare", (GameProgress gp) => MyGP(gp).PWelfare),
                new SimpleReportColumnVariable("DWelfare", (GameProgress gp) => MyGP(gp).DWelfare),
                new SimpleReportColumnVariable("TotExpense", (GameProgress gp) => MyGP(gp).TotalExpensesIncurred),
                new SimpleReportColumnVariable("False+", (GameProgress gp) => MyGP(gp).FalsePositiveExpenditures),
                new SimpleReportColumnVariable("False-", (GameProgress gp) => MyGP(gp).FalseNegativeShortfall),
                new SimpleReportColumnVariable("PDSWelfare", (GameProgress gp) => MyGP(gp).PreDisputeSWelfare),
                new SimpleReportColumnVariable("Chips", (GameProgress gp) => MyGP(gp).NumChips),
                new SimpleReportColumnFilter("Settles", (GameProgress gp) => MyGP(gp).SettlementValue != null, false),
                new SimpleReportColumnVariable("ValIfSettled", (GameProgress gp) => MyGP(gp).SettlementValue),
                new SimpleReportColumnFilter("PAbandons", (GameProgress gp) => MyGP(gp).PAbandons, false),
                new SimpleReportColumnFilter("DDefaults", (GameProgress gp) => MyGP(gp).DDefaults, false),
                new SimpleReportColumnFilter("BothReadyToGiveUp", (GameProgress gp) => MyGP(gp).BothReadyToGiveUp, false),
                new SimpleReportColumnFilter("TrulyLiable", (GameProgress gp) => MyGP(gp).IsTrulyLiable, false),
                new SimpleReportColumnVariable("PrimaryAction", (GameProgress gp) => (double) MyGP(gp).DisputeGeneratorActions.PrimaryAction),
            });
            // Now go through bargaining rounds again for our litigation flow diagram
            colItems.Add(
                new SimpleReportColumnFilter($"PDoesntFile",
                    (GameProgress gp) => !MyGP(gp).PFiles, false)
            );
            colItems.Add(
                new SimpleReportColumnFilter($"P Files",
                    (GameProgress gp) => MyGP(gp).PFiles, false)
            );
            colItems.Add(
                new SimpleReportColumnFilter($"DDoesntAnswer",
                    (GameProgress gp) => MyGP(gp).PFiles && !MyGP(gp).DAnswers, false)
            );
            colItems.Add(
                new SimpleReportColumnFilter($"D Answers",
                    (GameProgress gp) => MyGP(gp).PFiles && MyGP(gp).DAnswers, false)
            );
            for (byte b = 1; b <= Options.NumPotentialBargainingRounds; b++)
            {
                byte bargainingRoundNum = b; // needed for closure -- otherwise b below will always be max value.
                
                colItems.Add(
                    new SimpleReportColumnFilter($"Settles{b}",
                        (GameProgress gp) => MyGP(gp).SettlesInRound(bargainingRoundNum), false)
                );

                colItems.Add(
                    new SimpleReportColumnFilter($"DoesntSettle{b}",
                        (GameProgress gp) => MyGP(gp).DoesntSettleInRound(bargainingRoundNum), false)
                );

                colItems.Add(
                    new SimpleReportColumnFilter($"PAbandons{b}",
                        (GameProgress gp) => (MyGP(gp).PAbandonsInRound(bargainingRoundNum)), false)
                );
                colItems.Add(
                    new SimpleReportColumnFilter($"PDoesntAbandon{b}",
                        (GameProgress gp) => (MyGP(gp).PDoesntAbandonInRound(bargainingRoundNum)), false)
                );
                colItems.Add(
                    new SimpleReportColumnFilter($"DDefaults{b}",
                        (GameProgress gp) => (MyGP(gp).DDefaultsInRound(bargainingRoundNum)), false)
                );
                colItems.Add(
                    new SimpleReportColumnFilter($"DDoesntDefault{b}",
                        (GameProgress gp) => (MyGP(gp).DDoesntDefaultInRound(bargainingRoundNum)), false)
                );
            }

            colItems.Add(
                new SimpleReportColumnFilter($"P Loses",
                    (GameProgress gp) => MyGP(gp).TrialOccurs && !MyGP(gp).PWinsAtTrial, false)
            );

            colItems.Add(
                new SimpleReportColumnFilter($"P Wins",
                    (GameProgress gp) => MyGP(gp).TrialOccurs && MyGP(gp).PWinsAtTrial, false)
            );
            var rows = new List<SimpleReportFilter>()
            {
                new SimpleReportFilter("All", (GameProgress gp) => true),
                new SimpleReportFilter("NoDispute", (GameProgress gp) => !MyGP(gp).DisputeArises),
                new SimpleReportFilter("DisputeArises", (GameProgress gp) => MyGP(gp).DisputeArises),
                new SimpleReportFilter("Litigated", (GameProgress gp) => MyGP(gp).PFiles && MyGP(gp).DAnswers),
                new SimpleReportFilter("Settles", (GameProgress gp) => MyGP(gp).CaseSettles),
                new SimpleReportFilter("Tried", (GameProgress gp) => MyGP(gp).TrialOccurs),
                new SimpleReportFilter("Abandoned", (GameProgress gp) => MyGP(gp).PAbandons || MyGP(gp).DDefaults),
                new SimpleReportFilter("OneRound", (GameProgress gp) => MyGP(gp).BargainingRoundsComplete == 1),
                new SimpleReportFilter("TwoRounds", (GameProgress gp) => MyGP(gp).BargainingRoundsComplete == 2),
                new SimpleReportFilter("ThreeRounds", (GameProgress gp) => MyGP(gp).BargainingRoundsComplete == 3),
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
                new SimpleReportFilter("Custom", (GameProgress gp) => MyGP(gp).PSignalUniform >= 0.7 && !MyGP(gp).DDefaults),
            };
            for (byte signal = 1; signal < 11; signal++)
            {
                byte s = signal; // avoid closure
                rows.Add(
                    new SimpleReportFilter("PrePrimary" + s, (GameProgress gp) => MyGP(gp).DisputeGeneratorActions.PrePrimaryChanceAction == s));
            }
            for (byte signal = 1; signal <= Options.NumSignals; signal++)
            {
                byte s = signal; // avoid closure
                rows.Add(
                    new SimpleReportFilter("PSignal" + s, (GameProgress gp) => MyGP(gp).PSignalDiscrete == s));
            }
            for (byte signal = 1; signal <= Options.NumSignals; signal++)
            {
                byte s = signal; // avoid closure
                rows.Add(
                    new SimpleReportFilter("DSignal" + s, (GameProgress gp) => MyGP(gp).DSignalDiscrete == s));
            }
            for (byte litigationQuality = 1; litigationQuality <= Options.NumLitigationQualityPoints; litigationQuality++)
            {
                byte q = litigationQuality; // avoid closure
                rows.Add(
                    new SimpleReportFilter("Quality" + q, (GameProgress gp) => MyGP(gp).LitigationQualityDiscrete == q));
            }
            rows.Add(new SimpleReportFilter("Truly Liable", (GameProgress gp) => MyGP(gp).IsTrulyLiable));
            // DEBUG
            //for (byte litigationQuality = 1; litigationQuality <= Options.NumLitigationQualityPoints; litigationQuality++)
            //{
            //    byte q = litigationQuality; // avoid closure
            //    rows.Add(
            //        new SimpleReportFilter("Quality (Truly Liable) " + q, (GameProgress gp) => MyGP(gp).IsTrulyLiable && MyGP(gp).LitigationQualityDiscrete == q) { MultiplyByAllColumnForRowWithName = "Quality (Truly Liable) " + q, DivideByAllColumnForRowWithName = "Truly Liable"});
            //}
            rows.Add(new SimpleReportFilter("Truly Not Liable", (GameProgress gp) => !MyGP(gp).IsTrulyLiable));
            //for (byte litigationQuality = 1; litigationQuality <= Options.NumLitigationQualityPoints; litigationQuality++)
            //{
            //    byte q = litigationQuality; // avoid closure
            //    rows.Add(
            //        new SimpleReportFilter("Quality (Truly Not Liable) " + q, (GameProgress gp) => !MyGP(gp).IsTrulyLiable && MyGP(gp).LitigationQualityDiscrete == q) { MultiplyByAllColumnForRowWithName = "Quality (Truly Liable) " + q, DivideByAllColumnForRowWithName = "Truly Liable" });
            //}
            //for (byte signal = 1; signal <= Options.NumSignals; signal++)
            //{
            //    byte s = signal; // avoid closure
            //    simpleReportFilters.Add(
            //        new SimpleReportFilter("LitigPSignal" + s, (GameProgress gp) => MyGP(gp).PSignalDiscrete == s && MyGP(gp).PFiles && MyGP(gp).DAnswers));
            //}
            //for (byte signal = 1; signal <= Options.NumSignals; signal++)
            //{
            //    byte s = signal; // avoid closure
            //    simpleReportFilters.Add(
            //        new SimpleReportFilter("LitigDSignal" + s, (GameProgress gp) => MyGP(gp).DSignalDiscrete == s && MyGP(gp).PFiles && MyGP(gp).DAnswers));
            //}
            return new SimpleReportDefinition(
                "MyGameReport",
                null,
                rows,
                colItems
            );
        }

        private SimpleReportDefinition GetCourtSuccessReport()
        {
            bool reportResponseToOffer = false;
            (bool plaintiffMakesOffer, int offerNumber, bool isSimultaneous) =
                GetOfferorAndNumber(1, ref reportResponseToOffer);
            string reportName =
                $"Probability of winning given {(plaintiffMakesOffer ? "P" : "D")} signal";
            List<SimpleReportFilter> metaFilters = new List<SimpleReportFilter>()
            {
                new SimpleReportFilter("All", (GameProgress gp) => true)
            };

            List<SimpleReportFilter> rowFilters = new List<SimpleReportFilter>()
            {
                new SimpleReportFilter("All", (GameProgress gp) => true)
            };
            AddRowFilterSignalRegions(plaintiffMakesOffer, rowFilters);
            AddRowFiltersLitigationQuality(rowFilters);
            List<SimpleReportColumnItem> columnItems = new List<SimpleReportColumnItem>()
            {
                new SimpleReportColumnFilter("All", (GameProgress gp) => true, true),
                new SimpleReportColumnFilter("Trial", (GameProgress gp) => MyGP(gp).TrialOccurs, false),
                new SimpleReportColumnFilter("PWinsAtTrial", (GameProgress gp) => MyGP(gp).TrialOccurs && MyGP(gp).PWinsAtTrial == true, false),
                new SimpleReportColumnFilter("DWinsAtTrial", (GameProgress gp) => MyGP(gp).TrialOccurs && MyGP(gp).PWinsAtTrial == false, false),
                new SimpleReportColumnVariable("PWelfare", (GameProgress gp) => MyGP(gp).PWelfare),
                new SimpleReportColumnVariable("DWelfare", (GameProgress gp) => MyGP(gp).DWelfare),
            };
            AddColumnFiltersLitigationQuality(columnItems);
            AddColumnFiltersPSignal(columnItems);
            return new SimpleReportDefinition(
                    reportName,
                    metaFilters,
                    rowFilters,
                    columnItems,
                    reportResponseToOffer
                )
                {ActionsOverride = MyGameActionsGenerator.GamePlaysOutToTrial};
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
            List<SimpleReportColumnItem> columnItems = new List<SimpleReportColumnItem>()
            {
                new SimpleReportColumnFilter("All", (GameProgress gp) => true, true)
            };
            AddColumnFiltersLitigationQuality(columnItems);
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


        private void AddRowFiltersLitigationQuality(List<SimpleReportFilter> rowFilters)
        {
            for (int i = 1; i <= Options.NumLitigationQualityPoints; i++)
            {
                byte j = (byte)(i); // necessary to prevent access to modified closure
                rowFilters.Add(new SimpleReportFilter(
                    $"LitQual {j}",
                    (GameProgress gp) => MyGP(gp).LitigationQualityDiscrete == j));
            }
            //for (int i = 1; i <= Options.NumNoiseValues; i++)
            //{
            //    byte j = (byte)(i); // necessary to prevent access to modified closure
            //    rowFilters.Add(new SimpleReportFilter(
            //        $"LitQual 1 Noise {j}",
            //        (GameProgress gp) => MyGP(gp).LitigationQualityDiscrete == 1 && MyGP(gp).PNoiseDiscrete == j));
            //}
        }
        private void AddColumnFiltersLitigationQuality(List<SimpleReportColumnItem> columnFilters)
        {
            for (int i = 1; i <= Options.NumLitigationQualityPoints; i++)
            {
                byte j = (byte)(i); // necessary to prevent access to modified closure
                columnFilters.Add(new SimpleReportColumnFilter(
                    $"LitQual {j}",
                    (GameProgress gp) => MyGP(gp).LitigationQualityDiscrete == j,
                    false));
            }
        }
        private void AddColumnFiltersPSignal(List<SimpleReportColumnItem> columnFilters)
        {
            for (int i = 1; i <= Options.NumSignals; i++)
            {
                byte j = (byte)(i); // necessary to prevent access to modified closure
                columnFilters.Add(new SimpleReportColumnFilter(
                    $"PSignal {j}",
                    (GameProgress gp) => MyGP(gp).PSignalDiscrete == j,
                    false));
            }
        }

        private void AddRowFilterSignalRegions(bool plaintiffMakesOffer, List<SimpleReportFilter> rowFilters)
        {
            Tuple<double, double>[] signalRegions = EquallySpaced.GetRegions(Options.NumSignals);
            for (int i = 0; i < Options.NumSignals; i++)
            {
                double regionStart = signalRegions[i].Item1;
                double regionEnd = signalRegions[i].Item2;
                if (plaintiffMakesOffer)
                {
                    byte j = (byte) (i + 1);
                    rowFilters.Add(new SimpleReportFilter(
                        $"PSignal {j}",
                        (GameProgress gp) => MyGP(gp).PSignalDiscrete == j));
                }
                else
                {
                    byte j = (byte) (i + 1);
                    rowFilters.Add(new SimpleReportFilter(
                        $"DSignal {j}",
                        (GameProgress gp) => MyGP(gp).DSignalDiscrete == j));
                }
            }
        }

        private Func<GameProgress, bool?> GetOfferOrResponseFilter(bool plaintiffMakesOffer, int offerNumber,
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