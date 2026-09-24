using ACESim;
using ACESimBase.GameSolvingAlgorithms;
using ACESimBase.GameSolvingAlgorithms.ECTAAlgorithm;
using ACESimBase.GameSolvingSupport.ExactValues;
using System;
using System.IO;
using System.Diagnostics;
using System.Linq;
using System.Security.Cryptography;

namespace ACESimBase.Games.LitigGame.ManualReports;

/// <summary>One independent floating start, evaluated after every actual pivot.
/// No exact first solve, exact fallback, warm start, or support-indifference filter.</summary>
public static class ArticleApproximateSearch
{
    public sealed record PivotAudit(int Pivot, int LeavingVariable, int EnteringVariable, bool Final,
        bool Valid, string InvalidReason, double? FlowResidual, int[] PriorCompletedInformationSets,
        double[] SignedRawGains, double[] AcceptanceGains, double? AverageGain, double? MaximumGain,
        double[] CertaintyEquivalentGains, string CompleteProfileSha256);
    public sealed record Attempt(int StartIndex, int ActualPriorSeed, string Arithmetic, double Cutoff,
        ArticleApproximateGainUnits GainUnits, double[] TerminalUtilityRanges, double[] Prior,
        int EvaluatedPivots, int InvalidCandidates, ArticleApproximatePolicy.Decision Decision,
        ArticleApproximatePolicy.Candidate BestCandidate, string Error)
    {
        public Timing Performance { get; init; }
    }
    public sealed record Timing(double TraceSeconds, double CandidateCheckSeconds,
        double BestResponseSeconds, double AuditPersistenceSeconds, double SolverAndTraceOtherSeconds,
        double FinalSelectionCheckSeconds);
    private sealed class PolicyStop : Exception { }

