using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.ComponentModel.Composition;
using ACESim.Util;

namespace ACESim
{
    [Serializable]
    [ExportMetadata("CodeGeneratorName", "MyGameDefinition")]
    public class MyGameDefinition : GameDefinition
    {
        public MyGameOptions Options;

        public MyGameDefinition() : base()
        {

        }

        public void Setup(MyGameOptions options)
        {
            Options = options;
            FurtherOptionsSetup();

            Players = GetPlayersList();
            NumPlayers = (byte) Players.Count();
            DecisionsExecutionOrder = GetDecisionsList();

            IGameFactory gameFactory = new MyGameFactory();
            Initialize(gameFactory);
        }

        MyGameProgress MyGP(GameProgress gp) => gp as MyGameProgress;

        static string PlaintiffName = "P";
        static string DefendantName = "D";
        static string LitigationQualityChanceName = "QC";
        static string PlaintiffSignalChanceName = "PSC";
        static string DefendantSignalChanceName = "DSC";
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
                    new PlayerInfo(PlaintiffSignalChanceName, (int) MyGamePlayers.PSignalChance, true, false),
                    new PlayerInfo(DefendantSignalChanceName, (int) MyGamePlayers.DSignalChance, true, false),
                    new PlayerInfo(CourtChanceName, (int) MyGamePlayers.CourtChance, true, false),
                    new PlayerInfo(ResolutionPlayerName, (int) MyGamePlayers.Resolution, true, false),
                };
        }

        public override byte PlayerIndex_ResolutionPlayer => (byte) MyGamePlayers.Resolution;

        private List<Decision> GetDecisionsList()
        {
            var decisions = new List<Decision>();
            // Litigation Quality. This is not known by a player unless the player has perfect information. 
            // The SignalChance player relies on this information in calculating the probabilities of different signals
            List<byte> playersKnowingLitigationQuality = new List<byte>() { (byte) MyGamePlayers.PSignalChance, (byte)MyGamePlayers.DSignalChance, (byte) MyGamePlayers.CourtChance };
            if (Options.PNoiseStdev == 0)
                playersKnowingLitigationQuality.Add((byte)MyGamePlayers.Plaintiff);
            if (Options.DNoiseStdev == 0)
                playersKnowingLitigationQuality.Add((byte)MyGamePlayers.Defendant);
            decisions.Add(new Decision("LitigationQuality", "Qual", (byte)MyGamePlayers.QualityChance, playersKnowingLitigationQuality, Options.NumLitigationQualityPoints, (byte)MyGameDecisions.LitigationQuality));
            // Plaintiff and defendant signals. If a player has perfect information, then no signal is needed.
            if (Options.PNoiseStdev != 0)
                decisions.Add(new Decision("PlaintiffSignal", "PSig", (byte)MyGamePlayers.PSignalChance, new List<byte> { (byte)MyGamePlayers.Plaintiff }, Options.NumSignals, (byte)MyGameDecisions.PSignal, unevenChanceActions: true));
            if (Options.DNoiseStdev != 0)
                decisions.Add(new Decision("DefendantSignal", "DSig", (byte)MyGamePlayers.DSignalChance, new List<byte> { (byte)MyGamePlayers.Defendant }, Options.NumSignals, (byte)MyGameDecisions.DSignal, unevenChanceActions: true));
            for (int b = 0; b < Options.NumBargainingRounds; b++)
            {
                // bargaining -- note that we will do all information set manipulation in CustomInformationSetManipulation below.
                if (Options.BargainingRoundsSimultaneous)
                { // samuelson-chaterjee bargaining
                    var pOffer = new Decision("PlaintiffOffer" + (b + 1), "PO" + (b + 1), (byte)MyGamePlayers.Plaintiff, null, Options.NumOffers, (byte)MyGameDecisions.POffer) { CustomByte = (byte)(b + 1), CustomInformationSetManipulationOnly = true };
                    AddOfferDecisionOrSubdivisions(decisions, pOffer);
                    var dOffer = new Decision("DefendantOffer" + (b + 1), "DO" + (b + 1), (byte)MyGamePlayers.Defendant, null, Options.NumOffers, (byte)MyGameDecisions.DOffer) { CanTerminateGame = true, CustomByte = (byte)(b + 1), CustomInformationSetManipulationOnly = true };
                    AddOfferDecisionOrSubdivisions(decisions, dOffer);

                }
                else
                { // offer-response bargaining
                    // the response may be irrelevant but no harm adding it to information set
                    if (Options.PGoesFirstIfNotSimultaneous[b])
                    {
                        var pOffer = new Decision("PlaintiffOffer" + (b + 1), "PO" + (b + 1), (byte)MyGamePlayers.Plaintiff, null, Options.NumOffers, (byte)MyGameDecisions.POffer) { CustomByte = (byte)(b + 1), CustomInformationSetManipulationOnly = true }; // { AlwaysDoAction = 4});
                        AddOfferDecisionOrSubdivisions(decisions, pOffer);
                        decisions.Add(new Decision("DefendantResponse" + (b + 1), "DR" + (b + 1), (byte)MyGamePlayers.Defendant, null, 2, (byte)MyGameDecisions.DResponse) { CanTerminateGame = true, CustomByte = (byte)(b + 1), CustomInformationSetManipulationOnly = true });
                    }
                    else
                    {
                        var dOffer = new Decision("DefendantOffer" + (b + 1), "DO" + (b + 1), (byte)MyGamePlayers.Defendant, null, Options.NumOffers, (byte)MyGameDecisions.DOffer) { CustomByte = (byte)(b + 1), CustomInformationSetManipulationOnly = true };
                        AddOfferDecisionOrSubdivisions(decisions, dOffer);
                        decisions.Add(new Decision("PlaintiffResponse" + (b + 1), "PR" + (b + 1), (byte)MyGamePlayers.Plaintiff, null, 2, (byte)MyGameDecisions.PResponse) { CanTerminateGame = true, CustomByte = (byte)(b + 1), CustomInformationSetManipulationOnly = true });
                    }
                }
            }
            decisions.Add(new Decision("CourtDecision", "CD", (byte)MyGamePlayers.CourtChance, new List<byte> { (byte)MyGamePlayers.Resolution }, 2 /* for plaintiff or for defendant */, (byte)MyGameDecisions.CourtDecision, unevenChanceActions: true) { CanTerminateGame = true });
            return decisions;
        }

        private void AddOfferDecisionOrSubdivisions(List<Decision> decisions, Decision offerDecision)
        {
            AddPotentiallySubdividableDecision(decisions, offerDecision, Options.SubdivideOffers, (byte)MyGameDecisions.SubdividableOffer, 2, Options.NumOffers);
        }

        public override void CustomInformationSetManipulation(Decision currentDecision, byte currentDecisionIndex, byte actionChosen, ref GameHistory gameHistory)
        {
            byte decisionByteCode = currentDecision.Subdividable_IsSubdivision ? currentDecision.Subdividable_CorrespondingDecisionByteCode : currentDecision.DecisionByteCode;
            if (decisionByteCode >= (byte)MyGameDecisions.POffer && decisionByteCode <= (byte)MyGameDecisions.DResponse)
            {
                bool addPlayersOwnDecisionsToInformationSet = false;
                byte bargainingRound = currentDecision.CustomByte;
                byte bargainingRoundIndex = (byte)(bargainingRound - 1);
                byte currentPlayer = currentDecision.PlayerNumber;
                // Players information sets. We are going to use custom information set manipulation to add the players' information sets. This gives us the 
                // flexibility to remove information about old bargaining rounds. 
                if (Options.BargainingRoundsSimultaneous)
                { // samuelson-chaterjee bargaining
                    if (currentPlayer == (byte) MyGamePlayers.Defendant)
                    {
                        // If we are forgetting bargaining rounds, then we don't need to add this to either players' information set. 
                        // We'll still add the offers to the resolution set below.
                        if (!Options.ForgetEarlierBargainingRounds)
                        {
                            // We have completed this round of bargaining. Only now should we add the information to the plaintiff and defendant information sets. 
                            // Note that the plaintiff and defendant will both have made their decisions based on whatever information was available from before this round.
                            // We don't want to add the plaintiff's decision before the defendant has actually made a decision, so that's why we add both decisions now.

                            // Now add the information -- the actual decision for the other player. 
                            // But what did each party actually offer? To figure that out, we need to look at the GameHistory. Because these decisions may be subdivided, 
                            // GameHistory will look specifically at the simple actions list, which includes the aggregated decisions but not the subdivision decision.
                            (byte defendantsActionChosen, byte plaintiffsActionChosen) = gameHistory.GetLastActionAndActionBeforeThat();
                            if (addPlayersOwnDecisionsToInformationSet)
                            {
                                gameHistory.AddToInformationSet(plaintiffsActionChosen, currentDecisionIndex, (byte)MyGamePlayers.Plaintiff); // defendant's decision conveyed to plaintiff
                                gameHistory.AddToInformationSet(defendantsActionChosen, currentDecisionIndex, (byte)MyGamePlayers.Defendant); // plaintiff's decision conveyed to defendant
                            }
                            gameHistory.AddToInformationSet(defendantsActionChosen, currentDecisionIndex, (byte)MyGamePlayers.Plaintiff); // defendant's decision conveyed to plaintiff
                            gameHistory.AddToInformationSet(plaintiffsActionChosen, currentDecisionIndex, (byte)MyGamePlayers.Defendant); // plaintiff's decision conveyed to defendant
                        }
                    }
                }
                else
                { // offer-response bargaining
                    bool pGoesFirst = Options.PGoesFirstIfNotSimultaneous[bargainingRoundIndex];
                    byte partyGoingFirst = pGoesFirst ? (byte)MyGamePlayers.Plaintiff : (byte)MyGamePlayers.Defendant;
                    byte partyGoingSecond = pGoesFirst ? (byte)MyGamePlayers.Defendant : (byte)MyGamePlayers.Plaintiff;
                    byte otherPlayer = currentPlayer == (byte)MyGamePlayers.Plaintiff ? (byte)MyGamePlayers.Defendant : (byte)MyGamePlayers.Plaintiff;

                    // We don't need to put a player's decision into the player's own information set. A player at least knows the information that led to its earlier decision.
                    // TODO: We could create an option for player to remember own information. After all, with mixed strategies, player will not know what player actually decided.
                    if (currentPlayer == partyGoingFirst)
                    {
                        gameHistory.AddToInformationSet(actionChosen, currentDecisionIndex, otherPlayer); // convey decision to other player (who must choose whether to act on it)
                        if (addPlayersOwnDecisionsToInformationSet)
                            gameHistory.AddToInformationSet(actionChosen, currentDecisionIndex, currentPlayer); // add offer to the offering party's information set. Note that we never need to add a response, since it will always be clear in later rounds that the response was "no".
                    }
                    else if (Options.ForgetEarlierBargainingRounds)
                    {
                        gameHistory.RemoveItemsInInformationSet(currentPlayer, currentDecisionIndex, 1);
                        if (addPlayersOwnDecisionsToInformationSet)
                            gameHistory.RemoveItemsInInformationSet(otherPlayer, currentDecisionIndex, 1);
                    }
                }

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
                byte numItems = gameHistory.CountItemsInInformationSet((byte)MyGamePlayers.Resolution);
                if (numItems == 3)
                {
                    numItems = 0;
                    gameHistory.RemoveItemsInInformationSet((byte)MyGamePlayers.Resolution, currentDecisionIndex, numItems);
                }
                if (numItems == 0)
                    gameHistory.AddToInformationSet(currentDecisionIndex, currentDecisionIndex, (byte)MyGamePlayers.Resolution); // in effect, just note the decision leading to resolution
                gameHistory.AddToInformationSet(actionChosen, decisionByteCode, (byte)MyGamePlayers.Resolution);
            }
        }

        public override bool ShouldMarkGameHistoryComplete(Decision currentDecision, GameHistory gameHistory)
        {
            if (!currentDecision.CanTerminateGame)
                return false;

            byte decisionByteCode;
            if (currentDecision.Subdividable_IsSubdivision)
            {
                if (currentDecision.Subdividable_IsSubdivision_Last)
                    decisionByteCode = currentDecision.Subdividable_CorrespondingDecisionByteCode;
                else
                    return false; // must get to the last subdivision before considering this
            }
            else
                decisionByteCode = currentDecision.DecisionByteCode;

            switch (decisionByteCode)
            {
                case (byte)MyGameDecisions.CourtDecision:
                    return true;
                case (byte)MyGameDecisions.DResponse:
                case (byte)MyGameDecisions.PResponse:
                    var lastTwoActions = gameHistory.GetLastActionAndActionBeforeThat();
                    if (lastTwoActions.mostRecentAction == 1) // offer was accepted
                        return true;
                    break;
                case (byte)MyGameDecisions.DOffer:
                    // this is simultaneous bargaining (plaintiff offer is always first). 
                    if (!Options.BargainingRoundsSimultaneous)
                        throw new Exception("Internal error.");
                    (byte defendantAction, byte plaintiffAction) = gameHistory.GetLastActionAndActionBeforeThat();
                    if (defendantAction >= plaintiffAction)
                        return true;
                    break;
            }
            return false;
        }


        public override List<SimpleReportDefinition> GetSimpleReportDefinitions()
        {
            var reports = new List<SimpleReportDefinition>();
            reports.Add(GetOverallReport());
            if (Options.IncludeSignalsReport)
            {
                for (int b = 1; b <= Options.NumBargainingRounds; b++)
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
            for (int b = 1; b <= Options.NumBargainingRounds; b++)
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
            Tuple<double, double>[] signalRegions = EquallySpaced.GetRegions(Options.NumSignals);
            for (int i = 0; i < Options.NumSignals; i++)
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
            Tuple<double, double>[] offerRegions = EquallySpaced.GetRegions(Options.NumOffers);
            for (int i = 0; i < Options.NumOffers; i++)
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

        private void FurtherOptionsSetup()
        {

            if (Options.DeltaOffersOptions.SubsequentOffersAreDeltas)
                Options.DeltaOffersCalculation = new DeltaOffersCalculation(this);
            Options.PSignalParameters = new DiscreteValueSignalParameters()
            {
                NumPointsInSourceUniformDistribution = Options.NumLitigationQualityPoints,
                StdevOfNormalDistribution = Options.PNoiseStdev,
                NumSignals = Options.NumSignals
            };
            Options.DSignalParameters = new DiscreteValueSignalParameters()
            {
                NumPointsInSourceUniformDistribution = Options.NumLitigationQualityPoints,
                StdevOfNormalDistribution = Options.DNoiseStdev,
                NumSignals = Options.NumSignals
            };
        }

        public override double[] GetChanceActionProbabilities(byte decisionByteCode, GameProgress gameProgress)
        {
            if (decisionByteCode == (byte)MyGameDecisions.PSignal)
            {
                MyGameProgress myGameProgress = (MyGameProgress)gameProgress;
                return DiscreteValueSignal.GetProbabilitiesOfDiscreteSignals(myGameProgress.LitigationQualityDiscrete, Options.PSignalParameters);
            }
            else if (decisionByteCode == (byte)MyGameDecisions.DSignal)
            {
                MyGameProgress myGameProgress = (MyGameProgress)gameProgress;
                return DiscreteValueSignal.GetProbabilitiesOfDiscreteSignals(myGameProgress.LitigationQualityDiscrete, Options.DSignalParameters);
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
