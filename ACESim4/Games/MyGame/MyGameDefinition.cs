using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ACESim.Util;

namespace ACESim
{
    [Serializable]
    public partial class MyGameDefinition : GameDefinition
    {
        public MyGameOptions Options;

        #region Construction and setup

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

        private MyGameProgress MyGP(GameProgress gp) => gp as MyGameProgress;

        private static string PlaintiffName = "P";
        private static string DefendantName = "D";
        private static string LitigationQualityChanceName = "QC";
        private static string PlaintiffSignalChanceName = "PSC";
        private static string DefendantSignalChanceName = "DSC";
        private static string BothGiveUpChanceName = "GUC";
        private static string CourtChanceName = "CC";
        private static string ResolutionPlayerName = "R";

        private static List<PlayerInfo> GetPlayersList()
        {
            // IMPORTANT: Chance players MUST be listed after other players. Resolution player should be listed last.
            return new List<PlayerInfo>
                {
                    new PlayerInfo(PlaintiffName, (int) MyGamePlayers.Plaintiff, false, true),
                    new PlayerInfo(DefendantName, (int) MyGamePlayers.Defendant, false, true),
                    new PlayerInfo(LitigationQualityChanceName, (int) MyGamePlayers.QualityChance, true, false),
                    new PlayerInfo(PlaintiffSignalChanceName, (int) MyGamePlayers.PSignalChance, true, false),
                    new PlayerInfo(DefendantSignalChanceName, (int) MyGamePlayers.DSignalChance, true, false),
                    new PlayerInfo(BothGiveUpChanceName, (int) MyGamePlayers.BothGiveUpChance, true, false),
                    new PlayerInfo(CourtChanceName, (int) MyGamePlayers.CourtChance, true, false),
                    new PlayerInfo(ResolutionPlayerName, (int) MyGamePlayers.Resolution, true, false),
                };
        }

        public override byte PlayerIndex_ResolutionPlayer => (byte) MyGamePlayers.Resolution;

        private byte LitigationQualityDecisionIndex = (byte) 255;

        #endregion

        #region Decisions list

        private List<Decision> GetDecisionsList()
        {
            var decisions = new List<Decision>();
            AddFileAndAnswerDecisions(decisions);
            AddLitigationQualityAndSignalsDecisions(decisions);
            for (int b = 0; b < Options.NumBargainingRounds; b++)
            {
                AddDecisionsForBargainingRound(b, decisions);
                if (Options.AllowAbandonAndDefaults)
                    AddAbandonOrDefaultDecisions(b, decisions);
            }
            AddCourtDecision(decisions);
            return decisions;
        }

        private void AddLitigationQualityAndSignalsDecisions(List<Decision> decisions)
        {
// Litigation Quality. This is not known by a player unless the player has perfect information. 
            // The SignalChance player relies on this information in calculating the probabilities of different signals
            List<byte> playersKnowingLitigationQuality = new List<byte>()
            {
                (byte) MyGamePlayers.PSignalChance,
                (byte) MyGamePlayers.DSignalChance,
                (byte) MyGamePlayers.CourtChance
            };
            if (Options.UseRawSignals)
                playersKnowingLitigationQuality.Add((byte) MyGamePlayers.Resolution);
            if (Options.PNoiseStdev == 0)
                playersKnowingLitigationQuality.Add((byte) MyGamePlayers.Plaintiff);
            if (Options.DNoiseStdev == 0)
                playersKnowingLitigationQuality.Add((byte) MyGamePlayers.Defendant);
            decisions.Add(new Decision("LitigationQuality", "Qual", (byte) MyGamePlayers.QualityChance,
                playersKnowingLitigationQuality, Options.NumLitigationQualityPoints, (byte) MyGameDecisions.LitigationQuality));
            LitigationQualityDecisionIndex = 0; // we'll need to change this if we have decisions before litigation quality
            // Plaintiff and defendant signals. If a player has perfect information, then no signal is needed.
            bool
                partyReceivesDirectSignal =
                    !Options
                        .UseRawSignals; // when using processed signals, we have an uneven chance decision, so the action is the signal, and the party can receive it directly. When using raw signals, we want the party to receive the signal and we add that with custom information set manipulation below.
            if (!Options.UseRawSignals && Options.NumNoiseValues != Options.NumSignals)
                throw new NotImplementedException(); // our uneven chance probabilities assumes this is true
            if (Options.PNoiseStdev != 0)
                decisions.Add(new Decision("PlaintiffSignal", "PSig", (byte) MyGamePlayers.PSignalChance,
                    partyReceivesDirectSignal ? new List<byte>() {(byte) MyGamePlayers.Plaintiff} : new List<byte>(),
                    Options.NumNoiseValues, (byte) MyGameDecisions.PSignal, unevenChanceActions: !Options.UseRawSignals));
            if (Options.DNoiseStdev != 0)
                decisions.Add(new Decision("DefendantSignal", "DSig", (byte) MyGamePlayers.DSignalChance,
                    partyReceivesDirectSignal ? new List<byte>() {(byte) MyGamePlayers.Defendant} : new List<byte>(),
                    Options.NumNoiseValues, (byte) MyGameDecisions.DSignal, unevenChanceActions: !Options.UseRawSignals));
            CreateSignalsTable();
        }

