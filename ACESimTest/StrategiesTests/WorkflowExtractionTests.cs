using ACESim;
using ACESimBase.GameSolvingSupport.Execution;
using ACESimBase.GameSolvingSupport.Policies;
using ACESimBase.GameSolvingSupport.Settings;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace ACESimTest.StrategiesTests;

[TestClass]
public class WorkflowExtractionTests
{
    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", "EquilibriumWorkflows", name + ".json");

    [TestMethod]
    public async Task GeneratedCorrelationExpressionsMatchBeforeExtraction()
    {
        var solver = await BehavioralPolicyTests.Create();
        var expected = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Fixture("generated-correlation")));
        for (byte player = 0; player < 2; player++)
        foreach (var strategy in new[] { ActionStrategies.CorrelatedEquilibrium, ActionStrategies.BestResponseVsCorrelatedEquilibrium, ActionStrategies.CorrelatedEquilibriumVsBestResponse })
        {
            var expression = new StringBuilder();
            solver.CorrelatedEquilibriumCalculationString_Tree(expression, player, strategy);
            Assert.AreEqual(expected[player + "/" + strategy], expression.ToString());
        }
    }

    [TestMethod]
    public void ExistingSerializedBaseFieldsKeepNamesTypesAndVisibility()
    {
        var expected = JsonSerializer.Deserialize<string[]>(File.ReadAllText(Fixture("serialized-base-fields")));
        var actual = typeof(StrategiesDeveloperBase).GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(f => !f.IsDefined(typeof(NonSerializedAttribute))).Select(f => f.Name + "|" + f.FieldType.FullName + "|" + f.Attributes).OrderBy(x => x, StringComparer.Ordinal).ToArray();
        CollectionAssert.AreEqual(expected, actual);
    }

    [TestMethod]
    [DataRow(-2, 3)] // -2 represents no restriction
    [DataRow(-1, 1)] // initialization-only scenario sentinel
    [DataRow(0, 1)]
    [DataRow(1, 2)]
    public async Task DevelopmentKeepsHistoricalScenarioAndSolveOrdering(int restriction, int runIndex)
    {
        var solver = new TracedDeveloper();
        int? requested = restriction == -2 ? null : restriction;
        var result = await solver.DevelopStrategies("case", requested, "article");
        var indices = restriction == -2 ? new[] { 0, 1, 2 } : new[] { Math.Max(0, restriction) };
        var expected = new[] { "initialize" }.Concat(indices.SelectMany(i => new[] { "scenario:" + i, "reset:" + i }))
            .Append("solve:" + runIndex).ToArray();
        CollectionAssert.AreEqual(expected, solver.Calls);
        Assert.AreEqual(indices.Last(), solver.Status.ScenarioIndex);
        Assert.AreEqual("article", solver.MasterReportName);
        Assert.AreEqual("answer", result.csvReports.Single());
        // Preserve the old single RunAlgorithm call after scenario setup. Changing that
        // protocol is a separate behavior change, not part of relocating its implementation.
    }

    [TestMethod]
    public async Task PrincipalComponentObservationScheduleDispatchesSolverOverrides()
    {
        var solver = new TracedDeveloper();
        solver.EvolutionSettings.PCA_PerformPrincipalComponentAnalysis = true;
        solver.EvolutionSettings.PCA_FirstIterationToSaveAsPCAObservation = 2;
        solver.EvolutionSettings.PCA_SavePCAObservationEveryNIterationsAfterFirst = 2;
        solver.EvolutionSettings.TotalIterations = 10;
        for (int iteration = 1; iteration <= 4; iteration++)
            await solver.PostIterationWorkForPrincipalComponentsAnalysis(iteration, new ReportCollection());
        CollectionAssert.AreEqual(new[] { "pca-initialize", "model:1", "model:2" }, solver.Calls);
        Assert.AreEqual(2, solver.ModelDataSavedForPCA.Count);
        CollectionAssert.AreEqual(new[] { 1f, 2f }, solver.ModelDataSavedForPCA[0][0]);
        CollectionAssert.AreEqual(new[] { 2f, 4f }, solver.ModelDataSavedForPCA[1][0]);
        solver.EvolutionSettings.PCA_PerformPrincipalComponentAnalysis = false;
        await solver.PostIterationWorkForPrincipalComponentsAnalysis(1, new ReportCollection());
        Assert.AreEqual(2, solver.ModelDataSavedForPCA.Count);
        Assert.AreEqual(3, solver.Calls.Count);
    }

    [TestMethod]
    public async Task ScenarioRecordingCopiesStatusAndKeepsMatchedPolicyHistory()
    {
        var solver = await BehavioralPolicyTests.Create();
        solver.EvolutionSettings.RecordPastValues = true;
        solver.EvolutionSettings.RecordPastValues_AtEndOfScenarioOnly = true;
        solver.EvolutionSettings.CheckCorrelatedEquilibriumIncompatibilitiesAlongWay = false;
        solver.EvolutionSettings.SerializeResults = false;
        solver.EvolutionSettings.UseCurrentStrategyForBestResponse = true;
        var correlation = new ScenarioCorrelationWorkflow(solver);
        for (int scenario = 0; scenario < 2; scenario++)
        {
            FlatEquilibriumPolicies.ApplyCurrent(solver, scenario == 0 ? new[] { 1.0, 0, 1.0, 0 } : new[] { 0.0, 1, 0.0, 1 });
            solver.Status.ScenarioIndex = scenario;
            solver.Status.UtilitiesOverall = scenario == 0 ? new[] { 3.0, 2 } : new[] { 1.0, 4 };
            solver.Status.BestResponseUtilities = solver.Status.UtilitiesOverall.ToArray();
            solver.Status.BestResponseImprovement = new[] { 0.0, 0 };
            correlation.RememberScenarioForCorrelatedEquilibrium(scenario);
        }
        solver.Status.UtilitiesOverall[0] = 100;
        Assert.AreEqual(1, solver.RememberedStatuses[1].UtilitiesOverall[0]);
        foreach (var node in solver.InformationSets)
        {
            Assert.AreEqual(2, node.PastValues.Count);
            CollectionAssert.AreEqual(new[] { 1.0, 0 }, node.PastValues[0]);
            CollectionAssert.AreEqual(new[] { 0.0, 1 }, node.PastValues[1]);
            CollectionAssert.AreEqual(new[] { 0.0, 0 }, node.PastValuesCumulativeStrategyDiscounts);
        }
        CollectionAssert.AreEqual(new[] { 0.0, 0 }, solver.GetUtilitiesForPastValueCombination(0, 1));
        CollectionAssert.AreEqual(new[] { 3.0, 2 }, solver.GetUtilitiesForPastValueCombination(0, 0));
    }

    [TestMethod]
    public async Task ModelSuccessTrackingKeepsObservationLayoutAndAccumulationAcrossCalls()
    {
        var solver = await BehavioralPolicyTests.Create();
        solver.EvolutionSettings.ModelSuccessTracking = true;
        solver.EvolutionSettings.TotalIterations = 1;
        solver.EvolutionSettings.ModelSuccessTrackingSaveAfterNItems = null;
        FlatEquilibriumPolicies.ApplyCurrent(solver, new[] { .23456, .76544, .71234, .28766 });
        solver.Status.UtilitiesOverall = new[] { 1.0, 2.0 };
        solver.Status.BestResponseUtilities = new[] { 1.2, 2.4 };
        solver.Status.BestResponseImprovement = new[] { .2, .4 };
        solver.Status.ScoreRange = new[] { 1.0, 2.0 };
        solver.ConsiderModelSuccessTrackingForIteration(0);
        Assert.IsNull(solver.TrackedModels);
        solver.ConsiderModelSuccessTrackingForIteration(1);
        solver.ConsiderModelSuccessTrackingForIteration(2);
        Assert.AreEqual(2, solver.TrackedModelCount);
        CollectionAssert.AreEqual(new[] { .235f, .712f, 1f, 2f, 1.2f, 2.4f, .2f, .2f, .2f }, solver.TrackedModels[0]);
        CollectionAssert.AreEqual(solver.TrackedModels[0], solver.TrackedModels[1]);
    }

    private sealed class TracedDeveloper : StrategiesDeveloperBase
    {
        public List<string> Calls = new();
        private int observations;
        public TracedDeveloper() : base(new(), new(), new TracedGame())
        { ((TracedGame)GameDefinition).Calls = Calls; }
        public override Task Initialize() { Calls.Add("initialize"); return Task.CompletedTask; }
        public override Task<ReportCollection> RunAlgorithm(string optionSetName)
        { Calls.Add("solve:" + OverallScenarioIndex); return Task.FromResult(new ReportCollection("", "answer")); }
        public override IStrategiesDeveloper DeepCopy() => throw new NotSupportedException();
        public override void CompleteReinitializeForScenario(bool warmupVersion, int previousScenarioIndex, double previousWeightOnOpponentP0,
            double previousWeightOnOpponentOtherPlayers, int updatedScenarioIndex, bool alwaysReinitialize) => Calls.Add("reset:" + updatedScenarioIndex);
        public override void InitializePreparationForPCA() { Calls.Add("pca-initialize"); base.InitializePreparationForPCA(); }
        public override float[][] GetCurrentModelVariablesForPCA()
        { observations++; Calls.Add("model:" + observations); return new[] { new[] { (float)observations, 2f * observations } }; }
    }

    private sealed class TracedGame : GameDefinition
    {
        public List<string> Calls;
        public TracedGame() { Players = new(); OptionSetName = "case"; }
        public override bool PlayMultipleScenarios => true;
        public override int NumPostWarmupPossibilities => 3;
        public override string GetNameForScenario() => "scenario " + CurrentPostWarmupScenarioIndex;
        public override void SetScenario(int overallScenarioIndex, bool warmupVersion, bool alwaysResetScenario)
        { CurrentPostWarmupScenarioIndex = overallScenarioIndex; Calls.Add("scenario:" + overallScenarioIndex); }
    }
}
