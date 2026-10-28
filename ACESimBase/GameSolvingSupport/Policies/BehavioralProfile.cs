using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;

namespace ACESimBase.GameSolvingSupport.Policies;

/// <summary>An immutable behavioral policy at one information set. No solver state or equilibrium claim.</summary>
public sealed class InformationSetPolicy
{
    public string Key { get; }
    public byte Player { get; }
    public byte Decision { get; }
    public IReadOnlyList<string> ActionLabels { get; }
    public IReadOnlyList<double> Probabilities { get; }

    public InformationSetPolicy(string key, byte player, byte decision, IEnumerable<string> actionLabels,
        IEnumerable<double> probabilities)
    {
        if (string.IsNullOrEmpty(key)) throw new ArgumentException("An information-set key is required.", nameof(key));
        var labels = actionLabels?.ToArray() ?? throw new ArgumentNullException(nameof(actionLabels));
        var values = probabilities?.ToArray() ?? throw new ArgumentNullException(nameof(probabilities));
        if (values.Length == 0 || labels.Length != values.Length || labels.Any(x => x == null) ||
            values.Any(x => !double.IsFinite(x) || x < 0 || x > 1) || Math.Abs(values.Sum() - 1) > 1e-10)
            throw new InvalidDataException("Invalid behavioral policy at " + key);
        Key = key;
        Player = player;
        Decision = decision;
        ActionLabels = Array.AsReadOnly(labels);
        Probabilities = Array.AsReadOnly(values);
    }
}

/// <summary>
/// A named collection of immutable player policies. OptionSet is provenance, not a payoff-cache key.
/// Identical policy layouts may occur in games with different payoffs or chance probabilities.
/// </summary>
public sealed class BehavioralProfile
{
    public string Name { get; }
    public string OptionSet { get; }
    public IReadOnlyDictionary<string, InformationSetPolicy> InformationSets { get; }

    public BehavioralProfile(string name, string optionSet, IEnumerable<InformationSetPolicy> informationSets)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        OptionSet = optionSet;
        InformationSets = new ReadOnlyDictionary<string, InformationSetPolicy>(
            (informationSets ?? throw new ArgumentNullException(nameof(informationSets))).ToDictionary(x => x.Key, StringComparer.Ordinal));
    }

    public PlayerPolicy ForPlayer(byte player) => new(player,
        InformationSets.Values.Where(x => x.Player == player));

    /// <summary>Combine explicitly selected player components; duplicate information sets are rejected.</summary>
    public static BehavioralProfile Combine(string name, string optionSet, params PlayerPolicy[] components)
    {
        if (components.Select(x => x.Player).Distinct().Count() != components.Length)
            throw new ArgumentException("Select exactly one policy for each supplied player.", nameof(components));
        return new(name, optionSet, components.SelectMany(x => x.InformationSets.Values));
    }
}

/// <summary>One player's behavioral choices, independent of its original opponents or training run.</summary>
public sealed class PlayerPolicy
{
    public byte Player { get; }
    public IReadOnlyDictionary<string, InformationSetPolicy> InformationSets { get; }

    public PlayerPolicy(byte player, IEnumerable<InformationSetPolicy> informationSets)
    {
        var policies = informationSets.ToArray();
        if (policies.Any(x => x.Player != player))
            throw new ArgumentException("A player policy cannot contain another player's decisions.", nameof(informationSets));
        Player = player;
        InformationSets = new ReadOnlyDictionary<string, InformationSetPolicy>(policies.ToDictionary(x => x.Key, StringComparer.Ordinal));
    }
}
