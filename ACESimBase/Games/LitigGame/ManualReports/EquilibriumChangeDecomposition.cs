using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using static ACESimBase.Games.LitigGame.ManualReports.InformationSetPressureAnalysis;
using static ACESimBase.Games.LitigGame.ManualReports.ArticlePressureAnalysis;

namespace ACESimBase.Games.LitigGame.ManualReports;

/// <summary>
/// Direct-first accounting of changed, commonly reached equilibrium decisions.
/// Opponent increments are averaged over all six orders (three-factor Shapley
/// allocation conditional on the new rules). This is not a causal identification
/// claim and does not silently allocate equilibrium-selection residuals.
/// </summary>
public static class EquilibriumChangeDecomposition
{
    public const double PolicyTolerance = 1e-6;
    public static readonly string[] Components = { "participation", "offers", "exit" };
    public static readonly int[][] Orders =
    {
        new[] { 0, 1, 2 }, new[] { 0, 2, 1 }, new[] { 1, 0, 2 },
        new[] { 1, 2, 0 }, new[] { 2, 0, 1 }, new[] { 2, 1, 0 }
    };
    public sealed record Allocation(double Original, double Target, double Direct,
        double Entry, double Offers, double Exit, double SelectionResidual)
    {
        public double Change => Target - Original;
        public double Explained => Direct + Entry + Offers + Exit;
    }
    public sealed record ChangeRow(string Key, byte Player, string Decision, int Signal,
        double SignalValue, int? ExitCommitment, string Metric, int? Action,
        string[] ActionLabels, double[] OriginalPolicy, double[] TargetPolicy,
        Allocation Allocation, bool EndpointSelection, bool TieSensitive, bool CompletionSensitive,
        bool CounterfactualUndefined, double MaximumSensitivity, int[] UnreachedCoalitions);
    public sealed record ExcludedHistory(string Key, byte Player, string Decision,
        double SignalValue, int? ExitCommitment, string Reason);
    public sealed record PayoffGapRow(string Key, byte Player, string Decision, double SignalValue,
        int? ExitCommitment, string[] ActionLabels, double[] GainingWeights, double[] LosingWeights,
        double ShiftedProbability, double? OriginalGap, double? TargetGap, double?[] CoalitionGaps,
        Allocation Allocation, bool CounterfactualUndefined, bool TieSensitive, bool CompletionSensitive,
        double MaximumSensitivity, int[] UnreachedCoalitions);

