using System;
using System.Collections.Generic;
using System.Linq;
using ACESimBase.Util.NWayTreeStorage;

namespace ACESimBase.Util.NWayTreeStorage
{
    /// <summary>
    /// Builds an n-way tree that partitions a set of objects by their differing attributes (alphabetically)
    /// and can also return the attribute paths leading to each object.
    /// </summary>
    public static class AttributeTreeBuilder
    {
        /// <summary>
        /// Build the tree.
        /// </summary>
        public static NWayTreeStorage<T> BuildAttributeTree<T>(
            IEnumerable<T> items,
            Func<T, Dictionary<string, string>> attributeSelector)
        {
            BuildInternal(items, attributeSelector, out _, out var root);
            return root;
        }

        /// <summary>
        /// Build the tree (via <see cref="BuildAttributeTree{T}"/>) and return the attribute path taken to each object.
        /// </summary>
        public static List<(List<(string attribute, string value)> attributeValues, T item)>
            BuildAttributePaths<T>(
                IEnumerable<T> items,
                Func<T, Dictionary<string, string>> attributeSelector)
        {
            BuildInternal(items, attributeSelector, out var paths, out _);
            return paths;
        }

        // --------------------------------------------------------------------
        // Implementation
        // --------------------------------------------------------------------

        private static void BuildInternal<T>(
            IEnumerable<T> source,
            Func<T, Dictionary<string, string>> attributeSelector,
            out List<(List<(string, string)>, T)> paths,
            out NWayTreeStorage<T> root)
        {
            var itemList      = source.ToList();
            var attributeSets = itemList.Select(attributeSelector).ToList();

            var allAttributeKeys = attributeSets.SelectMany(d => d.Keys)
                                                .Distinct()
                                                .OrderBy(k => k, StringComparer.Ordinal)
                                                .ToList();

            // keep only attributes that vary among the objects
            var varyingAttributes = allAttributeKeys.Where(attr =>
                    attributeSets.Select(d => d.TryGetValue(attr, out var v) ? v : null)
                                 .Distinct()
                                 .Skip(1)
                                 .Any())
                .ToList();

            var internalRoot = new NWayTreeStorageRoot<T>(null, 0, useDictionary: false);
            root  = internalRoot;                                    // returned as NWayTreeStorage<T>
            paths = new List<(List<(string, string)>, T)>();

            BuildNode(varyingAttributes,
                      itemList,
                      attributeSets,
                      Enumerable.Range(0, itemList.Count).ToList(),
                      internalRoot,                                  // now correctly typed
                      new List<(string, string)>(),
                      paths);
        }


        private static void BuildNode<T>(
            List<string> attributeOrder,
            List<T> items,
            List<Dictionary<string, string>> attributeSets,
            List<int> indexes,
            NWayTreeStorageInternal<T> node,
            List<(string attribute, string value)> pathSoFar,
            List<(List<(string, string)>, T)> collectedPaths)
        {
            // Pick first attribute (alphabetically) that still differs in this subset
            string splitter = attributeOrder.FirstOrDefault(a =>
                indexes.Select(i => attributeSets[i].TryGetValue(a, out var v) ? v : null)
                       .Distinct()
                       .Skip(1)
                       .Any());

            if (splitter == null)   // no differing attributes remain
            {
                byte branchId = 1;
                foreach (int i in indexes)
                {
                    var leaf = node.AddBranch(branchId++, mayBeInternal: false);
                    leaf.StoredValue = items[i];
                    collectedPaths.Add((new List<(string, string)>(pathSoFar), items[i]));
                }
                return;
            }

            // Group by attribute value and recurse
            var grouped = indexes
                .GroupBy(i => attributeSets[i].TryGetValue(splitter, out var v) ? v ?? string.Empty : string.Empty)
                .OrderBy(g => g.Key, StringComparer.Ordinal);

            byte nextBranch = 1;
            foreach (var g in grouped)
            {
                var child = (NWayTreeStorageInternal<T>)node.AddBranch(nextBranch++, mayBeInternal: true);
                var nextPath = new List<(string, string)>(pathSoFar) { (splitter, g.Key) };
                BuildNode(attributeOrder.Where(a => a != splitter).ToList(),
                          items,
                          attributeSets,
                          g.ToList(),
                          child,
                          nextPath,
                          collectedPaths);
            }
        }
    }
}
