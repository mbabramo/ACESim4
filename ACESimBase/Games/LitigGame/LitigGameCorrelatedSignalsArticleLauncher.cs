using ACESim.Util;
using ACESimBase;
using ACESimBase.Games.LitigGame;
using ACESimBase.GameSolvingSupport;
using ACESimBase.GameSolvingSupport.Settings;
using ACESimBase.Util.Collections;
using ACESimBase.Util.Combinatorics;
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

        public bool TestDisputeGeneratorVariations = false;
        public bool IncludeRunningSideBetVariations = false;
        public bool LimitToAmerican = true;
        public bool UseSmallerTree = true; 

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
        
        public override string MasterReportNameForDistributedProcessing => "CS" + "001";

        public override GameOptions GetDefaultSingleGameOptions()
        {
            return LitigGameOptionsGenerator.GetLitigGameOptions();
        }

        public override List<GameOptions> GetOptionsSets()
        {
            List<GameOptions> optionSets = new List<GameOptions>();

            AddFeeShiftingArticleGames(optionSets);

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
            AddToOptionsSets(options);
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

        public override List<GameOptions> GetVariationSets()
        {
            var dims = new List<VariableCombinationGenerator.Dimension<LitigGameOptions>>
            {
                // global critical dimensions (three)
                new("CostsMultiplier",
                    CriticalCostsMultiplierTransformations(),
                    AdditionalCostsMultiplierTransformations(),
                    IsGlobal: true),

                new("FeeShiftingMultiplier",
                    CriticalFeeShiftingMultiplierTransformations(),
                    AdditionalFeeShiftingMultiplierTransformations(),
                    IsGlobal: true),

                new("RiskAversion",
                    CriticalRiskAversionTransformations(),
                    AdditionalRiskAversionTransformations(),
                    IsGlobal: true),

                // modifier-only variables
                new("Noise",
                    null,
                    NoiseTransformations()
                    ),

                new("RelativeCosts",
                    null,
                    PRelativeCostsTransformations()
                    ),

                new("FeeShiftingMode",
                    null,
                    FeeShiftingModeTransformations()
                    ),

                new("AllowAbandon",
                    null,
                    AllowAbandonAndDefaultsTransformations()
                    ),

                new("ProbabilityTrulyLiable",
                    null,
                    ProbabilityTrulyLiableTransformations()
                    ),

                new("NoiseForCaseStrength",
                    null,
                    NoiseToProduceCaseStrengthTransformations()
                    ),

                new("LiabilityVsDamages",
                    null,
                    LiabilityVsDamagesTransformations()
                    ),

                new("CostsStartFraction",
                    null,
                    ProportionOfCostsAtBeginningTransformations()
                    )
            };

            dims = dims.Where(d => d.CriticalTransforms != null ||
                                   d.ModifierTransforms?.Count > 0).ToList();

            var result = GenerateCombinations(dims, LitigGameOptionsGenerator.GetLitigGameOptions, true);

            return result;
        }

        public override List<SimulationSetsIdentifier> GetSimulationSetsIdentifiers(SimulationSetsTransformer transformer = null)
        {
            var varyingNothing = new List<SimulationIdentifier>()
            {
                new SimulationIdentifier("Baseline", DefaultVariableValues),
            };

            var varyingFeeShiftingRule_LiabilityUncertain = new List<SimulationIdentifier>()
            {
                // where liability is uncertain:
                new SimulationIdentifier("English", DefaultVariableValues),
                new SimulationIdentifier("Margin of Victory", DefaultVariableValues.WithReplacement("Fee Shifting Rule", "Margin of Victory")),
            };

            var varyingFeeShiftingRule_DamagesUncertain = new List<SimulationIdentifier>()
            {
                // where liability is uncertain:
                new SimulationIdentifier("English", DefaultVariableValues.WithReplacement("Issue", "Damages")),
                new SimulationIdentifier("Rule 68", DefaultVariableValues.WithReplacement("Fee Shifting Rule", "Rule 68").WithReplacement("Issue", "Damages")),
                new SimulationIdentifier("Reverse 68", DefaultVariableValues.WithReplacement("Fee Shifting Rule", "Reverse 68").WithReplacement("Issue", "Damages")),
            };

            var varyingNoiseMultipliersBoth = new List<SimulationIdentifier>()
            {
                new SimulationIdentifier(".25", DefaultVariableValues.WithReplacement("Noise Multiplier P", "0.25").WithReplacement("Noise Multiplier D", "0.25")),
                new SimulationIdentifier(".5", DefaultVariableValues.WithReplacement("Noise Multiplier P", "0.5").WithReplacement("Noise Multiplier D", "0.5")),
                new SimulationIdentifier("1", DefaultVariableValues.WithReplacement("Noise Multiplier P", "1").WithReplacement("Noise Multiplier D", "1")),
                new SimulationIdentifier("2", DefaultVariableValues.WithReplacement("Noise Multiplier P", "2").WithReplacement("Noise Multiplier D", "2")),
                new SimulationIdentifier("4", DefaultVariableValues.WithReplacement("Noise Multiplier P", "4").WithReplacement("Noise Multiplier D", "4")),
            };

            var varyingNoiseMultipliersAsymmetric = new List<SimulationIdentifier>()
            {
                new SimulationIdentifier("Equal Information", DefaultVariableValues.WithReplacement("Noise Multiplier P", "1").WithReplacement("Noise Multiplier D", "1")),
                new SimulationIdentifier("P Better", DefaultVariableValues.WithReplacement("Noise Multiplier P", "0.5").WithReplacement("Noise Multiplier D", "2")),
                new SimulationIdentifier("D Better", DefaultVariableValues.WithReplacement("Noise Multiplier P", "2").WithReplacement("Noise Multiplier D", "0.5")),
            };

            var varyingRelativeCosts = new List<SimulationIdentifier>()
            {
                new SimulationIdentifier("P Lower Costs", DefaultVariableValues.WithReplacement("Relative Costs", "0.5")),
                new SimulationIdentifier("Equal", DefaultVariableValues.WithReplacement("Relative Costs", "1")),
                new SimulationIdentifier("P Higher Costs", DefaultVariableValues.WithReplacement("Relative Costs", "2")),
            };

            var varyingRiskAversion = new List<SimulationIdentifier>()
            {
                new SimulationIdentifier("Risk Neutral", DefaultVariableValues.WithReplacement("Risk Aversion", "Risk Neutral")),
                new SimulationIdentifier("Mildly Averse", DefaultVariableValues.WithReplacement("Risk Aversion", "Mildly Risk Averse")),
                new SimulationIdentifier("Moderately Averse", DefaultVariableValues.WithReplacement("Risk Aversion", "Moderately Risk Averse")),
                new SimulationIdentifier("Highly Averse", DefaultVariableValues.WithReplacement("Risk Aversion", "Highly Risk Averse")),
            };

            var varyingRiskAversionAsymmetry = new List<SimulationIdentifier>()
            {
                new SimulationIdentifier("P Risk Averse", DefaultVariableValues.WithReplacement("Risk Aversion", "P Risk Averse")),
                new SimulationIdentifier("D Risk Averse", DefaultVariableValues.WithReplacement("Risk Aversion", "D Risk Averse")),
                new SimulationIdentifier("P More Risk Averse", DefaultVariableValues.WithReplacement("Risk Aversion", "P More Risk Averse")),
                new SimulationIdentifier("D More Risk Averse", DefaultVariableValues.WithReplacement("Risk Aversion", "D More Risk Averse")),
            };

            var varyingQuitRules = new List<SimulationIdentifier>()
            {
                new SimulationIdentifier("Quitting Allowed", DefaultVariableValues.WithReplacement("Allow Abandon and Defaults", "TRUE")),
                new SimulationIdentifier("Quitting Prohibited", DefaultVariableValues.WithReplacement("Allow Abandon and Defaults", "FALSE")),
            };

            var varyingProbabilityTrulyLiable = new List<SimulationIdentifier>()
            {
                new SimulationIdentifier("0.1", DefaultVariableValues.WithReplacement("Probability Truly Liable", "0.1")),
                new SimulationIdentifier("0.5", DefaultVariableValues.WithReplacement("Probability Truly Liable", "0.5")),
                new SimulationIdentifier("0.9", DefaultVariableValues.WithReplacement("Probability Truly Liable", "0.9")),
            };

            var varyingNoiseToProduceCaseStrength = new List<SimulationIdentifier>()
            {
                new SimulationIdentifier("0.175", DefaultVariableValues.WithReplacement("Noise to Produce Case Strength", "0.175")),
                new SimulationIdentifier("0.35", DefaultVariableValues.WithReplacement("Noise to Produce Case Strength", "0.35")),
                new SimulationIdentifier("0.70", DefaultVariableValues.WithReplacement("Noise to Produce Case Strength", "0.7")),
            };

            var varyingIssue = new List<SimulationIdentifier>()
            {
                new SimulationIdentifier("Liability", DefaultVariableValues.WithReplacement("Issue", "Liability")),
                new SimulationIdentifier("Damages", DefaultVariableValues.WithReplacement("Issue", "Damages")),
            };

            var varyingTimingOfCosts = new List<SimulationIdentifier>()
            {
                new SimulationIdentifier("0", DefaultVariableValues.WithReplacement("Proportion of Costs at Beginning", "0")),
                new SimulationIdentifier("0.25", DefaultVariableValues.WithReplacement("Proportion of Costs at Beginning", "0.25")),
                new SimulationIdentifier("0.5", DefaultVariableValues.WithReplacement("Proportion of Costs at Beginning", "0.5")),
                new SimulationIdentifier("0.75", DefaultVariableValues.WithReplacement("Proportion of Costs at Beginning", "0.75")),
                new SimulationIdentifier("1", DefaultVariableValues.WithReplacement("Proportion of Costs at Beginning", "1")),
            };

            var tentativeResults = new List<SimulationSetsIdentifier>()
            {
                new SimulationSetsIdentifier("Baseline", varyingNothing),
                new SimulationSetsIdentifier("Fee Shifting Rule (Liability Issue)", varyingFeeShiftingRule_LiabilityUncertain),
                new SimulationSetsIdentifier("Fee Shifting Rule (Damages Issue)", varyingFeeShiftingRule_DamagesUncertain),
                new SimulationSetsIdentifier("Noise Multiplier", varyingNoiseMultipliersBoth),
                new SimulationSetsIdentifier("Information Asymmetry", varyingNoiseMultipliersAsymmetric),
                new SimulationSetsIdentifier("Relative Costs", varyingRelativeCosts),
                new SimulationSetsIdentifier("Risk Aversion", varyingRiskAversion),
                new SimulationSetsIdentifier("Risk Aversion Asymmetry", varyingRiskAversionAsymmetry),
                new SimulationSetsIdentifier("Quitting Rules", varyingQuitRules),
                new SimulationSetsIdentifier("Proportion of Cases Where D Is Truly Liable", varyingProbabilityTrulyLiable),
                new SimulationSetsIdentifier("Case Strength Noise", varyingNoiseToProduceCaseStrength),
                new SimulationSetsIdentifier("Issue", varyingIssue),
                new SimulationSetsIdentifier("Proportion of Costs at Beginning", varyingTimingOfCosts)
            };
            tentativeResults = PerformArticleVariationInfoSetsTransformation(transformer, tentativeResults);

            return tentativeResults;
        }


        #endregion
    }
}