    /// <summary>
    /// Conditional Q of actions gaining probability minus Q of actions losing it.
    /// Normalize each side by the probability mass actually moved. This compares
    /// payoffs, never mean offer amounts, and needs no new solve or arbitrary tie break.
    /// Original/target Q use their actual continuation policies; coalition Q use
    /// optimized continuations. Thus Direct includes reoptimization, and any final
    /// continuation mismatch remains explicit in Allocation.SelectionResidual.
    /// </summary>
    public static PayoffGapRow[] BuildPayoffGaps(Reference original, Reference target,
        Scenario[] scenarios, ChangeRow[] changes, double tolerance = 1e-7)
    {
        var rows = new List<PayoffGapRow>();
        foreach (var changed in changes.Where(r => r.EndpointSelection || r.CounterfactualUndefined)
            .GroupBy(r => r.Key).Select(g => g.First()))
        {
            var old = original.InformationSets.Single(s => s.Key == changed.Key);
            var end = target.InformationSets.Single(s => s.Key == changed.Key);
            var primary = Enumerable.Range(0, 8).Select(mask => scenarios.Single(s =>
                s.Panel == "coalition" && s.Component == mask.ToString() && s.Result.Player == old.Player)
                .Result.InformationSets.Single(s => s.Key == old.Key)).ToArray();
            var gaining = old.Actions.Zip(end.Actions, (a, b) => Math.Max(0, b.Probability - a.Probability)).ToArray();
            var losing = old.Actions.Zip(end.Actions, (a, b) => Math.Max(0, a.Probability - b.Probability)).ToArray();
            double mass = gaining.Sum();
            Near(mass, losing.Sum(), 1e-9, "Equal gained/lost probability mass");
            if (mass <= PolicyTolerance) continue;
            gaining = gaining.Select(p => p / mass).ToArray();
            losing = losing.Select(p => p / mass).ToArray();
            double? Gap(InformationSet info)
            {
                if (!info.Actions.Select(a => a.Label).SequenceEqual(old.Actions.Select(a => a.Label)))
                    throw new InvalidDataException("Mismatched payoff-gap action menus.");
                if (info.CounterfactuallyUnreachable) return null;
                double gap = 0;
                for (int a = 0; a < gaining.Length; a++)
                {
                    double weight = gaining[a] - losing[a];
                    if (weight == 0) continue;
                    double? q = info.Actions[a].CounterfactualConditionalUtility;
                    if (!q.HasValue || !double.IsFinite(q.Value)) return null;
                    gap += weight * q.Value;
                }
                return gap;
            }
            double? start = Gap(old), finish = Gap(end);
            var gaps = primary.Select(Gap).ToArray();
            bool undefined = !start.HasValue || !finish.HasValue || gaps.Any(g => !g.HasValue);
            Allocation allocation = undefined ? null : Allocate(start.Value, finish.Value, gaps.Select(g => g.Value).ToArray());
            bool tieSensitive = false, completionSensitive = false;
            double maximumSensitivity = 0;
            if (!undefined)
                foreach (var group in scenarios.Where(s => s.Panel != "coalition" && s.Result.Player == old.Player).GroupBy(s => s.Panel))
                {
                    var candidate = gaps.ToArray();
                    foreach (var s in group)
                        candidate[int.Parse(s.Component)] = Gap(s.Result.InformationSets.Single(i => i.Key == old.Key));
                    double distance;
                    if (candidate.Any(g => !g.HasValue)) distance = double.PositiveInfinity;
                    else
                    {
                        var alternative = Allocate(start.Value, finish.Value, candidate.Select(g => g.Value).ToArray());
                        distance = new[] { allocation.Direct - alternative.Direct, allocation.Entry - alternative.Entry,
                            allocation.Offers - alternative.Offers, allocation.Exit - alternative.Exit,
                            allocation.SelectionResidual - alternative.SelectionResidual }.Select(Math.Abs).Max();
                    }
                    // A disappearing conditional value is sensitive, but Infinity
                    // must not be serialized into the numeric diagnostics.
                    maximumSensitivity = Math.Max(maximumSensitivity, double.IsFinite(distance) ? distance : 0);
                    if (distance > tolerance)
                    {
                        if (group.Key.StartsWith("tie-")) tieSensitive = true;
                        if (group.Key.StartsWith("completion-")) completionSensitive = true;
                    }
                }
            rows.Add(new(old.Key, old.Player, old.Decision, old.SignalValue, old.ExitCommitment,
                old.Actions.Select(a => a.Label).ToArray(), gaining, losing, mass, start, finish, gaps,
                allocation, undefined, tieSensitive, completionSensitive, maximumSensitivity,
                Enumerable.Range(0, 8).Where(m => primary[m].ActualOffPath).ToArray()));
        }
        return rows.ToArray();
    }

    public static Profile Coalition(Profile source, Profile target, byte player, int mask)
    {
        if (mask < 0 || mask > 7) throw new ArgumentOutOfRangeException(nameof(mask));
        var result = source;
        for (int component = 0; component < 3; component++)
            if ((mask & (1 << component)) != 0)
                result = Hybrid(result, target, (byte)(1 - player), Components[component], "coalition-" + mask);
        return result;
    }

    public static Allocation Allocate(double original, double target, double[] coalitionValues)
    {
        if (coalitionValues.Length != 8 || coalitionValues.Any(x => !double.IsFinite(x)) ||
            !double.IsFinite(original) || !double.IsFinite(target))
            throw new ArgumentException("Exactly eight finite coalition values are required.");
        var effects = new double[3];
        foreach (var order in Orders)
        {
            int mask = 0;
            foreach (int component in order)
            {
                int next = mask | (1 << component);
                effects[component] += (coalitionValues[next] - coalitionValues[mask]) / Orders.Length;
                mask = next;
            }
        }
        var result = new Allocation(original, target, coalitionValues[0] - original,
            effects[0], effects[1], effects[2], target - coalitionValues[7]);
        Near(target - original, result.Explained + result.SelectionResidual, 1e-9, "Additive change accounting");
        return result;
    }

