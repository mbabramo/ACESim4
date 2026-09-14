using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace LitigCharts;

/// <summary>One vocabulary and path convention for article figures, tables and case records.</summary>
public static class ArticleResultsLayout
{
    public const string RiskComparison = "Risk Comparison";
    public static string Cost(double cost) => "cost-" + cost.ToString("0.############", CultureInfo.InvariantCulture);
    public static string Risk(double alpha) => alpha switch
    {
        0 => "Risk Neutral", 2 => "Risk Averse",
        _ => "Risk Averse alpha " + alpha.ToString(CultureInfo.InvariantCulture)
    };
    public static string Specification(string family) => family switch
    {
        "baseline" => "Baseline",
        "baseline-offers-15" => "Offer-grid sensitivity",
        "low-noise" => "Low noise",
        "high-noise" => "High noise",
        "direct-binary-state-signals" => "Direct binary-state signals",
        "truth-conditioned-latent-merits" => "Truth-conditioned latent merits",
        "center-weighted-continuous-merits" => "Center-weighted continuous merits",
        "polarized-continuous-merits" => "Polarized continuous merits",
        "all-costs-avoidable" => "All litigation costs avoidable at bargaining",
        "all-costs-sunk" => "All litigation costs sunk before bargaining",
        _ => throw new InvalidDataException("Unregistered article specification: " + family)
    };
    public static string Aggregate(string root, string family, string risk) =>
        Path.Combine(root, Specification(family), risk);
    public static string Individual(string root, IReadOnlyDictionary<string, string> row) =>
        Path.Combine(root, Specification(WelfareOutcomeExhibits.Family(row)),
            Risk(PublicationTables.Number(row, "CARA Alpha")), WelfareOutcomeExhibits.FeeLabel(row));
    public static string Source(string directory, string stem, string extension) =>
        Path.Combine(directory, "Sources", stem + extension);
    public static string RenderedArtifact(string source, string extension)
    {
        string directory = Path.GetDirectoryName(source);
        if (Path.GetFileName(directory).Equals("Sources", StringComparison.OrdinalIgnoreCase))
            directory = Path.GetDirectoryName(directory);
        return Path.Combine(directory, Path.GetFileNameWithoutExtension(source) + extension);
    }
}
