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
        public void TestMethod1()
        {
            var myGameProgress = MyGameRunner.PlayMyGameOnce(MyGameOptionsGenerator.SingleBargainingRound_LowNoise(),
                MyGameActionsGenerator.SettleAtMidpoint_OneBargainingRound);
            myGameProgress.GameComplete.Should().BeTrue();

        }
    }
}
