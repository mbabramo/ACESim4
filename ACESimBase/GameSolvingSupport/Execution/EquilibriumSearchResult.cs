using System;
using System.Collections.Generic;
using System.Linq;

namespace ACESimBase.GameSolvingSupport.Execution;

/// <summary>One retained profile and its recovery count, not an equilibrium-selection weight.</summary>
public sealed class EquilibriumRecovery
{
    public IReadOnlyList<double> Probabilities { get; }
    public int Count { get; }
    public EquilibriumRecovery(IEnumerable<double> probabilities, int count)
    {
        Probabilities = Array.AsReadOnly(probabilities.ToArray());
        Count = count;
    }
}

/// <summary>Search provenance kept separately from policies and reporting. Retained profiles
/// have passed the caller's configured checks; this type does not strengthen their certificate.</summary>
public sealed class EquilibriumSearchResult
{
    public IReadOnlyList<EquilibriumRecovery> Recoveries { get; }
    public int RequestedPriors { get; }
    public int ExactAttempts { get; }
    public int InexactAttempts { get; }
    public int VerifiedRecoveries => Recoveries.Sum(x => x.Count);
    public EquilibriumSearchResult(IEnumerable<(double[] equilibrium, int frequency)> profiles,
        int requestedPriors, int exactAttempts, int inexactAttempts)
    {
        Recoveries = Array.AsReadOnly(profiles.Select(x => new EquilibriumRecovery(x.equilibrium, x.frequency)).ToArray());
        RequestedPriors = requestedPriors;
        ExactAttempts = exactAttempts;
        InexactAttempts = inexactAttempts;
    }
    public List<(double[] equilibrium, int frequency)> ToLegacyProfiles() =>
        Recoveries.Select(x => (x.Probabilities.ToArray(), x.Count)).ToList();
}
