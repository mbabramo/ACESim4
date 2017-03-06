﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.ComponentModel.Composition;
using ACESim.Util;

namespace ACESim
{
    [Export(typeof(ICodeBasedSettingGenerator))] // include the export and exportmetadata attributes if we implement GenerateSetting, setting up the GameModule by code instead of in the settings file.
    [ExportMetadata("CodeGeneratorName", "MyGameDefinition")]
    public class MyGameDefinition : GameDefinition, ICodeBasedSettingGenerator, ICodeBasedSettingGeneratorName
    {
        public byte NumLitigationQualityPoints;
        /// <summary>
        /// The number of discrete signals that a party can receive. For example, 10 signals would allow each party to differentiate 10 different levels of case strength.
        /// </summary>
        public byte NumSignals;
        /// <summary>
        /// The number of discrete offers a party can make at any given time. For example, 10 signals might allow offers of 0.05, 0.15, ..., 0.95, but delta offers may allow offers to get gradually more precise.
        /// </summary>
        public byte NumOffers;
        /// <summary>
        /// If true, then the second offer by a party is interpreted as a value relative to the first offer.
        /// </summary>
        public bool SubsequentOffersAreDeltas;
        /// <summary>
        /// When subsequent offers are deltas, this represents the minimum (non-zero) delta. The party making the offer can make an offer +/- this amount.
        /// </summary>
        public double DeltaStartingValue;
        /// <summary>
        /// When subsequent offers are deltas, this represents the maximum delta. The intermediate deltas will be determined relative to this. 
        /// </summary>
        public double MaxDelta;
        /// <summary>
        /// The standard deviation of the noise used to obfuscate the plaintiff's estimate of the case strength.
        /// </summary>
        public double PNoiseStdev;
        /// <summary>
        /// The standard deviation of the noise used to obfuscate the defendant's estimate of the case strength.
        /// </summary>
        public double DNoiseStdev;
        /// <summary>
        /// Costs that the plaintiff must pay if the case goes to trial.
        /// </summary>
        public double PTrialCosts;
        /// <summary>
        /// Costs that the defendant must pay if the case goes to trial.
        /// </summary>
        public double DTrialCosts;
        /// <summary>
        /// Costs that each party must pay per round of bargaining. Note that an immediate successful resolution will still produce costs.
        /// </summary>
        public double PerPartyBargainingRoundCosts;
        public int NumBargainingRounds;
        public List<bool> BargainingRoundsSimultaneous;
        public List<bool> BargainingRoundsPGoesFirstIfNotSimultaneous; // if not simultaneous
        public bool IncludeSignalsReport;
        public DiscreteValueSignalParameters PSignalParameters, DSignalParameters;
        public DeltaOffersCalculation DeltaOffersCalculation;

        public MyGameDefinition() : base()
        {

        }

        public string CodeGeneratorName => "MyGameDefinition";

        public object GenerateSetting(string options)
        {
            ParseOptions(options);

            Players = GetPlayersList();
            DecisionsExecutionOrder = GetDecisionsList();
            SimpleReportDefinitions = GetReports();

            if (SubsequentOffersAreDeltas)
                DeltaOffersCalculation = new DeltaOffersCalculation(this);

            return this;
        }

        MyGameProgress MyGP(GameProgress gp) => gp as MyGameProgress;

        private static List<PlayerInfo> GetPlayersList()
        {
            return new List<PlayerInfo>
                {
                    new PlayerInfo("C", (int) MyGamePlayers.Chance, byte.MaxValue, true, false),
                    new PlayerInfo("P", (int) MyGamePlayers.Plaintiff, 0, false, true),
                    new PlayerInfo("D", (int) MyGamePlayers.Defendant, 1, false, true),
                };
        }

