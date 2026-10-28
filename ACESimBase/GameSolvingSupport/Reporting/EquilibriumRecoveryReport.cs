using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ACESimBase.GameSolvingSupport.Execution;

namespace ACESimBase.GameSolvingSupport.Reporting;

/// <summary>Article-compatible recovery accounting CSV, separate from the solver and selection weights.</summary>
public static class EquilibriumRecoveryReport
{
    public static string Build(string optionSetName, EquilibriumSearchResult result) =>
        Build(optionSetName, result.ToLegacyProfiles(), result.RequestedPriors, result.ExactAttempts, result.InexactAttempts);
    public static string Build(
        string optionSetName,
        IReadOnlyList<(double[] equilibrium, int frequency)> equilibria,
        int requestedPriors,
        int exactSolverAttempts,
        int inexactSolverAttempts)
    {
        int attemptedSolves = exactSolverAttempts + inexactSolverAttempts;
        int verifiedRecoveries = equilibria.Sum(item => item.frequency);
        var output = new StringBuilder();
        output.AppendLine(
            "OptionSetName,Requested Priors,Attempted Solves,Inexact Attempts,Exact Attempts," +
            "Verified Recoveries,Distinct Reported Strategy Profiles,Equilibrium Number," +
            "Recovery Count,Recovery Share of Verified Recoveries,Verification Status,Distinctness Criterion");
        for (int index = 0; index < equilibria.Count; index++)
        {
            (double[] _, int frequency) = equilibria[index];
            string recoveryShare = verifiedRecoveries == 0
                ? string.Empty
                : ((double)frequency / verifiedRecoveries).ToString("G17", System.Globalization.CultureInfo.InvariantCulture);
            output.AppendLine(string.Join(",", new[]
            {
                CsvField(optionSetName),
                requestedPriors.ToString(System.Globalization.CultureInfo.InvariantCulture),
                attemptedSolves.ToString(System.Globalization.CultureInfo.InvariantCulture),
                inexactSolverAttempts.ToString(System.Globalization.CultureInfo.InvariantCulture),
                exactSolverAttempts.ToString(System.Globalization.CultureInfo.InvariantCulture),
                verifiedRecoveries.ToString(System.Globalization.CultureInfo.InvariantCulture),
                equilibria.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),
                (index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture),
                frequency.ToString(System.Globalization.CultureInfo.InvariantCulture),
                recoveryShare,
                CsvField("Retained after configured equilibrium checks"),
                CsvField("Exact equality of normalized action-probability vectors"),
            }));
        }
        return output.ToString();
    }

    private static string CsvField(string value) =>
        $"\"{(value ?? string.Empty).Replace("\"", "\"\"")}\"";
}
