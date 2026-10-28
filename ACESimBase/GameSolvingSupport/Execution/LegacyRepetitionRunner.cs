using ACESim;
using ACESimBase.GameSolvingAlgorithms;
using ACESimBase.GameSolvingSupport.SolverSpecificSupport;
using ACESimBase.Util.Debugging;
using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace ACESimBase.GameSolvingSupport.Execution;

/// <summary>The launcher's historical repetition protocol. GameNumber is a run identifier;
/// its meaning as an initialization or seed is solver-specific. Sequence-form priors are a
/// separate batched search. This does not define a population or selection weights.</summary>
public static class LegacyRepetitionRunner
{
    public sealed record Request(string OptionSetName, int Repetition, int StartGameNumber,
        int? RestrictToScenarioIndex, string MasterReportName);

    public static Task<ReportCollection> RunAsync(IStrategiesDeveloper developer, Request request,
        Action<string> log = null) => RunAsync(developer, request, log, Task.Delay, Random.Shared.NextDouble);

    internal static async Task<ReportCollection> RunAsync(IStrategiesDeveloper developer, Request request,
        Action<string> log, Func<int, Task> delay, Func<double> jitter)
    {
        log ??= message => Debug.WriteLine(message);
        developer.EvolutionSettings.GameNumber = request.StartGameNumber + request.Repetition;
        if (request.Repetition > 0) developer.Reinitialize();
        int retriesRemaining = 20, failuresSoFar = 0;
        while (true)
        {
            try
            {
                return await developer.DevelopStrategies(request.OptionSetName, request.RestrictToScenarioIndex,
                    request.MasterReportName);
            }
            catch (UnrollingException) { throw; }
            catch (Exception) when (developer is SequenceForm)
            {
                // A failed SequenceForm tree must be retried in a fresh coordinator worker.
                // Its exact/inexact prior fallback is owned by SequenceFormPriorSearch.
                throw;
            }
            catch (Exception error)
            {
                log(error.Message + error.StackTrace);
                TabbedText.WriteLine($"Error: {error}");
                TabbedText.WriteLine(error.StackTrace);
                retriesRemaining--;
                failuresSoFar++;
                if (retriesRemaining < 0) throw new Exception("Repeated failures", error);
                int milliseconds = RetryDelayMilliseconds(failuresSoFar, jitter());
                log($"Delaying {milliseconds} milliseconds");
                TabbedText.WriteLine($"Delaying {milliseconds} milliseconds");
                await delay(milliseconds);
            }
        }
    }

    public static int RetryDelayMilliseconds(int failures, double jitter)
    {
        if (failures < 1 || jitter < 0 || jitter >= 1 || double.IsNaN(jitter))
            throw new ArgumentOutOfRangeException();
        double backoff = Math.Min(60_000, Math.Pow(1.3, failures));
        return (int)Math.Min(100_000, backoff + (failures > 1 ? 10_000 * jitter : 0));
    }
}
