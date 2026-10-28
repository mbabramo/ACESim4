using ACESim;
using ACESimBase.GameSolvingSupport.GameTree;
using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;

namespace ACESimBase.GameSolvingSupport.Policies;

/// <summary>Compatibility adapter for the predictive benchmark's version-1 gzip/JSON archive.
/// Saved current/average policies are not a resumable training checkpoint. Selection is provenance,
/// not a mixture weight. The historical schema and information-set ordering are preserved.</summary>
public static class BenchmarkProfileArchive
{
    public sealed record Contents(BehavioralProfile Current, BehavioralProfile Average, JsonElement Selection);

    public static void Write(StrategiesDeveloperBase developer, string path, object selection,
        JsonSerializerOptions options = null)
    {
        using var stream = File.Create(path);
        using var zip = new GZipStream(stream, CompressionLevel.Optimal);
        JsonSerializer.Serialize(zip, new
        {
            schemaVersion = 1,
            optionsName = developer.GameDefinition.OptionSetName,
            selection,
            informationSets = developer.InformationSets.Select(node => new
            {
                node.PlayerIndex, node.DecisionIndex, node.DecisionByteCode,
                identity = node.InformationSetWithAlphabeticalLabels(developer.GameDefinition),
                current = node.GetCurrentProbabilitiesAsArray(), average = node.GetAverageStrategiesAsArray()
            }).ToArray()
        }, options);
    }

    /// <summary>Validate the complete legacy mapping before returning detached immutable profiles.
    /// Version 1 binds the ordered information-set layout, not the payoff or chance specification.</summary>
    public static Contents Read(StrategiesDeveloperBase developer, string path)
    {
        using var stream = File.OpenRead(path);
        using var zip = new GZipStream(stream, CompressionMode.Decompress);
        using var document = JsonDocument.Parse(zip);
        var root = document.RootElement;
        if (root.GetProperty("schemaVersion").GetInt32() != 1)
            throw new InvalidDataException("Unsupported benchmark profile schema.");
        var sets = root.GetProperty("informationSets").EnumerateArray().ToArray();
        if (sets.Length != developer.InformationSets.Count)
            throw new InvalidDataException("Saved profile has different information set count.");
        var current = new InformationSetPolicy[sets.Length];
        var average = new InformationSetPolicy[sets.Length];
        for (int index = 0; index < sets.Length; index++)
        {
            var node = developer.InformationSets[index];
            var saved = sets[index];
            if (saved.GetProperty("identity").GetString() != node.InformationSetWithAlphabeticalLabels(developer.GameDefinition) ||
                saved.GetProperty("PlayerIndex").GetByte() != node.PlayerIndex ||
                saved.GetProperty("DecisionIndex").GetByte() != node.DecisionIndex ||
                saved.GetProperty("DecisionByteCode").GetByte() != node.DecisionByteCode)
                throw new InvalidDataException("Saved profile information set identity differs.");
            var key = PolicyAccess.Key(node, developer.GameDefinition);
            var labels = PolicyAccess.ActionLabels(node, developer.GameDefinition);
            current[index] = new(key, node.PlayerIndex, node.DecisionByteCode, labels,
                saved.GetProperty("current").EnumerateArray().Select(x => x.GetDouble()));
            average[index] = new(key, node.PlayerIndex, node.DecisionByteCode, labels,
                saved.GetProperty("average").EnumerateArray().Select(x => x.GetDouble()));
        }
        var optionSet = root.GetProperty("optionsName").GetString();
        return new(new("current", optionSet, current), new("average", optionSet, average),
            root.GetProperty("selection").Clone());
    }

    /// <summary>Historical benchmark replay: installs current and normalized average probabilities.
    /// Regrets and iteration clocks are retained, so this must not be used to resume training.</summary>
    public static void LoadForReplay(StrategiesDeveloperBase developer, string path)
    {
        var saved = Read(developer, path);
        PolicyAccess.ApplyCurrent(developer, saved.Current);
        foreach (var node in developer.InformationSets)
        {
            var average = saved.Average.InformationSets[PolicyAccess.Key(node, developer.GameDefinition)].Probabilities;
            for (int action = 0; action < node.NumPossibleActions; action++)
            {
                node.NodeInformation[InformationSetNode.averageStrategyProbabilityDimension, action] = average[action];
                node.NodeInformation[InformationSetNode.cumulativeStrategyDimension, action] = average[action];
            }
        }
    }
}
