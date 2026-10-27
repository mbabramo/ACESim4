using System;
using System.Linq;
using System.Threading.Tasks;

namespace ACESim
{
    public abstract partial class StrategiesDeveloperBase
    {
        public double[] BenchmarkLastEnumeratedUtilities { get; private set; }
        /// <summary>Exact native report path/latent-state enumeration without a producer-consumer queue.
        /// Exceptions propagate directly instead of leaving a producer blocked behind a failed consumer.</summary>
        public async Task<ReportCollection> GenerateBenchmarkReportsAsync()
        {
            var definitions = GetSimpleReportDefinitions();
            var reports = definitions.Select(x => new SimpleReport(x)).ToArray();
            var expectedUtilities = Status.UtilitiesOverall?.ToArray();
            var enumeratedUtilities = new double[NumNonChancePlayers];
            double mass = 0;
            bool previousReportingMode = GamePlayer.ReportingMode;
            GamePlayer.ReportingMode = true;
            try
            {
                await ProcessAllPathsAsync(GetStartOfGameHistoryPoint().ToStorable(), (completed, probability) =>
                {
                    if (probability <= 0) return Task.CompletedTask;
                    mass += probability;
                    var utilities = GetUtilities(in completed);
                    for (int player = 0; player < NumNonChancePlayers; player++)
                        enumeratedUtilities[player] += probability * utilities[player];
                    var actions = completed.ShallowCopyToRefStruct().GetActionsToHere(Navigation);
                    var (progress, _) = GamePlayer.PlayPath(actions, false);
                    var progresses = progress.GetGameProgressIncludingAnySplits();
                    if (GameDefinition.GenerateConsistentGameProgresses)
                        progresses = progresses.SelectMany(x => x.progress.BayesianCalculations_GenerateAllConsistentGameProgresses(x.Item2));
                    foreach (var item in progresses)
                        foreach (var report in reports)
                            report.ProcessGameProgress(item.progress, probability * item.Item2);
                    return Task.CompletedTask;
                });
                BenchmarkLastEnumeratedUtilities = enumeratedUtilities;
                if (Math.Abs(mass - 1) > 1E-9) throw new ArithmeticException("Outcome path mass differs from one: " + mass);
                if (expectedUtilities != null && enumeratedUtilities.Zip(expectedUtilities, (actual, expected) => Math.Abs(actual - expected)).Any(error => error > 1E-8))
                    throw new ArithmeticException("Outcome enumeration utilities differ from the exact evaluated profile utilities.");
                var result = new ReportCollection();
                foreach (var report in reports) result.Add(report.BuildReport());
                return result;
            }
            finally { GamePlayer.ReportingMode = previousReportingMode; }
        }
    }
}
