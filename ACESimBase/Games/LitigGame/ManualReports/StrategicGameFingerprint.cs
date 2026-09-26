using ACESim;
using ACESimBase.GameSolvingSupport.GameTree;
using ACESimBase.GameSolvingSupport.Settings;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ACESimBase.Games.LitigGame.ManualReports
{
    /// <summary>Identity of the full instantiated strategic tree and saved probability coordinates.</summary>
    public static class StrategicGameFingerprint
    {
        public sealed record Snapshot(string Schema, string CompleteSha256, string TreeSha256,
            string CoordinatesSha256, string ParametersSha256, int TreeNodes, int TerminalNodes,
            int InformationSets, int StrategyEntries);

        private static string Hash(byte[] data) => Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
        private static string HashObject(object value) => Hash(JsonSerializer.SerializeToUtf8Bytes(value));
        private static string Number(double value) => BitConverter.DoubleToInt64Bits(value).ToString("X16", CultureInfo.InvariantCulture);

        private static SortedDictionary<string, string> ScalarConfiguration(object value)
        {
            var result = new SortedDictionary<string, string>(StringComparer.Ordinal) { ["$type"] = value.GetType().FullName };
            bool Scalar(Type t) => t.IsPrimitive || t.IsEnum || t == typeof(decimal);
            string Format(object v) => v is double d ? Number(d) : Convert.ToString(v, CultureInfo.InvariantCulture);
            foreach (var field in value.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public).Where(f => Scalar(f.FieldType)))
                result[field.Name] = Format(field.GetValue(value));
            foreach (var property in value.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Where(p => p.CanRead && p.CanWrite && p.GetIndexParameters().Length == 0 && Scalar(p.PropertyType)))
                result[property.Name] = Format(property.GetValue(value));
            return result;
        }

        public static Snapshot Capture(StrategiesDeveloperBase developer)
        {
            if (developer.GameDefinition is not LitigGameDefinition definition)
                throw new ArgumentException("Expected an initialized litigation game.");
            var options = definition.Options;
            var settings = developer.EvolutionSettings;
            // Display identities and profile values are deliberately absent. Changing a
            // name or loading a different strategy cannot create a new strategic game.
            string parameters = HashObject(new {
                Options = ScalarConfiguration(options),
                Offers = options.GetOfferValues().Select(Number).ToArray(),
                PlaintiffPreferences = ScalarConfiguration(options.PUtilityCalculator),
                DefendantPreferences = ScalarConfiguration(options.DUtilityCalculator),
                Generator = options.LitigGameDisputeGenerator.GetType().FullName,
                GeneratorOptions = options.LitigGameDisputeGenerator.OptionsString,
                GeneratorScalars = ScalarConfiguration(options.LitigGameDisputeGenerator),
                DeltaOffers = ScalarConfiguration(options.DeltaOffersOptions),
                RoundCosts = options.RoundSpecificBargainingCosts?.Select(c => new[] { Number(c.pCosts), Number(c.dCosts) }).ToArray(),
                EvolutionSettings.MaxIntegralUtility, EvolutionSettings.RoundOffChanceDigits,
                settings.SequenceFormCutOffProbabilityZeroNodes
            });
            var coordinates = developer.InformationSets.OrderBy(n => n.PlayerIndex).ThenBy(n => n.InformationSetNodeNumber)
                .Select(n => new { n.PlayerIndex, n.InformationSetNodeNumber, n.DecisionIndex, n.DecisionByteCode,
                    n.InformationSetContentsString, History = n.LabeledInformationSet.Select(i => new[] { (int)i.decisionIndex, i.information }).ToArray(),
                    Actions = Enumerable.Range(1, n.NumPossibleActions).Select(a => new { Index = a,
                        Label = definition.GetActionString((byte)a, n.DecisionByteCode) }).ToArray() }).ToArray();
            string coordinateHash = HashObject(coordinates);
            using var buffer = new MemoryStream();
            using var writer = new BinaryWriter(buffer, Encoding.UTF8, leaveOpen: true);
            var processor = new TreeWriter(writer, !settings.SequenceFormCutOffProbabilityZeroNodes);
            developer.TreeWalk_Tree(processor);
            writer.Flush();
            string tree = Hash(buffer.ToArray());
            return new("strategic-tree-v1", HashObject(new { tree, coordinates = coordinateHash, parameters }),
                tree, coordinateHash, parameters, processor.Nodes, processor.Terminals,
                developer.InformationSets.Count, developer.InformationSets.Sum(n => n.NumPossibleActions));
        }

        private sealed class TreeWriter : ITreeNodeProcessor<int, int>
        {
            private readonly BinaryWriter writer;
            private readonly bool positiveChance;
            public int Nodes { get; private set; }
            public int Terminals { get; private set; }
            public TreeWriter(BinaryWriter writer, bool positiveChance) { this.writer = writer; this.positiveChance = positiveChance; }
            private void Header(char kind, byte action) { writer.Write(kind); writer.Write(action); Nodes++; }
            private void Decision(Decision decision)
            {
                writer.Write(decision.DecisionByteCode); writer.Write(decision.PlayerIndex); writer.Write(decision.NumPossibleActions);
            }
            public int ChanceNode_Forward(ChanceNode node, IGameState predecessor, byte action, int state)
            {
                Header('C', action); Decision(node.Decision);
                // Conversion occurs on a detached node because the legacy conversion
                // also updates its double probabilities. Fingerprinting must be read-only.
                var probabilities = node.DeepCopy().GetProbabilitiesAsRationals(positiveChance, EvolutionSettings.MaxIntegralUtility);
                writer.Write(probabilities.Length);
                foreach (var value in probabilities)
                {
                    var exact = value.CanonicalForm;
                    writer.Write(exact.Numerator.ToString(CultureInfo.InvariantCulture));
                    writer.Write(exact.Denominator.ToString(CultureInfo.InvariantCulture));
                }
                return state;
            }
            public int InformationSet_Forward(InformationSetNode node, IGameState predecessor, byte action, int state)
            {
                Header('I', action); Decision(node.Decision); writer.Write(node.InformationSetNodeNumber);
                writer.Write(node.InformationSetContentsString); return state;
            }
            public int FinalUtilities_TurnAround(FinalUtilitiesNode node, IGameState predecessor, byte action, int state)
            {
                Header('U', action); Terminals++;
                writer.Write(node.Utilities.Length);
                foreach (double value in node.Utilities) writer.Write(BitConverter.DoubleToInt64Bits(value));
                return 1;
            }
            public int ChanceNode_Backward(ChanceNode node, IEnumerable<int> children) { writer.Write('E'); return 1; }
            public int InformationSet_Backward(InformationSetNode node, IEnumerable<int> children) { writer.Write('E'); return 1; }
        }
    }
}
