using System;
using System.Collections.Generic;
using System.Linq;

namespace ACESim
{
    public partial class LitigGameDefinition
    {

        // TODO: Getting parties' probability estimates. Right now, we know the parties' signals at various times and we can thus look at the distribution of those signals, including the possibility of separating into subsets of cases(e.g., truly liable or much more narrowly, cases in which the defendant has a signal of 6 and the plaintiff has given a particular offer). But we might also like to know more specifically the distribution of litigation quality estimates at various points.For any given information set, we should be able to calculate the actual litigation quality at each information set. That is, we would look at the game history and get a cached index with the litigation quality and then increment an array item held at the information set.Thus, we can imbue the distribution of signals with meaning given the point in the game. We could use a similar approach to estimate the other party's signals or anything else that might vary.
        // A further benefit of something like this might be that we could measure how well informed parties are about the actual litigation quality at various points in the game. You could, for example, measure the average absolute difference between a party's derived estimate and the true value. We could then compare this across different game situations. So at various points, if in a mode where we are doing this, the game would request the relevant mode (perhaps set by noting that we are reporting in the game progress), seek out the information set, and get the relevant information. 
        //How to do it: Ideally, we would calculate this AFTER optimization but BEFORE reporting, so that it gives us the most up-to-date information. Essentially, we want to have a probe from the beginning of the game. The probe doesn't have any effect on the evolution. But as we go through the probe, we would increment some other variable. When replaying at a particular point, we must take into account the possibility that the node has not been visited, in which case the distribution must be empty (in which case it won't factor into the average). 

        //Reporting: If using our current reporting approach, then for each item in the distribution, we need a separate variable for each game point(e.g., defendant offer in bargaining round 3). The problem is that we need to know the party's estimate AT THAT POINT -- so, we can't just have a column variable for the party's estimate across all game situations and then filter down.  But this should still be helpful. We can make it a column variable (like how much a party bets at a certain point), indicating the average expected quality (rather than a distribution), and the average absolute error in this estimate.

        // Alternative diagram A broader problem with the diagrams as we imagine them is that they aggregate all situations, so they don't show things from the perspective of a particular player.Arguably, this calls for creating a game tree based on a specific iteration of the game, showing each party's information. It would be cool, for a particular optimization, to be able to show a particular game tree. So we need to be able to produce a list of the different game steps. We could then have a graphic that shows the actual litigation quality, followed by a next column with the plaintiff's and defendant's estimates of litigation quality (as a distribution), followed by another column with the plaintiff's and defendant's distribution of offers, then the actual offer. We would then need some graphic illustrating the outcome for both parties (relative to the ideal one). In principal, we could find a "representative" optimization and then compare similar situations under two different rules. We might have a graphic that shows us cases getting resolved rapid fire for a particular initial situation.

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
                courtSuccessReport.ActionsOverride = LitigGameActionsGenerator.GamePlaysOutToTrial;
                reports.Add(courtSuccessReport);
            }
            if (Options.IncludeSignalsReport)
            {
                for (int b = 1; b <= Options.NumPotentialBargainingRounds; b++)
                {
                    reports.Add(GetSignalsReport(b, false));
                    reports.Add(GetSignalsReport(b, true)); // i.e., if not simultaneous, report fraction relative to previous report
                }
            }

