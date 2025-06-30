using System;
using System.Collections.Generic;
using System.Linq;

namespace ACESimBase.Util.Combinatorics
{
    /// <summary>
    /// Generic permutation engine that produces <em>every logical combination exactly once</em>
    /// according to three roles a dimension can play:
    ///
    /// • <b>Global</b> – values are crossed with <em>everything</em> (full Cartesian product).
    /// • <b>Core</b>  – their <paramref name="CriticalTransforms"/> form a Cartesian grid when all
    ///   modifiers are default; when any modifier is active the generator varies <strong>one core
    ///   dimension at a time</strong>.
    /// • <b>Modifier</b> – at most one modifier may be non-default in any option set; each of its
    ///   <paramref name="ModifierTransforms"/> values is intersected with every core dimension one
    ///   at a time.
    ///
    /// A single <see cref="Dimension{T}"/> can supply both critical <em>and</em> modifier transforms.
    /// This prevents duplicates when a variable has “extra” non-critical values (e.g., critical
    /// C1 = {A,B} and non-critical C1 = {Z}).
    /// </summary>
    public static class VariableCombinationGenerator
    {
        // ─────────────────────────────────────────────────────────────────────
        //  Public API types
        // ─────────────────────────────────────────────────────────────────────

        public sealed record Dimension<T>(
            string Name,
            IReadOnlyList<Func<T, T>> CriticalTransforms,   // may be null/empty
            IReadOnlyList<Func<T, T>> ModifierTransforms,   // may be null/empty
            bool IsGlobal = false,
            bool IncludeBaselineValueForNoncritical = false // usually, we list the baseline value but do not then explicitly create a combination for it
            ) where T : class
        {
            public bool HasCritical   => CriticalTransforms != null && CriticalTransforms.Count > 0;
            public bool HasModifier   => ModifierTransforms != null && ModifierTransforms.Count > 0;
            public Func<T, T> DefaultCritical => HasCritical ? CriticalTransforms[0] : (x => x);
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Public API method
        // ─────────────────────────────────────────────────────────────────────

        public static List<T> Generate<T>(
            IReadOnlyList<Dimension<T>> dimensions,
            Func<T> baselineFactory) where T : class
        {
            if (dimensions == null)  throw new ArgumentNullException(nameof(dimensions));
            if (baselineFactory == null) throw new ArgumentNullException(nameof(baselineFactory));

            var globals   = dimensions.Where(d => d.IsGlobal).ToList();
            var cores     = dimensions.Where(d => !d.IsGlobal && d.HasCritical).ToList();
            var modifiers = dimensions.Where(d => d.HasModifier).ToList();

            var result = new List<T>();

            // Helper: apply a sequence of (dimension, transform) picks to a fresh baseline object
            T Apply(T seed, IEnumerable<(Dimension<T> dim, Func<T, T> tfm)> picks)
            {
                var obj = seed;
                foreach (var (d, tfm) in picks)
                    obj = tfm(obj);
                return obj;
            }

            // Cartesian helper for critical lists
            static IEnumerable<IEnumerable<Func<T,T>>> Cartesian(IEnumerable<IReadOnlyList<Func<T,T>>> lists)
            {
                IEnumerable<IEnumerable<Func<T,T>>> seed = new[] { Enumerable.Empty<Func<T,T>>() };
                return lists.Aggregate(seed,
                    (acc, lst) =>
                        from pre in acc
                        from tfm in lst
                        select pre.Append(tfm));
            }

            // 1. Iterate over full Cartesian product of global dimensions ------------------------
            var globalCart = Cartesian(globals.Select(g => g.CriticalTransforms));
            foreach (var globalPick in globalCart)
            {
                // 1a. Baseline core grid (all modifiers default) --------------------------------
                var coreCart = Cartesian(cores.Select(c => c.CriticalTransforms));
                foreach (var corePick in coreCart)
                    result.Add(Apply(baselineFactory(),
                                     globals.Zip(globalPick).Concat(cores.Zip(corePick))
                                            .Select(z => (z.First, z.Second))));

            // ─────────────────────────────────────────────────────────────────────
            // 1b. Modifier sweeps -- skip index 0 (default) to avoid duplicates
            // ─────────────────────────────────────────────────────────────────────
            foreach (var mod in modifiers)
            {
                // start at 1 – index 0 is the baseline value already covered by the
                // core-only grid, so including it would duplicate rows
                int startIdx = mod.IncludeBaselineValueForNoncritical ? 0 : 1;
                for (int idx = startIdx; idx < mod.ModifierTransforms.Count; idx++)
                {
                    var modTfm = mod.ModifierTransforms[idx];

                    // (i) baseline cores with this modifier value
                    var baselinePick =
                        globals.Select((g, i) => (g, globalPick.ElementAt(i)))
                               .Append((mod, modTfm))
                               .Concat(cores.Except(new[] { mod })
                                            .Select(c => (c, c.DefaultCritical)));

                    result.Add(Apply(baselineFactory(), baselinePick));

                    // (ii) vary each *other* core one-at-a-time
                    foreach (var core in cores)
                    {
                        if (core == mod) continue;          // same logical variable

                        for (int c = 1; c < core.CriticalTransforms.Count; c++)
                        {
                            var picks =
                                globals.Select((g, i) => (g, globalPick.ElementAt(i)))
                                       .Append((mod,  modTfm))
                                       .Append((core, core.CriticalTransforms[c]));

                            result.Add(Apply(baselineFactory(), picks));
                        }
                    }
                }
            }


            }

            return result;
        }
    }
}