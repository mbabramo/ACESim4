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
        public byte NumPlaintiffSignals;
        public byte NumDefendantSignals;
        public byte NumPlaintiffOffers;
        public byte NumDefendantOffers;
        public double PNoiseStdev;
        public double DNoiseStdev;
        public double PLitigationCosts;
        public double DLitigationCosts;
        public int NumBargainingRounds;
        public List<bool> BargainingRoundsSimultaneous;
        public List<bool> BargainingRoundsPGoesFirstIfNotSimultaneous; // if not simultaneous
        public bool IncludeSignalsReport;
        public DiscreteValueSignalParameters PSignalParameters, DSignalParameters;

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

            return this;
        }

        MyGameProgress MyGP(GameProgress gp) => gp as MyGameProgress;

        private List<SimpleReportDefinition> GetReports()
        {
            var reports = new List<SimpleReportDefinition>();
            reports.Add(GetOverallReport());
            if (IncludeSignalsReport)
                reports.Add(GetStrategyReport());
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
                        new SimpleReportColumnVariable("POffer", (GameProgress gp) => MyGP(gp).POffer),
                        new SimpleReportColumnVariable("DOffer", (GameProgress gp) => MyGP(gp).DOffer),
                        new SimpleReportColumnFilter("Settles", (GameProgress gp) => MyGP(gp).SettlementValue != null, false),
                        new SimpleReportColumnVariable("ValIfSettled", (GameProgress gp) => MyGP(gp).SettlementValue),
                        new SimpleReportColumnVariable("PWelfare", (GameProgress gp) => MyGP(gp).PWelfare),
                        new SimpleReportColumnVariable("DWelfare", (GameProgress gp) => MyGP(gp).DWelfare),
                                }
                                );
        }

        private SimpleReportDefinition GetStrategyReport()
        {
            List<SimpleReportFilter> filters = new List<SimpleReportFilter>()
            {
                new SimpleReportFilter("All", (GameProgress gp) => true)
            };
            for (int i = 0; i < NumPlaintiffSignals; i++)
            {
                double signalValue = EquallySpaced.GetLocationOfEquallySpacedPoint(i, NumPlaintiffSignals);
                filters.Add(new SimpleReportFilter("PSignal " + signalValue, (GameProgress gp) => MyGP(gp).PSignalUniform == signalValue));
            }
            List<SimpleReportColumnItem> columnItems = new List<SimpleReportColumnItem>()
            {
                new SimpleReportColumnFilter("All", (GameProgress gp) => true, true)
            };
            for (int i = 0; i < NumPlaintiffOffers; i++)
            {
                double offerValue = EquallySpaced.GetLocationOfEquallySpacedPoint(i, NumPlaintiffOffers);
                columnItems.Add(new SimpleReportColumnFilter("POffer " + offerValue, (GameProgress gp) => MyGP(gp).POffer == offerValue, false));
            }
            return new SimpleReportDefinition(
                                "MyGameStrategyReport",
                                null,
                                filters,
                                columnItems
                                );
        }

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
                decisions.Add(new Decision("PlaintiffSignal", "PSig", (byte)MyGamePlayers.Chance, new List<byte> { }, NumPlaintiffSignals, (byte)MyGameDecisions.PSignal));
            if (DNoiseStdev != 0)
                decisions.Add(new Decision("DefendantSignal", "DSig", (byte)MyGamePlayers.Chance, new List<byte> { }, NumDefendantSignals, (byte)MyGameDecisions.DSignal));
            decisions.Add(new Decision("PlaintiffOffer", "PO", (byte)MyGamePlayers.Plaintiff, new List<byte> { (byte)MyGamePlayers.Plaintiff }, NumPlaintiffOffers, (byte)MyGameDecisions.POffer));
            decisions.Add(new Decision("DefendantOffer", "DO", (byte)MyGamePlayers.Defendant, new List<byte> { (byte)MyGamePlayers.Defendant }, NumDefendantOffers, (byte)MyGameDecisions.DOffer));
            decisions.Add(new Decision("CourtDecision", "CD", (byte)MyGamePlayers.Chance, new List<byte> { }, 2, (byte)MyGameDecisions.CourtDecision, unevenChanceActions: true));
            return decisions;
        }

        private void ParseOptions(string options)
        {
            NumLitigationQualityPoints = GameModule.GetByteCodeGeneratorOption(options, "NumLitigationQualityPoints");
            NumPlaintiffSignals = GameModule.GetByteCodeGeneratorOption(options, "NumPlaintiffSignals");
            NumDefendantSignals = GameModule.GetByteCodeGeneratorOption(options, "NumDefendantSignals");
            NumPlaintiffOffers = GameModule.GetByteCodeGeneratorOption(options, "NumPlaintiffOffers");
            NumDefendantOffers = GameModule.GetByteCodeGeneratorOption(options, "NumDefendantOffers");
            PNoiseStdev = GameModule.GetDoubleCodeGeneratorOption(options, "PNoiseStdev");
            DNoiseStdev = GameModule.GetDoubleCodeGeneratorOption(options, "DNoiseStdev");
            PLitigationCosts = GameModule.GetDoubleCodeGeneratorOption(options, "PLitigationCosts");
            DLitigationCosts = GameModule.GetDoubleCodeGeneratorOption(options, "DLitigationCosts");
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
                NumSignals = NumDefendantSignals
            };
            DSignalParameters = new DiscreteValueSignalParameters()
            {
                NumPointsInSourceUniformDistribution = NumLitigationQualityPoints,
                StdevOfNormalDistribution = DNoiseStdev,
                NumSignals = NumPlaintiffSignals
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
