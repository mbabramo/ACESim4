using ACESim;
using ACESimBase.Games.LitigGame;
using ACESimBase.Games.LitigGame.PrecautionModel;
using ACESimBase.GameSolvingSupport.GameTree;
using ACESimBase.GameSolvingSupport.Settings;
using ACESimBase.Util.Debugging;
using FluentAssertions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESimTest.StrategiesTests
{
    public class StrategiesDeveloperTestsBase
    {
        
        internal const double tolerance = 1E-12;

        /// <summary>
        /// Enumerates every decision-/chance-path in the current game definition,
        /// replays the game once for each path (using the action delegate derived
        /// from that path), generates all game progresses consistent with that outcome,
        /// and returns each associated with its corresponding probability.
        /// </summary>
        internal static async Task<List<(double probability, PrecautionNegligenceProgress progress)>>
            GetConsistentProgressForEveryGamePathAsync(LitigGameOptions options, bool randomInformationSets, EvolutionSettings evolutionSettings = null)
        {
            var finalResults = new List<(double probability, PrecautionNegligenceProgress progress)>();
            var initialResults = await GetProgressForEveryGamePathAsync(options, randomInformationSets, evolutionSettings);

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
        internal static async Task<List<(double probability, PrecautionNegligenceProgress progress)>>
            GetProgressForEveryGamePathAsync(LitigGameOptions options, bool randomInformationSets, EvolutionSettings evolutionSettings = null)
        {
            var developer = await GetGeneralizedVanilla(options, "PathEnumeration", evolutionSettings);
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

        internal static async Task<double[]> GetUtilities(LitigGameOptions options, string optionsName, bool randomInformationSets, EvolutionSettings evolutionSettings = null)
        {
            var developer = await GetGeneralizedVanilla(options, optionsName, evolutionSettings);
            if (randomInformationSets)
                RandomizeInformationSetProbabilities(developer);
            double[] overallUtilities = GetUtilities(developer);
            return overallUtilities;
        }

        internal static double[] GetUtilities(StrategiesDeveloperBase developer)
        {
            var treeWalker = new CalculateUtilitiesAtEachInformationSet();
            double[] overallUtilities = developer.TreeWalk_Tree(treeWalker);
            return overallUtilities;
        }

        internal static string PrintedGameTree(GeneralizedVanilla developer)
        {
            int initialAccumulatedTextLength = TabbedText.AccumulatedText.Length;
            developer.PrintGameTree(); 
            string afterPrintingGameTree = TabbedText.AccumulatedText.ToString()[initialAccumulatedTextLength..];
            return afterPrintingGameTree;
        }

        internal static Dictionary<string, double[]> RandomizeInformationSetProbabilities(GeneralizedVanilla developer)
        {
            var informationSets = developer.InformationSets;
            Dictionary<string, double[]> informationSetProbabilities = new();
            foreach (var informationSet in informationSets)
            {
                var numPossibleActions = informationSet.NumPossibleActions;
                // the goal here is to make sure that we randomize information sets the same, whether dealing with the regular decision or the collapsed decision. But with the collapsed game, the information set items come in a different order (with p's signal sometimes coming at end, when reconstructing the game). So, we use a hash code of a string that presents the decision labels alphabetically.
                string id = $"P{informationSet.PlayerIndex},D{informationSet.DecisionByteCode}:{informationSet.InformationSetWithAlphabeticalLabels(developer.GameDefinition)}";
                int hashCode = id.GetHashCode();
                double[] probabilities = CreateNormalizedRandomDistribution(numPossibleActions, hashCode);
                informationSet.SetCurrentProbabilities(probabilities);
                informationSetProbabilities[id] = probabilities;
            }

            var map = informationSetProbabilities.Select(x => (x.Key, string.Join(",", x.Value))).OrderBy(x => x).ToList();
            var combined = string.Join("\n", map);

            return informationSetProbabilities;
        }

        internal static async Task<GeneralizedVanilla> GetGeneralizedVanilla(LitigGameOptions gameOptions, string optionsName, EvolutionSettings evolutionSettings = null)
        {
            var launcher = new LitigGameEndogenousDisputesLauncher();
            if (evolutionSettings == null)
                evolutionSettings = launcher.GetEvolutionSettings();
            evolutionSettings.Algorithm = GameApproximationAlgorithm.GeneralizedVanilla;
            var developer = (GeneralizedVanilla)await launcher.GetInitializedDevelper(gameOptions, optionsName, evolutionSettings);
            return developer;
        }

        /// <summary>
        /// Generates a strictly positive probability distribution of the requested length,
        /// seeded by <paramref name="hashCode"/> so that the same inputs always yield the same output.
        /// </summary>
        internal static double[] CreateNormalizedRandomDistribution(int numPossibleActions, int hashCode)
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
