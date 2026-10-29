using System;
using System.Collections.Generic;
using System.Linq;

namespace ACESimBase.GameSolvingSupport.Population;

public enum JointSelectionObjective { MaximumGini, RelativeEntropy }
public sealed record JointSelectionSettings(double IncentiveTolerance = 1e-8, double GradientTolerance = 1e-10,
    int MaximumIterations = 20000, bool ScaleQuadraticIncentives = false);
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
        if (objective == JointSelectionObjective.MaximumGini && n <= 2048)
        {
            var constraints = new double[m][];
            for (int d = 0; d < m; d++)
            {
                constraints[d] = new double[n];
                for (int i = 0; i < rows; i++) for (int j = 0; j < columns; j++)
                    constraints[d][i * columns + j] = d < rows
                        ? (utilities[0][d * columns + j] - utilities[0][i * columns + j]) / scales[0]
                        : (utilities[1][i * columns + d - rows] - utilities[1][i * columns + j]) / scales[1];
            }
            return Quadratic(constraints, n, settings);
        }
        // A CCE deviation replaces one player's complete policy. Its matrix products
        // factor through marginals, so neither A nor its (rows+columns)*rows*columns
        // entries are needed by the dual. All products below take O(rows*columns).
        // Center first to avoid subtracting large wealth/utility offsets repeatedly.
        var payoffs = utilities.Select((u, p) => { double minimum = u.Min(); return u.Select(x => (x - minimum) / scales[p]).ToArray(); }).ToArray();
        double[] Gains(double[] q)
        {
            var rowMass = new double[rows]; var columnMass = new double[columns];
            double expected0 = 0, expected1 = 0;
            for (int i = 0, k = 0; i < rows; i++) for (int j = 0; j < columns; j++, k++)
            {
                rowMass[i] += q[k]; columnMass[j] += q[k];
                expected0 += q[k] * payoffs[0][k]; expected1 += q[k] * payoffs[1][k];
            }
            var result = new double[m];
            for (int i = 0, k = 0; i < rows; i++) for (int j = 0; j < columns; j++, k++)
            { result[i] += columnMass[j] * payoffs[0][k]; result[rows + j] += rowMass[i] * payoffs[1][k]; }
            for (int d = 0; d < m; d++) result[d] -= d < rows ? expected0 : expected1;
            return result;
        }
        var logReference = reference.Select(x => Math.Log(x)).ToArray();
        double[] Probability(double[] lambda, out double normalizer)
        {
            var columnPenalty = new double[columns]; var rowPenalty = new double[rows];
            double sum0 = 0, sum1 = 0;
            for (int i = 0; i < rows; i++) sum0 += lambda[i];
            for (int j = 0; j < columns; j++) sum1 += lambda[rows + j];
            for (int i = 0, k = 0; i < rows; i++) for (int j = 0; j < columns; j++, k++)
            { columnPenalty[j] += lambda[i] * payoffs[0][k]; rowPenalty[i] += lambda[rows + j] * payoffs[1][k]; }
            var logits = new double[n];
            for (int i = 0, k = 0; i < rows; i++) for (int j = 0; j < columns; j++, k++)
                logits[k] = (objective == JointSelectionObjective.RelativeEntropy ? logReference[k] : 0)
                    + (sum0 * payoffs[0][k] - columnPenalty[j]) + (sum1 * payoffs[1][k] - rowPenalty[i]);
            if (objective == JointSelectionObjective.MaximumGini) { normalizer = 0; return SimplexProjection(logits); }
            double max = logits.Max(), sum = 0;
            for (int k = 0; k < n; k++) { logits[k] = Math.Exp(logits[k] - max); sum += logits[k]; }
            for (int k = 0; k < n; k++) logits[k] /= sum;
            normalizer = max + Math.Log(sum); return logits;
        }
        void Function(double[] lambda, ref double value, double[] gradient, object context)
        {
            var q = Probability(lambda, out double logZ);
            var gains = Gains(q);
            value = objective == JointSelectionObjective.RelativeEntropy ? logZ : -.5 * q.Sum(x => x * x);
            for (int d = 0; d < m; d++)
            {
                gradient[d] = settings.IncentiveTolerance - gains[d];
                value += lambda[d] * (objective == JointSelectionObjective.RelativeEntropy ? settings.IncentiveTolerance : gradient[d]);
            }
        }
        alglib.minbleiccreate(new double[m], out var state);
        alglib.minbleicsetbc(state, new double[m], Enumerable.Repeat(double.PositiveInfinity, m).ToArray());
        alglib.minbleicsetcond(state, settings.GradientTolerance, 0, 1e-12, settings.MaximumIterations);
        alglib.minbleicoptimize(state, Function, null, null);
        alglib.minbleicresults(state, out var multipliers, out var report);
        var weights = Probability(multipliers, out _);
        var gains = Gains(weights);
        double gap = gains.Select((g, d) => multipliers[d] * (settings.IncentiveTolerance - g)).Sum();
        double projected = gains.Select((g, d) => multipliers[d] > 0 ? Math.Abs(settings.IncentiveTolerance - g) : Math.Max(0, g - settings.IncentiveTolerance)).Max();
        double primal = objective == JointSelectionObjective.MaximumGini ? .5 * weights.Sum(x => x * x)
            : weights.Select((q, k) => q == 0 ? 0 : q * (Math.Log(q) - logReference[k])).Sum();
        if (weights.Any(x => !double.IsFinite(x) || x < 0) || Math.Abs(weights.Sum() - 1) > 1e-10)
            throw new ArithmeticException("Selector produced invalid weights.");
        return new(weights, gains, primal, gap, projected, report.terminationtype, report.iterationscount, "factored-bounded-convex-dual");
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
            // Preserve each inequality exactly while avoiding almost-zero rows in
            // rare-event games. Report residuals in the original utility units.
            double scale = settings.ScaleQuadraticIncentives ? Math.Max(settings.IncentiveTolerance, constraints[d].Max(Math.Abs)) : 1;
            if (scale == 0) scale = 1;
            for (int k = 0; k < n; k++) matrix[d + 1, k] = constraints[d][k] / scale;
            matrix[d + 1, n] = settings.IncentiveTolerance / scale; types[d + 1] = -1;
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
        return new(weights, gains, .5 * weights.Sum(x => x * x), null, null, report.terminationtype, report.inneriterationscount,
            settings.ScaleQuadraticIncentives ? "scaled-primal-quadratic" : "primal-quadratic");
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
