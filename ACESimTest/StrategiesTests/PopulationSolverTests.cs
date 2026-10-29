using ACESimBase.GameSolvingSupport.Evaluation;
using ACESimBase.GameSolvingSupport.Policies;
using ACESimBase.GameSolvingSupport.Population;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Linq;
using System.Threading.Tasks;
using N = ACESimBase.GameSolvingSupport.Evaluation.PolicyGameTree.Node;

namespace ACESimTest.StrategiesTests;

[TestClass]
public class PopulationSolverTests
{
    private static InformationSetPolicy Info(int i, byte player, int actions = 2) =>
        new(i.ToString(), player, (byte)i, Enumerable.Range(0, actions).Select(a => a.ToString()), Enumerable.Repeat(1.0 / actions, actions));
    private static PlayerPolicy Pure(PolicyGameTree game, byte player, int action) => game.MakePolicy(player,
        i => Enumerable.Range(0, game.Layout[i].Probabilities.Count).Select(a => a == action ? 1.0 : 0).ToArray());

    [TestMethod]
    public void ResponseToHiddenOpponentDoesNotObserveItsPolicyIdentity()
    {
        var game = new PolicyGameTree(N.Decision(0, N.Decision(1, N.Terminal(1, 0), N.Terminal(0, 0)),
            N.Decision(1, N.Terminal(0, 0), N.Terminal(1, 0))), [Info(0, 1), Info(1, 0)]);
        var response = game.BestResponse(0, [Pure(game, 1, 0), Pure(game, 1, 1)], [.5, .5]);
        Assert.AreEqual(.5, response.Utility, 1e-12, "A clairvoyant response would incorrectly receive one.");
        Assert.AreEqual(response.Utility, response.ReplayUtility, 1e-12);
    }

    [TestMethod]
    public void HiddenMixtureUsesSequenceReachRatherThanActionAverages()
    {
        N Guess(int info, int first) => N.Decision(info, N.Terminal(first == 0 ? 1 : 0, 0), N.Terminal(first == 1 ? 1 : 0, 0));
        var game = new PolicyGameTree(N.Decision(0, N.Decision(1, Guess(3, 0), Guess(4, 0)),
            N.Decision(2, Guess(3, 1), Guess(4, 1))), [Info(0, 1), Info(1, 1), Info(2, 1), Info(3, 0), Info(4, 0)]);
        var a = game.MakePolicy(1, i => new[] { 1.0, 0 });
        var b = game.MakePolicy(1, i => i == 1 ? new[] { 1.0, 0 } : new[] { 0.0, 1 });
        var mix = game.Mix([a, b], [.5, .5]);
        CollectionAssert.AreEqual(new[] { 0.0, 1 }, mix.InformationSets["2"].Probabilities.ToArray());
        Assert.AreEqual(1, game.BestResponse(0, [a, b], [.5, .5]).Utility, 1e-12);
        foreach (int firstGuess in new[] { 0, 1 }) foreach (int secondGuess in new[] { 0, 1 })
        {
            var p = game.MakePolicy(0, i => Enumerable.Range(0, 2).Select(k => k == (i == 3 ? firstGuess : secondGuess) ? 1.0 : 0).ToArray());
            var expected = .5 * game.Evaluate(p, a)[0] + .5 * game.Evaluate(p, b)[0];
            Assert.AreEqual(expected, game.Evaluate(p, mix)[0], 1e-12);
        }
    }

    [TestMethod]
    public void MixtureConditionsCorrectlyOnUnderflowingOwnSequences()
    {
        var game = new PolicyGameTree(N.Decision(0, N.Decision(1, N.Decision(2,
            N.Terminal(0, 0), N.Terminal(1, 0)), N.Terminal(0, 0)), N.Terminal(0, 0)), [Info(0, 1), Info(1, 1), Info(2, 1)]);
        var rare = game.MakePolicy(1, i => i < 2 ? new[] { 1e-200, 1.0 } : new[] { 0.0, 1 });
        var absent = game.MakePolicy(1, i => new[] { 0.0, 1 });
        var mixture = game.Mix([rare, absent], [.5, .5]);
        CollectionAssert.AreEqual(new[] { 0.0, 1 }, mixture.InformationSets["2"].Probabilities.ToArray());
    }

