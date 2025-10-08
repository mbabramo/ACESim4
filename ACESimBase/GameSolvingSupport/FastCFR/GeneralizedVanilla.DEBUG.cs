using System;
using System.Collections.Generic;
using System.Linq;

namespace ACESim
{
    // Lightweight, GC-friendly snapshot of every information set’s relevant fields.
    public record InfoSetDebug(
        int NodeNumber,
        byte PlayerIndex,
        byte DecisionIndex,
        string Label,
        double[] Current,                    // current strategy (self)
        double[] OpponentCurrent,            // current strategy (opponent view), if used
        double[] Average,                    // average strategy
        double[] CumulativeRegret,           // cumulative regrets
        double[] LastCumulativeStrategyInc,  // last increment to cumulative strategy
        int BestResponseAction               // current BR action tag, if set
    );

    public static class InfoSetDebugDiff
    {
        public static string Diff(
            IReadOnlyList<InfoSetDebug> scalar,
            IReadOnlyList<InfoSetDebug> vector,
            double eps = 1e-12,
            int maxToShow = 25)
        {
            var left  = scalar.OrderBy(s => s.PlayerIndex).ThenBy(s => s.NodeNumber).ToList();
            var right = vector.OrderBy(s => s.PlayerIndex).ThenBy(s => s.NodeNumber).ToList();

            var sb = new System.Text.StringBuilder();
            if (left.Count != right.Count)
                sb.AppendLine($"Different info-set counts: scalar={left.Count}, vector={right.Count}");

            int shown = 0;
            for (int i = 0; i < Math.Min(left.Count, right.Count); i++)
            {
                var a = left[i]; var b = right[i];
                List<string> diffs = new();
                if (!Same(a.Current, b.Current, eps))                       diffs.Add("current");
                if (!Same(a.OpponentCurrent, b.OpponentCurrent, eps))       diffs.Add("oppCurrent");
                if (!Same(a.Average, b.Average, eps))                       diffs.Add("average");
                if (!Same(a.CumulativeRegret, b.CumulativeRegret, eps))     diffs.Add("cumRegret");
                if (!Same(a.LastCumulativeStrategyInc, b.LastCumulativeStrategyInc, eps)) diffs.Add("lastCum");
                if (a.BestResponseAction != b.BestResponseAction)           diffs.Add("bestRespAction");

                if (diffs.Count > 0)
                {
                    sb.AppendLine($"IS P{a.PlayerIndex} #{a.NodeNumber} (dec {a.DecisionIndex}) diffs: {string.Join(",", diffs)}");
                    sb.AppendLine($"  scalar  cur=[{Join(a.Current)}]  avg=[{Join(a.Average)}]  regret=[{Join(a.CumulativeRegret)}]  lastCum=[{Join(a.LastCumulativeStrategyInc)}]  br={a.BestResponseAction}");
                    sb.AppendLine($"  vector  cur=[{Join(b.Current)}]  avg=[{Join(b.Average)}]  regret=[{Join(b.CumulativeRegret)}]  lastCum=[{Join(b.LastCumulativeStrategyInc)}]  br={b.BestResponseAction}");
                    if (++shown >= maxToShow) break;
                }
            }
            return sb.ToString();

            static bool Same(double[] x, double[] y, double eps)
            {
                if (x == null && y == null) return true;
                if (x == null || y == null) return false;
                if (x.Length != y.Length) return false;
                for (int i = 0; i < x.Length; i++) if (Math.Abs(x[i] - y[i]) > eps) return false;
                return true;
            }
            static string Join(double[] a) => a == null ? "" : string.Join(",", a.Select(v => v.ToString("G17")));
        }
    }

    // Small partial that exposes a snapshot & an optional telemetry hook.
    public partial class GeneralizedVanilla
    {
        /// <summary>
        /// Optional hook invoked during the FastCFR loop to capture snapshots at named phases.
        /// </summary>
        public Action<string, List<InfoSetDebug>> FastCFRTelemetryHook { get; set; }

        /// <summary>Capture a stable, fully materialized view of all information-sets.</summary>
        public List<InfoSetDebug> SnapshotInfoSets()
        {
            var list = new List<InfoSetDebug>(InformationSets.Count);
            foreach (var iset in InformationSets)
            {
                var n = iset.NumPossibleActions;
                double[] cur    = new double[n];
                double[] curOpp = new double[n];
                double[] avg    = new double[n];
                double[] reg    = new double[n];
                double[] last   = new double[n];

                for (byte a = 1; a <= n; a++)
                {
                    cur[a - 1]    = iset.GetCurrentProbability(a, false);
                    curOpp[a - 1] = iset.GetCurrentProbability(a, true);   // the “opponent view” variant used in your unrolled path
                    avg[a - 1]    = iset.GetAverageStrategy(a);
                    reg[a - 1]    = iset.GetCumulativeRegret(a);
                    last[a - 1]   = iset.GetLastCumulativeStrategyIncrement(a);
                }

                list.Add(new InfoSetDebug(
                    NodeNumber: iset.InformationSetNodeNumber,
                    PlayerIndex: iset.PlayerIndex,
                    DecisionIndex: iset.DecisionIndex,
                    Label: iset.ToString(), // a stable printable form is fine here
                    Current: cur,
                    OpponentCurrent: curOpp,
                    Average: avg,
                    CumulativeRegret: reg,
                    LastCumulativeStrategyInc: last,
                    BestResponseAction: iset.BestResponseAction
                ));
            }
            return list;
        }
    }
}
// DEBUG -- delete entire file