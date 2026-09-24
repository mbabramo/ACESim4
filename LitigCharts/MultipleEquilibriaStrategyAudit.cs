using ACESim;
using ACESimBase.Games.EFGFileGame;
using ACESimBase.Games.LitigGame.ManualReports;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Globalization;
using System.Text;
using System.Threading.Tasks;

namespace LitigCharts;

/// <summary>Recheck saved profiles without running any equilibrium search.</summary>
public static class MultipleEquilibriaStrategyAudit
{
    public sealed record Profile(string OptionSet, int Equilibrium, int ActionRows,
        double[] PlayerGains, double MaximumGain, string ProfileFile, string ActionReport,
        string ReplayReport, int ReproducedOutcomeCells, string[] DiagramSources);

    public static async Task<Profile[]> RunAsync(LitigGameOptions[] options, string input, string output, string prefix = "CS004ME",
        bool writeStudyData = false, bool generateDiagrams = true, bool numbered = true)
    {
        var results = new List<Profile>();
        // Model initialization uses static caches, so keep cases sequential.
        foreach (var option in options)
        {
            var developer = await ArticleWorkedPathExtraction.InitializeAsync(option);
            developer.EvolutionSettings.UseAcceleratedBestResponse = true;
            developer.EvolutionSettings.UseCurrentStrategyForBestResponse = true;
            developer.EvolutionSettings.RoundOffLowProbabilitiesBeforeAcceleratedBestResponse = false;
            developer.EvolutionSettings.RoundOffLowProbabilitiesBeforeReporting = false;
            developer.SaveWeightedGameProgressesAfterEachReport = true;
            string casePrefix = prefix ?? (option.LoserPaysAfterAbandonment ? "CS006EF" : "CS004");
            string profileFile = Path.Combine(input, casePrefix + " " + option.Name + " -equ.csv");
            var profiles = File.ReadLines(profileFile).Where(s => !string.IsNullOrWhiteSpace(s)).ToArray();
            if (!numbered && profiles.Length != 1) throw new InvalidDataException("Expected one saved equilibrium: " + profileFile);
            for (int i = 0; i < profiles.Length; i++)
            {
                var fallbacks = ArticleWorkedPathExtraction.LoadProfile(developer,
                    profiles[i].Split(',').Select(EFGFileReader.RationalStringToDouble).ToArray());
                string actions = Path.Combine(input, $"{casePrefix} {option.Name} " + (numbered ? $"-Eq{i + 1}" : "") + "-InformationSetActions.csv");
                int rows = ArticleWorkedPathExtraction.ValidateActionReport(developer, i + 1, actions);
                developer.CalculateBestResponse(false);
                double[] gains = developer.Status.BestResponseImprovement.ToArray();
                if (!developer.Status.BestResponseReflectsCurrentStrategy ||
                    gains.Any(g => !double.IsFinite(g) || Math.Abs(g) > 1e-7))
                    throw new InvalidDataException($"Saved profile {option.Name} Eq{i + 1} failed the current-strategy best-response audit: {string.Join(", ", gains)}");
                // Rebuild diagrams from this profile alone. Older raw TeX accumulated
                // all preceding profiles' paths and cannot describe a single equilibrium.
                developer.SavedWeightedGameProgresses.Clear();
                developer.ActionStrategy = ActionStrategies.CurrentProbability;
                var replay = await developer.GenerateReportsByPlaying(false);
                string stem = $"{casePrefix} {option.Name}" + (numbered ? $" -Eq{i + 1}" : "");
                string replayFile = Path.Combine(output, "Sources", "Replayed reports", stem + ".csv");
                Directory.CreateDirectory(Path.GetDirectoryName(replayFile));
                File.WriteAllText(replayFile, replay.csvReports.Single(), new UTF8Encoding(false));
                int outcomeCells = ValidateReplay(Path.Combine(input, stem + ".csv"), replayFile);
                if (writeStudyData)
                    AgreementToBargainStudy.ExportProfile(developer, option, i + 1, profileFile, actions, replayFile, output, fallbacks);
                string risk = ArticleResultsLayout.Risk(option);
                string fee = LitigGameCorrelatedSignalsArticleLauncher.FeeRuleLabel(option);
                string diagrams = Path.Combine(output, "Individual simulations", risk, fee, "Sources");
                Directory.CreateDirectory(diagrams);
                var diagramSources = new List<string>();
                foreach (var report in generateDiagrams ? developer.GameDefinition.ProduceManualReports(developer.SavedWeightedGameProgresses, $"-Eq{i + 1}") : Enumerable.Empty<(string suffix, string reportcontent)>())
                {
                    if (!report.suffix.EndsWith(".tex", StringComparison.Ordinal)) continue;
                    string path = Path.Combine(diagrams, $"{prefix} {option.Name} " + report.suffix);
                    string contents = string.Join("\n", report.reportcontent.Replace("\r\n", "\n").Split('\n')
                        .Where(line => !line.Contains(@"node[midway] {\huge Costs:", StringComparison.Ordinal)));
                    File.WriteAllText(path, contents, new UTF8Encoding(false));
                    diagramSources.Add(path);
                }
                if (generateDiagrams && diagramSources.Count != 6) throw new InvalidDataException("Expected six individual diagram sources for " + stem);
                results.Add(new(option.Name, i + 1, rows, gains, Math.Max(0, gains.Max()), profileFile, actions,
                    replayFile, outcomeCells, diagramSources.ToArray()));
            }
            Console.WriteLine($"Audited {profiles.Length} saved profiles for {option.Name}; no equilibrium searches performed.");
        }
        return results.ToArray();
    }

    public static int ValidateReplay(string original, string replay)
    {
        var expected = PublicationFigures.ReadCsv(original);
        var actual = PublicationFigures.ReadCsv(replay);
        if (expected.Length != actual.Length) throw new InvalidDataException("Replay row count mismatch: " + original);
        int cells = 0;
        for (int i = 0; i < expected.Length; i++)
        {
            if (expected[i]["Filter"] != actual[i]["Filter"]) throw new InvalidDataException("Replay filter mismatch: " + original);
            foreach (var cell in expected[i])
            {
                if (cell.Key is "Exploit" or "Refine" or "Seconds" || !double.TryParse(cell.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double value)) continue;
                if (!actual[i].TryGetValue(cell.Key, out string text) ||
                    !double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double observed) ||
                    !double.IsFinite(observed) || Math.Abs(value - observed) > 1e-5 * Math.Max(1, Math.Abs(value)))
                    throw new InvalidDataException($"Replay mismatch in {original}, {cell.Key}, row {i}: {cell.Value} vs {text}");
                cells++;
            }
        }
        return cells;
    }
}
