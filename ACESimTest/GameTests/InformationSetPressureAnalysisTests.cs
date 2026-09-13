using ACESim;
using ACESimBase.Games.LitigGame.ManualReports;
using ACESimBase.GameSolvingSupport.GameTree;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using static ACESimBase.Games.LitigGame.ManualReports.InformationSetPressureAnalysis;

namespace ACESimTest.GameTests;

[TestClass, DoNotParallelize]
public class InformationSetPressureAnalysisTests
{
    private static StrategiesDeveloperBase developer;
    private static Profile baseline;

    [ClassInitialize]
    public static async Task Initialize(TestContext context)
    {
        var options = ArticleWorkedPathExtraction.CreateOptions("Specification-ModerateRiskAversion__Cost-1__Fee-British");
        options.NumLiabilitySignals = 2; options.NumOffers = 2;
        developer = await ArticleWorkedPathExtraction.InitializeAsync(options);
        // Synthetic full-support profile, intentionally not claimed to be equilibrium.
        foreach (var node in developer.InformationSets)
            node.SetCurrentProbabilities(new[] { .31, .69 });
        baseline = Capture(developer, "synthetic");
        baseline = WithObservedReach(baseline, Describe(developer, baseline));
    }

    [TestMethod]
    public void FullBestResponseAgreesWithIndependentExhaustiveSmallGameSearch()
    {
        foreach (byte player in new byte[] { 0, 1 })
        {
            var result = Respond(developer, baseline, player, "test");
            // With 2 signals and 2 offers, each type has 5 relevant pure plans:
            // do not participate, or participate with one of 2 exits and 2 offers.
            // This enumerates ALL pure plans, without monotonicity or symmetry.
            double best = double.NegativeInfinity;
            for (int a = 0; a < 5; a++) for (int b = 0; b < 5; b++)
            {
                var entries = baseline.Strategies.ToDictionary(x => x.Key, x => x.Value);
                foreach (var node in developer.InformationSets.Where(n => n.PlayerIndex == player))
                {
                    int signal = node.InformationSetContents[0];
                    int plan = signal == 1 ? a : b;
                    int action = Component(node.DecisionByteCode) switch
                    {
                        "participation" => plan == 0 ? 2 : 1,
                        "exit" => plan == 0 ? 1 : (plan - 1) / 2 + 1,
                        "offers" => plan == 0 ? 1 : (plan - 1) % 2 + 1,
                        _ => throw new Exception()
                    };
                    var key = Key(node, developer.GameDefinition);
                    entries[key] = entries[key] with { Probabilities = new[] { action == 1 ? 1.0 : 0, action == 2 ? 1.0 : 0 } };
                }
                Apply(developer, baseline with { Strategies = entries });
                best = Math.Max(best, developer.GetAverageUtilities(false)[player]);
            }
            result.BestResponseUtility.Should().BeApproximately(best, 1e-8);
            result.ResponseUtility.Should().BeApproximately(best, 1e-8);
            result.MaxActionValueError.Should().BeLessThan(1e-8);
        }
        Apply(developer, baseline);
    }

    [TestMethod]
    public void DiagnosticProfileRejectsWrongOptionsMissingHistoriesAndNonEquilibriumWithoutMutation()
    {
        Apply(developer, baseline);
        void Check(Profile profile)
        {
            Action inspect = () => ArticlePressureAnalysis.InspectEquilibrium(developer, profile, baseline.OptionSet, new());
            inspect.Should().Throw<InvalidDataException>();
            foreach (var node in developer.InformationSets)
                node.GetCurrentProbabilitiesAsArray().Should().Equal(baseline.Strategies[Key(node, developer.GameDefinition)].Probabilities);
        }
        Check(baseline with { OptionSet = "different-options" });
        Check(baseline with { Strategies = baseline.Strategies.Skip(1).ToDictionary(x => x.Key, x => x.Value) });
        Check(baseline); // Deliberately non-equilibrium synthetic profile.
    }

    [TestMethod]
    public void DiagnosticSourceFingerprintsIncludeOptionalOverlayAndRemainBackwardCompatible()
    {
        var saved = new ArticlePressureAnalysis.Fingerprint("equ", "a");
        var actions = new ArticlePressureAnalysis.Fingerprint("actions", "b");
        var overlay = new ArticlePressureAnalysis.Fingerprint("profile", "c");
        var source = new ArticlePressureAnalysis.Loaded(null, saved, actions, 0, baseline, null, null);
        ArticlePressureAnalysis.SourceFingerprints(source).Should().Equal(saved, actions);
        ArticlePressureAnalysis.SourceFingerprints(source with { ProfileOverride = overlay }).Should().Equal(saved, actions, overlay);
    }

