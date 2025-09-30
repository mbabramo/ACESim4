using ACESim;
using ACESim.Util;
using ACESim.Util.DiscreteProbabilities;
using ACESimBase.Games.LitigGame.Options;
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
using System.Text;
using System.Threading.Tasks;

namespace ACESimTest
{
    [TestClass]
    public class GeneralizedVanillaTests : StrategiesDeveloperTestsBase
    {
        [TestMethod]
        [DataRow(false, false)]
        [DataRow(false, true)]
        [DataRow(true, false)]
        [DataRow(true, true)]
        public async Task SameResultsRollingAndUnrolling(bool randomInformationSets, bool largerTree)
        {
            EvolutionSettings evolutionSettings = new EvolutionSettings();
            evolutionSettings.TotalIterations = 100;
            evolutionSettings.UnrollAlgorithm = false;
            evolutionSettings.UnrollTemplateIdenticalRanges = false;
            evolutionSettings.UnrollTemplateRepeatedRanges = false;
            evolutionSettings.Unroll_ChunkExecutorKind = ChunkExecutorKind.Interpreted;
            evolutionSettings.TraceCFR = false;

            double[] notUnrolled = await DevelopStrategyAndGetUtilities(randomInformationSets, largerTree, evolutionSettings, 1);

            evolutionSettings.UnrollAlgorithm = true;
            double[] unrolled = await DevelopStrategyAndGetUtilities(randomInformationSets, largerTree, evolutionSettings, 1);

            evolutionSettings.UnrollTemplateIdenticalRanges = true;
            double[] unrolledWithRepeats = await DevelopStrategyAndGetUtilities(randomInformationSets, largerTree, evolutionSettings, 1);

            notUnrolled.SequenceEqual(unrolled).Should().BeTrue();
            unrolledWithRepeats.SequenceEqual(unrolled).Should().BeTrue();
        }

        [TestMethod]
        [DataRow(false, false)]
        [DataRow(false, true)]
        [DataRow(true,  false)]
        [DataRow(true,  true)]
        public async Task UnrolledTemplates_AllPermutations_Parity(bool randomInformationSets, bool largerTree)
        {
            // 1) Baseline: algorithm OFF, no template unrolling flags.
            var evolutionSettings = new EvolutionSettings
            {
                TotalIterations = 100,
                UnrollAlgorithm = false,
                UnrollTemplateIdenticalRanges = false,
                UnrollTemplateRepeatedRanges  = false,
                Unroll_ChunkExecutorKind = ChunkExecutorKind.Interpreted,
                TraceCFR = false
            };

            // 2) Turn algorithm ON and test all permutations of the two template flags.
            var permutations = new (bool useIdentical, bool useRepeated)[]
            {
                (false, false),
                (true,  false),
                (false, true),
                (true,  true),
            };

            double[] firstUnrolled = null;

            foreach (var (useIdentical, useRepeated) in permutations)
            {
                evolutionSettings.UnrollAlgorithm = true;
                evolutionSettings.UnrollTemplateIdenticalRanges = useIdentical;
                evolutionSettings.UnrollTemplateRepeatedRanges  = useRepeated;
                evolutionSettings.Unroll_ChunkExecutorKind = ChunkExecutorKind.Interpreted;
                evolutionSettings.TraceCFR = false;

                double[] result = await DevelopStrategyAndGetUtilities(randomInformationSets, largerTree, evolutionSettings, 2);

                // And all permutations should match each other.
                if (firstUnrolled is null)
                    firstUnrolled = result;
                else
                    result.SequenceEqual(firstUnrolled).Should().BeTrue(
                        $"Permutation mismatch: UnrollTemplateIdenticalRanges={useIdentical}, UnrollTemplateRepeatedRanges={useRepeated}");
            }
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
            evolutionSettings.UnrollAlgorithm = true;
            evolutionSettings.UnrollTemplateIdenticalRanges = false;
            evolutionSettings.UnrollTemplateRepeatedRanges = false;
            evolutionSettings.TraceCFR = false;
            List<double[]> results = new();

            foreach (ChunkExecutorKind kind in new ChunkExecutorKind[] { ChunkExecutorKind.Interpreted, ChunkExecutorKind.Roslyn, ChunkExecutorKind.RoslynWithLocalVariableRecycling, ChunkExecutorKind.IL, ChunkExecutorKind.ILWithLocalVariableRecycling })
            {
                Stopwatch s = new Stopwatch();
                s.Start();
                evolutionSettings.Unroll_ChunkExecutorKind = kind;
                var developer = await Initialize(largerTree, evolutionSettings, 1);
                s.Stop();
                long initializationTime = s.ElapsedMilliseconds;
                s.Restart();
                double[] addToResults = await RunAlgorithmAndGetUtilities(randomInformationSets, developer);
                if (results.Any())
                    addToResults.SequenceEqual(results.First()).Should().BeTrue();
                s.Stop();
                long runTime = s.ElapsedMilliseconds;
                Debug.WriteLine($"Time (random? {randomInformationSets} larger? {largerTree}) -- kind {kind, 35} ==> initialization {initializationTime, 6} run for {evolutionSettings.TotalIterations} iterations {runTime, 6}");
            }
        }

        private static async Task<double[]> DevelopStrategyAndGetUtilities(bool randomInformationSets, bool largerTree, EvolutionSettings evolutionSettings, byte numPotentialBargainingRounds)
        {
            GeneralizedVanilla developer = await Initialize(largerTree, evolutionSettings, numPotentialBargainingRounds);
            double[] utilities = await RunAlgorithmAndGetUtilities(randomInformationSets, developer);
            return utilities;
        }

        private static async Task<double[]> RunAlgorithmAndGetUtilities(bool randomInformationSets, GeneralizedVanilla developer)
        {
            if (randomInformationSets)
                RandomizeInformationSetProbabilities(developer);
            await developer.RunAlgorithm("TESTOPTIONS");
            double[] utilities = GetUtilities(developer);
            return utilities;
        }

        private static async Task<GeneralizedVanilla> Initialize(bool largerTree, EvolutionSettings evolutionSettings, byte numPotentialBargainingRounds)
        {
            byte branching = largerTree ? (byte)4 : (byte)2; // signals, precaution powers, and precaution levels
            var options = LitigGameOptionsGenerator.PrecautionNegligenceGame(largerTree, largerTree, branching, numPotentialBargainingRounds, branching, branching);
            GeneralizedVanilla.ClearCache();
            var developer = await GetGeneralizedVanilla(options, "TESTOPTIONS", evolutionSettings);
            return developer;
        }
    }
}
