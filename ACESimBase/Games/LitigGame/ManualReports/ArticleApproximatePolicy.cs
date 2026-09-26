using ACESimBase.Util.Mathematics;
using System;
using System.IO;
using System.Linq;

namespace ACESimBase.Games.LitigGame.ManualReports;

public enum ArticleApproximateGainUnits { RawUtility, FullTerminalUtilityRange }

/// <summary>Separate article approximate policy. It is never used by primary exact validation.</summary>
public sealed class ArticleApproximatePolicy
{
    public const int PivotCap = 1000;
    public int MaximumPivots { get; }
    public const double EarlyThreshold = 0.001, CapThreshold = 0.0025;
    public double Cutoff { get; }
    public ArticleApproximateGainUnits GainUnits { get; }
    public ArticleApproximatePolicy(double cutoff, ArticleApproximateGainUnits gainUnits, int maximumPivots = PivotCap)
    {
        if (!double.IsFinite(cutoff) || cutoff < 0 || cutoff >= 1 || !Enum.IsDefined(gainUnits))
            throw new ArgumentOutOfRangeException(nameof(cutoff));
        if (maximumPivots < 1) throw new ArgumentOutOfRangeException(nameof(maximumPivots));
        Cutoff = cutoff; GainUnits = gainUnits; MaximumPivots = maximumPivots;
    }

    public double[] Round(double[] projection, int[] actionCounts)
    {
        if (actionCounts.Length == 0 || actionCounts.Any(n => n < 1) || actionCounts.Sum() != projection.Length ||
            projection.Any(p => !double.IsFinite(p) || p < 0 || p > 1))
            throw new InvalidDataException("Invalid complete projected strategy.");
        var result = projection.ToArray();
        int offset = 0;
        foreach (int count in actionCounts)
        {
            if (Math.Abs(result.Skip(offset).Take(count).Sum()-1) > 1e-9)
                throw new InvalidDataException("Projected probabilities do not sum to one.");
            double total = 0;
            for (int a = 0; a < count; a++)
            {
                if (result[offset+a] < Cutoff) result[offset+a] = 0;
                total += result[offset+a];
            }
            if (total <= 0) throw new InvalidDataException("Rounding removed every action at an information set.");
            for (int a = 0; a < count; a++) result[offset+a] /= total;
            offset += count;
        }
        return result;
    }

    public double[] Scale(double[] signedRawGains, double[] ranges)
    {
        if (signedRawGains.Length != 2 || ranges.Length != 2 ||
            signedRawGains.Any(g => !double.IsFinite(g) || g < -1e-7) ||
            ranges.Any(r => !double.IsFinite(r) || r <= 0))
            throw new InvalidDataException("Invalid full unilateral best-response gains or terminal ranges.");
        // Full BR cannot be worse than the supplied strategy. Preserve signed raw
        // differences in the audit; remove only insignificant negative roundoff here.
        return signedRawGains.Select((g,p) => Math.Max(0,g) /
            (GainUnits == ArticleApproximateGainUnits.FullTerminalUtilityRange ? ranges[p] : 1)).ToArray();
    }

    public static double CertaintyEquivalentWealth(UtilityCalculator calculator, double utility)
    {
        // The legacy inverse omits CARA's optional affine transformation. Undo it
        // locally for this diagnostic; do not change established utility or BR code.
        double result;
        if (calculator is CARARiskAverseUtilityCalculator cara)
        {
            double exponential = utility;
            if (cara.LinearTransformation)
            {
                double e1 = -Math.Exp(-cara.Alpha*cara.WealthValue1);
                double e2 = -Math.Exp(-cara.Alpha*cara.WealthValue2);
                exponential = e2 + (utility-cara.CorrespondingUtility2) *
                    (e1-e2)/(cara.CorrespondingUtility1-cara.CorrespondingUtility2);
            }
            result = -Math.Log(-exponential)/cara.Alpha;
        }
        else result = calculator.InitialWealth + calculator.GetDeltaFromInitialWealthProducingSpecifiedSubjectiveUtility(utility);
        if (!double.IsFinite(result)) throw new InvalidDataException("Certainty equivalent is undefined.");
        return result;
    }

    public sealed record Candidate(int Pivot, double AverageGain, double[] Probabilities);
    public sealed record Decision(string Reason, int StoppingPivot, Candidate Accepted);

    public sealed class Selection
    {
        private readonly int maximumPivots;
        public Selection(int maximumPivots = PivotCap)
        {
            if (maximumPivots < 1) throw new ArgumentOutOfRangeException(nameof(maximumPivots));
            this.maximumPivots = maximumPivots;
        }
        public Candidate Best { get; private set; }
        public Decision Finished { get; private set; }
        public void Observe(int pivot, double averageGain, double[] profile)
        {
            if (Finished != null) throw new InvalidOperationException("Attempt has already ended.");
            if (pivot < 1 || pivot > maximumPivots || !double.IsFinite(averageGain) || averageGain < 0)
                throw new ArgumentOutOfRangeException(nameof(pivot));
            var candidate = new Candidate(pivot, averageGain, profile.ToArray());
            if (Best == null || averageGain < Best.AverageGain) Best = candidate; // earliest exact tie retained
            if (averageGain < EarlyThreshold) Finished = new("first-below-0.001", pivot, candidate);
        }
        public Decision EndAtCap()
        {
            if (Finished != null) return Finished;
            return Finished = new(Best != null && Best.AverageGain < CapThreshold ? "cap-accepted" : "cap-unsuccessful",
                maximumPivots, Best != null && Best.AverageGain < CapThreshold ? Best : null);
        }
        public Decision EndWithoutCap(string reason, int pivot)
        {
            if (Finished != null) return Finished;
            // An earlier error or early algorithm termination is not cap recovery.
            return Finished = new(reason, pivot, null);
        }
    }
}