    [TestMethod]
    public void JointOutcomeReplayPreservesCorrelationAndDoesNotMultiplyMarginals()
    {
        var game = new PolicyGameTree(N.Decision(0, N.Decision(1, N.Terminal(3, 2), N.Terminal(0, 0)),
            N.Decision(1, N.Terminal(0, 0), N.Terminal(1, 4))), [Info(0, 0), Info(1, 1)]);
        double utility = 0; int paths = 0;
        double mass = game.VisitJointTerminals([Pure(game, 0, 0), Pure(game, 0, 1)], [Pure(game, 1, 0), Pure(game, 1, 1)], [.5, 0, 0, .5],
            (actions, probability) => { Assert.AreEqual(actions[0], actions[1]); paths++; utility += probability * (actions[0] == 1 ? 3 : 1); });
        Assert.AreEqual(1, mass, 1e-12); Assert.AreEqual(2, paths); Assert.AreEqual(2, utility, 1e-12);
        Assert.AreEqual(1, game.Evaluate(game.Uniform(0), game.Uniform(1))[0], 1e-12);
    }

    [TestMethod]
    public void ChanceBoundaryRoundoffIsCorrectedButInvalidProbabilitiesAreRejected()
    {
        var game = new PolicyGameTree(N.Chance([-2.220446049250313e-16, 1.0000000000000002],
            N.Terminal(999, 999), N.Terminal(2, 3)), []);
        Assert.AreEqual(2, game.ChanceBoundaryCorrections);
        CollectionAssert.AreEqual(new[] { 2.0, 3.0 }, game.Evaluate(game.Uniform(0), game.Uniform(1)));
        Assert.ThrowsException<System.IO.InvalidDataException>(() => new PolicyGameTree(
            N.Chance([-1e-6, 1.000001], N.Terminal(0, 0), N.Terminal(1, 1)), []));
    }

    [TestMethod]
    public void SparseEvaluationPreservesRareMassAndDoesNotReuseUnreachableValues()
    {
        var game = new PolicyGameTree(N.Chance([0, 1], N.Terminal(999, 999),
            N.Decision(0, N.Decision(1, N.Terminal(3, 2), N.Terminal(7, 5)),
                N.Decision(1, N.Terminal(11, 13), N.Terminal(17, 19)))), [Info(0, 0), Info(1, 1)]);
        foreach (double p in new[] { 0.0, 1.0, .25, 0.0, 1e-200, 1.0 })
        foreach (double d in new[] { 1.0, 0.0, .75, 1e-200 })
        {
            var plaintiff = game.MakePolicy(0, _ => [p, 1 - p]);
            var defendant = game.MakePolicy(1, _ => [d, 1 - d]);
            double expected = p * (d * 3 + (1 - d) * 7) + (1 - p) * (d * 11 + (1 - d) * 17);
            Assert.AreEqual(expected, game.Evaluate(plaintiff, defendant)[0], 1e-12);
            double replay = 0, rare = 0;
            game.VisitJointTerminals([plaintiff], [defendant], [1], (actions, mass) =>
            {
                Assert.AreEqual((byte)2, actions[0]);
                replay += mass * (actions[1] == 1 ? actions[2] == 1 ? 3 : 7 : actions[2] == 1 ? 11 : 17);
                if (actions[1] == 1) rare += mass;
            });
            Assert.AreEqual(expected, replay, 1e-12);
            if (p == 1e-200) Assert.AreEqual(p, rare, 1e-212);
        }
    }

    [TestMethod]
    public void ImperfectRecallIsRejectedInsteadOfSilentlyCertifying()
    {
        Assert.ThrowsException<NotSupportedException>(() => new PolicyGameTree(N.Decision(0,
            N.Decision(1, N.Terminal(0, 0), N.Terminal(1, 0)), N.Decision(1, N.Terminal(2, 0), N.Terminal(3, 0))), [Info(0, 0), Info(1, 0)]));
    }

    [DataTestMethod]
    [DataRow(1e-5)]
    [DataRow(1e-7)]
    public void ScaledQuadraticConstraintsPreserveRareEventIncentives(double amplitude)
    {
        var utility = new[] { new[] { 3.0, 0.0, 0.0, 1.0 }, new[] { 2.0, 0.0, 0.0, 4.0 } }
            .Select(u => u.Select(x => amplitude * x).ToArray()).ToArray();
        double tolerance = amplitude * 1e-4;
        var rare = CoarseCorrelatedSelector.Select(utility, 2, 2, [1, 1], JointSelectionObjective.MaximumGini,
            settings: new(IncentiveTolerance: tolerance, ScaleQuadraticIncentives: true));
        var ordinary = CoarseCorrelatedSelector.Select(utility.Select(u => u.Select(x => x / amplitude).ToArray()).ToArray(),
            2, 2, [1, 1], JointSelectionObjective.MaximumGini, settings: new(IncentiveTolerance: tolerance / amplitude));
        Assert.IsTrue(rare.RestrictedGains.Max() <= tolerance * 1.01);
        for (int i = 0; i < 4; i++) Assert.AreEqual(ordinary.Weights[i], rare.Weights[i], 1e-6);
    }

