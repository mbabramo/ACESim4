using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ACESim;
using ACESimBase.GameSolvingAlgorithms;
using ACESimBase.GameSolvingAlgorithms.ECTAAlgorithm;
using ACESimBase.GameSolvingSupport.ExactValues;
using ACESimBase.Games.LitigGame.ManualReports;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static ACESimBase.Games.LitigGame.ManualReports.InformationSetPressureAnalysis;

namespace ACESimTest.GameTests;

[TestClass, DoNotParallelize]
public class ECTATraceTests
{
    private static SequenceForm game;
    private static double[] prior;
    [ClassInitialize]
    public static async Task Initialize(TestContext context)
    {
        var options = ArticleWorkedPathExtraction.CreateOptions("Specification-ModerateRiskAversion__Cost-1__Fee-British");
        options.NumLiabilitySignals = 2; options.NumOffers = 2;
        game = (SequenceForm)await ArticleWorkedPathExtraction.InitializeAsync(options);
        prior = game.InformationSets.OrderBy(n => n.PlayerIndex).ThenBy(n => n.InformationSetNodeNumber)
            .SelectMany((n, i) => new[] { .1 + .04 * i, .9 - .04 * i }).ToArray();
    }

    [TestMethod]
    public void WarmStartPreservesEveryInformationSetAndInputArray()
    {
        game.DetermineGameNodeRelationships();
        var tree = new ECTATreeDefinition<ExactValue>(); game.SetupECTA(tree); tree.generateSequence();
        var initial = prior.Select(p => ExactValue.FromRational((Rationals.Rational)p)).ToArray();
        var copy = initial.Select(p => p.AsDouble).ToArray();
        tree.SetProbabilitiesToValues(initial, ExactValue.Zero());
        var engine = new ECTAStrategyDiagnostics<ExactValue>(tree, game.TraceOutcomeUtilities());
        engine.PriorProbabilities.Zip(copy, (a, b) => Math.Abs(a - b)).Max().Should().BeLessThan(1e-14);
        initial.Select(p => p.AsDouble).Should().Equal(copy);
        tree.SetProbabilitiesToValues(initial, ExactValue.FromRational((Rationals.Rational).2));
        var floored = new ECTAStrategyDiagnostics<ExactValue>(tree, game.TraceOutcomeUtilities()).PriorProbabilities;
        floored.Should().OnlyContain(p => p >= .2 - 1e-12 && p <= .8 + 1e-12);
        Action invalid = () => tree.SetProbabilitiesToValues(initial, ExactValue.FromRational((Rationals.Rational).6));
        invalid.Should().Throw<ArgumentException>();
    }

    [TestMethod]
    public void FastDiagnosticsAgreeWithTreeWalkAndUnrestrictedBestResponse()
    {
        game.DetermineGameNodeRelationships();
        var tree = new ECTATreeDefinition<ExactValue>(); game.SetupECTA(tree); tree.generateSequence();
        tree.SetProbabilitiesToValues(prior.Select(p => ExactValue.FromRational((Rationals.Rational)p)).ToArray(), ExactValue.Zero());
        var engine = new ECTAStrategyDiagnostics<ExactValue>(tree, game.TraceOutcomeUtilities());
        foreach (var policy in new[] { prior, prior.Select((_, i) => i % 2 == 0 ? 0.0 : 1.0).ToArray() })
        {
            ArticleWorkedPathExtraction.LoadProfile(game, policy);
            var profile = Capture(game, "test"); var reference = Describe(game, profile);
            var fast = engine.Evaluate(policy);
            if (policy == prior)
            {
                fast.OutsideSupportGaps.Where(v => v.HasValue).Should().OnlyContain(v => v.Value == 0);
                fast.LocalGaps.Should().Contain(v => v > 1e-6);
            }
            for (byte p = 0; p < 2; p++)
            {
                fast.Utilities[p].Should().BeApproximately(reference.Utilities[p], 1e-9);
                fast.BestResponseUtilities[p].Should().BeApproximately(Respond(game, profile, p, "check").BestResponseUtility, 1e-9);
            }
            int offset = 0;
            foreach (int i in engine.InformationSetIndices)
            {
                var key = Key(game.TraceInformationSets[i].InformationSetNode, game.GameDefinition);
                var info = reference.InformationSets.Single(s => s.Key == key);
                foreach (var a in info.Actions)
                {
                    if (a.CounterfactualConditionalUtility is double q)
                        fast.ActionUtilities[offset].Value.Should().BeApproximately(q, 1e-9);
                    else fast.ActionUtilities[offset].Should().BeNull();
                    offset++;
                }
            }
        }
    }

    [TestMethod]
    public void OriginalUnseededSolveIsUnchangedByObserver()
    {
        var without = game.TraceECTA<ExactValue>(maxPivots: 1000);
        var frames = new List<ECTAPivotSnapshot>();
        ECTAStrategyDiagnostics<ExactValue> engine = null;
        var with = game.TraceECTA<ExactValue>(beforeSolve: t =>
        {
            engine = new(t, game.TraceOutcomeUtilities());
            engine.PriorProbabilities.Should().OnlyContain(p => p == .5);
        }, afterPivot: (t, f) => frames.Add(f), maxPivots: 1000);
        with.Should().Equal(without);
        frames.Select(f => f.Pivot).Should().Equal(Enumerable.Range(1, frames.Count));
        engine.Project(frames.First()).Probabilities.Should().OnlyContain(p => p == .5);
        engine.Evaluate(engine.Project(frames.Last()).Probabilities).Epsilon.Should().BeLessThan(1e-8);
    }

    [TestMethod]
    public void ObserverCapturesEveryPivotWithoutChangingSolution()
    {
        var without = game.TraceECTA<ExactValue>(prior, maxPivots: 1000);
        ECTAStrategyDiagnostics<ExactValue> engine = null;
        var frames = new List<ECTAPivotSnapshot>(); ECTAIncentives last = null;
        var with = game.TraceECTA<ExactValue>(prior,
            t => { engine = new(t, game.TraceOutcomeUtilities()); engine.PriorProbabilities.Zip(prior, (a, b) => Math.Abs(a - b)).Max().Should().BeLessThan(1e-14); },
            (t, f) =>
            {
                frames.Add(f); last = engine.Evaluate(engine.Project(f).Probabilities);
                for (int i = 0; i < f.Z.Length; i++)
                {
                    double slack = t.Lemke.rhsq[i].AsDouble + t.Lemke.coveringVectorD[i].AsDouble * f.Auxiliary;
                    for (int j = 0; j < f.Z.Length; j++) slack += t.Lemke.lcpM[i][j].AsDouble * f.Z[j];
                    f.W[i].Should().BeApproximately(slack, 1e-7);
                }
            }, maxPivots: 1000);
        with.Should().Equal(without);
        frames.Should().NotBeEmpty(); frames.Select(f => f.Pivot).Should().Equal(Enumerable.Range(1, frames.Count));
        frames.Last().Final.Should().BeTrue(); frames.Last().Auxiliary.Should().Be(0);
        frames.Last().OriginalFeasibilityViolation.Should().BeLessThan(1e-7);
        last.Epsilon.Should().BeLessThan(1e-8);
        // The first pivot inserts z0 and the prior-completed projection is the supplied pair.
        engine.Project(frames.First()).Probabilities.Zip(prior, (a, b) => Math.Abs(a - b)).Max().Should().BeLessThan(1e-10);
    }
}
