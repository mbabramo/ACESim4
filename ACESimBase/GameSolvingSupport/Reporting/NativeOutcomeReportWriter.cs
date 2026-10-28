using ACESim;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace ACESimBase.GameSolvingSupport.Reporting;

/// <summary>Enumerate native economic reports using the initialized game's current ActionStrategy.
/// Keeps native row denominators and latent-state expansion. This is a live reporting operation,
/// unlike the detached-profile PolicyEvaluator; use a separate developer for concurrent reporting.</summary>
public static class NativeOutcomeReportWriter
{
    public sealed record Result(ReportCollection Reports, double[] Utilities, double TerminalProbability);

    public static async Task<Result> GenerateAsync(StrategiesDeveloperBase developer, double[] expectedUtilities = null)
    {
        var reports = developer.GetSimpleReportDefinitions().Select(x => new SimpleReport(x)).ToArray();
        var enumeratedUtilities = new double[developer.NumNonChancePlayers];
        double mass = 0;
        bool previousReportingMode = developer.GamePlayer.ReportingMode;
        developer.GamePlayer.ReportingMode = true;
        try
        {
            await developer.ProcessAllPathsAsync(developer.GetStartOfGameHistoryPoint().ToStorable(), (completed, probability) =>
            {
                if (probability <= 0) return Task.CompletedTask;
                mass += probability;
                var utilities = developer.GetUtilities(in completed);
                for (int player = 0; player < developer.NumNonChancePlayers; player++)
                    enumeratedUtilities[player] += probability * utilities[player];
                var actions = completed.ShallowCopyToRefStruct().GetActionsToHere(developer.Navigation);
                var (progress, _) = developer.GamePlayer.PlayPath(actions, false);
                var progresses = progress.GetGameProgressIncludingAnySplits();
                if (developer.GameDefinition.GenerateConsistentGameProgresses)
                    progresses = progresses.SelectMany(x => x.progress.BayesianCalculations_GenerateAllConsistentGameProgresses(x.Item2));
                foreach (var item in progresses)
                    foreach (var report in reports)
                        report.ProcessGameProgress(item.progress, probability * item.Item2);
                return Task.CompletedTask;
            });
            if (Math.Abs(mass - 1) > 1E-9) throw new ArithmeticException("Outcome path mass differs from one: " + mass);
            if (expectedUtilities != null && enumeratedUtilities.Zip(expectedUtilities, (actual, expected) => Math.Abs(actual - expected)).Any(error => error > 1E-8))
                throw new ArithmeticException("Outcome enumeration utilities differ from the exact evaluated profile utilities.");
            var result = new ReportCollection();
            foreach (var report in reports) result.Add(report.BuildReport());
            return new(result, enumeratedUtilities, mass);
        }
        finally { developer.GamePlayer.ReportingMode = previousReportingMode; }
    }
}