    [DataTestMethod]
    [DataRow(JointSelectionObjective.MaximumGini)]
    [DataRow(JointSelectionObjective.RelativeEntropy)]
    public void RestrictedSelectorSatisfiesIncentivesOnAsymmetricCoordination(JointSelectionObjective objective)
    {
        var result = CoarseCorrelatedSelector.Select([[3, 0, 0, 1], [2, 0, 0, 4]], 2, 2, [3, 4], objective);
        Assert.IsTrue(result.RestrictedGains.Max() < 2e-7);
        Assert.AreEqual(1, result.Weights.Sum(), 1e-12);
        Assert.IsTrue(result.Weights.All(x => x >= 0));
        if (result.DualityGap.HasValue) Assert.IsTrue(Math.Abs(result.DualityGap.Value) < 1e-5);
    }

    [DataTestMethod]
    [DataRow(JointSelectionObjective.MaximumGini)]
    [DataRow(JointSelectionObjective.RelativeEntropy)]
    public void ResponseExpansionFindsMissingRockPaperScissorsPolicies(JointSelectionObjective objective)
    {
        double Payoff(int i, int j) => i == j ? 0 : (i - j + 3) % 3 == 1 ? 1 : -1;
        var root = N.Decision(0, Enumerable.Range(0, 3).Select(i => N.Decision(1,
            Enumerable.Range(0, 3).Select(j => N.Terminal(Payoff(i, j), -Payoff(i, j))).ToArray())).ToArray());
        var game = new PolicyGameTree(root, [Info(0, 0, 3), Info(1, 1, 3)]);
        var run = new JpsroCoordinator(game, [new("rock", Pure(game, 0, 0))], [new("rock", Pure(game, 1, 0))]);
        var result = run.Run(objective, new(MaximumIterations: 15));
        Assert.AreEqual("FullGameCceVerified", result.Status);
        Assert.AreEqual(3, result.Plaintiffs.Length); Assert.AreEqual(3, result.Defendants.Length);
        Assert.IsTrue(result.NormalizedGains.Max() <= 1e-5);
        Assert.AreEqual(0, result.ExpectedValues[0], 1e-6);
        Assert.IsTrue(result.History.Length >= 3);
    }

    [TestMethod]
    public void RelativeEntropySelectionRespectsCloneAdjustedReferenceMass()
    {
        var original = CoarseCorrelatedSelector.Select([[3, 0, 0, 1], [2, 0, 0, 4]], 2, 2, [3, 4], JointSelectionObjective.RelativeEntropy);
        var cloned = CoarseCorrelatedSelector.Select([[3, 0, 0, 1, 0, 1], [2, 0, 0, 4, 0, 4]], 3, 2, [3, 4],
            JointSelectionObjective.RelativeEntropy, [.25, .25, .125, .125, .125, .125]);
        var aggregate = new[] { cloned.Weights[0], cloned.Weights[1], cloned.Weights[2] + cloned.Weights[4], cloned.Weights[3] + cloned.Weights[5] };
        for (int i = 0; i < 4; i++) Assert.AreEqual(original.Weights[i], aggregate[i], 1e-6);
        Assert.IsTrue(cloned.RestrictedGains.Max() < 2e-7);
    }

