using System;
using System.Linq;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ACESimBase.GameSolvingSupport.FastCFR;
using ACESimBase.Util.Collections;

namespace ACESimBase.GameSolvingSupport.FastCFR.Tests
{
    public static class TestTrees
    {
        public static void InitializeVectorPolicies(
            FastCFRInformationSetVec node,
            double[][] ownerPolicyByLane,
            double[][] oppPolicyByLane)
        {
            node.InitializeIterationVec(ownerPolicyByLane, oppPolicyByLane);
        }
    }

    [TestClass]
    public class FastCFRVectorTests
    {
        [TestMethod]
        public void DotPerLane_Works()
        {
            double[] weights = { 0.2, 0.3, 0.5 };

            double[] action0LaneValues = { 1, 2, 3, 4 };
            double[] action1LaneValues = { 10, 20, 30, 40 };
            double[] action2LaneValues = { -1, 0, 1, 2 };

            double[][] perActionLaneValues = { action0LaneValues, action1LaneValues, action2LaneValues };

            byte[] activeMask = { 1, 1, 0, 1 };

            double[] resultPerLane = new double[4];

            FastCFRVecMath.DotPerLane(weights, perActionLaneValues, activeMask, resultPerLane);

            resultPerLane[0].Should().BeApproximately(2.7, 1e-12);
            resultPerLane[1].Should().BeApproximately(6.4, 1e-12);
            resultPerLane[2].Should().BeApproximately(0.0, 1e-12);
            resultPerLane[3].Should().BeApproximately(13.8, 1e-12);
        }

        // -----------------------------
        // Additional game-context-free tests
        // -----------------------------

        [TestMethod]
        public void DotPerLane_RandomizedParityWithScalar()
        {
            var rnd = new Random(12345);

            for (int trial = 0; trial < 64; trial++)
            {
                int actions = rnd.Next(1, 8);
                int lanes   = rnd.Next(1, 8);

                var w = Enumerable.Range(0, actions).Select(_ => rnd.NextDouble()).ToArray();
                double sumW = w.Sum();
                if (sumW == 0) w[0] = 1.0; else for (int i = 0; i < actions; i++) w[i] /= sumW;

                var perAction = new double[actions][];
                for (int a = 0; a < actions; a++)
                {
                    perAction[a] = Enumerable.Range(0, lanes).Select(_ => rnd.NextDouble() * 2 - 1).ToArray();
                }

                var mask = Enumerable.Range(0, lanes).Select(_ => (byte)(rnd.NextDouble() < 0.2 ? 0 : 1)).ToArray();
                var got  = new double[lanes];

                FastCFRVecMath.DotPerLane(w, perAction, mask, got); // vector path under test

                // scalar baseline
                var exp = new double[lanes];
                for (int k = 0; k < lanes; k++)
                {
                    exp[k] = mask[k] == 0 ? 0.0 : Enumerable.Range(0, actions).Sum(a => w[a] * perAction[a][k]);
                }

                for (int k = 0; k < lanes; k++)
                    got[k].Should().BeApproximately(exp[k], 1e-12);
            }
        }

        [TestMethod]
        public void FinalVec_PassesThroughUtilities()
        {
            int lanes = 4;
            int players = 2;

            var utils = new double[players][];
            utils[0] = new double[] { 1, 2, 3, 4 };
            utils[1] = new double[] { 10, 20, 30, 40 };

            var custom = new FloatSet[lanes]; // default values ok
            var node   = new FastCFRFinalVec(utils, custom);

            var ctx = new FastCFRVecContext
            {
                IterationNumber = 1,
                OptimizedPlayerIndex = 0,
                ReachSelf = Enumerable.Repeat(1.0, lanes).ToArray(),
                ReachOpp  = Enumerable.Repeat(1.0, lanes).ToArray(),
                ReachChance = Enumerable.Repeat(1.0, lanes).ToArray(),
                SamplingCorrection = 1.0,
                ScenarioIndex = Enumerable.Repeat(0, lanes).ToArray(),
                ActiveMask = Enumerable.Repeat((byte)1, lanes).ToArray(),
                Rand01ForDecision = _ => 0.0
            };

            var r = node.GoVec(ref ctx);
            r.UtilitiesByPlayerByLane[0].Should().BeEquivalentTo(utils[0]);
            r.UtilitiesByPlayerByLane[1].Should().BeEquivalentTo(utils[1]);
        }

