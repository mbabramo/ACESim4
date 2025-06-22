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
    /// <para><b>Purpose</b></para>
    ///
    /// Many simulation studies solve the same game under a broad range of parameter choices.  
    /// The process follows a common pattern:
    ///
    ///   • Begin with a baseline <see cref="GameOptions"/> that uses default values only.  
    ///   • Provide one or more <em>transformation functions</em> for each conceptual variable.  
    ///     Each function both mutates the options object and appends a deterministic, human-readable
    ///     suffix to <see cref="GameOptions.Name"/>.  
    ///   • Ask the launcher to expand those functions into a collection of <see cref="GameOptions"/>
    ///     instances under the permutation rules described below.
    ///
    /// <para>This type exposes three core helpers:</para>
    ///
    ///   • <see cref="ApplyPermutationsOfTransformations{T}"/> – a low-level Cartesian product engine  
    ///     that applies one transform from each supplied list.  
    ///   • <see cref="GenerateCombinations{T}"/> – a high-level wrapper that understands
    ///     <b>critical</b>, <b>global</b>, and <b>modifier</b> variables and produces the required
    ///     set of combinations, optionally collapsing duplicates.  
    ///
    /// ************************************************************************************************
    /// <para><b>Variable classes and permutation rules</b></para>
    ///
    /// • <em>Critical variables</em> are the headline levers.  When all modifiers are at their default
    ///   values, the launcher produces the <strong>full Cartesian product</strong> across every
    ///   critical variable, enumerating every mutual interaction.  
    ///
    /// • <em>Global variables</em> (a prefix of the critical list) have an additional guarantee:
    ///   <strong>if one value appears, then every value appears</strong>.  The baseline grid together
    ///   with the one-at-a-time sweeps automatically fulfils this guarantee.  
    ///
    /// • <em>Modifier variables</em> are swept <em>one at a time</em>.  For each non-baseline modifier
    ///   value the launcher generates exactly one simulation for each value of each critical variable,
    ///   varying only one critical variable at a time.  Two modifiers are never off baseline together,
    ///   so no Cartesian product occurs once a modifier is active, keeping the total run count linear.  
    ///
    ///   Example – let criticals be A ∈ {1, 2} and B ∈ {10, 20} while the modifier C ∈ {100, 200}.  
    ///   The C-sweep consists of:
    ///
    ///     A = 1, B = 10, C = 100  (baseline)  
    ///     A = 2, B = 10, C = 100  (vary A)  
    ///     A = 1, B = 10, C = 200  (vary C)  
    ///     A = 1, B = 20, C = 100  (vary B)  
    ///     … and so on.  
    ///
    /// ************************************************************************************************
    /// <para><b>Redundancies and the optional explicit-baseline row</b></para>
    ///
    /// A modifier dimension whose only entry is the baseline value would duplicate the null branch.
    /// Passing <c>includeBaselineValueForNoncritical = true</c> preserves that explicit baseline row
    /// for reporting convenience; otherwise it is omitted.  
    ///
    /// ************************************************************************************************
    /// <para><b>Integrating a subclass</b></para>
    ///
    ///   1. Override <see cref="GetVariationSets"/> to assemble the transformation lists and call
    ///      either <see cref="GenerateCombinations{T}"/> or the original <c>PerformTransformations</c>
    ///      family.  
    ///   2. Feed the resulting option sets to the solver harness.  
    ///   3. Use <see cref="VariableInfoPerOption"/> and
    ///      <see cref="GroupOptionSetsByClassification"/> during post-processing.
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

        /// <summary>
        /// Builds a single flattened list of <see cref="GameOptions"/> by delegating to
        /// <see cref="VariableCombinationGenerator"/>. Set <paramref name="removeDuplicates"/> to
        /// <c>true</c> (default) when identical variable–value assignments should collapse to one row.
        /// </summary>
        protected List<GameOptions> GenerateCombinations<T>(
            IReadOnlyList<VariableCombinationGenerator.Dimension<T>> dimensions,
            Func<T> optionsFactory,
            bool removeDuplicates = true)
            where T : GameOptions
        {
            if (dimensions is null)
                throw new ArgumentNullException(nameof(dimensions));
            if (optionsFactory is null)
                throw new ArgumentNullException(nameof(optionsFactory));

            var list = VariableCombinationGenerator
                .Generate(dimensions, optionsFactory)
                .Cast<GameOptions>()
                .ToList();

            if (!removeDuplicates)
                return list;

            return list
                .GroupBy(o => string.Join("|",
                    o.VariableSettings
                     .OrderBy(kv => kv.Key)
                     .Select(kv => $"{kv.Key}={kv.Value}")))
                .Select(g => g.First())
                .ToList();
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
