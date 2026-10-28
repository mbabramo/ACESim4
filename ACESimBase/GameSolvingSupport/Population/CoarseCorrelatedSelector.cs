using System;
using System.Collections.Generic;
using System.Linq;

namespace ACESimBase.GameSolvingSupport.Population;

public enum JointSelectionObjective { MaximumGini, RelativeEntropy }
public sealed record JointSelectionSettings(double IncentiveTolerance = 1e-8, double GradientTolerance = 1e-10,
    int MaximumIterations = 20000);
public sealed record JointSelectionResult(double[] Weights, double[] RestrictedGains, double Objective,
    double? DualityGap, double? ProjectedGradient, int Termination, int Iterations, string Algorithm);

/// <summary>Normal-form CCE over complete policies. Incentives always use actual utilities;
/// similarity affects only the selection objective. The dual has one variable per deviation,
/// avoiding a quadratic matrix with one row/column per joint profile.</summary>
public static class CoarseCorrelatedSelector
{
    public static JointSelectionResult Select(double[][] utilities, int rows, int columns, double[] scales,
        JointSelectionObjective objective, double[] reference = null, JointSelectionSettings settings = null)
    {
        settings ??= new(); int n = checked(rows * columns), m = rows + columns;
        if (rows < 1 || columns < 1 || utilities.Length != 2 || utilities.Any(u => u.Length != n || u.Any(x => !double.IsFinite(x))) ||
            scales.Length != 2 || scales.Any(s => !double.IsFinite(s) || s <= 0) ||
            !double.IsFinite(settings.IncentiveTolerance) || settings.IncentiveTolerance < 0 ||
            !double.IsFinite(settings.GradientTolerance) || settings.GradientTolerance <= 0 || settings.MaximumIterations < 1 || !Enum.IsDefined(objective))
            throw new ArgumentException("Invalid restricted game or selector settings.");
        reference ??= Enumerable.Repeat(1.0 / n, n).ToArray();
        if (reference.Length != n || reference.Any(x => !double.IsFinite(x) || x <= 0) || Math.Abs(reference.Sum() - 1) > 1e-10)
            throw new ArgumentException("Relative entropy requires an explicitly positive reference distribution.");
        var constraints = new double[m][];
        for (int d = 0; d < m; d++)
        {
            constraints[d] = new double[n];
            for (int i = 0; i < rows; i++) for (int j = 0; j < columns; j++)
                constraints[d][i * columns + j] = d < rows
                    ? (utilities[0][d * columns + j] - utilities[0][i * columns + j]) / scales[0]
                    : (utilities[1][i * columns + d - rows] - utilities[1][i * columns + j]) / scales[1];
        }
        if (objective == JointSelectionObjective.MaximumGini && n <= 2048)
            return Quadratic(constraints, n, settings);
        var logReference = reference.Select(x => Math.Log(x)).ToArray();
        double[] Probability(double[] lambda, out double normalizer)
        {
            var logits = new double[n];
            for (int k = 0; k < n; k++)
            {
                logits[k] = objective == JointSelectionObjective.RelativeEntropy ? logReference[k] : 0;
                for (int d = 0; d < m; d++) logits[k] -= lambda[d] * constraints[d][k];
            }
            if (objective == JointSelectionObjective.MaximumGini) { normalizer = 0; return SimplexProjection(logits); }
            double max = logits.Max(), sum = 0;
            for (int k = 0; k < n; k++) { logits[k] = Math.Exp(logits[k] - max); sum += logits[k]; }
            for (int k = 0; k < n; k++) logits[k] /= sum;
            normalizer = max + Math.Log(sum); return logits;
        }
        void Function(double[] lambda, ref double value, double[] gradient, object context)
        {
            var q = Probability(lambda, out double logZ);
            value = objective == JointSelectionObjective.RelativeEntropy ? logZ : -.5 * q.Sum(x => x * x);
            for (int d = 0; d < m; d++)
            {
                double gain = 0; for (int k = 0; k < n; k++) gain += constraints[d][k] * q[k];
                gradient[d] = settings.IncentiveTolerance - gain;
                value += lambda[d] * (objective == JointSelectionObjective.RelativeEntropy ? settings.IncentiveTolerance : gradient[d]);
            }
        }
        alglib.minbleiccreate(new double[m], out var state);
        alglib.minbleicsetbc(state, new double[m], Enumerable.Repeat(double.PositiveInfinity, m).ToArray());
        alglib.minbleicsetcond(state, settings.GradientTolerance, 0, 1e-12, settings.MaximumIterations);
        alglib.minbleicoptimize(state, Function, null, null);
        alglib.minbleicresults(state, out var multipliers, out var report);
        var weights = Probability(multipliers, out _);
        var gains = constraints.Select(a => a.Zip(weights, (x, y) => x * y).Sum()).ToArray();
        double gap = gains.Select((g, d) => multipliers[d] * (settings.IncentiveTolerance - g)).Sum();
        double projected = gains.Select((g, d) => multipliers[d] > 0 ? Math.Abs(settings.IncentiveTolerance - g) : Math.Max(0, g - settings.IncentiveTolerance)).Max();
        double primal = objective == JointSelectionObjective.MaximumGini ? .5 * weights.Sum(x => x * x)
            : weights.Select((q, k) => q == 0 ? 0 : q * (Math.Log(q) - logReference[k])).Sum();
        if (weights.Any(x => !double.IsFinite(x) || x < 0) || Math.Abs(weights.Sum() - 1) > 1e-10)
            throw new ArithmeticException("Selector produced invalid weights.");
        return new(weights, gains, primal, gap, projected, report.terminationtype, report.iterationscount, "bounded-convex-dual");
    }

