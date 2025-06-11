using ACESim;
using ACESimBase.Games.LitigGame;
using ACESimBase.Games.LitigGame.PrecautionModel;
using ACESimBase.GameSolvingSupport.GameTree;
using ACESimBase.GameSolvingSupport.Settings;
using ACESimBase.Util.Collections;
using ACESimBase.Util.Statistical;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using static Microsoft.CodeAnalysis.CSharp.SyntaxTokenParser;

namespace ACESimTest
{
    [TestClass]
    public class PrecautionNegligenceDisputeGeneratorTests
    {
        const double tolerance = 1E-12;

        [TestMethod]
        public async Task CollapsingDecisionsGivesEquivalentUtilities()
        {
            var regular = LitigGameOptionsGenerator.PrecautionNegligenceGame(false, false, 2, 0, 2, 2);
            double[] regularUtilities = await GetUtilitiesWithRandomInformationSets(regular, "Regular");

            var collapsed = LitigGameOptionsGenerator.PrecautionNegligenceGame(true, false, 2, 0, 2, 2);
            double[] collapsedUtilities = await GetUtilitiesWithRandomInformationSets(collapsed, "Collapse");

            regularUtilities.Should().Equal(
                collapsedUtilities,
                (actualValue, expectedValue) =>
                    Math.Abs(actualValue - expectedValue) <= tolerance
            );
            // Note: Each execution should produce different runs (because string.GetHashCode()) is not consistent across runs, but they should match regardless.
        }

