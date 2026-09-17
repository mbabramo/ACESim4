using ACESim;
using ACESimBase.Games.EFGFileGame;
using ACESimBase.Games.LitigGame.ManualReports;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace LitigCharts;

/// <summary>Recheck saved profiles without running any equilibrium search.</summary>
public static class MultipleEquilibriaStrategyAudit
{
    public sealed record Profile(string OptionSet, int Equilibrium, int ActionRows,
        double[] PlayerGains, double MaximumGain, string ProfileFile, string ActionReport);

    public static async Task<Profile[]> RunAsync(LitigGameOptions[] options, string input)
    {
        var results = new List<Profile>();
        // Model initialization uses static caches, so keep cases sequential.
        foreach (var option in options)
        {
            var developer = await ArticleWorkedPathExtraction.InitializeAsync(option);
            developer.EvolutionSettings.UseAcceleratedBestResponse = true;
            developer.EvolutionSettings.UseCurrentStrategyForBestResponse = true;
            developer.EvolutionSettings.RoundOffLowProbabilitiesBeforeAcceleratedBestResponse = false;
            string profileFile = Path.Combine(input, "CS004ME " + option.Name + " -equ.csv");
            var profiles = File.ReadLines(profileFile).Where(s => !string.IsNullOrWhiteSpace(s)).ToArray();
            for (int i = 0; i < profiles.Length; i++)
            {
                ArticleWorkedPathExtraction.LoadProfile(developer,
                    profiles[i].Split(',').Select(EFGFileReader.RationalStringToDouble).ToArray());
                string actions = Path.Combine(input, $"CS004ME {option.Name} -Eq{i + 1}-InformationSetActions.csv");
                int rows = ArticleWorkedPathExtraction.ValidateActionReport(developer, i + 1, actions);
                developer.CalculateBestResponse(false);
                double[] gains = developer.Status.BestResponseImprovement.ToArray();
                if (!developer.Status.BestResponseReflectsCurrentStrategy ||
                    gains.Any(g => !double.IsFinite(g) || Math.Abs(g) > 1e-7))
                    throw new InvalidDataException($"Saved profile {option.Name} Eq{i + 1} failed the current-strategy best-response audit: {string.Join(", ", gains)}");
                results.Add(new(option.Name, i + 1, rows, gains, Math.Max(0, gains.Max()), profileFile, actions));
            }
            Console.WriteLine($"Audited {profiles.Length} saved profiles for {option.Name}; no equilibrium searches performed.");
        }
        return results.ToArray();
    }
}