        private List<Decision> GetDecisionsList()
        {
            var decisions = new List<Decision>();
            List<byte> playersKnowingLitigationQuality = new List<byte>();
            if (PNoiseStdev == 0)
                playersKnowingLitigationQuality.Add((byte)MyGamePlayers.Plaintiff);
            if (DNoiseStdev == 0)
                playersKnowingLitigationQuality.Add((byte)MyGamePlayers.Defendant);
            decisions.Add(new Decision("LitigationQuality", "Qual", (byte)MyGamePlayers.Chance, playersKnowingLitigationQuality, NumLitigationQualityPoints, (byte)MyGameDecisions.LitigationQuality));
            if (PNoiseStdev != 0)
                decisions.Add(new Decision("PlaintiffSignal", "PSig", (byte)MyGamePlayers.Chance, new List<byte> { }, NumSignals, (byte)MyGameDecisions.PSignal));
            if (DNoiseStdev != 0)
                decisions.Add(new Decision("DefendantSignal", "DSig", (byte)MyGamePlayers.Chance, new List<byte> { }, NumSignals, (byte)MyGameDecisions.DSignal));
            for (int b = 0; b < NumBargainingRounds; b++)
            {
                if (BargainingRoundsSimultaneous[b])
                { // samuelson-chaterjee bargaining
                    decisions.Add(new Decision("PlaintiffOffer" + (b + 1), "PO" + (b + 1), (byte)MyGamePlayers.Plaintiff, new List<byte> { (byte)MyGamePlayers.Plaintiff }, NumOffers, (byte)MyGameDecisions.POffer) );
                    decisions.Add(new Decision("DefendantOffer" + (b + 1), "DO" + (b + 1), (byte)MyGamePlayers.Defendant, new List<byte> { (byte)MyGamePlayers.Defendant }, NumOffers, (byte)MyGameDecisions.DOffer));
                }
                else if (BargainingRoundsPGoesFirstIfNotSimultaneous[b])
                {
                    decisions.Add(new Decision("PlaintiffOffer" + (b + 1), "PO" + (b + 1), (byte)MyGamePlayers.Plaintiff, new List<byte> { (byte)MyGamePlayers.Plaintiff, (byte)MyGamePlayers.Defendant }, NumOffers, (byte)MyGameDecisions.POffer)); // { AlwaysDoAction = 4 /* DEBUG */});
                    decisions.Add(new Decision("DefendantResponse" + (b + 1), "DR" + (b + 1), (byte)MyGamePlayers.Defendant, new List<byte> { (byte)MyGamePlayers.Defendant }, 2, (byte)MyGameDecisions.DResponse)); // Note: It might appear that it would not be necessary to add this to the information set tree. After all, if the response is to accept the offer ,the game ends. but we need to be able to distinguish the situation in which the defendant says "no" and then faces another decision before the plaintiff makes a move. This would occur if the first bargaining round is a plaintiff offer and the second bargaining round is a defendant offer. 
                }
                else
                {
                    decisions.Add(new Decision("DefendantOffer" + (b + 1), "DO" + (b + 1), (byte)MyGamePlayers.Defendant, new List<byte> { (byte)MyGamePlayers.Defendant, (byte)MyGamePlayers.Plaintiff }, NumOffers, (byte)MyGameDecisions.DOffer));
                    decisions.Add(new Decision("PlaintiffResponse" + (b + 1), "PR" + (b + 1), (byte)MyGamePlayers.Plaintiff, new List<byte> { (byte)MyGamePlayers.Plaintiff }, 2, (byte)MyGameDecisions.PResponse));
                }
            }
            decisions.Add(new Decision("CourtDecision", "CD", (byte)MyGamePlayers.Chance, new List<byte> { }, 2, (byte)MyGameDecisions.CourtDecision, unevenChanceActions: true));
            return decisions;
        }