    private static JointSelectionResult Quadratic(double[][] constraints, int n, JointSelectionSettings settings)
    {
        if (n == 1) return new([1], constraints.Select(a => a[0]).ToArray(), .5, 0, 0, 4, 0, "singleton");
        alglib.minqpcreate(n, out var state);
        var quadratic = new double[n, n]; for (int i = 0; i < n; i++) quadratic[i, i] = 1;
        alglib.minqpsetquadraticterm(state, quadratic); alglib.minqpsetlinearterm(state, new double[n]);
        alglib.minqpsetbc(state, new double[n], Enumerable.Repeat(1.0, n).ToArray());
        alglib.minqpsetscale(state, Enumerable.Repeat(1.0, n).ToArray());
        alglib.minqpsetstartingpoint(state, Enumerable.Repeat(1.0 / n, n).ToArray());
        var matrix = new double[constraints.Length + 1, n + 1]; var types = new int[constraints.Length + 1];
        for (int k = 0; k <= n; k++) matrix[0, k] = 1;
        for (int d = 0; d < constraints.Length; d++)
        {
            for (int k = 0; k < n; k++) matrix[d + 1, k] = constraints[d][k];
            matrix[d + 1, n] = settings.IncentiveTolerance; types[d + 1] = -1;
        }
        alglib.minqpsetlc(state, matrix, types);
        alglib.minqpsetalgobleic(state, settings.GradientTolerance, 0, 1e-12, settings.MaximumIterations);
        alglib.minqpoptimize(state); alglib.minqpresults(state, out var weights, out var report);
        if (report.terminationtype <= 0 || weights.Any(x => !double.IsFinite(x) || x < -1e-10))
            throw new ArithmeticException("Restricted Gini QP failed: " + report.terminationtype);
        weights = weights.Select(x => Math.Max(0, x)).ToArray(); double mass = weights.Sum();
        for (int k = 0; k < n; k++) weights[k] /= mass;
        var gains = constraints.Select(a => a.Zip(weights, (x, y) => x * y).Sum()).ToArray();
        // This API does not expose QP multipliers. Do not invent a duality certificate.
        return new(weights, gains, .5 * weights.Sum(x => x * x), null, null, report.terminationtype, report.inneriterationscount, "primal-quadratic");
    }

    public static double[] SimplexProjection(double[] values)
    {
        var sorted = values.OrderByDescending(x => x).ToArray(); double sum = 0, threshold = 0;
        for (int i = 0; i < sorted.Length; i++)
        {
            sum += sorted[i]; double candidate = (sum - 1) / (i + 1);
            if (sorted[i] > candidate) threshold = candidate;
        }
        var result = values.Select(x => Math.Max(0, x - threshold)).ToArray();
        double mass = result.Sum(); for (int i = 0; i < result.Length; i++) result[i] /= mass;
        return result;
    }
}
