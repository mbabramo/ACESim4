using System;
using System.Collections.Generic;
using System.Linq;
using ACESim;
using ACESim.Util;
using FluentAssertions;
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

        private MyGameOptions GetGameOptions(bool allowAbandonAndDefaults, byte numBargainingRounds, bool forgetEarlierBargainingRounds, bool simultaneousBargainingRounds)
        {
            var options = new MyGameOptions()
            {
                PInitialWealth = 1000000,
                DInitialWealth = 1000000,
                DamagesAlleged = 100000,
                NumLitigationQualityPoints = 5,
                NumSignals = 5,
                NumOffers = 5,
                NumNoiseValues = 5,
                UseRawSignals = false,
                PNoiseStdev = 0.1,
                DNoiseStdev = 0.1,
                PTrialCosts = 5000,
                DTrialCosts = 5000,
                PerPartyBargainingRoundCosts = 1000,
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

        private string ConstructExpectedResolutionSet_CaseSettles(bool pFiles, bool dAnswers,
            List<(byte pMove, byte dMove)> bargainingRounds, bool simultaneousBargainingRounds, bool forgetEarlierRounds,
            bool allowAbandonAndDefault)
        {
            return ConstructExpectedResolutionSet(pFiles, dAnswers, bargainingRounds, simultaneousBargainingRounds, true, allowAbandonAndDefault,
                false, false, false, false, false);
        }

        private string ConstructExpectedResolutionSet(bool pFiles, bool dAnswers, List<(byte pMove, byte dMove)> bargainingRounds, bool simultaneousBargainingRounds, bool settlementReachedLastRound, bool allowAbandonAndDefault, bool pAbandonsLastRound, bool dDefaultsLastRound, bool ifBothDefaultPlaintiffLoses, bool caseGoesToTrial, bool plaintiffWins)
        {
            List<byte> l = new List<byte>();
            if (pFiles)
            {
                l.Add(1);
                if (dAnswers)
                {
                    l.Add(1);
                    byte decisionIndex = 5;
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
                            if (allowAbandonAndDefault)
                            {
                                l.Add(pAbandonsLastRound ? (byte)1 : (byte)2);
                                l.Add(dDefaultsLastRound ? (byte)1 : (byte)2);
                                if (pAbandonsLastRound && dDefaultsLastRound)
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
                        l.Add(plaintiffWins ? (byte) 2 : (byte) 1);
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

        private (string pInformationSet, string dInformationSet) GetExpectedPartyInformationSets(byte pSignal, byte dSignal,
            List<(byte pMove, byte dMove)> bargainingMoves, bool forgetEarlierBargainingRounds, bool simultaneousBargaining)
        {
            List<byte> pInfo = new List<byte>() {pSignal};
            List<byte> dInfo = new List<byte>() {dSignal};
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

        private Func<Decision, GameProgress, byte> GetPlayerActions(bool pFiles, bool dAnswers, byte litigationQuality, byte pSignal, byte dSignal, List<(byte pMove, byte dMove)> bargainingRoundMoves, bool simultaneousBargainingRounds, byte? pAbandonsRound = null, byte? dDefaultsRound = null, byte mutualGiveUpResult = 0, byte courtResult = 0)
        {
            var offers = new List<(byte decision, byte customInfo, byte action)>();
            for (byte b = 1; b <= bargainingRoundMoves.Count(); b++)
            {
                if (simultaneousBargainingRounds)
                {
                    offers.Add(((byte) MyGameDecisions.POffer, b, bargainingRoundMoves[b - 1].pMove));
                    offers.Add(((byte) MyGameDecisions.DOffer, b, bargainingRoundMoves[b - 1].dMove));
                }
                else
                {
                    if (b % 2 == 1)
                    {
                        offers.Add(((byte)MyGameDecisions.POffer, b, bargainingRoundMoves[b - 1].pMove));
                        offers.Add(((byte)MyGameDecisions.DResponse, b, bargainingRoundMoves[b - 1].dMove));
                    }
                    else
                    {
                        offers.Add(((byte)MyGameDecisions.DOffer, b, bargainingRoundMoves[b - 1].dMove));
                        offers.Add(((byte)MyGameDecisions.PResponse, b, bargainingRoundMoves[b - 1].pMove));
                    }
                }
                offers.Add(((byte)MyGameDecisions.PAbandon, b, pAbandonsRound == b ? (byte)1 : (byte)2));
                offers.Add(((byte)MyGameDecisions.DDefault, b, dDefaultsRound == b ? (byte)1 : (byte)2));
            }
            var actionsToPlay = DefineActions.ForTest(
                new List<(byte decision, byte action)>()
                {
                    ((byte) MyGameDecisions.PFile, pFiles ? (byte) 1 : (byte) 2),
                    ((byte) MyGameDecisions.DAnswer, dAnswers ? (byte) 1 : (byte) 2),
                    ((byte) MyGameDecisions.LitigationQuality, litigationQuality),
                    ((byte) MyGameDecisions.PSignal, pSignal),
                    ((byte) MyGameDecisions.DSignal, dSignal),
                    ((byte) MyGameDecisions.CourtDecision, courtResult),
                },
                offers
                );
            return actionsToPlay;
        }
        
        // DEBUG -- must add abandon + default scenarios (probably a whole separate test), also not file and not answer scenarios

        [TestMethod]
        public void SettlingCase()
        {
            for (byte numPotentialBargainingRounds = 1; numPotentialBargainingRounds <= 3; numPotentialBargainingRounds++)
                for (byte settlementInRound = 1; settlementInRound <= numPotentialBargainingRounds; settlementInRound++)
                    foreach (bool forgetEarlierBargainingRounds in new bool[] { true, false })
                        foreach (bool simultaneousBargainingRounds in new bool[] { true, false })
                            foreach (bool allowAbandonAndDefault in new bool[] { true, false })
                                SettlingCase_Helper(numPotentialBargainingRounds, settlementInRound, forgetEarlierBargainingRounds, simultaneousBargainingRounds, allowAbandonAndDefault);
        }

        public void SettlingCase_Helper(byte numPotentialBargainingRounds, byte? settlementInRound, bool forgetEarlierBargainingRounds, bool simultaneousBargainingRounds, bool allowAbandonAndDefault)
        {
            var bargainingRoundMoves = GetBargainingRoundMoves(simultaneousBargainingRounds,
                settlementInRound ?? numPotentialBargainingRounds, settlementInRound != null);
            byte numActualRounds = (byte) bargainingRoundMoves.Count();
            var options = GetGameOptions(allowAbandonAndDefaults: false, numBargainingRounds: numPotentialBargainingRounds, forgetEarlierBargainingRounds: forgetEarlierBargainingRounds, simultaneousBargainingRounds: simultaneousBargainingRounds);
            var actionsToPlay = GetPlayerActions(pFiles: true, dAnswers: true, litigationQuality: 1, pSignal: 1,
                dSignal: 1, bargainingRoundMoves: bargainingRoundMoves, simultaneousBargainingRounds: simultaneousBargainingRounds);
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
            var expectedPartyInformationSets = GetExpectedPartyInformationSets(1, 1, bargainingRoundMoves, forgetEarlierBargainingRounds, simultaneousBargainingRounds);
            string expectedResolutionSet = ConstructExpectedResolutionSet_CaseSettles(pFiles: true, dAnswers: true,
                bargainingRounds: bargainingRoundMoves, simultaneousBargainingRounds: simultaneousBargainingRounds, forgetEarlierRounds: forgetEarlierBargainingRounds, allowAbandonAndDefault: false);
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
                                CaseTried_Helper(allowAbandonAndDefaults, forgetEarlierBargainingRounds, numBargainingRounds, plaintiffWins, simultaneousBargainingRounds);
        }

        private void CaseTried_Helper(bool allowAbandonAndDefaults, bool forgetEarlierBargainingRounds, byte numBargainingRounds, bool plaintiffWins, bool simultaneousBargainingRounds)
        {
            var options = GetGameOptions(allowAbandonAndDefaults, numBargainingRounds, forgetEarlierBargainingRounds, simultaneousBargainingRounds);
            var bargainingMoves = GetBargainingRoundMoves(simultaneousBargainingRounds, numBargainingRounds, false);
            var actions = GetPlayerActions(true, true, 1, 1, 1, bargainingMoves, simultaneousBargainingRounds, null, null, 0, plaintiffWins ? (byte)2 : (byte)1);
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
            var expectedPartyInformationSets = GetExpectedPartyInformationSets(1, 1, bargainingMoves,
                forgetEarlierBargainingRounds, simultaneousBargainingRounds);
            var expectedResolutionSet = ConstructExpectedResolutionSet(true, true, bargainingMoves,
                simultaneousBargainingRounds, false, allowAbandonAndDefaults, false, false, false, true, plaintiffWins);
            pInformationSet.Should().Be(expectedPartyInformationSets.pInformationSet);
            dInformationSet.Should().Be(expectedPartyInformationSets.dInformationSet);
            resolutionSet.Should().Be(expectedResolutionSet);
        }

        [TestMethod]
        public void UsingRawSignals_SettlementFails()
        {
            var options = MyGameOptionsGenerator.UsingRawSignals_10Points_1Round();
            var myGameProgress = MyGameRunner.PlayMyGameOnce(options,
                MyGameActionsGenerator.UsingRawSignals_SettlementFails);
            myGameProgress.GameComplete.Should().BeTrue();
            myGameProgress.PWinsAtTrial.Should().BeTrue();
            myGameProgress.PFinalWealth.Should().Be(options.PInitialWealth + options.DamagesAlleged - options.PTrialCosts - options.PerPartyBargainingRoundCosts);
            myGameProgress.DFinalWealth.Should().Be(options.DInitialWealth - options.DamagesAlleged - options.DTrialCosts - options.PerPartyBargainingRoundCosts);
            GetInformationSetStrings(myGameProgress, out string pInformationSet, out string dInformationSet, out string resolutionSet);
            pInformationSet.Should().Be("6"); // p's raw signal is 6, based on litigation quality 5 and noise 9
            dInformationSet.Should().Be("4"); // d's raw signal is 4, based on litigation quality 5 and noise 9
            resolutionSet.Should().Be("5,3,9,4,8"); // litigation quality 9, decision 3, p offer in last round 9, d offer in last round 4, court decision 8
        }
    }
}
