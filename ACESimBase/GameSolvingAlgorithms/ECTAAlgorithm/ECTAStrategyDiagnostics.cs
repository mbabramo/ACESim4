using System;
using System.Collections.Generic;
using System.Linq;
using ACESimBase.GameSolvingSupport.ExactValues;

namespace ACESimBase.GameSolvingAlgorithms.ECTAAlgorithm;

public sealed record ECTAStrategyProjection(double[] Probabilities, double FlowResidual,
    int[] PriorCompletedInformationSets);
public sealed record ECTAIncentives(double[] Probabilities, double[] Utilities,
    double[] BestResponseUtilities, double[] UnilateralGains, double Epsilon, double NashConv,
    double[] ActualReach, double[] CounterfactualReach, double?[] ActionUtilities,
    double?[] ActionAdvantages, double?[] LocalGaps, double?[] OutsideSupportGaps,
    double?[] SupportSpreads);

/// <summary>Allocation-light evaluation of complete behavior on the original game.
/// Local Q holds both players' continuation fixed. Global BR optimizes every own
/// continuation, via backward induction on perfect-recall sequences.</summary>
public sealed class ECTAStrategyDiagnostics<T> where T : IMaybeExact<T>, new()
{
    private readonly ECTATreeDefinition<T> tree;
    private readonly double[][] utilities;
    private readonly double[] priorBehavior, priorRealization;
    private readonly int[] strategicSets, strategicMoves, moveOrder, reverseSets;
    public int[] InformationSetIndices => strategicSets.ToArray();
    public int[] MoveIndices => strategicMoves.ToArray();
    public double[] PriorProbabilities => strategicMoves.Select(m => priorBehavior[m]).ToArray();

    public ECTAStrategyDiagnostics(ECTATreeDefinition<T> tree, double[][] outcomeUtilities)
    {
        this.tree = tree;
        if (outcomeUtilities.Length != tree.outcomes.Length ||
            outcomeUtilities.Any(u => u.Length != 2 || u.Any(v => !double.IsFinite(v))))
            throw new ArgumentException("Provide original two-player terminal utilities.");
        utilities = outcomeUtilities.Select(u => u.ToArray()).ToArray();
        strategicSets = Enumerable.Range(tree.firstInformationSet[1], tree.informationSets.Length - tree.firstInformationSet[1]).ToArray();
        strategicMoves = strategicSets.SelectMany(i => Enumerable.Range(tree.informationSets[i].firstMoveIndex, tree.informationSets[i].numMoves)).ToArray();
        priorBehavior = tree.moves.Select(m => m.behavioralProbability.AsDouble).ToArray();
        var depths = new int[tree.moves.Length]; Array.Fill(depths, -1);
        foreach (int m in tree.firstMove.Take(3)) depths[m] = 0;
        int Depth(int m)
        {
            if (depths[m] >= 0) return depths[m];
            return depths[m] = 1 + Depth(tree.informationSets[tree.moves[m].priorInformationSet].sequence);
        }
        moveOrder = Enumerable.Range(0, tree.moves.Length).Where(m => tree.moves[m].priorInformationSet >= 0).OrderBy(Depth).ToArray();
        reverseSets = strategicSets.OrderByDescending(i => Depth(tree.informationSets[i].sequence)).ToArray();
        priorRealization = Realization(priorBehavior);
        ValidateProbabilities(PriorProbabilities);
    }

    private double[] Realization(double[] behavior)
    {
        var result = new double[tree.moves.Length];
        foreach (int first in tree.firstMove.Take(3)) result[first] = 1;
        foreach (int move in moveOrder)
            result[move] = result[tree.informationSets[tree.moves[move].priorInformationSet].sequence] * behavior[move];
        return result;
    }

    public ECTAStrategyProjection Project(ECTAPivotSnapshot snapshot)
    {
        if (!double.IsFinite(snapshot.Auxiliary) || snapshot.Auxiliary < -1e-8)
            throw new InvalidOperationException("Invalid auxiliary variable.");
        var weights = new double[tree.moves.Length];
        double residual = 0;
        for (int p = 1; p <= 2; p++)
        {
            int offset = p == 1 ? 0 : tree.numSequences[1] + 1 + tree.numInfoSets[2];
            for (int j = 0; j < tree.numSequences[p]; j++)
            {
                double value = snapshot.Z[offset + j] + snapshot.Auxiliary * priorRealization[tree.firstMove[p] + j];
                if (!double.IsFinite(value) || value < -1e-8) throw new InvalidOperationException("Invalid realization weight.");
                weights[tree.firstMove[p] + j] = Math.Max(0, value);
            }
            residual = Math.Max(residual, Math.Abs(weights[tree.firstMove[p]] - 1));
        }
        var probabilities = new List<double>(); var completed = new List<int>();
        foreach (int i in strategicSets)
        {
            var h = tree.informationSets[i];
            double sum = Enumerable.Range(h.firstMoveIndex, h.numMoves).Sum(m => weights[m]);
            residual = Math.Max(residual, Math.Abs(sum - weights[h.sequence]));
            if (sum == 0) completed.Add(i);
            for (int a = 0; a < h.numMoves; a++)
                probabilities.Add(sum == 0 ? priorBehavior[h.firstMoveIndex + a] : weights[h.firstMoveIndex + a] / sum);
        }
        // x + z0*prior starts at the prior and ends at the LCP realization plan.
        // Local normalization also produces a valid behavioral pair when intermediate
        // flow constraints have slack. FlowResidual makes that projection explicit.
        return new(probabilities.ToArray(), residual, completed.ToArray());
    }

