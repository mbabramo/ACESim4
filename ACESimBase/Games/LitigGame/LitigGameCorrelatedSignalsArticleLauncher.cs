using ACESim.Util;
using ACESimBase;
using ACESimBase.Games.LitigGame;
using ACESimBase.GameSolvingSupport;
using ACESimBase.GameSolvingSupport.Settings;
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
    public class LitigGameCorrelatedSignalsArticleLauncher : LitigGameLauncherBase
    {

        private bool HigherRiskAversion = false;
        private bool PRiskAverse = false;
        public bool DRiskAverse = false;
        public bool TestDisputeGeneratorVariations = false;
        public bool IncludeRunningSideBetVariations = false;
        public bool LimitToAmerican = true;
        public bool UseSmallerTree = true; 


        public override List<string> NamesOfVariationSets => new List<string>()
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
        public override List<(string, string)> DefaultVariableValues
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

        public override List<ArticleVariationInfoSets> VariationInfoSets
            => GetArticleVariationInfoList_PossiblyFixingRiskAversion(false);
        public override string ReportPrefix => MasterReportNameForDistributedProcessing;

        public override string MasterReportNameForDistributedProcessing => "FS037";

        // We can use this to allow for multiple options sets. These can then run in parallel. But note that we can also have multiple runs with a single option set using different settings by using GameDefinition scenarios; this is useful when there is a long initialization and it makes sense to complete one set before starting the next set.

        // Fee shifting article

        public override List<(string criticalValueName, string[] criticalValueValues)> CriticalVariableValues
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
            GetGameOptions(options, includeBaselineValueForNoncritical);
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

        public override List<List<GameOptions>> GetSetsOfGameOptions(bool useAllPermutationsOfTransformations, bool includeBaselineValueForNoncritical)
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
            return result.Select(innerList => innerList.Cast<GameOptions>().ToList()).ToList();
        }

        public override List<ArticleVariationInfoSets> GetArticleVariationInfoList_PossiblyFixingRiskAversion(bool useRiskAversionForNonRiskReports)
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


        #endregion
    }
}