    [TestMethod]
    public void RespondDoesNotChangeTheLoadedStrategyAndReplaysHighTiePolicy()
    {
        Apply(developer, baseline);
        var low = Respond(developer, baseline, 0, "low");
        var high = Respond(developer, baseline, 0, "high", highTie: true);
        high.ResponseUtility.Should().BeApproximately(low.BestResponseUtility, 1e-7);
        foreach (var node in developer.InformationSets)
            node.GetCurrentProbabilitiesAsArray().Should().Equal(baseline.Strategies[Key(node, developer.GameDefinition)].Probabilities);
    }

    [TestMethod]
    public void ComponentSubstitutionIsNotCumulativeAndDoesNotChangeTheFocalPlayer()
    {
        var donor = baseline with { Strategies = baseline.Strategies.ToDictionary(x => x.Key,
            x => x.Value with { Probabilities = new[] { .9, .1 }, Donor = "donor" }) };
        foreach (string component in new[] { "none", "participation", "offers", "exit", "all" })
        {
            var result = Hybrid(baseline, donor, 1, component, "hybrid");
            foreach (var (key, strategy) in result.Strategies)
            {
                bool changed = strategy.Player == 1 && (component == "all" || Component(strategy.Decision) == component);
                strategy.Probabilities.Should().Equal((changed ? donor : baseline).Strategies[key].Probabilities);
            }
        }
    }

    [TestMethod]
    public void SelectionUpdatesBeliefsAndOffPathOwnHistoriesRetainCounterfactualValues()
    {
        var selective = baseline with { Strategies = baseline.Strategies.ToDictionary(x => x.Key, x =>
            x.Value.Player == 0 && Component(x.Value.Decision) == "participation"
                ? x.Value with { Probabilities = x.Value.Labels.Contains("Signal: 1") ? new[] { 0.0, 1.0 } : new[] { 1.0, 0.0 } }
                : x.Value) };
        var result = Respond(developer, selective, 1, "selection");
        foreach (var answer in result.InformationSets.Where(x => x.Decision == "D Answers"))
        {
            answer.OpponentSignalBeliefs[0].Should().Be(0);
            answer.OpponentSignalBeliefs[1].Should().BeApproximately(1, 1e-12);
        }
        var ownOffPath = result.InformationSets.Where(x => x.ActualOffPath && !x.CounterfactuallyUnreachable).ToArray();
        ownOffPath.Should().NotBeEmpty();
        ownOffPath.Should().OnlyContain(x => x.ConditionalUtility == null && x.Actions.All(a => a.CounterfactualConditionalUtility.HasValue));
        result.InformationSets.Where(x => x.Decision == "D Offer").Select(x => x.ExitCommitment).Distinct().Should().BeEquivalentTo(new int?[] { 1, 2 });
    }

    [TestMethod]
    public void OpponentZeroReachHasNoInventedBeliefsAndPreservesTheInputCompletion()
    {
        var noFiling = baseline with { Strategies = baseline.Strategies.ToDictionary(x => x.Key, x =>
            x.Value.Player == 0 && Component(x.Value.Decision) == "participation"
                ? x.Value with { Probabilities = new[] { 0.0, 1.0 } } : x.Value) };
        var result = Respond(developer, noFiling, 1, "no-filing");
        result.InformationSets.Should().OnlyContain(x => x.CounterfactuallyUnreachable && x.OpponentSignalBeliefs == null
            && x.Actions.All(a => a.CounterfactualConditionalUtility == null));
        foreach (var entry in result.Response.Strategies.Values.Where(x => x.Player == 1))
        {
            entry.Probabilities.Should().Equal(noFiling.Strategies[entry.Key].Probabilities);
            entry.DonorCounterfactuallyUnreachable.Should().BeTrue();
        }
    }

    [TestMethod]
    public void InvalidProfileIsRejectedBeforeMutationAndHistoryOrderIsSemantic()
    {
        Apply(developer, baseline);
        var bad = baseline with { Strategies = baseline.Strategies.Reverse().ToDictionary(x => x.Key, x => x.Value) };
        Apply(developer, bad); // Dictionary insertion order is not the mapping.
        var key = bad.Strategies.Keys.First();
        bad.Strategies[key] = bad.Strategies[key] with { Probabilities = new[] { double.NaN, 1.0 } };
        Action apply = () => Apply(developer, bad);
        apply.Should().Throw<InvalidDataException>();
        foreach (var node in developer.InformationSets)
            node.GetCurrentProbabilitiesAsArray().Should().Equal(baseline.Strategies[Key(node, developer.GameDefinition)].Probabilities);
    }

