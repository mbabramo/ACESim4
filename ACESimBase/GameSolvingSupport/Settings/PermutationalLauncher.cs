using ACESim;
using ACESimBase.Util.Collections;
using ACESimBase.Util.Combinatorics;
using ACESimBase.Util.Debugging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESimBase.GameSolvingSupport.Settings
{
    /// <summary>
    /// ************************************************************************************************
    /// <para><b>What this class is for</b></para>
    ///
    /// Almost every simulation project needs to run the same solver across a wide set of parameter
    /// configurations.  The pattern is always:
    ///
    ///   • Start with a baseline <see cref="GameOptions"/> object (all defaults).
    ///   • Supply one or more <em>transformation functions</em> for each high‑level variable.  Each
    ///     function mutates the object <em>and</em> appends a suffix to <c>GameOptions.Name</c> so the
    ///     final result bears a deterministic, human‑readable ID.
    ///   • Ask the launcher to turn those functions into a set of <see cref="GameOptions"/> instances
    ///     according to the rules described below.
    ///
    /// <para>This class provides three key helpers:</para>
    ///
    ///   • <see cref="ApplyPermutationsOfTransformations{T}"/> – the low‑level Cartesian‑product
    ///     engine that enumerates every way of picking <b>one</b> transform from each list and applies
    ///     them in order.
    ///   • <see cref="PerformTransformations{T}(List{List{Func{T,T}}},int,int,bool,bool,Func{T})"/> – the
    ///     high‑level routine that understands <b>critical</b>, <b>super‑critical</b>, and
    ///     <b>non‑critical</b> variables and generates the required sweep batches.
    ///   • <see cref="NameMap"/> – deduplicates filenames so that logically identical games (for
    ///     example, a baseline reached via two different paths) are solved only once on disk even if
    ///     they appear multiple times in memory.
    ///
    /// ************************************************************************************************
    /// <para><b>Variable classes and the launch‑space rules</b></para>
    ///
    /// • <em>Critical variables</em> are the headline levers. When <b>all</b> non‑critical variables
    ///   are at their default values the launcher produces the <strong>full Cartesian product</strong>
    ///   across every critical variable – i.e., it enumerates every possible mutual interaction.
    ///
    /// • <em>Super‑critical variables</em> are simply a prefix of the critical list.  They carry one
    ///   extra guarantee: <strong>if a simulation exists with one value of a super‑critical variable,
    ///   then a simulation exists with <em>every</em> value of that variable</strong>.  The baseline
    ///   full‑grid together with the one‑at‑a‑time sweeps described next automatically fulfils the
    ///   guarantee, so the parameter is retained only for compatibility.
    ///
    /// • <em>Non‑critical variables</em> are swept <em>one at a time</em>.  For each non‑critical value
    ///   the launcher creates <em>one</em> simulation for <em>each value of each critical variable</em>,
    ///   varying <strong>only one critical variable at a time</strong>.  Non‑critical variables are
    ///   never permuted against one another – i.e., at most one is off baseline in any simulation.
    ///
    ///   Concretely, if criticals are A ∈ {1,2} and B ∈ {10,20} while the non‑critical C ∈ {100,200},
    ///   then the C‑sweep looks like:
    ///
    ///     A = 1, B = 10, C = 100  (baseline)
    ///     A = 2, B = 10, C = 100  (vary A)
    ///     A = 1, B = 10, C = 200  (vary C)
    ///     A = 1, B = 20, C = 100  (vary B)
    ///     … and so on for every value of C.
    ///
    ///   Because each critical variable is exercised separately, no Cartesian products appear once a
    ///   non‑critical dimension is off baseline.  This keeps the total run‑count linear in the number
    ///   of critical values instead of exponential.
    ///
    /// ************************************************************************************************
    /// <para><b>Redundancies and the “baseline again” row</b></para>
    ///
    /// A non‑critical dimension whose <em>only</em> entry is “baseline” duplicates the null branch.
    /// Setting <paramref name="includeBaselineValueForNoncritical"/> to <c>true</c> preserves that
    /// duplicate so that reporting layers can display an explicit baseline row inside each sweep.
    /// The extra solver work is avoided because <see cref="NameMap"/> resolves both long names to the
    /// same underlying output file.
    ///
    /// ************************************************************************************************
    /// <para><b>How to use from a subclass</b></para>
    ///
    ///   1. Override <see cref="GetVariationSets"/> to build your transform lists and call
    ///      <see cref="PerformTransformations{T}"/>.
    ///   2. Point your solver harness at the resulting option sets.
    ///   3. Use <see cref="VariableInfoPerOption"/> and <see cref="GroupOptionSetsByClassification"/>
    ///      during post‑processing to organise the results.
    ///
    /// ************************************************************************************************
    /// </summary>
    public abstract class PermutationalLauncher : Launcher
    {
        #region Variation set definitions

        /// <summary>
        /// Ordered list of human‑readable names used to label the folders that hold simulation results
        /// for each outer batch produced by <see cref="GetVariationSets"/>.
        /// </summary>
        public abstract List<string> NamesOfVariationSets { get; }

        /// <summary>
        /// Tuple list mapping each variable name to the <strong>string form of its default value</strong>.
        /// Used when deciding what counts as “baseline”.
        /// </summary>
        public abstract List<(string, string)> DefaultVariableValues { get; }

        /// <summary>
        /// Supplies the set of values that count as <em>critical</em> (and are therefore part of the
        /// full Cartesian product) for each variable.
        /// </summary>
        public abstract List<(string criticalValueName, string[] criticalValueValues)> CriticalVariableValues { get; }

        /// <summary>
        /// Stable prefix (e.g., “FS036”, “APP001”) inserted in every report filename so that
        /// downstream code can glob them without hard‑coding paths.
        /// </summary>
        public abstract string ReportPrefix { get; }

        #endregion

        #region Helper data structures and methods

        /// <summary>
        /// Identifies <em>one</em> simulation run inside a variation set.  <c>columnMatches</c> holds
        /// CSV‑column/value pairs used by the post‑processing layer when searching cross‑tabs.
        /// </summary>
        public record SimulationIdentifier(
            string nameForSimulation,
            List<(string columnName, string expectedValue)> columnMatches)
        {
            public SimulationIdentifier With(string columnName, string expectedValue)
            {
                var replacementMatches = columnMatches.ToList();
                bool exists = false;
                for (int i = 0; i < columnMatches.Count; i++)
                {
                    if (replacementMatches[i].columnName == columnName)
                    {
                        exists = true;
                        replacementMatches[i] = (columnName, expectedValue);
                        break;
                    }
                }
                if (!exists)
                    replacementMatches.Add((columnName, expectedValue));
                return new SimulationIdentifier(nameForSimulation, replacementMatches);
            }

            public override string ToString()
                => $"{nameForSimulation}: {string.Join(",", columnMatches.Select(x => $"{x.columnName}={x.expectedValue}"))}";
        }

        /// <summary>
        /// A <em>group</em> of <see cref="SimulationIdentifier"/> objects that belong together in the
        /// same report or graphic.
        /// </summary>
        public record SimulationSetsIdentifier(
            string nameOfSet,
            List<SimulationIdentifier> simulationIdentifiers)
        {
            public override string ToString()
                => nameOfSet + " \n" + string.Join(" \n", simulationIdentifiers);
        }

        /// <summary>
        /// Normalised metadata used both for folder names and for high‑level summaries of each
        /// <see cref="GameOptions"/> instance.
        /// </summary>
        public record GroupingVariableInfo(
            string VariableName,
            string Value,
            bool IsDefault,
            bool IncludeInCritical);

        /// <summary>
        /// Helper: mutates <paramref name="options"/> via <paramref name="transform"/>, appends
        /// <paramref name="suffix"/> to its <c>Name</c>, and returns the same instance so that lambdas
        /// can be chained fluently.
        /// </summary>
        public T GetAndTransform<T>(T options, string suffix, Action<T> transform) where T : GameOptions
        {
            T g = options;
            transform(g);
            g.Name = g.Name + suffix;
            while (g.Name.StartsWith(" "))
                g.Name = g.Name.Substring(1);
            return g;
        }

        /// <summary>
        /// Core Cartesian‑product engine.  Takes <paramref name="transformLists"/> where each inner
        /// list represents one dimension, enumerates every way of choosing <b>one</b> transform from
        /// each list, applies them in order to a fresh options object, and returns the lot.
        /// </summary>
        public List<T> ApplyPermutationsOfTransformations<T>(
            Func<T> optionsFn,
            List<List<Func<T, T>>> transformLists)
            where T : GameOptions
        {
            var result = new List<T>();
            var permutations = PermutationMaker.GetPermutationsOfItems(transformLists);

            foreach (var permutation in permutations)
            {
                var options = optionsFn();
                foreach (var transform in permutation)
                    options = transform(options);
                result.Add(options);
            }
            return result;
        }

        protected List<List<GameOptions>> PerformTransformations<T>(
            List<List<Func<T, T>>> allTransformations,
            int numCritical,
            int numSupercriticals,
            bool includeBaselineValueForNoncritical,
            Func<T> optionsFactory)
            where T : GameOptions
        {
            // 1  Map each transformation list to a role understood by VariableCombinationGenerator
            var dimensions = new List<VariableCombinationGenerator.Dimension<T>>();
            for (int i = 0; i < allTransformations.Count; i++)
            {
                var role = i < numSupercriticals
                    ? VariableCombinationGenerator.DimensionRole.Global
                    : i < numCritical
                        ? VariableCombinationGenerator.DimensionRole.Core
                        : VariableCombinationGenerator.DimensionRole.Modifier;

                dimensions.Add(new VariableCombinationGenerator.Dimension<T>($"D{i}", allTransformations[i], role));
            }

            // 2  Generate every combination once
            var allOptions = VariableCombinationGenerator.Generate(
                dimensions,
                optionsFactory,
                includeBaselineValueForNoncritical);

            // 3  Re-create the historical batch structure
            var modifierDims = dimensions
                .Where(d => d.Role == VariableCombinationGenerator.DimensionRole.Modifier)
                .ToArray();

            bool IsModifierDefault(T opt, VariableCombinationGenerator.Dimension<T> mod)
            {
                var baseline = optionsFactory();
                var applied  = mod.Transforms[0](optionsFactory());
                return opt.ToString() == applied.ToString();   // simple string compare is enough
            }

            // Batch 0 – all modifiers at default
            var batches = new List<List<GameOptions>>
            {
                allOptions
                    .Where(o => modifierDims.All(m => IsModifierDefault(o, m)))
                    .Cast<GameOptions>()
                    .ToList()
            };
            AddDefaultNoncriticalValues(batches[0]);

            // One batch per modifier dimension
            foreach (var mod in modifierDims)
            {
                var sweep = allOptions
                    .Where(o => !IsModifierDefault(o, mod) &&
                                modifierDims.All(m => m == mod || IsModifierDefault(o, m)))
                    .Cast<GameOptions>()
                    .ToList();

                if (sweep.Count == 0)
                    continue;

                AddDefaultNoncriticalValues(sweep);
                batches.Add(sweep);
            }

            return batches;
        }


        // -----------------------------------------------------------------------------
        //  Helpers (private)
        // -----------------------------------------------------------------------------

        private void AddDefaultNoncriticalValues<T>(IEnumerable<T> optionSet) where T : GameOptions
        {
            var defaults = DefaultVariableValues;
            foreach (var opt in optionSet)
                foreach (var (varName, defVal) in defaults)
                    if (!opt.VariableSettings.ContainsKey(varName))
                        opt.VariableSettings[varName] = defVal;
        }

        #endregion

        /// <summary>
        /// Default flattener – simply concatenates all inner sets.  Override if a custom ordering is
        /// needed (e.g., litigation games sort by variable values).
        /// </summary>
        public virtual List<GameOptions> FlattenAndOrderGameSets(
            List<List<GameOptions>> gamesSets) =>
            gamesSets.SelectMany(x => x).ToList();

        /// <summary>
        /// Subclasses must return a <em>list of lists</em>, where each outer element corresponds to one
        /// of <see cref="NamesOfVariationSets"/>.
        /// </summary>
        public abstract List<List<GameOptions>> GetVariationSets(
            bool includeBaselineValueForNoncritical);

        /// <summary>
        /// Convenience wrapper that excludes redundant baseline entries.
        /// </summary>
        public void AddToOptionsSets(List<GameOptions> options)
        {
            bool includeBaselineValueForNoncritical = false; // default behaviour
            AddToOptionsSets(options, includeBaselineValueForNoncritical);
        }

        /// <summary>
        /// Adds every generated <see cref="GameOptions"/> to <paramref name="options"/>.  Throws if
        /// redundancies are detected while <paramref name="allowRedundancies"/> is <c>false</c>.
        /// </summary>
        public void AddToOptionsSets(List<GameOptions> options, bool allowRedundancies)
        {
            var gamesSets = GetVariationSets(allowRedundancies); // non‑critical sets
            List<GameOptions> eachGameIndependently = FlattenAndOrderGameSets(gamesSets);

            List<string> optionChoices = eachGameIndependently
                .Select(x => ToCompleteString(x.VariableSettings)).ToList();

            static string ToCompleteString<TKey, TValue>(IDictionary<TKey, TValue> dictionary) =>
                "{" + string.Join(",", dictionary.Select(kv => kv.Key + "=" + kv.Value)) + "}";

            if (!allowRedundancies && optionChoices.Distinct().Count() != optionChoices.Count())
                throw new Exception("redundancies found");

            options.AddRange(eachGameIndependently);
        }

        /// <summary>
        /// Convenience projection: returns, for every option set, the list of
        /// <see cref="GroupingVariableInfo"/> items that describe it.
        /// </summary>
        public List<(string OptionSetName, List<GroupingVariableInfo> Variables)>
            VariableInfoPerOption
        {
            get
            {
                var list = new List<(string, List<GroupingVariableInfo>)>();
                var defaultValues = DefaultVariableValues.ToDictionary(x => x.Item1, x => x.Item2);
                var criticalVars = new HashSet<string>(CriticalVariableValues.Select(x => x.criticalValueName));

                foreach (var opt in GetOptionsSets())
                    list.Add((opt.Name,
                              BuildGroupingVariableInfo(opt, defaultValues, criticalVars)));

                return list;
            }
        }

        /// <summary>
        /// Maps every verbose option‑set name (possibly redundant baseline) to the canonical
        /// non‑redundant name that actually appears on disk.  The algorithm repeatedly strips the
        /// last suffix until a match is found.
        /// </summary>
        public Dictionary<string, string> NameMap
        {
            get
            {
                var withRedundancies = new List<GameOptions>();
                AddToOptionsSets(withRedundancies, true);

                var withoutRedundancies = new List<GameOptions>();
                AddToOptionsSets(withoutRedundancies, false);

                var result = new Dictionary<string, string>();

                foreach (var gameOptions in withRedundancies)
                {
                    string runAsName = gameOptions.Name;
                    while (!withoutRedundancies.Any(x => x.Name == runAsName))
                        runAsName = runAsName.Substring(0, runAsName.LastIndexOf(' '));
                    result[gameOptions.Name] = runAsName;
                }
                return result;
            }
        }

        /// <summary>
        /// Builds the per‑variable metadata used elsewhere for grouping and for building folder
        /// names.
        /// </summary>
        public static List<GroupingVariableInfo> BuildGroupingVariableInfo(
            GameOptions option,
            Dictionary<string, string> defaultValues,
            HashSet<string> criticalVariables)
        {
            static string Normalize(object o) => o is double d
                ? d.ToString("G", System.Globalization.CultureInfo.InvariantCulture)
                : o?.ToString().Trim();

            var result = new List<GroupingVariableInfo>();

            foreach (var (key, val) in option.VariableSettings)
            {
                string normalizedVal = Normalize(val);
                bool isCritical = criticalVariables.Contains(key);
                bool isDefault = defaultValues.TryGetValue(key, out var def) && normalizedVal == Normalize(def);

                result.Add(new GroupingVariableInfo(key, normalizedVal, isDefault, isCritical));
            }

            return result;
        }

        /// <summary>
        /// Returns a short human‑readable label ("Baseline", "Fee Shift = 2.0", …) describing how an
        /// option set differs from the global default.
        /// </summary>
        public static string ClassifyOptionSet(List<GroupingVariableInfo> variables)
        {
            var criticals = variables.Where(x => x.IncludeInCritical).ToList();
            var noncriticals = variables.Where(x => !x.IncludeInCritical).ToList();

            var noncritDiffs = noncriticals.Where(x => !x.IsDefault).ToList();
            var critDiffs = criticals.Where(x => !x.IsDefault).ToList();

            if (noncritDiffs.Count + critDiffs.Count > 1)
            {
                var parts = noncritDiffs.Select(v => $"{v.VariableName} = {v.Value}");
                return string.Join(" & ", parts);
            }

            if (noncritDiffs.Count == 1)
                return $"{noncritDiffs[0].VariableName} = {noncritDiffs[0].Value}";
            if (critDiffs.Count == 1)
                return $"Additional {critDiffs[0].VariableName} = {critDiffs[0].Value}";

            return "Baseline";
        }

        /// <summary>
        /// Groups option‑set names into folders for the "Individual Simulation Results" tree created
        /// by the post‑processing layer.
        /// </summary>
        public Dictionary<string, List<string>> GroupOptionSetsByClassification()
        {
            static string Normalize(object o) => o is double d
                ? d.ToString("G", System.Globalization.CultureInfo.InvariantCulture)
                : o?.ToString().Trim();

            var defaultDict = DefaultVariableValues.ToDictionary(p => p.Item1, p => Normalize(p.Item2));
            var criticalValueDict = CriticalVariableValues.ToDictionary(
                cv => cv.criticalValueName,
                cv => cv.criticalValueValues.Select(Normalize).ToHashSet());

            var result = new Dictionary<string, List<string>>();

            foreach (var (optionSetName, vars) in VariableInfoPerOption)
            {
                bool qualifiesForBaseline = true;
                var folderParts = new List<string>();

                foreach (var v in vars)
                {
                    if (v.IsDefault)
                        continue;

                    bool isCritVar = v.IncludeInCritical;
                    bool isCritValue = isCritVar && criticalValueDict[v.VariableName].Contains(v.Value);

                    if (isCritVar && isCritValue)
                        continue;

                    qualifiesForBaseline = false;

                    string part = isCritVar
                        ? $"Additional {v.VariableName} = {v.Value}"
                        : $"{v.VariableName} = {v.Value}";

                    folderParts.Add(part);
                }

                string groupName = qualifiesForBaseline ? "Baseline" : string.Join(" & ", folderParts.OrderBy(p => p));

                if (!result.TryGetValue(groupName, out var list))
                    result[groupName] = list = new List<string>();

                list.Add(optionSetName);
            }

            return result;
        }
    }
}