            return reports;
        }

        private SimpleReportDefinition GetOverallReport()
        {
            var colItems = new List<SimpleReportColumnItem>()
            {
                //new SimpleReportColumnVariable("PWelfare", (GameProgress gp) => MyGP(gp).PWelfare),
                //new SimpleReportColumnVariable("DWelfare", (GameProgress gp) => MyGP(gp).DWelfare),
                new SimpleReportColumnFilter("All", (GameProgress gp) => true, SimpleReportColumnFilterOptions.ProportionOfAll),
                new SimpleReportColumnFilter("NoDispute", (GameProgress gp) => !MyGP(gp).DisputeArises, SimpleReportColumnFilterOptions.ProportionOfAll),
                new SimpleReportColumnFilter("DisputeArises", (GameProgress gp) => MyGP(gp).DisputeArises, SimpleReportColumnFilterOptions.ProportionOfAll),
                new SimpleReportColumnVariable("LitigQuality", (GameProgress gp) => MyGP(gp).LiabilityStrengthUniform),
                new SimpleReportColumnFilter("PFiles", (GameProgress gp) => MyGP(gp).PFiles, SimpleReportColumnFilterOptions.ProportionOfRow),
                new SimpleReportColumnFilter("DAnswers", (GameProgress gp) => MyGP(gp).DAnswers, SimpleReportColumnFilterOptions.ProportionOfRow),
            };
            for (byte b = 1; b <= Options.NumPotentialBargainingRounds; b++)
            {
                byte bargainingRoundNum = b; // needed for closure -- otherwise b below will always be max value.
                colItems.Add(
                    new SimpleReportColumnFilter($"PBargains{b}",
                        (GameProgress gp) => MyGP(gp).PAgreesToBargainInRound(bargainingRoundNum), SimpleReportColumnFilterOptions.ProportionOfRow)
                );

                colItems.Add(
                    new SimpleReportColumnFilter($"DBargains{b}",
                        (GameProgress gp) => MyGP(gp).DAgreesToBargainInRound(bargainingRoundNum), SimpleReportColumnFilterOptions.ProportionOfRow)
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
                    new SimpleReportColumnVariable($"POfferMixedness{b}",
                        (GameProgress gp) => MyGP(gp).GetOfferMixedness(true, bargainingRoundNum))
                );
                colItems.Add(
                    new SimpleReportColumnVariable($"DOfferMixedness{b}",
                        (GameProgress gp) => MyGP(gp).GetOfferMixedness(false, bargainingRoundNum))
                );
                colItems.Add(
                    new SimpleReportColumnFilter($"Settles{b}",
                        (GameProgress gp) => MyGP(gp).SettlementValue != null &&
                                             MyGP(gp).BargainingRoundsComplete == bargainingRoundNum, SimpleReportColumnFilterOptions.ProportionOfRow)
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
                    new SimpleReportColumnVariable($"PBetMixedness{b}",
                        (GameProgress gp) => MyGP(gp).GetBetMixedness(true, bargainingRoundNum))
                );
                colItems.Add(
                    new SimpleReportColumnVariable($"DBetMixedness{b}",
                        (GameProgress gp) => MyGP(gp).GetBetMixedness(false, bargainingRoundNum))
                );
                colItems.Add(
                    new SimpleReportColumnFilter($"PAbandons{b}",
                        (GameProgress gp) => (MyGP(gp).PAbandons && bargainingRoundNum == MyGP(gp).BargainingRoundsComplete), SimpleReportColumnFilterOptions.ProportionOfRow)
                );
                colItems.Add(
                    new SimpleReportColumnFilter($"DDefaults{b}",
                        (GameProgress gp) => (MyGP(gp).DDefaults && bargainingRoundNum == MyGP(gp).BargainingRoundsComplete), SimpleReportColumnFilterOptions.ProportionOfRow)
                );
            }
            colItems.AddRange(new List<SimpleReportColumnItem>()
            {
                new SimpleReportColumnFilter("Trial", (GameProgress gp) => MyGP(gp).TrialOccurs, SimpleReportColumnFilterOptions.ProportionOfRow),
                new SimpleReportColumnFilter("PWinPct", (GameProgress gp) => !MyGP(gp).TrialOccurs ? (bool?) null : MyGP(gp).PWinsAtTrial, SimpleReportColumnFilterOptions.ProportionOfRow),
                new SimpleReportColumnVariable("PWealth", (GameProgress gp) => MyGP(gp).PFinalWealth),
                new SimpleReportColumnVariable("DWealth", (GameProgress gp) => MyGP(gp).DFinalWealth),
                new SimpleReportColumnVariable("PWelfare", (GameProgress gp) => MyGP(gp).PWelfare),
                new SimpleReportColumnVariable("DWelfare", (GameProgress gp) => MyGP(gp).DWelfare),
                new SimpleReportColumnVariable("TotExpense", (GameProgress gp) => MyGP(gp).TotalExpensesIncurred),
                new SimpleReportColumnVariable("False+", (GameProgress gp) => MyGP(gp).FalsePositiveExpenditures),
                new SimpleReportColumnVariable("False-", (GameProgress gp) => MyGP(gp).FalseNegativeShortfall),
                new SimpleReportColumnVariable("PDSWelfare", (GameProgress gp) => MyGP(gp).PreDisputeSharedWelfare),
                new SimpleReportColumnVariable("Chips", (GameProgress gp) => MyGP(gp).NumChips),
                new SimpleReportColumnFilter("Settles", (GameProgress gp) => MyGP(gp).SettlementValue != null, SimpleReportColumnFilterOptions.ProportionOfRow),
                new SimpleReportColumnVariable("ValIfSettled", (GameProgress gp) => MyGP(gp).SettlementValue),
                new SimpleReportColumnFilter("PAbandons", (GameProgress gp) => MyGP(gp).PAbandons, SimpleReportColumnFilterOptions.ProportionOfRow),
                new SimpleReportColumnFilter("DDefaults", (GameProgress gp) => MyGP(gp).DDefaults, SimpleReportColumnFilterOptions.ProportionOfRow),
                new SimpleReportColumnFilter("BothReadyToGiveUp", (GameProgress gp) => MyGP(gp).BothReadyToGiveUp, SimpleReportColumnFilterOptions.ProportionOfRow),
                new SimpleReportColumnFilter("TrulyLiable", (GameProgress gp) => MyGP(gp).IsTrulyLiable, SimpleReportColumnFilterOptions.ProportionOfRow),
                new SimpleReportColumnVariable("PrimaryAction", (GameProgress gp) => (double) MyGP(gp).DisputeGeneratorActions.PrimaryAction),
                new SimpleReportColumnVariable("MutualOptimism", (GameProgress gp) => (double) MyGP(gp).PLiabilitySignalDiscrete - (double) MyGP(gp).DLiabilitySignalDiscrete),
            });
            // Now go through bargaining rounds again for our litigation flow diagram
            colItems.Add(
                new SimpleReportColumnFilter($"P Files",
                    (GameProgress gp) => MyGP(gp).PFiles, SimpleReportColumnFilterOptions.ProportionOfRow)
            );
            colItems.Add(
                new SimpleReportColumnFilter($"D Answers",
                    (GameProgress gp) => MyGP(gp).PFiles && MyGP(gp).DAnswers, SimpleReportColumnFilterOptions.ProportionOfRow)
                    );
            colItems.Add(
                new SimpleReportColumnFilter($"PDoesntFile",
                    (GameProgress gp) => !MyGP(gp).PFiles, SimpleReportColumnFilterOptions.ProportionOfRow)
            );
            colItems.Add(
                new SimpleReportColumnFilter($"DDoesntAnswer",
                    (GameProgress gp) => MyGP(gp).PFiles && !MyGP(gp).DAnswers, SimpleReportColumnFilterOptions.ProportionOfRow)
            );
            for (byte b = 1; b <= Options.NumPotentialBargainingRounds; b++)
            {
                byte bargainingRoundNum = b; // needed for closure -- otherwise b below will always be max value.
                
                colItems.Add(
                    new SimpleReportColumnFilter($"SettlesBR{b}",
                        (GameProgress gp) => MyGP(gp).SettlesInRound(bargainingRoundNum), SimpleReportColumnFilterOptions.ProportionOfRow)
                );

                //colItems.Add(
                //    new SimpleReportColumnFilter($"DoesntSettleBR{b}",
                //        (GameProgress gp) => MyGP(gp).DoesntSettleInRound(bargainingRoundNum), SimpleReportColumnFilterOptions.ProportionOfRow)
                //);

                colItems.Add(
                    new SimpleReportColumnFilter($"PAbandonsBR{b}",
                        (GameProgress gp) => (MyGP(gp).PAbandonsInRound(bargainingRoundNum)), SimpleReportColumnFilterOptions.ProportionOfRow)
                );
                //colItems.Add(
                //    new SimpleReportColumnFilter($"PDoesntAbandonBR{b}",
                //        (GameProgress gp) => (MyGP(gp).PDoesntAbandonInRound(bargainingRoundNum)), SimpleReportColumnFilterOptions.ProportionOfRow)
                //);
                colItems.Add(
                    new SimpleReportColumnFilter($"DDefaultsBR{b}",
                        (GameProgress gp) => (MyGP(gp).DDefaultsInRound(bargainingRoundNum)), SimpleReportColumnFilterOptions.ProportionOfRow)
                );
                //colItems.Add(
                //    new SimpleReportColumnFilter($"DDoesntDefaultBR{b}",
                //        (GameProgress gp) => (MyGP(gp).DDoesntDefaultInRound(bargainingRoundNum)), SimpleReportColumnFilterOptions.ProportionOfRow)
                //);
                //colItems.Add(
                //    new SimpleReportColumnFilter($"POrDQuitsBR{b}",
                //        (GameProgress gp) => (MyGP(gp).PAbandonsInRound(bargainingRoundNum)) || (MyGP(gp).DDefaultsInRound(bargainingRoundNum)), SimpleReportColumnFilterOptions.ProportionOfRow)
                //);


            }

            colItems.Add(
                new SimpleReportColumnFilter($"P Loses",
                    (GameProgress gp) => MyGP(gp).TrialOccurs && !MyGP(gp).PWinsAtTrial, SimpleReportColumnFilterOptions.ProportionOfRow)
            );

            colItems.Add(
                new SimpleReportColumnFilter($"P Wins",
                    (GameProgress gp) => MyGP(gp).TrialOccurs && MyGP(gp).PWinsAtTrial, SimpleReportColumnFilterOptions.ProportionOfRow)
            );
            List<SimpleReportFilter> rows;
            if (Options.FirstRowOnly)
                rows = new List<SimpleReportFilter>()
                {
                    new SimpleReportFilter("All", (GameProgress gp) => true),
                };
            else
            {
                rows = new List<SimpleReportFilter>()
                {
                    new SimpleReportFilter("All", (GameProgress gp) => true),
                    new SimpleReportFilter("NoDispute", (GameProgress gp) => !MyGP(gp).DisputeArises),
                    new SimpleReportFilter("DisputeArises", (GameProgress gp) => MyGP(gp).DisputeArises),
                    new SimpleReportFilter("PFiles", (GameProgress gp) => MyGP(gp).PFiles),
                    new SimpleReportFilter("DAnswers", (GameProgress gp) => MyGP(gp).DAnswers),
                    new SimpleReportFilter("Litigated", (GameProgress gp) => MyGP(gp).PFiles && MyGP(gp).DAnswers),
                    new SimpleReportFilter("Settles", (GameProgress gp) => MyGP(gp).CaseSettles),
                    new SimpleReportFilter("Tried", (GameProgress gp) => MyGP(gp).TrialOccurs),
                    new SimpleReportFilter("PWins", (GameProgress gp) => MyGP(gp).PWinsAtTrial),
                    new SimpleReportFilter("DWins", (GameProgress gp) => MyGP(gp).DWinsAtTrial),
                    new SimpleReportFilter("Abandoned", (GameProgress gp) => MyGP(gp).PAbandons || MyGP(gp).DDefaults),
                };
                for (int b = 1; b <= Options.NumPotentialBargainingRounds; b++)
                {
                    int b2 = b; // avoid closure
                    rows.Add(
                        new SimpleReportFilter("Rounds" + b, (GameProgress gp) => MyGP(gp).BargainingRoundsComplete == b2)
                    );
                }
                    //new SimpleReportFilter("LowQuality",
                    //    (GameProgress gp) => MyGP(gp).LiabilityStrengthUniform <= 0.25),
                    //new SimpleReportFilter("MediumQuality",
                    //    (GameProgress gp) => MyGP(gp).LiabilityStrengthUniform > 0.25 &&
                    //                         MyGP(gp).LiabilityStrengthUniform < 0.75),
                    //new SimpleReportFilter("HighQuality",
                    //    (GameProgress gp) => MyGP(gp).LiabilityStrengthUniform >= 0.75),
                    //new SimpleReportFilter("LowPLiabilitySignal", (GameProgress gp) => MyGP(gp).PLiabilitySignalUniform <= 0.25),
                    //new SimpleReportFilter("LowDLiabilitySignal", (GameProgress gp) => MyGP(gp).DLiabilitySignalUniform <= 0.25),
                    //new SimpleReportFilter("MedPLiabilitySignal",
                    //    (GameProgress gp) => MyGP(gp).PLiabilitySignalUniform > 0.25 && MyGP(gp).PLiabilitySignalUniform < 0.75),
                    //new SimpleReportFilter("MedDLiabilitySignal",
                    //    (GameProgress gp) => MyGP(gp).DLiabilitySignalUniform > 0.25 && MyGP(gp).DLiabilitySignalUniform < 0.75),
                    //new SimpleReportFilter("HiPLiabilitySignal", (GameProgress gp) => MyGP(gp).PLiabilitySignalUniform >= 0.75),
                    //new SimpleReportFilter("HiDLiabilitySignal", (GameProgress gp) => MyGP(gp).DLiabilitySignalUniform >= 0.75),
                    //new SimpleReportFilter("Custom", (GameProgress gp) => MyGP(gp).PLiabilitySignalUniform >= 0.7 && !MyGP(gp).DDefaults),
                
                for (byte signal = 1; signal < 11; signal++)
                {
                    byte s = signal; // avoid closure
                    rows.Add(
                        new SimpleReportFilter("PrePrimary" + s, (GameProgress gp) => MyGP(gp).DisputeGeneratorActions.PrePrimaryChanceAction == s));
                }
                rows.Add(new SimpleReportFilter("Truly Liable", (GameProgress gp) => MyGP(gp).IsTrulyLiable));
                rows.Add(new SimpleReportFilter("Truly Not Liable", (GameProgress gp) => !MyGP(gp).IsTrulyLiable));
                rows.Add(new SimpleReportFilter("AllCount", (GameProgress gp) => true) { UseSum = true });
                bool useCounts = false;
                string countString = useCounts ? " Count" : "";
                AddRowsForLiabilityStrengthAndSignalDistributions("", true, useCounts, rows, gp => true);
                if (useCounts)
                    rows.Add(new SimpleReportFilter($"Truly Liable{countString}", (GameProgress gp) => MyGP(gp).IsTrulyLiable) { UseSum = useCounts });
                AddRowsForLiabilityStrengthAndSignalDistributions("Truly Liable ", false, useCounts, rows, gp => MyGP(gp).IsTrulyLiable);
                if (useCounts)
                    rows.Add(new SimpleReportFilter($"Truly Not Liable{countString}", (GameProgress gp) => !MyGP(gp).IsTrulyLiable) { UseSum = useCounts });
                AddRowsForLiabilityStrengthAndSignalDistributions("Truly Not Liable ", false, useCounts, rows, gp => !MyGP(gp).IsTrulyLiable);
                rows.Add(new SimpleReportFilter($"High Quality{countString}", (GameProgress gp) => MyGP(gp).LiabilityStrengthDiscrete >= 8) { UseSum = useCounts });
                AddRowsForLiabilityStrengthAndSignalDistributions("High Quality Liable ", false, useCounts, rows, gp => MyGP(gp).LiabilityStrengthDiscrete >= 8);
                AddRowsForDamagesStrengthAndSignalDistributions("", true, false, rows, gp => true);
                AddRowFilterSignalRegions(true, rows);
                AddRowFilterSignalRegions(false, rows);
                // Now include the litigation flow diagram in rows too, so we get get things like average total expenses for cases settling in particular round
                rows.Add(
                    new SimpleReportFilter($"PDoesntFile",
                        (GameProgress gp) => !MyGP(gp).PFiles)
                );
                rows.Add(
                    new SimpleReportFilter($"P Files",
                        (GameProgress gp) => MyGP(gp).PFiles)
                );
                rows.Add(
                    new SimpleReportFilter($"DDoesntAnswer",
                        (GameProgress gp) => MyGP(gp).PFiles && !MyGP(gp).DAnswers)
                );
                rows.Add(
                    new SimpleReportFilter($"D Answers",
                        (GameProgress gp) => MyGP(gp).PFiles && MyGP(gp).DAnswers)
                );
                for (byte b = 1; b <= Options.NumPotentialBargainingRounds; b++)
                {
                    byte bargainingRoundNum = b; // needed for closure -- otherwise b below will always be max value.

                    rows.Add(
                        new SimpleReportFilter($"Settles{b}",
                            (GameProgress gp) => MyGP(gp).SettlesInRound(bargainingRoundNum))
                    );

                    rows.Add(
                        new SimpleReportFilter($"DoesntSettle{b}",
                            (GameProgress gp) => MyGP(gp).DoesntSettleInRound(bargainingRoundNum))
                    );

                    rows.Add(
                        new SimpleReportFilter($"PAbandons{b}",
                            (GameProgress gp) => (MyGP(gp).PAbandonsInRound(bargainingRoundNum)))
                    );
                    rows.Add(
                        new SimpleReportFilter($"PDoesntAbandon{b}",
                            (GameProgress gp) => (MyGP(gp).PDoesntAbandonInRound(bargainingRoundNum)))
                    );
                    rows.Add(
                        new SimpleReportFilter($"DDefaults{b}",
                            (GameProgress gp) => (MyGP(gp).DDefaultsInRound(bargainingRoundNum)))
                    );
                    rows.Add(
                        new SimpleReportFilter($"DDoesntDefault{b}",
                            (GameProgress gp) => (MyGP(gp).DDoesntDefaultInRound(bargainingRoundNum)))
                    );

                    rows.Add(
                        new SimpleReportFilter($"POrDQuits{b}",
                            (GameProgress gp) => (MyGP(gp).PAbandonsInRound(bargainingRoundNum)) || (MyGP(gp).DDefaultsInRound(bargainingRoundNum)))
                    );
                }

                rows.Add(
                    new SimpleReportFilter($"P Loses",
                        (GameProgress gp) => MyGP(gp).TrialOccurs && !MyGP(gp).PWinsAtTrial)
                );

                rows.Add(
                    new SimpleReportFilter($"P Wins",
                        (GameProgress gp) => MyGP(gp).TrialOccurs && MyGP(gp).PWinsAtTrial)
                );
            }

            return new SimpleReportDefinition(
                "LitigGameReport",
                null,
                rows,
                colItems
            );
        }

        private void AddRowsForLiabilityStrengthAndSignalDistributions(string prefix, bool includeAverages, bool includeCounts, List<SimpleReportFilter> rows, Func<GameProgress, bool> extraRequirement)
        {
            string countString = includeCounts ? " Count" : "";
            // Note that averages will not be weighted by the number of observations in the column. That's why counts may be more useful.
            if (includeAverages)
                for (byte litigationQuality = 1; litigationQuality <= Options.NumLiabilityStrengthPoints; litigationQuality++)
                {
                    byte q = litigationQuality; // avoid closure
                    rows.Add(
                        new SimpleReportFilter(prefix + "Liability Strength" + q, (GameProgress gp) => MyGP(gp).LiabilityStrengthDiscrete == q && extraRequirement(gp)));
                }
            if (includeCounts)
                for (byte litigationQuality = 1; litigationQuality <= Options.NumLiabilityStrengthPoints; litigationQuality++)
                {
                    byte q = litigationQuality; // avoid closure
                    rows.Add(
                        new SimpleReportFilter(prefix + "Liability Strength" + q + countString, (GameProgress gp) => MyGP(gp).LiabilityStrengthDiscrete == q && extraRequirement(gp)) {UseSum = true});
                }
            if (includeAverages)
                for (byte signal = 1; signal <= Options.NumLiabilitySignals; signal++)
                {
                    byte s = signal; // avoid closure
                    rows.Add(
                        new SimpleReportFilter(prefix + "PLiabilitySignal" + s, (GameProgress gp) => MyGP(gp).PLiabilitySignalDiscrete == s && extraRequirement(gp)));
                }
            if (includeCounts)
                for (byte signal = 1; signal <= Options.NumLiabilitySignals; signal++)
                {
                    byte s = signal; // avoid closure
                    rows.Add(
                        new SimpleReportFilter(prefix + "PLiabilitySignal" + s + countString, (GameProgress gp) => MyGP(gp).PLiabilitySignalDiscrete == s && extraRequirement(gp)) {UseSum = true});
                }
            if (includeAverages)
                for (byte signal = 1; signal <= Options.NumLiabilitySignals; signal++)
                {
                    byte s = signal; // avoid closure
                    rows.Add(
                        new SimpleReportFilter(prefix + "DLiabilitySignal" + s, (GameProgress gp) => MyGP(gp).DLiabilitySignalDiscrete == s && extraRequirement(gp)));
                }

            if (includeCounts)
                for (byte signal = 1; signal <= Options.NumLiabilitySignals; signal++)
                {
                    byte s = signal; // avoid closure
                    rows.Add(
                        new SimpleReportFilter(prefix + "DLiabilitySignal" + s + countString, (GameProgress gp) => MyGP(gp).DLiabilitySignalDiscrete == s && extraRequirement(gp)) {UseSum = true});
                }
            // Add mutual optimism report
            rows.Add(
            new SimpleReportFilter(prefix + "MutOptLiability5" + countString, (GameProgress gp) => MyGP(gp).PLiabilitySignalDiscrete - MyGP(gp).DLiabilitySignalDiscrete >= 5 && extraRequirement(gp)) { UseSum = includeCounts });
            rows.Add(
                new SimpleReportFilter(prefix + "MutOptLiability4" + countString, (GameProgress gp) => MyGP(gp).PLiabilitySignalDiscrete - MyGP(gp).DLiabilitySignalDiscrete == 4 && extraRequirement(gp)) { UseSum = includeCounts });
            rows.Add(
                new SimpleReportFilter(prefix + "MutOptLiability3" + countString, (GameProgress gp) => MyGP(gp).PLiabilitySignalDiscrete - MyGP(gp).DLiabilitySignalDiscrete == 3 && extraRequirement(gp)) { UseSum = includeCounts });
            rows.Add(
                new SimpleReportFilter(prefix + "MutOptLiability2" + countString, (GameProgress gp) => MyGP(gp).PLiabilitySignalDiscrete - MyGP(gp).DLiabilitySignalDiscrete == 2 && extraRequirement(gp)) { UseSum = includeCounts });
            rows.Add(
                new SimpleReportFilter(prefix + "MutOptLiability1" + countString, (GameProgress gp) => MyGP(gp).PLiabilitySignalDiscrete - MyGP(gp).DLiabilitySignalDiscrete == 1 && extraRequirement(gp)) { UseSum = includeCounts });
            rows.Add(
                new SimpleReportFilter(prefix + "MutOptLiability0" + countString, (GameProgress gp) => MyGP(gp).PLiabilitySignalDiscrete - MyGP(gp).DLiabilitySignalDiscrete == 0 && extraRequirement(gp)) { UseSum = includeCounts });
            rows.Add(
                new SimpleReportFilter(prefix + "MutOptLiability-1" + countString, (GameProgress gp) => MyGP(gp).PLiabilitySignalDiscrete - MyGP(gp).DLiabilitySignalDiscrete == -1 && extraRequirement(gp)) { UseSum = includeCounts });
            rows.Add(
                new SimpleReportFilter(prefix + "MutOptLiability-2" + countString, (GameProgress gp) => MyGP(gp).PLiabilitySignalDiscrete - MyGP(gp).DLiabilitySignalDiscrete == -2 && extraRequirement(gp)) { UseSum = includeCounts });
            rows.Add(
                new SimpleReportFilter(prefix + "MutOptLiability-3" + countString, (GameProgress gp) => MyGP(gp).PLiabilitySignalDiscrete - MyGP(gp).DLiabilitySignalDiscrete == -3 && extraRequirement(gp)) { UseSum = includeCounts });
            rows.Add(
                new SimpleReportFilter(prefix + "MutOptLiability-4" + countString, (GameProgress gp) => MyGP(gp).PLiabilitySignalDiscrete - MyGP(gp).DLiabilitySignalDiscrete == -4 && extraRequirement(gp)) { UseSum = includeCounts });
            rows.Add(
                new SimpleReportFilter(prefix + "MutOptLiability-5" + countString, (GameProgress gp) => MyGP(gp).PLiabilitySignalDiscrete - MyGP(gp).DLiabilitySignalDiscrete <= -5 && extraRequirement(gp)) { UseSum = includeCounts });
        }

        private void AddRowsForDamagesStrengthAndSignalDistributions(string prefix, bool includeAverages, bool includeCounts, List<SimpleReportFilter> rows, Func<GameProgress, bool> extraRequirement)
        {
            string countString = includeCounts ? " Count" : "";
            if (Options.NumDamagesStrengthPoints <= 1)
                return;
            // Note that averages will not be weighted by the number of observations in the column. That's why counts may be more useful.
            if (includeAverages)
                for (byte litigationQuality = 1; litigationQuality <= Options.NumDamagesStrengthPoints; litigationQuality++)
                {
                    byte q = litigationQuality; // avoid closure
                    rows.Add(
                        new SimpleReportFilter(prefix + "Damages Strength" + q, (GameProgress gp) => MyGP(gp).DamagesStrengthDiscrete == q && extraRequirement(gp)));
                }
            if (includeCounts)
                for (byte litigationQuality = 1; litigationQuality <= Options.NumDamagesStrengthPoints; litigationQuality++)
                {
                    byte q = litigationQuality; // avoid closure
                    rows.Add(
                        new SimpleReportFilter(prefix + "Damages Strength" + q + countString, (GameProgress gp) => MyGP(gp).DamagesStrengthDiscrete == q && extraRequirement(gp)) { UseSum = true });
                }
            if (includeAverages)
                for (byte signal = 1; signal <= Options.NumDamagesSignals; signal++)
                {
                    byte s = signal; // avoid closure
                    rows.Add(
                        new SimpleReportFilter(prefix + "PDamagesSignal" + s, (GameProgress gp) => MyGP(gp).PDamagesSignalDiscrete == s && extraRequirement(gp)));
                }
            if (includeCounts)
                for (byte signal = 1; signal <= Options.NumDamagesSignals; signal++)
                {
                    byte s = signal; // avoid closure
                    rows.Add(
                        new SimpleReportFilter(prefix + "PDamagesSignal" + s + countString, (GameProgress gp) => MyGP(gp).PDamagesSignalDiscrete == s && extraRequirement(gp)) { UseSum = true });
                }
            if (includeAverages)
                for (byte signal = 1; signal <= Options.NumDamagesSignals; signal++)
                {
                    byte s = signal; // avoid closure
                    rows.Add(
                        new SimpleReportFilter(prefix + "DDamagesSignal" + s, (GameProgress gp) => MyGP(gp).DDamagesSignalDiscrete == s && extraRequirement(gp)));
                }
            if (includeCounts)
                for (byte signal = 1; signal <= Options.NumDamagesSignals; signal++)
                {
                    byte s = signal; // avoid closure
                    rows.Add(
                        new SimpleReportFilter(prefix + "DDamagesSignal" + s + countString, (GameProgress gp) => MyGP(gp).DDamagesSignalDiscrete == s && extraRequirement(gp)) { UseSum = true });
                }
            // Add mutual optimism report
            rows.Add(
            new SimpleReportFilter(prefix + "MutOptDamages5" + countString, (GameProgress gp) => MyGP(gp).PDamagesSignalDiscrete - MyGP(gp).DDamagesSignalDiscrete >= 5 && extraRequirement(gp)) { UseSum = includeCounts });
            rows.Add(
                new SimpleReportFilter(prefix + "MutOptDamages4" + countString, (GameProgress gp) => MyGP(gp).PDamagesSignalDiscrete - MyGP(gp).DDamagesSignalDiscrete == 4 && extraRequirement(gp)) { UseSum = includeCounts });
            rows.Add(
                new SimpleReportFilter(prefix + "MutOptDamages3" + countString, (GameProgress gp) => MyGP(gp).PDamagesSignalDiscrete - MyGP(gp).DDamagesSignalDiscrete == 3 && extraRequirement(gp)) { UseSum = includeCounts });
            rows.Add(
                new SimpleReportFilter(prefix + "MutOptDamages2" + countString, (GameProgress gp) => MyGP(gp).PDamagesSignalDiscrete - MyGP(gp).DDamagesSignalDiscrete == 2 && extraRequirement(gp)) { UseSum = includeCounts });
            rows.Add(
                new SimpleReportFilter(prefix + "MutOptDamages1" + countString, (GameProgress gp) => MyGP(gp).PDamagesSignalDiscrete - MyGP(gp).DDamagesSignalDiscrete == 1 && extraRequirement(gp)) { UseSum = includeCounts });
            rows.Add(
                new SimpleReportFilter(prefix + "MutOptDamages0" + countString, (GameProgress gp) => MyGP(gp).PDamagesSignalDiscrete - MyGP(gp).DDamagesSignalDiscrete == 0 && extraRequirement(gp)) { UseSum = includeCounts });
            rows.Add(
                new SimpleReportFilter(prefix + "MutOptDamages-1" + countString, (GameProgress gp) => MyGP(gp).PDamagesSignalDiscrete - MyGP(gp).DDamagesSignalDiscrete == -1 && extraRequirement(gp)) { UseSum = includeCounts });
            rows.Add(
                new SimpleReportFilter(prefix + "MutOptDamages-2" + countString, (GameProgress gp) => MyGP(gp).PDamagesSignalDiscrete - MyGP(gp).DDamagesSignalDiscrete == -2 && extraRequirement(gp)) { UseSum = includeCounts });
            rows.Add(
                new SimpleReportFilter(prefix + "MutOptDamages-3" + countString, (GameProgress gp) => MyGP(gp).PDamagesSignalDiscrete - MyGP(gp).DDamagesSignalDiscrete == -3 && extraRequirement(gp)) { UseSum = includeCounts });
            rows.Add(
                new SimpleReportFilter(prefix + "MutOptDamages-4" + countString, (GameProgress gp) => MyGP(gp).PDamagesSignalDiscrete - MyGP(gp).DDamagesSignalDiscrete == -4 && extraRequirement(gp)) { UseSum = includeCounts });
            rows.Add(
                new SimpleReportFilter(prefix + "MutOptDamages-5" + countString, (GameProgress gp) => MyGP(gp).PDamagesSignalDiscrete - MyGP(gp).DDamagesSignalDiscrete <= -5 && extraRequirement(gp)) { UseSum = includeCounts });
        }

        private SimpleReportDefinition GetCourtSuccessReport()
        {
            string suboptionSetName =
                $"PlayToTrial";
            string allString = "PlayToTrial-All";
            List<SimpleReportFilter> metaFilters = new List<SimpleReportFilter>()
            {
                new SimpleReportFilter(allString, (GameProgress gp) => true)
            };

            List<SimpleReportFilter> rowFilters = new List<SimpleReportFilter>()
            {
                new SimpleReportFilter(allString, (GameProgress gp) => true)
            };
            bool reportResponseToOffer = false;
            (bool plaintiffMakesOffer, int offerNumber, bool isSimultaneous) =
                GetOfferorAndNumber(1, ref reportResponseToOffer);
            bool reportPlaintiff = (plaintiffMakesOffer && !reportResponseToOffer) || (!plaintiffMakesOffer && reportResponseToOffer);
            AddRowFilterSignalRegions(reportPlaintiff, rowFilters);
            if (reportPlaintiff && Options.BargainingRoundsSimultaneous)
                AddRowFilterSignalRegions(false, rowFilters); // add defendant too
            AddRowFiltersLitigationStrength(rowFilters);
            List<SimpleReportColumnItem> columnItems = new List<SimpleReportColumnItem>()
            {
                new SimpleReportColumnFilter(allString, (GameProgress gp) => true, SimpleReportColumnFilterOptions.ProportionOfAll),
                new SimpleReportColumnFilter("Trial", (GameProgress gp) => MyGP(gp).TrialOccurs, SimpleReportColumnFilterOptions.ProportionOfRow),
                new SimpleReportColumnFilter("PWinsAtTrial", (GameProgress gp) => MyGP(gp).TrialOccurs && MyGP(gp).PWinsAtTrial == true, SimpleReportColumnFilterOptions.ProportionOfRow),
                new SimpleReportColumnFilter("DWinsAtTrial", (GameProgress gp) => MyGP(gp).TrialOccurs && MyGP(gp).PWinsAtTrial == false, SimpleReportColumnFilterOptions.ProportionOfRow),
                new SimpleReportColumnVariable("TrialDamages", (GameProgress gp) => MyGP(gp).DamagesAwarded),
                new SimpleReportColumnVariable("PWelfare", (GameProgress gp) => MyGP(gp).PWelfare),
                new SimpleReportColumnVariable("DWelfare", (GameProgress gp) => MyGP(gp).DWelfare),
            };
            AddColumnFiltersTrulyLiable(columnItems);
            AddColumnFiltersLiabilityStrength(columnItems);
            AddColumnFiltersDamagesStrength(columnItems);
            AddColumnFiltersPLiabilitySignal(columnItems);
            AddColumnFiltersDLiabilitySignal(columnItems);

            bool includeSuboptionSetNameInRow = true;
            string rowNamePrefix = includeSuboptionSetNameInRow ? suboptionSetName + " " : "";
            if (includeSuboptionSetNameInRow)
                foreach (var rowFilter in rowFilters)
                    if (rowFilter.Name != allString)
                        rowFilter.Name = rowNamePrefix + rowFilter.Name;
            return new SimpleReportDefinition(
                    suboptionSetName,
                    metaFilters,
                    rowFilters,
                    columnItems,
                    false // reportResponseToOffer --> right now we're reporting only one report
                )
                {ActionsOverride = LitigGameActionsGenerator.GamePlaysOutToTrial};
        }

        private SimpleReportDefinition GetSignalsReport(int bargainingRound, bool reportResponseToOffer)
        {
            // Note: When reporting response to offer, we are calculating everything in this report as a fraction of what was in the previous report
            (bool plaintiffMakesOffer, int offerNumber, bool isSimultaneous) =
                GetOfferorAndNumber(bargainingRound, ref reportResponseToOffer);
            string suboptionSetName =
                $"Round {bargainingRound} {(reportResponseToOffer ? "ResponseTo" : "")}{(plaintiffMakesOffer ? "P" : "D")} {offerNumber}";
            string allString = suboptionSetName + "-All";
            List<SimpleReportFilter> metaFilters = new List<SimpleReportFilter>
            {
                new SimpleReportFilter("RoundOccurs",
                    (GameProgress gp) => MyGP(gp).BargainingRoundsComplete >= bargainingRound)
            };
            List<SimpleReportFilter> rowFilters = new List<SimpleReportFilter>()
            {
                new SimpleReportFilter(allString, (GameProgress gp) => true)
            };
            bool reportPlaintiff = (plaintiffMakesOffer && !reportResponseToOffer) || (!plaintiffMakesOffer && reportResponseToOffer);
            Func<GameProgress, bool> additionalCriterion = null;
            if (Options.BargainingRoundsSimultaneous || !reportResponseToOffer)
                additionalCriterion = gp => MyGP(gp).BothAgreeToBargainInRound(bargainingRound);
            AddRowFilterSignalRegions(reportPlaintiff, rowFilters, additionalCriterion);
            //AddRowFilterLiabilitySignalRegions(false, rowFilters);
            List<SimpleReportColumnItem> columnItems = new List<SimpleReportColumnItem>()
            {
                new SimpleReportColumnFilter(allString, (GameProgress gp) => true, SimpleReportColumnFilterOptions.ProportionOfAll)
            };
            //AddColumnFiltersLiabilityStrength(columnItems); // not needed -- in court success report
            double[] offerPoints = EquallySpaced.GetEquallySpacedPoints(Options.NumOffers, Options.IncludeEndpointsForOffers);
            Tuple<double, double>[] offerRegions = offerPoints.Select(x => new Tuple<double, double>(x - 0.001, x + 0.001)).ToArray();
            for (int i = 0; i < Options.NumOffers; i++)
            {
                //double regionStart = offerRegions[i].Item1;
                //double regionEnd = offerRegions[i].Item2;
                //columnItems.Add(new SimpleReportColumnFilter(
                //        $"{(reportResponseToOffer ? "To" : "")}{(plaintiffMakesOffer ? "P" : "D")}{offerNumber} {regionStart.ToSignificantFigures(2)}-{regionEnd.ToSignificantFigures(2)}{(reportResponseToOffer ? "" : "  ")}",
                //        GetOfferOrResponseFilter(plaintiffMakesOffer, offerNumber, reportResponseToOffer,
                //            offerRegions[i]),
                //        false
                //    )
                //);
                columnItems.Add(new SimpleReportColumnFilter(
                        $"{(reportResponseToOffer ? "To" : "")}{(plaintiffMakesOffer ? "P" : "D")}{offerNumber} {offerPoints[i].ToSignificantFigures(2)}{(reportResponseToOffer ? "" : "  ")}",
                        GetOfferOrResponseFilter(plaintiffMakesOffer, offerNumber, reportResponseToOffer,
                            offerRegions[i]),
                        SimpleReportColumnFilterOptions.ProportionOfRow
                    )
                );
            }
            bool includeSuboptionSetNameInRow = true;
            string rowNamePrefix = includeSuboptionSetNameInRow ? suboptionSetName + " " : "";
            if (includeSuboptionSetNameInRow)
                foreach (var rowFilter in rowFilters)
                    if (rowFilter.Name != allString)
                        rowFilter.Name = rowNamePrefix + rowFilter.Name;
            return new SimpleReportDefinition(
                suboptionSetName,
                metaFilters,
                rowFilters,
                columnItems,
                reportResponseToOffer
            );
        }


        private void AddRowFiltersLitigationStrength(List<SimpleReportFilter> rowFilters)
        {
            for (int i = 1; i <= Options.NumLiabilityStrengthPoints; i++)
            {
                byte i2 = (byte)(i); // necessary to prevent access to modified closure
                if (Options.NumDamagesStrengthPoints <= 1)
                    rowFilters.Add(new SimpleReportFilter(
                        $"LiabQual {i2}",
                        (GameProgress gp) => MyGP(gp).LiabilityStrengthDiscrete == i2));
                else
                {
                    rowFilters.Add(new SimpleReportFilter(
                           $"LiabStr {i2}",
                           (GameProgress gp) => MyGP(gp).LiabilityStrengthDiscrete == i2));
                    for (int j = 1; j <= Options.NumDamagesStrengthPoints; j++)
                    {
                        byte j2 = (byte)j;
                        rowFilters.Add(new SimpleReportFilter(
                            $"LiabStr {i2} DamStr {j2}",
                            (GameProgress gp) => MyGP(gp).LiabilityStrengthDiscrete == i2 && MyGP(gp).DamagesStrengthDiscrete == j2));
                    }
                }
            }
            //for (int i = 1; i <= Options.NumNoiseValues; i++)
            //{
            //    byte j = (byte)(i); // necessary to prevent access to modified closure
            //    rowFilters.Add(new SimpleReportFilter(
            //        $"LitQual 1 Noise {j}",
            //        (GameProgress gp) => MyGP(gp).LiabilityStrengthDiscrete == 1 && MyGP(gp).PNoiseDiscrete == j));
            //}
        }
        private void AddColumnFiltersTrulyLiable(List<SimpleReportColumnItem> columnFilters)
        {
            columnFilters.Add(new SimpleReportColumnFilter(
                       $"NotTrulyLiable",
                       (GameProgress gp) => !MyGP(gp).IsTrulyLiable,
                       SimpleReportColumnFilterOptions.ProportionOfRow));
            columnFilters.Add(new SimpleReportColumnFilter(
                $"NotTrulyLiablePct",
                (GameProgress gp) => !MyGP(gp).IsTrulyLiable,
                SimpleReportColumnFilterOptions.ProportionOfFirstRowOfColumn));
            columnFilters.Add(new SimpleReportColumnFilter(
                    $"TrulyLiable",
                    (GameProgress gp) => MyGP(gp).IsTrulyLiable,
                    SimpleReportColumnFilterOptions.ProportionOfRow));
            columnFilters.Add(new SimpleReportColumnFilter(
                $"TrulyLiablePct",
                (GameProgress gp) => MyGP(gp).IsTrulyLiable,
                SimpleReportColumnFilterOptions.ProportionOfFirstRowOfColumn));
        }
        private void AddColumnFiltersLiabilityStrength(List<SimpleReportColumnItem> columnFilters)
        {
            for (int i = 1; i <= Options.NumLiabilityStrengthPoints; i++)
            {
                byte j = (byte)(i); // necessary to prevent access to modified closure
                columnFilters.Add(new SimpleReportColumnFilter(
                    $"LiabStr {j}",
                    (GameProgress gp) => MyGP(gp).LiabilityStrengthDiscrete == j,
                    SimpleReportColumnFilterOptions.ProportionOfRow));
                columnFilters.Add(new SimpleReportColumnFilter(
                    $"LiabStrPct {j}",
                    (GameProgress gp) => MyGP(gp).LiabilityStrengthDiscrete == j,
                    SimpleReportColumnFilterOptions.ProportionOfFirstRowOfColumn));
            }
        }
        private void AddColumnFiltersDamagesStrength(List<SimpleReportColumnItem> columnFilters)
        {
            if (Options.NumDamagesStrengthPoints <= 1)
                return;
            for (int i = 1; i <= Options.NumDamagesStrengthPoints; i++)
            {
                byte j = (byte)(i); // necessary to prevent access to modified closure
                columnFilters.Add(new SimpleReportColumnFilter(
                    $"DamStr {j}",
                    (GameProgress gp) => MyGP(gp).DamagesStrengthDiscrete == j,
                    SimpleReportColumnFilterOptions.ProportionOfRow));
            }
        }

        private void AddColumnFiltersPLiabilitySignal(List<SimpleReportColumnItem> columnFilters)
        {
            for (int i = 1; i <= Options.NumLiabilitySignals; i++)
            {
                byte j = (byte)(i); // necessary to prevent access to modified closure
                columnFilters.Add(new SimpleReportColumnFilter(
                    $"PLiabilitySignal {j}",
                    (GameProgress gp) => MyGP(gp).PLiabilitySignalDiscrete == j,
                    SimpleReportColumnFilterOptions.ProportionOfRow));
            }
        }

        private void AddColumnFiltersDLiabilitySignal(List<SimpleReportColumnItem> columnFilters)
        {
            for (int i = 1; i <= Options.NumLiabilitySignals; i++)
            {
                byte j = (byte)(i); // necessary to prevent access to modified closure
                columnFilters.Add(new SimpleReportColumnFilter(
                    $"DLiabilitySignal {j}",
                    (GameProgress gp) => MyGP(gp).DLiabilitySignalDiscrete == j,
                    SimpleReportColumnFilterOptions.ProportionOfRow));
            }
        }

        private void AddRowFilterSignalRegions(bool plaintiffSignal, List<SimpleReportFilter> rowFilters, Func<GameProgress, bool> additionalCriterion = null)
        {
            Tuple<double, double>[] signalRegions = EquallySpaced.GetRegions(Options.NumLiabilitySignals);
            for (int i = 1; i <= Options.NumLiabilitySignals; i++)
            {
                double regionStart = signalRegions[i - 1].Item1;
                double regionEnd = signalRegions[i - 1].Item2;
                if (Options.NumDamagesSignals <= 1)
                {
                    byte i2 = (byte)(i);
                    if (plaintiffSignal)
                    {
                        rowFilters.Add(new SimpleReportFilter(
                             $"PLiabilitySignal {i2}",
                            (GameProgress gp) => MyGP(gp).PLiabilitySignalDiscrete == i2 && (additionalCriterion == null || additionalCriterion(gp))));
                    }
                    else
                    {
                        rowFilters.Add(new SimpleReportFilter(
                            $"DLiabilitySignal {i2}",
                            (GameProgress gp) => MyGP(gp).DLiabilitySignalDiscrete == i2 && (additionalCriterion == null || additionalCriterion(gp))));
                    }
                }
                else
                    for (int j = 1; j <= Options.NumDamagesSignals; j++)
                    {
                        byte i2 = (byte)(i);
                        byte j2 = (byte)(j);
                        if (plaintiffSignal)
                        {
                            rowFilters.Add(new SimpleReportFilter(
                                $"PSignal Liab {i2} Dam {j2}",
                                (GameProgress gp) => MyGP(gp).PLiabilitySignalDiscrete == i2 && MyGP(gp).PDamagesSignalDiscrete == j2 && (additionalCriterion == null || additionalCriterion(gp))));
                        }
                        else
                        {
                            rowFilters.Add(new SimpleReportFilter(
                                $"DSignal Liab {i2} Dam {j2}",
                                (GameProgress gp) => MyGP(gp).DLiabilitySignalDiscrete == i2 && MyGP(gp).DDamagesSignalDiscrete == j2 && (additionalCriterion == null || additionalCriterion(gp))));
                        }
                    }
            }
        }

        private Func<GameProgress, bool?> GetOfferOrResponseFilter(bool plaintiffMakesOffer, int offerNumber,
            bool reportResponseToOffer, Tuple<double, double> offerRange)
        {
            if (reportResponseToOffer)
                return (GameProgress gp) => IsInOfferRange(MyGP(gp).GetOffer(plaintiffMakesOffer, offerNumber), offerRange) && MyGP(gp).GetResponse(!plaintiffMakesOffer, offerNumber);
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