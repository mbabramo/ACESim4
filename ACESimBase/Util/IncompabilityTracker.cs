using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ACESim
{
    public class IncompabilityTracker
    {
        private HashSet<(int i, int j)> Incompatibilities = new HashSet<(int i, int j)>();
        public void AddIncompability(int i, int j)
        {
            Incompatibilities.Add(Ordered(i, j));
        }

        public void Remove(int k)
        {
            Incompatibilities = Incompatibilities.Where(x => x.i != k && x.j != k).ToHashSet();
        }

        private static (int, int) Ordered(int i, int j)
        {
            return (Math.Min(i, j), Math.Max(i, j));
        }

        public bool IsIncompatible(int i, int j)
        {
            return Incompatibilities.Contains(Ordered(i, j));
        }

        public bool IsIncompatibleWithAny(int i, IEnumerable<int> js)
        {
            return js.Any(j => IsIncompatible(i, j));
        }

        public int[] CountIncompatibilities(int max)
        {
            int[] results = new int[max];
            foreach (var incompatibility in Incompatibilities)
            {
                results[incompatibility.i]++;
                results[incompatibility.j]++;
            }
            return results;
        }

        public int[] OrderBy(int max, bool mostIncompatible)
        {
            int[] results = CountIncompatibilities(max);
            var zipped = Enumerable.Range(0, results.Length).Zip(results, (index, incompatibilities) => new { Index = index, Incompatibilities = incompatibilities });
            return mostIncompatible ? zipped.OrderByDescending(x => x.Incompatibilities).Select(x => x.Index).ToArray() : zipped.OrderBy(x => x.Incompatibilities).Select(x => x.Index).ToArray();
        }
    }
}