        public void GetDiscreteSignal(byte litigationQuality, byte noise, bool plaintiff, out byte discreteSignal,
            out double uniformSignal)
        {
            ValueTuple<byte, double> tableValue;
            if (plaintiff)
                tableValue = PRawSignalsTable[litigationQuality, noise];
            else
                tableValue = DRawSignalsTable[litigationQuality, noise];
            discreteSignal = tableValue.Item1;
            uniformSignal = tableValue.Item2;
        }

        private ValueTuple<byte, double>[,] PRawSignalsTable, DRawSignalsTable;
        public void CreateSignalsTable()
        {
            PRawSignalsTable = new ValueTuple<byte, double>[Options.NumLitigationQualityPoints + 1, Options.NumNoiseValues + 1];
            DRawSignalsTable = new ValueTuple<byte, double>[Options.NumLitigationQualityPoints + 1, Options.NumNoiseValues + 1];
            for (byte litigationQuality = 1;
                litigationQuality <= Options.NumLitigationQualityPoints;
                litigationQuality++)
            {
                double litigationQualityUniform =
                    EquallySpaced.GetLocationOfEquallySpacedPoint(litigationQuality - 1,
                        Options.NumLitigationQualityPoints);
                for (byte noise = 1; noise <= Options.NumNoiseValues; noise++)
                {
                    MyGame.ConvertNoiseActionToDiscreteAndUniformSignal(noise, Options.UseRawSignals,
                        litigationQualityUniform, Options.NumNoiseValues,
                        Options.PNoiseStdev, Options.NumSignals,
                        out byte pDiscreteSignal, out double pUniformSignal);
                    PRawSignalsTable[litigationQuality, noise] = (pDiscreteSignal, pUniformSignal);
                    MyGame.ConvertNoiseActionToDiscreteAndUniformSignal(noise, Options.UseRawSignals,
                        litigationQualityUniform, Options.NumNoiseValues,
                        Options.DNoiseStdev, Options.NumSignals,
                        out byte dDiscreteSignal, out double dUniformSignal);
                    DRawSignalsTable[litigationQuality, noise] = (dDiscreteSignal, dUniformSignal);
                }
            }
        }
        
        private void AddFileAndAnswerDecisions(List<Decision> decisions)
        {
            var pFile =
                new Decision("PFile", "PF", (byte)MyGamePlayers.Plaintiff, new List<byte>() { (byte)MyGamePlayers.Resolution },
                    2, (byte)MyGameDecisions.PFile)
                {
                    CanTerminateGame = true, // not filing always terminates
                };
            decisions.Add(pFile);

            var dAnswer =
                new Decision("DAnswer", "DA", (byte)MyGamePlayers.Defendant, new List<byte>() { (byte)MyGamePlayers.Resolution },
                    2, (byte)MyGameDecisions.DAnswer)
                {
                    CanTerminateGame = true, // not answering terminates, with defendant paying full damages
                };
            decisions.Add(dAnswer);
        }