        private List<SimpleReportDefinition> GetReports()
        {
            var reports = new List<SimpleReportDefinition>();
            reports.Add(GetOverallReport());
            if (IncludeSignalsReport)
            {
                reports.Add(GetStrategyReport(true, false, false));
                reports.Add(GetStrategyReport(true, false, true));
                reports.Add(GetStrategyReport(false, true, false));
                reports.Add(GetStrategyReport(false, true, true));
            }
            return reports;
        }

        private SimpleReportDefinition GetOverallReport()
        {
            return new SimpleReportDefinition(
                "MyGameReport",
                null,
                new List<SimpleReportFilter>()
                {
                    new SimpleReportFilter("All", (GameProgress gp) => true),
                    new SimpleReportFilter("Settles", (GameProgress gp) => MyGP(gp).CaseSettles),
                    new SimpleReportFilter("Tried", (GameProgress gp) => !MyGP(gp).CaseSettles),
                    new SimpleReportFilter("LowQuality", (GameProgress gp) => MyGP(gp).LitigationQuality <= 0.25),
                    new SimpleReportFilter("MediumQuality", (GameProgress gp) => MyGP(gp).LitigationQuality > 0.25 && MyGP(gp).LitigationQuality < 0.75),
                    new SimpleReportFilter("HighQuality", (GameProgress gp) => MyGP(gp).LitigationQuality >= 0.75),
                    new SimpleReportFilter("LowPSignal", (GameProgress gp) => MyGP(gp).PSignalUniform <= 0.25),
                    new SimpleReportFilter("LowDSignal", (GameProgress gp) => MyGP(gp).DSignalUniform <= 0.25),
                    new SimpleReportFilter("MedPSignal", (GameProgress gp) => MyGP(gp).PSignalUniform > 0.25 && MyGP(gp).PSignalUniform < 0.75),
                    new SimpleReportFilter("MedDSignal", (GameProgress gp) => MyGP(gp).DSignalUniform > 0.25 && MyGP(gp).DSignalUniform < 0.75),
                    new SimpleReportFilter("HiPSignal", (GameProgress gp) => MyGP(gp).PSignalUniform >= 0.75),
                    new SimpleReportFilter("HiDSignal", (GameProgress gp) => MyGP(gp).DSignalUniform >= 0.75),
                },
                new List<SimpleReportColumnItem>()
                {
                    new SimpleReportColumnFilter("All", (GameProgress gp) => true, true),
                    new SimpleReportColumnVariable("LitigQuality", (GameProgress gp) => MyGP(gp).LitigationQuality),
                    new SimpleReportColumnVariable("PFirstOffer", (GameProgress gp) => MyGP(gp).PFirstOffer),
                    new SimpleReportColumnVariable("DFirstOffer", (GameProgress gp) => MyGP(gp).DFirstOffer),
                    new SimpleReportColumnVariable("PLastOffer", (GameProgress gp) => MyGP(gp).PLastOffer),
                    new SimpleReportColumnVariable("DLastOffer", (GameProgress gp) => MyGP(gp).DLastOffer),
                    new SimpleReportColumnFilter("Settles", (GameProgress gp) => MyGP(gp).SettlementValue != null, false),
                    new SimpleReportColumnVariable("ValIfSettled", (GameProgress gp) => MyGP(gp).SettlementValue),
                    new SimpleReportColumnVariable("PWelfare", (GameProgress gp) => MyGP(gp).PWelfare),
                    new SimpleReportColumnVariable("DWelfare", (GameProgress gp) => MyGP(gp).DWelfare),
                }
                );
        }

