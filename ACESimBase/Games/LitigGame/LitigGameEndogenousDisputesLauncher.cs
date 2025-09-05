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
                    ("Court Noise", "Baseline"),
                    ("Issue", "Liability"),
                    ("Proportion of Costs at Beginning", "0.5"),
                    ("Cost Misestimation", "1"),
                    ("Num Signals and Offers", "Baseline"),
                    ("Wrongful Attribution Probability", "Normal"),
                    ("Adjudication Mode", "Baseline")
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

        public record class CourtNoiseInfo(double noiseStdev, bool doProbit, double? probitScale, string name);

        public CourtNoiseInfo[] CourtNoiseValues = new CourtNoiseInfo[] { new CourtNoiseInfo(0.2, true, 0.25, "Baseline"), new CourtNoiseInfo(0, false, null, "Perfect"), new CourtNoiseInfo(0.1, true, 0.25, "Low Noise"), new CourtNoiseInfo(0.2, true, 0.1, "Low Indet"),  new CourtNoiseInfo(0.4, true, 0.25, "High Noise"), new CourtNoiseInfo(0.2, true, 0.5, "High Indet"), new CourtNoiseInfo(0.2, true, 0.5, "High Noise and Indet") };

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

        public override string MasterReportNameForDistributedProcessing => MasterReportNamePrefix + "007";

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

        public LitigGameOptions GetAndTransform_CourtNoise(LitigGameOptions options, CourtNoiseInfo courtNoiseInfo) => GetAndTransform(options, " CN " + courtNoiseInfo.name, g =>
        {
            options.CourtLiabilityNoiseStdev = courtNoiseInfo.noiseStdev;
            PrecautionNegligenceDisputeGenerator disputeGenerator = (PrecautionNegligenceDisputeGenerator)options.LitigGameDisputeGenerator;
            disputeGenerator.CourtDecisionRule = courtNoiseInfo.doProbit ? CourtDecisionRule.ProbitThreshold : CourtDecisionRule.DeterministicThreshold;
            disputeGenerator.CourtProbitScale = courtNoiseInfo.probitScale ?? 0;
            g.VariableSettings["Court Noise"] = courtNoiseInfo.name;
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

        // Perfect adjudication

        public List<Func<LitigGameOptions, LitigGameOptions>> PerfectAdjudicationTransformations()
            => Transform(GetAndTransform_PerfectAdjudication, new[] { true });

        public LitigGameOptions GetAndTransform_PerfectAdjudication(LitigGameOptions options, bool enabled) =>
            GetAndTransform(options, " PA", g =>
            {
                if (!enabled)
                    return;

                // “Perfect” adjudication + zero litigation/bargaining costs
                g.CourtDamagesNoiseStdev = 0.0;
                g.CourtLiabilityNoiseStdev = 0.0;
                g.DTrialCosts = 0.0;
                g.PTrialCosts = 0.0;
                g.PerPartyCostsLeadingUpToBargainingRound = 0.0;
                g.PFilingCost = 0.0;
                g.DAnswerCost = 0.0;
                PrecautionNegligenceDisputeGenerator pndg = g.LitigGameDisputeGenerator as PrecautionNegligenceDisputeGenerator;
                pndg.CourtDecisionRule = CourtDecisionRule.DeterministicThreshold;

                // keep reporting columns consistent with existing sets
                g.VariableSettings["Court Noise"] = "Perfect";
                g.VariableSettings["Noise Multiplier P"] = "0";
                g.VariableSettings["Noise Multiplier D"] = "0";
                g.VariableSettings["Costs Multiplier"] = "0";
                g.VariableSettings["Adjudication Mode"] = "Perfect";
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
            // If the global-core flag is on, limit to some specific permutations.
            if (OnlyRunCoreSimulations)
            {
                return new List<VariableCombinationGenerator.Dimension<LitigGameOptions>>
                {
                    new("RiskAversion",
                        CriticalRiskAversionTransformations(),
                        null,
                        IsGlobal: true),

                    new("CostsMultiplier",
                        CriticalCostsMultiplierTransformations(),
                        null,
                        IsGlobal: true),

                    new("FeeShiftingMultiplier",
                        null, CriticalFeeShiftingMultiplierTransformations()
                        ),

                    new("DamagesMultiplier",
                        null, CriticalDamagesMultiplierTransformations()
                        ),

                    new("LiabilityThreshold",
                        null, CriticalLiabilityThresholdTransformations()
                        ),
                };
            }

            var dims = new List<VariableCombinationGenerator.Dimension<LitigGameOptions>>
            {
                new("RiskAversion",
                    CriticalRiskAversionTransformations(),
                    AdditionalRiskAversionTransformations(),
                    IsGlobal: true),

                new("CostsMultiplier",
                    CriticalCostsMultiplierTransformations(),
                    AdditionalCostsMultiplierTransformations(),
                    IsGlobal: true
                    ),

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
                    CostMisestimationTransformations(),
                    IncludeBaselineValueForNoncritical: true
                    ),

                new("PerfectAdjudication",
                    null,
                    PerfectAdjudicationTransformations(),
                    IncludeBaselineValueForNoncritical: true
                    ),
            };

            dims = dims.Where(d => d.CriticalTransforms != null ||
                                   d.ModifierTransforms?.Count > 0).ToList();

            return dims;
        }

        public override List<SimulationSetsIdentifier> GetSimulationSetsIdentifiers(SimulationSetsTransformer transformer = null)
        {
            if (OnlyRunCoreSimulations)
            {
                var variationSets = GetVariationSets();

                var varyingNothing = new List<SimulationIdentifier>
                {
                    new SimulationIdentifier("Baseline Values", DefaultVariableValues),
                };

                // Critical values only for Costs Multiplier: 1.0, 0.25, 0.5, 2.0, 4.0
                var varyingCostsMultiplier = new List<SimulationIdentifier>
                {
                    new SimulationIdentifier("0.25", DefaultVariableValues.WithReplacement("Costs Multiplier", "0.25")),
                    new SimulationIdentifier("1",    DefaultVariableValues.WithReplacement("Costs Multiplier", "1")),
                    new SimulationIdentifier("4",    DefaultVariableValues.WithReplacement("Costs Multiplier", "4")),
                };

                // Critical values only for Risk Aversion: Risk Neutral, Moderately Risk Averse
                var varyingRiskAversion = new List<SimulationIdentifier>
                {
                    new SimulationIdentifier("Risk Neutral",           DefaultVariableValues.WithReplacement("Risk Aversion", "Risk Neutral")),
                    new SimulationIdentifier("Moderately Risk Averse", DefaultVariableValues.WithReplacement("Risk Aversion", "Moderately Risk Averse")),
                };

                var simulationSetsIdentifiers = new List<SimulationSetsIdentifier>
                {
                    new SimulationSetsIdentifier("Baseline",         varyingNothing),
                    new SimulationSetsIdentifier("Costs Multiplier", varyingCostsMultiplier),
                    new SimulationSetsIdentifier("Risk Aversion",    varyingRiskAversion),
                };

                simulationSetsIdentifiers = PerformArticleVariationInfoSetsTransformation(transformer, simulationSetsIdentifiers);

                SimulationIdentifier BindToUniqueOptionSet(SimulationIdentifier sim)
                {
                    static string S(object v) => v?.ToString();

                    bool Matches(GameOptions opt) =>
                        sim.columnMatches.All(cm =>
                            opt.VariableSettings.TryGetValue(cm.columnName, out var v) && S(v) == cm.expectedValue);

                    var candidates = variationSets.Where(Matches).ToList();
                    if (candidates.Count != 1)
                        throw new InvalidOperationException(
                            $"Simulation identifier '{sim.nameForSimulation}' resolves to {candidates.Count} option sets; expected exactly one.");

                    var bound = sim;
                    foreach (var kv in candidates[0].VariableSettings.OrderBy(k => k.Key))
                        bound = bound.With(kv.Key, S(kv.Value));

                    return bound;
                }

                simulationSetsIdentifiers = simulationSetsIdentifiers
                    .Select(set => set with
                    {
                        simulationIdentifiers = set.simulationIdentifiers.Select(BindToUniqueOptionSet).ToList()
                    })
                    .ToList();

                return simulationSetsIdentifiers;
            }
            else
            {
                var variationSets = GetVariationSets();

                var varyingNothing = new List<SimulationIdentifier>()
                {
                    new SimulationIdentifier("Baseline Values", DefaultVariableValues),
                };

                var varyingFeeShiftingMultiplier = new List<SimulationIdentifier>()
                {
                    new SimulationIdentifier("American", DefaultVariableValues.WithReplacement("Fee Shifting Multiplier", "0")),
                    new SimulationIdentifier("Half",     DefaultVariableValues.WithReplacement("Fee Shifting Multiplier", "0.5")),
                    new SimulationIdentifier("English",  DefaultVariableValues.WithReplacement("Fee Shifting Multiplier", "1")),
                    new SimulationIdentifier("Double",   DefaultVariableValues.WithReplacement("Fee Shifting Multiplier", "2")),
                    new SimulationIdentifier("X Five",   DefaultVariableValues.WithReplacement("Fee Shifting Multiplier", "5")),
                };

                var varyingDamagesMultiplier = new List<SimulationIdentifier>()
                {
                    new SimulationIdentifier("0.5", DefaultVariableValues.WithReplacement("Damages Multiplier", "0.5")),
                    new SimulationIdentifier("1",   DefaultVariableValues.WithReplacement("Damages Multiplier", "1")),
                    new SimulationIdentifier("2",   DefaultVariableValues.WithReplacement("Damages Multiplier", "2")),
                    new SimulationIdentifier("4",   DefaultVariableValues.WithReplacement("Damages Multiplier", "4")),
                };

                var varyingLiabilityThreshold = new List<SimulationIdentifier>()
                {
                    new SimulationIdentifier("Strict Liability", DefaultVariableValues.WithReplacement("Liability Threshold", "0")),
                    new SimulationIdentifier("Low",              DefaultVariableValues.WithReplacement("Liability Threshold", "0.8")),
                    new SimulationIdentifier("Normal",           DefaultVariableValues.WithReplacement("Liability Threshold", "1")),
                    new SimulationIdentifier("High",             DefaultVariableValues.WithReplacement("Liability Threshold", "1.2")),
                    new SimulationIdentifier("Very High",        DefaultVariableValues.WithReplacement("Liability Threshold", "2")),
                };

                var varyingUnitPrecautionCost = new List<SimulationIdentifier>()
                {
                    new SimulationIdentifier("Very Low", DefaultVariableValues.WithReplacement("Unit Precaution Cost", "1E-06")),
                    new SimulationIdentifier("Low",      DefaultVariableValues.WithReplacement("Unit Precaution Cost", "8E-06")),
                    new SimulationIdentifier("Normal",   DefaultVariableValues.WithReplacement("Unit Precaution Cost", "1E-05")),
                    new SimulationIdentifier("High",     DefaultVariableValues.WithReplacement("Unit Precaution Cost", "1.2E-05")),
                };

                var varyingNoiseMultipliersBoth = new List<SimulationIdentifier>()
                {
                    new SimulationIdentifier(".25", DefaultVariableValues.WithReplacement("Noise Multiplier P", "0.25").WithReplacement("Noise Multiplier D", "0.25")),
                    new SimulationIdentifier(".5",  DefaultVariableValues.WithReplacement("Noise Multiplier P", "0.5"). WithReplacement("Noise Multiplier D", "0.5")),
                    new SimulationIdentifier("1",   DefaultVariableValues.WithReplacement("Noise Multiplier P", "1").   WithReplacement("Noise Multiplier D", "1")),
                    new SimulationIdentifier("2",   DefaultVariableValues.WithReplacement("Noise Multiplier P", "2").   WithReplacement("Noise Multiplier D", "2")),
                    new SimulationIdentifier("4",   DefaultVariableValues.WithReplacement("Noise Multiplier P", "4").   WithReplacement("Noise Multiplier D", "4")),
                };

                var varyingNoiseMultipliersAsymmetric = new List<SimulationIdentifier>()
                {
                    new SimulationIdentifier("P Better",         DefaultVariableValues.WithReplacement("Noise Multiplier P", "0.5").WithReplacement("Noise Multiplier D", "2")),
                    new SimulationIdentifier("Equal Information",DefaultVariableValues.WithReplacement("Noise Multiplier P", "1").  WithReplacement("Noise Multiplier D", "1")),
                    new SimulationIdentifier("D Better",         DefaultVariableValues.WithReplacement("Noise Multiplier P", "2").  WithReplacement("Noise Multiplier D", "0.5")),
                };
                
                var varyingCourtNoise = new List<SimulationIdentifier>()
                {
                    new SimulationIdentifier("Perfect", DefaultVariableValues.WithReplacement("Court Noise", "Perfect")),
                    new SimulationIdentifier("Baseline",  DefaultVariableValues.WithReplacement("Court Noise", "Baseline")),
                    new SimulationIdentifier("Low Noise", DefaultVariableValues.WithReplacement("Court Noise", "Low Noise")),
                    new SimulationIdentifier("Low Indet",  DefaultVariableValues.WithReplacement("Court Noise", "Low Indet.")),
                    new SimulationIdentifier("High Noise", DefaultVariableValues.WithReplacement("Court Noise", "High Noise")),
                    new SimulationIdentifier("High Indet",  DefaultVariableValues.WithReplacement("Court Noise", "High Indet.")),
                    new SimulationIdentifier("High Noise and Indet",  DefaultVariableValues.WithReplacement("Court Noise", "High Noise, Indet.")),
                };

                var varyingRelativeCosts = new List<SimulationIdentifier>()
                {
                    new SimulationIdentifier("P Lower Costs", DefaultVariableValues.WithReplacement("Relative Costs", "0.5")),
                    new SimulationIdentifier("Equal",         DefaultVariableValues.WithReplacement("Relative Costs", "1")),
                    new SimulationIdentifier("P Higher Costs",DefaultVariableValues.WithReplacement("Relative Costs", "2")),
                };

                var varyingRiskAversion = new List<SimulationIdentifier>()
                {
                    new SimulationIdentifier("Risk Neutral",     DefaultVariableValues.WithReplacement("Risk Aversion", "Risk Neutral")),
                    new SimulationIdentifier("Mildly Averse",    DefaultVariableValues.WithReplacement("Risk Aversion", "Mildly Risk Averse")),
                    new SimulationIdentifier("Moderately Averse",DefaultVariableValues.WithReplacement("Risk Aversion", "Moderately Risk Averse")),
                    new SimulationIdentifier("Highly Averse",    DefaultVariableValues.WithReplacement("Risk Aversion", "Highly Risk Averse")),
                };

                var varyingRiskAversionAsymmetry = new List<SimulationIdentifier>()
                {
                    new SimulationIdentifier("P Risk Averse",     DefaultVariableValues.WithReplacement("Risk Aversion", "P Risk Averse")),
                    new SimulationIdentifier("D Risk Averse",     DefaultVariableValues.WithReplacement("Risk Aversion", "D Risk Averse")),
                    new SimulationIdentifier("P More Risk Averse",DefaultVariableValues.WithReplacement("Risk Aversion", "P More Risk Averse")),
                    new SimulationIdentifier("D More Risk Averse",DefaultVariableValues.WithReplacement("Risk Aversion", "D More Risk Averse")),
                };

                var varyingTimingOfCosts = new List<SimulationIdentifier>()
                {
                    new SimulationIdentifier("0",    DefaultVariableValues.WithReplacement("Proportion of Costs at Beginning", "0")),
                    new SimulationIdentifier("0.25", DefaultVariableValues.WithReplacement("Proportion of Costs at Beginning", "0.25")),
                    new SimulationIdentifier("0.5",  DefaultVariableValues.WithReplacement("Proportion of Costs at Beginning", "0.5")),
                    new SimulationIdentifier("0.75", DefaultVariableValues.WithReplacement("Proportion of Costs at Beginning", "0.75")),
                    new SimulationIdentifier("1",    DefaultVariableValues.WithReplacement("Proportion of Costs at Beginning", "1")),
                };

                var disputeGeneratorSettings = variationSets
                    .Select(x => (((LitigGameOptions)x), ((LitigGameOptions)x).LitigGameDisputeGenerator as PrecautionNegligenceDisputeGenerator))
                    .ToList();
                var exampleWithoutCostMisestimation = disputeGeneratorSettings.First(x => x.Item2.CostMisestimationFactor == 1);
                var exampleWithCostMisestimation    = disputeGeneratorSettings.First(x => x.Item2.CostMisestimationFactor != 1);
                bool reducedSignalsOrOffers = exampleWithCostMisestimation.Item1.NumLiabilitySignals + exampleWithCostMisestimation.Item1.NumOffers
                                            < exampleWithoutCostMisestimation.Item1.NumLiabilitySignals + exampleWithoutCostMisestimation.Item1.NumOffers;
                string numSignalsAndOffersString = reducedSignalsOrOffers ? "Reduced" : "Baseline";

                var varyingCostMisestimations = new List<SimulationIdentifier>()
                {
                    new SimulationIdentifier("1",  DefaultVariableValues.WithReplacement("Cost Misestimation", "1"). WithReplacement("Num Signals and Offers", numSignalsAndOffersString)),
                    new SimulationIdentifier("2",  DefaultVariableValues.WithReplacement("Cost Misestimation", "2"). WithReplacement("Num Signals and Offers", numSignalsAndOffersString)),
                    new SimulationIdentifier("5",  DefaultVariableValues.WithReplacement("Cost Misestimation", "5"). WithReplacement("Num Signals and Offers", numSignalsAndOffersString)),
                    new SimulationIdentifier("10", DefaultVariableValues.WithReplacement("Cost Misestimation", "10").WithReplacement("Num Signals and Offers", numSignalsAndOffersString)),
                };

                var varyingFeeShiftingWithHighMisestimation = new List<SimulationIdentifier>()
                {
                    new SimulationIdentifier("American", DefaultVariableValues.WithReplacement("Cost Misestimation", "5").WithReplacement("Fee Shifting Multiplier", "0").WithReplacement("Num Signals and Offers", numSignalsAndOffersString)),
                    new SimulationIdentifier("English",  DefaultVariableValues.WithReplacement("Cost Misestimation", "5").WithReplacement("Fee Shifting Multiplier", "1").WithReplacement("Num Signals and Offers", numSignalsAndOffersString)),
                    new SimulationIdentifier("Double",   DefaultVariableValues.WithReplacement("Cost Misestimation", "5").WithReplacement("Fee Shifting Multiplier", "2").WithReplacement("Num Signals and Offers", numSignalsAndOffersString)),
                };

                var varyingDamagesWithHighMisestimation = new List<SimulationIdentifier>()
                {
                    new SimulationIdentifier("0.5", DefaultVariableValues.WithReplacement("Cost Misestimation", "5").WithReplacement("Damages Multiplier", "0.5").WithReplacement("Num Signals and Offers", numSignalsAndOffersString)),
                    new SimulationIdentifier("1",   DefaultVariableValues.WithReplacement("Cost Misestimation", "5").WithReplacement("Damages Multiplier", "1").  WithReplacement("Num Signals and Offers", numSignalsAndOffersString)),
                    new SimulationIdentifier("2",   DefaultVariableValues.WithReplacement("Cost Misestimation", "5").WithReplacement("Damages Multiplier", "2").  WithReplacement("Num Signals and Offers", numSignalsAndOffersString)),
                };

                var varyingWrongfulAttribution = new List<SimulationIdentifier>()
                {
                    new SimulationIdentifier("None",   DefaultVariableValues.WithReplacement("Wrongful Attribution Probability", "None")),
                    new SimulationIdentifier("Normal", DefaultVariableValues.WithReplacement("Wrongful Attribution Probability", "Normal")),
                    new SimulationIdentifier("High",   DefaultVariableValues.WithReplacement("Wrongful Attribution Probability", "High")),
                };

                var varyingAdjudicationMode = new List<SimulationIdentifier>()
                {
                    new SimulationIdentifier("Baseline", DefaultVariableValues.WithReplacement("Adjudication Mode", "Baseline")),
                    new SimulationIdentifier("Perfect",
                        DefaultVariableValues
                            .WithReplacement("Adjudication Mode", "Perfect")
                            .WithReplacement("Court Noise",       "Perfect")
                            .WithReplacement("Noise Multiplier P","0")
                            .WithReplacement("Noise Multiplier D","0")
                            .WithReplacement("Costs Multiplier",  "0")),
                };

                var simulationSetsIdentifiers = new List<SimulationSetsIdentifier>()
                {
                    new SimulationSetsIdentifier("Baseline", varyingNothing),
                    new SimulationSetsIdentifier("Fee Shifting Multiplier", varyingFeeShiftingMultiplier),
                    new SimulationSetsIdentifier("Liability Threshold", varyingLiabilityThreshold),
                    new SimulationSetsIdentifier("Unit Precaution Cost", varyingUnitPrecautionCost),
                    new SimulationSetsIdentifier("Damages Multiplier", varyingDamagesMultiplier),
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
                    new SimulationSetsIdentifier("Adjudication Mode", varyingAdjudicationMode),
                };

                simulationSetsIdentifiers = PerformArticleVariationInfoSetsTransformation(transformer, simulationSetsIdentifiers);
                AddPairwiseSimulationSets(simulationSetsIdentifiers);

                // --- NEW: subset-safe filter for non-core runs too, just in case transforms removed some columns
                var activeColumns = new HashSet<string>(variationSets.SelectMany(vs => vs.VariableSettings.Keys));
                bool RefersOnlyToActive(SimulationIdentifier sim) =>
                    sim.columnMatches.All(cm => activeColumns.Contains(cm.columnName));

                simulationSetsIdentifiers = simulationSetsIdentifiers
                    .Select(set => set with
                    {
                        simulationIdentifiers = set.simulationIdentifiers.Where(RefersOnlyToActive).ToList()
                    })
                    .Where(set => set.simulationIdentifiers.Count > 0)
                    .ToList();
                // --- END NEW

                SimulationIdentifier BindToUniqueOptionSet(SimulationIdentifier sim)
                {
                    static string S(object v) => v?.ToString();

                    bool Matches(GameOptions opt) =>
                        sim.columnMatches.All(cm =>
                            opt.VariableSettings.TryGetValue(cm.columnName, out var v) && S(v) == cm.expectedValue);

                    var candidates = variationSets.Where(Matches).ToList();
                    if (candidates.Count != 1)
                        throw new InvalidOperationException(
                            $"Simulation identifier '{sim.nameForSimulation}' resolves to {candidates.Count} option sets; expected exactly one.");

                    var bound = sim;
                    foreach (var kv in candidates[0].VariableSettings.OrderBy(k => k.Key))
                        bound = bound.With(kv.Key, S(kv.Value));

                    return bound;
                }

                simulationSetsIdentifiers = simulationSetsIdentifiers
                    .Select(set => set with
                    {
                        simulationIdentifiers = set.simulationIdentifiers.Select(BindToUniqueOptionSet).ToList()
                    })
                    .ToList();

                return simulationSetsIdentifiers;
            }
        }





        private void AddPairwiseSimulationSets(List<SimulationSetsIdentifier> simulationSetsIdentifiers)
        {
            var extremes = new Dictionary<string, (string Low, string High)>
            {
                { "Fee Shifting Multiplier", ( "0",       "2"       ) },
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

            Func<string, string> fixForNoFeeShifting = x => x.Replace("Low F", "No F");

            var ids = new List<SimulationIdentifier>
            {
                new SimulationIdentifier("Baseline", DefaultVariableValues),
                new SimulationIdentifier(fixForNoFeeShifting($"Low {a}, Low {b}"),
                    DefaultVariableValues.WithReplacement(variableAName, variableAExtremes.Low)
                                         .WithReplacement(variableBName, variableBExtremes.Low)),
                new SimulationIdentifier(fixForNoFeeShifting($"Low {a}, High {b}"),
                    DefaultVariableValues.WithReplacement(variableAName, variableAExtremes.Low)
                                         .WithReplacement(variableBName, variableBExtremes.High)),
                new SimulationIdentifier(fixForNoFeeShifting($"High {a}, Low {b}"),
                    DefaultVariableValues.WithReplacement(variableAName, variableAExtremes.High)
                                         .WithReplacement(variableBName, variableBExtremes.Low)),
                new SimulationIdentifier(fixForNoFeeShifting($"High {a}, High {b}"),
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
