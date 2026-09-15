using ACESim;
using ACESimBase.GameSolvingAlgorithms;
using ACESimBase.GameSolvingSupport.Settings;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ACESimTest.StrategiesTests;

[TestClass]
public class SequenceFormRecoveryTests
{
    [TestMethod]
    public void AllInexactFailuresReachExactFallbackAndKeepTheInitialEquilibrium()
    {
        var exactRequests = new List<int>();
        var result = SequenceForm.CollectEquilibriumRecoveries(50, false, true,
            count => { exactRequests.Add(count); return new() { (new[] { 1.0, 0.0 }, count) }; },
            count => { count.Should().Be(49); return new(); }, out int exact, out int inexact);
        exactRequests.Should().Equal(1, 49);
        result.Should().ContainSingle();
        result.Single().frequency.Should().Be(50);
        exact.Should().Be(50);
        inexact.Should().Be(49);
    }

    [TestMethod]
    public void RecoveryCountsRatherThanDistinctProfilesDetermineFallbackSize()
    {
        var requests = new List<int>();
        var result = SequenceForm.CollectEquilibriumRecoveries(50, false, true,
            count => { requests.Add(count); return new() { (new[] { 1.0 }, count) }; },
            _ => new() { (new[] { 1.0 }, 40) }, out int exact, out int inexact);
        requests.Should().Equal(1, 9);
        result.Single().frequency.Should().Be(50);
        exact.Should().Be(10);
        inexact.Should().Be(49);
    }

    [TestMethod]
    public void CompleteInexactRecoveryDoesNotRequestAZeroPriorExactSolve()
    {
        int calls = 0;
        SequenceForm.CollectEquilibriumRecoveries(50, false, true,
            count => { calls++; return new() { (new[] { 1.0 }, count) }; },
            _ => new() { (new[] { 1.0 }, 49) }, out int exact, out _);
        calls.Should().Be(1);
        exact.Should().Be(1);
    }

    [TestMethod]
    public void FailedExactFallbackPreservesVerifiedResultsWithoutInflatingRecoveryCounts()
    {
        int calls = 0;
        var result = SequenceForm.CollectEquilibriumRecoveries(50, false, true,
            _ => ++calls == 1 ? new() { (new[] { 1.0 }, 1) } : new(),
            _ => new(), out int exact, out int inexact);
        result.Single().frequency.Should().Be(1);
        string csv = SequenceForm.BuildEquilibriumRecoveryCsv("case", result, 50, exact, inexact);
        csv.Should().Contain("\"case\",50,99,49,50,1,1,1,1,1,");
    }

    [TestMethod]
    public void FailedInitialExactSolveReportsTheActualFailure()
    {
        Action action = () => SequenceForm.CollectEquilibriumRecoveries(50, false, true,
            _ => new(), _ => throw new Exception("Must not attempt additional priors"), out _, out _);
        action.Should().Throw<InvalidOperationException>().WithMessage("*initial exact prior*no verified equilibrium*");
    }

    [TestMethod]
    public void RetryDelayCannotOverflowIntoNegativeMilliseconds()
    {
        foreach (int failures in new[] { 1, 2, 20, 100, int.MaxValue })
        foreach (double jitter in new[] { 0.0, 0.5, 0.999999999 })
            Launcher.StrategyRetryDelayMilliseconds(failures, jitter).Should().BeInRange(1, 100_000);
    }
}
