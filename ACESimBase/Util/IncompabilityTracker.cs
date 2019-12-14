using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ACESim
{
    public class IncompabilityTracker
    {
        private Dictionary<(int i, int j), (bool iHatesJ, bool jHatesI)> Incompatibilities = new Dictionary<(int i, int j), (bool iHatesJ, bool jHatesI)>();
        public void AddIncompability(int i, int j, bool iHatesJ, bool jHatesI)
        {
            if (i <= j)
                Incompatibilities[(i, j)] = (iHatesJ, jHatesI);
            else
                Incompatibilities[(j, i)] = (jHatesI, iHatesJ);
        }

        public void Remove(int k)
        {
            Incompatibilities = Incompatibilities.Where(x => x.Key.i != k && x.Key.j != k).ToDictionary(x => x.Key, x => x.Value);
        }

        private static (int, int) Ordered(int i, int j)
        {
            return (Math.Min(i, j), Math.Max(i, j));
        }

        public bool Tracked(int i, int j)
        {
            return Incompatibilities.ContainsKey(Ordered(i, j));
        }

        public bool IsKnownIncompatible(int i, int j)
        {
            if (!Tracked(i, j))
                return false;
            var status = Incompatibilities[Ordered(i, j)];
            return status.iHatesJ || status.jHatesI;
        }

        public bool IsKnownIncompatibleWithAny(int i, IEnumerable<int> js)
        {
            return js.Any(j => IsKnownIncompatible(i, j));
        }

        public int[] CountIncompatibilities(int max, bool includeHaters, bool includeHated)
        {
            int[] results = new int[max];
            foreach (var incompatibility in Incompatibilities)
            {
                if ((includeHaters && incompatibility.Value.iHatesJ) || (includeHated && incompatibility.Value.jHatesI))
                    results[incompatibility.Key.i]++;
                if ((includeHaters && incompatibility.Value.jHatesI) || (includeHated && incompatibility.Value.iHatesJ))
                    results[incompatibility.Key.j]++;
            }
            return results;
        }

        public int[] GetOrdered(int max, bool mostIncompatibleFirst, bool includeHaters, bool includeHated)
        {
            int[] results = CountIncompatibilities(max, includeHaters, includeHated);
            var zipped = Enumerable.Range(0, results.Length).Zip(results, (index, incompatibilities) => new { Index = index, Incompatibilities = incompatibilities });
            return mostIncompatibleFirst ? zipped.OrderByDescending(x => x.Incompatibilities).Select(x => x.Index).ToArray() : zipped.OrderBy(x => x.Incompatibilities).Select(x => x.Index).ToArray();
        }
    }
}