    [TestMethod]
    public void CompletionAuditIncludesUnplayedPoliciesAndSensitivityLeavesVisitedPoliciesAlone()
    {
        var unvisited = baseline with { Strategies = baseline.Strategies.ToDictionary(x => x.Key,
            x => x.Value with { DonorReach = 0 }) };
        var result = Respond(developer, unvisited, 0, "completion");
        result.ExposedOpponentSets.Should().NotBeEmpty();
        result.ExposedOpponentSets.Should().OnlyContain(x => x.FocalDeviationReachWeight > 0 && x.DonorReach == 0);
        result.NewlyReachedOpponentSets.Should().OnlyContain(x => x.Reach > ReachTolerance);
        var low = ArticlePressureAnalysis.ReplaceUnvisitedOpponentPolicies(unvisited, 1, false);
        foreach (var x in low.Strategies.Values)
            x.Probabilities.Should().Equal(x.Player == 1 ? new[] { 1.0, 0.0 } : baseline.Strategies[x.Key].Probabilities);
        var unchanged = ArticlePressureAnalysis.ReplaceUnvisitedOpponentPolicies(baseline, 1, true);
        foreach (var x in unchanged.Strategies.Values)
            x.Probabilities.Should().Equal(baseline.Strategies[x.Key].Probabilities);
    }

    [TestMethod]
    public void InterventionGuardAllowsOneFeeOrPreferenceChangeButNotConfoundedCosts()
    {
        var original = ArticleWorkedPathExtraction.CreateOptions("Specification-Baseline__Cost-1__Fee-American");
        ArticlePressureAnalysis.ValidateMatchedOptions(original, ArticleWorkedPathExtraction.CreateOptions("Specification-Baseline__Cost-1__Fee-British"));
        ArticlePressureAnalysis.ValidateMatchedOptions(original, ArticleWorkedPathExtraction.CreateOptions("Specification-ModerateRiskAversion__Cost-1__Fee-American"));
        Action cost = () => ArticlePressureAnalysis.ValidateMatchedOptions(original, ArticleWorkedPathExtraction.CreateOptions("Specification-Baseline__Cost-4__Fee-British"));
        cost.Should().Throw<InvalidDataException>();
        Action both = () => ArticlePressureAnalysis.ValidateMatchedOptions(original, ArticleWorkedPathExtraction.CreateOptions("Specification-ModerateRiskAversion__Cost-1__Fee-British"));
        both.Should().Throw<InvalidDataException>();
        Action neither = () => ArticlePressureAnalysis.ValidateMatchedOptions(original, original);
        neither.Should().Throw<InvalidDataException>();
    }

    [TestMethod]
    public void DirectionalHeadingsIdentifyInterventionAndFixedPrimitive()
    {
        const string american = "Specification-Baseline__Cost-1__Fee-American";
        const string british = "Specification-Baseline__Cost-1__Fee-British";
        const string americanRa = "Specification-ModerateRiskAversion__Cost-1__Fee-American";
        const string britishRa = "Specification-ModerateRiskAversion__Cost-1__Fee-British";
        var rnFees = LitigCharts.EquilibriumChangeTables.Heading(american, british);
        rnFees.Title.Should().Be("Fee shifting: American to British");
        rnFees.HeldFixed.Should().Be("Both players remain risk neutral");
        rnFees.Original.Should().Be("American rule, risk neutral");
        rnFees.Target.Should().Be("British rule, risk neutral");
        var raFees = LitigCharts.EquilibriumChangeTables.Heading(americanRa, britishRa);
        raFees.Title.Should().Be(rnFees.Title);
        raFees.HeldFixed.Should().Be("Both players remain moderately risk averse");
        foreach (var pair in new[] { (american, americanRa, "American"), (british, britishRa, "British") })
        {
            var heading = LitigCharts.EquilibriumChangeTables.Heading(pair.Item1, pair.Item2);
            heading.Title.Should().Be("Preferences: risk neutral to moderately risk averse");
            heading.HeldFixed.Should().Be(pair.Item3 + " rule remains in force");
        }
        LitigCharts.EquilibriumChangeTables.Heading(british, american).Title.Should().Be("Fee shifting: British to American");
        LitigCharts.EquilibriumChangeTables.Heading(americanRa, american).Title.Should().Be("Preferences: moderately risk averse to risk neutral");
    }

}