    private void ValidateProbabilities(double[] probabilities)
    {
        if (probabilities.Length != strategicMoves.Length || probabilities.Any(p => !double.IsFinite(p) || p < 0 || p > 1))
            throw new ArgumentException("Invalid strategy vector.");
        int offset = 0;
        foreach (int i in strategicSets)
        {
            int count = tree.informationSets[i].numMoves;
            if (Math.Abs(probabilities.Skip(offset).Take(count).Sum() - 1) > 1e-9)
                throw new ArgumentException("Probabilities must sum to one at each information set.");
            offset += count;
        }
    }

    public ECTAIncentives Evaluate(double[] probabilities, double supportTolerance = 1e-10)
    {
        ValidateProbabilities(probabilities);
        if (!double.IsFinite(supportTolerance) || supportTolerance < 0) throw new ArgumentOutOfRangeException(nameof(supportTolerance));
        var behavior = priorBehavior.ToArray();
        for (int a = 0; a < strategicMoves.Length; a++) behavior[strategicMoves[a]] = probabilities[a];
        var r = Realization(behavior);
        var nodeValue = new[] { new double[tree.nodes.Length], new double[tree.nodes.Length] };
        var g = new[] { new double[tree.moves.Length], new double[tree.moves.Length] };
        var q = new double[tree.moves.Length];
        var cf = new double[tree.informationSets.Length]; var reach = new double[cf.Length];
        for (int n = tree.nodes.Length - 1; n >= 1; n--)
        {
            var node = tree.nodes[n];
            if (node.terminal)
                for (int p = 0; p < 2; p++)
                {
                    nodeValue[p][n] = utilities[node.outcome][p];
                    double weight = r[node.sequenceForPlayer[0]] * r[node.sequenceForPlayer[2 - p]];
                    g[p][node.sequenceForPlayer[p + 1]] += weight * utilities[node.outcome][p];
                }
            else if (tree.informationSets[node.iset].playerIndex != 0)
            {
                int p = tree.informationSets[node.iset].playerIndex;
                double weight = r[node.sequenceForPlayer[0]] * r[node.sequenceForPlayer[3 - p]];
                cf[node.iset] += weight; reach[node.iset] += weight * r[node.sequenceForPlayer[p]];
            }
            if (n == 1) continue;
            var parent = tree.nodes[node.father];
            int owner = tree.informationSets[parent.iset].playerIndex;
            if (owner != 0)
                q[node.moveAtFather] += r[parent.sequenceForPlayer[0]] * r[parent.sequenceForPlayer[3 - owner]] * nodeValue[owner - 1][n];
            for (int p = 0; p < 2; p++) nodeValue[p][node.father] += behavior[node.moveAtFather] * nodeValue[p][n];
        }
        foreach (int i in reverseSets)
        {
            var h = tree.informationSets[i]; var values = g[h.playerIndex - 1];
            values[h.sequence] += Enumerable.Range(h.firstMoveIndex, h.numMoves).Max(m => values[m]);
        }
        var u = new[] { nodeValue[0][1], nodeValue[1][1] };
        var br = new[] { g[0][tree.firstMove[1]], g[1][tree.firstMove[2]] };
        var gain = br.Zip(u, (b, v) => Math.Max(0, b - v)).ToArray();
        var actionValues = new List<double?>(); var advantages = new List<double?>();
        var gaps = new List<double?>(); var outside = new List<double?>(); var spreads = new List<double?>();
        foreach (int i in strategicSets)
        {
            var h = tree.informationSets[i];
            if (cf[i] <= 1e-15)
            {
                gaps.Add(null); outside.Add(null); spreads.Add(null);
                for (int a = 0; a < h.numMoves; a++) { actionValues.Add(null); advantages.Add(null); }
                continue;
            }
            var values = Enumerable.Range(h.firstMoveIndex, h.numMoves).Select(m => q[m] / cf[i]).ToArray();
            double current = Enumerable.Range(0, h.numMoves).Sum(a => behavior[h.firstMoveIndex + a] * values[a]);
            double outsideGain = 0;
            var supportValues = new List<double>();
            for (int a = 0; a < h.numMoves; a++)
            {
                actionValues.Add(values[a]); advantages.Add(values[a] - current);
                if (behavior[h.firstMoveIndex + a] <= supportTolerance) outsideGain = Math.Max(outsideGain, values[a] - current);
                else supportValues.Add(values[a]);
            }
            gaps.Add(Math.Max(0, values.Max() - current)); outside.Add(outsideGain);
            spreads.Add(supportValues.Count == 0 ? 0 : supportValues.Max() - supportValues.Min());
        }
        return new(probabilities.ToArray(), u, br, gain, gain.Max(), gain.Sum(),
            strategicSets.Select(i => reach[i]).ToArray(), strategicSets.Select(i => cf[i]).ToArray(),
            actionValues.ToArray(), advantages.ToArray(), gaps.ToArray(), outside.ToArray(), spreads.ToArray());
    }
}
