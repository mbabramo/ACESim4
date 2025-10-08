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

        private int _iterationsForParity = 100; // minimum number causing test to fail

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
                ParallelOptimization = false
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
