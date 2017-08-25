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

        private static void GetInformationSetStrings(MyGameProgress myGameProgress, out string pInformationSet,
            out string dInformationSet, out string resolutionSet)
        {
            pInformationSet = myGameProgress.GameHistory.GetPlayerInformationString((byte)MyGamePlayers.Plaintiff, null);
            dInformationSet = myGameProgress.GameHistory.GetPlayerInformationString((byte)MyGamePlayers.Defendant, null);
            resolutionSet = myGameProgress.GameHistory.GetPlayerInformationString((byte)MyGamePlayers.Resolution, null);
        }

        private const double PartyNoise = 0.2, InitialWealth = 1_000_000, DamagesAlleged = 100_000, TrialCosts = 5000, PerRoundBargainingCost = 1000;
        private const byte NumDistinctPoints = 5;
        private const byte LitigationQuality = 3;
        private const byte PSignalOrNoise = 5, DSignalOrNoise = 1; 

        private MyGameOptions GetGameOptions(bool allowAbandonAndDefaults, byte numBargainingRounds, bool forgetEarlierBargainingRounds, bool simultaneousBargainingRounds, bool actionIsNoiseNotSignal)
        {
            var options = new MyGameOptions()
            {
                PInitialWealth = InitialWealth,
                DInitialWealth = InitialWealth,
                DamagesAlleged = DamagesAlleged,
                NumLitigationQualityPoints = NumDistinctPoints,
                NumSignals = NumDistinctPoints,
                NumOffers = NumDistinctPoints,
                NumNoiseValues = NumDistinctPoints,
                ActionIsNoiseNotSignal = actionIsNoiseNotSignal,
                PNoiseStdev = PartyNoise,
                DNoiseStdev = PartyNoise,
                CourtNoiseStdev = 0.5,
                PTrialCosts = TrialCosts,
                DTrialCosts = TrialCosts,
                PerPartyBargainingRoundCosts = PerRoundBargainingCost,
                AllowAbandonAndDefaults = allowAbandonAndDefaults,
                DeltaOffersOptions = new DeltaOffersOptions()
                {
                    SubsequentOffersAreDeltas = false,
                    DeltaStartingValue = 0.01,
                    MaxDelta = 0.25
                },
                NumBargainingRounds = numBargainingRounds,
                ForgetEarlierBargainingRounds = forgetEarlierBargainingRounds,
                SubdivideOffers = false,
                BargainingRoundsSimultaneous = simultaneousBargainingRounds,
                PGoesFirstIfNotSimultaneous = new List<bool> { true, false, true, false, true, false, true, false },
                IncludeSignalsReport = true
            };
            options.PUtilityCalculator = new RiskNeutralUtilityCalculator() { InitialWealth = options.PInitialWealth };
            options.DUtilityCalculator = new RiskNeutralUtilityCalculator() { InitialWealth = options.DInitialWealth };
            return options;
        }

        private string ConstructExpectedResolutionSet_CaseSettles(bool actionIsNoiseNotSignal, byte litigationQuality, bool pFiles, bool dAnswers,
            List<(byte pMove, byte dMove)> bargainingRounds, bool simultaneousBargainingRounds,
            bool allowAbandonAndDefault)
        {
            return ConstructExpectedResolutionSet(actionIsNoiseNotSignal, litigationQuality, pFiles, dAnswers, bargainingRounds, simultaneousBargainingRounds, true, allowAbandonAndDefault,
                false, false, false, false, 0);
        }

        private string ConstructExpectedResolutionSet(bool actionIsNoiseNotSignal, byte litigationQuality, bool pFiles, bool dAnswers, List<(byte pMove, byte dMove)> bargainingRounds, bool simultaneousBargainingRounds, bool settlementReachedLastRound, bool allowAbandonAndDefault, bool pReadyToAbandonLastRound, bool dReadyToDefaultLastRound, bool ifBothDefaultPlaintiffLoses, bool caseGoesToTrial, byte courtResultAtTrial)
        {
            List<byte> l = new List<byte>();
            if (pFiles)
            {
                l.Add(1); 
                if (dAnswers)
                {
                    l.Add(1);
                    if (actionIsNoiseNotSignal)
                        l.Add(litigationQuality); // the resolution set must contain the actual litigation quality if we're using noise actions, so then the court can determine what to do based on its own noise value. Not so when actions are signals, in which case the action the court receives simply determines its decision and is based on an uneven chance probabilities decision that doesn't need to be part of the resolution set.
                    byte decisionIndex = (byte) MyGameDecisions.POffer; // this assumes that all earlier decisions in the list are in the game
                    int bargainingRound = 1;
                    int numBargainingRoundsCompleted = bargainingRounds.Count();
                    foreach (var moveSet in bargainingRounds)
                    {
                        bool isLastBargainedRound = bargainingRound == numBargainingRoundsCompleted;
                        // Note that the resolution information set always consists of just the last round settled, regardless of whether the case goes to trial. Some of this information may be irrelevant to trial (if we're not doing the settlement shootout).
                        if (isLastBargainedRound)
                        {
                            l.Add(decisionIndex);
                            bool pMovesFirst = (bargainingRound % 2 == 1) || simultaneousBargainingRounds;
                            if (pMovesFirst)
                                l.Add(moveSet.pMove);
                            l.Add(moveSet.dMove);
                            if (!pMovesFirst)
                                l.Add(moveSet.pMove);
                            if (allowAbandonAndDefault && !settlementReachedLastRound)
                            {
                                l.Add(pReadyToAbandonLastRound ? (byte)1 : (byte)2);
                                l.Add(dReadyToDefaultLastRound ? (byte)1 : (byte)2);
                                if (pReadyToAbandonLastRound && dReadyToDefaultLastRound)
                                    l.Add(ifBothDefaultPlaintiffLoses ? (byte)1 : (byte)2);
                            }
                            break; // we don't need to enter/track anything about later bargaining rounds, since they didn't occur
                        }
                        else
                        {
                            decisionIndex += 2;
                            if (allowAbandonAndDefault)
                                decisionIndex += 3; // since there is a later bargaining round. Note that we skip over the mutual abandonment decision, regardless of whether it is placed, since we're interested in the decision index, not only in played decisions
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

        private List<(byte pMove, byte dMove)> GetBargainingRoundMoves(bool simultaneousBargaining, byte numRoundsToInclude, bool settlementInLastRound)
        {
            List<(byte pMove, byte dMove)> moves = new List<(byte pMove, byte dMove)>();
            for (byte b = 1; b <= numRoundsToInclude; b++)
            {
                if (simultaneousBargaining)
                    moves.Add((3, settlementInLastRound && b == numRoundsToInclude ? (byte) 3 : (byte) 2));
                else
                {
                    byte offer = 3;
                    byte response = settlementInLastRound && b == numRoundsToInclude ? (byte)1 : (byte)2;
                    if (b % 2 == 1)
                        moves.Add((offer, response));
                    else
                        moves.Add((response, offer));
                }
            }
            return moves;
        }

        private (string pInformationSet, string dInformationSet) GetExpectedPartyInformationSets(bool actionIsNoiseNotSignal, byte litigationQuality, byte pNoise, byte dNoise,
            List<(byte pMove, byte dMove)> bargainingMoves, bool forgetEarlierBargainingRounds, bool simultaneousBargaining)
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
            if (!forgetEarlierBargainingRounds)
            {
                for (int b = 1; b <= bargainingMoves.Count(); b++)
                {
                    (byte pMove, byte dMove) = bargainingMoves[b - 1];
                    if (simultaneousBargaining)
                    {
                        pInfo.Add(dMove);
                        dInfo.Add(pMove);
                    }
                    else
                    {
                        bool isPOfferRound = b % 2 == 1;
                        if (isPOfferRound)
                            dInfo.Add(pMove);
                        else
                            pInfo.Add(dMove);
                    }
                }
            }
            return (String.Join(",", pInfo), String.Join(",", dInfo));
        }

        private Func<Decision, GameProgress, byte> GetPlayerActions(bool pFiles, bool dAnswers, byte litigationQuality, byte pSignalOrNoise, byte dSignalOrNoise, List<(byte pMove, byte dMove)> bargainingRoundMoves, bool simultaneousBargainingRounds, byte? pReadyToAbandonRound = null, byte? dReadyToDefaultRound = null, byte mutualGiveUpResult = 0, byte courtResult = 0)
        {
            var bargaining = new List<(byte decision, byte customInfo, byte action)>();
            for (byte b = 1; b <= bargainingRoundMoves.Count(); b++)
            {
                if (simultaneousBargainingRounds)
                {
                    bargaining.Add(((byte) MyGameDecisions.POffer, b, bargainingRoundMoves[b - 1].pMove));
                    bargaining.Add(((byte) MyGameDecisions.DOffer, b, bargainingRoundMoves[b - 1].dMove));
                }
                else
                {
                    if (b % 2 == 1)
                    {
                        bargaining.Add(((byte)MyGameDecisions.POffer, b, bargainingRoundMoves[b - 1].pMove));
                        bargaining.Add(((byte)MyGameDecisions.DResponse, b, bargainingRoundMoves[b - 1].dMove));
                    }
                    else
                    {
                        bargaining.Add(((byte)MyGameDecisions.DOffer, b, bargainingRoundMoves[b - 1].dMove));
                        bargaining.Add(((byte)MyGameDecisions.PResponse, b, bargainingRoundMoves[b - 1].pMove));
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
                    ((byte)MyGameDecisions.MutualGiveUp, mutualGiveUpResult), // we'll only reach this if both try to give up, so it won't be called in multiple bargaining rounds
                    ((byte) MyGameDecisions.CourtDecision, courtResult),
                },
                bargaining
                );
            return actionsToPlay;
        }

        // DEBUG -- must add abandon + default scenarios (probably a whole separate test), also not file and not answer scenarios


        [TestMethod]
        public void CaseGivenUp()
        {
            for (byte numPotentialBargainingRounds = 1; numPotentialBargainingRounds <= 3; numPotentialBargainingRounds++)
                for (byte abandonmentInRound = 1; abandonmentInRound <= numPotentialBargainingRounds; abandonmentInRound++)
                    foreach (bool forgetEarlierBargainingRounds in new bool[] { true, false })
                        foreach (bool simultaneousBargainingRounds in new bool[] { true, false })
                            foreach (bool actionIsNoiseNotSignal in new bool[] { true, false }) 
                                foreach (bool plaintiffGivesUp in new bool[] { true, false })
                                    foreach (bool defendantGivesUp in new bool[] { true, false })
                                        foreach (bool plaintiffWinsIfBothGiveUp in new bool[] {true, false})
                                        {
                                            if (!plaintiffGivesUp && !defendantGivesUp)
                                                continue; // not interested in this case
                                            if ((!plaintiffGivesUp || !defendantGivesUp) && !plaintiffWinsIfBothGiveUp)
                                                continue; // only need to test both values of plaintiff wins if both give up if both give up.
                                            CaseGivenUp_Helper(numPotentialBargainingRounds, abandonmentInRound, forgetEarlierBargainingRounds,
                                                simultaneousBargainingRounds, actionIsNoiseNotSignal, (byte)LitigationQuality, pReadyToAbandonRound: plaintiffGivesUp ? (byte?)abandonmentInRound : (byte?)null, dReadyToDefaultRound: defendantGivesUp ? (byte?)abandonmentInRound : (byte?)null, mutualGiveUpResult: plaintiffWinsIfBothGiveUp ? (byte)2 : (byte)1);
                                        }
        }

        public void CaseGivenUp_Helper(byte numPotentialBargainingRounds, byte? abandonmentInRound, bool forgetEarlierBargainingRounds, bool simultaneousBargainingRounds, bool actionIsNoiseNotSignal, byte litigationQuality, byte? pReadyToAbandonRound = null, byte? dReadyToDefaultRound = null, byte mutualGiveUpResult = 0)
        {
            var bargainingRoundMoves = GetBargainingRoundMoves(simultaneousBargainingRounds,
                abandonmentInRound ?? numPotentialBargainingRounds, false);
            byte numActualRounds = (byte)bargainingRoundMoves.Count();
            var options = GetGameOptions(allowAbandonAndDefaults: true, numBargainingRounds: numPotentialBargainingRounds, forgetEarlierBargainingRounds: forgetEarlierBargainingRounds, simultaneousBargainingRounds: simultaneousBargainingRounds, actionIsNoiseNotSignal: actionIsNoiseNotSignal);
            var actionsToPlay = GetPlayerActions(pFiles: true, dAnswers: true, litigationQuality: litigationQuality, pSignalOrNoise: PSignalOrNoise,
                dSignalOrNoise: DSignalOrNoise, bargainingRoundMoves: bargainingRoundMoves, simultaneousBargainingRounds: simultaneousBargainingRounds, pReadyToAbandonRound: pReadyToAbandonRound, dReadyToDefaultRound: dReadyToDefaultRound, mutualGiveUpResult: mutualGiveUpResult);
            var myGameProgress = MyGameRunner.PlayMyGameOnce(options, actionsToPlay);

            bool pWins = (pReadyToAbandonRound == null && dReadyToDefaultRound != null) ||
                         (pReadyToAbandonRound != null && dReadyToDefaultRound != null && mutualGiveUpResult == (byte) 2);

            double damages = pWins ? options.DamagesAlleged : 0;

            myGameProgress.GameComplete.Should().BeTrue();
            myGameProgress.CaseSettles.Should().BeFalse();
            myGameProgress.PAbandons.Should().Be(!pWins);
            myGameProgress.DDefaults.Should().Be(pWins);
            myGameProgress.PFinalWealth.Should().Be(options.PInitialWealth + damages -
                                                    numActualRounds * options.PerPartyBargainingRoundCosts);
            myGameProgress.DFinalWealth.Should().Be(options.DInitialWealth - damages -
                                                    numActualRounds * options.PerPartyBargainingRoundCosts);
            myGameProgress.PWelfare.Should().Be(myGameProgress.PFinalWealth);
            myGameProgress.DWelfare.Should().Be(myGameProgress.DFinalWealth);

            //var informationSetHistories = myGameProgress.GameHistory.GetInformationSetHistoryItems().ToList();
            GetInformationSetStrings(myGameProgress, out string pInformationSet, out string dInformationSet, out string resolutionSet);
            var expectedPartyInformationSets = GetExpectedPartyInformationSets(actionIsNoiseNotSignal, LitigationQuality, PSignalOrNoise, DSignalOrNoise, bargainingRoundMoves, forgetEarlierBargainingRounds, simultaneousBargainingRounds);
            string expectedResolutionSet = ConstructExpectedResolutionSet(pFiles: true, dAnswers: true,
                bargainingRounds: bargainingRoundMoves, simultaneousBargainingRounds: simultaneousBargainingRounds, actionIsNoiseNotSignal: actionIsNoiseNotSignal, litigationQuality: litigationQuality, settlementReachedLastRound: false, allowAbandonAndDefault: true, pReadyToAbandonLastRound: pReadyToAbandonRound != null, dReadyToDefaultLastRound: dReadyToDefaultRound != null, ifBothDefaultPlaintiffLoses: mutualGiveUpResult == (byte)1, caseGoesToTrial: false, courtResultAtTrial: 0);
            pInformationSet.Should().Be(expectedPartyInformationSets.pInformationSet);
            dInformationSet.Should().Be(expectedPartyInformationSets.dInformationSet);
            resolutionSet.Should().Be(expectedResolutionSet);
        }

        [TestMethod]
        public void SettlingCase()
        {
            for (byte numPotentialBargainingRounds = 1; numPotentialBargainingRounds <= 3; numPotentialBargainingRounds++)
                for (byte settlementInRound = 1; settlementInRound <= numPotentialBargainingRounds; settlementInRound++)
                    foreach (bool forgetEarlierBargainingRounds in new bool[] { true, false })
                        foreach (bool simultaneousBargainingRounds in new bool[] { true, false })
                            foreach (bool allowAbandonAndDefault in new bool[] { true, false })
                                foreach (bool actionIsNoiseNotSignal in new bool[] { true, false })
                                    SettlingCase_Helper(numPotentialBargainingRounds, settlementInRound, forgetEarlierBargainingRounds, simultaneousBargainingRounds, allowAbandonAndDefault, actionIsNoiseNotSignal);
        }

        public void SettlingCase_Helper(byte numPotentialBargainingRounds, byte? settlementInRound, bool forgetEarlierBargainingRounds, bool simultaneousBargainingRounds, bool allowAbandonAndDefault, bool actionIsNoiseNotSignal)
        {
            var bargainingRoundMoves = GetBargainingRoundMoves(simultaneousBargainingRounds,
                settlementInRound ?? numPotentialBargainingRounds, settlementInRound != null);
            byte numActualRounds = (byte) bargainingRoundMoves.Count();
            var options = GetGameOptions(allowAbandonAndDefaults: allowAbandonAndDefault, numBargainingRounds: numPotentialBargainingRounds, forgetEarlierBargainingRounds: forgetEarlierBargainingRounds, simultaneousBargainingRounds: simultaneousBargainingRounds, actionIsNoiseNotSignal:actionIsNoiseNotSignal);
            var actionsToPlay = GetPlayerActions(pFiles: true, dAnswers: true, litigationQuality: LitigationQuality, pSignalOrNoise: PSignalOrNoise,
                dSignalOrNoise: DSignalOrNoise, bargainingRoundMoves: bargainingRoundMoves, simultaneousBargainingRounds: simultaneousBargainingRounds);
            var myGameProgress = MyGameRunner.PlayMyGameOnce(options, actionsToPlay);

            myGameProgress.GameComplete.Should().BeTrue();
            myGameProgress.CaseSettles.Should().BeTrue();
            myGameProgress.PFinalWealth.Should().Be(options.PInitialWealth + 0.5 * options.DamagesAlleged -
                                                    numActualRounds * options.PerPartyBargainingRoundCosts);
            myGameProgress.DFinalWealth.Should().Be(options.DInitialWealth - 0.5 * options.DamagesAlleged -
                                                    numActualRounds * options.PerPartyBargainingRoundCosts);
            myGameProgress.PWelfare.Should().Be(myGameProgress.PFinalWealth);
            myGameProgress.DWelfare.Should().Be(myGameProgress.DFinalWealth);

            //var informationSetHistories = myGameProgress.GameHistory.GetInformationSetHistoryItems().ToList();
            GetInformationSetStrings(myGameProgress, out string pInformationSet, out string dInformationSet, out string resolutionSet);
            var expectedPartyInformationSets = GetExpectedPartyInformationSets(actionIsNoiseNotSignal, LitigationQuality, PSignalOrNoise, DSignalOrNoise, bargainingRoundMoves, forgetEarlierBargainingRounds, simultaneousBargainingRounds);
            string expectedResolutionSet = ConstructExpectedResolutionSet_CaseSettles(pFiles: true, dAnswers: true,
                bargainingRounds: bargainingRoundMoves, simultaneousBargainingRounds: simultaneousBargainingRounds, actionIsNoiseNotSignal:actionIsNoiseNotSignal, litigationQuality:LitigationQuality, allowAbandonAndDefault: allowAbandonAndDefault);
            pInformationSet.Should().Be(expectedPartyInformationSets.pInformationSet);
            dInformationSet.Should().Be(expectedPartyInformationSets.dInformationSet);
            resolutionSet.Should().Be(expectedResolutionSet);
        }

        [TestMethod]
        public void CaseTried()
        {
            foreach (bool allowAbandonAndDefaults in new bool[] { true, false })
                foreach (bool forgetEarlierBargainingRounds in new bool[] { true, false })
                    foreach (byte numBargainingRounds in new byte[] { 1, 2 })
                        foreach (bool plaintiffWins in new bool[] { true, false })
                            foreach (bool simultaneousBargainingRounds in new bool[] { true, false })
                                foreach (bool actionIsNoiseNotSignal in new bool[] { true, false })
                                    CaseTried_Helper(allowAbandonAndDefaults, forgetEarlierBargainingRounds, numBargainingRounds, plaintiffWins, simultaneousBargainingRounds, actionIsNoiseNotSignal);
        }

        private void CaseTried_Helper(bool allowAbandonAndDefaults, bool forgetEarlierBargainingRounds, byte numBargainingRounds, bool plaintiffWins, bool simultaneousBargainingRounds, bool actionIsNoiseNotSignal)
        {
            var options = GetGameOptions(allowAbandonAndDefaults, numBargainingRounds, forgetEarlierBargainingRounds, simultaneousBargainingRounds, actionIsNoiseNotSignal);
            var bargainingMoves = GetBargainingRoundMoves(simultaneousBargainingRounds, numBargainingRounds, false);
            byte courtResult;
            if (actionIsNoiseNotSignal)
                courtResult = plaintiffWins ? (byte) NumDistinctPoints : (byte) 1; // we've used a high value of court noise above, so if the court has its highest possible noise, it will definitely conclude plaintiff has won, and if the court has its lowest possible noise, it will definitely conclude that defendant has won
            else
                courtResult = plaintiffWins ? (byte) 2 : (byte) 1;
            var actions = GetPlayerActions(true, true, LitigationQuality, PSignalOrNoise, DSignalOrNoise, bargainingMoves, simultaneousBargainingRounds, null, null, 0, courtResult);
            var myGameProgress = MyGameRunner.PlayMyGameOnce(options, actions);
            myGameProgress.GameComplete.Should().BeTrue();
            double pFinalWealthExpected = options.PInitialWealth - options.PTrialCosts -
                              options.NumBargainingRounds * options.PerPartyBargainingRoundCosts;
            double dFinalWealthExpected = options.DInitialWealth - options.DTrialCosts -
                                          options.NumBargainingRounds * options.PerPartyBargainingRoundCosts;
            if (plaintiffWins)
            {
                pFinalWealthExpected += options.DamagesAlleged;
                dFinalWealthExpected -= options.DamagesAlleged;
            }
            myGameProgress.PFinalWealth.Should().Be(pFinalWealthExpected);
            myGameProgress.DFinalWealth.Should().Be(dFinalWealthExpected);
            GetInformationSetStrings(myGameProgress, out string pInformationSet, out string dInformationSet,
                out string resolutionSet);
            var expectedPartyInformationSets = GetExpectedPartyInformationSets(actionIsNoiseNotSignal, LitigationQuality, PSignalOrNoise, DSignalOrNoise, bargainingMoves,
                forgetEarlierBargainingRounds, simultaneousBargainingRounds);
            var expectedResolutionSet = ConstructExpectedResolutionSet(actionIsNoiseNotSignal, LitigationQuality, true, true, bargainingMoves,
                simultaneousBargainingRounds, false, allowAbandonAndDefaults, false, false, false, true, courtResult);
            pInformationSet.Should().Be(expectedPartyInformationSets.pInformationSet);
            dInformationSet.Should().Be(expectedPartyInformationSets.dInformationSet);
            resolutionSet.Should().Be(expectedResolutionSet);
        }
    }
}
