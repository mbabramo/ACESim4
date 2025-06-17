using ACESim.Util;
using ACESimBase;
using ACESimBase.Games.LitigGame;
using ACESimBase.GameSolvingSupport.DeepCFRSupport;
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
    public class LitigGameEndogenousDisputesLauncher : LitigGameLauncherBase
    {

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
                    ("Damages Multiplier", "1"),
                    ("Issue", "Liability"),
                    ("Proportion of Costs at Beginning", "0.5"),
                };
            }
        }

        public override List<string> NamesOfVariationSets => new List<string>()
        {
            "Costs Multipliers",
            "Fee Shifting Multiples",
            "Risk Aversion",
            "Noise Multipliers", // includes P & D
            "Relative Costs",
            "Damages Multiplier",
            // TODO: Add back in "Fee Shifting Mode",
            "Proportion of Costs at Beginning",
        };

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

        public enum UnderlyingGame
        {
            AppropriationGame,
            PrecautionNegligenceGame,
        }

        public UnderlyingGame GameToPlay => UnderlyingGame.PrecautionNegligenceGame;

        public string MasterReportNamePrefix => GameToPlay switch
        {
            UnderlyingGame.AppropriationGame => "APP",
            UnderlyingGame.PrecautionNegligenceGame => "PREC",
            _ => throw new NotImplementedException("Unknown game to play: " + GameToPlay.ToString()),
        };

        public override string MasterReportNameForDistributedProcessing => MasterReportNamePrefix + "001";

        // We can use this to allow for multiple options sets. These can then run in parallel. But note that we can also have multiple runs with a single option set using different settings by using GameDefinition scenarios; this is useful when there is a long initialization and it makes sense to complete one set before starting the next set.


        public override FeeShiftingRule[] FeeShiftingModes => new[] { FeeShiftingRule.English_LiabilityIssue, FeeShiftingRule.MarginOfVictory_LiabilityIssue, 
            FeeShiftingRule.Rule68_DamagesIssue, FeeShiftingRule.Rule68English_DamagesIssue 
        }; // TODO -- note that these are disabled now (see TODOs above and below). Can be added back in by providing support for margin of fee shifting by creating logic around that in PrecautionNegligence game


        public override List<GameOptions> GetOptionsSets()
        {
            // honour console runs that deliberately want one set
            if (LaunchSingleOptionsSetOnly)
                return new List<GameOptions>
                {
                    LitigGameOptionsGenerator.GetLitigGameOptions()
                };

            var optionSets = new List<GameOptions>();

            // this call comes from PermutationalLauncher and
            // enumerates every transformation defined in GetSetsOfGameOptions()
            AddToOptionsSets(optionSets);

            // optional post-processing already present in the original
            if (LimitToTaskIDs is { Length: > 0 })
                optionSets = LimitToTaskIDs.Select(id => optionSets[id]).ToList();

            return optionSets.OrderBy(o => o.Name).ToList();
        }


        #region Game sets generation

        public override List<GameOptions> FlattenAndOrderGameSets(List<List<GameOptions>> gamesSets)
        {
            var eachGameIndependently = gamesSets.SelectMany(x => x)
                .OrderBy(x => ((LitigGameOptions)x).LoserPaysOnlyLargeMarginOfVictory) // place here anything that will change the game tree size
                .ToList();
            return eachGameIndependently.Select(x => (GameOptions) x).ToList();
        }

        public override List<List<GameOptions>> GetVariationSets(bool useAllPermutationsOfTransformations, bool includeBaselineValueForNoncritical)
        {
            const int numCritical = 3; // critical transformations are all interacted with one another and then with each of the other transformations  
            var criticalCostsMultiplierTransformations = CriticalCostsMultiplierTransformations(true);
            var noncriticalCostsMultiplierTransformations = AdditionalCostsMultiplierTransformations(includeBaselineValueForNoncritical);
            var criticalFeeShiftingMultipleTransformations = CriticalFeeShiftingMultiplierTransformations(true);
            var noncriticalFeeShiftingMultipleTransformations = AdditionalFeeShiftingMultiplierTransformations(includeBaselineValueForNoncritical);
            var criticalRiskAversionTransformations = CriticalRiskAversionTransformations(true);
            var noncriticalRiskAversionTransformations = AdditionalRiskAversionTransformations(includeBaselineValueForNoncritical);
            List<List<Func<LitigGameOptions, LitigGameOptions>>> allTransformations = new List<List<Func<LitigGameOptions, LitigGameOptions>>>()
           {  
               // IMPORTANT: When changing these, change NamesOfFeeShiftingArticleSets to match each of these  
               // Can always choose any of these:  
               criticalCostsMultiplierTransformations,
               criticalFeeShiftingMultipleTransformations,
               criticalRiskAversionTransformations,  
               // IMPORTANT: Must follow with the corresponding noncritical transformations
               // And then can vary ONE of these (avoiding inconsistencies with above):  
               noncriticalCostsMultiplierTransformations, // i.e., not only the core costs multipliers and other core variables, but also the critical costs multipliers  
               noncriticalFeeShiftingMultipleTransformations,
               noncriticalRiskAversionTransformations,
               // Now other noncritical transformations
               NoiseTransformations(includeBaselineValueForNoncritical),
               PRelativeCostsTransformations(includeBaselineValueForNoncritical),
               DamagesMultiplierTransformations(includeBaselineValueForNoncritical),
               // FeeShiftingModeTransformations(includeBaselineValueForNoncritical),  // TODO: Add this back in by providing support for Bayesian logic with the margin-of-victory approach -- then make change in NamesOfVariationSets
               ProportionOfCostsAtBeginningTransformations(includeBaselineValueForNoncritical),
           };
            List<List<GameOptions>> result = PerformTransformations(allTransformations, numCritical, useAllPermutationsOfTransformations, includeBaselineValueForNoncritical);
            return result;
        }

        public override List<SimulationSetsIdentifier> GetSimulationSetsIdentifiers(SimulationSetsTransformer transformer = null)
        {
            var varyingNothing = new List<SimulationIdentifier>()
            {
                new SimulationIdentifier("Baseline", DefaultVariableValues),
            };

            //var varyingFeeShiftingRule_LiabilityUncertain = new List<ArticleVariationInfo>()
            //{
            //    // where liability is uncertain:
            //    new ArticleVariationInfo("English", DefaultVariableValues),
            //    new ArticleVariationInfo("Margin of Victory", DefaultVariableValues.WithReplacement("Fee Shifting Rule", "Margin of Victory")),
            //};

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

            var varyingDamagesMultiplier = new List<SimulationIdentifier>()
            {
                new SimulationIdentifier("0.5", DefaultVariableValues.WithReplacement("Damages Multiplier", "0.5")),
                new SimulationIdentifier("1", DefaultVariableValues.WithReplacement("Damages Multiplier", "1")),
                new SimulationIdentifier("2", DefaultVariableValues.WithReplacement("Damages Multiplier", "2")),
                new SimulationIdentifier("4", DefaultVariableValues.WithReplacement("Damages Multiplier", "4")),
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
                // TODO new ArticleVariationInfoSets("Fee Shifting Rule (Liability Issue)", varyingFeeShiftingRule_LiabilityUncertain),
                new SimulationSetsIdentifier("Noise Multiplier", varyingNoiseMultipliersBoth),
                new SimulationSetsIdentifier("Information Asymmetry", varyingNoiseMultipliersAsymmetric),
                new SimulationSetsIdentifier("Relative Costs", varyingRelativeCosts),
                new SimulationSetsIdentifier("Risk Aversion", varyingRiskAversion),
                new SimulationSetsIdentifier("Risk Aversion Asymmetry", varyingRiskAversionAsymmetry),
                new SimulationSetsIdentifier("Damages Multiplier", varyingDamagesMultiplier),
                new SimulationSetsIdentifier("Proportion of Costs at Beginning", varyingTimingOfCosts)
            };
            
            tentativeResults = PerformArticleVariationInfoSetsTransformation(transformer, tentativeResults);

            return tentativeResults;
        }

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