        private void AddDecisionsForBargainingRound(int b, List<Decision> decisions)
        {
            // note that we will do all information set manipulation in CustomInformationSetManipulation below.
            if (Options.BargainingRoundsSimultaneous)
            {
                // samuelson-chaterjee bargaining
                var pOffer =
                    new Decision("PlaintiffOffer" + (b + 1), "PO" + (b + 1), (byte)MyGamePlayers.Plaintiff, null,
                        Options.NumOffers, (byte)MyGameDecisions.POffer)
                    {
                        CustomByte = (byte)(b + 1),
                        CustomInformationSetManipulationOnly = true
                    };
                AddOfferDecisionOrSubdivisions(decisions, pOffer);
                var dOffer =
                    new Decision("DefendantOffer" + (b + 1), "DO" + (b + 1), (byte)MyGamePlayers.Defendant, null,
                        Options.NumOffers, (byte)MyGameDecisions.DOffer)
                    {
                        CanTerminateGame = true,
                        CustomByte = (byte)(b + 1),
                        CustomInformationSetManipulationOnly = true
                    };
                AddOfferDecisionOrSubdivisions(decisions, dOffer);
            }
            else
            {
                // offer-response bargaining
                // the response may be irrelevant but no harm adding it to information set
                if (Options.PGoesFirstIfNotSimultaneous[b])
                {
                    var pOffer =
                        new Decision("PlaintiffOffer" + (b + 1), "PO" + (b + 1), (byte)MyGamePlayers.Plaintiff, null,
                            Options.NumOffers, (byte)MyGameDecisions.POffer)
                        {
                            CustomByte = (byte)(b + 1),
                            CustomInformationSetManipulationOnly = true
                        }; // { AlwaysDoAction = 4});
                    AddOfferDecisionOrSubdivisions(decisions, pOffer);
                    decisions.Add(
                        new Decision("DefendantResponse" + (b + 1), "DR" + (b + 1), (byte)MyGamePlayers.Defendant, null, 2,
                            (byte)MyGameDecisions.DResponse)
                        {
                            CanTerminateGame = true,
                            CustomByte = (byte)(b + 1),
                            CustomInformationSetManipulationOnly = true
                        });
                }
                else
                {
                    var dOffer =
                        new Decision("DefendantOffer" + (b + 1), "DO" + (b + 1), (byte)MyGamePlayers.Defendant, null,
                            Options.NumOffers, (byte)MyGameDecisions.DOffer)
                        {
                            CustomByte = (byte)(b + 1),
                            CustomInformationSetManipulationOnly = true
                        };
                    AddOfferDecisionOrSubdivisions(decisions, dOffer);
                    decisions.Add(
                        new Decision("PlaintiffResponse" + (b + 1), "PR" + (b + 1), (byte)MyGamePlayers.Plaintiff, null, 2,
                            (byte)MyGameDecisions.PResponse)
                        {
                            CanTerminateGame = true,
                            CustomByte = (byte)(b + 1),
                            CustomInformationSetManipulationOnly = true
                        });
                }
            }
        }

        private void AddOfferDecisionOrSubdivisions(List<Decision> decisions, Decision offerDecision)
        {
            AddPotentiallySubdividableDecision(decisions, offerDecision, Options.SubdivideOffers, (byte)MyGameDecisions.SubdividableOffer, 2, Options.NumOffers);
        }
        
        private void AddAbandonOrDefaultDecisions(int b, List<Decision> decisions)
        {
            var pAbandon =
                new Decision("PAbandon" + (b + 1), "PA" + (b + 1), (byte)MyGamePlayers.Plaintiff, new List<byte>() { (byte)MyGamePlayers.Resolution }, 
                    2, (byte)MyGameDecisions.PAbandon)
                {
                    CustomByte = (byte)(b + 1),
                    CanTerminateGame = false, // we always must look at whether D is defaulting too. 
                };
            decisions.Add(pAbandon);

            var dDefault =
                new Decision("DDefault" + (b + 1), "DD" + (b + 1), (byte)MyGamePlayers.Defendant, new List<byte>() { (byte)MyGamePlayers.Resolution },
                    2, (byte)MyGameDecisions.DDefault)
                {
                    CustomByte = (byte)(b + 1),
                    CanTerminateGame = true, // if either but not both has given up, game terminates
                };
            decisions.Add(dDefault);

            var bothGiveUp =
                new Decision("MutualGiveUp" + (b + 1), "MGU" + (b + 1), (byte)MyGamePlayers.BothGiveUpChance, new List<byte>() { (byte)MyGamePlayers.Resolution },
                    2, (byte)MyGameDecisions.MutualGiveUp, unevenChanceActions: false)
                {
                    CustomByte = (byte)(b + 1),
                    CanTerminateGame = true, // if this decision is needed, then both have given up, and the decision always terminates the game
                    CriticalNode = true, // always play out both sides of this coin flip
                };
            decisions.Add(dDefault);
        }

