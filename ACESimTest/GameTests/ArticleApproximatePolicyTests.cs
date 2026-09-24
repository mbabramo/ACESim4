using ACESimBase.Games.LitigGame.ManualReports;
using ACESimBase.Util.Mathematics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;

namespace ACESimTest.GameTests;

[TestClass]
public class ArticleApproximatePolicyTests
{
    [TestMethod]
    public void ThresholdsAreStrictAndFirstEarlyAcceptanceEndsTheStart()
    {
        var selection = new ArticleApproximatePolicy.Selection();
        selection.Observe(1, 0.001, new[] { 0.4, 0.6 });
        Assert.IsNull(selection.Finished);
        selection.Observe(2, double.BitDecrement(0.001), new[] { 0.6, 0.4 });
        Assert.AreEqual(2, selection.Finished.Accepted.Pivot);
        Assert.AreEqual("first-below-0.001", selection.Finished.Reason);
        Assert.ThrowsException<InvalidOperationException>(() => selection.Observe(3, 0, new[] { 1.0, 0.0 }));
        var boundary = new ArticleApproximatePolicy.Selection();
        boundary.Observe(1, 0.0025, new[] { 1.0, 0.0 });
        Assert.IsNull(boundary.EndAtCap().Accepted);
    }

    [TestMethod]
    public void CapUsesBestSingleProfileAndAnEarlierErrorCannotRecoverIt()
    {
        double[] profile = { 0.25, 0.75 };
        var selection = new ArticleApproximatePolicy.Selection();
        selection.Observe(10, 0.002, profile);
        profile[0] = 1; // audit selection owns its snapshot
        selection.Observe(20, 0.002, new[] { 1.0, 0.0 });
        selection.Observe(1000, 0.02, new[] { 0.5, 0.5 });
        var decision = selection.EndAtCap();
        Assert.AreEqual(1000, decision.StoppingPivot);
        Assert.AreEqual(10, decision.Accepted.Pivot);
        Assert.AreEqual(0.25, decision.Accepted.Probabilities[0]);
        var failed = new ArticleApproximatePolicy.Selection();
        failed.Observe(1, 0.0015, new[] { 0.2, 0.8 });
        Assert.IsNull(failed.EndWithoutCap("algorithm-error-before-cap", 6).Accepted);
    }

    [TestMethod]
    public void RoundingKeepsThresholdEqualityAndRejectsInvalidNormalization()
    {
        var policy = new ArticleApproximatePolicy(0.005, ArticleApproximateGainUnits.RawUtility);
        double[] rounded = policy.Round(new[] { 0.004, 0.996, 0.005, 0.995 }, new[] { 2, 2 });
        CollectionAssert.AreEqual(new[] { 0.0, 1.0, 0.005, 0.995 }, rounded);
        Assert.ThrowsException<InvalidDataException>(() => policy.Round(new[] { 0.2, 0.2 }, new[] { 2 }));
        Assert.ThrowsException<InvalidDataException>(() => new ArticleApproximatePolicy(0.6, ArticleApproximateGainUnits.RawUtility)
            .Round(new[] { 0.5, 0.5 }, new[] { 2 }));
        Assert.ThrowsException<InvalidDataException>(() => policy.Round(new[] { double.NaN, 1.0 }, new[] { 2 }));
        var scaled = new ArticleApproximatePolicy(0, ArticleApproximateGainUnits.FullTerminalUtilityRange);
        CollectionAssert.AreEqual(new[] { 0.001, 0.002 }, scaled.Scale(new[] { 0.01, 0.04 }, new[] { 10.0, 20.0 }));
        Assert.ThrowsException<InvalidDataException>(() => scaled.Scale(new[] { 0.001, -0.1 }, new[] { 1.0, 1.0 }));
    }

    [TestMethod]
    public void CertaintyEquivalentDiagnosticInvertsAffineCaraUtilities()
    {
        var cara = new CARARiskAverseUtilityCalculator { InitialWealth = 10, Alpha = 2, LinearTransformation = true };
        foreach (double wealth in new[] { 8.0, 9.1, 10.0, 10.9 })
            Assert.AreEqual(wealth, ArticleApproximatePolicy.CertaintyEquivalentWealth(cara,
                cara.GetSubjectiveUtilityForWealthLevel(wealth)), 1e-10);
    }
}