        [TestMethod]
        public void ChanceVec_StaticProbabilities_Aggregates_ByLane_AndRestoresContext()
        {
            int lanes = 4;
            int players = 2;

            var childUtils = new double[players][];
            childUtils[0] = new double[] {  1,  2,  3,  4 };
            childUtils[1] = new double[] { 10, 20, 30, 40 };
            var child = new FastCFRFinalVec(childUtils, new FloatSet[lanes]);

            // One step, static probability 0.25
            var step = new FastCFRChanceStepVec(outcomeIndexOneBased: 1, childAccessor: () => child, staticProbability: 0.25);
            var visit = new FastCFRChanceVisitProgramVec(new[] { step }, players);
            var node  = new FastCFRChanceVec(decisionIndex: 0, numOutcomes: 4, numPlayers: players, visits: new[] { visit });
            node.BindChildrenAfterFinalize();

            var ctx = new FastCFRVecContext
            {
                IterationNumber = 1,
                OptimizedPlayerIndex = 0,
                ReachSelf = Enumerable.Repeat(1.0, lanes).ToArray(),
                ReachOpp  = Enumerable.Repeat(1.0, lanes).ToArray(),
                ReachChance = Enumerable.Repeat(1.0, lanes).ToArray(),
                SamplingCorrection = 1.0,
                ScenarioIndex = Enumerable.Repeat(0, lanes).ToArray(),
                ActiveMask = new byte[] { 1, 0, 1, 1 }, // lane 1 inactive
                Rand01ForDecision = _ => 0.0
            };

            // Keep copies to ensure restoration
            var origOpp    = (double[])ctx.ReachOpp.Clone();
            var origChance = (double[])ctx.ReachChance.Clone();
            var origMask   = (byte[])ctx.ActiveMask.Clone();

            var r = node.GoVec(ref ctx); // exercise vector chance with static p

            // Expected: each active lane scaled by 0.25; inactive lane is zeroed
            r.UtilitiesByPlayerByLane[0].Should().BeEquivalentTo(new double[] { 0.25, 0.0, 0.75, 1.0 });
            r.UtilitiesByPlayerByLane[1].Should().BeEquivalentTo(new double[] { 2.5,  0.0, 7.5, 10.0 });

            // Context must be restored (mask and reach vectors)
            ctx.ActiveMask.Should().BeEquivalentTo(origMask);
            ctx.ReachOpp.Should().BeEquivalentTo(origOpp);
            ctx.ReachChance.Should().BeEquivalentTo(origChance);
        }

        [TestMethod]
        public void ChanceVec_DynamicProbabilities_Provider_IsUsed_PerLane()
        {
            int lanes = 3;
            int players = 2;

            var childUtils = new double[players][];
            childUtils[0] = Enumerable.Repeat(1.0, lanes).ToArray();
            childUtils[1] = Enumerable.Repeat(2.0, lanes).ToArray();
            var child = new FastCFRFinalVec(childUtils, new FloatSet[lanes]);

            FastCFRProbProviderVec provider = (ref FastCFRVecContext _ctx, byte _outcomeIndexOneBased, Span<double> pLane) =>
            {
                for (int k = 0; k < pLane.Length; k++) pLane[k] = 0.1 * (k + 1);
            };

            var step = new FastCFRChanceStepVec(outcomeIndexOneBased: 1, childAccessor: () => child, provider);
            var visit = new FastCFRChanceVisitProgramVec(new[] { step }, players);
            var node  = new FastCFRChanceVec(decisionIndex: 0, numOutcomes: 3, numPlayers: players, visits: new[] { visit });
            node.BindChildrenAfterFinalize();

            var ctx = new FastCFRVecContext
            {
                IterationNumber = 1,
                OptimizedPlayerIndex = 0,
                ReachSelf = Enumerable.Repeat(1.0, lanes).ToArray(),
                ReachOpp  = Enumerable.Repeat(1.0, lanes).ToArray(),
                ReachChance = Enumerable.Repeat(1.0, lanes).ToArray(),
                SamplingCorrection = 1.0,
                ScenarioIndex = Enumerable.Repeat(0, lanes).ToArray(),
                ActiveMask = Enumerable.Repeat((byte)1, lanes).ToArray(),
                Rand01ForDecision = _ => 0.0
            };

            var r = node.GoVec(ref ctx);

            var exp0 = new[] { 0.1, 0.2, 0.3 };
            var exp1 = new[] { 0.2, 0.4, 0.6 };

            for (int k = 0; k < lanes; k++)
                r.UtilitiesByPlayerByLane[0][k].Should().BeApproximately(exp0[k], 1e-12);

            for (int k = 0; k < lanes; k++)
                r.UtilitiesByPlayerByLane[1][k].Should().BeApproximately(exp1[k], 1e-12);
        }

    }
}
