using System;
using System.Collections.Generic;
using System.Linq;

namespace ACESimBase.Util.Combinatorics
{
    /// <summary>
    /// A generic, data‑driven engine that enumerates option‑combinations for a set of independent
    /// <em>dimensions</em> according to three simple roles:
    ///
    /// <list type="bullet">
    ///   <item>
    ///     <term><see cref="DimensionRole.Global"/></term>
    ///     <description>
    ///       Global dimensions are always crossed with <b>every</b> other dimension.  The generator
    ///       therefore builds the complete Cartesian product across the set of globals before any
    ///       further logic is applied.
    ///     </description>
    ///   </item>
    ///   <item>
    ///     <term><see cref="DimensionRole.Core"/></term>
    ///     <description>
    ///       Core dimensions form a Cartesian product <b>only</b> when all <see cref="DimensionRole.Modifier"/>
    ///       dimensions are at their default values.  When at least one modifier chooses a non‑default
    ///       value, the generator varies <strong>one core dimension at a time</strong> while holding
    ///       the others at default.
    ///     </description>
    ///   </item>
    ///   <item>
    ///     <term><see cref="DimensionRole.Modifier"/></term>
    ///     <description>
    ///       At most one modifier dimension may hold a non‑default value in any generated option set.
    ///       For each value of that modifier the generator intersects it with every value of every
    ///       core dimension (one at a time, as noted above), <em>as well as</em> a baseline row in
    ///       which all core dimensions are default.
    ///     </description>
    ///   </item>
    /// </list>
    ///
    /// The algorithm guarantees that:
    /// <br/>• Every global combination appears in <b>every</b> generated option set.
    /// <br/>• No two modifier dimensions are off baseline together.
    /// <br/>• Once a modifier is off baseline, core dimensions are never permuted against one another.
    ///
    /// The class is completely agnostic about what an “option set” is: callers provide a factory that
    /// creates a baseline instance of <typeparamref name="T"/> plus, for each dimension, a list of
    /// transformations (functions <c>T → T</c>) whose <strong>first</strong> element represents the
    /// default value.
    ///
    /// A typical workflow is:
    /// <code language="csharp">
    /// var dims = new[]
    /// {
    ///     new Dimension<GameOptions>("FeeShift", feeShiftTransforms, DimensionRole.Core),
    ///     new Dimension<GameOptions>("RoundLimit", roundLimitTransforms, DimensionRole.Modifier),
    ///     new Dimension<GameOptions>("CostModel", costModelTransforms, DimensionRole.Global),
    /// };
    /// var allOptions = VariableCombinationGenerator.Generate(dims, () => new GameOptions());
    /// </code>
    /// </summary>
    public static class VariableCombinationGenerator
    {
        /// <summary>
        /// The behavioural category that controls how a dimension is combined with others.
        /// </summary>
        public enum DimensionRole
        {
            /// <summary>
            /// Fully crosses with every other dimension.
            /// </summary>
            Global,

            /// <summary>
            /// Forms a Cartesian product with <b>other core</b> dimensions <em>only</em> when every
            /// modifier dimension is default.  Otherwise the generator varies one core dimension at a
            /// time.
            /// </summary>
            Core,

            /// <summary>
            /// At most one modifier may be non‑default in any option set.  Each of its values is
            /// intersected with every core dimension (one at a time) plus a baseline row in which all
            /// cores are default.
            /// </summary>
            Modifier
        }

        /// <summary>
        /// A list of transforms together with a name and role.
        /// </summary>
        public sealed record Dimension<T>(
            string Name,
            IReadOnlyList<Func<T, T>> Transforms,
            DimensionRole Role) where T : class
        {
            /// <summary> Index of the default transform (always 0). </summary>
            public const int DefaultIndex = 0;
        }