        private void AddCourtDecision(List<Decision> decisions)
        {
            if (Options.UseRawSignals)
                decisions.Add(new Decision("CourtDecision", "CD", (byte)MyGamePlayers.CourtChance,
                        new List<byte> { (byte)MyGamePlayers.Resolution }, Options.NumSignals, (byte)MyGameDecisions.CourtDecision,
                        unevenChanceActions: false, criticalNode: true)
                    { CanTerminateGame = true }); // even chance options
            else
                decisions.Add(new Decision("CourtDecision", "CD", (byte)MyGamePlayers.CourtChance,
                    new List<byte> { (byte)MyGamePlayers.Resolution }, 2 /* for plaintiff or for defendant */,
                    (byte)MyGameDecisions.CourtDecision, unevenChanceActions: true, criticalNode: true)
                {
                    CanTerminateGame = true
                }); // uneven chance options
        }

        #endregion

        #region Game play support 
        
        public override double[] GetChanceActionProbabilities(byte decisionByteCode, GameProgress gameProgress)
        {
            if (decisionByteCode == (byte)MyGameDecisions.PSignal)
            {
                if (Options.UseRawSignals)
                    throw new NotImplementedException();
                MyGameProgress myGameProgress = (MyGameProgress)gameProgress;
                return DiscreteValueSignal.GetProbabilitiesOfDiscreteSignals(myGameProgress.LitigationQualityDiscrete, Options.PSignalParameters);
            }
            else if (decisionByteCode == (byte)MyGameDecisions.DSignal)
            {
                if (Options.UseRawSignals)
                    throw new NotImplementedException();
                MyGameProgress myGameProgress = (MyGameProgress)gameProgress;
                return DiscreteValueSignal.GetProbabilitiesOfDiscreteSignals(myGameProgress.LitigationQualityDiscrete, Options.DSignalParameters);
            }
            else if (decisionByteCode == (byte)MyGameDecisions.CourtDecision)
            {
                if (Options.UseRawSignals)
                    throw new NotImplementedException();
                double[] probabilities = new double[2];
                MyGameProgress myGameProgress = (MyGameProgress)gameProgress;
                probabilities[0] =
                    1.0 - myGameProgress.LitigationQualityUniform; // probability action 1 ==> rule for defendant
                probabilities[1] =
                    myGameProgress.LitigationQualityUniform; // probability action 2 ==> rule for plaintiff
                return probabilities;
            }
            else
                throw new NotImplementedException(); // subclass should define if needed
        }

