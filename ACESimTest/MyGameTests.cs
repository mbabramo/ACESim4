using System;
using System.Collections.Generic;
using System.Linq;
using ACESim;
using ACESim.Util;
using FluentAssertions;
using MathNet.Numerics.Providers.LinearAlgebra.OpenBlas;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ACESimTest
{
    [TestClass]
    public class MyGameTests
    {
        private int CaseNumber = 0;

        private static void GetInformationSetStrings(MyGameProgress myGameProgress, out string pInformationSet,
            out string dInformationSet, out string resolutionSet)
        {
            pInformationSet = myGameProgress.InformationSetLog.GetPlayerInformationAtPointString((byte)MyGamePlayers.Plaintiff, null);
            dInformationSet = myGameProgress.InformationSetLog.GetPlayerInformationAtPointString((byte)MyGamePlayers.Defendant, null);
            resolutionSet = myGameProgress.InformationSetLog.GetPlayerInformationAtPointString((byte)MyGamePlayers.Resolution, null);
            string pInformationSet2 = myGameProgress.GameHistory.GetCurrentPlayerInformationString((byte)MyGamePlayers.Plaintiff);
            string dInformationSet2 = myGameProgress.GameHistory.GetCurrentPlayerInformationString((byte)MyGamePlayers.Defendant);
            string resolutionSet2 = myGameProgress.GameHistory.GetCurrentPlayerInformationString((byte)MyGamePlayers.Resolution);
            pInformationSet.Should().Be(pInformationSet2);
            dInformationSet.Should().Be(dInformationSet2);
            resolutionSet.Should().Be(resolutionSet2);

        }

        private const double PartyNoise = 0.2, InitialWealth = 1_000_000, DamagesAlleged = 100_000, PFileCost = 3000, DAnswerCost = 2000, PTrialCosts = 4000, DTrialCosts = 6000, PerRoundBargainingCost = 1000, RegretAversion = 0.25;
        private const byte NumDistinctPoints = 8;
        private const byte NumCourtNoiseValues = 10;
        public const byte ValueWhenCaseSettles = 4;
        private const byte LitigationQuality = 3;
        private const byte PSignalOrNoise = 5, DSignalOrNoise = 1;


        public enum LoserPaysPolicy
        {
            NoLoserPays,
            AfterTrialOnly,
            EvenAfterAbandonOrDefault,
        }
        public enum HowToSimulateBargainingFailure
        {
            PRefusesToBargain,
            DRefusesToBargain,
            BothRefuseToBargain,
            BothAgreeToBargain,
            BothHaveNoChoiceAndMustBargain // i.e., we don't have the decisions representing agreement to bargain
        }

        private MyGameOptions GetGameOptions(bool allowAbandonAndDefaults, byte numBargainingRounds, bool subdivideOffers, bool forgetEarlierBargainingRounds, bool simultaneousBargainingRounds, bool actionIsNoiseNotSignal, LoserPaysPolicy loserPaysPolicy, HowToSimulateBargainingFailure simulatingBargainingFailure)
        {
            var options = new MyGameOptions()
            {
                PInitialWealth = InitialWealth,
                DInitialWealth = InitialWealth,
                DamagesAlleged = DamagesAlleged,
                NumLitigationQualityPoints = NumDistinctPoints,
                NumSignals = NumDistinctPoints,
                NumOffers = NumDistinctPoints,
                NumCourtNoiseValues = NumCourtNoiseValues,
                NumNoiseValues = NumDistinctPoints,
                ActionIsNoiseNotSignal = actionIsNoiseNotSignal,
                PNoiseStdev = PartyNoise,
                DNoiseStdev = PartyNoise,
                CourtNoiseStdev = 0.5,
                PFilingCost = PFileCost,
                DAnswerCost = DAnswerCost,
                PTrialCosts = PTrialCosts,
                DTrialCosts = DTrialCosts,
                RegretAversion = RegretAversion,
                LoserPays = loserPaysPolicy != LoserPaysPolicy.NoLoserPays,
                LoserPaysMultiple = 1.5,
                LoserPaysAfterAbandonment = loserPaysPolicy == LoserPaysPolicy.EvenAfterAbandonOrDefault,
                PerPartyCostsLeadingUpToBargainingRound = PerRoundBargainingCost,
                AllowAbandonAndDefaults = allowAbandonAndDefaults,
                DeltaOffersOptions = new DeltaOffersOptions()
                {
                    SubsequentOffersAreDeltas = false,
                    DeltaStartingValue = 0.01,
                    MaxDelta = 0.25
                },
                IncludeAgreementToBargainDecisions = simulatingBargainingFailure != HowToSimulateBargainingFailure.BothHaveNoChoiceAndMustBargain,
                NumPotentialBargainingRounds = numBargainingRounds,
                ForgetEarlierBargainingRounds = forgetEarlierBargainingRounds,
                SubdivideOffers = subdivideOffers,
                BargainingRoundsSimultaneous = simultaneousBargainingRounds,
                PGoesFirstIfNotSimultaneous = new List<bool> { true, false, true, false, true, false, true, false },
                IncludeSignalsReport = true
            };
            options.PUtilityCalculator = new RiskNeutralUtilityCalculator() { InitialWealth = options.PInitialWealth };
            options.DUtilityCalculator = new RiskNeutralUtilityCalculator() { InitialWealth = options.DInitialWealth };
            return options;
        }

        private string ConstructExpectedResolutionSet_CaseSettles(bool actionIsNoiseNotSignal, byte litigationQuality, bool pFiles, bool dAnswers, HowToSimulateBargainingFailure simulatingBargainingFailure,
            List<(byte? pMove, byte? dMove)> bargainingRounds, bool simultaneousBargainingRounds,
            bool allowAbandonAndDefault)
        {
            return ConstructExpectedResolutionSet(actionIsNoiseNotSignal, litigationQuality, pFiles, dAnswers, simulatingBargainingFailure, bargainingRounds, simultaneousBargainingRounds, true, allowAbandonAndDefault,
                false, false, false, false, 0);
        }

        private string ConstructExpectedResolutionSet(bool actionIsNoiseNotSignal, byte litigationQuality, bool pFiles, bool dAnswers, HowToSimulateBargainingFailure simulatingBargainingFailure, List<(byte? pMove, byte? dMove)> bargainingRounds, bool simultaneousBargainingRounds, bool settlementReachedLastRound, bool allowAbandonAndDefault, bool pReadyToAbandonLastRound, bool dReadyToDefaultLastRound, bool ifBothDefaultPlaintiffLoses, bool caseGoesToTrial, byte courtResultAtTrial)
        {
            List<byte> l = new List<byte>();
            
            if (actionIsNoiseNotSignal)
                l.Add(litigationQuality); // the resolution set must contain the actual litigation quality if we're using noise actions, so then the court can determine what to do based on its own noise value. Not so when actions are signals, in which case the action the court receives simply determines its decision and is based on an uneven chance probabilities decision that doesn't need to be part of the resolution set.
            if (pFiles)
            {
                l.Add(1);
                if (dAnswers)
                {
                    l.Add(1);
                    byte bargainingRound = 1;
                    int numBargainingRoundsCompleted = bargainingRounds.Count();
                    foreach (var moveSet in bargainingRounds)
                    {
                        bool isLastBargainedRound = bargainingRound == numBargainingRoundsCompleted;
                        // Note that the resolution information set always consists of just the last round settled, regardless of whether the case goes to trial. Some of this information may be irrelevant to trial (if we're not doing the settlement shootout).
                        if (isLastBargainedRound)
                        {
                            l.Add(bargainingRound);
                            bool pAgreesToBargain = moveSet.pMove != null;
                            bool dAgreesToBargain = moveSet.dMove != null;
                            if (simulatingBargainingFailure != HowToSimulateBargainingFailure.BothHaveNoChoiceAndMustBargain)
                            {
                                l.Add(pAgreesToBargain ? (byte) 1 : (byte) 2);
                                l.Add(dAgreesToBargain ? (byte) 1 : (byte) 2);
                            }
                            if (pAgreesToBargain && dAgreesToBargain)
                            {
                                bool pMovesFirst = (bargainingRound % 2 == 1) || simultaneousBargainingRounds;
                                if (pMovesFirst)
                                {
                                    l.Add((byte) moveSet.pMove);
                                    l.Add((byte) moveSet.dMove);
                                }
                                else
                                {
                                    l.Add((byte) moveSet.dMove);
                                    l.Add((byte) moveSet.pMove);
                                }
                            }
                            if (allowAbandonAndDefault && !settlementReachedLastRound)
                            {
                                l.Add(pReadyToAbandonLastRound ? (byte) 1 : (byte) 2);
                                l.Add(dReadyToDefaultLastRound ? (byte) 1 : (byte) 2);
                                if (pReadyToAbandonLastRound && dReadyToDefaultLastRound)
                                    l.Add(ifBothDefaultPlaintiffLoses ? (byte) 1 : (byte) 2);
                            }
                            break; // we don't need to enter/track anything about later bargaining rounds, since they didn't occur
                        }
                        bargainingRound++;
                    }
                    if (caseGoesToTrial)
                        l.Add(courtResultAtTrial);
                }
                else
                    l.Add(2); // d doesn't answer
            }
            else
                l.Add(2); // p doesn't file
            return String.Join(",", l);
        }

        private List<(byte? pMove, byte? dMove)> GetBargainingRoundMoves(bool simultaneousBargaining, byte numRoundsToInclude, bool settlementInLastRound, HowToSimulateBargainingFailure simulatingBargainingFailure, out List<(byte offerMove, byte bargainingRoundNumber, bool isOfferToP)> offers)
        {
            offers = new List<(byte offerMove, byte bargainingRoundNumber, bool isOfferToP)>(); // we are also going to track offers; note that not every move is an offer
            bool ifNotSettlingPRefusesToBargain = simulatingBargainingFailure == HowToSimulateBargainingFailure.PRefusesToBargain || simulatingBargainingFailure == HowToSimulateBargainingFailure.BothRefuseToBargain;
            bool ifNotSettlingDRefusesToBargain = simulatingBargainingFailure == HowToSimulateBargainingFailure.DRefusesToBargain || simulatingBargainingFailure == HowToSimulateBargainingFailure.BothRefuseToBargain;
            List<(byte? pMove, byte? dMove)> moves = new List<(byte? pMove, byte? dMove)>();
            for (byte b = 1; b <= numRoundsToInclude; b++)
            {
                bool settlingThisRound = settlementInLastRound && b == numRoundsToInclude;
                if (!settlingThisRound && (ifNotSettlingPRefusesToBargain || ifNotSettlingDRefusesToBargain))
                {
                    // Note that if one player refuses to bargain, then the other party's move will be ignored -- hence, the use of the dummy 255 here
                    moves.Add((ifNotSettlingPRefusesToBargain ? (byte?) null : (byte?) 255, ifNotSettlingDRefusesToBargain ? (byte?) null : (byte?)255));
                }
                else
                {
                    if (simultaneousBargaining)
                    {
                        if (settlingThisRound)
                            moves.Add((ValueWhenCaseSettles, (byte) ValueWhenCaseSettles));
                        else
                        { // we will have different ways of making settlement fail -- this will help us when testing regret aversion. The plaintiff may be regretful based on the generous offer from the defendant in bargaining round 2, and the defendant may be regretful based on the generous offer from the plaintiff in bargaining round 1.
                            if (b == 1)
                                moves.Add(((byte) (ValueWhenCaseSettles - 1), (byte) (ValueWhenCaseSettles - 2)));
                            else
                                moves.Add(((byte)(ValueWhenCaseSettles + 2), (byte)(ValueWhenCaseSettles + 1)));
                        }
                        offers.Add(((byte) moves.Last().pMove, b, false));
                        offers.Add(((byte)moves.Last().dMove, b, true));
                    }
                    else
                    {
                        byte offer = ValueWhenCaseSettles;
                        byte response = settlingThisRound ? (byte) 1 : (byte) 2;
                        bool plaintiffIsOffering = b % 2 == 1; // plaintiff offers in first round, which is odd numbered
                        if (plaintiffIsOffering)
                            moves.Add((offer, response));
                        else
                            moves.Add((response, offer));
                        offers.Add((offer, b, !plaintiffIsOffering));
                    }
                }
            }
            return moves;
        }

        private (double? bestRejectedOfferToP, double? bestRejectedOfferToD) GetBestOffers(
            List<(byte offerMove, byte bargainingRoundNumber, bool isOfferToP)> offers)
        {
            Br.eak.IfAdded("A");
            (double? bestRejectedOfferToP, double? bestRejectedOfferToD) bestOffers = (null, null);
            foreach ((byte offerMove, byte bargainingRoundNumber, bool isOfferToP) offer in offers)
            {
                if (offer.isOfferToP)
                {
                    double offerToP = EquallySpaced.GetLocationOfEquallySpacedPoint((byte) (offer.offerMove - 1), NumDistinctPoints);
                    double roundAdjustedOfferToP = InitialWealth + offerToP * DamagesAlleged - PFileCost - offer.bargainingRoundNumber * PerRoundBargainingCost;
                    if (bestOffers.bestRejectedOfferToP == null || roundAdjustedOfferToP > bestOffers.bestRejectedOfferToP)
                        bestOffers.bestRejectedOfferToP =  roundAdjustedOfferToP;
                }
                else
                { 
                    double offerToD = EquallySpaced.GetLocationOfEquallySpacedPoint((byte)(offer.offerMove - 1), NumDistinctPoints);
                    double roundAdjustedOfferToD = InitialWealth - offerToD * DamagesAlleged - DAnswerCost - offer.bargainingRoundNumber * PerRoundBargainingCost; // note that this is negative, because it's the change in wealth for D
                    if (bestOffers.bestRejectedOfferToD == null || roundAdjustedOfferToD > bestOffers.bestRejectedOfferToD)
                        bestOffers.bestRejectedOfferToD = roundAdjustedOfferToD;
                }
            }
            return bestOffers;
        }

        private (string pInformationSet, string dInformationSet) GetExpectedPartyInformationSets(bool actionIsNoiseNotSignal, byte litigationQuality, byte pNoise, byte dNoise, HowToSimulateBargainingFailure simulatingBargainingFailure,
            List<(byte? pMove, byte? dMove)> bargainingMoves, bool forgetEarlierBargainingRounds, bool simultaneousBargaining, bool subdivideOffers)
        {
            byte pSignal, dSignal;
            if (actionIsNoiseNotSignal)
            {
                double litigationQualityUniform =
                    EquallySpaced.GetLocationOfEquallySpacedPoint(litigationQuality - 1, NumDistinctPoints);
                MyGame.ConvertNoiseActionToDiscreteAndUniformSignal(pNoise, litigationQualityUniform, NumDistinctPoints, PartyNoise, NumDistinctPoints, out pSignal, out _);
                MyGame.ConvertNoiseActionToDiscreteAndUniformSignal(dNoise, litigationQualityUniform, NumDistinctPoints, PartyNoise, NumDistinctPoints, out dSignal, out _);
            }
            else
            {
                pSignal = pNoise;
                dSignal = dNoise;
            }
            List<byte> pInfo = new List<byte>() { pSignal };
            List<byte> dInfo = new List<byte>() { dSignal };
            int bargainingRoundCount = bargainingMoves.Count();
            byte startingRound = forgetEarlierBargainingRounds ? (byte) bargainingRoundCount : (byte) 1;
            if (bargainingRoundCount != 0)
                for (byte b = startingRound; b <= bargainingRoundCount; b++)
                {
                    pInfo.Add(b);
                    dInfo.Add(b);
                    (byte? pMove, byte? dMove) = bargainingMoves[b - 1];
                    if (simulatingBargainingFailure != HowToSimulateBargainingFailure.BothHaveNoChoiceAndMustBargain)
                    {
                        dInfo.Add(pMove == null ? (byte) 2 : (byte) 1);
                        pInfo.Add(dMove == null ? (byte) 2 : (byte) 1);
                    }
                    if (pMove != null && dMove != null)
                    {
                        if (simultaneousBargaining)
                        {
                            if (subdivideOffers)
                            {
                                // Note: The reason these are both at the beginning of the decision is that the decision is deferred
                                pInfo.Add(GameHistory.EndDetourMarker);
                                dInfo.Add(GameHistory.EndDetourMarker);
                            }
                            dInfo.Add((byte)pMove);
                            pInfo.Add((byte) dMove);
                        }
                        else
                        {
                            if (b % 2 == 1)
                            {
                                // plaintiff offers
                                dInfo.Add((byte) pMove);
                                if (subdivideOffers)
                                    pInfo.Add(GameHistory.EndDetourMarker); // the first end detour marker comes to P after P's move.
                                pInfo.Add((byte) dMove); // not a subdivision decision
                            }
                            else
                            {
                                // defendant offers
                                pInfo.Add((byte)dMove);
                                if (subdivideOffers)
                                    dInfo.Add(GameHistory.EndDetourMarker); // the first end detour marker comes to P after P's move.
                                dInfo.Add((byte)pMove); // not a subdivision decision
                            }
                        }
                    }
                }
            return (String.Join(",", pInfo), String.Join(",", dInfo));
        }

        private Func<Decision, GameProgress, byte> GetPlayerActions(bool pFiles, bool dAnswers, byte litigationQuality, byte pSignalOrNoise, byte dSignalOrNoise, HowToSimulateBargainingFailure simulatingBargainingFailure, List<(byte? pMove, byte? dMove)> bargainingRoundMoves, bool simultaneousBargainingRounds, byte? pReadyToAbandonRound = null, byte? dReadyToDefaultRound = null, byte mutualGiveUpResult = 0, byte courtResult = 0)
        {
            var bargaining = new List<(byte decision, byte customInfo, byte action)>();
            for (byte b = 1; b <= bargainingRoundMoves.Count(); b++)
            {
                var bargainingRoundMove = bargainingRoundMoves[b - 1];
                var pMove = bargainingRoundMove.pMove;
                var dMove = bargainingRoundMove.dMove;
                if (simulatingBargainingFailure != HowToSimulateBargainingFailure.BothHaveNoChoiceAndMustBargain)
                {
                    bargaining.Add(((byte) MyGameDecisions.PAgreeToBargain, b, pMove == null ? (byte) 2 : (byte) 1));
                    bargaining.Add(((byte)MyGameDecisions.DAgreeToBargain, b, dMove == null ? (byte)2 : (byte)1));
                }
                if (pMove != null && dMove != null)
                {
                    if (simultaneousBargainingRounds)
                    {
                        bargaining.Add(((byte) MyGameDecisions.POffer, b, (byte) pMove));
                        bargaining.Add(((byte) MyGameDecisions.DOffer, b, (byte) dMove));
                    }
                    else
                    {
                        if (b % 2 == 1)
                        {
                            bargaining.Add(((byte) MyGameDecisions.POffer, b, (byte) pMove));
                            bargaining.Add(((byte) MyGameDecisions.DResponse, b, (byte) dMove));
                        }
                        else
                        {
                            bargaining.Add(((byte) MyGameDecisions.DOffer, b, (byte) dMove));
                            bargaining.Add(((byte) MyGameDecisions.PResponse, b, (byte) pMove));
                        }
                    }
                }
                bargaining.Add(((byte)MyGameDecisions.PAbandon, b, pReadyToAbandonRound == b ? (byte)1 : (byte)2));
                bargaining.Add(((byte)MyGameDecisions.DDefault, b, dReadyToDefaultRound == b ? (byte)1 : (byte)2));
            }
            var actionsToPlay = DefineActions.ForTest(
                new List<(byte decision, byte action)>()
                {
                    ((byte) MyGameDecisions.PFile, pFiles ? (byte) 1 : (byte) 2),
                    ((byte) MyGameDecisions.DAnswer, dAnswers ? (byte) 1 : (byte) 2),
                    ((byte) MyGameDecisions.LitigationQuality, litigationQuality),
                    ((byte) MyGameDecisions.PNoiseOrSignal, pSignalOrNoise),
                    ((byte) MyGameDecisions.DNoiseOrSignal, dSignalOrNoise),
                    ((byte) MyGameDecisions.PreBargainingRound, (byte)1 /* only action -- dummy decision */),
                    ((byte) MyGameDecisions.DNoiseOrSignal, dSignalOrNoise),
                    ((byte)MyGameDecisions.MutualGiveUp, mutualGiveUpResult), // we'll only reach this if both try to give up, so it won't be called in multiple bargaining rounds
                    ((byte)MyGameDecisions.PostBargainingRound, 1 /* only action */),
                    ((byte) MyGameDecisions.CourtDecision, courtResult),
                },
                bargaining
                );
            return actionsToPlay;
        }

        [TestMethod]
        public void CaseGivenUp()
        {
            // settings
            CaseNumber = 0;
            for (byte numPotentialBargainingRounds = 1; numPotentialBargainingRounds <= 3; numPotentialBargainingRounds++)
                foreach (bool subdivideOffers in new bool[] { false, true })
                    foreach (bool forgetEarlierBargainingRounds in new bool[] { true, false })
                foreach (bool simultaneousBargainingRounds in new bool[] { true, false })
                foreach (bool actionIsNoiseNotSignal in new bool[] {true, false})
                foreach (LoserPaysPolicy loserPaysPolicy in new LoserPaysPolicy[] { LoserPaysPolicy.NoLoserPays, LoserPaysPolicy.AfterTrialOnly, LoserPaysPolicy.EvenAfterAbandonOrDefault, })
                foreach (HowToSimulateBargainingFailure simulatingBargainingFailure in new HowToSimulateBargainingFailure[] { HowToSimulateBargainingFailure.PRefusesToBargain, HowToSimulateBargainingFailure.DRefusesToBargain, HowToSimulateBargainingFailure.BothRefuseToBargain, HowToSimulateBargainingFailure.BothAgreeToBargain, HowToSimulateBargainingFailure.BothHaveNoChoiceAndMustBargain })
                {
                    CaseGivenUpVariousActions(numPotentialBargainingRounds, subdivideOffers, forgetEarlierBargainingRounds, simultaneousBargainingRounds, actionIsNoiseNotSignal, loserPaysPolicy, simulatingBargainingFailure);
                }
        }

        private void CaseGivenUpVariousActions(byte numPotentialBargainingRounds, bool subdivideOffers, bool forgetEarlierBargainingRounds,
            bool simultaneousBargainingRounds, bool actionIsNoiseNotSignal, LoserPaysPolicy loserPaysPolicy, HowToSimulateBargainingFailure simulatingBargainingFailure)
        {
            try
            {
                for (byte abandonmentInRound = 0;
                    abandonmentInRound <= numPotentialBargainingRounds;
                    abandonmentInRound++)
                    foreach (bool plaintiffGivesUp in new bool[] { true, false })
                        foreach (bool defendantGivesUp in new bool[] { true, false })
                            foreach (bool plaintiffWinsIfBothGiveUp in new bool[] { true, false })
                            {
                                if (!plaintiffGivesUp && !defendantGivesUp)
                                    continue; // not interested in this case
                                if ((!plaintiffGivesUp || !defendantGivesUp) && !plaintiffWinsIfBothGiveUp)
                                    continue; // only need to test both values of plaintiff wins if both give up.
                                if (CaseNumber == 3404)
                                {
                                    GameProgressLogger.LoggingOn = true;
                                    GameProgressLogger.OutputLogMessages = true;
                                }
                                else
                                {
                                    GameProgressLogger.LoggingOn = false;
                                    GameProgressLogger.OutputLogMessages = false;
                                }
                                CaseGivenUp_SpecificSettingsAndActions(numPotentialBargainingRounds, subdivideOffers, abandonmentInRound,
                                    forgetEarlierBargainingRounds,
                                    simultaneousBargainingRounds, actionIsNoiseNotSignal, (byte)LitigationQuality,
                                    pReadyToAbandonRound: plaintiffGivesUp ? (byte?)abandonmentInRound : (byte?)null,
                                    dReadyToDefaultRound: defendantGivesUp ? (byte?)abandonmentInRound : (byte?)null,
                                    mutualGiveUpResult: plaintiffWinsIfBothGiveUp ? (byte)2 : (byte)1,
                                    loserPaysPolicy: loserPaysPolicy,
                                    simulatingBargainingFailure: simulatingBargainingFailure);
                                CaseNumber++;
                            }
            }
            catch (Exception e)
            {
                throw new Exception($"Case number {CaseNumber} failed: {e.Message}");
            }
        }

        public void CaseGivenUp_SpecificSettingsAndActions(byte numPotentialBargainingRounds, bool subdivideOffers, byte? abandonmentInRound, bool forgetEarlierBargainingRounds, bool simultaneousBargainingRounds, bool actionIsNoiseNotSignal, byte litigationQuality, byte? pReadyToAbandonRound, byte? dReadyToDefaultRound, byte mutualGiveUpResult, LoserPaysPolicy loserPaysPolicy, HowToSimulateBargainingFailure simulatingBargainingFailure)
        {
            var bargainingRoundMoves = GetBargainingRoundMoves(simultaneousBargainingRounds,
                abandonmentInRound ?? numPotentialBargainingRounds, false, simulatingBargainingFailure, out var offers);
            var bestOffers = GetBestOffers(offers);
            byte numActualRounds = (byte)bargainingRoundMoves.Count();
            var options = GetGameOptions(allowAbandonAndDefaults: true, numBargainingRounds: numPotentialBargainingRounds, subdivideOffers:subdivideOffers, forgetEarlierBargainingRounds: forgetEarlierBargainingRounds, simultaneousBargainingRounds: simultaneousBargainingRounds, actionIsNoiseNotSignal: actionIsNoiseNotSignal, loserPaysPolicy: loserPaysPolicy, simulatingBargainingFailure: simulatingBargainingFailure);
            bool pFiles = pReadyToAbandonRound != 0;
            bool dAnswers = pFiles && dReadyToDefaultRound != 0;
            var actionsToPlay = GetPlayerActions(pFiles: pFiles, dAnswers: dAnswers, litigationQuality: litigationQuality, pSignalOrNoise: PSignalOrNoise,
                dSignalOrNoise: DSignalOrNoise, bargainingRoundMoves: bargainingRoundMoves, simultaneousBargainingRounds: simultaneousBargainingRounds, pReadyToAbandonRound: pReadyToAbandonRound, dReadyToDefaultRound: dReadyToDefaultRound, mutualGiveUpResult: mutualGiveUpResult, simulatingBargainingFailure: simulatingBargainingFailure);
            var myGameProgress = MyGameRunner.PlayMyGameOnce(options, actionsToPlay);

            bool pWins = (pReadyToAbandonRound == null && dReadyToDefaultRound != null) ||
                         (pReadyToAbandonRound != null && dReadyToDefaultRound != null && pFiles && mutualGiveUpResult == (byte) 2);

            double damages = pWins ? options.DamagesAlleged : 0;

            myGameProgress.GameComplete.Should().BeTrue();
            myGameProgress.CaseSettles.Should().BeFalse();
            if (pReadyToAbandonRound == 0)
            {
                myGameProgress.PFiles.Should().Be(false);
                myGameProgress.DAnswers.Should().Be(false);
                myGameProgress.PAbandons.Should().Be(false);
                myGameProgress.DDefaults.Should().Be(false);
            }
            else if (dReadyToDefaultRound == 0)
            {
                myGameProgress.PFiles.Should().Be(true);
                myGameProgress.DAnswers.Should().Be(false);
                myGameProgress.PAbandons.Should().Be(false);
                myGameProgress.DDefaults.Should().Be(false);
            }
            else
            {
                myGameProgress.PAbandons.Should().Be(!pWins);
                myGameProgress.DDefaults.Should().Be(pWins);
            }
            double pExpenses, dExpenses;
            if (myGameProgress.PFiles && myGameProgress.DAnswers)
            {
                double pInitialExpenses = options.PFilingCost + numActualRounds * options.PerPartyCostsLeadingUpToBargainingRound;
                double dInitialExpenses = options.DAnswerCost + numActualRounds * options.PerPartyCostsLeadingUpToBargainingRound;
                GetExpensesAfterFeeShifting(options, true, pWins, pInitialExpenses, dInitialExpenses, out pExpenses, out dExpenses);
            }
            else
            { // either p didn't file or p filed and d didn't answer
                pExpenses = myGameProgress.PFiles ? options.PFilingCost : 0;
                dExpenses = 0;
            }


            double pFinalWealthExpected = options.PInitialWealth + damages - pExpenses;
            double dFinalWealthExpected = options.DInitialWealth - damages - dExpenses;
            CheckFinalWelfare(myGameProgress, pFinalWealthExpected, dFinalWealthExpected, bestOffers);

            //var informationSetHistories = myGameProgress.GameHistory.GetInformationSetHistoryItems().ToList();
            GetInformationSetStrings(myGameProgress, out string pInformationSet, out string dInformationSet, out string resolutionSet);
            var expectedPartyInformationSets = GetExpectedPartyInformationSets(actionIsNoiseNotSignal, LitigationQuality, PSignalOrNoise, DSignalOrNoise, simulatingBargainingFailure, bargainingRoundMoves, forgetEarlierBargainingRounds, simultaneousBargainingRounds, subdivideOffers);
            string expectedResolutionSet = ConstructExpectedResolutionSet(pFiles: pFiles, dAnswers: dAnswers,
                bargainingRounds: bargainingRoundMoves, simultaneousBargainingRounds: simultaneousBargainingRounds, actionIsNoiseNotSignal: actionIsNoiseNotSignal, litigationQuality: litigationQuality, settlementReachedLastRound: false, allowAbandonAndDefault: true, pReadyToAbandonLastRound: pReadyToAbandonRound != null, dReadyToDefaultLastRound: dReadyToDefaultRound != null, ifBothDefaultPlaintiffLoses: mutualGiveUpResult == (byte)1, caseGoesToTrial: false, courtResultAtTrial: 0, simulatingBargainingFailure:simulatingBargainingFailure);
            pInformationSet.Should().Be(expectedPartyInformationSets.pInformationSet);
            dInformationSet.Should().Be(expectedPartyInformationSets.dInformationSet);
            resolutionSet.Should().Be(expectedResolutionSet);
        }

        private void GetExpensesAfterFeeShifting(MyGameOptions options, bool loserPaysAfterAbandonmentRequired, bool pWins, double pInitialExpenses, double dInitialExpenses, out double pExpenses, out double dExpenses)
        {
            if (options.LoserPays && (!loserPaysAfterAbandonmentRequired || options.LoserPaysAfterAbandonment))
            {
                if (pWins)
                {
                    pExpenses = pInitialExpenses - options.LoserPaysMultiple * pInitialExpenses;
                    dExpenses = dInitialExpenses + options.LoserPaysMultiple * pInitialExpenses;
                }
                else
                {
                    pExpenses = pInitialExpenses + options.LoserPaysMultiple * dInitialExpenses;
                    dExpenses = dInitialExpenses - options.LoserPaysMultiple * dInitialExpenses;
                }
            }
            else
            {
                // American rule
                pExpenses = pInitialExpenses;
                dExpenses = dInitialExpenses;
            }
        }

        [TestMethod]
        public void SettlingCase()
        {
            CaseNumber = 0;
            try
            {
                for (byte numPotentialBargainingRounds = 1; numPotentialBargainingRounds <= 3; numPotentialBargainingRounds++)
                    foreach (bool subdivideOffers in new bool[] { false, true })
                        foreach (bool forgetEarlierBargainingRounds in new bool[] { true, false })
                    foreach (bool simultaneousBargainingRounds in new bool[] { true, false })
                    foreach (bool allowAbandonAndDefault in new bool[] { true, false })
                    foreach (bool actionIsNoiseNotSignal in new bool[] { true, false })
                    foreach (LoserPaysPolicy loserPaysPolicy in new LoserPaysPolicy[] { LoserPaysPolicy.NoLoserPays, LoserPaysPolicy.AfterTrialOnly, LoserPaysPolicy.EvenAfterAbandonOrDefault, })
                    foreach (HowToSimulateBargainingFailure simulatingBargainingFailure in new HowToSimulateBargainingFailure[] { HowToSimulateBargainingFailure.PRefusesToBargain, HowToSimulateBargainingFailure.DRefusesToBargain, HowToSimulateBargainingFailure.BothRefuseToBargain, HowToSimulateBargainingFailure.BothAgreeToBargain, HowToSimulateBargainingFailure.BothHaveNoChoiceAndMustBargain })
                        for (byte settlementInRound = 1; settlementInRound <= numPotentialBargainingRounds; settlementInRound++)
                        {
                            if (CaseNumber == 9999999)
                            {
                                GameProgressLogger.LoggingOn = true;
                                GameProgressLogger.OutputLogMessages = true;
                            }
                            SettlingCase_Helper(numPotentialBargainingRounds, subdivideOffers, settlementInRound, forgetEarlierBargainingRounds, simultaneousBargainingRounds, allowAbandonAndDefault, actionIsNoiseNotSignal, loserPaysPolicy, simulatingBargainingFailure);
                            CaseNumber++;
                        }
            }
            catch (Exception e)
            {
                throw new Exception($"Failed at case number {CaseNumber}. Inner exception: {e.Message}", e);
            }
        }

        public void SettlingCase_Helper(byte numPotentialBargainingRounds, bool subdivideOffers, byte? settlementInRound, bool forgetEarlierBargainingRounds, bool simultaneousBargainingRounds, bool allowAbandonAndDefault, bool actionIsNoiseNotSignal, LoserPaysPolicy loserPaysPolicy, HowToSimulateBargainingFailure simulatingBargainingFailure)
        {
            var bargainingRoundMoves = GetBargainingRoundMoves(simultaneousBargainingRounds,
                settlementInRound ?? numPotentialBargainingRounds, settlementInRound != null, simulatingBargainingFailure, out var offers);
            var bestOffers = GetBestOffers(offers);
            byte numActualRounds = (byte) bargainingRoundMoves.Count();
            var options = GetGameOptions(allowAbandonAndDefaults: allowAbandonAndDefault, numBargainingRounds: numPotentialBargainingRounds, subdivideOffers: subdivideOffers, forgetEarlierBargainingRounds: forgetEarlierBargainingRounds, simultaneousBargainingRounds: simultaneousBargainingRounds, actionIsNoiseNotSignal:actionIsNoiseNotSignal, loserPaysPolicy: loserPaysPolicy, simulatingBargainingFailure: simulatingBargainingFailure);
            var actionsToPlay = GetPlayerActions(pFiles: true, dAnswers: true, litigationQuality: LitigationQuality, pSignalOrNoise: PSignalOrNoise,
                dSignalOrNoise: DSignalOrNoise, simulatingBargainingFailure: simulatingBargainingFailure, bargainingRoundMoves: bargainingRoundMoves, simultaneousBargainingRounds: simultaneousBargainingRounds);
            var myGameProgress = MyGameRunner.PlayMyGameOnce(options, actionsToPlay);

            double settlementProportion = EquallySpaced.GetLocationOfEquallySpacedPoint((byte) (ValueWhenCaseSettles - 1), NumDistinctPoints);

            myGameProgress.GameComplete.Should().BeTrue();
            myGameProgress.CaseSettles.Should().BeTrue();
            double pFinalWealthExpected = options.PInitialWealth - options.PFilingCost + settlementProportion * options.DamagesAlleged - numActualRounds * options.PerPartyCostsLeadingUpToBargainingRound;
            double dFinalWealthExpected = options.DInitialWealth - options.DAnswerCost - settlementProportion * options.DamagesAlleged -
                                          numActualRounds * options.PerPartyCostsLeadingUpToBargainingRound;
            CheckFinalWelfare(myGameProgress, pFinalWealthExpected, dFinalWealthExpected, bestOffers);

            //var informationSetHistories = myGameProgress.GameHistory.GetInformationSetHistoryItems().ToList();
            GetInformationSetStrings(myGameProgress, out string pInformationSet, out string dInformationSet, out string resolutionSet);
            var expectedPartyInformationSets = GetExpectedPartyInformationSets(actionIsNoiseNotSignal, LitigationQuality, PSignalOrNoise, DSignalOrNoise, simulatingBargainingFailure, bargainingRoundMoves, forgetEarlierBargainingRounds, simultaneousBargainingRounds, subdivideOffers);
            string expectedResolutionSet = ConstructExpectedResolutionSet_CaseSettles(pFiles: true, dAnswers: true, simulatingBargainingFailure: simulatingBargainingFailure,
                bargainingRounds: bargainingRoundMoves, simultaneousBargainingRounds: simultaneousBargainingRounds, actionIsNoiseNotSignal:actionIsNoiseNotSignal, litigationQuality:LitigationQuality, allowAbandonAndDefault: allowAbandonAndDefault);
            pInformationSet.Should().Be(expectedPartyInformationSets.pInformationSet);
            dInformationSet.Should().Be(expectedPartyInformationSets.dInformationSet);
            resolutionSet.Should().Be(expectedResolutionSet);
        }

        [TestMethod]
        public void CaseTried()
        {
            CaseNumber = 0;
            try
            {
                foreach (bool allowAbandonAndDefaults in new[] { true, false })
                    foreach (bool forgetEarlierBargainingRounds in new[] { true, false })
                        foreach (byte numBargainingRounds in new byte[] { 1, 2 })
                        foreach (bool subdivideOffers in new[] { false, true })
                                foreach (bool plaintiffWins in new[] { true, false })
                                foreach (bool simultaneousBargainingRounds in new[] { true, false })
                                    foreach (bool actionIsNoiseNotSignal in new[] { true, false })
                                        foreach (var loserPaysPolicy in new[] { LoserPaysPolicy.NoLoserPays, LoserPaysPolicy.AfterTrialOnly, LoserPaysPolicy.EvenAfterAbandonOrDefault })
                                            foreach (var simulatingBargainingFailure in new[] { HowToSimulateBargainingFailure.PRefusesToBargain, HowToSimulateBargainingFailure.DRefusesToBargain, HowToSimulateBargainingFailure.BothRefuseToBargain, HowToSimulateBargainingFailure.BothAgreeToBargain, HowToSimulateBargainingFailure.BothHaveNoChoiceAndMustBargain })
                                            {
                                                if (CaseNumber == 999999)
                                                {
                                                    GameProgressLogger.LoggingOn = true;
                                                    GameProgressLogger.OutputLogMessages = true;
                                                }
                                                else
                                                {
                                                    GameProgressLogger.LoggingOn = false;
                                                    GameProgressLogger.OutputLogMessages = false;
                                                    }
                                                CaseTried_Helper(allowAbandonAndDefaults, forgetEarlierBargainingRounds, numBargainingRounds, subdivideOffers, plaintiffWins, simultaneousBargainingRounds, actionIsNoiseNotSignal, loserPaysPolicy, simulatingBargainingFailure);
                                                CaseNumber++;
                                            }
            }
            catch (Exception e)
            {
                throw new Exception($"Failed case number {CaseNumber}: {e.Message}");
            }
        }

        private void CaseTried_Helper(bool allowAbandonAndDefaults, bool forgetEarlierBargainingRounds, byte numBargainingRounds, bool subdivideOffers, bool plaintiffWins, bool simultaneousBargainingRounds, bool actionIsNoiseNotSignal, LoserPaysPolicy loserPaysPolicy, HowToSimulateBargainingFailure simulatingBargainingFailure)
        {
            var options = GetGameOptions(allowAbandonAndDefaults, numBargainingRounds, subdivideOffers, forgetEarlierBargainingRounds, simultaneousBargainingRounds, actionIsNoiseNotSignal, loserPaysPolicy, simulatingBargainingFailure);
            var bargainingMoves = GetBargainingRoundMoves(simultaneousBargainingRounds, numBargainingRounds, false, simulatingBargainingFailure, out var offers);
            var bestOffers = GetBestOffers(offers);
            byte courtResult;
            if (actionIsNoiseNotSignal)
                courtResult = plaintiffWins ? (byte) NumCourtNoiseValues : (byte) 1; // we've used a high value of court noise above, so if the court has its highest possible noise, it will definitely conclude plaintiff has won, and if the court has its lowest possible noise, it will definitely conclude that defendant has won
            else
                courtResult = plaintiffWins ? (byte) 2 : (byte) 1;
            var actions = GetPlayerActions(true, true, LitigationQuality, PSignalOrNoise, DSignalOrNoise, simulatingBargainingFailure, bargainingMoves, simultaneousBargainingRounds, null, null, 0, courtResult);
            var myGameProgress = MyGameRunner.PlayMyGameOnce(options, actions);
            myGameProgress.GameComplete.Should().BeTrue();
            double pExpenses, dExpenses;

            double pInitialExpenses = options.PFilingCost + numBargainingRounds * options.PerPartyCostsLeadingUpToBargainingRound + options.PTrialCosts;
            double dInitialExpenses = options.DAnswerCost + numBargainingRounds * options.PerPartyCostsLeadingUpToBargainingRound + options.DTrialCosts;
            GetExpensesAfterFeeShifting(options, false, plaintiffWins, pInitialExpenses, dInitialExpenses, out pExpenses, out dExpenses);

            double pFinalWealthExpected = options.PInitialWealth - pExpenses;
            double dFinalWealthExpected = options.DInitialWealth - dExpenses;
            if (plaintiffWins)
            {
                pFinalWealthExpected += options.DamagesAlleged;
                dFinalWealthExpected -= options.DamagesAlleged;
            }
            CheckFinalWelfare(myGameProgress, pFinalWealthExpected, dFinalWealthExpected, bestOffers);
            GetInformationSetStrings(myGameProgress, out string pInformationSet, out string dInformationSet,
                out string resolutionSet);
            var expectedPartyInformationSets = GetExpectedPartyInformationSets(actionIsNoiseNotSignal, LitigationQuality, PSignalOrNoise, DSignalOrNoise, simulatingBargainingFailure, bargainingMoves,
                forgetEarlierBargainingRounds, simultaneousBargainingRounds, subdivideOffers);
            var expectedResolutionSet = ConstructExpectedResolutionSet(actionIsNoiseNotSignal, LitigationQuality, true, true, simulatingBargainingFailure, bargainingMoves,
                simultaneousBargainingRounds, false, allowAbandonAndDefaults, false, false, false, true, courtResult);
            pInformationSet.Should().Be(expectedPartyInformationSets.pInformationSet);
            dInformationSet.Should().Be(expectedPartyInformationSets.dInformationSet);
            resolutionSet.Should().Be(expectedResolutionSet);
        }

        private static void CheckFinalWelfare(MyGameProgress myGameProgress, double pFinalWealthExpected, double dFinalWealthExpected, ValueTuple<double?, double?> bestOffers)
        {
            myGameProgress.PFinalWealth.Should().Be(pFinalWealthExpected);
            myGameProgress.DFinalWealth.Should().Be(dFinalWealthExpected);
            double pRegretAversionAdjustedWealth = myGameProgress.PFinalWealth;
            double dRegretAversionAdjustedWealth = myGameProgress.DFinalWealth;
            if (bestOffers.Item1 != null && bestOffers.Item1 > myGameProgress.PFinalWealth)
                pRegretAversionAdjustedWealth -= RegretAversion * ((double) bestOffers.Item1 - myGameProgress.PFinalWealth);
            if (bestOffers.Item2 != null && bestOffers.Item2 > myGameProgress.DFinalWealth)
                dRegretAversionAdjustedWealth -= RegretAversion * ((double) bestOffers.Item2 - myGameProgress.DFinalWealth);
            myGameProgress.PWelfare.Should().Be(pRegretAversionAdjustedWealth);
            myGameProgress.DWelfare.Should().Be(dRegretAversionAdjustedWealth);
        }
    }
}
