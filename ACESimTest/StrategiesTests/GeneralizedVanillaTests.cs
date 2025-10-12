using ACESim;
using ACESim.Util;
using ACESim.Util.DiscreteProbabilities;
using ACESimBase.Games.LitigGame.Options;
using ACESimBase.GameSolvingSupport.FastCFR;
using ACESimBase.GameSolvingSupport.GameTree;
using ACESimBase.GameSolvingSupport.Settings;
using ACESimBase.Util;
using ACESimBase.Util.ArrayProcessing.ChunkExecutors;
using ACESimBase.Util.Debugging;
using ACESimBase.Util.DiscreteProbabilities;
using ACESimBase.Util.Mathematics;
using ACESimBase.Util.Statistical;
using FluentAssertions;
using HDF5CSharp;
using JetBrains.Annotations;
using MathNet.Numerics;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace ACESimTest.StrategiesTests
{
    [TestClass]
    public class GeneralizedVanillaTests : StrategiesDeveloperTestsBase
    {
        public GeneralizedVanillaTests() : base()
        {
            TabbedText.DisableConsoleProgressString = true;
        }

    private sealed class EvaluationResult
        {
            public double[] Utilities { get; }
            public double[] InformationSetValues { get; }
            public EvaluationResult(double[] utilities, double[] informationSetValues)
            {
                Utilities = utilities;
                InformationSetValues = informationSetValues;
            }
        }

        private int _iterationsForParity =  100; // minimum number causing test to fail

        private static bool ConfirmInformationSetsMatch(
            GeneralizedVanilla devA, string labelA,
            GeneralizedVanilla devB, string labelB,
            int maxLines = 1,
            double tolerance = 1E-08)
        {
            var aSets = devA.InformationSets;
            var bSets = devB.InformationSets;

            if (aSets.Count != bSets.Count)
            {
                Debug.WriteLine($"Different number of information sets: {labelA}={aSets.Count}, {labelB}={bSets.Count}");
                return false;
            }

            //int CP  = InformationSetNode.currentProbabilityDimension;
            //int CPO = InformationSetNode.currentProbabilityForOpponentDimension;
            //int ASP = InformationSetNode.averageStrategyProbabilityDimension;
            int REG = InformationSetNode.cumulativeRegretDimension;
            //int CST = InformationSetNode.cumulativeStrategyDimension;
            int SRT = InformationSetNode.sumRegretTimesInversePiDimension;
            int SIV = InformationSetNode.sumInversePiDimension;
            //int LCI = InformationSetNode.lastCumulativeStrategyIncrementsDimension;
            int BRD = InformationSetNode.bestResponseDenominatorDimension;

            int lines = 0;

            bool DiffVec(string field, double[] va, double[] vb)
            {
                if (va.Length != vb.Length)
                {
                    if (lines++ < maxLines)
                        Debug.WriteLine($"{field} length mismatch {labelA}={va.Length} vs {labelB}={vb.Length}");
                    return true;
                }

                for (int a = 0; a < va.Length; a++)
                {
                    if (Math.Abs(va[a] - vb[a]) > tolerance)
                    {
                        if (lines++ < maxLines)
                            Debug.WriteLine($"[Δ>{tolerance:G}] {field} {labelA}={va[a]:G17} vs {labelB}={vb[a]:G17}");
                        return true;
                    }
                }
                return false;
            }

            for (int i = 0; i < aSets.Count; i++)
            {
                var A = aSets[i];
                var B = bSets[i];

                if (A.PlayerIndex != B.PlayerIndex || A.DecisionIndex != B.DecisionIndex || A.NumPossibleActions != B.NumPossibleActions)
                {
                    if (lines++ < maxLines)
                        Debug.WriteLine($"[ISet#{i}] identity mismatch: {labelA}(P{A.PlayerIndex},D{A.DecisionIndex},nA={A.NumPossibleActions}) vs {labelB}(P{B.PlayerIndex},D{B.DecisionIndex},nA={B.NumPossibleActions})");
                    return false;
                }

                int nA = A.NumPossibleActions;

                double[] rawNumA = new double[nA], rawNumB = new double[nA];
                double[] rawDenA = new double[nA], rawDenB = new double[nA];
                double[] regA    = new double[nA], regB    = new double[nA];

                double[] lciA = A.GetLastCumulativeStrategyIncrementAsArray();
                double[] lciB = B.GetLastCumulativeStrategyIncrementAsArray();
                double[] lriA = A.GetLastRegretIncrementsAsArray();
                double[] lriB = B.GetLastRegretIncrementsAsArray();
                double[] cstA = A.GetCumulativeStrategiesAsArray();
                double[] cstB = B.GetCumulativeStrategiesAsArray();
                double[] aspA = A.GetAverageStrategiesAsArray();
                double[] aspB = B.GetAverageStrategiesAsArray();
                double[] curA = A.GetCurrentProbabilitiesAsArray();
                double[] curB = B.GetCurrentProbabilitiesAsArray();
                double[] oppA = new double[nA], oppB = new double[nA];
                A.GetCurrentProbabilities(oppA, opponentProbabilities: true);
                B.GetCurrentProbabilities(oppB, opponentProbabilities: true);

                for (int a = 0; a < nA; a++)
                {
                    rawNumA[a] = A.NodeInformation[SRT, a];
                    rawNumB[a] = B.NodeInformation[SRT, a];
                    rawDenA[a] = A.NodeInformation[SIV, a];
                    rawDenB[a] = B.NodeInformation[SIV, a];
                    regA[a]    = A.NodeInformation[REG, a];
                    regB[a]    = B.NodeInformation[REG, a];
                }

                if (DiffVec("rawNum(sumRegret*invPi)", rawNumA, rawNumB)) return false;
                if (DiffVec("rawDen(sumInvPi)",        rawDenA, rawDenB)) return false;
                if (DiffVec("lastRegretIncrement",     lriA,    lriB))    return false;
                if (DiffVec("lastCumStrategyIncrement",lciA,    lciB))    return false;
                if (DiffVec("cumulativeRegret",        regA,    regB))    return false;
                if (DiffVec("cumulativeStrategy",      cstA,    cstB))    return false;
                if (DiffVec("averageStrategy(array)",  aspA,    aspB))    return false;
                if (DiffVec("currentProbability",      curA,    curB))    return false;
                if (DiffVec("opponentTraversalProb",   oppA,    oppB))    return false;

                bool hasBRA = Enumerable.Range(0, nA).Any(a => A.NodeInformation[BRD, a] != 0.0);
                bool hasBRB = Enumerable.Range(0, nA).Any(a => B.NodeInformation[BRD, a] != 0.0);
                if (hasBRA && hasBRB && A.BestResponseAction != B.BestResponseAction)
                {
                    if (lines++ < maxLines)
                        Debug.WriteLine($"[ISet#{i} P{A.PlayerIndex} D{A.DecisionIndex}] BestResponseAction {labelA}={A.BestResponseAction} vs {labelB}={B.BestResponseAction}");
                    return false;
                }
            }

            return true;
        }

        private static bool AreVectorsNearlyEqual(double[] a, double[] b, double tolerance = 1E-08)
        {
            if (a == null || b == null) return a == b;
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
                if (Math.Abs(a[i] - b[i]) > tolerance)
                    return false;
            return true;
        }


        private async Task<GeneralizedVanilla> BuildWithFlavor(bool largerTree, bool randomize, GeneralizedVanillaFlavor flavor, int rounds, int iters)
        {
            var s = new EvolutionSettings
            {
                TotalIterations = iters,
                GeneralizedVanillaFlavor = flavor,
                UnrollTemplateIdenticalRanges = false,
                UnrollTemplateRepeatedRanges = false,
                Unroll_ChunkExecutorKind = ChunkExecutorKind.Interpreted,
                TraceCFR = false,
                ParallelOptimization = false,
                FastCFR_UseFloat = false,
            };
            var dev = await Initialize(largerTree, s, (byte)rounds);
            if (randomize) RandomizeInformationSetProbabilities(dev);
            return dev;
        }

        [TestMethod]
        [DataRow(false, false)]
        [DataRow(false, true)]
        [DataRow(true,  false)]
        [DataRow(true,  true)]
        public async Task SameResultsRegularAndUnrolling(bool randomInformationSets, bool largerTree)
        {
            var regular  = await BuildWithFlavor(largerTree, randomInformationSets, GeneralizedVanillaFlavor.Regular,  1, _iterationsForParity);
            await regular.RunAlgorithm("TESTOPTIONS");

            var unrolled = await BuildWithFlavor(largerTree, randomInformationSets, GeneralizedVanillaFlavor.Unrolled, 1, _iterationsForParity);
            await unrolled.RunAlgorithm("TESTOPTIONS");

            var ok = ConfirmInformationSetsMatch(regular, "Regular", unrolled, "Unrolled", maxLines: 1);
            ok.Should().BeTrue("information sets must match between Regular and Unrolled");
        }

        [TestMethod]
        [DataRow(false, false)]
        [DataRow(false, true)]
        [DataRow(true,  false)]
        [DataRow(true,  true)]
        public async Task SameResultsRegularAndFastCFR(bool randomInformationSets, bool largerTree)
        {
            var regular = await BuildWithFlavor(largerTree, randomInformationSets, GeneralizedVanillaFlavor.Regular, 1, _iterationsForParity);
            await regular.RunAlgorithm("TESTOPTIONS");

            var fast = await BuildWithFlavor(largerTree, randomInformationSets, GeneralizedVanillaFlavor.Fast, 1, _iterationsForParity);
            await fast.RunAlgorithm("TESTOPTIONS");

            var ok = ConfirmInformationSetsMatch(regular, "Regular", fast, "Fast", maxLines: 1);
            ok.Should().BeTrue("information sets must match between Regular and Fast");
        }

        [TestMethod]
        [DataRow(false, false)]
        [DataRow(false, true)]
        [DataRow(true,  false)]
        [DataRow(true,  true)]
        public async Task SameResultsRegularAndGPUCFR(bool randomInformationSets, bool largerTree)
        {
            var regular = await BuildWithFlavor(largerTree, randomInformationSets, GeneralizedVanillaFlavor.Regular, 1, _iterationsForParity);
            await regular.RunAlgorithm("TESTOPTIONS");

            var gpu = await BuildWithFlavor(largerTree, randomInformationSets, GeneralizedVanillaFlavor.Gpu, 1, _iterationsForParity);
            await gpu.RunAlgorithm("TESTOPTIONS");

            // Dump entire tree state (all information sets, all vectors) for both flavors.
            DumpAllInformationSets("REGULAR post-update", regular);
            DumpAllInformationSets("GPU     post-update", gpu);

            // Keep your parity assertion (increase maxLines if you want more detail in failures).
            var ok = ConfirmInformationSetsMatch(regular, "Regular", gpu, "GPU", maxLines: 200, tolerance: 1E-08);
            ok.Should().BeTrue("information sets must match between Regular and GPU");
        }

        private static string F(double v) => v.ToString("G17", System.Globalization.CultureInfo.InvariantCulture);

        private static void DumpAllInformationSets(string label, GeneralizedVanilla dev)
        {
            // Keep it “small tree” friendly (adjust thresholds as you like)
            if (dev.InformationSets == null) return;
            if (dev.InformationSets.Count > 64)
            {
                System.Diagnostics.Debug.WriteLine($"[{label}] skip dump: {dev.InformationSets.Count} information sets (raise threshold if desired).");
                return;
            }

            System.Diagnostics.Debug.WriteLine($"[{label}] informationSets={dev.InformationSets.Count}");

            for (int i = 0; i < dev.InformationSets.Count; i++)
            {
                var iset = dev.InformationSets[i];
                var name = iset.Decision?.Name ?? "";

                System.Diagnostics.Debug.WriteLine($"ISet idx={i} node={iset.InformationSetNodeNumber} P{iset.PlayerIndex} D{iset.DecisionIndex} actions={iset.NumPossibleActions} name=\"{name}\"");

                // Core per-action tallies
                DumpVector("sumRegretTimesInversePi", iset, InformationSetNode.sumRegretTimesInversePiDimension);
                DumpVector("sumInversePi",            iset, InformationSetNode.sumInversePiDimension);
                DumpVector("cumulativeRegret",        iset, InformationSetNode.cumulativeRegretDimension);
                DumpVector("bestResponseDenominator", iset, InformationSetNode.bestResponseDenominatorDimension);

                // Last-iteration increments (note: after UpdateInformationSets they may be zeros)
                var lastReg = iset.GetLastRegretIncrementsAsArray();
                if (lastReg != null) DumpArray("lastRegretIncrements", lastReg);

                var lastCum = iset.GetLastCumulativeStrategyIncrementAsArray();
                if (lastCum != null) DumpArray("lastCumulativeStrategyIncrements", lastCum);

                // Running strategy state
                var cumStr = iset.GetCumulativeStrategiesAsArray();
                if (cumStr != null) DumpArray("cumulativeStrategy", cumStr);

                var avgStr = iset.GetAverageStrategiesAsArray();
                if (avgStr != null) DumpArray("averageStrategy", avgStr);

                // Current (self) and opponent probabilities
                var cur = iset.GetCurrentProbabilitiesAsArray();
                if (cur != null) DumpArray("currentProbabilities(self)", cur);

                var opp = new double[iset.NumPossibleActions];
                iset.GetCurrentProbabilities(opp, opponentProbabilities: true);
                DumpArray("currentProbabilities(opponent)", opp);
            }

            static void DumpVector(string label, InformationSetNode iset, int dim)
            {
                int n = iset.NumPossibleActions;
                var arr = new double[n];
                for (int a = 0; a < n; a++) arr[a] = iset.NodeInformation[dim, a];
                DumpArray(label, arr);
            }

            static void DumpArray(string label, double[] arr)
            {
                var sb = new System.Text.StringBuilder();
                sb.Append(label);
                sb.Append(" [");
                for (int i = 0; i < arr.Length; i++)
                {
                    if (i > 0) sb.Append(", ");
                    sb.Append(F(arr[i]));
                }
                sb.Append(']');
                System.Diagnostics.Debug.WriteLine(sb.ToString());
            }
        }


        private static void PrintParityDigest(string label, GeneralizedVanilla dev)
        {
            int REG = InformationSetNode.cumulativeRegretDimension;
            int SRT = InformationSetNode.sumRegretTimesInversePiDimension;
            int SIV = InformationSetNode.sumInversePiDimension;
            int BRD = InformationSetNode.bestResponseDenominatorDimension;

            double sumRawNum = 0, sumRawDen = 0, sumReg = 0, sumLCI = 0, sumLRI = 0;
            int brdCount = 0, nActions = 0;

            foreach (var iset in dev.InformationSets)
            {
                var lci = iset.GetLastCumulativeStrategyIncrementAsArray();
                var lri = iset.GetLastRegretIncrementsAsArray();
                if (lci != null) sumLCI += lci.Sum();
                if (lri != null) sumLRI += lri.Sum();

                for (int a = 0; a < iset.NumPossibleActions; a++)
                {
                    nActions++;
                    sumRawNum += iset.NodeInformation[SRT, a];
                    sumRawDen += iset.NodeInformation[SIV, a];
                    sumReg    += iset.NodeInformation[REG, a];
                    if (iset.NodeInformation[BRD, a] != 0.0) brdCount++;
                }
            }

            Debug.WriteLine($"{label} :: rawNum={sumRawNum:G17} rawDen={sumRawDen:G17} lastRegIncr={sumLRI:G17} lastCumStrIncr={sumLCI:G17} cumReg={sumReg:G17} brd>0={brdCount}/{nActions}");
        }


        [TestMethod]
        [DataRow(false, false)]
        [DataRow(false, true)]
        [DataRow(true,  false)]
        [DataRow(true,  true)]
        public async Task SameResultsUnrolledAndFastCFR(bool randomInformationSets, bool largerTree)
        {
            byte rounds = 1;

            var unrolled = await BuildWithFlavor(largerTree, randomInformationSets, GeneralizedVanillaFlavor.Unrolled, rounds, _iterationsForParity);
            await unrolled.RunAlgorithm("TESTOPTIONS");

            var fast = await BuildWithFlavor(largerTree, randomInformationSets, GeneralizedVanillaFlavor.Fast, rounds, _iterationsForParity);
            await fast.RunAlgorithm("TESTOPTIONS");

            var ok = ConfirmInformationSetsMatch(unrolled, "Unrolled", fast, "Fast", maxLines: 1);
            ok.Should().BeTrue("information sets must match between Unrolled and Fast");
        }

        [TestMethod]
        [DataRow(false, false)]
        [DataRow(false, true)]
        [DataRow(true, false)]
        [DataRow(true, true)]
        public async Task EachExecutorProducesSameResults(bool randomInformationSets, bool largerTree)
        {
            EvolutionSettings evolutionSettings = new EvolutionSettings();
            evolutionSettings.TotalIterations = 100;
            evolutionSettings.GeneralizedVanillaFlavor = GeneralizedVanillaFlavor.Unrolled;
            evolutionSettings.UnrollTemplateIdenticalRanges = false;
            evolutionSettings.UnrollTemplateRepeatedRanges = false;
            evolutionSettings.TraceCFR = false;
            evolutionSettings.FastCFR_UseFloat = false;
            List<EvaluationResult> results = new();

            foreach (ChunkExecutorKind kind in new ChunkExecutorKind[] { ChunkExecutorKind.Interpreted, ChunkExecutorKind.Roslyn, ChunkExecutorKind.RoslynWithLocalVariableRecycling, ChunkExecutorKind.IL, ChunkExecutorKind.ILWithLocalVariableRecycling })
            {
                Stopwatch s = new Stopwatch();
                s.Start();
                evolutionSettings.Unroll_ChunkExecutorKind = kind;
                var developer = await Initialize(largerTree, evolutionSettings, 1);
                s.Stop();
                long initializationTime = s.ElapsedMilliseconds;
                s.Restart();

                EvaluationResult addToResults = await RunAlgorithmAndGetResults(randomInformationSets, developer);

                if (results.Any())
                {
                    AreVectorsNearlyEqual(addToResults.Utilities, results.First().Utilities).Should().BeTrue($"utilities mismatch for executor {kind}");
                    AreVectorsNearlyEqual(addToResults.InformationSetValues, results.First().InformationSetValues).Should().BeTrue($"information set values mismatch for executor {kind}");
                }

                results.Add(addToResults);

                s.Stop();
                long runTime = s.ElapsedMilliseconds;
                Debug.WriteLine($"Time (random? {randomInformationSets} larger? {largerTree}) -- kind {kind, 35} ==> initialization {initializationTime, 6} run for {evolutionSettings.TotalIterations} iterations {runTime, 6}");
            }
        }

        private static async Task<EvaluationResult> DevelopStrategyAndGetResults(bool randomInformationSets, bool largerTree, EvolutionSettings evolutionSettings, byte numPotentialBargainingRounds)
        {
            GeneralizedVanilla developer = await Initialize(largerTree, evolutionSettings, numPotentialBargainingRounds);
            EvaluationResult results = await RunAlgorithmAndGetResults(randomInformationSets, developer);
            return results;
        }

        private static async Task<EvaluationResult> RunAlgorithmAndGetResults(bool randomInformationSets, GeneralizedVanilla developer)
        {
            if (randomInformationSets)
                RandomizeInformationSetProbabilities(developer);

            await developer.RunAlgorithm("TESTOPTIONS");

            double[] utilities = GetUtilities(developer);
            double[] informationSetValues = developer.GetInformationSetValues();

            return new EvaluationResult(utilities, informationSetValues);
        }

        private static async Task<GeneralizedVanilla> Initialize(bool largerTree, EvolutionSettings evolutionSettings, byte numPotentialBargainingRounds)
        {
            byte branching = largerTree ? (byte)4 : (byte)2; // signals, precaution powers, and precaution levels
            var options = LitigGameOptionsGenerator.PrecautionNegligenceGame(largerTree, largerTree, branching, numPotentialBargainingRounds, branching, branching);
            options.SkipFileAndAnswerDecisions = false;
            options.PredeterminedAbandonAndDefaults = false; 
            GeneralizedVanilla.ClearCache();
            var developer = await GetGeneralizedVanilla(options, "TESTOPTIONS", evolutionSettings);
            return developer;
        }
    }
}