        private SimpleReportDefinition GetStrategyReport(bool plaintiffMakesOffer, bool lastRound, bool reportResponseToOffer)
        {
            string reportName = $"Strategies {(reportResponseToOffer ? "ResponseTo" : "")}{(plaintiffMakesOffer ? "P" : "D")} {(lastRound ? "Last" : "First")}";
            List<SimpleReportFilter> metaFilters = new List<SimpleReportFilter>();
            if (plaintiffMakesOffer)
                metaFilters.Add(new SimpleReportFilter("PMakesOffer", (GameProgress gp) => MyGP(gp).PLastOffer != null));
            else
                metaFilters.Add(new SimpleReportFilter("DMakesOffer", (GameProgress gp) => MyGP(gp).DLastOffer != null));
            List<SimpleReportFilter> rowFilters = new List<SimpleReportFilter>()
            {
                new SimpleReportFilter("All", (GameProgress gp) => true)
            };
            Tuple<double, double>[] regions = EquallySpaced.GetRegions(NumSignals);
            for (int i = 0; i < NumSignals; i++)
            {
                double regionStart = regions[i].Item1;
                double regionEnd = regions[i].Item2;
                if (plaintiffMakesOffer)
                    rowFilters.Add(new SimpleReportFilter($"PSignal {regionStart.ToSignificantFigures(2)}-{regionEnd.ToSignificantFigures(2)}", (GameProgress gp) => MyGP(gp).PSignalUniform >= regionStart && MyGP(gp).PSignalUniform < regionEnd));
                else
                    rowFilters.Add(new SimpleReportFilter($"DSignal {regionStart.ToSignificantFigures(2)}-{regionEnd.ToSignificantFigures(2)}", (GameProgress gp) => MyGP(gp).DSignalUniform >= regionStart && MyGP(gp).DSignalUniform < regionEnd));
            }
            List<SimpleReportColumnItem> columnItems = new List<SimpleReportColumnItem>()
            {
                new SimpleReportColumnFilter("All", (GameProgress gp) => true, true)
            };
            for (int i = 0; i < NumOffers; i++)
            {
                double offerValue = EquallySpaced.GetLocationOfEquallySpacedPoint(i, NumOffers);
                columnItems.Add(new SimpleReportColumnFilter(
                    $"{(reportResponseToOffer ? "ResponseTo" : "")}{(plaintiffMakesOffer ? "P" : "D")}{(lastRound ? "LastOffer" : "FirstOffer")} {offerValue.ToSignificantFigures(2)}", 
                    GetOfferOrResponseFilter(plaintiffMakesOffer, lastRound, reportResponseToOffer, offerValue), 
                    false
                    )
                    );
            }
            return new SimpleReportDefinition(
                                reportName,
                                metaFilters,
                                rowFilters,
                                columnItems
                                );
        }

        private Func<GameProgress, bool> GetOfferOrResponseFilter(bool plaintiffMakesOffer, bool lastRound, bool reportResponseToOffer, double offerValue)
        {
            if (reportResponseToOffer)
            {
                if (lastRound)
                {
                    if (plaintiffMakesOffer)
                        return (GameProgress gp) => MyGP(gp).PLastOffer == offerValue && MyGP(gp).DLastResponse == true;
                    else
                        return (GameProgress gp) => MyGP(gp).DLastOffer == offerValue && MyGP(gp).PLastResponse == true;
                }
                else
                {
                    if (plaintiffMakesOffer)
                        return (GameProgress gp) => MyGP(gp).PFirstOffer == offerValue && MyGP(gp).DFirstResponse == true;
                    else
                        return (GameProgress gp) => MyGP(gp).DFirstOffer == offerValue && MyGP(gp).PFirstResponse == true;
                }
            }
            else
            {
                if (lastRound)
                {
                    if (plaintiffMakesOffer)
                        return (GameProgress gp) => MyGP(gp).PLastOffer == offerValue;
                    else
                        return (GameProgress gp) => MyGP(gp).DLastOffer == offerValue;
                }
                else
                {
                    if (plaintiffMakesOffer)
                        return (GameProgress gp) => MyGP(gp).PFirstOffer == offerValue;
                    else
                        return (GameProgress gp) => MyGP(gp).DFirstOffer == offerValue;
                }
            }
        }

