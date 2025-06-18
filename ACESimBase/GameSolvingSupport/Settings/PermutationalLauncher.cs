﻿using ACESim;
using ACESimBase.Util.Collections;
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
    /// Almost every simulation project needs to run the exact same solver under a very large set of
    /// parameter configurations.  The pattern is always:
    /// 
    ///   • Start with a baseline <see cref="GameOptions"/> object (all defaults).  
    ///   • For each high-level variable you care about, supply one or more <i>transformation
    ///     functions</i> – each function mutates the object <em>and</em> appends a short suffix to
    ///     <c>GameOptions.Name</c> so that the final result bears a deterministic, human-readable ID.
    ///   • Ask for the Cartesian product of those functions so that every combination becomes its own
    ///     simulation run.
    /// 
    /// <para>This class supplies the reusable plumbing for that pattern:</para>
    /// 
    ///   • <see cref="ApplyPermutationsOfTransformations{T}"/> – the core “Cartesian product engine”
    ///     that physically iterates through permutations and applies them in order.  
    ///   • <see cref="PerformTransformations{T}"/> – a higher-level helper
    ///     that knows how to partition variables into <b>critical</b>
    ///     and <b>non-critical</b> dimensions and, when requested, create <i>one-at-a-time</i>
    ///     sweeps of the non-critical variables rather than exploding the search space.  
    ///   • <see cref="NameMap"/> – deduplicates output filenames so that if two different option
    ///     objects happen to describe the <i>same logical game</i> (e.g., “baseline via null branch”
    ///     versus “baseline via explicit transform”) the solver still runs only once and subsequent
    ///     code finds a unique result file.
    /// 
    /// ************************************************************************************************
    /// <para><b>Critical vs. non-critical variables</b></para>
    /// 
    /// • <i>Critical</i> variables are the handful that are central to the research question and thus
    ///   are <em>always</em> explored in full factorial combination with each other.  For these
    ///   variables you usually want to see interactions (“If risk aversion is high <i>and</i> fee
    ///   shifting is 4×, what happens?”).  
    /// 
    /// • <i>Non-critical</i> variables are supporting parameters that you would like to probe but
    ///   that would blow up the search space if fully crossed with every other non-critical.  By
    ///   default this class varies them <em>one at a time</em> against the complete critical bundle,
    ///   keeping everything else at its baseline value.  That produces a tidy set of sweeps:
    /// 
    ///   <code>
    ///   Baseline criticals only
    ///   Baseline + (NonCritA = 0.5)
    ///   Baseline + (NonCritA = 2.0)
    ///   …
    ///   Baseline + (NonCritB = 1.2)
    ///   </code>
    /// 
    ///   The switch <paramref name="useAllPermutationsOfTransformations"/> lets an advanced user
    ///   override this and request the full Cartesian product across non-criticals, but that is rare
    ///   because it grows exponentially.
    /// 
    /// ************************************************************************************************
    /// <para><b>Redundancies and the “baseline again” row</b></para>
    /// 
    /// A non-critical dimension whose <em>only</em> entry is “baseline” is logically redundant with
    /// the null branch (not applying the transform at all).  Yet we sometimes want that duplicate to
    /// exist so that data-processing code can print a table that begins with the baseline row inside
    /// the same sweep:
    /// 
    ///   <code>
    ///   CostsMultiplier   WinRate
    ///   1.0               0.63   &lt;-- explicit baseline, produced only if flag = true
    ///   4.0               0.70
    ///   16.0              0.81
    ///   </code>
    /// 
    /// Turning <paramref name="includeBaselineValueForNoncritical"/> on tells
    /// <see cref="PerformTransformations{T}"/> to keep that “baseline again” transform.  The run is
    /// <em>functionally identical</em> to the null branch but carries a different <c>.Name</c>
    /// suffix, which is the key that makes the reporting layer show it as a distinct row.  The extra
    /// solver work is avoided because <see cref="NameMap"/> resolves both verbose names to the same
    /// underlying output file.
    /// 
    /// ************************************************************************************************
    /// <para><b>Subclass responsibilities</b></para>
    /// 
    /// To plug in a concrete simulation you subclass <see cref="PermutationalLauncher"/> and supply:
    /// 
    ///   • <see cref="NamesOfVariationSets"/> – parallel to the outer list returned by
    ///     <see cref="GetVariationSets"/> so that each batch has a folder name.  
    ///   • <see cref="DefaultVariableValues"/> and <see cref="CriticalVariableValues"/> – informative
    ///     metadata that the post-processing utilities need for classification and plotting.  
    ///   • An implementation of <see cref="GetVariationSets"/> that builds the full list of
    ///     <c>Func&lt;T,T&gt;</c> lists and calls <see cref="PerformTransformations{T}"/> with the
    ///     correct factory function for “baseline options” (for example
    ///     <c>LitigGameOptionsGenerator.GetLitigGameOptions</c>).  
    /// 
    /// ************************************************************************************************
    /// <para><b>Workflow in practice</b></para>
    /// 
    /// 1.  The subclass creates a launcher, calls <see cref="AddToOptionsSets"/> and hands the
    ///     resulting list to the solver harness.  
    /// 2.  Solver writes one JSON/CSV per unique logical game.  
    /// 3.  Post-processing code enumerates <see cref="VariableInfoPerOption"/>, groups by
    ///     <see cref="GroupOptionSetsByClassification"/>, and uses <see cref="NameMap"/> to look up
    ///     the correct file for every verbose option name.  
    /// 4.  Any “baseline again” duplicates vanish silently at file lookup time but remain distinct
    ///     in memory, which keeps report tables complete and readable.
    /// 
    /// ************************************************************************************************
    /// </summary>
    public abstract class PermutationalLauncher : Launcher
    {
        #region Variation set definitions

        /// <summary>
        /// Ordered list of human-readable names used to *label the folders*
        /// that hold simulation results for each major dimension (e.g. “Risk Aversion”).
        /// Must line up with the outer list produced by <see cref="GetVariationSets"/>.
        /// </summary>
        public abstract List<string> NamesOfVariationSets { get; }

        /// <summary>
        /// Tuple list mapping each variable name to the **string form of its default value**.
        /// Used by <see cref="BuildGroupingVariableInfo"/> to decide what counts as “baseline”.
        /// </summary>
        public abstract List<(string, string)> DefaultVariableValues { get; }

        /// <summary>
        /// Supplies the set of values that count as *critical* (fully factorial)
        /// for each variable.  Downstream logic tags everything else as non-critical.
        /// </summary>
        public abstract List<(string criticalValueName, string[] criticalValueValues)> CriticalVariableValues { get; }

        /// <summary>
        /// Stable prefix (e.g. “FS036”, “APP001”) used in all report filenames
        /// so that data processors can glob them without hard-coding paths.
        /// </summary>
        public abstract string ReportPrefix { get; }

        #endregion

        #region Helper data structures and methods

        /// <summary>
        /// Identifies **one simulation run** inside a variation set.
        /// <c>columnMatches</c> holds CSV-column/value pairs needed when
        /// the post-processing code searches a cross-tab.
        /// </summary>
        public record SimulationIdentifier(
            string nameForSimulation,
            List<(string columnName, string expectedValue)> columnMatches);

        /// <summary>
        /// A *group* of <see cref="SimulationIdentifier"/> objects that
        /// belong together in a single report or graphic.
        /// </summary>
        public record SimulationSetsIdentifier(
            string nameOfSet,
            List<SimulationIdentifier> simulationIdentifiers);

        /// <summary>
        /// Normalised metadata used both for folder names and for
        /// high-level summaries about each <see cref="GameOptions"/> instance.
        /// </summary>
        public record GroupingVariableInfo(
            string VariableName,
            string Value,
            bool IsDefault,
            bool IncludeInCritical);

        /// <summary>
        /// Helper: mutates an options object via <paramref name="transform"/>,
        /// appends <paramref name="suffix"/> to its <c>Name</c>,
        /// and returns the same instance so that lambdas can be chained.
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
        /// Core Cartesian-product engine.  
        /// Takes <paramref name="transformLists"/> (each inner list is “one dimension”),
        /// enumerates every way of choosing **one transform from each list**,
        /// applies them in order to a fresh <c>T options</c>, and returns the lot.
        /// </summary>
        public List<T> ApplyPermutationsOfTransformations<T>(
            Func<T> optionsFn,
            List<List<Func<T, T>>> transformLists)
            where T : GameOptions
        {
            List<T> result = new List<T>();
            List<List<Func<T, T>>> permutationsOfTransforms =
                PermutationMaker.GetPermutationsOfItems(transformLists);

            foreach (var permutation in permutationsOfTransforms)
            {
                var options = optionsFn();
                foreach (var transform in permutation)
                {
                    options = transform(options);
                }
                result.Add(options);
            }
            return result;
        }

                /// <summary>
        /// Subclasses may override to disable all non-critical dimensions in the
        /// variation builder.  Defaults to <c>true</c> so behaviour is unchanged
        /// for existing launchers.
        /// </summary>
        protected virtual bool IncludeNonCriticalTransformations => true;

        /// <summary>
        /// General-purpose version of the “critical × (non-critical ∨ null)” sweep
        /// formerly hard-coded for litigation games.
        ///
        /// <paramref name="allTransformations"/>   Outer list = dimensions; inner list = values.  
        /// <paramref name="numCritical"/>          How many leading dimensions are “critical”.  
        /// <paramref name="useAllPermutationsOfTransformations"/> If <c>true</c>, every
        ///                                        non-critical dimension is permuted with
        ///                                        every other; otherwise dimensions are
        ///                                        varied one-at-a-time.  
        /// <paramref name="includeBaselineValueForNoncritical"/> Controls whether a
        ///                                        non-critical dimension whose *only*
        ///                                        value is the baseline is kept (duplicative)
        ///                                        or skipped.  
        /// <paramref name="optionsFn"/>           Factory that returns a fresh baseline object.
        /// </summary>
        protected List<List<GameOptions>> PerformTransformations<T>(
            List<List<Func<T, T>>> allTransformations,
            int numCritical,
            bool useAllPermutationsOfTransformations,
            bool includeBaselineValueForNoncritical,
            Func<T> optionsFn)
            where T : GameOptions
        {
            // Partition the incoming lists ------------------------------------------------
            var criticalTransforms     = allTransformations.Take(numCritical).ToList();
            var noncriticalTransforms  = allTransformations.Skip(
                IncludeNonCriticalTransformations ? numCritical : allTransformations.Count)
                .ToList();

            var resultSets = new List<List<T>>();

            // Fast path: full Cartesian product across every dimension --------------------
            if (useAllPermutationsOfTransformations)
            {
                var combined = criticalTransforms.Concat(noncriticalTransforms).ToList();
                resultSets.Add(ApplyPermutationsOfTransformations(optionsFn, combined));
            }
            else
            {
                // One-at-a-time sweeps: null branch + each non-critical list ---------------
                var sweepTargets = BuildSweepTargetList(noncriticalTransforms);

                foreach (var noncritList in sweepTargets)
                {
                    if (ShouldSkipBaseline(noncritList, includeBaselineValueForNoncritical))
                        continue;

                    var combinedLists = BuildTransformListsForSweep(
                        criticalTransforms,
                        noncritList,
                        allTransformations,
                        numCritical);

                    var options = ApplyPermutationsOfTransformations(optionsFn, combinedLists);
                    AddDefaultNoncriticalValues(options);
                    resultSets.Add(options);
                }
            }

            return resultSets.Select(inner => inner.Cast<GameOptions>().ToList()).ToList();
        }

        // -----------------------------------------------------------------------------
        //  Helpers (private)
        // -----------------------------------------------------------------------------
        private static bool ShouldSkipBaseline<T>(
            List<Func<T, T>> noncritList,
            bool includeBaselineValueForNoncritical) =>
            includeBaselineValueForNoncritical &&
            noncritList != null &&
            noncritList.Count <= 1;   // single entry means “baseline again”

        private static List<List<Func<T, T>>> BuildSweepTargetList<T>(
            List<List<Func<T, T>>> noncriticalTransforms)
        {
            var list = new List<List<Func<T, T>>> { null };        // null branch = “don’t vary”
            list.AddRange(noncriticalTransforms.Where(x => x.Count != 0));
            return list;
        }

        private List<List<Func<T, T>>> BuildTransformListsForSweep<T>(
            List<List<Func<T, T>>> criticalTransforms,
            List<Func<T, T>> chosenNoncrit,
            List<List<Func<T, T>>> allTransforms,
            int numCritical)
        {
            var lists = criticalTransforms.ToList();   // copy

            // If the chosen list is the partner of a critical dimension, replace in-place
            bool replaced = false;
            for (int i = 0; i < numCritical; i++)
            {
                var partner = allTransforms[numCritical + i];
                if (partner == chosenNoncrit)
                {
                    lists[i] = chosenNoncrit;
                    replaced = true;
                    break;
                }
            }

            // Otherwise (independent non-critical) just append
            if (chosenNoncrit != null && !replaced)
                lists.Add(chosenNoncrit);

            return lists;
        }

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
        /// Default flattener used by most subclasses – just concatenates all inner sets.
        /// Override if you need custom ordering (e.g., LitigGame sorts by variable values).
        /// </summary>
        public virtual List<GameOptions> FlattenAndOrderGameSets(
            List<List<GameOptions>> gamesSets) =>
            gamesSets.SelectMany(x => x).ToList();

        /// <summary>
        /// Subclasses must return a *list of lists* where each outer element
        /// corresponds to one of <see cref="NamesOfVariationSets"/>.
        ///
        /// <paramref name="includeBaselineValueForNoncritical"/> propagates the
        /// “allow redundancies” switch discussed earlier.
        /// </summary>
        public abstract List<List<GameOptions>> GetVariationSets(
            bool useAllPermutationsOfTransformations,
            bool includeBaselineValueForNoncritical);

        /// <summary>
        /// Convenience wrapper that **excludes** redundant baseline entries.
        /// </summary>
        public void AddToOptionsSets(List<GameOptions> options)
        {
            bool includeBaselineValueForNoncritical = false; // see comment above
            AddToOptionsSets(options, includeBaselineValueForNoncritical);
        }

        /// <summary>
        /// Adds every generated <see cref="GameOptions"/> to <paramref name="options"/>.
        /// Throws if redundancies are detected while <paramref name="allowRedundancies"/> is false.
        /// </summary>
        public void AddToOptionsSets(List<GameOptions> options, bool allowRedundancies)
        {
            var gamesSets = GetVariationSets(false, allowRedundancies); // non-critical sets
            List<GameOptions> eachGameIndependently = FlattenAndOrderGameSets(gamesSets);

            List<string> optionChoices =
                eachGameIndependently.Select(x => ToCompleteString(x.VariableSettings)).ToList();

            static string ToCompleteString<TKey, TValue>(IDictionary<TKey, TValue> dictionary) =>
                "{" + string.Join(",", dictionary.Select(kv => kv.Key + "=" + kv.Value)) + "}";

            if (!allowRedundancies &&
                optionChoices.Distinct().Count() != optionChoices.Count())
            {
                // Guardrail: developer turned off redundancies but
                // duplicate (variable-setting) rows were still produced.
                throw new Exception("redundancies found");
            }

            options.AddRange(eachGameIndependently);
        }

        /// <summary>
        /// Convenience projection: for every option set, return the list of
        /// <see cref="GroupingVariableInfo"/> items that describe it.
        /// </summary>
        public List<(string OptionSetName, List<GroupingVariableInfo> Variables)>
            VariableInfoPerOption
        {
            get
            {
                var list = new List<(string, List<GroupingVariableInfo>)>();
                var defaultValues =
                    DefaultVariableValues.ToDictionary(x => x.Item1, x => x.Item2.ToString());
                var criticalVars =
                    new HashSet<string>(CriticalVariableValues.Select(x => x.criticalValueName));

                foreach (var opt in GetOptionsSets())
                    list.Add((opt.Name,
                              BuildGroupingVariableInfo(opt, defaultValues, criticalVars)));

                return list;
            }
        }

        /// <summary>
        /// Maps every *verbose* option-set name (possibly redundant baseline)
        /// to the canonical **non-redundant** name that actually appears on disk.
        /// The algorithm repeatedly strips the last suffix until a match is found.  
        /// This prevents solver re-runs yet still lets reports show the
        /// “explicit baseline” rows when desired.
        /// </summary>
        public Dictionary<string, string> NameMap
        {
            get
            {
                List<GameOptions> withRedundancies = new List<GameOptions>();
                AddToOptionsSets(withRedundancies, true);

                List<GameOptions> withoutRedundancies = new List<GameOptions>();
                AddToOptionsSets(withoutRedundancies, false);

                Dictionary<string, string> result = new();

                foreach (var gameOptions in withRedundancies)
                {
                    string runAsName = gameOptions.Name;
                    while (!withoutRedundancies.Any(x => x.Name == runAsName))
                    {
                        var lastIndex = runAsName.LastIndexOf(' ');
                        runAsName = runAsName.Substring(0, lastIndex);
                    }
                    result[gameOptions.Name] = runAsName;
                }
                return result;
            }
        }

        /// <summary>
        /// Builds the per-variable metadata used elsewhere for grouping
        /// and for building folder names.
        /// </summary>
        public static List<GroupingVariableInfo> BuildGroupingVariableInfo(
            GameOptions option,
            Dictionary<string, string> defaultValues,
            HashSet<string> criticalVariables)
        {
            static string Normalize(object o) =>
                o is double d
                    ? d.ToString("G", System.Globalization.CultureInfo.InvariantCulture)
                    : o?.ToString().Trim();

            var result = new List<GroupingVariableInfo>();

            foreach (var (key, val) in option.VariableSettings)
            {
                string normalizedVal = Normalize(val);
                bool isCritical = criticalVariables.Contains(key);
                bool isDefault =
                    defaultValues.TryGetValue(key, out var def) &&
                    normalizedVal == Normalize(def);

                result.Add(new GroupingVariableInfo(
                    key,
                    normalizedVal,
                    isDefault,
                    isCritical));
            }

            return result;
        }

        /// <summary>
        /// Returns a short human-readable label (“Baseline”, “Fee Shift = 2.0”, …)
        /// describing how an option set differs from the global default.
        /// </summary>
        public static string ClassifyOptionSet(List<GroupingVariableInfo> variables)
        {
            var criticals     = variables.Where(x => x.IncludeInCritical).ToList();
            var noncriticals  = variables.Where(x => !x.IncludeInCritical).ToList();

            var noncritDiffs  = noncriticals.Where(x => !x.IsDefault).ToList();
            var critDiffs     = criticals.Where(x => !x.IsDefault).ToList();

            // If more than one variable differs, join them with “ & ”
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
        /// Groups option-set names into folders for the “Individual Simulation Results”
        /// tree that post-processing code creates.
        /// </summary>
        public Dictionary<string, List<string>> GroupOptionSetsByClassification()
        {
            // ---------- helpers ----------
            static string Normalize(object o) =>
                o is double d
                    ? d.ToString("G", System.Globalization.CultureInfo.InvariantCulture)
                    : o?.ToString().Trim();

            // Defaults   variableName → defaultValue
            var defaultDict = DefaultVariableValues
                .ToDictionary(p => p.Item1, p => Normalize(p.Item2));

            // Critical values   variableName → { criticalValue1, … }
            var criticalValueDict = CriticalVariableValues
                .ToDictionary(
                    cv => cv.criticalValueName,
                    cv => cv.criticalValueValues.Select(Normalize).ToHashSet());

            // ---------- main loop ----------
            var result = new Dictionary<string, List<string>>();

            foreach (var (optionSetName, vars) in VariableInfoPerOption)
            {
                bool qualifiesForBaseline = true;
                var folderParts = new List<string>();

                foreach (var v in vars)
                {
                    // Skip defaults
                    if (v.IsDefault)
                        continue;

                    bool isCritVar   = v.IncludeInCritical;
                    bool isCritValue = isCritVar &&
                                       criticalValueDict[v.VariableName].Contains(v.Value);

                    if (isCritVar && isCritValue)
                    {
                        // Critical variable but **at** a critical value → still baseline
                        continue;
                    }

                    qualifiesForBaseline = false;

                    string part = isCritVar
                        ? $"Additional {v.VariableName} = {v.Value}"
                        : $"{v.VariableName} = {v.Value}";

                    folderParts.Add(part);
                }

                string groupName = qualifiesForBaseline
                    ? "Baseline"
                    : string.Join(" & ", folderParts.OrderBy(p => p));

                if (!result.TryGetValue(groupName, out var list))
                    result[groupName] = list = new List<string>();

                list.Add(optionSetName);
            }

            return result;
        }
    }
}
