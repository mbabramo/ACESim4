using ACESim;
using ACESimBase.Games.LitigGame.PrecautionModel;
using ACESimBase.GameSolvingSupport.GameTree;
using ACESimBase.GameSolvingSupport.Settings;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace ACESimTest
{
    [TestClass]
    public class PrecautionNegligenceDisputeGeneratorTests
    {
        [TestMethod]
        public async Task CollapsingDecisionsIsEquivalent()
        {
            var regular = LitigGameOptionsGenerator.PrecautionNegligenceGame(false, false, 2, 0, 2, 2);
            double[] regularUtilities = await GetUtilitiesWithRandomInformationSets(regular, "Regular");

            var collapsed = LitigGameOptionsGenerator.PrecautionNegligenceGame(true, false, 2, 0, 2, 2);
            double[] collapsedUtilities = await GetUtilitiesWithRandomInformationSets(regular, "Collapse");

            regularUtilities.Should().Equal(collapsedUtilities); // Note: Each execution should produce different runs (because string.GetHashCode()) is not consistent across runs, but they should match regardless.
        }

        private static async Task<double[]> GetUtilitiesWithRandomInformationSets(LitigGameOptions regular, string optionsName)
        {
            var developer = await GetGeneralizedVanilla(regular, optionsName);
            Dictionary<string, double[]> informationSetProbabilities = SetInformationSetProbabilities(developer);
            var treeWalker = new CalculateUtilitiesAtEachInformationSet();
            double[] overallUtilities = developer.TreeWalk_Tree(treeWalker);
            return overallUtilities;
        }

        private static Dictionary<string, double[]> SetInformationSetProbabilities(GeneralizedVanilla developer)
        {
            var informationSets = developer.InformationSets;
            Dictionary<string, double[]> informationSetProbabilities = new();
            foreach (var informationSet in informationSets)
            {
                var numPossibleActions = informationSet.NumPossibleActions;
                string id = $"{informationSet.PlayerIndex},{informationSet.DecisionIndex}:{informationSet.InformationSetContentsString}";
                int hashCode = (id).GetHashCode();
                double[] probabilities = CreateNormalizedRandomDistribution(numPossibleActions, hashCode);
                informationSet.SetCurrentProbabilities(probabilities);
                informationSetProbabilities[id] = probabilities;
            }

            return informationSetProbabilities;
        }

        private static async Task<GeneralizedVanilla> GetGeneralizedVanilla(LitigGameOptions gameOptions, string optionsName)
        {
            var launcher = new LitigGameEndogenousDisputesLauncher();
            var evolutionSettings = launcher.GetEvolutionSettings();
            evolutionSettings.Algorithm = GameApproximationAlgorithm.RegretMatching;
            var developer = (GeneralizedVanilla)await launcher.GetInitializedDevelper(gameOptions, optionsName, evolutionSettings);
            return developer;
        }

        /// <summary>
        /// Generates a strictly positive probability distribution of the requested length,
        /// seeded by <paramref name="hashCode"/> so that the same inputs always yield the same output.
        /// </summary>
        private static double[] CreateNormalizedRandomDistribution(int numPossibleActions, int hashCode)
        {
            if (numPossibleActions <= 0)
                throw new ArgumentOutOfRangeException(nameof(numPossibleActions), "Value must be positive.");

            var random = new Random(hashCode);

            var values = new double[numPossibleActions];
            double rawSum = 0.0;

            // Draw raw positive samples.
            for (int i = 0; i < numPossibleActions; i++)
            {
                // Repeat until NextDouble() gives a non-zero value (zero is rare but possible).
                double sample;
                do
                {
                    sample = random.NextDouble();
                } while (sample == 0.0);

                values[i] = sample;
                rawSum += sample;
            }

            // First-pass normalization.
            for (int i = 0; i < numPossibleActions; i++)
            {
                values[i] /= rawSum;
            }

            // Correct any accumulated floating-point drift so the total is exactly 1.0.
            double runningTotal = 0.0;
            for (int i = 0; i < numPossibleActions - 1; i++)
            {
                runningTotal += values[i];
            }
            values[numPossibleActions - 1] = 1.0 - runningTotal;

            return values;
        }

    }
}
