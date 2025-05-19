using ACESim.Util;
using ACESimBase;
using ACESimBase.GameSolvingSupport;
using ACESimBase.Util.Collections;
using ACESimBase.Util.Mathematics;
using JetBrains.Annotations;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ACESim
{
    public class LitigGameCorrelatedSignalsArticleLauncher : Launcher, IFeeShiftingLauncher
    {
        public List<GameOptions> AllGameOptions => GetOptionsSets();
        public Dictionary<string, string> NameMap => GetFeeShiftingArticleNameMap();
        public List<ArticleVariationInfoSets> VariationInfoSets
            => GetArticleVariationInfoList(false);
        public string ReportPrefix => MasterReportNameForDistributedProcessing;

        public override string MasterReportNameForDistributedProcessing => "FS036";

        // We can use this to allow for multiple options sets. These can then run in parallel. But note that we can also have multiple runs with a single option set using different settings by using GameDefinition scenarios; this is useful when there is a long initialization and it makes sense to complete one set before starting the next set.

        private bool HigherRiskAversion = false;
        private bool PRiskAverse = false;
        public bool DRiskAverse = false;
        public bool TestDisputeGeneratorVariations = false;
        public bool IncludeRunningSideBetVariations = false;
        public bool LimitToAmerican = true;
        public bool UseSmallerTree = false; // DEBUG

        // Fee shifting article
        public bool IncludeNonCriticalTransformations = true;
        public FeeShiftingRule[] FeeShiftingModes = new[] { FeeShiftingRule.English_LiabilityIssue, FeeShiftingRule.MarginOfVictory_LiabilityIssue, FeeShiftingRule.Rule68_DamagesIssue, FeeShiftingRule.Rule68English_DamagesIssue };
        public double[] CriticalCostsMultipliers = new double[] { 1.0, 0.25, 0.5, 2.0, 4.0 };
        public double[] AdditionalCostsMultipliers = new double[] { 1.0, 0.125, 8.0 }; // NOTE: If restoring this, also change NamesOfFeeShiftingArticleSets
        public (double pNoiseMultiplier, double dNoiseMultiplier)[] NoiseMultipliers = new (double pNoiseMultiplier, double dNoiseMultiplier)[] { (1.0, 1.0), (0.50, 0.50), (0.5, 2.0), (2.0, 2.0), (2.0, 0.5), (0.25, 0.25), (4.0, 4.0) };
        public double[] CriticalFeeShiftingMultipliers = new double[] { 0.0, 1.0, 0.5, 1.5, 2.0 };
        public double[] AdditionalFeeShiftingMultipliers = new double[] { 0.0 }; // added all but 0 //, 0.25, 4.0 };// NOTE: If restoring this, also change NamesOfFeeShiftingArticleSets
        public double[] RelativeCostsMultipliers = new double[] { 1.0, 0.5, 2.0 };
        public double[] ProbabilitiesTrulyLiable = new double[] { 0.5, 0.1, 0.9 };
        public double[] StdevsNoiseToProduceLiabilityStrength = new double[] { 0.35, 0.175, 0.70 };
        public double[] ProportionOfCostsAtBeginning = new double[] { 0.5, 0.75, 0.25, 1.0, 0.0 };



        public List<(string criticalValueName, string[] criticalValueValues)> CriticalVariableValues
        {
            get
            {
                return new List<(string, string[])>()
                {
                    ("Costs Multiplier", CriticalCostsMultipliers.Select(x => x.ToString()).ToArray()),
                    ("Fee Shifting Multiplier", CriticalFeeShiftingMultipliers.Select(x => x.ToString()).ToArray()),
                    ("Risk Aversion", new[] { "Risk Neutral", "Moderately Risk Averse" }),
                };
            }
        }

        public enum FeeShiftingRule
        {
            English_LiabilityIssue,
            MarginOfVictory_LiabilityIssue,
            English_DamagesIssue,
            Rule68_DamagesIssue,
            Rule68English_DamagesIssue,
        }

        public enum RiskAversionMode
        {
            RiskNeutral,
            RiskAverse,
            VeryRiskAverse,
            POnlyRiskAverse,
            DOnlyRiskAverse,
            PMoreRiskAverse,
            DMoreRiskAverse,
        }

        public override GameDefinition GetGameDefinition() => new LitigGameDefinition();

        private enum OptionSetChoice
        {
            JustOneOption,
            Fast,
            ShootoutPermutations,
            VariousUncertainties,
            VariedAlgorithms,
            Custom2,
            ShootoutGameVariations,
            BluffingVariations,
            KlermanEtAl,
            KlermanEtAl_MultipleStrengthPoints,
            KlermanEtAl_Options,
            KlermanEtAl_DamagesUncertainty,
            Simple1BR,
            Simple2BR,
            FeeShiftingArticle,
            FeeShiftingArticleBaselineOnly,
        }
        OptionSetChoice OptionSetChosen = OptionSetChoice.FeeShiftingArticle;  // <<-- Choose option set here


        public IEnumerable<(string OptionSetName, List<GroupingVariableInfo> Variables)> GetVariableInfoPerOption()
        {
            var defaultValues = DefaultVariableValues.ToDictionary(x => x.Item1, x => x.Item2.ToString());
            var criticalVars = new HashSet<string> { "Costs Multiplier", "Fee Shifting Multiplier", "Risk Aversion" };

            foreach (var opt in AllGameOptions)
                yield return (opt.Name, Launcher.BuildGroupingVariableInfo(opt, defaultValues, criticalVars));
        }

        public override GameOptions GetDefaultSingleGameOptions()
        {
            return LitigGameOptionsGenerator.GetLitigGameOptions();
        }

        public override List<GameOptions> GetOptionsSets()
        {
            List<GameOptions> optionSets = new List<GameOptions>();

            switch (OptionSetChosen)
            {
                case OptionSetChoice.FeeShiftingArticle:
                    AddFeeShiftingArticleGames(optionSets);
                    break;
                default: throw new NotImplementedException();

            }

            optionSets = optionSets.OrderBy(x => x.Name).ToList();

            bool simplify = false; // Enable for debugging purposes to speed up execution without going all the way to "fast" option
            if (simplify)
                foreach (var optionSet in optionSets)
                    optionSet.Simplify();

            if (LimitToTaskIDs != null)
            {
                List<GameOptions> replacements = new List<GameOptions>();
                foreach (int idToKeep in LimitToTaskIDs)
                    replacements.Add(optionSets[idToKeep]);
                optionSets = replacements;
            }

            var optionSetNames = optionSets.Select(x => x.Name).OrderBy(x => x).Distinct().ToList();

            return optionSets;
        }

        #region Fee shifting article

        public void AddFeeShiftingArticleGames(List<GameOptions> options)
        {
            bool includeBaselineValueForNoncritical = false; // By setting this to false, we avoid repeating the baseline value for noncritical transformations, which would produce redundant options sets.
            GetFeeShiftingArticleGames(options, includeBaselineValueForNoncritical);
            if (UseSmallerTree)
            {
                foreach (var option in options)
                {
                    // Note -- some of this is redundant, but we need to do this because of transformations to the original options that may affect damages and liability strength points.
                    LitigGameOptions o = (LitigGameOptions)option;
                    const int bigTreeNumber = 10;
                    const int littleTreeNumber = 5;
                    if (o.NumLiabilitySignals == bigTreeNumber)
                        o.NumLiabilitySignals = littleTreeNumber;
                    if (o.NumLiabilityStrengthPoints == bigTreeNumber)
                        o.NumLiabilityStrengthPoints = littleTreeNumber;
                    if (o.NumDamagesSignals == bigTreeNumber)
                        o.NumDamagesSignals = littleTreeNumber;
                    if (o.NumDamagesStrengthPoints == bigTreeNumber)
                        o.NumDamagesStrengthPoints = littleTreeNumber;
                    if (o.NumOffers == bigTreeNumber)
                        o.NumOffers = littleTreeNumber;
                }
            }
        }

        /// <summary>
        /// Return the name that a set of fee-shifting article options was run under -- taking into account that we avoid repeating redundant options sets.
        /// </summary>
        /// <returns></returns>
        public Dictionary<string, string> GetFeeShiftingArticleNameMap()
        {
            List<GameOptions> withRedundancies = new List<GameOptions>();
            GetFeeShiftingArticleGames(withRedundancies, true);
            List<GameOptions> withoutRedundancies = new List<GameOptions>();
            GetFeeShiftingArticleGames(withoutRedundancies, false);
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

        public void GetFeeShiftingArticleGames(List<GameOptions> options, bool allowRedundancies)
        {
            var gamesSets = GetFeeShiftingArticleGamesSets(false, allowRedundancies); // each is a set with noncritical
            var eachGameIndependently = gamesSets.SelectMany(x => x).ToList();

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

        public List<List<LitigGameOptions>> GetFeeShiftingArticleBaselineGamesSets(bool smallerTree)
        {
            List<List<LitigGameOptions>> result = new List<List<LitigGameOptions>>();
            List<List<Func<LitigGameOptions, LitigGameOptions>>> allTransformations = new List<List<Func<LitigGameOptions, LitigGameOptions>>>()
            {
                EssentialFeeShiftingMultiplierTransformations(),
            };
            List<LitigGameOptions> gameOptions = ApplyPermutationsOfTransformations(() => (LitigGameOptions)LitigGameOptionsGenerator.FeeShiftingBase(smallerTree).WithName("FSA"), allTransformations);
            result.Add(gameOptions);
            return result;
        }

        public List<List<LitigGameOptions>> GetFeeShiftingArticleGamesSets(bool useAllPermutationsOfTransformations, bool includeBaselineValueForNoncritical)
        {
            List<List<LitigGameOptions>> result = new List<List<LitigGameOptions>>();
            const int numCritical = 3; // critical transformations are all interacted with one another and then with each of the other transformations
            var criticalCostsMultiplierTransformations = CriticalCostsMultiplierTransformations(true);
            var noncriticalCostsMultiplierTransformations = AdditionalCostsMultiplierTransformations(includeBaselineValueForNoncritical);
            var criticalFeeShiftingMultipleTransformations = CriticalFeeShiftingMultiplierTransformations(true);
            var noncriticalFeeShiftingMultipleTransformations = AdditionalFeeShiftingMultiplierTransformations(includeBaselineValueForNoncritical);
            var criticalRiskAversionTransformations = CriticalRiskAversionTransformations(true);
            var noncriticalRiskAversionTransformations = AdditionalRiskAversionTransformations(includeBaselineValueForNoncritical);
            List<List<Func<LitigGameOptions, LitigGameOptions>>> allTransformations = new List<List<Func<LitigGameOptions, LitigGameOptions>>>()
            {
                // Can always choose any of these:
                criticalCostsMultiplierTransformations,
                criticalFeeShiftingMultipleTransformations,
                criticalRiskAversionTransformations,
                // And then can vary ONE of these (avoiding inconsistencies with above):
                // IMPORTANT: When changing these, change NamesOfFeeShiftingArticleSets to match each of these
                noncriticalCostsMultiplierTransformations, // i.e., not only the core costs multipliers and other core variables, but also the critical costs multipliers
                noncriticalFeeShiftingMultipleTransformations,
                noncriticalRiskAversionTransformations,
                NoiseTransformations(includeBaselineValueForNoncritical),
                PRelativeCostsTransformations(includeBaselineValueForNoncritical),
                FeeShiftingModeTransformations(includeBaselineValueForNoncritical),
                AllowAbandonAndDefaultsTransformations(includeBaselineValueForNoncritical),
                ProbabilityTrulyLiableTransformations(includeBaselineValueForNoncritical),
                NoiseToProduceCaseStrengthTransformations(includeBaselineValueForNoncritical),
                LiabilityVsDamagesTransformations(includeBaselineValueForNoncritical),
                ProportionOfCostsAtBeginningTransformations(includeBaselineValueForNoncritical),
            };
            List<List<Func<LitigGameOptions, LitigGameOptions>>> criticalTransformations = allTransformations.Take(numCritical).ToList();
            List<List<Func<LitigGameOptions, LitigGameOptions>>> noncriticalTransformations = allTransformations.Skip(IncludeNonCriticalTransformations ? numCritical : allTransformations.Count()).ToList();
            List<LitigGameOptions> gameOptions = new List<LitigGameOptions>(); // ApplyPermutationsOfTransformations(() => (LitigGameOptions) LitigGameOptionsGenerator.FeeShiftingArticleBase().WithName("FSA"), transformations);
            if (!useAllPermutationsOfTransformations)
            {
                var noncriticalTransformationPlusNoTransformation = new List<List<Func<LitigGameOptions, LitigGameOptions>>>();
                noncriticalTransformationPlusNoTransformation.AddRange(noncriticalTransformations.Where(x => x.Count() != 0));
                noncriticalTransformationPlusNoTransformation.Insert(0, null);
                // We still want the non-critical transformations, just not permuted with the others.
                for (int noncriticalIndex = 0; noncriticalIndex < noncriticalTransformationPlusNoTransformation.Count; noncriticalIndex++)
                {
                    List<Func<LitigGameOptions, LitigGameOptions>> noncriticalTransformation = noncriticalTransformationPlusNoTransformation[noncriticalIndex];
                    if (includeBaselineValueForNoncritical && noncriticalTransformation != null && noncriticalTransformation.Count() <= 1)
                        continue; // if there is only 1 entry, that will be the baseline, and thus there is no transformation here, so there is nothing to add. But we keep the null case, because that is the case for just keeping the baseline critical transformations.
                    List<List<Func<LitigGameOptions, LitigGameOptions>>> transformLists = criticalTransformations.ToList();
                    bool replaced = false;
                    foreach ((List<Func<LitigGameOptions, LitigGameOptions>> noncritical, List<Func<LitigGameOptions, LitigGameOptions>> critical) in new (List<Func<LitigGameOptions, LitigGameOptions>> noncritical, List<Func<LitigGameOptions, LitigGameOptions>> critical)[] { (noncriticalCostsMultiplierTransformations, criticalCostsMultiplierTransformations), (noncriticalFeeShiftingMultipleTransformations, criticalFeeShiftingMultipleTransformations), (noncriticalRiskAversionTransformations, criticalRiskAversionTransformations) })
                    {
                        if (noncriticalTransformation == noncritical)
                        {
                            // Keep the order the same for naming purposes
                            int indexOfCritical = transformLists.IndexOf(critical);
                            transformLists[indexOfCritical] = noncriticalTransformation;
                            replaced = true;
                        }
                    }
                    if (noncriticalTransformation != null && !replaced)
                        transformLists.Add(noncriticalTransformation);
                    List<LitigGameOptions> noncriticalOptions = ApplyPermutationsOfTransformations(() => (LitigGameOptions)LitigGameOptionsGenerator.FeeShiftingBase(UseSmallerTree).WithName("FSA"), transformLists);
                    List<(string, string)> defaultNonCriticalValues = DefaultVariableValues;
                    foreach (var optionSet in noncriticalOptions)
                    {
                        foreach (var defaultPair in defaultNonCriticalValues)
                            if (!optionSet.VariableSettings.ContainsKey(defaultPair.Item1))
                                optionSet.VariableSettings[defaultPair.Item1] = defaultPair.Item2;
                    }

                    //var optionSetNames = noncriticalOptions.Select(x => x.Name).OrderBy(x => x).ToList();
                    result.Add(noncriticalOptions);
                }
            }
            return result;
        }

        public List<(string, string)> DefaultVariableValues
        {
            get
            {
                return new List<(string, string)>()
                {
                    ("Costs Multiplier", "1"),
                    ("Fee Shifting Multiplier", "0"),
                    ("Risk Aversion", "Risk Neutral"),
                    ("Fee Shifting Rule", "English"),
                    ("Relative Costs", "1"),
                    ("Noise Multiplier P", "1"),
                    ("Noise Multiplier D", "1"),
                    ("Allow Abandon and Defaults", "true"),
                    ("Probability Truly Liable", "0.5"),
                    ("Noise to Produce Case Strength", "0.35"),
                    ("Issue", "Liability"),
                    ("Proportion of Costs at Beginning", "0.5"),
                };
            }
        }

        public List<string> NamesOfVariationSets => new List<string>()
        {
           // "Additional Costs Multipliers",
           // "Additional Fee Shifting Multipliers",
           // "Additional Risk Options",
            "Costs Multipliers",
            "Fee Shifting Multiples",
            "Risk Aversion",
            "Noise Multipliers", // includes P & D
            "Relative Costs",
            "Fee Shifting Mode",
            "Allowing Abandon and Defaults",
            "Probability Truly Liable",
            "Noise to Produce Case Strength",
            "Liability vs Damages",
            "Proportion of Costs at Beginning",
        };

        public List<ArticleVariationInfoSets> GetArticleVariationInfoList(bool useRiskAversionForNonRiskReports)
        {
            var varyingNothing = new List<ArticleVariationInfo>()
            {
                new ArticleVariationInfo("Baseline", DefaultVariableValues),
            };

            var varyingFeeShiftingRule_LiabilityUncertain = new List<ArticleVariationInfo>()
            {
                // where liability is uncertain:
                new ArticleVariationInfo("English", DefaultVariableValues),
                new ArticleVariationInfo("Margin of Victory", DefaultVariableValues.WithReplacement("Fee Shifting Rule", "Margin of Victory")),
            };

            var varyingFeeShiftingRule_DamagesUncertain = new List<ArticleVariationInfo>()
            {
                // where liability is uncertain:
                new ArticleVariationInfo("English", DefaultVariableValues.WithReplacement("Issue", "Damages")),
                new ArticleVariationInfo("Rule 68", DefaultVariableValues.WithReplacement("Fee Shifting Rule", "Rule 68").WithReplacement("Issue", "Damages")),
                new ArticleVariationInfo("Reverse 68", DefaultVariableValues.WithReplacement("Fee Shifting Rule", "Reverse 68").WithReplacement("Issue", "Damages")),
            };

            var varyingNoiseMultipliersBoth = new List<ArticleVariationInfo>()
            {
                new ArticleVariationInfo(".25", DefaultVariableValues.WithReplacement("Noise Multiplier P", "0.25").WithReplacement("Noise Multiplier D", "0.25")),
                new ArticleVariationInfo(".5", DefaultVariableValues.WithReplacement("Noise Multiplier P", "0.5").WithReplacement("Noise Multiplier D", "0.5")),
                new ArticleVariationInfo("1", DefaultVariableValues.WithReplacement("Noise Multiplier P", "1").WithReplacement("Noise Multiplier D", "1")),
                new ArticleVariationInfo("2", DefaultVariableValues.WithReplacement("Noise Multiplier P", "2").WithReplacement("Noise Multiplier D", "2")),
                new ArticleVariationInfo("4", DefaultVariableValues.WithReplacement("Noise Multiplier P", "4").WithReplacement("Noise Multiplier D", "4")),
            };

            var varyingNoiseMultipliersAsymmetric = new List<ArticleVariationInfo>()
            {
                new ArticleVariationInfo("Equal Information", DefaultVariableValues.WithReplacement("Noise Multiplier P", "1").WithReplacement("Noise Multiplier D", "1")),
                new ArticleVariationInfo("P Better", DefaultVariableValues.WithReplacement("Noise Multiplier P", "0.5").WithReplacement("Noise Multiplier D", "2")),
                new ArticleVariationInfo("D Better", DefaultVariableValues.WithReplacement("Noise Multiplier P", "2").WithReplacement("Noise Multiplier D", "0.5")),
            };

            var varyingRelativeCosts = new List<ArticleVariationInfo>()
            {
                new ArticleVariationInfo("P Lower Costs", DefaultVariableValues.WithReplacement("Relative Costs", "0.5")),
                new ArticleVariationInfo("Equal", DefaultVariableValues.WithReplacement("Relative Costs", "1")),
                new ArticleVariationInfo("P Higher Costs", DefaultVariableValues.WithReplacement("Relative Costs", "2")),
            };

            var varyingRiskAversion = new List<ArticleVariationInfo>()
            {
                new ArticleVariationInfo("Risk Neutral", DefaultVariableValues.WithReplacement("Risk Aversion", "Risk Neutral")),
                new ArticleVariationInfo("Mildly Averse", DefaultVariableValues.WithReplacement("Risk Aversion", "Mildly Risk Averse")),
                new ArticleVariationInfo("Moderately Averse", DefaultVariableValues.WithReplacement("Risk Aversion", "Moderately Risk Averse")),
                new ArticleVariationInfo("Highly Averse", DefaultVariableValues.WithReplacement("Risk Aversion", "Highly Risk Averse")),
            };

            var varyingRiskAversionAsymmetry = new List<ArticleVariationInfo>()
            {
                new ArticleVariationInfo("P Risk Averse", DefaultVariableValues.WithReplacement("Risk Aversion", "P Risk Averse")),
                new ArticleVariationInfo("D Risk Averse", DefaultVariableValues.WithReplacement("Risk Aversion", "D Risk Averse")),
                new ArticleVariationInfo("P More Risk Averse", DefaultVariableValues.WithReplacement("Risk Aversion", "P More Risk Averse")),
                new ArticleVariationInfo("D More Risk Averse", DefaultVariableValues.WithReplacement("Risk Aversion", "D More Risk Averse")),
            };

            var varyingQuitRules = new List<ArticleVariationInfo>()
            {
                new ArticleVariationInfo("Quitting Allowed", DefaultVariableValues.WithReplacement("Allow Abandon and Defaults", "TRUE")),
                new ArticleVariationInfo("Quitting Prohibited", DefaultVariableValues.WithReplacement("Allow Abandon and Defaults", "FALSE")),
            };

            var varyingProbabilityTrulyLiable = new List<ArticleVariationInfo>()
            {
                new ArticleVariationInfo("0.1", DefaultVariableValues.WithReplacement("Probability Truly Liable", "0.1")),
                new ArticleVariationInfo("0.5", DefaultVariableValues.WithReplacement("Probability Truly Liable", "0.5")),
                new ArticleVariationInfo("0.9", DefaultVariableValues.WithReplacement("Probability Truly Liable", "0.9")),
            };

            var varyingNoiseToProduceCaseStrength = new List<ArticleVariationInfo>()
            {
                new ArticleVariationInfo("0.175", DefaultVariableValues.WithReplacement("Noise to Produce Case Strength", "0.175")),
                new ArticleVariationInfo("0.35", DefaultVariableValues.WithReplacement("Noise to Produce Case Strength", "0.35")),
                new ArticleVariationInfo("0.70", DefaultVariableValues.WithReplacement("Noise to Produce Case Strength", "0.7")),
            };

            var varyingIssue = new List<ArticleVariationInfo>()
            {
                new ArticleVariationInfo("Liability", DefaultVariableValues.WithReplacement("Issue", "Liability")),
                new ArticleVariationInfo("Damages", DefaultVariableValues.WithReplacement("Issue", "Damages")),
            };

            var varyingTimingOfCosts = new List<ArticleVariationInfo>()
            {
                new ArticleVariationInfo("0", DefaultVariableValues.WithReplacement("Proportion of Costs at Beginning", "0")),
                new ArticleVariationInfo("0.25", DefaultVariableValues.WithReplacement("Proportion of Costs at Beginning", "0.25")),
                new ArticleVariationInfo("0.5", DefaultVariableValues.WithReplacement("Proportion of Costs at Beginning", "0.5")),
                new ArticleVariationInfo("0.75", DefaultVariableValues.WithReplacement("Proportion of Costs at Beginning", "0.75")),
                new ArticleVariationInfo("1", DefaultVariableValues.WithReplacement("Proportion of Costs at Beginning", "1")),
            };

            var tentativeResults = new List<ArticleVariationInfoSets>()
            {
                new ArticleVariationInfoSets("Baseline", varyingNothing),
                new ArticleVariationInfoSets("Fee Shifting Rule (Liability Issue)", varyingFeeShiftingRule_LiabilityUncertain),
                new ArticleVariationInfoSets("Fee Shifting Rule (Damages Issue)", varyingFeeShiftingRule_DamagesUncertain),
                new ArticleVariationInfoSets("Noise Multiplier", varyingNoiseMultipliersBoth),
                new ArticleVariationInfoSets("Information Asymmetry", varyingNoiseMultipliersAsymmetric),
                new ArticleVariationInfoSets("Relative Costs", varyingRelativeCosts),
                new ArticleVariationInfoSets("Risk Aversion", varyingRiskAversion),
                new ArticleVariationInfoSets("Risk Aversion Asymmetry", varyingRiskAversionAsymmetry),
                new ArticleVariationInfoSets("Quitting Rules", varyingQuitRules),
                new ArticleVariationInfoSets("Proportion of Cases Where D Is Truly Liable", varyingProbabilityTrulyLiable),
                new ArticleVariationInfoSets("Case Strength Noise", varyingNoiseToProduceCaseStrength),
                new ArticleVariationInfoSets("Issue", varyingIssue),
                new ArticleVariationInfoSets("Proportion of Costs at Beginning", varyingTimingOfCosts)
            };

            if (useRiskAversionForNonRiskReports)
            {
                // eliminate risk-related reports
                tentativeResults = tentativeResults.Where(x => !x.nameOfSet.StartsWith("Risk")).ToList();
                for (int i = 0; i < tentativeResults.Count; i++)
                {
                    ArticleVariationInfoSets variationSetInfo = tentativeResults[i];
                    tentativeResults[i] = variationSetInfo with
                    {
                        requirementsForEachVariation = variationSetInfo.requirementsForEachVariation.Select(x => x with
                        {
                            columnMatches = x.columnMatches.WithReplacement("Risk Aversion", "Moderately Risk Averse")
                        }).ToList()
                    };
                }
            }

            return tentativeResults;
        }

        List<Func<LitigGameOptions, LitigGameOptions>> FeeShiftingModeTransformations(bool includeBaselineValue)
        {
            List<Func<LitigGameOptions, LitigGameOptions>> results = new List<Func<LitigGameOptions, LitigGameOptions>>();
            foreach (FeeShiftingRule mode in FeeShiftingModes.Skip(includeBaselineValue ? 0 : 1))
            {
                results.Add(o => GetAndTransform_FeeShiftingMode(o, mode));
            }
            return results;
        }

        LitigGameOptions GetAndTransform_FeeShiftingMode(LitigGameOptions options, FeeShiftingRule mode) => GetAndTransform(options, " Fee Rule " + mode switch
        {
            FeeShiftingRule.English_LiabilityIssue => "English",
            FeeShiftingRule.MarginOfVictory_LiabilityIssue => "Margin",
            FeeShiftingRule.English_DamagesIssue => "English Damages Dispute",
            FeeShiftingRule.Rule68_DamagesIssue => "Rule 68 Damages Dispute",
            FeeShiftingRule.Rule68English_DamagesIssue => "Reverse 68 Damages Dispute",
            _ => throw new NotImplementedException()
        }
        , g =>
        {
            // This transformation happens after Multiplier, which will set LoserPays = true
            switch (mode)
            {
                case FeeShiftingRule.English_LiabilityIssue:
                    g.LoserPays = true;
                    break;
                case FeeShiftingRule.MarginOfVictory_LiabilityIssue:
                    g.LoserPays = true;
                    g.LoserPaysOnlyLargeMarginOfVictory = true;
                    g.LoserPaysMarginOfVictoryThreshold = 0.8;
                    break;
                case FeeShiftingRule.English_DamagesIssue:
                    ChangeToDamagesIssue(g);
                    g.LoserPays = true;
                    break;
                case FeeShiftingRule.Rule68_DamagesIssue:
                    ChangeToDamagesIssue(g);
                    g.Rule68 = true;
                    g.LoserPays = false; // Note that most of our simulations use English rule, even where testing for American rule (i.e., by doing English rule with multiple of 0). So, this will be an exception, and we set loser pays to false. But when Rule 68 is triggered, there will be fee shifting, according to the LoserPaysMultiple, so that variable matters. In other words, with Rule 68 American, loser pays is set to false, but the loser pays multiple still matters, because fee-shifting is still possible.
                    break;
                case FeeShiftingRule.Rule68English_DamagesIssue:
                    ChangeToDamagesIssue(g);
                    g.Rule68 = true;
                    g.LoserPays = true;
                    break;
            }
            g.VariableSettings["Fee Shifting Rule"] = mode switch
            {
                FeeShiftingRule.English_LiabilityIssue => "English",
                FeeShiftingRule.MarginOfVictory_LiabilityIssue => "Margin of Victory",
                FeeShiftingRule.English_DamagesIssue => "English",
                FeeShiftingRule.Rule68_DamagesIssue => "Rule 68",
                FeeShiftingRule.Rule68English_DamagesIssue => "Reverse 68",
                _ => throw new NotImplementedException()
            };
            g.VariableSettings["Issue"] = mode switch
            {
                FeeShiftingRule.English_LiabilityIssue => "Liability",
                FeeShiftingRule.MarginOfVictory_LiabilityIssue => "Liability",
                FeeShiftingRule.English_DamagesIssue => "Damages",
                FeeShiftingRule.Rule68_DamagesIssue => "Damages",
                FeeShiftingRule.Rule68English_DamagesIssue => "Damages",
                _ => throw new NotImplementedException()
            };
        });

        List<Func<LitigGameOptions, LitigGameOptions>> CriticalCostsMultiplierTransformations(bool includeBaselineValue)
        {
            List<Func<LitigGameOptions, LitigGameOptions>> results = new List<Func<LitigGameOptions, LitigGameOptions>>();
            foreach (double multiplier in CriticalCostsMultipliers.Skip(includeBaselineValue ? 0 : 1))
                results.Add(o => GetAndTransform_CostsMultiplier(o, multiplier));
            return results;
        }

        List<Func<LitigGameOptions, LitigGameOptions>> AdditionalCostsMultiplierTransformations(bool includeBaselineValue)
        {
            List<Func<LitigGameOptions, LitigGameOptions>> results = new List<Func<LitigGameOptions, LitigGameOptions>>();
            foreach (double multiplier in AdditionalCostsMultipliers.Skip(includeBaselineValue ? 0 : 1))
                results.Add(o => GetAndTransform_CostsMultiplier(o, multiplier));
            return results;
        }

        LitigGameOptions GetAndTransform_CostsMultiplier(LitigGameOptions options, double multiplier) => GetAndTransform(options, " Costs Multiplier " + multiplier, g =>
        {
            g.CostsMultiplier = multiplier;
            g.VariableSettings["Costs Multiplier"] = multiplier;
        });

        List<Func<LitigGameOptions, LitigGameOptions>> CriticalFeeShiftingMultiplierTransformations(bool includeBaselineValue)
        {
            List<Func<LitigGameOptions, LitigGameOptions>> results = new List<Func<LitigGameOptions, LitigGameOptions>>();
            foreach (double multiplier in CriticalFeeShiftingMultipliers.Skip(includeBaselineValue ? 0 : 1))
                results.Add(o => GetAndTransform_FeeShiftingMultiplier(o, multiplier));
            return results;
        }

        List<Func<LitigGameOptions, LitigGameOptions>> EssentialFeeShiftingMultiplierTransformations()
        {
            List<Func<LitigGameOptions, LitigGameOptions>> results = new List<Func<LitigGameOptions, LitigGameOptions>>();
            foreach (double multiplier in new double[] { 0, 1 })
                results.Add(o => GetAndTransform_FeeShiftingMultiplier(o, multiplier));
            return results;
        }

        List<Func<LitigGameOptions, LitigGameOptions>> AdditionalFeeShiftingMultiplierTransformations(bool includeBaselineValue)
        {
            List<Func<LitigGameOptions, LitigGameOptions>> results = new List<Func<LitigGameOptions, LitigGameOptions>>();
            foreach (double multiplier in AdditionalFeeShiftingMultipliers.Skip(includeBaselineValue ? 0 : 1))
                results.Add(o => GetAndTransform_FeeShiftingMultiplier(o, multiplier));
            return results;
        }

        LitigGameOptions GetAndTransform_FeeShiftingMultiplier(LitigGameOptions options, double multiplier) => GetAndTransform(options, " Fee Shifting Multiplier " + multiplier, g =>
        {
            g.LoserPays = true; // note that we're not using American rule, we're just varying the multiplier, so we should always set this to TRUE, unless we're using Rule 68 American, but that will then override this
            g.LoserPaysMultiple = multiplier;
            g.VariableSettings["Fee Shifting Multiplier"] = multiplier;
        });

        List<Func<LitigGameOptions, LitigGameOptions>> PRelativeCostsTransformations(bool includeBaselineValue)
        {
            List<Func<LitigGameOptions, LitigGameOptions>> results = new List<Func<LitigGameOptions, LitigGameOptions>>();
            foreach (double multiplier in RelativeCostsMultipliers.Skip(includeBaselineValue ? 0 : 1))
                results.Add(o => GetAndTransform_PRelativeCosts(o, multiplier));
            return results;
        }

        LitigGameOptions GetAndTransform_PRelativeCosts(LitigGameOptions options, double pRelativeCosts) => GetAndTransform(options, " Relative Costs " + pRelativeCosts, g =>
        {
            // Note: Currently, this does not affect per-round bargaining costs (not relevant in fee shifting article anyway).
            g.PFilingCost = g.DAnswerCost * pRelativeCosts;
            g.PTrialCosts = g.DTrialCosts * pRelativeCosts;
            g.VariableSettings["Relative Costs"] = pRelativeCosts;
        });

        List<Func<LitigGameOptions, LitigGameOptions>> ProportionOfCostsAtBeginningTransformations(bool includeBaselineValue)
        {
            List<Func<LitigGameOptions, LitigGameOptions>> results = new List<Func<LitigGameOptions, LitigGameOptions>>();
            foreach (double multiplier in ProportionOfCostsAtBeginning.Skip(includeBaselineValue ? 0 : 1))
                results.Add(o => GetAndTransform_ProportionOfCostsAtBeginning(o, multiplier));
            return results;
        }

        LitigGameOptions GetAndTransform_ProportionOfCostsAtBeginning(LitigGameOptions options, double proportionAtBeginning) => GetAndTransform(options, " Timing " + proportionAtBeginning, g =>
        {
            // Note: Currently, this does not affect per-round bargaining costs (not relevant in fee shifting article anyway).
            double pTotalCosts = g.PFilingCost + g.PTrialCosts;
            double dTotalCosts = g.DAnswerCost + g.DTrialCosts;
            double proportionAtEnd = (1.0 - proportionAtBeginning);
            g.PFilingCost = proportionAtBeginning * pTotalCosts;
            g.PTrialCosts = proportionAtEnd * pTotalCosts;
            g.DAnswerCost = proportionAtBeginning * dTotalCosts;
            g.DTrialCosts = proportionAtEnd * dTotalCosts;
            g.VariableSettings["Proportion of Costs at Beginning"] = proportionAtBeginning;
        });

        List<Func<LitigGameOptions, LitigGameOptions>> NoiseTransformations(bool includeBaselineValue)
        {
            List<Func<LitigGameOptions, LitigGameOptions>> results = new List<Func<LitigGameOptions, LitigGameOptions>>();
            foreach ((double pNoiseMultiplier, double dNoiseMultiplier) in NoiseMultipliers.Skip(includeBaselineValue ? 0 : 1))
                results.Add(o => GetAndTransform_Noise(o, pNoiseMultiplier, dNoiseMultiplier));
            return results;
        }

        LitigGameOptions GetAndTransform_Noise(LitigGameOptions options, double pNoiseMultiplier, double dNoiseMultiplier) => GetAndTransform(options, " Noise " + pNoiseMultiplier + "," + dNoiseMultiplier, g =>
        {
            g.PDamagesNoiseStdev *= pNoiseMultiplier;
            g.PLiabilityNoiseStdev *= pNoiseMultiplier;
            g.DDamagesNoiseStdev *= dNoiseMultiplier;
            g.DLiabilityNoiseStdev *= dNoiseMultiplier;
            g.CourtLiabilityNoiseStdev = Math.Min(g.PLiabilityNoiseStdev, g.DLiabilityNoiseStdev);
            g.CourtDamagesNoiseStdev = Math.Min(g.PDamagesNoiseStdev, g.DDamagesNoiseStdev);

            g.VariableSettings["Noise Multiplier P"] = pNoiseMultiplier;
            g.VariableSettings["Noise Multiplier D"] = dNoiseMultiplier;
        });

        List<Func<LitigGameOptions, LitigGameOptions>> CriticalRiskAversionTransformations(bool includeBaselineValue) => new List<Func<LitigGameOptions, LitigGameOptions>>() { GetAndTransform_RiskNeutral, GetAndTransform_ModeratelyRiskAverse }.Skip(includeBaselineValue ? 0 : 1).ToList();

        List<Func<LitigGameOptions, LitigGameOptions>> AdditionalRiskAversionTransformations(bool includeBaselineValue) => new List<Func<LitigGameOptions, LitigGameOptions>>() { GetAndTransform_RiskNeutral, GetAndTransform_MildlyRiskAverse, GetAndTransform_VeryRiskAverse, GetAndTransform_POnlyRiskAverse, GetAndTransform_DOnlyRiskAverse, GetAndTransform_PMoreRiskAverse, GetAndTransform_DMoreRiskAverse }.Skip(includeBaselineValue ? 0 : 1).ToList();

        LitigGameOptions GetAndTransform_ModeratelyRiskAverse(LitigGameOptions options) => GetAndTransform(options, " Moderately Risk Averse", g =>
        {
            g.PUtilityCalculator = new CARARiskAverseUtilityCalculator() { InitialWealth = g.PInitialWealth, Alpha = 2, LinearTransformation = true };
            g.DUtilityCalculator = new CARARiskAverseUtilityCalculator() { InitialWealth = g.DInitialWealth, Alpha = 2, LinearTransformation = true };
            g.VariableSettings["Risk Aversion"] = "Moderately Risk Averse";
        });
        LitigGameOptions GetAndTransform_MildlyRiskAverse(LitigGameOptions options) => GetAndTransform(options, " Mildly Risk Averse", g =>
        {
            g.PUtilityCalculator = new CARARiskAverseUtilityCalculator() { InitialWealth = g.PInitialWealth, Alpha = 1, LinearTransformation = true };
            g.DUtilityCalculator = new CARARiskAverseUtilityCalculator() { InitialWealth = g.DInitialWealth, Alpha = 1, LinearTransformation = true };
            g.VariableSettings["Risk Aversion"] = "Mildly Risk Averse";
        });
        LitigGameOptions GetAndTransform_VeryRiskAverse(LitigGameOptions options) => GetAndTransform(options, " Highly Risk Averse", g =>
        {
            g.PUtilityCalculator = new CARARiskAverseUtilityCalculator() { InitialWealth = g.PInitialWealth, Alpha = 4, LinearTransformation = true };
            g.DUtilityCalculator = new CARARiskAverseUtilityCalculator() { InitialWealth = g.DInitialWealth, Alpha = 4, LinearTransformation = true };
            g.VariableSettings["Risk Aversion"] = "Highly Risk Averse";
        });
        LitigGameOptions GetAndTransform_PMoreRiskAverse(LitigGameOptions options) => GetAndTransform(options, " P More Risk Averse", g =>
        {
            g.PUtilityCalculator = new CARARiskAverseUtilityCalculator() { InitialWealth = g.PInitialWealth, Alpha = 4, LinearTransformation = true };
            g.DUtilityCalculator = new CARARiskAverseUtilityCalculator() { InitialWealth = g.DInitialWealth, Alpha = 2, LinearTransformation = true };
            g.VariableSettings["Risk Aversion"] = "P More Risk Averse";
        });
        LitigGameOptions GetAndTransform_DMoreRiskAverse(LitigGameOptions options) => GetAndTransform(options, " D More Risk Averse", g =>
        {
            g.PUtilityCalculator = new CARARiskAverseUtilityCalculator() { InitialWealth = g.PInitialWealth, Alpha = 2, LinearTransformation = true };
            g.DUtilityCalculator = new CARARiskAverseUtilityCalculator() { InitialWealth = g.DInitialWealth, Alpha = 4, LinearTransformation = true };
            g.VariableSettings["Risk Aversion"] = "D More Risk Averse";
        });

        LitigGameOptions GetAndTransform_RiskNeutral(LitigGameOptions options) => GetAndTransform(options, " Risk Neutral", g =>
        {
            g.PUtilityCalculator = new RiskNeutralUtilityCalculator() { InitialWealth = g.PInitialWealth };
            g.DUtilityCalculator = new RiskNeutralUtilityCalculator() { InitialWealth = g.DInitialWealth };
            g.VariableSettings["Risk Aversion"] = "Risk Neutral";
        });
        LitigGameOptions GetAndTransform_POnlyRiskAverse(LitigGameOptions options) => GetAndTransform(options, " P Risk Averse", g =>
        {
            g.PUtilityCalculator = new CARARiskAverseUtilityCalculator() { InitialWealth = g.PInitialWealth, Alpha = 2, LinearTransformation = true };
            g.DUtilityCalculator = new RiskNeutralUtilityCalculator() { InitialWealth = g.DInitialWealth };
            g.VariableSettings["Risk Aversion"] = "P Risk Averse";
        });
        LitigGameOptions GetAndTransform_DOnlyRiskAverse(LitigGameOptions options) => GetAndTransform(options, " D Risk Averse", g =>
        {
            g.PUtilityCalculator = new RiskNeutralUtilityCalculator() { InitialWealth = g.PInitialWealth };
            g.DUtilityCalculator = new CARARiskAverseUtilityCalculator() { InitialWealth = g.DInitialWealth, Alpha = 2, LinearTransformation = true };
            g.VariableSettings["Risk Aversion"] = "D Risk Averse";
        });
        List<Func<LitigGameOptions, LitigGameOptions>> AllowAbandonAndDefaultsTransformations(bool includeBaselineValue)
        {
            List<Func<LitigGameOptions, LitigGameOptions>> results = new List<Func<LitigGameOptions, LitigGameOptions>>();
            foreach (bool allowAbandonAndDefaults in new[] { true, false }.Skip(includeBaselineValue ? 0 : 1))
                results.Add(o => GetAndTransform_AllowAbandonAndDefaults(o, allowAbandonAndDefaults));
            return results;
        }

        LitigGameOptions GetAndTransform_AllowAbandonAndDefaults(LitigGameOptions options, bool allowAbandonAndDefaults) => GetAndTransform(options, " Abandonable " + allowAbandonAndDefaults, g =>
        {
            g.AllowAbandonAndDefaults = allowAbandonAndDefaults;
            g.VariableSettings["Allow Abandon and Defaults"] = allowAbandonAndDefaults;
        });
        List<Func<LitigGameOptions, LitigGameOptions>> ProbabilityTrulyLiableTransformations(bool includeBaselineValue)
        {
            List<Func<LitigGameOptions, LitigGameOptions>> results = new List<Func<LitigGameOptions, LitigGameOptions>>();
            foreach (double probability in ProbabilitiesTrulyLiable.Skip(includeBaselineValue ? 0 : 1))
                results.Add(o => GetAndTransform_ProbabilityTrulyLiable(o, probability));
            return results;
        }

        LitigGameOptions GetAndTransform_ProbabilityTrulyLiable(LitigGameOptions options, double probability) => GetAndTransform(options, " Truly Liable Probability " + probability, g =>
        {
            ((LitigGameExogenousDisputeGenerator)g.LitigGameDisputeGenerator).ExogenousProbabilityTrulyLiable = probability;

            g.VariableSettings["Probability Truly Liable"] = probability;
        });

        List<Func<LitigGameOptions, LitigGameOptions>> NoiseToProduceCaseStrengthTransformations(bool includeBaselineValue)
        {
            List<Func<LitigGameOptions, LitigGameOptions>> results = new List<Func<LitigGameOptions, LitigGameOptions>>();
            foreach (double noise in StdevsNoiseToProduceLiabilityStrength.Skip(includeBaselineValue ? 0 : 1))
                results.Add(o => GetAndTransform_NoiseToProduceCaseStrength(o, noise));
            return results;
        }

        LitigGameOptions GetAndTransform_NoiseToProduceCaseStrength(LitigGameOptions options, double noise) => GetAndTransform(options, " Case Strength Noise " + noise, g =>
        {
            ((LitigGameExogenousDisputeGenerator)g.LitigGameDisputeGenerator).StdevNoiseToProduceLiabilityStrength = noise;
            g.VariableSettings["Noise to Produce Case Strength"] = noise;
        });
        List<Func<LitigGameOptions, LitigGameOptions>> LiabilityVsDamagesTransformations(bool includeBaselineValue)
        {
            List<Func<LitigGameOptions, LitigGameOptions>> results = new List<Func<LitigGameOptions, LitigGameOptions>>();
            foreach (bool liabilityIsUncertain in new[] { true, false }.Skip(includeBaselineValue ? 0 : 1))
                results.Add(o => GetAndTransform_LiabilityVsDamages(o, liabilityIsUncertain));
            return results;
        }

        LitigGameOptions GetAndTransform_LiabilityVsDamages(LitigGameOptions options, bool liabilityIsUncertain) => GetAndTransform(options, liabilityIsUncertain ? " Liability Dispute" : " Damages Dispute", g =>
        {
            if (!liabilityIsUncertain)
            {
                ChangeToDamagesIssue(g);
            }

            g.VariableSettings["Issue"] = liabilityIsUncertain ? "Liability" : "Damages";
        });

        private static void ChangeToDamagesIssue(LitigGameOptions g)
        {
            g.NumDamagesStrengthPoints = g.NumLiabilityStrengthPoints;
            g.NumDamagesSignals = g.NumLiabilitySignals;
            g.NumLiabilityStrengthPoints = 1;
            g.NumLiabilitySignals = 1;
            g.LitigGameDisputeGenerator = new LitigGameExogenousDisputeGenerator()
            {
                ExogenousProbabilityTrulyLiable = 1.0,
                StdevNoiseToProduceLiabilityStrength = 0,
            };
        }

        #endregion
    }
}
