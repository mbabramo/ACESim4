using System;
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
            return new List<SimpleReportDefinition>()
            {
                new SimpleReportDefinition(
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
                    )
            };
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
            return new List<Decision>()
                {
                    new Decision("LitigationQuality", "Qual", (byte) MyGamePlayers.Chance, new List<byte> { }, NumLitigationQualityPoints, (byte) MyGameDecisions.LitigationQuality),
                    new Decision("PlaintiffSignal", "PSig", (byte) MyGamePlayers.Chance, new List<byte> { }, NumPlaintiffSignals, (byte) MyGameDecisions.PSignal),
                    new Decision("DefendantSignal", "DSig", (byte) MyGamePlayers.Chance, new List<byte> { }, NumDefendantSignals, (byte) MyGameDecisions.DSignal),
                    // initially the offers will be kept secret (TODO: Make it so that the decisions are revealed AFTER defendant makes its offer if the case is not resolved)
                    new Decision("PlaintiffOffer", "PO", (byte) MyGamePlayers.Plaintiff, new List<byte> { (byte) MyGamePlayers.Plaintiff }, NumPlaintiffOffers, (byte) MyGameDecisions.POffer),
                    new Decision("DefendantOffer", "DO", (byte) MyGamePlayers.Defendant, new List<byte> { (byte) MyGamePlayers.Defendant }, NumDefendantOffers, (byte) MyGameDecisions.DOffer),
                    new Decision("CourtDecision", "CD", (byte) MyGamePlayers.Chance, new List<byte> {  }, 2, (byte) MyGameDecisions.CourtDecision, unevenChanceActions: true),
                };
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

        public override double[] GetChanceActionProbabilities(byte decisionNum, GameProgress gameProgress)
        {
            if (decisionNum == (byte)MyGameDecisions.CourtDecision)
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
