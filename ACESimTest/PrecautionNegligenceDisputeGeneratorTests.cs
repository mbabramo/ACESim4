using ACESim;
using ACESimBase.Games.LitigGame;
using ACESimBase.Games.LitigGame.PrecautionModel;
using ACESimBase.GameSolvingSupport.GameTree;
using ACESimBase.GameSolvingSupport.Settings;
using ACESimBase.Util.Collections;
using ACESimBase.Util.Debugging;
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
        [DataRow(false, false)]
        [DataRow(false, true)]
        [DataRow(true, false)]
        [DataRow(true, true)]
        public async Task CollapsingDecisionsGivesEquivalentUtilities(bool randomInformationSets, bool largerTree)
        {
            byte branching = largerTree ? (byte)5 : (byte)2; // signals, precaution powers, and precaution levels
            var regular = LitigGameOptionsGenerator.PrecautionNegligenceGame(false, false, branching, 1, branching, branching);
            double[] regularUtilities = await GetUtilities(regular, "Regular", randomInformationSets);

            var collapsed = LitigGameOptionsGenerator.PrecautionNegligenceGame(true, false, branching, 1, branching, branching);
            double[] collapsedUtilities = await GetUtilities(collapsed, "Collapse", randomInformationSets);

            regularUtilities.Should().Equal(
                collapsedUtilities,
                (actualValue, expectedValue) =>
                    Math.Abs(actualValue - expectedValue) <= tolerance
            );
            // Note: Each execution should produce different runs (because string.GetHashCode()) is not consistent across runs, but they should match regardless.
        }

        [TestMethod]
        [DataRow(false, false)]
        [DataRow(false, true)]
        [DataRow(true, false)]
        [DataRow(true, true)]
        public async Task CollapsingDecisionsAggregatesProperly(bool randomInformationSets, bool largerTree)
        {
            byte branching = largerTree ? (byte) 3 : (byte) 2; // signals, precaution powers, and precaution levels

            var regular = LitigGameOptionsGenerator.PrecautionNegligenceGame(false, false, branching, 1, branching, branching);
            List<(double probability, PrecautionNegligenceProgress progress)> regularResults = await GetConsistentProgressForEveryGamePathAsync(regular, randomInformationSets);

            var collapsed = LitigGameOptionsGenerator.PrecautionNegligenceGame(true, false, branching, 1, branching, branching);
            List<(double probability, PrecautionNegligenceProgress progress)> collapsedResults = await GetConsistentProgressForEveryGamePathAsync(collapsed, randomInformationSets);

            regularResults.Sum(x => x.probability).Should().BeApproximately(1.0, tolerance);
            collapsedResults.Sum(x => x.probability).Should().BeApproximately(1.0, tolerance);

            // first, confirm equivalence for all signals taken together (for all as a whole, plus some subsets)
            ConfirmEquivalence(x => true);
            ConfirmEquivalence(x => x.EngagesInActivity);
            ConfirmEquivalence(x => x.AccidentOccurs);
            ConfirmEquivalence(x => x.AccidentWronglyCausallyAttributedToDefendant);
            ConfirmEquivalence(x => x.TrialOccurs);
            ConfirmEquivalence(x => x.PWinsAtTrial);

            // second, confirm equivalence for subsets defined by the combination of signals
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

            // third, confirm equivalence by hidden state and the wrongful/true split when an accident occurs
            var hiddenValues = GetDistinctValues(regularResults, x => x.LiabilityStrengthDiscrete).OrderBy(x => x).ToList();
            foreach (var h in hiddenValues)
            {
                // Hidden-state marginal
                ConfirmEquivalence(x => x.LiabilityStrengthDiscrete == h);

                // Accident by hidden state
                ConfirmEquivalence(x => x.LiabilityStrengthDiscrete == h && x.AccidentOccurs);

                // Wrongful vs. true attribution by hidden state (only meaningful when an accident occurs)
                ConfirmEquivalence(x => x.LiabilityStrengthDiscrete == h && x.AccidentOccurs && x.AccidentWronglyCausallyAttributedToDefendant);
                ConfirmEquivalence(x => x.LiabilityStrengthDiscrete == h && x.AccidentOccurs && !x.AccidentWronglyCausallyAttributedToDefendant);
            }

            // local helper functions
            HashSet<T> GetDistinctValues<T>(List<(double probability, PrecautionNegligenceProgress progress)> results, Func<PrecautionNegligenceProgress, T> selector)
                => new HashSet<T>(results.Select(x => selector(x.progress)));

            List<(double probability, PrecautionNegligenceProgress progress)> Filter(
                List<(double probability, PrecautionNegligenceProgress progress)> results,
                Func<PrecautionNegligenceProgress, bool> filterFunc)
                => results.Where(x => filterFunc(x.progress)).ToList();

            (List<(double probability, PrecautionNegligenceProgress progress)> regular, List<(double probability, PrecautionNegligenceProgress progress)> collapsed)
                FilterBoth(Func<PrecautionNegligenceProgress, bool> filterFunc)
                => (Filter(regularResults, filterFunc), Filter(collapsedResults, filterFunc));

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
                    x => x.PLiabilitySignalDiscrete,
                    x => x.DLiabilitySignalDiscrete,
                    x => x.PLiabilitySignalDiscrete - x.DLiabilitySignalDiscrete,
                    x => x.LiabilityStrengthDiscrete,
                    x => x.RelativePrecautionLevel,
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

        private static async Task<double[]> GetUtilities(LitigGameOptions options, string optionsName, bool randomInformationSets)
        {
            var developer = await GetGeneralizedVanilla(options, optionsName);
            if (randomInformationSets)
                RandomizeInformationSetProbabilities(developer);
            var treeWalker = new CalculateUtilitiesAtEachInformationSet();
            double[] overallUtilities = developer.TreeWalk_Tree(treeWalker);
            return overallUtilities;
        }

        private static string PrintedGameTree(GeneralizedVanilla developer)
        {
            int initialAccumulatedTextLength = TabbedText.AccumulatedText.Length;
            developer.PrintGameTree(); 
            string afterPrintingGameTree = TabbedText.AccumulatedText.ToString()[initialAccumulatedTextLength..];
            return afterPrintingGameTree;
        }

        private static Dictionary<string, double[]> RandomizeInformationSetProbabilities(GeneralizedVanilla developer)
        {
            var informationSets = developer.InformationSets;
            Dictionary<string, double[]> informationSetProbabilities = new();
            foreach (var informationSet in informationSets)
            {
                var numPossibleActions = informationSet.NumPossibleActions;
                // the goal here is to make sure that we randomize information sets the same, whether dealing with the regular decision or the collapsed decision. But with the collapsed game, the information set items come in a different order (with p's signal sometimes coming at end, when reconstructing the game). So, we use a hash code of a string that presents the decision labels alphabetically.
                string id = $"P{informationSet.PlayerIndex},D{informationSet.DecisionByteCode}:{informationSet.InformationSetWithAlphabeticalLabels(developer.GameDefinition)}";
                int hashCode = (id).GetHashCode();
                double[] probabilities = CreateNormalizedRandomDistribution(numPossibleActions, hashCode);
                informationSet.SetCurrentProbabilities(probabilities);
                informationSetProbabilities[id] = probabilities;
            }

            var map = informationSetProbabilities.Select(x => (x.Key, String.Join(",", x.Value))).OrderBy(x => x).ToList();
            var combined = String.Join("\n", map);

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