        /// <summary>
        /// Generates the complete set of option objects under the rules explained in the class‑level
        /// documentation.
        /// </summary>
        /// <typeparam name="T">Type of the option object.</typeparam>
        /// <param name="dimensions">All dimensions with their roles and transforms.</param>
        /// <param name="baselineFactory">Factory that returns a fresh, baseline instance of <typeparamref name="T"/>.</param>
        /// <param name="includeBaselineModifierValue">
        ///     If <c>true</c> and a modifier dimension contains "default" as one of its transforms,
        ///     an explicit baseline row is emitted inside that modifier sweep.  When <c>false</c> the
        ///     baseline row is omitted because it is already covered by the pure core Cartesian grid.
        /// </param>
        /// <returns>All option sets in generation order.</returns>
        public static List<T> Generate<T>(
            IReadOnlyList<Dimension<T>> dimensions,
            Func<T> baselineFactory,
            bool includeBaselineModifierValue = false) where T : class
        {
            if (dimensions == null) throw new ArgumentNullException(nameof(dimensions));
            if (baselineFactory == null) throw new ArgumentNullException(nameof(baselineFactory));

            var globals   = dimensions.Where(d => d.Role == DimensionRole.Global ).ToList();
            var cores     = dimensions.Where(d => d.Role == DimensionRole.Core   ).ToList();
            var modifiers = dimensions.Where(d => d.Role == DimensionRole.Modifier).ToList();

            var result = new List<T>();

            // ------------------------------------------------------------------
            // 1. Iterate over every global‑dimension combination (full Cartesian)
            // ------------------------------------------------------------------
            foreach (var globalChoice in CartesianProduct(globals))
            {
                // Helper that starts from a fresh baseline T and applies the supplied selections.
                T Apply(params (Dimension<T> dim, int index)[] picks)
                {
                    var obj = baselineFactory();
                    foreach (var (d, idx) in picks)
                        obj = d.Transforms[idx](obj);
                    return obj;
                }

                // Pack the global selections as a tuple list for easy concatenation later.
                var globalTuple = globalChoice.ToArray();

                // --------------------------------------------------------------
                // 1a. Baseline grid: full Cartesian product of the cores while
                //     all modifiers are default.
                // --------------------------------------------------------------
                foreach (var coreChoice in CartesianProduct(cores))
                    result.Add(Apply(globalTuple.Concat(coreChoice).ToArray()));

                // --------------------------------------------------------------
                // 1b. For each modifier dimension, sweep its values one at a time
                //     while varying one core dimension at a time.
                // --------------------------------------------------------------
                foreach (var modifier in modifiers)
                {
                    for (int m = 0; m < modifier.Transforms.Count; m++)
                    {
                        bool isBaseline = m == Dimension<T>.DefaultIndex;
                        if (isBaseline && !includeBaselineModifierValue)
                            continue; // baseline already covered by 1a

                        // (i) Baseline row for this modifier value (all cores default)
                        result.Add(Apply(globalTuple.Append((modifier, m)).ToArray()));

                        // (ii) One‑core‑at‑a‑time rows
                        foreach (var core in cores)
                        {
                            for (int c = 1; c < core.Transforms.Count; c++) // skip default
                                result.Add(Apply(globalTuple
                                                 .Append((modifier, m))
                                                 .Append((core, c))
                                                 .ToArray()));
                        }
                    }
                }
            }

            return result;
        }

        // ---------------------------------------------------------------------
        //  Internal helpers
        // ---------------------------------------------------------------------

        private static IEnumerable<IEnumerable<(Dimension<T> dim, int index)>> CartesianProduct<T>(
            IReadOnlyList<Dimension<T>> dimensions) where T : class
        {
            IEnumerable<IEnumerable<(Dimension<T>, int)>> Seed() => new[]
            {
                Enumerable.Empty<(Dimension<T>, int)>()
            };

            return dimensions.Aggregate(
                Seed(),
                (acc, dim) =>
                    from partial in acc
                    from idx in Enumerable.Range(0, dim.Transforms.Count)
                    select partial.Append((dim, idx)));
        }
    }
}
