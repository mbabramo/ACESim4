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
            NumPlayers = (byte) Players.Count();
            DecisionsExecutionOrder = GetDecisionsList();
            SimpleReportDefinitions = GetReports();

            if (SubsequentOffersAreDeltas)
                DeltaOffersCalculation = new DeltaOffersCalculation(this);

            return this;
        }

        MyGameProgress MyGP(GameProgress gp) => gp as MyGameProgress;

        static string PlaintiffName = "P";
        static string DefendantName = "D";
        static string LitigationQualityChanceName = "QC";
        static string SignalChanceName = "SC";
        static string CourtChanceName = "CC";
        static string ResolutionPlayerName = "R";

        private static List<PlayerInfo> GetPlayersList()
        {
            // IMPORTANT: Chance players MUST be listed after other players.
            return new List<PlayerInfo>
                {
                    new PlayerInfo(PlaintiffName, (int) MyGamePlayers.Plaintiff, false, true),
                    new PlayerInfo(DefendantName, (int) MyGamePlayers.Defendant, false, true),
                    new PlayerInfo(LitigationQualityChanceName, (int) MyGamePlayers.QualityChance, true, false),
                    new PlayerInfo(SignalChanceName, (int) MyGamePlayers.SignalChance, true, false),
                    new PlayerInfo(CourtChanceName, (int) MyGamePlayers.CourtChance, true, false),
                    new PlayerInfo(ResolutionPlayerName, (int) MyGamePlayers.Resolution, true, false),
                };
        }

        public override byte PlayerIndex_ResolutionPlayer => (byte) MyGamePlayers.Resolution;

        private List<Decision> GetDecisionsList()
        {
            bool addPlayersOwnDecisionsToInformationSet = true;
            // TODO: if above is FALSE, always add decision number to information set (to avoid possible confusion about which decision we're doing) -- although sometimes we might want something other than the decision number itself, if we are repeating a decision multiple times.
            var decisions = new List<Decision>();
            // Litigation Quality. This is not known by a player unless the player has perfect information. 
            // The SignalChance player relies on this information in calculating the probabilities of different signals
            List<byte> playersKnowingLitigationQuality = new List<byte>() { (byte) MyGamePlayers.SignalChance, (byte) MyGamePlayers.CourtChance };
            if (PNoiseStdev == 0)
                playersKnowingLitigationQuality.Add((byte)MyGamePlayers.Plaintiff);
            if (DNoiseStdev == 0)
                playersKnowingLitigationQuality.Add((byte)MyGamePlayers.Defendant);
            decisions.Add(new Decision("LitigationQuality", "Qual", (byte)MyGamePlayers.QualityChance, playersKnowingLitigationQuality, NumLitigationQualityPoints, (byte)MyGameDecisions.LitigationQuality));
            // Plaintiff and defendant signals. If a player has perfect information, then no signal is needed.
            if (PNoiseStdev != 0)
                decisions.Add(new Decision("PlaintiffSignal", "PSig", (byte)MyGamePlayers.SignalChance, new List<byte> { (byte)MyGamePlayers.Plaintiff }, NumSignals, (byte)MyGameDecisions.PSignal, unevenChanceActions: true));
            if (DNoiseStdev != 0)
                decisions.Add(new Decision("DefendantSignal", "DSig", (byte)MyGamePlayers.SignalChance, new List<byte> { (byte)MyGamePlayers.Defendant }, NumSignals, (byte)MyGameDecisions.DSignal, unevenChanceActions: true));
            for (int b = 0; b < NumBargainingRounds; b++)
            {
                List<byte> informationSetsToAddPlaintiffMoveTo = new List<byte>();
                List<byte> informationSetsToAddDefendantMoveTo = new List<byte>();
                if (addPlayersOwnDecisionsToInformationSet)
                {
                    informationSetsToAddPlaintiffMoveTo.Add((byte)MyGamePlayers.Plaintiff);
                    informationSetsToAddDefendantMoveTo.Add((byte)MyGamePlayers.Defendant);
                }
                if (BargainingRoundsSimultaneous[b])
                { // samuelson-chaterjee bargaining
                    // TODO: Make this an option
                    bool eachPlayerFindsOutAboutOthersOffer = true;
                    if (eachPlayerFindsOutAboutOthersOffer)
                    {
                        informationSetsToAddPlaintiffMoveTo.Add((byte)MyGamePlayers.Defendant);
                        informationSetsToAddDefendantMoveTo.Add((byte)MyGamePlayers.Plaintiff);
                    }
                    decisions.Add(new Decision("PlaintiffOffer" + (b + 1), "PO" + (b + 1), (byte)MyGamePlayers.Plaintiff, informationSetsToAddPlaintiffMoveTo, NumOffers, (byte)MyGameDecisions.POffer));
                    decisions.Add(new Decision("DefendantOffer" + (b + 1), "DO" + (b + 1), (byte)MyGamePlayers.Defendant, informationSetsToAddDefendantMoveTo, NumOffers, (byte)MyGameDecisions.DOffer));
                }
                else
                { // offer-response bargaining
                    // the response may be irrelevant but no harm adding it to information set
                    informationSetsToAddPlaintiffMoveTo.Add((byte)MyGamePlayers.Defendant);
                    informationSetsToAddDefendantMoveTo.Add((byte)MyGamePlayers.Plaintiff); 
                    if (BargainingRoundsPGoesFirstIfNotSimultaneous[b])
                    {
                        decisions.Add(new Decision("PlaintiffOffer" + (b + 1), "PO" + (b + 1), (byte)MyGamePlayers.Plaintiff, informationSetsToAddPlaintiffMoveTo, NumOffers, (byte)MyGameDecisions.POffer)); // { AlwaysDoAction = 4});
                        decisions.Add(new Decision("DefendantResponse" + (b + 1), "DR" + (b + 1), (byte)MyGamePlayers.Defendant, informationSetsToAddDefendantMoveTo, 2, (byte)MyGameDecisions.DResponse));
                    }
                    else
                    {
                        decisions.Add(new Decision("DefendantOffer" + (b + 1), "DO" + (b + 1), (byte)MyGamePlayers.Defendant, informationSetsToAddDefendantMoveTo, NumOffers, (byte)MyGameDecisions.DOffer));
                        decisions.Add(new Decision("PlaintiffResponse" + (b + 1), "PR" + (b + 1), (byte)MyGamePlayers.Plaintiff, informationSetsToAddPlaintiffMoveTo, 2, (byte)MyGameDecisions.PResponse));
                    }
                }
            }
            decisions.Add(new Decision("CourtDecision", "CD", (byte)MyGamePlayers.CourtChance, new List<byte> { (byte)MyGamePlayers.Resolution }, 2 /* for plaintiff or for defendant */, (byte)MyGameDecisions.CourtDecision, unevenChanceActions: true));
            return decisions;
        }

        public override void CustomInformationSetManipulation(Decision currentDecision, byte action, ref GameHistory gameHistory)
        {
            byte decisionByteCode = currentDecision.DecisionByteCode;
            // Resolution information set. We need an information set that uniquely identifies each distinct resolution. We have added the court decision 
            // to the resolution set above, but also need information about whether we have reached some kind of offer.
            // We only need to put the LAST offer and response in the information set. This could, of course, be the first offer and response if it is accepted. 
            // We will also need information on the nature of the offer and response, since different bargaining rounds may have different structures,
            // and since the number of bargaining rounds that has occurred may affect the parties' payoffs.
            // For example, a response of 2 may mean something different if we have simultaneous bargaining or if plaintiff or defendant is responding.
            // So, we would like our resolution information set to have the decision number of the last offer (which may be the first of two simultaneous offers)
            // and the actions of both players. Thus, if there is nothing in the resolution information set, then we add the decision byte code and the action.
            // If there are two items, then we add the decision byte code and the action. If there are three, we delete everything and then there are zero, so
            // we respond accordingly. 
            if (decisionByteCode >= (byte)MyGameDecisions.POffer && decisionByteCode <= (byte)MyGameDecisions.DResponse)
            {
                byte numItems = gameHistory.CountItemsInInformationSet((byte)MyGamePlayers.Resolution);
                if (numItems == 3)
                {
                    numItems = 0;
                    gameHistory.ReduceItemsInInformationSet((byte)MyGamePlayers.Resolution, decisionByteCode, numItems);
                }
                if (numItems == 0)
                    gameHistory.AddToInformationSet(decisionByteCode, decisionByteCode, (byte)MyGamePlayers.Resolution); // note that the decisionByteCode will be stored in the information set twice in a row. But that's because we will strip out the first of these when getting information back, and we need this information to differentiate resolution sets.
                gameHistory.AddToInformationSet(action, decisionByteCode, (byte)MyGamePlayers.Resolution);
            }
        }


        private List<SimpleReportDefinition> GetReports()
        {
            var reports = new List<SimpleReportDefinition>();
            reports.Add(GetOverallReport());
            if (IncludeSignalsReport)
            {
                for (int b = 1; b <= NumBargainingRounds; b++)
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
                    new SimpleReportColumnVariable("PFirstOffer", (GameProgress gp) => MyGP(gp).PFirstOffer),
                    new SimpleReportColumnVariable("DFirstOffer", (GameProgress gp) => MyGP(gp).DFirstOffer),
                    new SimpleReportColumnVariable("PLastOffer", (GameProgress gp) => MyGP(gp).PLastOffer),
                    new SimpleReportColumnVariable("DLastOffer", (GameProgress gp) => MyGP(gp).DLastOffer),
                    new SimpleReportColumnFilter("Settles", (GameProgress gp) => MyGP(gp).SettlementValue != null, false),
                    new SimpleReportColumnVariable("ValIfSettled", (GameProgress gp) => MyGP(gp).SettlementValue),
                    new SimpleReportColumnVariable("PWelfare", (GameProgress gp) => MyGP(gp).PWelfare),
                    new SimpleReportColumnVariable("DWelfare", (GameProgress gp) => MyGP(gp).DWelfare),
                };
            for (int b = 1; b <= NumBargainingRounds; b++)
            {
                int bargainingRoundNum = b; // needed for closure -- otherwise b below will always be max value.
                colItems.Add(
                    new SimpleReportColumnFilter($"Settles{b}", (GameProgress gp) => MyGP(gp).SettlementValue != null && MyGP(gp).BargainingRoundsComplete == bargainingRoundNum, false)
                    );
            }
            return new SimpleReportDefinition(
                "MyGameReport",
                null,
                new List<SimpleReportFilter>()
                {
                    new SimpleReportFilter("All", (GameProgress gp) => true),
                    new SimpleReportFilter("Settles", (GameProgress gp) => MyGP(gp).CaseSettles),
                    new SimpleReportFilter("Tried", (GameProgress gp) => !MyGP(gp).CaseSettles),
                    new SimpleReportFilter("LowQuality", (GameProgress gp) => MyGP(gp).LitigationQualityUniform <= 0.25),
                    new SimpleReportFilter("MediumQuality", (GameProgress gp) => MyGP(gp).LitigationQualityUniform > 0.25 && MyGP(gp).LitigationQualityUniform < 0.75),
                    new SimpleReportFilter("HighQuality", (GameProgress gp) => MyGP(gp).LitigationQualityUniform >= 0.75),
                    new SimpleReportFilter("LowPSignal", (GameProgress gp) => MyGP(gp).PSignalUniform <= 0.25),
                    new SimpleReportFilter("LowDSignal", (GameProgress gp) => MyGP(gp).DSignalUniform <= 0.25),
                    new SimpleReportFilter("MedPSignal", (GameProgress gp) => MyGP(gp).PSignalUniform > 0.25 && MyGP(gp).PSignalUniform < 0.75),
                    new SimpleReportFilter("MedDSignal", (GameProgress gp) => MyGP(gp).DSignalUniform > 0.25 && MyGP(gp).DSignalUniform < 0.75),
                    new SimpleReportFilter("HiPSignal", (GameProgress gp) => MyGP(gp).PSignalUniform >= 0.75),
                    new SimpleReportFilter("HiDSignal", (GameProgress gp) => MyGP(gp).DSignalUniform >= 0.75),
                },
                colItems
                );
        }

        private SimpleReportDefinition GetStrategyReport(int bargainingRound, bool reportResponseToOffer) 
        {
            (bool plaintiffMakesOffer, int offerNumber, bool isSimultaneous) = GetOfferorAndNumber(bargainingRound, ref reportResponseToOffer);
            string reportName = $"Round {bargainingRound} {(reportResponseToOffer ? "ResponseTo" : "")}{(plaintiffMakesOffer ? "P" : "D")} {offerNumber}";
            List<SimpleReportFilter> metaFilters = new List<SimpleReportFilter>();
            metaFilters.Add(new SimpleReportFilter("RoundOccurs", (GameProgress gp) => MyGP(gp).BargainingRoundsComplete >= bargainingRound));
            List<SimpleReportFilter> rowFilters = new List<SimpleReportFilter>()
            {
                new SimpleReportFilter("All", (GameProgress gp) => true)
            };
            Tuple<double, double>[] signalRegions = EquallySpaced.GetRegions(NumSignals);
            for (int i = 0; i < NumSignals; i++)
            {
                double regionStart = signalRegions[i].Item1;
                double regionEnd = signalRegions[i].Item2;
                if (plaintiffMakesOffer)
                    rowFilters.Add(new SimpleReportFilter($"PSignal {regionStart.ToSignificantFigures(2)}-{regionEnd.ToSignificantFigures(2)}", (GameProgress gp) => MyGP(gp).PSignalUniform >= regionStart && MyGP(gp).PSignalUniform < regionEnd));
                else
                    rowFilters.Add(new SimpleReportFilter($"DSignal {regionStart.ToSignificantFigures(2)}-{regionEnd.ToSignificantFigures(2)}", (GameProgress gp) => MyGP(gp).DSignalUniform >= regionStart && MyGP(gp).DSignalUniform < regionEnd));
            }
            List<SimpleReportColumnItem> columnItems = new List<SimpleReportColumnItem>()
            {
                new SimpleReportColumnFilter("All", (GameProgress gp) => true, true)
            };
            Tuple<double, double>[] offerRegions = EquallySpaced.GetRegions(NumOffers);
            for (int i = 0; i < NumOffers; i++)
            {
                double regionStart = offerRegions[i].Item1;
                double regionEnd = offerRegions[i].Item2;
                columnItems.Add(new SimpleReportColumnFilter(
                    $"{(reportResponseToOffer ? "To" : "")}{(plaintiffMakesOffer ? "P" : "D")}{offerNumber} {regionStart.ToSignificantFigures(2)}-{regionEnd.ToSignificantFigures(2)}{(reportResponseToOffer ? "" : "  ")}",
                    GetOfferOrResponseFilter(plaintiffMakesOffer, offerNumber, reportResponseToOffer, offerRegions[i]),
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
                    if (BargainingRoundsSimultaneous[b - 1])
                    {
                        earlierOffersPlaintiff++;
                        earlierOffersDefendant++;
                    }
                    else
                    {
                        if (BargainingRoundsPGoesFirstIfNotSimultaneous[b - 1])
                            earlierOffersPlaintiff++;
                        else
                            earlierOffersDefendant++;
                    }
                }
                else
                {
                    if (BargainingRoundsSimultaneous[b - 1])
                    {
                        plaintiffMakesOffer = !reportResponseToOffer;
                        reportResponseToOffer = false; // we want to report the offer (which may be the defendant's).
                        isSimultaneous = false;
                    }
                    else
                        plaintiffMakesOffer = BargainingRoundsPGoesFirstIfNotSimultaneous[b - 1];
                    offerNumber = plaintiffMakesOffer ? earlierOffersPlaintiff + 1 : earlierOffersDefendant + 1;
                }
            }
            return (plaintiffMakesOffer, offerNumber, isSimultaneous);
        }

        private Func<GameProgress, bool> GetOfferOrResponseFilter(bool plaintiffMakesOffer, int offerNumber, bool reportResponseToOffer, Tuple<double, double> offerRange)
        {
            if (reportResponseToOffer)
                return (GameProgress gp) => IsInOfferRange(MyGP(gp).GetOffer(plaintiffMakesOffer, offerNumber), offerRange) && MyGP(gp).GetResponse(!plaintiffMakesOffer, offerNumber);
            else
                return (GameProgress gp) => IsInOfferRange(MyGP(gp).GetOffer(plaintiffMakesOffer, offerNumber), offerRange);
        }

        private bool IsInOfferRange(double? value, Tuple<double, double> offerRange)
        {
            return value != null && value >= offerRange.Item1 && value < offerRange.Item2;
        }

        private void ParseOptions(string options)
        {
            NumLitigationQualityPoints = GameModule.GetByteCodeGeneratorOption(options, "NumLitigationQualityPoints");
            NumSignals = GameModule.GetByteCodeGeneratorOption(options, "NumSignals");
            NumOffers = GameModule.GetByteCodeGeneratorOption(options, "NumOffers");
            SubsequentOffersAreDeltas = GameModule.GetBoolCodeGeneratorOption(options, "SubsequentOffersAreDeltas");
            DeltaStartingValue = GameModule.GetDoubleCodeGeneratorOption(options, "DeltaStartingValue");
            MaxDelta = GameModule.GetDoubleCodeGeneratorOption(options, "MaxDelta");
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
            if (decisionByteCode == (byte)MyGameDecisions.PSignal)
            {
                MyGameProgress myGameProgress = (MyGameProgress)gameProgress;
                return DiscreteValueSignal.GetProbabilitiesOfDiscreteSignals(myGameProgress.LitigationQualityDiscrete, PSignalParameters);
            }
            else if (decisionByteCode == (byte)MyGameDecisions.DSignal)
            {
                MyGameProgress myGameProgress = (MyGameProgress)gameProgress;
                return DiscreteValueSignal.GetProbabilitiesOfDiscreteSignals(myGameProgress.LitigationQualityDiscrete, DSignalParameters);
            }
            else if (decisionByteCode == (byte)MyGameDecisions.CourtDecision)
            {
                double[] probabilities = new double[2];
                MyGameProgress myGameProgress = (MyGameProgress)gameProgress;
                probabilities[0] = 1.0 - myGameProgress.LitigationQualityUniform; // probability action 1 ==> rule for defendant
                probabilities[1] = myGameProgress.LitigationQualityUniform; // probability action 2 ==> rule for plaintiff
                return probabilities;
            }
            else
                throw new NotImplementedException(); // subclass should define if needed
        }

    }
}
