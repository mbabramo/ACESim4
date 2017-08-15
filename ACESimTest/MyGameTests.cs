using System;
using ACESim;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ACESimTest
{
    [TestClass]
    public class MyGameTests
    {
        [TestMethod]
        public void SettlementAfterOneBargainingRound()
        {
            var options = MyGameOptionsGenerator.SingleBargainingRound_LowNoise();
            var myGameProgress = MyGameRunner.PlayMyGameOnce(options,
                MyGameActionsGenerator.SettleAtMidpoint_OneBargainingRound);
            myGameProgress.GameComplete.Should().BeTrue();
            myGameProgress.PFinalWealth.Should().Be(options.PInitialWealth + 0.5 * options.DamagesAlleged -
                                                    options.PerPartyBargainingRoundCosts);
            myGameProgress.DFinalWealth.Should().Be(options.DInitialWealth - 0.5 * options.DamagesAlleged -
                                                    options.PerPartyBargainingRoundCosts);
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
        }
    }
}
