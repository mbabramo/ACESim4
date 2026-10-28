using ACESim;
using ACESimBase.Games.EFGFileGame;
using ACESimBase.GameSolvingAlgorithms;
using ACESimBase.GameSolvingSupport.Execution;
using ACESimBase.GameSolvingSupport.GameTree;
using ACESimBase.GameSolvingSupport.Policies;
using ACESimBase.GameSolvingSupport.Settings;
using ACESimBase.GameSolvingSupport.SolverSpecificSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace ACESimTest.StrategiesTests;

[TestClass]
public class ExecutionWorkflowTests
{
    [TestMethod]
    public async Task RepetitionSetsRunIdentityAndResetsOnceBeforeRetries()
    {
        var developer = new StubDeveloper();
        var report = new ReportCollection();
        var delays = new List<int>();
        developer.Operation = () => developer.Calls < 3 ? throw new InvalidOperationException("retry") : Task.FromResult(report);
        var result = await LegacyRepetitionRunner.RunAsync(developer, new("case", 2, 7, 4, "article"),
            _ => { }, delay => { delays.Add(delay); return Task.CompletedTask; }, () => .5);
        Assert.AreSame(report, result);
        Assert.AreEqual(9, developer.EvolutionSettings.GameNumber);
        Assert.AreEqual(1, developer.Resets);
        Assert.AreEqual(("case", (int?)4, "article"), developer.LastRequest);
        CollectionAssert.AreEqual(new[] { 1, 5001 }, delays);
        Assert.AreEqual(3, developer.Calls);
    }

    [TestMethod]
    public async Task FirstRepetitionCanSkipWithoutResetAndRetryBudgetIsPreserved()
    {
        var developer = new StubDeveloper { Operation = () => Task.FromResult<ReportCollection>(null) };
        Assert.IsNull(await LegacyRepetitionRunner.RunAsync(developer, new("case", 0, 7, null, "article")));
        Assert.AreEqual(0, developer.Resets);
        int delays = 0;
        developer.Calls = 0;
        developer.Operation = () => throw new InvalidOperationException("unavailable");
        var error = await Assert.ThrowsExceptionAsync<Exception>(() => LegacyRepetitionRunner.RunAsync(developer,
            new("case", 0, 7, null, "article"), _ => { }, _ => { delays++; return Task.CompletedTask; }, () => 0));
        Assert.AreEqual("Repeated failures", error.Message);
        Assert.AreEqual(21, developer.Calls);
        Assert.AreEqual(20, delays);
    }

    [TestMethod]
    public async Task UnrollingAndSequenceFormFailuresNeverRetryPartiallyInitializedDeveloper()
    {
        var unrolling = new UnrollingException(new Exception("cache"));
        var developer = new StubDeveloper { Operation = () => throw unrolling };
        var error = await Assert.ThrowsExceptionAsync<UnrollingException>(() => LegacyRepetitionRunner.RunAsync(developer,
            new("case", 0, 0, null, "article"), _ => { }, _ => throw new Exception("Must not delay"), () => 0));
        Assert.AreSame(unrolling, error);
        Assert.AreEqual(1, developer.Calls);
        var sequence = new FailingSequenceForm();
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => LegacyRepetitionRunner.RunAsync(sequence,
            new("case", 0, 0, null, "article"), _ => { }, _ => throw new Exception("Must not delay"), () => 0));
        Assert.AreEqual(1, sequence.Calls);
    }

    [TestMethod]
    public void ParallelPriorSearchPreservesBatchOrderAndRecoveryCountsAreDetached()
    {
        var calls = new List<string>();
        var first = new[] { .2, .8 };
        var second = new[] { .7, .3 };
        var result = SequenceFormPriorSearch.Collect(5, true, true,
            count => { calls.Add("exact:" + count); return new() { (second, 2) }; },
            count => { calls.Add("inexact:" + count); return new() { (first, 3) }; });
        CollectionAssert.AreEqual(new[] { "inexact:5", "exact:2" }, calls);
        Assert.AreEqual(5, result.VerifiedRecoveries);
        Assert.AreEqual(2, result.Recoveries.Count);
        first[0] = 1;
        Assert.AreEqual(.2, result.Recoveries[0].Probabilities[0]);
        result.ToLegacyProfiles()[0].equilibrium[0] = 0;
        Assert.AreEqual(.2, result.Recoveries[0].Probabilities[0]);
        Assert.AreEqual(3, result.Recoveries[0].Count);
    }

    [TestMethod]
    public async Task LegacyEquilibriumFilesKeepRationalInputOrderingAndZeroCompletion()
    {
        string path = Path.GetTempFileName();
        var oldCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            File.WriteAllText(path, "1/4,3/4,0,0" + Environment.NewLine);
            var profiles = LegacyEquilibriumFiles.Read(path);
            var solver = await BehavioralPolicyTests.Create();
            LegacyEquilibriumPolicies.ApplyCurrent(solver, profiles.Single());
            CollectionAssert.AreEqual(new[] { .25, .75, .5, .5 }, LegacyEquilibriumPolicies.CaptureCurrent(solver));
            LegacyEquilibriumFiles.Write(path, profiles);
            Assert.AreEqual("0.25,0.75,0,0" + Environment.NewLine, File.ReadAllText(path));
            solver.MasterReportName = "paper";
            solver.GameDefinition.OptionSetName = "";
            Assert.AreEqual("paper  -equ.csv", Path.GetFileName(LegacyEquilibriumFiles.PathFor(solver)));
        }
        finally { CultureInfo.CurrentCulture = oldCulture; File.Delete(path); }
    }

    private sealed class FailingSequenceForm : SequenceForm, IStrategiesDeveloper
    {
        public int Calls;
        public FailingSequenceForm() : base(new(), new(), new GameDefinition { Players = new() }) { }
        public new Task<ReportCollection> DevelopStrategies(string optionSetName, int? restrictToScenarioIndex, string masterReportName)
        { Calls++; throw new InvalidOperationException("failed tree"); }
    }

    private sealed class StubDeveloper : IStrategiesDeveloper
    {
        public int Calls, Resets;
        public (string, int?, string) LastRequest;
        public Func<Task<ReportCollection>> Operation;
        public List<Strategy> Strategies { get; set; } = new();
        public EvolutionSettings EvolutionSettings { get; set; } = new();
        public GameDefinition GameDefinition { get; set; }
        public IGameFactory GameFactory { get; set; }
        public GamePlayer GamePlayer { get; set; }
        public HistoryNavigationInfo Navigation { get; set; }
        public InformationSetLookupApproach LookupApproach { get; set; }
        public void Reinitialize() => Resets++;
        public Task<ReportCollection> DevelopStrategies(string optionSetName, int? restrictToScenarioIndex, string masterReportName)
        { Calls++; LastRequest = (optionSetName, restrictToScenarioIndex, masterReportName); return Operation(); }
        public Task Initialize() => throw new NotSupportedException();
        public IStrategiesDeveloper DeepCopy() => throw new NotSupportedException();
        public Task<ReportCollection> RunAlgorithm(string optionSetName) => throw new NotSupportedException();
        public void PreSerialize() => throw new NotSupportedException();
        public void UndoPreSerialize() => throw new NotSupportedException();
    }
}
