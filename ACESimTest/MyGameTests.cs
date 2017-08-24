using System;
using System.Collections.Generic;
using System.Linq;
using ACESim;
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

        private string ConstructExpectedResolutionSet_CaseSettles(bool pFiles, bool dAnswers,
            List<(byte pMove, byte dMove)> bargainingRounds,
            bool allowAbandonAndDefault)
        {
            return ConstructExpectedResolutionSet(pFiles, dAnswers, bargainingRounds, true, allowAbandonAndDefault,
                false, false, false, false, false);
        }

        private string ConstructExpectedResolutionSet(bool pFiles, bool dAnswers, List<(byte pMove, byte dMove)> bargainingRounds, bool settlementReachedLastRound, bool allowAbandonAndDefault, bool pAbandonsLastRound, bool dDefaultsLastRound, bool ifBothDefaultPlaintiffLoses, bool caseGoesToTrial, bool plaintiffWins)
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
                    int numBargainingRounds = bargainingRounds.Count();
                    foreach (var moveSet in bargainingRounds)
                    {
                        l.Add(decisionIndex);
                        l.Add(moveSet.pMove);
                        l.Add(moveSet.dMove);
                        decisionIndex += 2;
                        if (allowAbandonAndDefault && bargainingRound != numBargainingRounds)
                        { // nobody gave up at this point
                            l.Add(2);
                            l.Add(2);
                            decisionIndex += 2;
                        }
                    }
                    if (allowAbandonAndDefault && !settlementReachedLastRound)
                    {
                        l.Add(pAbandonsLastRound ? (byte) 1 : (byte) 2);
                        l.Add(dDefaultsLastRound ? (byte)1 : (byte) 2);
                        if (pAbandonsLastRound && dDefaultsLastRound)
                            l.Add(ifBothDefaultPlaintiffLoses ? (byte) 1 : (byte) 2);
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

        [TestMethod]
        public void SettlementAfterOneBargainingRound()
        {
            var options = MyGameOptionsGenerator.SingleBargainingRound_5Points_LowNoise();
            var actionsToPlay = DefineActions.ForTest(
                new List<(byte decision, byte action)>()
            {
                ((byte) MyGameDecisions.PFile, (byte) 1),
                ((byte) MyGameDecisions.DAnswer, (byte) 1),
                ((byte) MyGameDecisions.LitigationQuality, (byte) 1),
                ((byte) MyGameDecisions.LitigationQuality, (byte) 1),
                ((byte) MyGameDecisions.PSignal, (byte) 1),
                ((byte) MyGameDecisions.DSignal, (byte) 1),
                ((byte) MyGameDecisions.POffer, (byte) 3),
                ((byte) MyGameDecisions.DOffer, (byte) 3),
            });
            var myGameProgress = MyGameRunner.PlayMyGameOnce(options, actionsToPlay);
            myGameProgress.GameComplete.Should().BeTrue();
            myGameProgress.CaseSettles.Should().BeTrue();
            myGameProgress.PFinalWealth.Should().Be(options.PInitialWealth + 0.5 * options.DamagesAlleged -
                                                    options.PerPartyBargainingRoundCosts);
            myGameProgress.DFinalWealth.Should().Be(options.DInitialWealth - 0.5 * options.DamagesAlleged -
                                                    options.PerPartyBargainingRoundCosts);
            myGameProgress.PWelfare.Should().Be(myGameProgress.PFinalWealth);
            myGameProgress.DWelfare.Should().Be(myGameProgress.DFinalWealth);

            var informationSetHistories = myGameProgress.GameHistory.GetInformationSetHistoryItems().ToList();
            GetInformationSetStrings(myGameProgress, out string pInformationSet, out string dInformationSet, out string resolutionSet);
            pInformationSet.Should().Be("1"); // forgetting earlier bargaining, so just signal
            dInformationSet.Should().Be("1"); // forgetting earlier bargaining, so just signal
            string expectedResolutionSet = ConstructExpectedResolutionSet_CaseSettles(pFiles: true, dAnswers: true,
                bargainingRounds: new List<(byte pMove, byte dMove)>() {((byte) 3, (byte) 3)}, allowAbandonAndDefault: true);
            resolutionSet.Should().Be(expectedResolutionSet);
        }

        [TestMethod]
        public void SettlementAfterOneBargainingRound_Remembering()
        {
            var options = MyGameOptionsGenerator.SingleBargainingRound_5Points_LowNoise();
            options.ForgetEarlierBargainingRounds = false;
            var myGameProgress = MyGameRunner.PlayMyGameOnce(options,
                MyGameActionsGenerator.SettleAtMidpointOf5Points_FirstBargainingRound);
            myGameProgress.GameComplete.Should().BeTrue();
            myGameProgress.PFinalWealth.Should().Be(options.PInitialWealth + 0.5 * options.DamagesAlleged -
                                                    options.PerPartyBargainingRoundCosts);
            myGameProgress.DFinalWealth.Should().Be(options.DInitialWealth - 0.5 * options.DamagesAlleged -
                                                    options.PerPartyBargainingRoundCosts);
            myGameProgress.PWelfare.Should().Be(myGameProgress.PFinalWealth);
            myGameProgress.DWelfare.Should().Be(myGameProgress.DFinalWealth);

            GetInformationSetStrings(myGameProgress, out string pInformationSet, out string dInformationSet, out string resolutionSet);
            pInformationSet.Should().Be("1,3"); // remembering opponent's offer after signal
            dInformationSet.Should().Be("1,3"); // remembering opponent's offer after signal
            resolutionSet.Should().Be("3,3,3"); // decision 3 is beginning of bargaining round, each player plays 3
        }

        [TestMethod]
        public void SettlementAfterOneBargainingRound_WhenTwoArePossible()
        {
            var options = MyGameOptionsGenerator.TwoSimultaneousBargainingRounds();
            var myGameProgress = MyGameRunner.PlayMyGameOnce(options,
                MyGameActionsGenerator.SettleAtMidpointOf5Points_FirstBargainingRound);
            myGameProgress.GameComplete.Should().BeTrue();
            myGameProgress.PFinalWealth.Should().Be(options.PInitialWealth + 0.5 * options.DamagesAlleged -
                                                    options.PerPartyBargainingRoundCosts);
            myGameProgress.DFinalWealth.Should().Be(options.DInitialWealth - 0.5 * options.DamagesAlleged -
                                                    options.PerPartyBargainingRoundCosts);
            GetInformationSetStrings(myGameProgress, out string pInformationSet, out string dInformationSet, out string resolutionSet);
            pInformationSet.Should().Be("1"); // forgetting earlier bargaining, so just signal
            dInformationSet.Should().Be("1"); // forgetting earlier bargaining, so just signal
            resolutionSet.Should().Be("3,3,3"); // decision 3 is beginning of bargaining round, each player plays 3
        }

        [TestMethod]
        public void SettlementAtMidpointAfterTwoBargainingRounds()
        {
            var options = MyGameOptionsGenerator.TwoSimultaneousBargainingRounds();
            var myGameProgress = MyGameRunner.PlayMyGameOnce(options,
                MyGameActionsGenerator.SettleAtMidpoint_SecondBargainingRound);
            myGameProgress.GameComplete.Should().BeTrue();
            myGameProgress.PFinalWealth.Should().Be(options.PInitialWealth + 0.5 * options.DamagesAlleged -
                                                    2 * options.PerPartyBargainingRoundCosts);
            myGameProgress.DFinalWealth.Should().Be(options.DInitialWealth - 0.5 * options.DamagesAlleged -
                                                    2 * options.PerPartyBargainingRoundCosts);
            GetInformationSetStrings(myGameProgress, out string pInformationSet, out string dInformationSet, out string resolutionSet);
            pInformationSet.Should().Be("1"); // forgetting earlier bargaining, so just signal
            dInformationSet.Should().Be("1"); // forgetting earlier bargaining, so just signal
            resolutionSet.Should().Be("5,3,3"); // decision 5 is beginning of 2nd bargaining round, each player plays 3
        }

        [TestMethod]
        public void SettlementAtMidpointAfterTwoBargainingRounds_RememberingEarlierRound()
        {
            var options = MyGameOptionsGenerator.TwoSimultaneousBargainingRounds();
            options.ForgetEarlierBargainingRounds = false;
            var myGameProgress = MyGameRunner.PlayMyGameOnce(options,
                MyGameActionsGenerator.SettleAtMidpoint_SecondBargainingRound);
            myGameProgress.GameComplete.Should().BeTrue();
            myGameProgress.PFinalWealth.Should().Be(options.PInitialWealth + 0.5 * options.DamagesAlleged -
                                                    2 * options.PerPartyBargainingRoundCosts);
            myGameProgress.DFinalWealth.Should().Be(options.DInitialWealth - 0.5 * options.DamagesAlleged -
                                                    2 * options.PerPartyBargainingRoundCosts);
            GetInformationSetStrings(myGameProgress, out string pInformationSet, out string dInformationSet, out string resolutionSet);
            pInformationSet.Should().Be("1,3,3"); // signal, then both rounds
            dInformationSet.Should().Be("1,5,3"); // signal, then both rounds
            resolutionSet.Should().Be("5,3,3"); // decision 3 is beginning of bargaining round, each player plays 3
        }

        [TestMethod]
        public void SettlementAtTwoThirdsAfterTwoBargainingRounds()
        {
            var options = MyGameOptionsGenerator.TwoSimultaneousBargainingRounds();
            var myGameProgress = MyGameRunner.PlayMyGameOnce(options,
                MyGameActionsGenerator.SettleAtTwoThirds_SecondBargainingRound);
            myGameProgress.GameComplete.Should().BeTrue();
            myGameProgress.PFinalWealth.Should().Be(options.PInitialWealth + (2.0 / 3.0) * options.DamagesAlleged -
                                                    2 * options.PerPartyBargainingRoundCosts);
            myGameProgress.DFinalWealth.Should().Be(options.DInitialWealth - (2.0 / 3.0) * options.DamagesAlleged -
                                                    2 * options.PerPartyBargainingRoundCosts);
            GetInformationSetStrings(myGameProgress, out string pInformationSet, out string dInformationSet, out string resolutionSet);
            pInformationSet.Should().Be("1");
            dInformationSet.Should().Be("1");
            resolutionSet.Should().Be("5,4,4"); // decision 5 --> agreement to 4
        }

        [TestMethod]
        public void SettlementFailsAfterOneRound_RememberingOffers_PWins()
        {
            SettlementFails_Helper(rememberOffers: true, twoBargainingRounds: false, plaintiffWins: true);
        }

        [TestMethod]
        public void SettlementFailsAfterOneRound_RememberingOffers_PLoses()
        {
            SettlementFails_Helper(rememberOffers: true, twoBargainingRounds: false, plaintiffWins: false);
        }

        [TestMethod]
        public void SettlementFailsAfterOneRound_ForgettingOffers_PWins()
        {
            SettlementFails_Helper(rememberOffers: false, twoBargainingRounds: false, plaintiffWins: true);
        }

        [TestMethod]
        public void SettlementFailsAfterOneRound_ForgettingOffers_PLoses()
        {
            SettlementFails_Helper(rememberOffers: false, twoBargainingRounds: false, plaintiffWins: false);
        }

        [TestMethod]
        public void SettlementFailsAfterTwoRounds_RememberingOffers_PWins()
        {
            SettlementFails_Helper(rememberOffers: true, twoBargainingRounds: true, plaintiffWins: true);
        }

        [TestMethod]
        public void SettlementFailsAfterTwoRounds_RememberingOffers_PLoses()
        {
            SettlementFails_Helper(rememberOffers: true, twoBargainingRounds: true, plaintiffWins: false);
        }

        [TestMethod]
        public void SettlementFailsAfterTwoRounds_ForgettingOffers_PWins()
        {
            SettlementFails_Helper(rememberOffers: false, twoBargainingRounds: true, plaintiffWins: true);
        }

        [TestMethod]
        public void SettlementFailsAfterTwoRounds_ForgettingOffers_PLoses()
        {
            SettlementFails_Helper(rememberOffers: false, twoBargainingRounds: true, plaintiffWins: false);
        }

        private static void SettlementFails_Helper(bool rememberOffers, bool twoBargainingRounds,
            bool plaintiffWins)
        {
            var options = MyGameOptionsGenerator.TwoSimultaneousBargainingRounds();
            if (rememberOffers)
                options.ForgetEarlierBargainingRounds = false;
            if (!twoBargainingRounds)
                options.NumBargainingRounds = 1;
            var myGameProgress = MyGameRunner.PlayMyGameOnce(options, plaintiffWins ? (Func<Decision,GameProgress,byte>) MyGameActionsGenerator.SettlementFails_PWins : (Func<Decision, GameProgress, byte>)MyGameActionsGenerator.SettlementFails_PLoses);
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
            string pOffers = !twoBargainingRounds ? ",5" : ",5,5";
            string dOffers = !twoBargainingRounds ? ",1" : ",1,1";
            pInformationSet.Should().Be("4" + (rememberOffers ? dOffers : "")); // p gets signal 4, d offers 1 twice
            dInformationSet.Should().Be("2" + (rememberOffers ? pOffers : ""));
            string lastPlaintiffOfferDecision = twoBargainingRounds ? "5," : "3,";
            resolutionSet.Should().Be(lastPlaintiffOfferDecision + "5,1" + (plaintiffWins ? ",2" : ",1")); // still, only last round affects resolution set
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
