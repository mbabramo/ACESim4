#nullable enable
using System;
using ACESimBase.GameSolvingSupport.FastCFR;
using ACESimBase.GameSolvingSupport.GameTree;
using ACESimBase.Util.Collections;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ACESimTest
{
    [TestClass]
    public sealed class FastCFRVectorTestsFile
    {
        private const int NumPlayers = 2;
        private const byte PlayerIndexOwner = 0;
        private const byte DecisionIndex = 0;
        private const byte NumActions = 2;

        // ------------------------------------------------------------
        // Equality tests: scalar (feature disabled) vs vector subtree
        // ------------------------------------------------------------

        [TestMethod]
        public void Utilities_Equal_ForRandomProbabilitiesAndPolicies()
        {
            int lanes = 4;
            double[] pLane = { 0.05, 0.20, 0.50, 0.25 };
            var ownerPolicyByLane = new double[lanes][];
            var oppPolicyByLane   = new double[lanes][];
            var utilsAction0ByLane = new double[NumPlayers][];
            var utilsAction1ByLane = new double[NumPlayers][];

            for (int k = 0; k < lanes; k++)
            {
                double w = 0.3 + 0.1 * k;
                ownerPolicyByLane[k] = new[] { w, 1.0 - w };
                oppPolicyByLane[k] = new[] { 0.6, 0.4 };

                double u0p = 10.0 + 2.0 * k;
                double u0d = -u0p;
                double u1p = -5.0 + 1.5 * k;
                double u1d = -u1p;

                if (k == 0)
                {
                    utilsAction0ByLane[0] = new double[lanes];
                    utilsAction0ByLane[1] = new double[lanes];
                    utilsAction1ByLane[0] = new double[lanes];
                    utilsAction1ByLane[1] = new double[lanes];
                }
                utilsAction0ByLane[0][k] = u0p;
                utilsAction0ByLane[1][k] = u0d;
                utilsAction1ByLane[0][k] = u1p;
                utilsAction1ByLane[1][k] = u1d;
            }

            var (scalarRoot, _) = TestTrees.BuildScalar_Chance_Info_Finals_WithInfosets(
                lanes, pLane, ownerPolicyByLane, oppPolicyByLane, utilsAction0ByLane, utilsAction1ByLane);

            var vectorRoot = TestTrees.BuildVector_Info_Finals(
                lanes, ownerPolicyByLane, oppPolicyByLane, utilsAction0ByLane, utilsAction1ByLane);

            var scalarCtx = new FastCFRIterationContext
            {
                IterationNumber = 0,
                OptimizedPlayerIndex = PlayerIndexOwner,
                ReachSelf = 1.0,
                ReachOpp = 1.0,
                ReachChance = 1.0,
                SamplingCorrection = 1.0,
                ScenarioIndex = 0,
                SuppressMath = false,
                Rand01ForDecision = _ => 0.0
            };

            var scalarResult = scalarRoot.Go(ref scalarCtx);

            var vctx = TestTrees.MakeDefaultVecContext(lanes, optimizedPlayerIndex: PlayerIndexOwner);
            TestTrees.InitializeVectorPolicies(vectorRoot, ownerPolicyByLane, oppPolicyByLane);

            var vecSubtree = vectorRoot.GoVec(ref vctx);
            var expectedU = new double[NumPlayers];
            var expectedCustom = default(FloatSet);
            for (int k = 0; k < lanes; k++)
            {
                expectedU[0] += pLane[k] * vecSubtree.UtilitiesByPlayerByLane[0][k];
                expectedU[1] += pLane[k] * vecSubtree.UtilitiesByPlayerByLane[1][k];
                expectedCustom = expectedCustom.Plus(vecSubtree.CustomByLane[k].Times((float)pLane[k]));
            }

            scalarResult.Utilities[0].Should().BeApproximately(expectedU[0], 1e-12);
            scalarResult.Utilities[1].Should().BeApproximately(expectedU[1], 1e-12);
            scalarResult.Custom.Equals(expectedCustom).Should().BeTrue();
        }

        [TestMethod]
        public void Regrets_And_CumulativeStrategyIncrements_Equal_PerLane()
        {
            int lanes = 4;
            double[] pLane = { 0.2, 0.3, 0.1, 0.4 };

            var ownerPolicyByLane = new double[lanes][];
            var oppPolicyByLane   = new double[lanes][];
            var utilsAction0ByLane = new double[NumPlayers][];
            var utilsAction1ByLane = new double[NumPlayers][];

            for (int k = 0; k < lanes; k++)
            {
                double w = 0.25 + 0.15 * (k % 3);
                ownerPolicyByLane[k] = new[] { w, 1.0 - w };
                oppPolicyByLane[k] = new[] { 0.55, 0.45 };

                double u0p = 2.0 * (k + 1);
                double u0d = -1.0 * (k + 2);
                double u1p = -3.0 + 0.5 * k;
                double u1d = -u1p;

                if (k == 0)
                {
                    utilsAction0ByLane[0] = new double[lanes];
                    utilsAction0ByLane[1] = new double[lanes];
                    utilsAction1ByLane[0] = new double[lanes];
                    utilsAction1ByLane[1] = new double[lanes];
                }
                utilsAction0ByLane[0][k] = u0p;
                utilsAction0ByLane[1][k] = u0d;
                utilsAction1ByLane[0][k] = u1p;
                utilsAction1ByLane[1][k] = u1d;
            }

            var (scalarRoot, scalarInfosets) = TestTrees.BuildScalar_Chance_Info_Finals_WithInfosets(
                lanes, pLane, ownerPolicyByLane, oppPolicyByLane, utilsAction0ByLane, utilsAction1ByLane);

            var vectorRoot = TestTrees.BuildVector_Info_Finals(
                lanes, ownerPolicyByLane, oppPolicyByLane, utilsAction0ByLane, utilsAction1ByLane);

            var scalarCtx = new FastCFRIterationContext
            {
                IterationNumber = 0,
                OptimizedPlayerIndex = PlayerIndexOwner,
                ReachSelf = 1.0,
                ReachOpp = 1.0,
                ReachChance = 1.0,
                SamplingCorrection = 1.0,
                ScenarioIndex = 0,
                SuppressMath = false,
                Rand01ForDecision = _ => 0.0
            };
            _ = scalarRoot.Go(ref scalarCtx);

            var vctx = TestTrees.MakeDefaultVecContext(lanes, optimizedPlayerIndex: PlayerIndexOwner);
            for (int k = 0; k < lanes; k++)
                vctx.ReachOpp[k] = pLane[k];
            TestTrees.InitializeVectorPolicies(vectorRoot, ownerPolicyByLane, oppPolicyByLane);
            _ = vectorRoot.GoVec(ref vctx);

            for (int a = 0; a < NumActions; a++)
            {
                var vecSr = vectorRoot.GetSumRegretTimesInversePi(a);
                var vecSi = vectorRoot.GetSumInversePi(a);
                var vecInc = vectorRoot.GetLastCumulativeStrategyIncrements(a);

                for (int k = 0; k < lanes; k++)
                {
                    double srScalar = scalarInfosets[k].SumRegretTimesInversePi[a];
                    double siScalar = scalarInfosets[k].SumInversePi[a];
                    double incScalar = scalarInfosets[k].LastCumulativeStrategyIncrements[a];

                    vecSr[k].Should().BeApproximately(srScalar, 1e-12, $"lane {k} action {a} regret");
                    vecSi[k].Should().BeApproximately(siScalar, 1e-12, $"lane {k} action {a} invPi");
                    vecInc[k].Should().BeApproximately(incScalar, 1e-12, $"lane {k} action {a} incr");
                }
            }
        }

        [TestMethod]
        public void LanePruning_Works_WithZeroProbabilityLanes()
        {
            int lanes = 4;
            double[] pLane = { 0.0, 0.35, 0.0, 0.65 };

            var ownerPolicyByLane = new double[lanes][];
            var oppPolicyByLane   = new double[lanes][];
            var utilsAction0ByLane = new double[NumPlayers][];
            var utilsAction1ByLane = new double[NumPlayers][];

            for (int k = 0; k < lanes; k++)
            {
                ownerPolicyByLane[k] = new[] { 0.5, 0.5 };
                oppPolicyByLane[k]   = new[] { 0.5, 0.5 };

                if (k == 0)
                {
                    utilsAction0ByLane[0] = new double[lanes];
                    utilsAction0ByLane[1] = new double[lanes];
                    utilsAction1ByLane[0] = new double[lanes];
                    utilsAction1ByLane[1] = new double[lanes];
                }
                utilsAction0ByLane[0][k] = 1 + k;
                utilsAction0ByLane[1][k] = -1 - k;
                utilsAction1ByLane[0][k] = 10 + k;
                utilsAction1ByLane[1][k] = -10 - k;
            }

            var (scalarRoot, _) = TestTrees.BuildScalar_Chance_Info_Finals_WithInfosets(
                lanes, pLane, ownerPolicyByLane, oppPolicyByLane, utilsAction0ByLane, utilsAction1ByLane);

            var vectorRoot = TestTrees.BuildVector_Info_Finals(
                lanes, ownerPolicyByLane, oppPolicyByLane, utilsAction0ByLane, utilsAction1ByLane);

            var scalarCtx = new FastCFRIterationContext
            {
                IterationNumber = 0,
                OptimizedPlayerIndex = PlayerIndexOwner,
                ReachSelf = 1.0,
                ReachOpp = 1.0,
                ReachChance = 1.0,
                SamplingCorrection = 1.0,
                ScenarioIndex = 0,
                SuppressMath = false,
                Rand01ForDecision = _ => 0.0
            };
            var scalar = scalarRoot.Go(ref scalarCtx);

            var vctx = TestTrees.MakeDefaultVecContext(lanes, optimizedPlayerIndex: PlayerIndexOwner);
            TestTrees.InitializeVectorPolicies(vectorRoot, ownerPolicyByLane, oppPolicyByLane);
            var vecSubtree = vectorRoot.GoVec(ref vctx);

            var u0 = 0.0; var u1 = 0.0; var custom = default(FloatSet);
            for (int k = 0; k < lanes; k++)
            {
                u0 += pLane[k] * vecSubtree.UtilitiesByPlayerByLane[0][k];
                u1 += pLane[k] * vecSubtree.UtilitiesByPlayerByLane[1][k];
                custom = custom.Plus(vecSubtree.CustomByLane[k].Times((float)pLane[k]));
            }

            scalar.Utilities[0].Should().BeApproximately(u0, 1e-12);
            scalar.Utilities[1].Should().BeApproximately(u1, 1e-12);
            scalar.Custom.Equals(custom).Should().BeTrue();
        }

        // -----------------------------------------
        // Math helper tests (SIMD masked operations)
        // -----------------------------------------

        [TestMethod]
        public void MulAccumulateMasked_Works()
        {
            double[] a = { 1, 2, 3, 4, 5, 6 };
            double[] b = { 10, 20, 30, 40, 50, 60 };
            byte[] m  = { 1, 0, 1, 0, 1, 0 };
            double[] dst = { 0, 0, 0, 0, 0, 0 };

            FastCFRVecMath.MulAccumulateMasked(a, b, m, dst);

            dst[0].Should().Be(10);
            dst[1].Should().Be(0);
            dst[2].Should().Be(90);
            dst[3].Should().Be(0);
            dst[4].Should().Be(250);
            dst[5].Should().Be(0);
        }

        [TestMethod]
        public void ScaleInPlaceMasked_Works()
        {
            double[] x = { 1, 2, 3, 4 };
            double[] f = { 10, 10, 10, 10 };
            byte[] m = { 0, 1, 0, 1 };

            FastCFRVecMath.ScaleInPlaceMasked(x, f, m);

            x[0].Should().Be(1);
            x[1].Should().Be(20);
            x[2].Should().Be(3);
            x[3].Should().Be(40);
        }

        [TestMethod]
        public void ReduceSumMasked_Works()
        {
            double[] x = { 1, 2, 3, 4, 5 };
            byte[] m =  { 1, 0, 1, 0, 1 };
            double s = FastCFRVecMath.ReduceSumMasked(x, m);
            s.Should().Be(1 + 3 + 5);
        }

        [TestMethod]
        public void DotPerLane_Works()
        {
            double[] w = { 0.2, 0.3, 0.5 };
            double[] a0 = { 1, 2, 3, 4 };
            double[] a1 = { 10, 20, 30, 40 };
            double[] a2 = { -1, 0, 1, 2 };
            ReadOnlySpan<double>[] perAction = { a0, a1, a2 };
            byte[] m = { 1, 1, 0, 1 };
            double[] r = new double[4];

            FastCFRVecMath.DotPerLane(w, perAction, m, r);

            r[0].Should().BeApproximately(2.7, 1e-12);
            r[1].Should().BeApproximately(6.4, 1e-12);
            r[2].Should().BeApproximately(0.0, 1e-12);
            r[3].Should().BeApproximately(13.8, 1e-12);
        }

        // -----------------------------------------
        // Test scaffolding: minimal scalar/vector trees
        // -----------------------------------------

        private static class TestTrees
        {
            public static (FastCFRChance root, FastCFRInformationSet[] infosets) BuildScalar_Chance_Info_Finals_WithInfosets(
                int lanes,
                double[] pLane,
                double[][] ownerPolicyByLane,
                double[][] oppPolicyByLane,
                double[][] utilsAction0ByLane_byPlayer,
                double[][] utilsAction1ByLane_byPlayer)
            {
                var infosets = new FastCFRInformationSet[lanes];

                for (int k = 0; k < lanes; k++)
                {
                    var final0 = new FastCFRFinal(new[] {
                        new[] { utilsAction0ByLane_byPlayer[0][k], utilsAction0ByLane_byPlayer[1][k] }
                    }, new[] { default(FloatSet) });

                    var final1 = new FastCFRFinal(new[] {
                        new[] { utilsAction1ByLane_byPlayer[0][k], utilsAction1ByLane_byPlayer[1][k] }
                    }, new[] { default(FloatSet) });

                    var steps = new[]
                    {
                        new FastCFRVisitStep(FastCFRVisitStepKind.ChildForAction, 0, () => final0),
                        new FastCFRVisitStep(FastCFRVisitStepKind.ChildForAction, 1, () => final1),
                    };
                    var visit = new FastCFRVisitProgram(steps, NumPlayers);

                    var info = new FastCFRInformationSet(PlayerIndexOwner, DecisionIndex, NumActions, new[] { visit });
                    info.InitializeIteration(ownerPolicyByLane[k], oppPolicyByLane[k]);
                    infosets[k] = info;
                }

                var chanceSteps = new FastCFRChanceStep[lanes];
                for (int k = 0; k < lanes; k++)
                {
                    int idx = k;
                    var childAccessor = new Func<IFastCFRNode>(() => infosets[idx]);
                    double probStatic = pLane[k];
                    chanceSteps[k] = new FastCFRChanceStep(childAccessor, probStatic);
                }

                var cvisit = new FastCFRChanceVisitProgram(chanceSteps, NumPlayers);
                var chance = new FastCFRChance(DecisionIndex, (byte)lanes, new[] { cvisit });
                chance.InitializeIteration(ReadOnlySpan<double>.Empty, ReadOnlySpan<double>.Empty);

                return (chance, infosets);
            }

            public static FastCFRInformationSetVec BuildVector_Info_Finals(
                int lanes,
                double[][] ownerPolicyByLane,
                double[][] oppPolicyByLane,
                double[][] utilsAction0ByLane_byPlayer,
                double[][] utilsAction1ByLane_byPlayer)
            {
                var final0 = new FastCFRFinalVec(
                    utilitiesByPlayerByLane: new[]
                    {
                        utilsAction0ByLane_byPlayer[0],
                        utilsAction0ByLane_byPlayer[1],
                    },
                    customByLane: new FloatSet[lanes]);

                var final1 = new FastCFRFinalVec(
                    utilitiesByPlayerByLane: new[]
                    {
                        utilsAction1ByLane_byPlayer[0],
                        utilsAction1ByLane_byPlayer[1],
                    },
                    customByLane: new FloatSet[lanes]);

                var steps = new[]
                {
                    new FastCFRVisitStepVec(0, () => final0),
                    new FastCFRVisitStepVec(1, () => final1),
                };
                var visit = new FastCFRVisitProgramVec(steps, NumPlayers);

                // Backing nodes are not used in these unit tests (copy-out is a builder concern).
                var backingPerLane = new InformationSetNode[lanes];
                var node = new FastCFRInformationSetVec(PlayerIndexOwner, DecisionIndex, NumActions, NumPlayers,
                    backingPerLane, new[] { visit });
                node.BindChildrenAfterFinalizeVec();

                // Freeze policies for the test run
                InitializeVectorPolicies(node, ownerPolicyByLane, oppPolicyByLane);
                return node;
            }

            public static FastCFRVecContext MakeDefaultVecContext(int lanes, byte optimizedPlayerIndex)
            {
                var reachSelf = new double[lanes];
                var reachOpp = new double[lanes];
                var reachChance = new double[lanes];
                var mask = new byte[lanes];
                var scn = new int[lanes];

                for (int k = 0; k < lanes; k++)
                {
                    reachSelf[k] = 1.0;
                    reachOpp[k] = 1.0;
                    reachChance[k] = 1.0;
                    mask[k] = 1;
                    scn[k] = 0;
                }

                return new FastCFRVecContext
                {
                    IterationNumber = 0,
                    OptimizedPlayerIndex = optimizedPlayerIndex,
                    SamplingCorrection = 1.0,
                    ReachSelf = reachSelf,
                    ReachOpp = reachOpp,
                    ReachChance = reachChance,
                    ActiveMask = mask,
                    ScenarioIndex = scn,
                    Rand01ForDecision = _ => 0.0
                };
            }

            public static void InitializeVectorPolicies(FastCFRInformationSetVec node,
                double[][] ownerPolicyByLane, double[][] oppPolicyByLane)
            {
                int lanes = ownerPolicyByLane.Length;
                var owner = new ReadOnlySpan<double>[lanes];
                var opp = new ReadOnlySpan<double>[lanes];
                for (int k = 0; k < lanes; k++)
                {
                    owner[k] = ownerPolicyByLane[k];
                    opp[k] = oppPolicyByLane[k];
                }
                node.InitializeIterationVec(owner, opp);
            }
        }
    }
}
