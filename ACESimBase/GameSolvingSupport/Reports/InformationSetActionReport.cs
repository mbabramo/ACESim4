using ACESimBase.GameSolvingSupport.GameTree;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace ACESim
{
    /// <summary>
    /// Produces one row per action at every player information set. Utilities are
    /// conditional on reaching the information set and assume equilibrium play after
    /// the reported action. They are deliberately left blank at off-path information sets.
    /// </summary>
    public static class InformationSetActionReport
    {
        public const string ReportSuffix = "InformationSetActions";
        public const double OffPathTolerance = 1E-15;

        public static readonly IReadOnlyList<string> Headers = new[]
        {
            "OptionSetName",
            "Equilibrium Number",
            "Information Set Number",
            "Player Index",
            "Player",
            "Decision Index",
            "Decision Code",
            "Decision",
            "Information Set Labels",
            "Information Set Contents",
            "Equilibrium Reach Probability",
            "Off Path",
            "Conditional Information-Set Utility",
            "Best Action Utility",
            "Action",
            "Action Label",
            "Equilibrium Action Probability",
            "Conditional Action Utility",
            "Utility Loss from Best Action",
            "Is Best Action",
        };

        public static string BuildCsv(
            StrategiesDeveloperBase developer,
            int equilibriumNumber)
        {
            if (developer == null)
                throw new ArgumentNullException(nameof(developer));
            if (equilibriumNumber < 1)
                throw new ArgumentOutOfRangeException(nameof(equilibriumNumber));

            var utilityCalculator = new CalculateUtilitiesAtEachInformationSet();
            developer.TreeWalk_Tree(utilityCalculator);

            var csv = new StringBuilder();
            AppendRow(csv, Headers);
            foreach (InformationSetNode informationSet in developer.InformationSets
                .OrderBy(node => node.PlayerIndex)
                .ThenBy(node => node.InformationSetNodeNumber))
            {
                double[] actionProbabilities = informationSet.GetCurrentProbabilitiesAsArray();
                if (actionProbabilities.Length != informationSet.NumPossibleActions)
                    throw new InvalidOperationException(
                        $"Information set {informationSet.InformationSetNodeNumber} has an invalid action-probability vector.");
                RequireApproximately(
                    informationSet.InformationSetNodeNumber,
                    "action probabilities",
                    1.0,
                    actionProbabilities.Sum());

                (double[] utilities, List<double[]> utilitiesAtSuccessors, double reachProbability) =
                    utilityCalculator.GetUtilitiesAndReachProbability(
                        informationSet.InformationSetNodeNumber);
                bool offPath = reachProbability <= OffPathTolerance;
                if (utilitiesAtSuccessors.Count != informationSet.NumPossibleActions)
                    throw new InvalidOperationException(
                        $"Information set {informationSet.InformationSetNodeNumber} has an invalid action-utility vector.");

                int playerIndex = informationSet.PlayerIndex;
                PlayerInfo player = developer.GameDefinition.Players
                    .Single(item => item.PlayerIndex == playerIndex);
                double? informationSetUtility = null;
                double? bestActionUtility = null;
                double[] conditionalActionUtilities = null;
                if (!offPath)
                {
                    informationSetUtility = utilities[playerIndex];
                    conditionalActionUtilities = utilitiesAtSuccessors
                        .Select(successorUtilities => successorUtilities[playerIndex])
                        .ToArray();
                    bestActionUtility = player.HighestIsBest
                        ? conditionalActionUtilities.Max()
                        : conditionalActionUtilities.Min();
                    RequireApproximately(
                        informationSet.InformationSetNodeNumber,
                        "conditional information-set utility",
                        informationSetUtility.Value,
                        actionProbabilities.Zip(
                            conditionalActionUtilities,
                            (probability, utility) => probability * utility).Sum());
                }

                string informationSetLabels = informationSet.LabeledInformationSet == null
                    ? string.Empty
                    : informationSet.InformationSetWithLabels(developer.GameDefinition);
                for (byte action = 1; action <= informationSet.NumPossibleActions; action++)
                {
                    double? actionUtility = offPath
                        ? null
                        : conditionalActionUtilities[action - 1];
                    double? utilityLoss = offPath
                        ? null
                        : player.HighestIsBest
                            ? bestActionUtility - actionUtility
                            : actionUtility - bestActionUtility;
                    bool? isBestAction = offPath
                        ? null
                        : Math.Abs(utilityLoss.Value) <= 1E-10;
                    AppendRow(csv, new object[]
                    {
                        developer.GameDefinition.OptionSetName,
                        equilibriumNumber,
                        informationSet.InformationSetNodeNumber,
                        playerIndex,
                        player.PlayerName,
                        informationSet.DecisionIndex,
                        informationSet.DecisionByteCode,
                        informationSet.Decision.Name,
                        informationSetLabels,
                        informationSet.InformationSetContentsString,
                        reachProbability,
                        offPath,
                        informationSetUtility,
                        bestActionUtility,
                        action,
                        developer.GameDefinition.GetActionString(
                            action,
                            informationSet.DecisionByteCode),
                        actionProbabilities[action - 1],
                        actionUtility,
                        utilityLoss,
                        isBestAction,
                    });
                }
            }
            return csv.ToString();
        }

        private static void RequireApproximately(
            int informationSetNumber,
            string description,
            double expected,
            double actual)
        {
            double tolerance = 1E-9 * Math.Max(1.0, Math.Max(Math.Abs(expected), Math.Abs(actual)));
            if (!double.IsFinite(expected) || !double.IsFinite(actual) ||
                Math.Abs(expected - actual) > tolerance)
                throw new InvalidOperationException(
                    $"Information set {informationSetNumber} fails the {description} identity: " +
                    $"expected {Format(expected)}, found {Format(actual)}.");
        }

        private static void AppendRow(StringBuilder csv, IEnumerable<object> fields)
        {
            csv.AppendLine(string.Join(",", fields.Select(field => CsvField(FieldText(field)))));
        }

        private static string FieldText(object field) => field switch
        {
            null => string.Empty,
            double value => Format(value),
            float value => value.ToString("G9", CultureInfo.InvariantCulture),
            bool value => value ? "true" : "false",
            IFormattable value => value.ToString(null, CultureInfo.InvariantCulture),
            _ => field.ToString() ?? string.Empty,
        };

        private static string Format(double value) =>
            value.ToString("G17", CultureInfo.InvariantCulture);

        private static string CsvField(string value) =>
            $"\"{value.Replace("\"", "\"\"")}\"";
    }
}
