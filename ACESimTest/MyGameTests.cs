using System;
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

        [TestMethod]
        public void SettlementAfterOneBargainingRound()
        {
            var options = MyGameOptionsGenerator.SingleBargainingRound_5Points_LowNoise();
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
            pInformationSet.Should().Be("1"); // forgetting earlier bargaining, so just signal
            dInformationSet.Should().Be("1"); // forgetting earlier bargaining, so just signal
            resolutionSet.Should().Be("3,3,3"); // decision 3 is beginning of bargaining round, each player plays 3
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
        public void SettlementFailsAfterTwoRounds_PWins()
        {
            var options = MyGameOptionsGenerator.TwoSimultaneousBargainingRounds();
            var myGameProgress = MyGameRunner.PlayMyGameOnce(options,
                MyGameActionsGenerator.SettlementFails_PWins);
            myGameProgress.GameComplete.Should().BeTrue();
            myGameProgress.PFinalWealth.Should().Be(options.PInitialWealth + options.DamagesAlleged - options.PTrialCosts - 2 * options.PerPartyBargainingRoundCosts);
            myGameProgress.DFinalWealth.Should().Be(options.DInitialWealth - options.DTrialCosts - options.DamagesAlleged - 2 * options.PerPartyBargainingRoundCosts);
            GetInformationSetStrings(myGameProgress, out string pInformationSet, out string dInformationSet, out string resolutionSet);
            pInformationSet.Should().Be("4");
            dInformationSet.Should().Be("2");
            resolutionSet.Should().Be("5,5,1,2"); // decision 5, plaintiff offers 5, defendant offers 1, court rules for plaintiff (2)
        }

        [TestMethod]
        public void SettlementFailsAfterTwoRounds_RememberingOffers_PWins()
        {
            var options = MyGameOptionsGenerator.TwoSimultaneousBargainingRounds();
            options.ForgetEarlierBargainingRounds = false;
            var myGameProgress = MyGameRunner.PlayMyGameOnce(options,
                MyGameActionsGenerator.SettlementFails_PWins);
            myGameProgress.GameComplete.Should().BeTrue();
            myGameProgress.PFinalWealth.Should().Be(options.PInitialWealth + options.DamagesAlleged - options.PTrialCosts - 2 * options.PerPartyBargainingRoundCosts);
            myGameProgress.DFinalWealth.Should().Be(options.DInitialWealth - options.DTrialCosts - options.DamagesAlleged - 2 * options.PerPartyBargainingRoundCosts);
            GetInformationSetStrings(myGameProgress, out string pInformationSet, out string dInformationSet, out string resolutionSet);
            pInformationSet.Should().Be("4,1,1"); // p gets signal 4, d offers 1 twice
            dInformationSet.Should().Be("2,5,5");
            resolutionSet.Should().Be("5,5,1,2"); // still, only last round affects resolution set
        }

        [TestMethod]
        public void UsingRawSignals_SettlementFails()
        {
            var options = MyGameOptionsGenerator.UsingRawSignals_10Points_2Rounds();
            var myGameProgress = MyGameRunner.PlayMyGameOnce(options,
                MyGameActionsGenerator.UsingRawSignals_SettlementFails);
            myGameProgress.GameComplete.Should().BeTrue();
            myGameProgress.PWinsAtTrial.Should().BeTrue();
            myGameProgress.PFinalWealth.Should().Be(options.PInitialWealth + options.DamagesAlleged - options.PTrialCosts - 2 * options.PerPartyBargainingRoundCosts);
            myGameProgress.DFinalWealth.Should().Be(options.DInitialWealth - options.DamagesAlleged - options.DTrialCosts - 2 * options.PerPartyBargainingRoundCosts);
            GetInformationSetStrings(myGameProgress, out string pInformationSet, out string dInformationSet, out string resolutionSet);
            pInformationSet.Should().Be("10"); // p's raw signal
            dInformationSet.Should().Be("5");
            resolutionSet.Should().Be("3,5,9,4,8"); // litigation quality 3, decision 5, p offer in last round 9, d offer in last round 4, court decision 8
        }
    }
}