        [TestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public async Task CollapsingDecisionsAggregatesProperly(bool randomInformationSets)
        {
            var regular = LitigGameOptionsGenerator.PrecautionNegligenceGame(false, false, 3, 0, 3, 3);
            List<(double probability, PrecautionNegligenceProgress progress)> regularResults = await GetConsistentProgressForEveryGamePathAsync(regular, randomInformationSets);

            var collapsed = LitigGameOptionsGenerator.PrecautionNegligenceGame(true, false, 3, 0, 3, 3);
            List<(double probability, PrecautionNegligenceProgress progress)> collapsedResults = await GetConsistentProgressForEveryGamePathAsync(collapsed, randomInformationSets);

            regularResults.Sum(x => x.probability).Should().BeApproximately(1.0, tolerance);
            collapsedResults.Sum(x => x.probability).Should().BeApproximately(1.0, tolerance);

            var signalValues = GetDistinctValues(regularResults, x => x.PLiabilitySignalDiscrete).OrderBy(x => x).ToList();
            foreach (var pSignalValue in signalValues)
            {
                foreach (var dSignalValue in signalValues)
                {
                    ConfirmEquivalence(x => x.PLiabilitySignalDiscrete == pSignalValue && x.DLiabilitySignalDiscrete == dSignalValue);
                    ConfirmEquivalence(x => x.PLiabilitySignalDiscrete == pSignalValue && x.DLiabilitySignalDiscrete == dSignalValue && x.EngagesInActivity);
                    ConfirmEquivalence(x => x.PLiabilitySignalDiscrete == pSignalValue && x.DLiabilitySignalDiscrete == dSignalValue && x.AccidentOccurs);
                    ConfirmEquivalence(x => x.PLiabilitySignalDiscrete == pSignalValue && x.DLiabilitySignalDiscrete == dSignalValue && x.AccidentWronglyCausallyAttributedToDefendant);
                    ConfirmEquivalence(x => x.PLiabilitySignalDiscrete == pSignalValue && x.DLiabilitySignalDiscrete == dSignalValue && x.TrialOccurs);
                    ConfirmEquivalence(x => x.PLiabilitySignalDiscrete == pSignalValue && x.DLiabilitySignalDiscrete == dSignalValue && x.PWinsAtTrial);
                }
            }

            // local helper functions
            HashSet<T> GetDistinctValues<T>(List<(double probability, PrecautionNegligenceProgress progress)> results, Func<PrecautionNegligenceProgress, T> predicate) => new HashSet<T>(results.Select(x => predicate(x.progress)));
            List<(double probability, PrecautionNegligenceProgress progress)> Filter(List<(double probability, PrecautionNegligenceProgress progress)> results, Func<PrecautionNegligenceProgress, bool> filterFunc) => results.Where(x => filterFunc(x.progress)).ToList();
            (List<(double probability, PrecautionNegligenceProgress progress)> regular, List<(double probability, PrecautionNegligenceProgress progress)> collapsed) FilterBoth(Func<PrecautionNegligenceProgress, bool> filterFunc) => (Filter(regularResults, filterFunc), Filter(collapsedResults, filterFunc));
            void ConfirmEquivalence(Func<PrecautionNegligenceProgress, bool> filterFunc)
            {
                ConfirmEquivalentProbabilities(filterFunc);
                int funcIndex = 0; // to help identify func in event of test failure
                foreach (Func<PrecautionNegligenceProgress, double?> func in new Func<PrecautionNegligenceProgress, double?>[]
                {
                    x => x.HarmCost,
                    x => x.OpportunityCost,
                    x => x.BenefitCostRatio,
                    x => x.DamagesAwarded,
                    x => x.TotalExpensesIncurred,
                    x => x.PWelfare,
                    x => x.DWelfare,
                })
                {
                    ConfirmEquivalentValues(filterFunc, func);
                    funcIndex++;
                }
            }
            void ConfirmEquivalentProbabilities(Func<PrecautionNegligenceProgress, bool> filterFunc)
            {
                var filtered = FilterBoth(filterFunc);
                double regularSum = filtered.regular.Sum(x => x.probability);
                double collapsedSum = filtered.collapsed.Sum(x => x.probability);
                regularSum.Should().BeApproximately(collapsedSum, tolerance);
            }
            void ConfirmEquivalentValues(Func<PrecautionNegligenceProgress, bool> filterFunc, Func<PrecautionNegligenceProgress, double?> valueFunc)
            {
                var filtered = FilterBoth(filterFunc);
                double? regularResult = filtered.regular.WeightedAverage(x => valueFunc(x.progress), x => x.probability);
                double? collapsedResult = filtered.collapsed.WeightedAverage(x => valueFunc(x.progress), x => x.probability);
                regularResult.Should().BeApproximately(collapsedResult, tolerance);
            }

        }

        /// <summary>
        /// Enumerates every decision-/chance-path in the current game definition,
        /// replays the game once for each path (using the action delegate derived
        /// from that path), generates all game progresses consistent with that outcome,
        /// and returns each associated with its corresponding probability.
        /// </summary>
        private static async Task<List<(double probability, PrecautionNegligenceProgress progress)>>
            GetConsistentProgressForEveryGamePathAsync(LitigGameOptions options, bool randomInformationSets)
        {
            var finalResults = new List<(double probability, PrecautionNegligenceProgress progress)>();
            var initialResults = await GetProgressForEveryGamePathAsync(options, randomInformationSets);

            double initialProbabilitySum = initialResults.Sum(x => x.probability);
            var disputeGenerator = (PrecautionNegligenceDisputeGenerator)options.LitigGameDisputeGenerator;
            foreach (var initialResult in initialResults)
            {
                var consistentProgresses = disputeGenerator.BayesianCalculations_GenerateAllConsistentGameProgresses(initialResult.progress.PLiabilitySignalDiscrete, initialResult.progress.DLiabilitySignalDiscrete, initialResult.progress.CLiabilitySignalDiscrete, initialResult.progress.PDamagesSignalDiscrete, initialResult.progress.DDamagesSignalDiscrete, initialResult.progress.CDamagesSignalDiscrete, initialResult.progress);
                finalResults.AddRange(consistentProgresses.Select(x => (x.weight * initialResult.probability, (PrecautionNegligenceProgress) x.progress)));
            }
            double finalProbabilitySum = finalResults.Sum(x => x.probability);
            initialProbabilitySum.Should().BeApproximately(finalProbabilitySum, tolerance);
            return finalResults;
        }

        /// <summary>
        /// Enumerates every decision-/chance-path in the current game definition,
        /// replays the game once for each path (using the action delegate derived
        /// from that path), and returns the reach probability together with the
        /// resulting LitigGameProgress.
        /// </summary>
        private static async Task<List<(double probability, PrecautionNegligenceProgress progress)>>
            GetProgressForEveryGamePathAsync(LitigGameOptions options, bool randomInformationSets)
        {
            var developer = await GetGeneralizedVanilla(options, "PathEnumeration");
            if (randomInformationSets)
                RandomizeInformationSetProbabilities(developer);

            // Walk the tree and collect all paths with their probabilities.
            var pathRecorder = new RecordGamePathsProcessor();
            developer.TreeWalk_Tree(pathRecorder);

            var results = new List<(double probability, PrecautionNegligenceProgress progress)>();

            foreach (var path in pathRecorder.Paths)
            {
                // Convert the recorded path to the delegate expected by the game player.
                var actionsOverride = DefineActions.GamePathToActionFunction(path);

                // Replay the game once under these fixed actions.
                PrecautionNegligenceProgress progress = (PrecautionNegligenceProgress) LitigGameLauncherBase.PlayLitigGameOnce(
                    options, actionsOverride);

                results.Add((path.Probability, progress));
            }

            return results;
        }

        private static async Task<double[]> GetUtilitiesWithRandomInformationSets(LitigGameOptions regular, string optionsName)
        {
            var developer = await GetGeneralizedVanilla(regular, optionsName);
            Dictionary<string, double[]> informationSetProbabilities = RandomizeInformationSetProbabilities(developer);
            var treeWalker = new CalculateUtilitiesAtEachInformationSet();
            double[] overallUtilities = developer.TreeWalk_Tree(treeWalker);
            return overallUtilities;
        }

        private static Dictionary<string, double[]> RandomizeInformationSetProbabilities(GeneralizedVanilla developer)
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
