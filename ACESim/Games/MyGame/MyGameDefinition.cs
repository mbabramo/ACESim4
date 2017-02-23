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
        public byte NumCourtSignals;
        public byte NumPlaintiffOffers;
        public byte NumDefendantOffers;
        public double PNoiseStdev;
        public double DNoiseStdev;
        public double CourtNoiseStdev;
        public double PLitigationCosts;
        public double DLitigationCosts;
        public DiscreteValueSignalParameters PSignalParameters, DSignalParameters, CourtSignalParameters;

        public MyGameDefinition() : base()
        {

        }

        public string CodeGeneratorName => "MyGameDefinition";

        public object GenerateSetting(string options)
        {
            ParseOptions(options);

            Players = GetPlayersList();
            DecisionsExecutionOrder = GetDecisionsList();

            return this;
        }

        private static List<PlayerInfo> GetPlayersList()
        {
            return new List<PlayerInfo>
                {
                    new PlayerInfo("C",(int) MyGamePlayers.Chance,true,false),
                    new PlayerInfo("P",(int) MyGamePlayers.Plaintiff,false,true),
                    new PlayerInfo("D",(int) MyGamePlayers.Defendant,false,true),
                };
        }

        private List<Decision> GetDecisionsList()
        {
            return new List<Decision>()
                {
                    new Decision("LitigationQuality", "Qual", (byte) MyGamePlayers.Chance, new List<byte> { }, NumLitigationQualityPoints, (byte) MyGameDecisions.LitigationQuality),
                    new Decision("PlaintiffSignal", "PSig", (byte) MyGamePlayers.Chance, new List<byte> { }, NumPlaintiffSignals, (byte) MyGameDecisions.PSignal),
                    new Decision("DefendantSignal", "DSig", (byte) MyGamePlayers.Chance, new List<byte> { }, NumDefendantSignals, (byte) MyGameDecisions.DSignal),
                    new Decision("PlaintiffOffer", "PO", (byte) MyGamePlayers.Plaintiff, new List<byte> { (byte) MyGamePlayers.Plaintiff, (byte) MyGamePlayers.Defendant }, NumPlaintiffOffers, (byte) MyGameDecisions.POffer),
                    new Decision("DefendantOffer", "DO", (byte) MyGamePlayers.Defendant, new List<byte> { (byte) MyGamePlayers.Plaintiff, (byte) MyGamePlayers.Defendant }, NumDefendantOffers, (byte) MyGameDecisions.DOffer),
                    new Decision("CourtDecision", "CD", (byte) MyGamePlayers.Chance, new List<byte> {  }, NumCourtSignals, (byte) MyGameDecisions.CourtDecision),
                };
        }

        private void ParseOptions(string options)
        {
            NumLitigationQualityPoints = GameModule.GetByteCodeGeneratorOption(options, "NumLitigationQualityPoints");
            NumPlaintiffSignals = GameModule.GetByteCodeGeneratorOption(options, "NumPlaintiffSignals");
            NumDefendantSignals = GameModule.GetByteCodeGeneratorOption(options, "NumDefendantSignals");
            NumCourtSignals = GameModule.GetByteCodeGeneratorOption(options, "NumCourtSignals");
            NumPlaintiffOffers = GameModule.GetByteCodeGeneratorOption(options, "NumPlaintiffOffers");
            NumDefendantOffers = GameModule.GetByteCodeGeneratorOption(options, "NumDefendantOffers");
            PNoiseStdev = GameModule.GetDoubleCodeGeneratorOption(options, "PNoiseStdev");
            DNoiseStdev = GameModule.GetDoubleCodeGeneratorOption(options, "DNoiseStdev");
            CourtNoiseStdev = GameModule.GetDoubleCodeGeneratorOption(options, "CourtNoiseStdev");
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
            CourtSignalParameters = new DiscreteValueSignalParameters()
            {
                NumPointsInSourceUniformDistribution = NumLitigationQualityPoints,
                StdevOfNormalDistribution = CourtNoiseStdev,
                NumSignals = NumCourtSignals
            };
        }

    }
}
