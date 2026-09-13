using ACESimBase.Games.LitigGame.ManualReports;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using static ACESimBase.Games.LitigGame.ManualReports.EquilibriumMixingSearch;
using static ACESimBase.Games.LitigGame.ManualReports.InformationSetPressureAnalysis;

namespace ACESimTest.GameTests;

[TestClass]
public class EquilibriumMixingSearchTests
{
    [TestMethod]
    public void MixingScoreDistinguishesPureUniformAndIntermediateStrategies()
    {
        Mixing(new[] { 1.0, 0, 0 }).Should().Be(0);
        Mixing(new[] { 1.0 / 3, 1.0 / 3, 1.0 / 3 }).Should().BeApproximately(1, 1e-12);
        Mixing(new[] { .5, .5, 0 }).Should().BeApproximately(.75, 1e-12);
        Math.Exp(Entropy(new[] { .5, .5, 0 })).Should().BeApproximately(2, 1e-12);
    }

    [TestMethod]
    public void UnconstrainedProjectionProducesUniformMixing()
    {
        var result = Project(new[] { 1.0, 0, 0 }, Array.Empty<Cut>());
        result.Termination.Should().BePositive();
        result.Probabilities.Should().OnlyContain(p => Math.Abs(p - 1.0 / 3) < 1e-8);
    }

    [TestMethod]
    public void JointInformationSetOptimizationAllowsCoordinatedRedistribution()
    {
        // Two mutually exclusive information sets: Pr(B|1)+Pr(B|2) <= .3.
        // Coordinate-only optimization can stop at (.3,0); the joint optimum is (.15,.15).
        var initial = new[] { .7, .3, 1.0, 0 };
        Check Oracle(double[] p)
        {
            double gain = Math.Max(0, p[1] + p[3] - .3);
            return new(new[] { 0.0, gain }, gain > 1e-9 ?
                new[] { new Cut(new[] { 0.0, 1, 0, 1 }, .3, "joint D deviation") } : Array.Empty<Cut>());
        }
        var result = OptimizeBlock(initial, Oracle, new(), new[] { 2, 2 });
        result.Status.Should().Be("Verified block optimum");
        result.Probabilities[1].Should().BeApproximately(.15, 1e-8);
        result.Probabilities[3].Should().BeApproximately(.15, 1e-8);
        (result.Probabilities[0] + result.Probabilities[1]).Should().BeApproximately(1, 1e-12);
        (result.Probabilities[2] + result.Probabilities[3]).Should().BeApproximately(1, 1e-12);
    }

    [TestMethod]
    public void OpponentDeviationLimitsMixingDespiteFocalIndifference()
    {
        // Row is indifferent. Column's deviation gains are -1 at A and +4 at B.
        // Thus Column continues best responding only while Pr(B) <= 1/5.
        Check Oracle(double[] p)
        {
            double gain = Math.Max(0, -p[0] + 4 * p[1]);
            return new(new[] { 0.0, gain }, gain > 1e-9 ?
                new[] { new Cut(new[] { -1.0, 4.0 }, 0, "column deviation") } : Array.Empty<Cut>());
        }
        var result = OptimizeBlock(new[] { 1.0, 0 }, Oracle, new());
        result.Status.Should().Be("Verified block optimum");
        result.Probabilities[1].Should().BeApproximately(.2, 1e-8);
        result.Gains.Max().Should().BeLessThanOrEqualTo(1e-9);
        result.Cuts.Should().BeGreaterThan(0);
    }

    [TestMethod]
    public void ConstraintGenerationHandlesMultipleDeviationsAndForcedZeros()
    {
        // p1 <= .2 and p2 == 0; the uniform proposal violates both.
        Check Oracle(double[] p)
        {
            var cuts = new List<Cut>();
            if (p[1] > .2 + 1e-9) cuts.Add(new(new[] { -.2, .8, -.2 }, 0, "D deviation one"));
            if (p[2] > 1e-9) cuts.Add(new(new[] { 0.0, 0, 1 }, 0, "D deviation two"));
            return new(new[] { 0.0, Math.Max(0, Math.Max(p[1] - .2, p[2])) }, cuts.ToArray());
        }
        var result = OptimizeBlock(new[] { 1.0, 0, 0 }, Oracle, new());
        result.Status.Should().Be("Verified block optimum");
        result.Probabilities[0].Should().BeApproximately(.8, 1e-8);
        result.Probabilities[1].Should().BeApproximately(.2, 1e-8);
        result.Probabilities[2].Should().BeApproximately(0, 1e-9);
    }

    [TestMethod]
    public void CutLimitDoesNotReturnAnUnverifiedCandidate()
    {
        var initial = new[] { 1.0, 0 };
        var result = OptimizeBlock(initial, p => new(new[] { 0.0, p[1] },
            new[] { new Cut(new[] { 0.0, 1 }, 0, "D") }), new(MaxCutsPerBlock: 1));
        result.Status.Should().Be("Cut limit; unchanged");
        result.Probabilities.Should().Equal(initial);
    }

    [TestMethod]
    public void InvalidInputsAndFalseOracleCutsAreRejected()
    {
        Action badSettings = () => Validate(new(GainLimit: 1e-4));
        badSettings.Should().Throw<ArgumentException>();
        Action badProjection = () => Project(new[] { 1.0, double.NaN }, Array.Empty<Cut>());
        badProjection.Should().Throw<ArgumentException>();
        Action badOracle = () => OptimizeBlock(new[] { 1.0, 0 }, p =>
            new(new[] { 0.0, 1.0 }, new[] { new Cut(new[] { 0.0, 0 }, 1, "not separating") }), new());
        badOracle.Should().Throw<System.IO.InvalidDataException>();
    }

    [TestMethod]
    public void ReplacingOneInformationSetNeverMutatesTheSourceOrOtherPolicies()
    {
        var a = new Strategy("a", 0, 1, "a", new[] { "Yes", "No" }, new[] { 1.0, 0 }, 1, false, "source");
        var b = a with { Key = "b", Player = 1 };
        var source = new Profile("source", "test", new() { ["a"] = a, ["b"] = b });
        var proposal = Replace(source, "a", new[] { .5, .5 });
        source.Strategies["a"].Probabilities.Should().Equal(1, 0);
        proposal.Strategies["a"].Probabilities.Should().Equal(.5, .5);
        proposal.Strategies["b"].Probabilities.Should().Equal(1, 0);
    }
}
