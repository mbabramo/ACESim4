using ACESim;
using ACESimBase.Util.Collections;
using ACESimBase.Util.Debugging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESimBase.GameSolvingSupport.Settings
{
    public abstract class PermutationalLauncher : Launcher
    {

        #region Variation set definitions

        // Names of variation sets for grouping scenarios in reports (e.g., "Risk Aversion")
        public abstract List<string> NamesOfVariationSets { get; }

        // For each named variation set, a string representation of the default value of the variation.
        public abstract List<(string, string)> DefaultVariableValues { get; }

        // For each critical variable name, a list of tuples containing the critical variable name and its values.
        // Noncritical variables should not be included.
        public abstract List<(string criticalValueName, string[] criticalValueValues)> CriticalVariableValues { get; }

        // Article variation metadata: list of variation info sets for grouping scenarios in reports.
        public abstract List<ArticleVariationInfoSets> VariationInfoSets { get; }

        // Stable report prefix used for output files (e.g., "FS036" or "APP001").
        public abstract string ReportPrefix { get; }

        #endregion

        #region Helper data structures and methods

        public record ArticleVariationInfo(string nameOfVariation, List<(string columnName, string expectedValue)> columnMatches);

        public record ArticleVariationInfoSets(string nameOfSet, List<ArticleVariationInfo> requirementsForEachVariation);

        public record GroupingVariableInfo(
            string VariableName,
            string Value,
            bool IsDefault,
            bool IncludeInCritical
        );

        public T GetAndTransform<T>(T options, string suffix, Action<T> transform) where T : GameOptions
        {
            T g = options;
            transform(g);
            g.Name = g.Name + suffix;
            return g;
        }

        public List<T> ApplyPermutationsOfTransformations<T>(Func<T> optionsFn, List<List<Func<T, T>>> transformLists) where T : GameOptions
        {
            List<T> result = new List<T>();
            List<List<Func<T, T>>> permutationsOfTransforms = PermutationMaker.GetPermutationsOfItems(transformLists);
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

        #endregion

        public virtual List<GameOptions> FlattenAndOrderGameSets(List<List<GameOptions>> gamesSets)
        {
            return gamesSets.SelectMany(x => x).ToList();
        }

        public abstract List<List<GameOptions>> GetSetsOfGameOptions(bool useAllPermutationsOfTransformations, bool includeBaselineValueForNoncritical);

        public void GetGameOptions(List<GameOptions> options)
        {
            bool includeBaselineValueForNoncritical = false; // By setting this to false, we avoid repeating the baseline value for noncritical transformations, which would produce redundant options sets.
            GetGameOptions(options, includeBaselineValueForNoncritical);
        }

        public void GetGameOptions(List<GameOptions> options, bool allowRedundancies)
        {
            var gamesSets = GetSetsOfGameOptions(false, allowRedundancies); // each is a set with noncritical
            List<GameOptions> eachGameIndependently = FlattenAndOrderGameSets(gamesSets);

            List<string> optionChoices = eachGameIndependently.Select(x => ToCompleteString(x.VariableSettings)).ToList();
            static string ToCompleteString<TKey, TValue>(IDictionary<TKey, TValue> dictionary)
            {
                return "{" + string.Join(",", dictionary.Select(kv => kv.Key + "=" + kv.Value).ToArray()) + "}";
            }
            if (!allowRedundancies && optionChoices.Distinct().Count() != optionChoices.Count())
            {
                var redundancies = optionChoices.Where(x => optionChoices.Count(y => x == y) > 1).Select(x => (x, optionChoices.Count(y => x == y), optionChoices.Select((item, index) => (item, index)).Where(z => z.item == x).Select(z => z.index).ToList())).ToList();
                throw new Exception("redundancies found");
            }

            options.AddRange(eachGameIndependently);
        }

        public List<(string OptionSetName, List<GroupingVariableInfo> Variables)> VariableInfoPerOption
        {
            get
            {
                var list = new List<(string OptionSetName, List<GroupingVariableInfo> Variables)>();
                var defaultValues = DefaultVariableValues.ToDictionary(x => x.Item1, x => x.Item2.ToString());
                var criticalVars = new HashSet<string>(CriticalVariableValues.Select(x => x.criticalValueName));

                foreach (var opt in GetOptionsSets())
                    list.Add((opt.Name, BuildGroupingVariableInfo(opt, defaultValues, criticalVars)));

                return list;
            }
        }

        /// <summary>
        /// Return the name that a set of options was run under -- taking into account that we avoid repeating redundant options sets.
        /// </summary>
        /// <returns></returns>
        public Dictionary<string, string> NameMap
        {
            get
            {
                List<GameOptions> withRedundancies = new List<GameOptions>();
                GetGameOptions(withRedundancies, true);
                List<GameOptions> withoutRedundancies = new List<GameOptions>();
                GetGameOptions(withoutRedundancies, false);
                Dictionary<string, string> result = new Dictionary<string, string>();
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

        public static List<GroupingVariableInfo> BuildGroupingVariableInfo(
            GameOptions option,
            Dictionary<string, string> defaultValues,
            HashSet<string> criticalVariables)
        {
            static string Normalize(object o) =>
                o is double d ? d.ToString("G", System.Globalization.CultureInfo.InvariantCulture) : o?.ToString().Trim();

            var result = new List<GroupingVariableInfo>();

            foreach (var (key, val) in option.VariableSettings)
            {
                string normalizedVal = Normalize(val);
                bool isCritical = criticalVariables.Contains(key);
                bool isDefault = defaultValues.TryGetValue(key, out var defaultVal)
                                 && normalizedVal == Normalize(defaultVal);

                result.Add(new GroupingVariableInfo(
                    key,
                    normalizedVal,
                    isDefault,
                    isCritical));
            }

            return result;
        }

        public static string ClassifyOptionSet(List<GroupingVariableInfo> variables)
        {
            var criticals = variables.Where(x => x.IncludeInCritical).ToList();
            var noncriticals = variables.Where(x => !x.IncludeInCritical).ToList();

            var noncritDiffs = noncriticals.Where(x => !x.IsDefault).ToList();
            var critDiffs = criticals.Where(x => !x.IsDefault).ToList();

            // Handle multiple variations by combining keys
            if (noncritDiffs.Count + critDiffs.Count > 1)
            {
                var parts = noncritDiffs
                    .Select(v => $"{v.VariableName} = {v.Value}");
                return string.Join(" & ", parts);
            }

            // Existing logic for single-difference cases
            if (noncritDiffs.Count == 1)
                return $"{noncritDiffs[0].VariableName} = {noncritDiffs[0].Value}";
            if (critDiffs.Count == 1)
                return $"Additional {critDiffs[0].VariableName} = {critDiffs[0].Value}";
            return "Baseline";
        }
        public Dictionary<string, List<string>> GroupOptionSetsByClassification()
        {
            // ---------- helpers ----------
            static string Normalize(object o) =>
                o is double d
                    ? d.ToString("G", System.Globalization.CultureInfo.InvariantCulture)
                    : o?.ToString().Trim();

            // Defaults for every variable
            var defaultDict = DefaultVariableValues
                .ToDictionary(p => p.Item1, p => Normalize(p.Item2));

            // Critical-value lookup:  varName → { criticalValue1, … }
            var criticalValueDict = CriticalVariableValues
                .ToDictionary(
                    cv => cv.criticalValueName,
                    cv => cv.criticalValueValues
                             .Select(Normalize)
                             .ToHashSet()
                );

            // ---------- main loop ----------
            var result = new Dictionary<string, List<string>>();

            foreach (var (optionSetName, vars) in VariableInfoPerOption)
            {
                bool qualifiesForBaseline = true;
                var folderParts = new List<string>();

                foreach (var v in vars)
                {
                    // Skip variables that are identical to their default value
                    if (v.IsDefault)
                        continue;

                    bool isCritVar = v.IncludeInCritical;
                    bool isCritValue = isCritVar &&
                                        criticalValueDict[v.VariableName].Contains(v.Value);

                    if (isCritVar && isCritValue)
                    {
                        // Still baseline-eligible – critical variable at a critical value
                        continue;
                    }

                    // Not baseline any more – create the appropriate folder part
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