    public static (ChangeRow[] Rows, ExcludedHistory[] Excluded) BuildRows(
        Reference original, Reference target, Scenario[] scenarios, double[] offerValues)
    {
        var rows = new List<ChangeRow>();
        var excluded = new List<ExcludedHistory>();
        foreach (var old in original.InformationSets)
        {
            var end = target.InformationSets.Single(x => x.Key == old.Key);
            if (!old.Actions.Select(a => a.Label).SequenceEqual(end.Actions.Select(a => a.Label)))
                throw new InvalidDataException("Mismatched action menus.");
            if (old.ActualOffPath || end.ActualOffPath)
            {
                if (old.ActualOffPath != end.ActualOffPath)
                    excluded.Add(new(old.Key, old.Player, old.Decision, old.SignalValue, old.ExitCommitment,
                        old.ActualOffPath ? "newly reached; no comparable original action" : "no longer reached; no comparable target action"));
                continue;
            }
            double[] startPolicy = old.Actions.Select(a => a.Probability).ToArray();
            double[] endPolicy = end.Actions.Select(a => a.Probability).ToArray();
            if (startPolicy.Zip(endPolicy, (a, b) => Math.Abs(a - b)).Max() <= PolicyTolerance) continue;
            var primary = Enumerable.Range(0, 8).Select(mask => scenarios.Single(s =>
                s.Panel == "coalition" && s.Component == mask.ToString() && s.Result.Player == old.Player)
                .Result.InformationSets.Single(i => i.Key == old.Key)).ToArray();
            var alternatives = scenarios.Where(s => s.Panel != "coalition" && s.Result.Player == old.Player).ToArray();
            bool offer = old.Decision is "P Offer" or "D Offer";
            bool Pure(InformationSet i) => i.Actions.Count(a => a.Probability > PolicyTolerance) == 1;
            // Never summarize mixed offers by their mean. Pure offers can use their
            // monetary grid coordinate; mixed endpoints/hybrids use action shares.
            bool amount = offer && new[] { old, end }.Concat(primary).All(Pure) &&
                alternatives.All(s => Pure(s.Result.InformationSets.Single(i => i.Key == old.Key)));
            int[] actions = !offer || amount ? new[] { -1 } :
                Enumerable.Range(0, old.Actions.Length).Where(a =>
                    Math.Abs(startPolicy[a] - endPolicy[a]) > PolicyTolerance).ToArray();
            foreach (int action in actions)
            {
                double Value(InformationSet i) => amount
                    ? offerValues[Array.FindIndex(i.Actions, a => a.Probability > PolicyTolerance)]
                    : action >= 0 ? 100 * i.Actions[action].Probability
                    : 100 * i.Actions.Single(a => a.Label == "Yes").Probability;
                var coalitionValues = primary.Select(Value).ToArray();
                var allocation = Allocate(Value(old), Value(end), coalitionValues);
                double maximumSensitivity = 0;
                bool tieSensitive = false, completionSensitive = false;
                // Compare whole allocations for complete tie variants; for one-at-a-time
                // donor completion stresses replace just that coalition before allocating.
                foreach (var group in alternatives.GroupBy(s => s.Panel))
                {
                    var candidate = coalitionValues.ToArray();
                    foreach (var s in group)
                        candidate[int.Parse(s.Component)] = Value(s.Result.InformationSets.Single(i => i.Key == old.Key));
                    var alternative = Allocate(Value(old), Value(end), candidate);
                    double distance = new[] { allocation.Direct - alternative.Direct, allocation.Entry - alternative.Entry,
                        allocation.Offers - alternative.Offers, allocation.Exit - alternative.Exit,
                        allocation.SelectionResidual - alternative.SelectionResidual }.Select(Math.Abs).Max();
                    maximumSensitivity = Math.Max(maximumSensitivity, distance);
                    if (distance > PolicyTolerance)
                    {
                        if (group.Key.StartsWith("tie-")) tieSensitive = true;
                        if (group.Key.StartsWith("completion-")) completionSensitive = true;
                    }
                }
                rows.Add(new(old.Key, old.Player, old.Decision, old.Signal, old.SignalValue, old.ExitCommitment,
                    amount ? "offer amount" : action >= 0 ? "offer action probability" : "decision probability",
                    action >= 0 ? action + 1 : null, old.Actions.Select(a => a.Label).ToArray(),
                    startPolicy, endPolicy, allocation, Math.Abs(allocation.SelectionResidual) > PolicyTolerance,
                    tieSensitive, completionSensitive, primary.Any(i => i.CounterfactuallyUnreachable),
                    maximumSensitivity, Enumerable.Range(0, 8).Where(m => primary[m].ActualOffPath).ToArray()));
            }
        }
        return (rows.ToArray(), excluded.ToArray());
    }
}