        public override bool ShouldMarkGameHistoryComplete(Decision currentDecision, GameHistory gameHistory, byte actionChosen)
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
                case (byte)MyGameDecisions.PFile:
                    if (actionChosen == 2)
                        return true; // plaintiff hasn't filed
                    break;
                case (byte)MyGameDecisions.DAnswer:
                    if (actionChosen == 2)
                        return true; // defendant's hasn't answered
                    break;
                case (byte)MyGameDecisions.DDefault:
                    (byte dTriesToDefault, byte pTriesToAbandon) = gameHistory.GetLastActionAndActionBeforeThat();
                    if (dTriesToDefault == 1 ^ pTriesToAbandon == 1) // i.e., one but not both parties try to default
                        return true;
                    break;
                case (byte)MyGameDecisions.MutualGiveUp:
                    return true; // if we reach this decision, the game is definitely over; just a question of who wins
            }
            return false;
        }

        public override void CustomInformationSetManipulation(Decision currentDecision, byte currentDecisionIndex, byte actionChosen, ref GameHistory gameHistory)
        {
            byte decisionByteCode = currentDecision.Subdividable_IsSubdivision ? currentDecision.Subdividable_CorrespondingDecisionByteCode : currentDecision.DecisionByteCode;
            if (Options.UseRawSignals && (decisionByteCode == (byte) MyGameDecisions.PSignal || decisionByteCode == (byte) MyGameDecisions.DSignal))
            {
                // When using processed signals, we just send the signal that the player receives, because there are unequal chance probabilities. With raw signals, we have an even chance of each noise value. We can't just give the player the noise value; we have to take into account the litigation quality. So, we do that here.
                byte litigationQuality = gameHistory.GetPlayerInformationItem((byte)MyGamePlayers.Resolution, LitigationQualityDecisionIndex);
                GetDiscreteSignal(litigationQuality, actionChosen, decisionByteCode == (byte)MyGameDecisions.PSignal, out byte discreteSignal, out _);
                gameHistory.AddToInformationSet(discreteSignal, currentDecisionIndex, decisionByteCode == (byte)MyGameDecisions.PSignal ? (byte) MyGamePlayers.Plaintiff : (byte) MyGamePlayers.Defendant);
                // NOTE: We don't have to do anything like this for the court's information set. The court simply gets the actual litigation quality and the noise. When the game is actually being played, the court will combine these to determine whether the plaintiff wins. The plaintiff and defendant are non-chance players, and so we want to have the same information set for all situations with the same signal.  But with the court, that doesn't matter. We can have lots of information sets, covering the wide range of possibilities.
            }
            if (decisionByteCode >= (byte)MyGameDecisions.POffer && decisionByteCode <= (byte)MyGameDecisions.DResponse)
            {
                bool addPlayersOwnDecisionsToInformationSet = false;
                byte bargainingRound = currentDecision.CustomByte;
                byte bargainingRoundIndex = (byte)(bargainingRound - 1);
                byte currentPlayer = currentDecision.PlayerNumber;
                // Players information sets. We are going to use custom information set manipulation to add the players' information sets. This gives us the 
                // flexibility to remove information about old bargaining rounds. 
                if (Options.BargainingRoundsSimultaneous)
                    CustomInformationSetManipulationSamuelsonChaterjeeBargaining(currentDecisionIndex, ref gameHistory, currentPlayer, addPlayersOwnDecisionsToInformationSet);
                else
                    CustomInformationSetManipulationOfferResponseBargaining(currentDecisionIndex, actionChosen, ref gameHistory, bargainingRoundIndex, currentPlayer, addPlayersOwnDecisionsToInformationSet);
                CustomInformationSetManipulationBargainingToResolutionInformationSet(currentDecisionIndex, actionChosen, ref gameHistory, decisionByteCode);
            }
        }

        private void CustomInformationSetManipulationOfferResponseBargaining(byte currentDecisionIndex, byte actionChosen,
            ref GameHistory gameHistory, byte bargainingRoundIndex, byte currentPlayer, bool addPlayersOwnDecisionsToInformationSet)
        {
// offer-response bargaining
            bool pGoesFirst = Options.PGoesFirstIfNotSimultaneous[bargainingRoundIndex];
            byte partyGoingFirst = pGoesFirst ? (byte) MyGamePlayers.Plaintiff : (byte) MyGamePlayers.Defendant;
            byte partyGoingSecond = pGoesFirst ? (byte) MyGamePlayers.Defendant : (byte) MyGamePlayers.Plaintiff;
            byte otherPlayer = currentPlayer == (byte) MyGamePlayers.Plaintiff
                ? (byte) MyGamePlayers.Defendant
                : (byte) MyGamePlayers.Plaintiff;

            // We don't need to put a player's decision into the player's own information set. A player at least knows the information that led to its earlier decision.
            // TODO: We could create an option for player to remember own information. After all, with mixed strategies, player will not know what player actually decided.
            if (currentPlayer == partyGoingFirst)
            {
                gameHistory.AddToInformationSet(actionChosen, currentDecisionIndex,
                    otherPlayer); // convey decision to other player (who must choose whether to act on it)
                if (addPlayersOwnDecisionsToInformationSet)
                    gameHistory.AddToInformationSet(actionChosen, currentDecisionIndex,
                        currentPlayer); // add offer to the offering party's information set. Note that we never need to add a response, since it will always be clear in later rounds that the response was "no".
            }
            else if (Options.ForgetEarlierBargainingRounds)
            {
                gameHistory.RemoveItemsInInformationSet(currentPlayer, currentDecisionIndex, 1);
                if (addPlayersOwnDecisionsToInformationSet)
                    gameHistory.RemoveItemsInInformationSet(otherPlayer, currentDecisionIndex, 1);
            }
        }

        private void CustomInformationSetManipulationSamuelsonChaterjeeBargaining(byte currentDecisionIndex,
            ref GameHistory gameHistory, byte currentPlayer, bool addPlayersOwnDecisionsToInformationSet)
        {
            // samuelson-chaterjee bargaining
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
                        gameHistory.AddToInformationSet(plaintiffsActionChosen, currentDecisionIndex,
                            (byte) MyGamePlayers.Plaintiff); // defendant's decision conveyed to plaintiff
                        gameHistory.AddToInformationSet(defendantsActionChosen, currentDecisionIndex,
                            (byte) MyGamePlayers.Defendant); // plaintiff's decision conveyed to defendant
                    }
                    gameHistory.AddToInformationSet(defendantsActionChosen, currentDecisionIndex,
                        (byte) MyGamePlayers.Plaintiff); // defendant's decision conveyed to plaintiff
                    gameHistory.AddToInformationSet(plaintiffsActionChosen, currentDecisionIndex,
                        (byte) MyGamePlayers.Defendant); // plaintiff's decision conveyed to defendant
                }
            }
        }

        private void CustomInformationSetManipulationBargainingToResolutionInformationSet(byte currentDecisionIndex, byte actionChosen,
            ref GameHistory gameHistory, byte decisionByteCode)
        {
            // Resolution information set. We need an information set that uniquely identifies each distinct resolution. We have added the court decision 
            // to the resolution set above, but still need two types of information. First, we need information about how we got to the court decision.
            // When we are using raw signals, then the court's decision is based on both the raw signal and the noise value that the court receives. Thus,
            // the court's action, by itself, doesn't tell us who won the litigation. Thus, when using raw signals, we need to know what the original litigation
            // quality was. However, if we are not using raw signals, then for tried cases, it is enough to know the court decision action, which tells
            // us which party wins. Second, for settled cases, the resolution set must contain enough information to determine the settlement and its consequences.
            // We only need to put the LAST offer and response in the information set. This could, of course, be the first offer and response if it is accepted. 
            // We will also need information on the nature of the offer and response, since different bargaining rounds may have different structures,
            // and since the number of bargaining rounds that has occurred may affect the parties' payoffs.
            // For example, a response of 2 may mean something different if we have simultaneous bargaining or if plaintiff or defendant is responding.
            // So, we would like our resolution information set to have the decision number of the last offer (which may be the first of two simultaneous offers)
            // and the actions of both players. Thus, if there is nothing in the resolution information set, or just one item in the case where we use raw signals,
            // then we add the decision byte code and the action. If there are two items, or three when using raw signals then we add the decision byte code and 
            // the action. If there are three, or four in the case where we use raw signals, we delete everything and then there are zero, so
            // we respond accordingly. 
            byte numberItemsDefiningLitigationQuality = Options.UseRawSignals ? (byte)1 : (byte)0;
            byte numberItemsDefiningBargainingRound = 3;
            byte numItems = gameHistory.CountItemsInInformationSet((byte)MyGamePlayers.Resolution);
            if (numItems == numberItemsDefiningLitigationQuality + numberItemsDefiningBargainingRound)
            {
                gameHistory.RemoveItemsInInformationSet((byte)MyGamePlayers.Resolution, currentDecisionIndex,
                    numberItemsDefiningBargainingRound);
                numItems = numberItemsDefiningLitigationQuality;
            }
            if (numItems == numberItemsDefiningLitigationQuality)
            {
                gameHistory.AddToInformationSet(currentDecisionIndex, currentDecisionIndex,
                    (byte)MyGamePlayers.Resolution); // in effect, just note the decision leading to resolution
                numItems++;
            }
            // We'll be adding this offer/response to the information set, regardless of what we did above
            gameHistory.AddToInformationSet(actionChosen, decisionByteCode, (byte)MyGamePlayers.Resolution);
            numItems++; // doesn't matter since it's at the end, but useful to help understand the code.
        }

        #endregion


    }
}
