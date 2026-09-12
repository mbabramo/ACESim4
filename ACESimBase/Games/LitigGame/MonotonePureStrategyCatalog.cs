using ACESim;
using ACESimBase.GameSolvingAlgorithms;
using ACESimBase.GameSolvingSupport.GameTree;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text.Json;

namespace ACESimBase.Games.LitigGame
{
    public sealed record MonotoneLitigationStrategy(string Id, int Index, bool Plaintiff,
        int SignalCount, int OfferCount, int ParticipatingTypes, int ContinuingTypes, int[] Offers)
    {
        // Signals are zero-based here. Both parties observe the SAME plaintiff-favorable quality scale.
        public bool Participates(int signal) => Plaintiff ? signal >= SignalCount - ParticipatingTypes : signal < ParticipatingTypes;
        public bool Continues(int signal) => Participates(signal) && (Plaintiff ? signal >= SignalCount - ContinuingTypes : signal < ContinuingTypes);
        public int ParticipationBoundary => Plaintiff ? SignalCount - ParticipatingTypes : ParticipatingTypes;
        public int ContinuationBoundary => Plaintiff ? SignalCount - ContinuingTypes : ContinuingTypes;
        public byte Action(LitigGameDecisions decision, int signal) => decision switch
        {
            LitigGameDecisions.PFile or LitigGameDecisions.DAnswer => Participates(signal) ? (byte)1 : (byte)2,
            LitigGameDecisions.PAbandon or LitigGameDecisions.DDefault => !Participates(signal) || Continues(signal) ? (byte)2 : (byte)1,
            // Offers for nonparticipants are canonical deterministic completions. Exit-planning
            // participants keep their offers because exit is implemented only after bargaining fails.
            LitigGameDecisions.POffer or LitigGameDecisions.DOffer => (byte)(Participates(signal) ? Offers[signal] : 1),
            _ => throw new NotSupportedException($"Unexpected strategic decision {decision}.")
        };
    }

    public sealed class MonotonePureStrategyCatalog : IEnumeratedPureStrategySpace
    {
        public const string Restrictions = "Common plaintiff-favorable signal scale. P files increasingly and abandons decreasingly; D answers decreasingly and defaults increasingly. Both monetary offers increase weakly over ALL participating types, across the exit boundary. No symmetry. Own nonparticipation collapses exit/offers; deterministic continue/offer-1 completions. Exit commitments precede offers but apply only after unsuccessful bargaining.";
        public MonotoneLitigationStrategy[] Plaintiff { get; }
        public MonotoneLitigationStrategy[] Defendant { get; }
        private readonly Dictionary<int, int> signals;
        public int Count(int player) => (player == 0 ? Plaintiff : Defendant).Length;
        public string Id(int player, int strategy) => (player == 0 ? Plaintiff : Defendant)[strategy].Id;
        public string DescriptionJson => JsonSerializer.Serialize(new { Schema = 1, Restrictions, Plaintiff, Defendant }, PureRunStore.Json);

        public MonotonePureStrategyCatalog(int n, int m, StrategiesDeveloperBase developer = null)
        {
            Plaintiff = Enumerate(n, m, true).ToArray(); Defendant = Enumerate(n, m, false).ToArray();
            if (developer == null) return;
            signals = new Dictionary<int, int>();
            foreach (var node in developer.InformationSets)
            {
                if (node.PlayerIndex > 1) throw new NotSupportedException("Two litigation parties required.");
                var signalCode = node.PlayerIndex == 0 ? LitigGameDecisions.PLiabilitySignal : LitigGameDecisions.DLiabilitySignal;
                var values = node.LabeledInformationSet.Where(x => developer.GameDefinition.DecisionsExecutionOrder[x.decisionIndex].DecisionByteCode == (byte)signalCode).Select(x => x.information).ToArray();
                if (values.Length != 1 && !(n == 1 && values.Length == 0)) throw new InvalidOperationException("Expected exactly one private liability signal at each strategic information set.");
                int signal = values.Length == 0 ? 0 : values[0] - 1;
                if (signal < 0 || signal >= n) throw new InvalidOperationException("Private signal out of range.");
                signals[node.InformationSetNodeNumber] = signal;
            }
        }
        public byte Action(int player, int strategy, InformationSetNode node)
        {
            if (node.PlayerIndex != player) throw new ArgumentException("Player mismatch.");
            return (player == 0 ? Plaintiff : Defendant)[strategy].Action((LitigGameDecisions)node.Decision.DecisionByteCode,
                signals[node.InformationSetNodeNumber]);
        }
        public static BigInteger ExpectedCount(int n, int m)
        {
            if (n < 1 || m < 1) throw new ArgumentOutOfRangeException(nameof(n));
            BigInteger binomial = 1, sum = 1;
            for (int k = 1; k <= n; k++) { binomial = binomial * (k + m - 1) / k; sum += (k + 1) * binomial; }
            return sum;
        }
        public static IEnumerable<MonotoneLitigationStrategy> Enumerate(int n, int m, bool plaintiff)
        {
            if (n < 1 || n > 254 || m < 1 || m > 254) throw new ArgumentOutOfRangeException(nameof(n));
            if (ExpectedCount(n, m) > 100_000) throw new ArgumentException("Catalog exceeds the explicit experimental resource guard (100,000 strategies per player).");
            int index = 0;
            for (int k = 0; k <= n; k++) for (int continuing = 0; continuing <= k; continuing++)
            foreach (int[] schedule in Schedules(k, m))
            {
                var offers = new int[n];
                Array.Copy(schedule, 0, offers, plaintiff ? n - k : 0, k);
                string id = $"{(plaintiff ? "P" : "D")}-n{n}-m{m}-k{k}-c{continuing}-o{string.Join(".", schedule)}";
                yield return new(id, index++, plaintiff, n, m, k, continuing, offers);
            }
        }
        private static IEnumerable<int[]> Schedules(int k, int m)
        {
            var values = new int[k];
            IEnumerable<int[]> Visit(int position, int minimum)
            {
                if (position == k) { yield return values.ToArray(); yield break; }
                for (int v = minimum; v <= m; v++)
                { values[position] = v; foreach (var s in Visit(position + 1, v)) yield return s; }
            }
            return Visit(0, 1);
        }
    }
}
