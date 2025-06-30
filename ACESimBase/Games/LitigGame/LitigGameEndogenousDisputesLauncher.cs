using ACESim.Util;
using ACESimBase;
using ACESimBase.Games.LitigGame;
using ACESimBase.Games.LitigGame.PrecautionModel;
using ACESimBase.GameSolvingSupport.DeepCFRSupport;
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
    public class LitigGameEndogenousDisputesLauncher : LitigGameLauncherBase
    {

        public override List<(string, string)> DefaultVariableValues
        {
            get
            {
                return new List<(string, string)>()
                {
                    ("Risk Aversion", "Risk Neutral"),
                    ("Costs Multiplier", "1"),
                    ("Fee Shifting Multiplier", "0"),
                    ("Damages Multiplier", "1"),
                    ("Liability Threshold", "1"),
                    ("Unit Precaution Cost", "1E-05"),
                    ("Relative Costs", "1"),
                    ("Noise Multiplier P", "1"),
                    ("Noise Multiplier D", "1"),
                    ("Court Noise", "0.2"),
                    ("Issue", "Liability"),
                    ("Proportion of Costs at Beginning", "0.5"),
                    ("Cost Misestimation", "1"),
                    ("Num Signals and Offers", "Baseline"),
                    ("Wrongful Attribution Probability", "Normal")
                };
            }
        }

        public override List<(string criticalValueName, string[] criticalValueValues)> CriticalVariableValues
        {
            get
            {
                return new List<(string, string[])>()
                {
                    ("Risk Aversion", new[] { "Risk Neutral", "Moderately Risk Averse" }),
                    ("Costs Multiplier", CriticalCostsMultipliers.Select(x => x.ToString()).ToArray()),
                    ("Fee Shifting Multiplier", CriticalFeeShiftingMultipliers.Select(x => x.ToString()).ToArray()),
                    ("Damages Multiplier", CriticalDamagesMultipliers.Select(x => x.ToString()).ToArray()),
                    ("Liability Threshold", CriticalLiabilityThresholds.Select(x => x.ToString()).ToArray()),
                    ("Unit Precaution Cost", CriticalPrecautionCosts.Select(x => x.ToString()).ToArray()),
                };
            }
        }
        public override FeeShiftingRule[] FeeShiftingModes => new[] { FeeShiftingRule.English_LiabilityIssue };
        
        // REMINDER: When changing these, must also change GetSimulationSetsIdentifiers so that reports work correctly.

        public override double[] CriticalCostsMultipliers => new double[] { 1.0, 0.25, 4.0 };
        public override double[] AdditionalCostsMultipliers => new double[] { 1.0, 0, 0.5, 2.0 };
        public override double[] CriticalFeeShiftingMultipliers => new double[] { 0.0, 1.0, 2.0 };
        public override double[] AdditionalFeeShiftingMultipliers => new double[] { 0.0, 0.5, 5.0 };

        public double[] CriticalLiabilityThresholds => new double[] { 1.0, 0.8, 1.2 };
        public double[] AdditionalLiabilityThresholds => new double[] { 1.0, 0, 2.0 };

        public double[] CriticalPrecautionCosts => new double[] { 0.00001, 0.000008, 0.000012 };
        public double[] AdditionalPrecautionCosts => new double[] { 0.00001, 0.000001 };

        
        public double[] CriticalDamagesMultipliers => new double[] { 1.0, 0.5, 2.0 };
        public double[] AdditionalDamagesMultipliers => new double[] { 1.0, 4.0 };

        public double[] CourtNoiseValues = new double[] { 0.2, 0, 0.4 };

        public double[] CostMisestimations => new double[] { 1.0, 2.0, 5.0, 10.0 };

        public double[] WrongfulAttributionProbabilities => new double[] { 1E-05, 0, 1E-04 /* high value */ };

        public (double precautionCostPerception, double feeShiftingMultiplier)[] HighMisestimationFeeShiftingMultipliers => [(5.0, 0.0), (5.0, 1.0), (5.0, 2.0)];
        public (double precautionCostPerception, double damagesMultipliers)[] HighMisestimationDamagesMultipliers => [(5.0, 1.0), (5.0, 0.5), (5.0, 2.0)];

        

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

            optionSets = optionSets.OrderBy(o => o.Name).ToList();

            // optional post-processing already present in the original
            if (LimitToTaskIDs is { Length: > 0 })
                optionSets = LimitToTaskIDs.Select(id => optionSets[id]).ToList();

            return optionSets;
        }

        #region Custom transformations

        // Steps:
        // * Define default value above
        // * Default range of values above in double[], listing default first
        // * Follow form here to define transformations function
        // * Below, add to GetVariationSetsInfo

        // Damages multiplier

        public List<Func<LitigGameOptions, LitigGameOptions>> CriticalDamagesMultiplierTransformations()
            => Transform(GetAndTransform_DamagesMultiplier, CriticalDamagesMultipliers);
        
        public List<Func<LitigGameOptions, LitigGameOptions>> AdditionalDamagesMultiplierTransformations()
            => Transform(GetAndTransform_DamagesMultiplier, AdditionalDamagesMultipliers);

        // Note: GetAndTransform_DamagesMultiplier is defined in base class

        // Marginal precaution cost

        public List<Func<LitigGameOptions, LitigGameOptions>> CriticalPrecautionCostTransformations()
            => Transform(GetAndTransform_PrecautionCost, CriticalPrecautionCosts);
        
        public List<Func<LitigGameOptions, LitigGameOptions>> AdditionalPrecautionCostTransformations()
            => Transform(GetAndTransform_PrecautionCost, AdditionalPrecautionCosts);

        public LitigGameOptions GetAndTransform_PrecautionCost(LitigGameOptions options, double marginalPrecautionCost) => GetAndTransform(options, " UPC " + marginalPrecautionCost, g =>
        {
            PrecautionNegligenceDisputeGenerator disputeGenerator = (PrecautionNegligenceDisputeGenerator)options.LitigGameDisputeGenerator;
            disputeGenerator.UnitPrecautionCost = marginalPrecautionCost;
            g.VariableSettings["Unit Precaution Cost"] = marginalPrecautionCost;
        });

        // Liability threshold

        public List<Func<LitigGameOptions, LitigGameOptions>> CriticalLiabilityThresholdTransformations()
            => Transform(GetAndTransform_LiabilityThreshold, CriticalLiabilityThresholds);
        
        public List<Func<LitigGameOptions, LitigGameOptions>> AdditionalLiabilityThresholdTransformations()
            => Transform(GetAndTransform_LiabilityThreshold, AdditionalLiabilityThresholds);

        public LitigGameOptions GetAndTransform_LiabilityThreshold(LitigGameOptions options, double liabilityThreshold) => GetAndTransform(options, " LT " + liabilityThreshold, g =>
        {
            PrecautionNegligenceDisputeGenerator disputeGenerator = (PrecautionNegligenceDisputeGenerator)options.LitigGameDisputeGenerator;
            disputeGenerator.LiabilityThreshold = liabilityThreshold;
            g.VariableSettings["Liability Threshold"] = liabilityThreshold;
        });

        // CourtNoise
        
        public List<Func<LitigGameOptions, LitigGameOptions>> CourtNoiseTransformations()
            => Transform(GetAndTransform_CourtNoise, CourtNoiseValues);

        public LitigGameOptions GetAndTransform_CourtNoise(LitigGameOptions options, double courtNoise) => GetAndTransform(options, " CN " + courtNoise, g =>
        {
            options.CourtLiabilityNoiseStdev = courtNoise;
            g.VariableSettings["Court Noise"] = courtNoise;
        });

        // Wrongful attribution probability
        
        public List<Func<LitigGameOptions, LitigGameOptions>> WrongfulAttributionTransformations()
            => Transform(GetAndTransform_WrongfulAttributionProbability, WrongfulAttributionProbabilities);

        public LitigGameOptions GetAndTransform_WrongfulAttributionProbability(LitigGameOptions options, double wrongfulAttributionProbability) => GetAndTransform(options, " WA " + wrongfulAttributionProbability, g =>
        {
            PrecautionNegligenceDisputeGenerator disputeGenerator = (PrecautionNegligenceDisputeGenerator)options.LitigGameDisputeGenerator;
            disputeGenerator.ProbabilityAccidentWrongfulAttribution = wrongfulAttributionProbability;
            g.VariableSettings["Wrongful Attribution Probability"] = wrongfulAttributionProbability switch
            {
                0 => "None",
                0.00001 => "Normal",
                0.0001 => "High",
                _ => throw new NotImplementedException()
            };
        });

        // Misestimation (precaution cost perception multiplier)

        const int maxSignalsAndOffersWhenMisestimatingCost = 5;

        public List<Func<LitigGameOptions, LitigGameOptions>> CostMisestimationTransformations()
            => Transform(GetAndTransform_DCostMisestimation, CostMisestimations);

        public LitigGameOptions GetAndTransform_DCostMisestimation(LitigGameOptions options, double precautionCostPerceptionMultiplier) => GetAndTransform(options, " PCPM " + precautionCostPerceptionMultiplier, g =>
        {
            PrecautionNegligenceDisputeGenerator disputeGenerator = (PrecautionNegligenceDisputeGenerator)options.LitigGameDisputeGenerator;
            disputeGenerator.CostMisestimationFactor = precautionCostPerceptionMultiplier;
            bool reducedNumSignalsOrOffers = false;
            if (options.NumOffers > maxSignalsAndOffersWhenMisestimatingCost)
            {
                options.NumOffers = maxSignalsAndOffersWhenMisestimatingCost;
                reducedNumSignalsOrOffers = true;
            }
            if (options.NumLiabilityStrengthPoints > maxSignalsAndOffersWhenMisestimatingCost)
            {
                options.NumLiabilityStrengthPoints = maxSignalsAndOffersWhenMisestimatingCost;
                reducedNumSignalsOrOffers = true;
            }
            if (options.NumLiabilitySignals > maxSignalsAndOffersWhenMisestimatingCost)
            {
                options.NumLiabilitySignals = maxSignalsAndOffersWhenMisestimatingCost;
                reducedNumSignalsOrOffers = true;
            }
            g.VariableSettings["Cost Misestimation"] = precautionCostPerceptionMultiplier.ToString();
            g.VariableSettings["Num Signals and Offers"] = reducedNumSignalsOrOffers ? "Reduced": "Baseline";
        });

        #endregion


        #region Game sets generation

        public override List<GameOptions> FlattenAndOrderGameSets(List<List<GameOptions>> gamesSets)
        {
            var eachGameIndependently = gamesSets.SelectMany(x => x)
                .OrderBy(x => ((LitigGameOptions)x).LoserPaysOnlyLargeMarginOfVictory) // place here anything that will change the game tree size
                .ToList();
            return eachGameIndependently.Select(x => (GameOptions) x).ToList();
        }

        public override List<VariableCombinationGenerator.Dimension<LitigGameOptions>> GetVariationSetsInfo()
        {
            // critical + extras (paired) ----------------------------------------
            var dims = new List<VariableCombinationGenerator.Dimension<LitigGameOptions>>
            {
                // super-critical (everything is always run against both of these)
                new("RiskAversion",
                    CriticalRiskAversionTransformations(),
                    AdditionalRiskAversionTransformations(),
                    IsGlobal: true),

                new("CostsMultiplier",
                    CriticalCostsMultiplierTransformations(),
                    AdditionalCostsMultiplierTransformations(),
                    IsGlobal: true
                    ),

                // critical with modifiers (critical are always permuted against one another)

                new("FeeShiftingMultiplier",
                    CriticalFeeShiftingMultiplierTransformations(),
                    AdditionalFeeShiftingMultiplierTransformations()
                    ),

                new("DamagesMultiplier",
                    CriticalDamagesMultiplierTransformations(),
                    AdditionalDamagesMultiplierTransformations()),

                new("LiabilityThreshold",
                    CriticalLiabilityThresholdTransformations(),
                    AdditionalLiabilityThresholdTransformations()
                    ),

                new("PrecautionCost",
                    CriticalPrecautionCostTransformations(),
                    AdditionalPrecautionCostTransformations()
                    ),

                // modifier-only variables
                new("Noise",
                    null,
                    NoiseTransformations()
                    ),

                new("RelativeCosts",
                    null,
                    PRelativeCostsTransformations()
                    ),

                new ("CourtNoise",
                    null,
                    CourtNoiseTransformations()
                    ),

                new ("WrongfulAttributionProbability",
                    null,
                    WrongfulAttributionTransformations()
                    ),

                new("CostsStartFraction",
                    null,
                    ProportionOfCostsAtBeginningTransformations()
                    ),

                new("CostMisestimation",
                    null,
                    CostMisestimationTransformations()
                         ),
            };

            // prune empty modifier-only dimensions ------------------------------
            dims = dims.Where(d => d.CriticalTransforms != null ||
                                   d.ModifierTransforms?.Count > 0).ToList();

            return dims;
        }

        public override List<SimulationSetsIdentifier> GetSimulationSetsIdentifiers(SimulationSetsTransformer transformer = null)
        {
            var variationSets = GetVariationSets(); // for specifying what diagrams etc. to produce if that depends on the options as they have been set, e.g. in LitigGameOptionsGenerator.

            var varyingNothing = new List<SimulationIdentifier>()
            {
                new SimulationIdentifier("Baseline", DefaultVariableValues),
            };

            
            var varyingFeeShiftingMultiplier = new List<SimulationIdentifier>()
            {
                new SimulationIdentifier("American", DefaultVariableValues.WithReplacement("Fee Shifting Multiplier", "0")),
                new SimulationIdentifier("Half", DefaultVariableValues.WithReplacement("Fee Shifting Multiplier", "0.5")),
                new SimulationIdentifier("English", DefaultVariableValues.WithReplacement("Fee Shifting Multiplier", "1")),
                new SimulationIdentifier("Double", DefaultVariableValues.WithReplacement("Fee Shifting Multiplier", "2")),
                new SimulationIdentifier("X Five", DefaultVariableValues.WithReplacement("Fee Shifting Multiplier", "5")),
            };

            var varyingDamagesMultiplier = new List<SimulationIdentifier>()
            {
                new SimulationIdentifier("0.5", DefaultVariableValues.WithReplacement("Damages Multiplier", "0.5")),
                new SimulationIdentifier("1", DefaultVariableValues.WithReplacement("Damages Multiplier", "1")),
                new SimulationIdentifier("2", DefaultVariableValues.WithReplacement("Damages Multiplier", "2")),
                // omit additional (we could add this back in, but not in combination with other things): new SimulationIdentifier("4", DefaultVariableValues.WithReplacement("Damages Multiplier", "4")),
            };

            var varyingLiabilityThreshold = new List<SimulationIdentifier>()
            {
                new SimulationIdentifier("Strict Liability", DefaultVariableValues.WithReplacement("Liability Threshold", "0")),
                new SimulationIdentifier("Low", DefaultVariableValues.WithReplacement("Liability Threshold", "0.8")),
                new SimulationIdentifier("Normal", DefaultVariableValues.WithReplacement("Liability Threshold", "1")),
                new SimulationIdentifier("High", DefaultVariableValues.WithReplacement("Liability Threshold", "1.2")),
                new SimulationIdentifier("Very High", DefaultVariableValues.WithReplacement("Liability Threshold", "2")),
            };
            
            var varyingUnitPrecautionCost = new List<SimulationIdentifier>()
            {
                new SimulationIdentifier("Very Low", DefaultVariableValues.WithReplacement("Unit Precaution Cost", "1E-06")),
                new SimulationIdentifier("Low", DefaultVariableValues.WithReplacement("Unit Precaution Cost", "8E-06")),
                new SimulationIdentifier("Normal", DefaultVariableValues.WithReplacement("Unit Precaution Cost", "1E-05")),
                new SimulationIdentifier("High", DefaultVariableValues.WithReplacement("Unit Precaution Cost", "1.2E-05")),
                // DEBUG -- add later or consider changing Low to 5E-06 and High to 1.5E-05 new SimulationIdentifier("Very High", DefaultVariableValues.WithReplacement("Unit Precaution Cost", "2E-05")),
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
                new SimulationIdentifier("P Better", DefaultVariableValues.WithReplacement("Noise Multiplier P", "0.5").WithReplacement("Noise Multiplier D", "2")),
                new SimulationIdentifier("Equal Information", DefaultVariableValues.WithReplacement("Noise Multiplier P", "1").WithReplacement("Noise Multiplier D", "1")),
                new SimulationIdentifier("D Better", DefaultVariableValues.WithReplacement("Noise Multiplier P", "2").WithReplacement("Noise Multiplier D", "0.5")),
            };

            var varyingCourtNoise = new List<SimulationIdentifier>()
            {
                new SimulationIdentifier("Perfect", DefaultVariableValues.WithReplacement("Court Noise", "0")),
                new SimulationIdentifier("Normal", DefaultVariableValues.WithReplacement("Court Noise", "0.2")),
                new SimulationIdentifier("Noisy", DefaultVariableValues.WithReplacement("Court Noise", "0.4")),
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

            var varyingTimingOfCosts = new List<SimulationIdentifier>()
            {
                new SimulationIdentifier("0", DefaultVariableValues.WithReplacement("Proportion of Costs at Beginning", "0")),
                new SimulationIdentifier("0.25", DefaultVariableValues.WithReplacement("Proportion of Costs at Beginning", "0.25")),
                new SimulationIdentifier("0.5", DefaultVariableValues.WithReplacement("Proportion of Costs at Beginning", "0.5")),
                new SimulationIdentifier("0.75", DefaultVariableValues.WithReplacement("Proportion of Costs at Beginning", "0.75")),
                new SimulationIdentifier("1", DefaultVariableValues.WithReplacement("Proportion of Costs at Beginning", "1")),
            };

            // for cost misestimation, we also need to specify if the number of signals or offers is reduced
            var disputeGeneratorSettings = variationSets.Select(x => (((LitigGameOptions)x), ((LitigGameOptions)x).LitigGameDisputeGenerator as PrecautionNegligenceDisputeGenerator)).ToList();
            var exampleWithoutCostMisestimation = disputeGeneratorSettings.Where(x => x.Item2.CostMisestimationFactor == 1).First();
            var exampleWithCostMisestimation = disputeGeneratorSettings.Where(x => x.Item2.CostMisestimationFactor != 1).First();
            bool reducedSignalsOrOffers = exampleWithCostMisestimation.Item1.NumLiabilitySignals + exampleWithCostMisestimation.Item1.NumOffers < exampleWithoutCostMisestimation.Item1.NumLiabilitySignals + exampleWithoutCostMisestimation.Item1.NumOffers;
            string numSignalsAndOffersString = reducedSignalsOrOffers ? "Reduced" : "Baseline";
            var varyingCostMisestimations = new List<SimulationIdentifier>()
            {
                new SimulationIdentifier("1", DefaultVariableValues.WithReplacement("Cost Misestimation", "1").WithReplacement("Num Signals and Offers", numSignalsAndOffersString)), // this is the key -- we need the baseline value to be with lower number of signals and offers for comparability
                new SimulationIdentifier("2", DefaultVariableValues.WithReplacement("Cost Misestimation", "2").WithReplacement("Num Signals and Offers", numSignalsAndOffersString)),
                new SimulationIdentifier("5", DefaultVariableValues.WithReplacement("Cost Misestimation", "5").WithReplacement("Num Signals and Offers", numSignalsAndOffersString)),
                new SimulationIdentifier("10", DefaultVariableValues.WithReplacement("Cost Misestimation", "10").WithReplacement("Num Signals and Offers", numSignalsAndOffersString)),
            };

            var varyingFeeShiftingWithHighMisestimation = new List<SimulationIdentifier>()
            {
                new SimulationIdentifier("American", DefaultVariableValues.WithReplacement("Cost Misestimation", "5").WithReplacement("Fee Shifting Multiplier", "0").WithReplacement("Num Signals and Offers", numSignalsAndOffersString)),
                new SimulationIdentifier("English", DefaultVariableValues.WithReplacement("Cost Misestimation", "5").WithReplacement("Fee Shifting Multiplier", "1").WithReplacement("Num Signals and Offers", numSignalsAndOffersString)),
                new SimulationIdentifier("Double", DefaultVariableValues.WithReplacement("Cost Misestimation", "5").WithReplacement("Fee Shifting Multiplier", "2").WithReplacement("Num Signals and Offers", numSignalsAndOffersString)),
            };

            var varyingDamagesWithHighMisestimation = new List<SimulationIdentifier>()
            {
                new SimulationIdentifier("0.5", DefaultVariableValues.WithReplacement("Cost Misestimation", "5").WithReplacement("Damages Multiplier", "0.5").WithReplacement("Num Signals and Offers", numSignalsAndOffersString)),
                new SimulationIdentifier("1", DefaultVariableValues.WithReplacement("Cost Misestimation", "5").WithReplacement("Damages Multiplier", "1").WithReplacement("Num Signals and Offers", numSignalsAndOffersString)),
                new SimulationIdentifier("2", DefaultVariableValues.WithReplacement("Cost Misestimation", "5").WithReplacement("Damages Multiplier", "2").WithReplacement("Num Signals and Offers", numSignalsAndOffersString)),
            };
            
            var varyingWrongfulAttribution = new List<SimulationIdentifier>()
            {
                new SimulationIdentifier("None", DefaultVariableValues.WithReplacement("Wrongful Attribution Probability", "None")),
                new SimulationIdentifier("Normal", DefaultVariableValues.WithReplacement("Wrongful Attribution Probability", "Normal")),
                new SimulationIdentifier("High", DefaultVariableValues.WithReplacement("Wrongful Attribution Probability", "High")),
            };

            var simulationSetsIdentifiers = new List<SimulationSetsIdentifier>()
            {
                new SimulationSetsIdentifier("Baseline", varyingNothing),
                new SimulationSetsIdentifier("Fee Shifting Multiplier", varyingFeeShiftingMultiplier),
                new SimulationSetsIdentifier("Liability Threshold", varyingLiabilityThreshold),
                new SimulationSetsIdentifier("Unit Precaution Cost", varyingUnitPrecautionCost),
                new SimulationSetsIdentifier("Damages Multiplier", varyingDamagesMultiplier),
                // TODO new ArticleVariationInfoSets("Fee Shifting Rule (Liability Issue)", varyingFeeShiftingRule_LiabilityUncertain),
                new SimulationSetsIdentifier("Noise Multiplier", varyingNoiseMultipliersBoth),
                new SimulationSetsIdentifier("Information Asymmetry", varyingNoiseMultipliersAsymmetric),
                new SimulationSetsIdentifier("Court Quality", varyingCourtNoise),
                new SimulationSetsIdentifier("Relative Costs", varyingRelativeCosts),
                new SimulationSetsIdentifier("Risk Aversion", varyingRiskAversion),
                new SimulationSetsIdentifier("Risk Aversion Asymmetry", varyingRiskAversionAsymmetry),
                new SimulationSetsIdentifier("Proportion of Costs at Beginning", varyingTimingOfCosts),
                new SimulationSetsIdentifier("Misestimation Level", varyingCostMisestimations),
                new SimulationSetsIdentifier("Fee Shifting Multiplier (High Misestimation)", varyingFeeShiftingWithHighMisestimation),
                new SimulationSetsIdentifier("Damages Multiplier (High Misestimation)", varyingDamagesWithHighMisestimation),
                new SimulationSetsIdentifier("Wrongful Attribution Probability", varyingWrongfulAttribution),
            };
            
            simulationSetsIdentifiers = PerformArticleVariationInfoSetsTransformation(transformer, simulationSetsIdentifiers);
            AddPairwiseSimulationSets(simulationSetsIdentifiers);

            return simulationSetsIdentifiers;
        }

        private void AddPairwiseSimulationSets(List<SimulationSetsIdentifier> simulationSetsIdentifiers)
        {
            var extremes = new Dictionary<string, (string Low, string High)>
            {
                { "Fee Shifting Multiplier", ( "1",       "2"       ) },
                { "Damages Multiplier",      ( "0.5",     "2"       ) },
                { "Liability Threshold",     ( "0.8",     "1.2"     ) },
                { "Unit Precaution Cost",    ( "8E-06",   "1.2E-05" ) },
            };

            var variableNames = new[]
            {
                "Fee Shifting Multiplier",
                "Damages Multiplier",
                "Liability Threshold",
                "Unit Precaution Cost",
            };

            for (int i = 0; i < variableNames.Length - 1; i++)
            {
                for (int j = i + 1; j < variableNames.Length; j++)
                {
                    var a = variableNames[i];
                    var b = variableNames[j];
                    var setName = $"{a} and {b}";
                    simulationSetsIdentifiers.Add(
                        CreatePairSimulationSet(setName, a, b, extremes[a], extremes[b]));
                }
            }
        }

        private static readonly Dictionary<string, string> VariableAbbreviations = new Dictionary<string, string>
        {
            { "Fee Shifting Multiplier", "F" },
            { "Damages Multiplier",      "D"  },
            { "Liability Threshold",     "L"  },
            { "Unit Precaution Cost",    "P" },
        };

        private SimulationSetsIdentifier CreatePairSimulationSet(
            string setName,
            string variableAName,
            string variableBName,
            (string Low, string High) variableAExtremes,
            (string Low, string High) variableBExtremes)
        {
            string a = VariableAbbreviations[variableAName];
            string b = VariableAbbreviations[variableBName];

            var ids = new List<SimulationIdentifier>
            {
                new SimulationIdentifier("Baseline", DefaultVariableValues),
                new SimulationIdentifier($"Low {a}, Low {b}",
                    DefaultVariableValues.WithReplacement(variableAName, variableAExtremes.Low)
                                         .WithReplacement(variableBName, variableBExtremes.Low)),
                new SimulationIdentifier($"Low {a}, High {b}",
                    DefaultVariableValues.WithReplacement(variableAName, variableAExtremes.Low)
                                         .WithReplacement(variableBName, variableBExtremes.High)),
                new SimulationIdentifier($"High {a}, Low {b}",
                    DefaultVariableValues.WithReplacement(variableAName, variableAExtremes.High)
                                         .WithReplacement(variableBName, variableBExtremes.Low)),
                new SimulationIdentifier($"High {a}, High {b}",
                    DefaultVariableValues.WithReplacement(variableAName, variableAExtremes.High)
                                         .WithReplacement(variableBName, variableBExtremes.High)),
            };

            return new SimulationSetsIdentifier(setName, ids);
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
