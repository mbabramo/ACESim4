using ACESim;
using ACESim.Util;
using ACESim.Util.DiscreteProbabilities;
using ACESimBase.Games.LitigGame.Options;
using ACESimBase.GameSolvingSupport.Settings;
using ACESimBase.Util;
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
            evolutionSettings.UnrollRepeatIdenticalRanges = false;

            double[] notUnrolled = await DevelopStrategyAndGetUtilities(randomInformationSets, largerTree, evolutionSettings);

            evolutionSettings.UnrollAlgorithm = true;
            double[] unrolled = await DevelopStrategyAndGetUtilities(randomInformationSets, largerTree, evolutionSettings);

            evolutionSettings.UnrollRepeatIdenticalRanges = true;
            double[] unrolledWithRepeats = await DevelopStrategyAndGetUtilities(randomInformationSets, largerTree, evolutionSettings);

            notUnrolled.SequenceEqual(unrolled).Should().BeTrue();
            unrolledWithRepeats.SequenceEqual(unrolled).Should().BeTrue();
        }

        private static async Task<double[]> DevelopStrategyAndGetUtilities(bool randomInformationSets, bool largerTree, EvolutionSettings evolutionSettings)
        {
            byte branching = largerTree ? (byte)5 : (byte)2; // signals, precaution powers, and precaution levels
            var options = LitigGameOptionsGenerator.PrecautionNegligenceGame(largerTree, largerTree, branching, 1, branching, branching);
            GeneralizedVanilla.ClearCache();
            var developer = await GetGeneralizedVanilla(options, "TESTOPTIONS", evolutionSettings);
            if (randomInformationSets)
                RandomizeInformationSetProbabilities(developer);
            await developer.RunAlgorithm("TESTOPTIONS");
            double[] utilities = GetUtilities(developer);
            return utilities;
        }
    }
}