        private void ParseOptions(string options)
        {
            NumLitigationQualityPoints = GameModule.GetByteCodeGeneratorOption(options, "NumLitigationQualityPoints");
            NumSignals = GameModule.GetByteCodeGeneratorOption(options, "NumSignals");
            NumOffers = GameModule.GetByteCodeGeneratorOption(options, "NumOffers");
            SubsequentOffersAreDeltas = GameModule.GetBoolCodeGeneratorOption(options, "SubsequentOffersAreDeltas");
            DeltaStartingValue = GameModule.GetByteCodeGeneratorOption(options, "DeltaStartingValue");
            MaxDelta = GameModule.GetByteCodeGeneratorOption(options, "MaxDelta");
            PNoiseStdev = GameModule.GetDoubleCodeGeneratorOption(options, "PNoiseStdev");
            DNoiseStdev = GameModule.GetDoubleCodeGeneratorOption(options, "DNoiseStdev");
            PTrialCosts = GameModule.GetDoubleCodeGeneratorOption(options, "PTrialCosts");
            DTrialCosts = GameModule.GetDoubleCodeGeneratorOption(options, "DTrialCosts");
            PerPartyBargainingRoundCosts = GameModule.GetDoubleCodeGeneratorOption(options, "PerPartyBargainingRoundCosts"); 
             IncludeSignalsReport = GameModule.GetBoolCodeGeneratorOption(options, "IncludeSignalsReport");
            NumBargainingRounds = GameModule.GetIntCodeGeneratorOption(options, "NumBargainingRounds");
            BargainingRoundsSimultaneous = new List<bool>()
            {
                GameModule.GetBoolCodeGeneratorOption(options, "BargainingRound1Simultaneous"),
                GameModule.GetBoolCodeGeneratorOption(options, "BargainingRound2Simultaneous"),
                GameModule.GetBoolCodeGeneratorOption(options, "BargainingRound3Simultaneous"),
                GameModule.GetBoolCodeGeneratorOption(options, "BargainingRound4Simultaneous"),
                GameModule.GetBoolCodeGeneratorOption(options, "BargainingRound5Simultaneous"),
            };
            BargainingRoundsPGoesFirstIfNotSimultaneous = new List<bool>()
            {
                GameModule.GetBoolCodeGeneratorOption(options, "BargainingRound1PGoesFirstIfNotSimultaneous"),
                GameModule.GetBoolCodeGeneratorOption(options, "BargainingRound2PGoesFirstIfNotSimultaneous"),
                GameModule.GetBoolCodeGeneratorOption(options, "BargainingRound3PGoesFirstIfNotSimultaneous"),
                GameModule.GetBoolCodeGeneratorOption(options, "BargainingRound4PGoesFirstIfNotSimultaneous"),
                GameModule.GetBoolCodeGeneratorOption(options, "BargainingRound5PGoesFirstIfNotSimultaneous"),
            };
            PSignalParameters = new DiscreteValueSignalParameters()
            {
                NumPointsInSourceUniformDistribution = NumLitigationQualityPoints,
                StdevOfNormalDistribution = PNoiseStdev,
                NumSignals = NumSignals
            };
            DSignalParameters = new DiscreteValueSignalParameters()
            {
                NumPointsInSourceUniformDistribution = NumLitigationQualityPoints,
                StdevOfNormalDistribution = DNoiseStdev,
                NumSignals = NumSignals
            };
        }

        public override double[] GetChanceActionProbabilities(byte decisionByteCode, GameProgress gameProgress)
        {
            if (decisionByteCode == (byte)MyGameDecisions.CourtDecision)
            {
                double[] probabilities = new double[2];
                MyGameProgress myGameProgress = (MyGameProgress)gameProgress;
                probabilities[0] = 1.0 - myGameProgress.LitigationQuality; // probability action 1 ==> rule for defendant
                probabilities[1] = myGameProgress.LitigationQuality; // probability action 2 ==> rule for plaintiff
                return probabilities;
            }
            else
                throw new NotImplementedException(); // subclass should define if needed
        }

    }
}