    public static string ProfileHash(double[] values)
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true))
            foreach (double value in values) writer.Write(BitConverter.DoubleToInt64Bits(value));
        return Convert.ToHexString(SHA256.HashData(stream.ToArray())).ToLowerInvariant();
    }

    public static Attempt Run(SequenceForm developer, LitigGameOptions options, int startIndex,
        ArticleApproximatePolicy policy, Action<PivotAudit> savePivot, Action<double[]> savePrior)
    {
        if (startIndex < 0 || startIndex > int.MaxValue-1_000_000 || savePivot == null || savePrior == null)
            throw new ArgumentException("Supply a valid start index and durable audit writers.");
        if (developer.EvolutionSettings.ParallelOptimization || developer.NumNonChancePlayers != 2 ||
            !options.IncludeAgreementToBargainDecisions)
            throw new InvalidOperationException("Requires the full single-threaded two-player agreement game.");
        var settings = developer.EvolutionSettings;
        settings.UseAcceleratedBestResponse = true;
        settings.UseCurrentStrategyForBestResponse = true;
        settings.RoundOffLowProbabilitiesBeforeAcceleratedBestResponse = false;
        settings.RoundOffLowProbabilitiesBeforeReporting = false;
        int[] counts = developer.InformationSets.OrderBy(i => i.PlayerIndex).ThenBy(i => i.InformationSetNodeNumber)
            .Select(i => (int)i.NumPossibleActions).ToArray();
        double[] ranges = Enumerable.Range(0,2).Select(p => developer.FinalUtilitiesNodes.Max(n => n.Utilities[p])-
            developer.FinalUtilitiesNodes.Min(n => n.Utilities[p])).ToArray();
        var calculators = new[] { options.PUtilityCalculator, options.DUtilityCalculator };
        var selection = new ArticleApproximatePolicy.Selection();
        ECTAStrategyDiagnostics<InexactValue> diagnostics = null;
        double[] prior = null;
        int evaluated = 0, invalid = 0;
        string error = null;
        var traceTimer = Stopwatch.StartNew();
        double checkSeconds = 0, bestResponseSeconds = 0, persistenceSeconds = 0;
        try
        {
            developer.TraceECTA<InexactValue>(initialProbabilities: null, seed: startIndex,
                probabilityFloor: 0.001, maxPivots: ArticleApproximatePolicy.PivotCap,
                beforeSolve: tree => {
                    diagnostics = new(tree, developer.TraceOutcomeUtilities());
                    if (!counts.SequenceEqual(diagnostics.InformationSetIndices.Select(i => tree.informationSets[i].numMoves)))
                        throw new InvalidOperationException("Projection and complete-profile action coordinates differ.");
                    prior = diagnostics.PriorProbabilities;
                    savePrior(prior.ToArray());
                },
                afterPivot: (tree, snapshot) => {
                    var checkTimer = Stopwatch.StartNew();
                    evaluated = snapshot.Pivot;
                    ECTAStrategyProjection projection = null;
                    PivotAudit audit;
                    try
                    {
                        projection = diagnostics.Project(snapshot);
                        double[] rounded = policy.Round(projection.Probabilities, counts);
                        developer.SetInformationSetsToEquilibrium(rounded);
                        double[] actual = developer.GetEquilibriumFromInformationSets();
                        if (!rounded.SequenceEqual(actual)) throw new InvalidDataException("Strategy installation changed the rounded profile.");
                        var bestResponseTimer = Stopwatch.StartNew();
                        try { developer.CalculateBestResponse(false); } // full own continuation, including agreement actions
                        finally { bestResponseSeconds += bestResponseTimer.Elapsed.TotalSeconds; }
                        if (!developer.Status.BestResponseReflectsCurrentStrategy)
                            throw new InvalidDataException("Best response did not evaluate the saved current strategy.");
                        double[] raw = developer.Status.BestResponseImprovement.ToArray();
                        double[] gains = policy.Scale(raw, ranges);
                        double[] ce = Enumerable.Range(0,2).Select(p =>
                            ArticleApproximatePolicy.CertaintyEquivalentWealth(calculators[p], developer.Status.BestResponseUtilities[p])-
                            ArticleApproximatePolicy.CertaintyEquivalentWealth(calculators[p], developer.Status.UtilitiesOverall[p])).ToArray();
                        selection.Observe(snapshot.Pivot, gains.Average(), actual);
                        audit = new(snapshot.Pivot, snapshot.LeavingVariable, snapshot.EnteringVariable, snapshot.Final,
                            true, null, projection.FlowResidual, projection.PriorCompletedInformationSets, raw, gains,
                            gains.Average(), gains.Max(), ce, ProfileHash(actual));
                    }
                    catch (Exception ex) when (ex is InvalidDataException || ex is InvalidOperationException)
                    {
                        invalid++;
                        audit = new(snapshot.Pivot, snapshot.LeavingVariable, snapshot.EnteringVariable, snapshot.Final,
                            false, ex.Message, projection != null && double.IsFinite(projection.FlowResidual) ? projection.FlowResidual : null,
                            projection?.PriorCompletedInformationSets,
                            null, null, null, null, null, null);
                    }
                    checkSeconds += checkTimer.Elapsed.TotalSeconds;
                    var persistenceTimer = Stopwatch.StartNew();
                    try { savePivot(audit); } // persistence failures propagate and cannot become successful attempts
                    finally { persistenceSeconds += persistenceTimer.Elapsed.TotalSeconds; }
                    if (selection.Finished != null) throw new PolicyStop();
                    if (snapshot.Pivot == ArticleApproximatePolicy.PivotCap)
                    {
                        selection.EndAtCap();
                        throw new PolicyStop();
                    }
                });
            selection.EndWithoutCap("algorithm-ended-without-early-threshold", evaluated);
        }
        catch (PolicyStop) { }
        catch (Exception ex) when (ex is InvalidOperationException || ex is ArithmeticException || ex is ECTAException)
        {
            error = ex.ToString();
            selection.EndWithoutCap("algorithm-error-before-cap", evaluated);
        }
        traceTimer.Stop();
        var finalCheckTimer = Stopwatch.StartNew();
        if (selection.Finished?.Accepted is { } accepted)
        {
            developer.SetInformationSetsToEquilibrium(accepted.Probabilities);
            developer.CalculateBestResponse(false);
            double average = policy.Scale(developer.Status.BestResponseImprovement.ToArray(), ranges).Average();
            if (average != accepted.AverageGain || !developer.GetEquilibriumFromInformationSets().SequenceEqual(accepted.Probabilities))
                throw new InvalidDataException("Restoring the selected complete profile changed its acceptance audit.");
        }
        finalCheckTimer.Stop();
        return new(startIndex, 1_000_000+startIndex, "InexactValue (double); no exact fallback", policy.Cutoff,
            policy.GainUnits, ranges, prior, evaluated, invalid, selection.Finished, selection.Best, error)
        {
            Performance = new(traceTimer.Elapsed.TotalSeconds, checkSeconds, bestResponseSeconds, persistenceSeconds,
                traceTimer.Elapsed.TotalSeconds - checkSeconds - persistenceSeconds, finalCheckTimer.Elapsed.TotalSeconds)
        };
    }
}