    [DataTestMethod]
    [DataRow(JointSelectionObjective.MaximumGini)]
    [DataRow(JointSelectionObjective.RelativeEntropy)]
    public void LargeRestrictedGameMatchesIndependentDenseIncentives(JointSelectionObjective objective)
    {
        // More than 2,048 profiles exercises the large-game Gini dual as well as KL.
        // Uneven copies of R/P/S require active constraints; uniform joint weights fail.
        int columns = 701;
        int Type(int j) => j < 600 ? 0 : j < 700 ? 1 : 2;
        double Payoff(int i, int j) => i == Type(j) ? 0 : (i - Type(j) + 3) % 3 == 1 ? 1 : -1;
        var values = new[] { new double[3 * columns], new double[3 * columns] };
        for (int i = 0; i < 3; i++) for (int j = 0; j < columns; j++)
        { values[0][i * columns + j] = 1000 + Payoff(i, j); values[1][i * columns + j] = -700 - Payoff(i, j); }
        var result = CoarseCorrelatedSelector.Select(values, 3, columns, [2, 2], objective);
        Assert.IsTrue(result.Algorithm.StartsWith("factored-"));
        for (int d = 0; d < 3 + columns; d++)
        {
            double gain = 0;
            for (int i = 0; i < 3; i++) for (int j = 0; j < columns; j++)
                gain += result.Weights[i * columns + j] * (d < 3
                    ? values[0][d * columns + j] - values[0][i * columns + j]
                    : values[1][i * columns + d - 3] - values[1][i * columns + j]) / 2;
            Assert.AreEqual(gain, result.RestrictedGains[d], 1e-12);
            Assert.IsTrue(gain < 2e-7);
        }
        for (int i = 0; i < 3; i++)
            Assert.AreEqual(1.0 / 3, result.Weights.Skip(i * columns).Take(columns).Sum(), 2e-6);
        for (int type = 0; type < 3; type++)
            Assert.AreEqual(1.0 / 3, result.Weights.Where((_, k) => Type(k % columns) == type).Sum(), 2e-6);
    }

    [TestMethod]
    public void AffinityDoesNotCountExactCloneMultiplicityInBlockKernel()
    {
        var kernel = new double[6, 6];
        for (int i = 0; i < 6; i++) for (int j = 0; j < 6; j++) kernel[i, j] = (i == 0) == (j == 0) ? 1 : 0;
        var reference = AffinityReference.Maximize(kernel);
        Assert.AreEqual(.5, reference.Weights[0], 1e-7);
        Assert.AreEqual(.5, reference.Weights.Skip(1).Sum(), 1e-7);
        Assert.AreEqual(.5, reference.Entropy, 1e-7);
        Assert.IsTrue(reference.KktResidual < 1e-7);
    }

    [TestMethod]
    public void AffinityNearCopiesHaveSmallAggregateWeightEffectAndPositiveSupport()
    {
        var original = AffinityReference.FromSignatures([[0], [1]]);
        var copies = AffinityReference.FromSignatures([[0], [1], [1.0001], [.9999], [1.0002]]);
        Assert.AreEqual(original.Weights[0], copies.Weights[0], .002);
        Assert.IsTrue(copies.Weights.All(x => x > 0));
        Assert.AreEqual(1, copies.Weights.Sum(), 1e-12);
    }

    [TestMethod]
    public void BudgetExhaustionCannotBeReportedAsConvergence()
    {
        var game = new PolicyGameTree(N.Decision(0, N.Decision(1, N.Terminal(0, 0), N.Terminal(-1, 1)),
            N.Decision(1, N.Terminal(1, -1), N.Terminal(0, 0))), [Info(0, 0), Info(1, 1)]);
        var result = new JpsroCoordinator(game, [new("seed", Pure(game, 0, 0))], [new("seed", Pure(game, 1, 0))])
            .Run(JointSelectionObjective.MaximumGini, new(MaximumIterations: 1));
        Assert.AreEqual("IterationLimit", result.Status);
        Assert.IsTrue(result.NormalizedGains.Max() > .1);
    }

    [TestMethod]
    public async Task DetachedEvaluationAndResponseAgreeWithExistingEngineAndPreserveTraining()
    {
        var developer = await BehavioralPolicyTests.Create();
        var before = developer.InformationSets.Select(n => n.NodeInformation.Cast<double>().ToArray()).ToArray();
        var profile = PolicyAccess.CaptureCurrent(developer, "test");
        var tree = PolicyGameTree.Create(developer);
        var value = tree.Evaluate(profile.ForPlayer(0), profile.ForPlayer(1));
        var expected = PolicyEvaluator.Evaluate(developer, profile);
        for (int p = 0; p < 2; p++) Assert.AreEqual(expected.Utilities[p], value[p], 1e-12);
        var response = tree.BestResponse(0, [profile.ForPlayer(1)], [1]);
        Assert.AreEqual(1.5, response.Utility, 1e-12);
        for (int i = 0; i < before.Length; i++) CollectionAssert.AreEqual(before[i], developer.InformationSets[i].NodeInformation.Cast<double>().ToArray());
    }
}
