using ACESim;
using ACESimBase.Games.LitigGame.PrecautionModel;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Linq;

namespace ACESimTest.GameTests;

[TestClass, DoNotParallelize]
public class LitigGameEndogenousDisputesLauncherTests
{
    [TestMethod]
    public void DefaultAndSingleOptionUseTheEndogenousModelWithoutChangingConsoleSelection()
    {
        string consoleBefore = LitigGameOptionsGenerator.GetLitigGameOptions().ToString();
        var launcher = new LitigGameEndogenousDisputesLauncher();
        var expected = LitigGameOptionsGenerator.PrecautionNegligenceGame();
        var actual = (LitigGameOptions)launcher.GetDefaultSingleGameOptions();
        Assert.IsInstanceOfType<PrecautionNegligenceDisputeGenerator>(actual.LitigGameDisputeGenerator);
        Assert.AreEqual(expected.ToString(), actual.ToString());
        launcher.LaunchSingleOptionsSetOnly = true;
        var single = (LitigGameOptions)launcher.GetOptionsSets().Single();
        Assert.IsInstanceOfType<PrecautionNegligenceDisputeGenerator>(single.LitigGameDisputeGenerator);
        Assert.AreEqual(expected.ToString(), single.ToString());
        Assert.AreEqual(consoleBefore, LitigGameOptionsGenerator.GetLitigGameOptions().ToString());
    }

    [TestMethod]
    public void EndogenousVariationMatrixCanBeConstructedAlongsideCorrelatedSignals()
    {
        var correlated = new LitigGameCorrelatedSignalsArticleLauncher();
        string before = correlated.GetDefaultSingleGameOptions().ToString();
        var options = new LitigGameEndogenousDisputesLauncher().GetOptionsSets().Cast<LitigGameOptions>().ToArray();
        Assert.IsTrue(options.Length > 0);
        Assert.IsTrue(options.All(o => o.LitigGameDisputeGenerator is PrecautionNegligenceDisputeGenerator));
        Assert.AreEqual(before, correlated.GetDefaultSingleGameOptions().ToString());
    }
}
