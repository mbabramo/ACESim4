using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using static ACESimBase.Games.LitigGame.ManualReports.InformationSetPressureAnalysis;
using static ACESimBase.Games.LitigGame.ManualReports.ArticlePressureAnalysis;
using static ACESimBase.Games.LitigGame.ManualReports.EquilibriumChangeDecomposition;

namespace ACESimBase.Games.LitigGame.ManualReports;

/// <summary>One row per changed information set, with an explicit, non-causal focus filter.</summary>
public static class EquilibriumChangeFocus
{
    public sealed record Row(string Key, byte Player, string Decision, double SignalValue,
        int? ExitCommitment, string[] ActionLabels, double[] OriginalPolicy, double[] TargetPolicy,
        int[] GainingActions, bool SupportsDisjoint, double ShiftedProbability,
        double? OriginalPolicyLossAtTarget, double? FixedTargetContinuationLoss, double? InferiorOriginalMass, bool Focus,
        Allocation Allocation, bool CounterfactualUndefined, bool TieSensitive,
        bool CompletionSensitive, int[] UnreachedCoalitions);

    /// <summary>
    /// Use target-opponent best-response continuation utilities. This is the loss
    /// from choosing the original local distribution and reoptimizing subsequent
    /// own decisions, relative to the best local action; it is NOT the loss of the
    /// original complete strategy. Also retain fixed-target-continuation loss.
    /// Shared support does not imply no incentive change; disjoint support does
    /// not imply strict incentives. Keep all rows, label the selected subset.
    /// </summary>
    public static Row[] Build(ContrastResult result, double utilityTolerance = 1e-6,
        double probabilityTolerance = PolicyTolerance)
    {
        if (!double.IsFinite(utilityTolerance) || utilityTolerance <= 0 ||
            !double.IsFinite(probabilityTolerance) || probabilityTolerance <= 0 || probabilityTolerance >= 1)
            throw new ArgumentException("Invalid focus tolerances.");
        var rows = new List<Row>();
        foreach (var changed in result.Changes.GroupBy(r => r.Key).Select(g => g.First()))
        {
            var old = result.SourceEquilibrium.InformationSets.Single(i => i.Key == changed.Key);
            var end = result.TargetEquilibrium.InformationSets.Single(i => i.Key == changed.Key);
            if (old.ActualOffPath || end.ActualOffPath) throw new InvalidDataException("Focus requires two reached endpoints.");
            var p = old.Actions.Select(a => a.Probability).ToArray();
            var q = end.Actions.Select(a => a.Probability).ToArray();
            var gained = Enumerable.Range(0, p.Length).Where(a => q[a] - p[a] > probabilityTolerance).ToArray();
            if (gained.Length == 0) continue;
            double mass = p.Zip(q, (a, b) => Math.Max(0, b - a)).Sum();
            bool disjoint = !p.Zip(q, (a, b) => a > probabilityTolerance && b > probabilityTolerance).Any(x => x);
            var primary = Enumerable.Range(0, 8).Select(mask => result.Scenarios.Single(s =>
                s.Panel == "coalition" && s.Component == mask.ToString() && s.Result.Player == old.Player)
                .Result.InformationSets.Single(i => i.Key == old.Key)).ToArray();
            double? LocalLoss(InformationSet info)
            {
                if (info.CounterfactuallyUnreachable || info.Actions.Any(a => !a.CounterfactualConditionalUtility.HasValue)) return null;
                double best = info.Actions.Max(a => a.CounterfactualConditionalUtility.Value);
                return Math.Max(0, info.Actions.Select((a, i) => p[i] * (best - a.CounterfactualConditionalUtility.Value)).Sum());
            }
            double? loss = LocalLoss(primary[7]), fixedLoss = LocalLoss(end), inferiorMass = null;
            if (loss.HasValue)
            {
                double best = primary[7].Actions.Max(a => a.CounterfactualConditionalUtility.Value);
                inferiorMass = primary[7].Actions.Select((a, i) => best - a.CounterfactualConditionalUtility.Value > utilityTolerance ? p[i] : 0).Sum();
            }
            double Value(InformationSet info) => 100 * gained.Sum(a => info.Actions[a].Probability);
            bool undefined = primary.Any(i => i.CounterfactuallyUnreachable);
            var values = primary.Select(Value).ToArray();
            var allocation = undefined ? null : Allocate(Value(old), Value(end), values);
            bool tieSensitive = false, completionSensitive = false;
            if (!undefined)
                foreach (var group in result.Scenarios.Where(s => s.Panel != "coalition" && s.Result.Player == old.Player).GroupBy(s => s.Panel))
                {
                    var alternativeValues = values.ToArray();
                    bool alternativeUndefined = false;
                    foreach (var scenario in group)
                    {
                        var info = scenario.Result.InformationSets.Single(i => i.Key == old.Key);
                        alternativeUndefined |= info.CounterfactuallyUnreachable;
                        alternativeValues[int.Parse(scenario.Component)] = Value(info);
                    }
                    var alternative = Allocate(Value(old), Value(end), alternativeValues);
                    double distance = new[] { allocation.Direct - alternative.Direct, allocation.Entry - alternative.Entry,
                        allocation.Offers - alternative.Offers, allocation.Exit - alternative.Exit,
                        allocation.SelectionResidual - alternative.SelectionResidual }.Select(Math.Abs).Max();
                    if (alternativeUndefined || distance > 100 * probabilityTolerance)
                    {
                        tieSensitive |= group.Key.StartsWith("tie-");
                        completionSensitive |= group.Key.StartsWith("completion-");
                    }
                }
            rows.Add(new(old.Key, old.Player, old.Decision, old.SignalValue, old.ExitCommitment,
                old.Actions.Select(a => a.Label).ToArray(), p, q, gained.Select(a => a + 1).ToArray(), disjoint,
                mass, loss, fixedLoss, inferiorMass, inferiorMass > probabilityTolerance,
                allocation, undefined, tieSensitive, completionSensitive,
                Enumerable.Range(0, 8).Where(m => primary[m].ActualOffPath).ToArray()));
        }
        return rows.ToArray();
    }
}
