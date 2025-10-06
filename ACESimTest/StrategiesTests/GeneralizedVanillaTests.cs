using ACESim;
using ACESim.Util;
using ACESim.Util.DiscreteProbabilities;
using ACESimBase.Games.LitigGame.Options;
using ACESimBase.GameSolvingSupport.FastCFR;
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

        [TestMethod]
        [DataRow(false, false)]
        [DataRow(false, true)]
        [DataRow(true,  false)]
        [DataRow(true,  true)]
        public async Task SameResultsRegularAndUnrolling(bool randomInformationSets, bool largerTree)
        {
            var evolutionSettings = new EvolutionSettings
            {
                TotalIterations = 1000, 
                GeneralizedVanillaFlavor = GeneralizedVanillaFlavor.Regular,
                UnrollTemplateIdenticalRanges = false,
                UnrollTemplateRepeatedRanges  = false,
                Unroll_ChunkExecutorKind = ChunkExecutorKind.Interpreted,
                TraceCFR = false,
                ParallelOptimization = false // deterministic for bitwise-equality tests
            };

            // Regular from a seeded initial state
            var regularDev = await Initialize(largerTree, evolutionSettings, 1);
            if (randomInformationSets)
                RandomizeInformationSetProbabilities(regularDev);
            var seed = regularDev.GetInformationSetValues();

            await regularDev.RunAlgorithm("TESTOPTIONS");
            var notUnrolled = new EvaluationResult(GetUtilities(regularDev), regularDev.GetInformationSetValues());

            // Unrolled from the *same* initial state
            evolutionSettings.GeneralizedVanillaFlavor = GeneralizedVanillaFlavor.Unrolled;
            evolutionSettings.UnrollTemplateIdenticalRanges = false;
            var unrolledDev = await Initialize(largerTree, evolutionSettings, 1);
            if (randomInformationSets)
                RandomizeInformationSetProbabilities(unrolledDev);

            await unrolledDev.RunAlgorithm("TESTOPTIONS");
            var unrolled = new EvaluationResult(GetUtilities(unrolledDev), unrolledDev.GetInformationSetValues());

            // Unrolled + repeated/identical templates, same seed
            evolutionSettings.UnrollTemplateIdenticalRanges = true;
            var unrolledWithRepeatsDev = await Initialize(largerTree, evolutionSettings, 1);
            if (randomInformationSets)
                RandomizeInformationSetProbabilities(unrolledWithRepeatsDev);

            await unrolledWithRepeatsDev.RunAlgorithm("TESTOPTIONS");
            var unrolledWithRepeats = new EvaluationResult(GetUtilities(unrolledWithRepeatsDev), unrolledWithRepeatsDev.GetInformationSetValues());

            notUnrolled.Utilities.SequenceEqual(unrolled.Utilities).Should().BeTrue("utilities must match between Regular and Unrolled");
            notUnrolled.InformationSetValues.SequenceEqual(unrolled.InformationSetValues).Should().BeTrue("information set values must match between Regular and Unrolled");

            unrolledWithRepeats.Utilities.SequenceEqual(unrolled.Utilities).Should().BeTrue("utilities must match across Unrolled template permutations");
            unrolledWithRepeats.InformationSetValues.SequenceEqual(unrolled.InformationSetValues).Should().BeTrue("information set values must match across Unrolled template permutations");
        }


        [TestMethod]
        [DataRow(false, false)]
        [DataRow(false, true)]
        [DataRow(true,  false)]
        [DataRow(true,  true)]
        public async Task SameResultsRegularAndFastCFR(bool randomInformationSets, bool largerTree)
        {
            var evolutionSettings = new EvolutionSettings
            {
                TotalIterations = 1000,
                GeneralizedVanillaFlavor = GeneralizedVanillaFlavor.Regular,
                UnrollTemplateIdenticalRanges = false,
                UnrollTemplateRepeatedRanges = false,
                Unroll_ChunkExecutorKind = ChunkExecutorKind.Interpreted,
                TraceCFR = false,
                ParallelOptimization = false // deterministic for bitwise-equality tests
            };

            // Regular from a seeded initial state
            var regularDev = await Initialize(largerTree, evolutionSettings, 1);
            if (randomInformationSets)
                RandomizeInformationSetProbabilities(regularDev);
            var seed = regularDev.GetInformationSetValues();

            await regularDev.RunAlgorithm("TESTOPTIONS");
            var regular = new EvaluationResult(GetUtilities(regularDev), regularDev.GetInformationSetValues());

            // FastCFR from the *same* initial state
            evolutionSettings.GeneralizedVanillaFlavor = GeneralizedVanillaFlavor.Fast;
            var fastDev = await Initialize(largerTree, evolutionSettings, 1);
            if (randomInformationSets)
                RandomizeInformationSetProbabilities(fastDev);

            await fastDev.RunAlgorithm("TESTOPTIONS");
            var fast = new EvaluationResult(GetUtilities(fastDev), fastDev.GetInformationSetValues());

            regular.Utilities.SequenceEqual(fast.Utilities)
                .Should().BeTrue("utilities must match between Regular and Fast");
            regular.InformationSetValues.SequenceEqual(fast.InformationSetValues)
                .Should().BeTrue("information set values must match between Regular and Fast");
        }


        [TestMethod]
        [DataRow(false, false)]
        [DataRow(false, true)]
        [DataRow(true,  false)]
        [DataRow(true,  true)]
        public async Task UnrolledTemplates_AllPermutations_Parity(bool randomInformationSets, bool largerTree)
        {
            var evolutionSettings = new EvolutionSettings
            {
                TotalIterations = 1000,
                GeneralizedVanillaFlavor = GeneralizedVanillaFlavor.Regular,
                UnrollTemplateIdenticalRanges = false,
                UnrollTemplateRepeatedRanges  = false,
                Unroll_ChunkExecutorKind = ChunkExecutorKind.Interpreted,
                TraceCFR = false
            };

            var permutations = new (bool useIdentical, bool useRepeated)[]
            {
                (false, false),
                (true,  false),
                (false, true),
                (true,  true),
            };

            EvaluationResult firstUnrolled = null;

            foreach (var (useIdentical, useRepeated) in permutations)
            {
                evolutionSettings.GeneralizedVanillaFlavor = GeneralizedVanillaFlavor.Unrolled;
                evolutionSettings.UnrollTemplateIdenticalRanges = useIdentical;
                evolutionSettings.UnrollTemplateRepeatedRanges  = useRepeated;
                evolutionSettings.Unroll_ChunkExecutorKind = ChunkExecutorKind.Interpreted;
                evolutionSettings.TraceCFR = false;

                EvaluationResult result = await DevelopStrategyAndGetResults(randomInformationSets, largerTree, evolutionSettings, 2);

                if (firstUnrolled is null)
                {
                    firstUnrolled = result;
                }
                else
                {
                    result.Utilities.SequenceEqual(firstUnrolled.Utilities).Should().BeTrue(
                        $"Permutation mismatch (utilities): UnrollTemplateIdenticalRanges={useIdentical}, UnrollTemplateRepeatedRanges={useRepeated}");
                    result.InformationSetValues.SequenceEqual(firstUnrolled.InformationSetValues).Should().BeTrue(
                        $"Permutation mismatch (information set values): UnrollTemplateIdenticalRanges={useIdentical}, UnrollTemplateRepeatedRanges={useRepeated}");
                }
            }
        }

        [TestMethod]
        [DataRow(false, false)]
        [DataRow(false, true)]
        [DataRow(true,  false)]
        [DataRow(true,  true)]
        public async Task SameResultsUnrolledAndFastCFR(bool randomInformationSets, bool largerTree)
        {
            var evolutionSettings = new EvolutionSettings
            {
                TotalIterations = 1000, 
                GeneralizedVanillaFlavor = GeneralizedVanillaFlavor.Unrolled,
                UnrollTemplateIdenticalRanges = false,
                UnrollTemplateRepeatedRanges = false,
                Unroll_ChunkExecutorKind = ChunkExecutorKind.Interpreted,
                TraceCFR = false,
                ParallelOptimization = false // deterministic for bitwise-equality tests
            };

            // Unrolled from a seeded initial state
            var unrolledDev = await Initialize(largerTree, evolutionSettings, 1);
            if (randomInformationSets)
                RandomizeInformationSetProbabilities(unrolledDev);
            var seed = unrolledDev.GetInformationSetValues();

            await unrolledDev.RunAlgorithm("TESTOPTIONS");
            var unrolled = new EvaluationResult(GetUtilities(unrolledDev), unrolledDev.GetInformationSetValues());

            // FastCFR from the *same* initial state
            evolutionSettings.GeneralizedVanillaFlavor = GeneralizedVanillaFlavor.Fast;
            var fastDev = await Initialize(largerTree, evolutionSettings, 1);
            if (randomInformationSets)
                RandomizeInformationSetProbabilities(fastDev);

            await fastDev.RunAlgorithm("TESTOPTIONS");
            var fast = new EvaluationResult(GetUtilities(fastDev), fastDev.GetInformationSetValues());

            // Direct equality checks
            unrolled.Utilities.SequenceEqual(fast.Utilities)
                .Should().BeTrue("utilities must match between Unrolled and Fast");
            unrolled.InformationSetValues.SequenceEqual(fast.InformationSetValues)
                .Should().BeTrue("information set values must match between Unrolled and Fast");

            // Also verify unrolled template permutations against FastCFR from the same seed
            evolutionSettings.GeneralizedVanillaFlavor = GeneralizedVanillaFlavor.Unrolled;
            evolutionSettings.UnrollTemplateIdenticalRanges = true;
            evolutionSettings.UnrollTemplateRepeatedRanges = false; // keep false unless/until repeats are enabled for your tree

            var unrolledWithIdenticalRangesDev = await Initialize(largerTree, evolutionSettings, 1);
            if (randomInformationSets)
                RandomizeInformationSetProbabilities(unrolledWithIdenticalRangesDev);

            await unrolledWithIdenticalRangesDev.RunAlgorithm("TESTOPTIONS");
            var unrolledWithIdenticalRanges = new EvaluationResult(
                GetUtilities(unrolledWithIdenticalRangesDev),
                unrolledWithIdenticalRangesDev.GetInformationSetValues());

            unrolledWithIdenticalRanges.Utilities.SequenceEqual(fast.Utilities)
                .Should().BeTrue("utilities must match between Unrolled(identical ranges) and Fast");
            unrolledWithIdenticalRanges.InformationSetValues.SequenceEqual(fast.InformationSetValues)
                .Should().BeTrue("information set values must match between Unrolled(identical ranges) and Fast");
        }


        [TestMethod]
        [DataRow(false, false)]
        [DataRow(false, true)]
        [DataRow(true, false)]
        [DataRow(true, true)]
        public async Task EachExecutorProducesSameResults(bool randomInformationSets, bool largerTree)
        {
            EvolutionSettings evolutionSettings = new EvolutionSettings();
            evolutionSettings.TotalIterations = 1000;
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
                    addToResults.Utilities.SequenceEqual(results.First().Utilities).Should().BeTrue($"utilities mismatch for executor {kind}");
                    addToResults.InformationSetValues.SequenceEqual(results.First().InformationSetValues).Should().BeTrue($"information set values mismatch for executor {kind}");
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
            GeneralizedVanilla.ClearCache();
            var developer = await GetGeneralizedVanilla(options, "TESTOPTIONS", evolutionSettings);
            return developer;
        }
    }
}
