using System;
using System.Linq;

namespace ACESimBase.GameSolvingSupport.Population;

public sealed record AffinitySettings(double Bandwidth = .1, double ReferenceFloorMass = 1e-6);
/// <summary>Weights include the declared floor; Entropy and KktResidual describe the
/// unfloored affinity optimum. Maximize alone returns zero floor and bandwidth.</summary>
public sealed record AffinityResult(double[] Weights, double Entropy, double KktResidual, int Termination,
    double FloorMass, double Bandwidth);

/// <summary>Liu et al. (2025), equations 5–7 with p=1: maximize 1-||K diag(1/||K_j||_2)x||².
/// Inputs are explicitly scaled economic signatures against a fixed probe bank. That metric
/// is an adaptation; it is not the paper's own-payoff-only experimental metric.</summary>
public static class AffinityReference
{
    public static AffinityResult FromSignatures(double[][] signatures, AffinitySettings settings = null)
    {
        settings ??= new();
        if (signatures.Length == 0 || signatures[0].Length == 0 || signatures.Any(s => s.Length != signatures[0].Length || s.Any(x => !double.IsFinite(x))) ||
            !double.IsFinite(settings.Bandwidth) || settings.Bandwidth <= 0 || !double.IsFinite(settings.ReferenceFloorMass) || settings.ReferenceFloorMass <= 0 || settings.ReferenceFloorMass >= 1)
            throw new ArgumentException("Invalid affinity signatures/settings.");
        int n = signatures.Length; var kernel = new double[n, n];
        for (int i = 0; i < n; i++) for (int j = 0; j < n; j++)
        {
            double distance = signatures[i].Zip(signatures[j], (a, b) => (a - b) * (a - b)).Sum();
            kernel[i, j] = Math.Exp(-distance / (4 * settings.Bandwidth * settings.Bandwidth));
        }
        var raw = Maximize(kernel);
        // Give every policy positive reference support. Split the floor equally between
        // exact signature classes, then within each class. Policies are never removed.
        var groups = Enumerable.Range(0, n).GroupBy(i => string.Join(",", signatures[i].Select(x => BitConverter.DoubleToInt64Bits(x == 0 ? 0 : x)))).ToArray();
        var weights = raw.Weights.Select(x => (1 - settings.ReferenceFloorMass) * x).ToArray();
        foreach (var group in groups) foreach (int i in group) weights[i] += settings.ReferenceFloorMass / groups.Length / group.Count();
        return raw with { Weights = weights, FloorMass = settings.ReferenceFloorMass, Bandwidth = settings.Bandwidth };
    }

    public static AffinityResult Maximize(double[,] kernel)
    {
        int n = kernel.GetLength(0);
        if (n == 0 || kernel.GetLength(1) != n || kernel.Cast<double>().Any(x => !double.IsFinite(x) || x < 0 || x > 1)) throw new ArgumentException("Invalid affinity kernel.");
        if (n == 1) return new([1], 0, 0, 4, 0, 0);
        var u = new double[n, n];
        for (int j = 0; j < n; j++)
        {
            double norm = Math.Sqrt(Enumerable.Range(0, n).Sum(i => kernel[i, j] * kernel[i, j]));
            if (norm == 0) throw new ArgumentException("Zero affinity column.");
            for (int i = 0; i < n; i++) u[i, j] = kernel[i, j] / norm;
        }
        var quadratic = new double[n, n];
        for (int i = 0; i < n; i++) for (int j = 0; j < n; j++)
            for (int k = 0; k < n; k++) quadratic[i, j] += 2 * u[k, i] * u[k, j];
        alglib.minqpcreate(n, out var state);
        alglib.minqpsetquadraticterm(state, quadratic);
        alglib.minqpsetlinearterm(state, new double[n]);
        alglib.minqpsetbc(state, new double[n], Enumerable.Repeat(1.0, n).ToArray());
        alglib.minqpsetscale(state, Enumerable.Repeat(1.0, n).ToArray());
        alglib.minqpsetstartingpoint(state, Enumerable.Repeat(1.0 / n, n).ToArray());
        var equality = new double[1, n + 1]; for (int i = 0; i <= n; i++) equality[0, i] = 1;
        alglib.minqpsetlc(state, equality, [0]);
        alglib.minqpsetalgobleic(state, 1e-11, 0, 1e-12, 20000);
        alglib.minqpoptimize(state); alglib.minqpresults(state, out var weights, out var report);
        if (report.terminationtype <= 0 || weights.Any(x => !double.IsFinite(x) || x < -1e-10)) throw new ArithmeticException("Affinity QP failed: " + report.terminationtype);
        weights = weights.Select(x => Math.Max(0, x)).ToArray(); double mass = weights.Sum();
        for (int i = 0; i < n; i++) weights[i] /= mass;
        var gradient = Enumerable.Range(0, n).Select(i => Enumerable.Range(0, n).Sum(j => quadratic[i, j] * weights[j])).ToArray();
        double dot = gradient.Zip(weights, (a, b) => a * b).Sum();
        double kkt = gradient.Select((g, i) => weights[i] > 1e-8 ? Math.Abs(g - dot) : Math.Max(0, dot - g)).Max();
        return new(weights, 1 - dot / 2, kkt, report.terminationtype, 0, 0);
    }
}
