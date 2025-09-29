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

            CounterfactualRegretMinimization.TraceCFR = false;

            EvolutionSettings evolutionSettings = new EvolutionSettings();
            evolutionSettings.TotalIterations = 100;
            evolutionSettings.UnrollAlgorithm = false;
            evolutionSettings.UnrollTemplateIdenticalRanges = false;
            evolutionSettings.UnrollTemplateRepeatedRanges = false;
            evolutionSettings.Unroll_ChunkExecutorKind = ChunkExecutorKind.Interpreted;

            double[] notUnrolled = await DevelopStrategyAndGetUtilities(randomInformationSets, largerTree, evolutionSettings);

            evolutionSettings.UnrollAlgorithm = true;
            double[] unrolled = await DevelopStrategyAndGetUtilities(randomInformationSets, largerTree, evolutionSettings);

            evolutionSettings.UnrollTemplateIdenticalRanges = true;
            double[] unrolledWithRepeats = await DevelopStrategyAndGetUtilities(randomInformationSets, largerTree, evolutionSettings);

            notUnrolled.SequenceEqual(unrolled).Should().BeTrue();
            unrolledWithRepeats.SequenceEqual(unrolled).Should().BeTrue();
        }

        [TestMethod]
        [DataRow(false, false)]
        [DataRow(false, true)]
        [DataRow(true, false)]
        [DataRow(true, true)]
        public async Task EachExecutorProducesSameResults(bool randomInformationSets, bool largerTree)
        {
            CounterfactualRegretMinimization.TraceCFR = false;
            EvolutionSettings evolutionSettings = new EvolutionSettings();
            evolutionSettings.TotalIterations = 100;
            evolutionSettings.UnrollAlgorithm = true;
            evolutionSettings.UnrollTemplateIdenticalRanges = true;
            evolutionSettings.UnrollTemplateRepeatedRanges = true;
            List<double[]> results = new();

            foreach (ChunkExecutorKind kind in new ChunkExecutorKind[] { ChunkExecutorKind.Interpreted, ChunkExecutorKind.Roslyn, ChunkExecutorKind.RoslynWithLocalVariableRecycling, ChunkExecutorKind.IL, ChunkExecutorKind.ILWithLocalVariableRecycling })
            {
                Stopwatch s = new Stopwatch();
                s.Start();
                evolutionSettings.Unroll_ChunkExecutorKind = kind;
                var developer = await Initialize(largerTree, evolutionSettings);
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

        private static async Task<double[]> DevelopStrategyAndGetUtilities(bool randomInformationSets, bool largerTree, EvolutionSettings evolutionSettings)
        {
            GeneralizedVanilla developer = await Initialize(largerTree, evolutionSettings);
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

        private static async Task<GeneralizedVanilla> Initialize(bool largerTree, EvolutionSettings evolutionSettings)
        {
            byte branching = largerTree ? (byte)5 : (byte)2; // signals, precaution powers, and precaution levels
            var options = LitigGameOptionsGenerator.PrecautionNegligenceGame(largerTree, largerTree, branching, 1, branching, branching);
            GeneralizedVanilla.ClearCache();
            var developer = await GetGeneralizedVanilla(options, "TESTOPTIONS", evolutionSettings);
            return developer;
        }
    }
}
