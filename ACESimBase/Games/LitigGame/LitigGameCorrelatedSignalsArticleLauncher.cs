using ACESim.Util.DiscreteProbabilities;
using ACESimBase;
using ACESimBase.Games.LitigGame;
using ACESimBase.GameSolvingSupport.Settings;
using ACESimBase.Util.Collections;
using ACESimBase.Util.Combinatorics;
using ACESimBase.Util.Mathematics;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace ACESim
{
    /// <summary>
    /// Production launcher for the correlated-signals article. The focused production matrix
    /// crosses each substantive specification with costs and fee regimes and includes the
    /// baseline and moderate-risk-aversion 15-offer pairs as integrated discretization checks.
    /// </summary>
    public class LitigGameCorrelatedSignalsArticleLauncher : LitigGameLauncherBase
    {
        public enum ProductionRunPlan
        {
            LegacyTwoStructure,
            UniformBaselineSupplement,
            UnifiedThreeStructure,
            FocusedContinuousMerits,
            MultipleEquilibriaRobustness,
            IncreasedOfferGridRobustness,
        }

        public enum FocusedSpecification
        {
            Baseline,
            LowNoise,
            HighNoise,
            ModerateRiskAversion,
            LowNoiseModerateRiskAversion,
            DirectBinaryStateSignals,
            TruthConditionedLatentMerits,
            CenterWeightedContinuousMerits,
            PolarizedContinuousMerits,
            AllCostsAvoidable,
            AllCostsSunk,
            MandatoryFilingAndAnswering,
            MandatoryFilingAnsweringNoExit,
        }

        public enum ArticleSignalStructure
        {
            CaseQuality,
            BinaryTruth,
            UniformQuality,
        }

        public sealed record InformationLevel(
            string Label,
            double CaseQualityPartySigma,
            double BinaryTruthPartySigma,
            double BinaryTruthCourtSigma);

        public sealed record ProductionMatrixAudit(
            int OptionSetCount,
            int CoreCombinationCount,
            int PairedComparisonCount,
            IReadOnlyDictionary<string, int> CountsByInformationAndRisk)
        {
            public int FeeRegimeComparisonCount { get; init; }
        }

        public sealed record FocusedSpecificationDefinition(
            FocusedSpecification Specification,
            string Label);

        public const string CaseQualityLabel = "Case quality";
        public const string BinaryTruthLabel = "Binary truth";
        public const string UniformQualityLabel = "Uniform quality";
        public const string ContinuousMeritsLabel = "Continuous merits";
        public const string BaselineInformationLevelLabel = "1x";
        public const string FocusedBaselineLabel = "Baseline";
        public const int ProductionOptionSetCount = 200;
        public const int ProductionCoreCombinationCount = 50;
        public const int ProductionPairedComparisonCount = 100;
        public const int SupplementalOptionSetCount = 25;
        public const int SupplementalCoreCombinationCount = 25;
        public const int SupplementalComparisonGroupCount = 25;
        public const int UnifiedOptionSetCount = 300;
        public const int UnifiedCoreCombinationCount = 75;
        public const int UnifiedComparisonGroupCount = 100;
        public const int FocusedOptionSetCount = 134;
        public const int FocusedCoreCombinationCount = 10;
        public const int FocusedSpecificationComparisonCount = 122;
        public const int FocusedFeeRegimeComparisonCount = 67;
        public const int MultipleEquilibriaOptionSetCount = 2;
        public const int MultipleEquilibriaInitializationCount = 50;
        public const int IncreasedOfferGridOptionSetCount = 4;
        public const int IncreasedOfferGridOfferCount = 15;
        private const double PublishedTotalPerPartyLitigationCosts = 0.30;
        private const double PublishedProportionOfCostsAtBeginning = 0.5;

        public static readonly IReadOnlyList<FocusedSpecificationDefinition> FocusedSpecifications =
            new[]
            {
                new FocusedSpecificationDefinition(FocusedSpecification.Baseline, FocusedBaselineLabel),
                new FocusedSpecificationDefinition(FocusedSpecification.LowNoise, "Low noise"),
                new FocusedSpecificationDefinition(FocusedSpecification.HighNoise, "High noise"),
                new FocusedSpecificationDefinition(FocusedSpecification.ModerateRiskAversion, "Moderate symmetric risk aversion"),
                new FocusedSpecificationDefinition(FocusedSpecification.LowNoiseModerateRiskAversion, "Low noise plus moderate risk aversion"),
                new FocusedSpecificationDefinition(FocusedSpecification.DirectBinaryStateSignals, "Direct binary-state signals"),
                new FocusedSpecificationDefinition(FocusedSpecification.TruthConditionedLatentMerits, "Truth-conditioned latent merits"),
                new FocusedSpecificationDefinition(FocusedSpecification.CenterWeightedContinuousMerits, "Center-weighted continuous merits"),
                new FocusedSpecificationDefinition(FocusedSpecification.PolarizedContinuousMerits, "Polarized continuous merits"),
                new FocusedSpecificationDefinition(FocusedSpecification.AllCostsAvoidable, "All litigation costs avoidable at bargaining"),
                new FocusedSpecificationDefinition(FocusedSpecification.AllCostsSunk, "All litigation costs sunk before bargaining"),
                new FocusedSpecificationDefinition(FocusedSpecification.MandatoryFilingAndAnswering, "Mandatory filing and answering"),
                new FocusedSpecificationDefinition(FocusedSpecification.MandatoryFilingAnsweringNoExit, "Mandatory filing and answering; no later exit"),
            };

        public static readonly IReadOnlyList<FocusedSpecification> IncreasedOfferGridSpecifications =
            new[]
            {
                FocusedSpecification.Baseline,
                FocusedSpecification.ModerateRiskAversion,
            };

        public static readonly IReadOnlyList<InformationLevel> ProductionInformationLevels =
            new[]
            {
                new InformationLevel("0.5x", 0.1000000000, 0.2964025888, 0.1947624474),
                new InformationLevel("1x",   0.2000000000, 0.3498283040, 0.3060453855),
                new InformationLevel("2x",   0.4000000000, 0.5507929452, 0.5210455266),
            };

        public ProductionRunPlan RunPlan { get; }

        public override string MasterReportNameForDistributedProcessing => RunPlan switch
        {
            ProductionRunPlan.LegacyTwoStructure => "CS001",
            ProductionRunPlan.UniformBaselineSupplement => "CS002U",
            ProductionRunPlan.UnifiedThreeStructure => "CS002",
            ProductionRunPlan.FocusedContinuousMerits => "CS003",
            ProductionRunPlan.MultipleEquilibriaRobustness => "CS004ME",
            ProductionRunPlan.IncreasedOfferGridRobustness => "CS005O15",
            _ => throw new NotSupportedException(),
        };

        public LitigGameCorrelatedSignalsArticleLauncher()
            : this(ProductionRunPlan.LegacyTwoStructure)
        {
        }

        public LitigGameCorrelatedSignalsArticleLauncher(ProductionRunPlan runPlan)
        {
            RunPlan = runPlan;
            UseDistributedProcessingForMultipleOptionsSets = true;
            SeparateScenariosWhenUsingDistributedProcessing = false;
            CombineResultsOfAllOptionSetsAfterExecution = false;
        }

        public static ProductionRunPlan ParseProductionRunPlan(string value) =>
            (value ?? "focused").Trim().ToLowerInvariant() switch
            {
                "legacy" or "cs001" => ProductionRunPlan.LegacyTwoStructure,
                "supplemental" or "supplement" or "uniform" or "cs002u" => ProductionRunPlan.UniformBaselineSupplement,
                "unified" or "all" or "cs002" => ProductionRunPlan.UnifiedThreeStructure,
                "focused" or "continuous" or "cs003" => ProductionRunPlan.FocusedContinuousMerits,
                "multiple-equilibria" or "multiple" or "equilibria" or "cs004me" => ProductionRunPlan.MultipleEquilibriaRobustness,
                "offers-15" or "offers15" or "increased-offers" or "cs005o15" => ProductionRunPlan.IncreasedOfferGridRobustness,
                _ => throw new ArgumentException(
                    $"Unknown correlated-signals plan '{value}'. Expected legacy, supplemental, unified, focused, " +
                    "multiple-equilibria, or offers-15."),
            };

        public IReadOnlyList<ArticleSignalStructure> IncludedSignalStructures => RunPlan switch
        {
            ProductionRunPlan.LegacyTwoStructure =>
                new[] { ArticleSignalStructure.CaseQuality, ArticleSignalStructure.BinaryTruth },
            ProductionRunPlan.UniformBaselineSupplement =>
                new[] { ArticleSignalStructure.UniformQuality },
            ProductionRunPlan.UnifiedThreeStructure =>
                new[]
                {
                    ArticleSignalStructure.CaseQuality,
                    ArticleSignalStructure.BinaryTruth,
                    ArticleSignalStructure.UniformQuality,
                },
            ProductionRunPlan.FocusedContinuousMerits =>
                new[] { ArticleSignalStructure.UniformQuality },
            ProductionRunPlan.MultipleEquilibriaRobustness =>
                new[] { ArticleSignalStructure.UniformQuality },
            ProductionRunPlan.IncreasedOfferGridRobustness =>
                new[] { ArticleSignalStructure.UniformQuality },
            _ => throw new NotSupportedException(),
        };

        private int ExpectedOptionSetCount => RunPlan switch
        {
            ProductionRunPlan.LegacyTwoStructure => ProductionOptionSetCount,
            ProductionRunPlan.UniformBaselineSupplement => SupplementalOptionSetCount,
            ProductionRunPlan.UnifiedThreeStructure => UnifiedOptionSetCount,
            ProductionRunPlan.FocusedContinuousMerits => FocusedOptionSetCount,
            ProductionRunPlan.MultipleEquilibriaRobustness => MultipleEquilibriaOptionSetCount,
            ProductionRunPlan.IncreasedOfferGridRobustness => IncreasedOfferGridOptionSetCount,
            _ => throw new NotSupportedException(),
        };

        private int ExpectedCoreCombinationCount => RunPlan switch
        {
            ProductionRunPlan.LegacyTwoStructure => ProductionCoreCombinationCount,
            ProductionRunPlan.UniformBaselineSupplement => SupplementalCoreCombinationCount,
            ProductionRunPlan.UnifiedThreeStructure => UnifiedCoreCombinationCount,
            ProductionRunPlan.FocusedContinuousMerits => FocusedCoreCombinationCount,
            ProductionRunPlan.MultipleEquilibriaRobustness => 1,
            ProductionRunPlan.IncreasedOfferGridRobustness => IncreasedOfferGridSpecifications.Count,
            _ => throw new NotSupportedException(),
        };

        private int ExpectedComparisonGroupCount => RunPlan switch
        {
            ProductionRunPlan.LegacyTwoStructure => ProductionPairedComparisonCount,
            ProductionRunPlan.UniformBaselineSupplement => SupplementalComparisonGroupCount,
            ProductionRunPlan.UnifiedThreeStructure => UnifiedComparisonGroupCount,
            ProductionRunPlan.FocusedContinuousMerits => FocusedSpecificationComparisonCount,
            ProductionRunPlan.MultipleEquilibriaRobustness => 1,
            ProductionRunPlan.IncreasedOfferGridRobustness => IncreasedOfferGridSpecifications.Count,
            _ => throw new NotSupportedException(),
        };

        public override double[] AdditionalCostsMultipliers => Array.Empty<double>();
        public override double[] AdditionalFeeShiftingMultipliers => Array.Empty<double>();
        public override double[] CriticalFeeShiftingMultipliers =>
            IsFocusedFamilyRun
                ? new[] { 0.0, 1.0 }
                : base.CriticalFeeShiftingMultipliers;

        private bool IsFocusedFamilyRun => RunPlan is
            ProductionRunPlan.FocusedContinuousMerits or
            ProductionRunPlan.MultipleEquilibriaRobustness or
            ProductionRunPlan.IncreasedOfferGridRobustness;

        public override List<(string, string)> DefaultVariableValues
        {
            get
            {
                if (IsFocusedFamilyRun)
                    return FocusedDefaultVariableValuesForRun();

                var values = new List<(string, string)>
                {
                    ("Signal Structure", CaseQualityLabel),
                    ("Information Level", BaselineInformationLevelLabel),
                    ("Party Signal Sigma", FormatSigma(GetInformationLevel(BaselineInformationLevelLabel).CaseQualityPartySigma)),
                    ("Court Signal Sigma", FormatSigma(GetInformationLevel(BaselineInformationLevelLabel).CaseQualityPartySigma)),
                    ("Costs Multiplier", "1"),
                    ("Fee Shifting Multiplier", "0"),
                    ("Risk Aversion", "Risk Neutral"),
                    ("Fee Shifting Rule", "English"),
                    ("Relative Costs", "1"),
                    ("Allow Abandon and Defaults", "true"),
                    ("Probability Truly Liable", "0.5"),
                    ("Noise to Produce Case Strength", "0.35"),
                    ("Issue", "Liability"),
                    ("Proportion of Costs at Beginning", "0.5"),
                    ("Liability Signal Shaping", "Identity"),
                    ("Damages Signal Shaping", "Identity"),
                    ("Number of Signals", "10"),
                    ("Number of Court Signals", "2"),
                    ("Number of Offers", "10"),
                };
                if (RunPlan != ProductionRunPlan.LegacyTwoStructure)
                {
                    values.AddRange(new[]
                    {
                        ("Quality Distribution", "Truth-conditioned 10-point quality"),
                        ("Quality-Truth Link", "T -> Q"),
                        ("Integration Method", "Finite sum"),
                        ("Quadrature Order", "N/A"),
                    });
                }
                return values;
            }
        }

        private static List<(string, string)> FocusedDefaultVariableValues() =>
            new()
            {
                ("Specification", FocusedBaselineLabel),
                ("Signal Structure", ContinuousMeritsLabel),
                ("Information Level", BaselineInformationLevelLabel),
                ("Party Signal Sigma", "0.2000000000"),
                ("Court Signal Sigma", "0.2000000000"),
                ("Costs Multiplier", "1"),
                ("Fee Shifting Multiplier", "0"),
                ("Fee Regime", "American"),
                ("Risk Aversion", "Risk Neutral"),
                ("CARA Alpha", "0"),
                ("Fee Shifting Rule", "English"),
                ("Relative Costs", "1"),
                ("Filing and Answering", "Endogenous"),
                ("Allow Abandon and Defaults", "true"),
                ("Probability Truly Liable", "0.5"),
                ("Noise to Produce Case Strength", "N/A"),
                ("Issue", "Liability"),
                ("Proportion of Costs at Beginning", "0.5"),
                ("Liability Signal Shaping", "Identity"),
                ("Damages Signal Shaping", "Identity"),
                ("Number of Signals", "10"),
                ("Number of Court Signals", "2"),
                ("Number of Offers", "10"),
                ("Quality Distribution", "Uniform [0..1] continuous quality"),
                ("Quality-Truth Link", "T | Q ~ Bernoulli(Q)"),
                ("Integration Method", "Gauss-Legendre"),
                ("Quadrature Order", LitigGameUniformQualityDisputeGenerator.DefaultQuadratureOrder.ToString(CultureInfo.InvariantCulture)),
                ("Detailed Signal Reporting", "true"),
            };

        private List<(string, string)> FocusedDefaultVariableValuesForRun()
        {
            List<(string, string)> values = FocusedDefaultVariableValues();
            if (RunPlan == ProductionRunPlan.IncreasedOfferGridRobustness)
                values = values.WithReplacement(
                    "Number of Offers",
                    IncreasedOfferGridOfferCount.ToString(CultureInfo.InvariantCulture));
            if (RunPlan == ProductionRunPlan.MultipleEquilibriaRobustness)
            {
                values.Add((
                    "Initialization Starts",
                    MultipleEquilibriaInitializationCount.ToString(CultureInfo.InvariantCulture)));
                values.Add(("Additional-Prior Arithmetic", "Inexact with exact fallback"));
            }
            return values;
        }

        public override List<(string criticalValueName, string[] criticalValueValues)> CriticalVariableValues =>
            IsFocusedFamilyRun
                ? new()
                {
                    ("Specification", RobustnessSpecifications().Select(GetFocusedSpecificationDefinition).Select(x => x.Label).ToArray()),
                    ("Costs Multiplier", RunPlan == ProductionRunPlan.FocusedContinuousMerits
                        ? CriticalCostsMultipliers.Select(FormatNumber).ToArray()
                        : new[] { "1" }),
                    ("Fee Regime", new[] { "American", "British" }),
                }
                : new()
                {
                    ("Signal Structure", IncludedSignalStructures.Select(GetSignalStructureLabel).ToArray()),
                    ("Costs Multiplier", CriticalCostsMultipliers.Select(FormatNumber).ToArray()),
                    ("Fee Shifting Multiplier", CriticalFeeShiftingMultipliers.Select(FormatNumber).ToArray()),
                };

        public override GameDefinition GetGameDefinition() => new LitigGameDefinition();

        public override GameOptions GetDefaultSingleGameOptions()
        {
            LitigGameOptions options = LitigGameOptionsGenerator.CorrelatedSignalsBase(smallerTree: false);
            SetPublishedCostStructure(options);
            options.NumOffers = 10;
            options.NumLiabilitySignals = 10;
            options.LiabilitySignalShapeParameters = IdentitySignalShapeParameters();
            options.DamagesSignalShapeParameters = IdentitySignalShapeParameters();
            if (IsFocusedFamilyRun)
            {
                foreach ((string key, string value) in FocusedDefaultVariableValuesForRun())
                    options.VariableSettings[key] = value;
                ConfigureFocusedSpecification(options, FocusedSpecification.Baseline);
                ApplyRunSpecificRobustnessSettings(options);
            }
            else
                ConfigureSignalStructureAndInformation(
                    options,
                    IncludedSignalStructures.First(),
                    GetInformationLevel(BaselineInformationLevelLabel));
            return options;
        }

        public override List<GameOptions> GetOptionsSets()
        {
            if (RunPlan is ProductionRunPlan.MultipleEquilibriaRobustness or
                ProductionRunPlan.IncreasedOfferGridRobustness)
                return GetRobustnessOptionSets();

            var optionSets = new List<GameOptions>();
            AddToOptionsSets(optionSets);
            if (RunPlan == ProductionRunPlan.FocusedContinuousMerits)
                optionSets.AddRange(CreateIntegratedFinerOfferOptionSets());

            foreach (LitigGameOptions options in optionSets.Cast<LitigGameOptions>())
                options.Name = CreateStableOptionSetIdentifier(options);

            optionSets = optionSets.OrderBy(x => x.Name, StringComparer.Ordinal).ToList();
            ValidateProductionMatrix(optionSets);
            return optionSets;
        }

        private IEnumerable<GameOptions> CreateIntegratedFinerOfferOptionSets()
        {
            foreach (FocusedSpecification specification in IncreasedOfferGridSpecifications)
            foreach (double feeMultiplier in new[] { 0.0, 1.0 })
            {
                LitigGameOptions options = LitigGameOptionsGenerator.CorrelatedSignalsBase(smallerTree: false);
                foreach ((string key, string value) in FocusedDefaultVariableValues())
                    options.VariableSettings[key] = value;
                SetPublishedCostStructure(options);
                ConfigureFocusedSpecification(options, specification);
                options.CostsMultiplier = 1.0;
                options.VariableSettings["Costs Multiplier"] = "1";
                options.LoserPays = true;
                options.LoserPaysMultiple = feeMultiplier;
                options.VariableSettings["Fee Shifting Multiplier"] = FormatNumber(feeMultiplier);
                options.VariableSettings["Fee Regime"] = feeMultiplier == 0.0 ? "American" : "British";
                options.NumOffers = IncreasedOfferGridOfferCount;
                options.VariableSettings["Number of Offers"] =
                    IncreasedOfferGridOfferCount.ToString(CultureInfo.InvariantCulture);
                ApplyRunSpecificRobustnessSettings(options);
                yield return options;
            }
        }

        private List<GameOptions> GetRobustnessOptionSets()
        {
            var optionSets = new List<GameOptions>();
            foreach (FocusedSpecification specification in RobustnessSpecifications())
            foreach (double feeMultiplier in new[] { 0.0, 1.0 })
            {
                LitigGameOptions options = LitigGameOptionsGenerator.CorrelatedSignalsBase(smallerTree: false);
                foreach ((string key, string value) in FocusedDefaultVariableValuesForRun())
                    options.VariableSettings[key] = value;
                SetPublishedCostStructure(options);
                ConfigureFocusedSpecification(options, specification);
                options.CostsMultiplier = 1.0;
                options.VariableSettings["Costs Multiplier"] = "1";
                options.LoserPays = true;
                options.LoserPaysMultiple = feeMultiplier;
                options.VariableSettings["Fee Shifting Multiplier"] = FormatNumber(feeMultiplier);
                options.VariableSettings["Fee Regime"] = feeMultiplier == 0.0 ? "American" : "British";
                ApplyRunSpecificRobustnessSettings(options);
                options.Name = CreateStableOptionSetIdentifier(options);
                optionSets.Add(options);
            }

            optionSets = optionSets.OrderBy(option => option.Name, StringComparer.Ordinal).ToList();
            ValidateProductionMatrix(optionSets);
            return optionSets;
        }

        private IReadOnlyList<FocusedSpecification> RobustnessSpecifications() => RunPlan switch
        {
            ProductionRunPlan.FocusedContinuousMerits =>
                FocusedSpecifications.Select(definition => definition.Specification).ToArray(),
            ProductionRunPlan.MultipleEquilibriaRobustness =>
                new[] { FocusedSpecification.Baseline },
            ProductionRunPlan.IncreasedOfferGridRobustness =>
                IncreasedOfferGridSpecifications,
            _ => Array.Empty<FocusedSpecification>(),
        };

        private void ApplyRunSpecificRobustnessSettings(LitigGameOptions options)
        {
            options.VariableSettings["Number of Signals"] =
                options.NumLiabilitySignals.ToString(CultureInfo.InvariantCulture);
            options.VariableSettings["Number of Court Signals"] =
                options.NumCourtLiabilitySignals.ToString(CultureInfo.InvariantCulture);

            if (RunPlan == ProductionRunPlan.IncreasedOfferGridRobustness)
            {
                options.NumOffers = IncreasedOfferGridOfferCount;
                options.VariableSettings["Number of Offers"] =
                    IncreasedOfferGridOfferCount.ToString(CultureInfo.InvariantCulture);
            }
            else if (RunPlan == ProductionRunPlan.MultipleEquilibriaRobustness)
            {
                options.VariableSettings["Initialization Starts"] =
                    MultipleEquilibriaInitializationCount.ToString(CultureInfo.InvariantCulture);
                options.VariableSettings["Additional-Prior Arithmetic"] = "Inexact with exact fallback";
            }

            Action<EvolutionSettings> existingModifier = options.ModifyEvolutionSettings;
            options.ModifyEvolutionSettings = settings =>
            {
                existingModifier?.Invoke(settings);
                settings.GenerateInformationSetActionReport = true;
                if (RunPlan == ProductionRunPlan.MultipleEquilibriaRobustness)
                {
                    settings.SequenceFormNumPriorsToUseToGenerateEquilibria =
                        MultipleEquilibriaInitializationCount;
                    settings.TryInexactArithmeticForAdditionalEquilibria = true;
                    settings.ThrowIfNotPerfectEquilibrium = false;
                }
            };
        }

        public override List<VariableCombinationGenerator.Dimension<LitigGameOptions>> GetVariationSetsInfo()
        {
            if (RunPlan == ProductionRunPlan.FocusedContinuousMerits)
            {
                return new()
                {
                    new(
                        "Specification",
                        FocusedSpecificationTransformations(),
                        null,
                        IsGlobal: true),
                    new(
                        "CostsMultiplier",
                        CriticalCostsMultiplierTransformations(),
                        null,
                        IsGlobal: true),
                    new(
                        "FeeRegime",
                        FocusedFeeRegimeTransformations(),
                        null,
                        IsGlobal: true),
                };
            }

            return new()
            {
                new(
                    "SignalStructure",
                    SignalStructureTransformations(),
                    null,
                    IsGlobal: true),

                new(
                    "CostsMultiplier",
                    CriticalCostsMultiplierTransformations(),
                    null,
                    IsGlobal: true),

                new(
                    "FeeShiftingMultiplier",
                    CriticalFeeShiftingMultiplierTransformations(),
                    null,
                    IsGlobal: true),

                new(
                    "InformationLevel",
                    null,
                    InformationLevelTransformations()),

                new(
                    "RiskAversion",
                    null,
                    RiskAversionTransformations()),
            };
        }

        private List<Func<LitigGameOptions, LitigGameOptions>> FocusedSpecificationTransformations() =>
            FocusedSpecifications
                .Select(definition => (Func<LitigGameOptions, LitigGameOptions>)(options =>
                    GetAndTransform(options, " Specification " + definition.Specification, transformed =>
                        ConfigureFocusedSpecification(transformed, definition.Specification))))
                .ToList();

        private List<Func<LitigGameOptions, LitigGameOptions>> FocusedFeeRegimeTransformations() =>
            CriticalFeeShiftingMultipliers
                .Select(multiplier => (Func<LitigGameOptions, LitigGameOptions>)(options =>
                    GetAndTransform(options, " FeeRegime " + (multiplier == 0.0 ? "American" : "British"), transformed =>
                    {
                        transformed.LoserPays = true;
                        transformed.LoserPaysMultiple = multiplier;
                        transformed.VariableSettings["Fee Shifting Multiplier"] = FormatNumber(multiplier);
                        transformed.VariableSettings["Fee Regime"] = multiplier == 0.0 ? "American" : "British";
                    })))
                .ToList();

        private List<Func<LitigGameOptions, LitigGameOptions>> RiskAversionTransformations() =>
            RunPlan == ProductionRunPlan.UniformBaselineSupplement
                ? new List<Func<LitigGameOptions, LitigGameOptions>> { GetAndTransform_RiskNeutral }
                : new List<Func<LitigGameOptions, LitigGameOptions>>
                {
                    GetAndTransform_RiskNeutral,
                    GetAndTransform_ModeratelyRiskAverse,
                };

        private void ConfigureFocusedSpecification(
            LitigGameOptions options,
            FocusedSpecification specification)
        {
            InformationLevel baselineInformation = GetInformationLevel(BaselineInformationLevelLabel);
            options.NumOffers = 10;
            options.NumLiabilitySignals = 10;
            options.NumLiabilityStrengthPoints = 2;
            options.PLiabilityNoiseStdev = baselineInformation.CaseQualityPartySigma;
            options.DLiabilityNoiseStdev = baselineInformation.CaseQualityPartySigma;
            options.CourtLiabilityNoiseStdev = baselineInformation.CaseQualityPartySigma;
            options.LiabilitySignalShapeParameters = IdentitySignalShapeParameters();
            options.DamagesSignalShapeParameters = IdentitySignalShapeParameters();
            options.PUtilityCalculator = new RiskNeutralUtilityCalculator { InitialWealth = options.PInitialWealth };
            options.DUtilityCalculator = new RiskNeutralUtilityCalculator { InitialWealth = options.DInitialWealth };
            SetPublishedCostStructure(options);
            options.SkipFileAndAnswerDecisions = false;
            options.AllowAbandonAndDefaults = true;
            options.PredeterminedAbandonAndDefaults = true;
            options.LitigGameDisputeGenerator = new LitigGameUniformQualityDisputeGenerator
            {
                QuadratureOrder = LitigGameUniformQualityDisputeGenerator.DefaultQuadratureOrder,
                QualityDistribution = ContinuousQualityDistribution.Uniform,
            };

            string informationLevel = BaselineInformationLevelLabel;
            string signalStructure = ContinuousMeritsLabel;
            string riskAversion = "Risk Neutral";
            string caraAlpha = "0";
            string filingAndAnswering = "Endogenous";
            string qualityDistribution = "Uniform [0..1] continuous quality";
            string qualityTruthLink = "T | Q ~ Bernoulli(Q)";
            string integrationMethod = "Gauss-Legendre";
            string quadratureOrder = LitigGameUniformQualityDisputeGenerator.DefaultQuadratureOrder
                .ToString(CultureInfo.InvariantCulture);
            string noiseToProduceCaseStrength = "N/A";
            double proportionOfCostsAtBeginning = 0.5;

            switch (specification)
            {
                case FocusedSpecification.Baseline:
                    break;

                case FocusedSpecification.LowNoise:
                    informationLevel = "0.5x";
                    SetFocusedSignalNoise(options, 0.10, 0.10);
                    break;

                case FocusedSpecification.HighNoise:
                    informationLevel = "2x";
                    SetFocusedSignalNoise(options, 0.40, 0.40);
                    break;

                case FocusedSpecification.ModerateRiskAversion:
                    riskAversion = "Moderately Risk Averse";
                    caraAlpha = "2";
                    SetModerateRiskAversion(options);
                    break;

                case FocusedSpecification.LowNoiseModerateRiskAversion:
                    informationLevel = "0.5x";
                    riskAversion = "Moderately Risk Averse";
                    caraAlpha = "2";
                    SetFocusedSignalNoise(options, 0.10, 0.10);
                    SetModerateRiskAversion(options);
                    break;

                case FocusedSpecification.DirectBinaryStateSignals:
                    signalStructure = BinaryTruthLabel;
                    SetFocusedSignalNoise(
                        options,
                        baselineInformation.BinaryTruthPartySigma,
                        baselineInformation.BinaryTruthCourtSigma);
                    options.LitigGameDisputeGenerator = new LitigGameExogenousDirectSignalDisputeGenerator
                    {
                        ExogenousProbabilityTrulyLiable = 0.5,
                    };
                    qualityDistribution = "Binary truth";
                    qualityTruthLink = "Signals generated directly by T";
                    integrationMethod = "Finite sum";
                    quadratureOrder = "N/A";
                    break;

                case FocusedSpecification.TruthConditionedLatentMerits:
                    signalStructure = CaseQualityLabel;
                    options.NumLiabilityStrengthPoints = 10;
                    options.LitigGameDisputeGenerator = new LitigGameExogenousDisputeGenerator
                    {
                        ExogenousProbabilityTrulyLiable = 0.5,
                        StdevNoiseToProduceLiabilityStrength = 0.35,
                    };
                    qualityDistribution = "Truth-conditioned 10-point latent merits";
                    qualityTruthLink = "T generates Q; Q generates signals";
                    integrationMethod = "Finite sum";
                    quadratureOrder = "N/A";
                    noiseToProduceCaseStrength = "0.35";
                    break;

                case FocusedSpecification.CenterWeightedContinuousMerits:
                    ((LitigGameUniformQualityDisputeGenerator)options.LitigGameDisputeGenerator)
                        .QualityDistribution = ContinuousQualityDistribution.BetaTwoTwo;
                    qualityDistribution = "Beta(2,2) continuous quality";
                    break;

                case FocusedSpecification.PolarizedContinuousMerits:
                    ((LitigGameUniformQualityDisputeGenerator)options.LitigGameDisputeGenerator)
                        .QualityDistribution = ContinuousQualityDistribution.BetaHalfHalf;
                    qualityDistribution = "Beta(0.5,0.5) continuous quality";
                    integrationMethod = "Gauss-Legendre after arcsine transform";
                    break;

                case FocusedSpecification.AllCostsAvoidable:
                    proportionOfCostsAtBeginning = 0.0;
                    SetFocusedCostTiming(options, proportionOfCostsAtBeginning);
                    break;

                case FocusedSpecification.AllCostsSunk:
                    proportionOfCostsAtBeginning = 1.0;
                    SetFocusedCostTiming(options, proportionOfCostsAtBeginning);
                    break;

                case FocusedSpecification.MandatoryFilingAndAnswering:
                    options.SkipFileAndAnswerDecisions = true;
                    filingAndAnswering = "Mandatory";
                    break;

                case FocusedSpecification.MandatoryFilingAnsweringNoExit:
                    options.SkipFileAndAnswerDecisions = true;
                    options.AllowAbandonAndDefaults = false;
                    filingAndAnswering = "Mandatory";
                    break;

                default:
                    throw new NotSupportedException();
            }

            options.VariableSettings["Specification"] = GetFocusedSpecificationDefinition(specification).Label;
            options.VariableSettings["Signal Structure"] = signalStructure;
            options.VariableSettings["Information Level"] = informationLevel;
            options.VariableSettings["Party Signal Sigma"] = FormatSigma(options.PLiabilityNoiseStdev);
            options.VariableSettings["Court Signal Sigma"] = FormatSigma(options.CourtLiabilityNoiseStdev);
            options.VariableSettings["Risk Aversion"] = riskAversion;
            options.VariableSettings["CARA Alpha"] = caraAlpha;
            options.VariableSettings["Filing and Answering"] = filingAndAnswering;
            options.VariableSettings["Allow Abandon and Defaults"] = options.AllowAbandonAndDefaults ? "true" : "false";
            options.VariableSettings["Probability Truly Liable"] = "0.5";
            options.VariableSettings["Noise to Produce Case Strength"] = noiseToProduceCaseStrength;
            options.VariableSettings["Issue"] = "Liability";
            options.VariableSettings["Proportion of Costs at Beginning"] = FormatNumber(proportionOfCostsAtBeginning);
            options.VariableSettings["Liability Signal Shaping"] = "Identity";
            options.VariableSettings["Damages Signal Shaping"] = "Identity";
            options.VariableSettings["Number of Signals"] =
                options.NumLiabilitySignals.ToString(CultureInfo.InvariantCulture);
            options.VariableSettings["Number of Court Signals"] =
                options.NumCourtLiabilitySignals.ToString(CultureInfo.InvariantCulture);
            options.VariableSettings["Number of Offers"] =
                options.NumOffers.ToString(CultureInfo.InvariantCulture);
            options.VariableSettings["Quality Distribution"] = qualityDistribution;
            options.VariableSettings["Quality-Truth Link"] = qualityTruthLink;
            options.VariableSettings["Integration Method"] = integrationMethod;
            options.VariableSettings["Quadrature Order"] = quadratureOrder;
            options.VariableSettings["Detailed Signal Reporting"] = "true";
        }

        private static void SetFocusedSignalNoise(
            LitigGameOptions options,
            double partySigma,
            double courtSigma)
        {
            options.PLiabilityNoiseStdev = partySigma;
            options.DLiabilityNoiseStdev = partySigma;
            options.CourtLiabilityNoiseStdev = courtSigma;
        }

        private static void SetModerateRiskAversion(LitigGameOptions options)
        {
            options.PUtilityCalculator = new CARARiskAverseUtilityCalculator
            {
                InitialWealth = options.PInitialWealth,
                Alpha = 2,
                LinearTransformation = true,
            };
            options.DUtilityCalculator = new CARARiskAverseUtilityCalculator
            {
                InitialWealth = options.DInitialWealth,
                Alpha = 2,
                LinearTransformation = true,
            };
        }

        private static void SetFocusedCostTiming(
            LitigGameOptions options,
            double proportionAtBeginning)
        {
            options.PFilingCost = options.DAnswerCost =
                proportionAtBeginning * PublishedTotalPerPartyLitigationCosts;
            options.PTrialCosts = options.DTrialCosts =
                (1.0 - proportionAtBeginning) * PublishedTotalPerPartyLitigationCosts;
        }

        private static void SetPublishedCostStructure(LitigGameOptions options)
        {
            SetFocusedCostTiming(options, PublishedProportionOfCostsAtBeginning);
            options.PerPartyCostsLeadingUpToBargainingRound = 0;
            options.RoundSpecificBargainingCosts = null;
        }

        public static FocusedSpecificationDefinition GetFocusedSpecificationDefinition(
            FocusedSpecification specification) =>
            FocusedSpecifications.Single(x => x.Specification == specification);

        public ProductionMatrixAudit ValidateProductionMatrix(IReadOnlyList<GameOptions> optionSets = null)
        {
            optionSets ??= GetOptionsSets();
            var litigOptions = optionSets.Cast<LitigGameOptions>().ToList();
            var errors = new List<string>();

            if (AlwaysDoTaskID != null)
                errors.Add($"{nameof(AlwaysDoTaskID)} must be null for production.");
            if (LimitToTaskIDs != null)
                errors.Add($"{nameof(LimitToTaskIDs)} must be null for production.");
            if (OnlyRunCoreSimulations)
                errors.Add($"{nameof(OnlyRunCoreSimulations)} must be false for production.");
            if (SeparateScenariosWhenUsingDistributedProcessing)
                errors.Add($"{nameof(SeparateScenariosWhenUsingDistributedProcessing)} must be false for production.");
            if (CombineResultsOfAllOptionSetsAfterExecution)
                errors.Add($"{nameof(CombineResultsOfAllOptionSetsAfterExecution)} must be false; aggregation is a separate validated step.");
            if (litigOptions.Count != ExpectedOptionSetCount)
                errors.Add($"Expected {ExpectedOptionSetCount} option sets but found {litigOptions.Count}.");

            var duplicateNames = litigOptions
                .GroupBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() != 1)
                .Select(g => $"{g.Key} ({g.Count()})")
                .ToList();
            if (duplicateNames.Count > 0)
                errors.Add("Duplicate option-set identifiers: " + string.Join(", ", duplicateNames));

            if (RunPlan == ProductionRunPlan.FocusedContinuousMerits)
                return ValidateFocusedProductionMatrix(litigOptions, errors);
            if (RunPlan is ProductionRunPlan.MultipleEquilibriaRobustness or
                ProductionRunPlan.IncreasedOfferGridRobustness)
                return ValidateRobustnessProductionMatrix(litigOptions, errors);

            foreach (LitigGameOptions options in litigOptions)
                ValidateOptionSet(options, errors);

            var coreGroups = litigOptions.GroupBy(CoreCombinationKey).ToList();
            if (coreGroups.Count != ExpectedCoreCombinationCount)
                errors.Add($"Expected {ExpectedCoreCombinationCount} core combinations but found {coreGroups.Count}.");

            foreach (var coreGroup in coreGroups)
            {
                RequireExactlyOne(coreGroup, BaselineInformationLevelLabel, "Risk Neutral", errors);
                if (RunPlan != ProductionRunPlan.UniformBaselineSupplement)
                {
                    RequireExactlyOne(coreGroup, "0.5x", "Risk Neutral", errors);
                    RequireExactlyOne(coreGroup, "2x", "Risk Neutral", errors);
                    RequireExactlyOne(coreGroup, BaselineInformationLevelLabel, "Moderately Risk Averse", errors);
                }

                var unexpected = coreGroup.Where(x =>
                    GetSetting(x, "Risk Aversion") == "Moderately Risk Averse" &&
                    GetSetting(x, "Information Level") != BaselineInformationLevelLabel).ToList();
                if (unexpected.Count > 0)
                    errors.Add($"Core combination {coreGroup.Key} crosses risk aversion with non-baseline information.");

                int expectedRowsPerCoreGroup =
                    RunPlan == ProductionRunPlan.UniformBaselineSupplement ? 1 : 4;
                if (coreGroup.Count() != expectedRowsPerCoreGroup)
                    errors.Add(
                        $"Core combination {coreGroup.Key} contains {coreGroup.Count()} rows instead of {expectedRowsPerCoreGroup}.");
            }

            int pairedComparisonCount = litigOptions
                .GroupBy(PairedComparisonKey)
                .Count(g =>
                    g.Select(x => GetSetting(x, "Signal Structure")).Distinct().Count() == IncludedSignalStructures.Count &&
                    g.Count() == IncludedSignalStructures.Count);
            if (pairedComparisonCount != ExpectedComparisonGroupCount)
                errors.Add(
                    $"Expected {ExpectedComparisonGroupCount} complete structure comparison groups but found {pairedComparisonCount}.");

            if (errors.Count > 0)
                throw new InvalidOperationException(
                    "Correlated-signals production matrix validation failed:" + Environment.NewLine +
                    string.Join(Environment.NewLine, errors.Select(x => "- " + x)));

            var counts = litigOptions
                .GroupBy(x => $"{GetSetting(x, "Information Level")}|{GetSetting(x, "Risk Aversion")}")
                .OrderBy(g => g.Key, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);

            return new ProductionMatrixAudit(
                litigOptions.Count,
                coreGroups.Count,
                pairedComparisonCount,
                counts);
        }

        private ProductionMatrixAudit ValidateFocusedProductionMatrix(
            IReadOnlyList<LitigGameOptions> options,
            ICollection<string> errors)
        {
            foreach (LitigGameOptions option in options)
                ValidateFocusedOptionSet(option, errors, option.NumOffers);

            List<LitigGameOptions> coreOptions = options
                .Where(option => option.NumOffers == 10)
                .ToList();
            List<LitigGameOptions> finerOfferOptions = options
                .Where(option => option.NumOffers == IncreasedOfferGridOfferCount)
                .ToList();
            if (options.Count != coreOptions.Count + finerOfferOptions.Count)
                errors.Add("CS003 contains an offer count outside the planned 10- and 15-offer grids.");
            if (finerOfferOptions.Count != IncreasedOfferGridOptionSetCount)
                errors.Add(
                    $"Expected {IncreasedOfferGridOptionSetCount} integrated 15-offer rows " +
                    $"but found {finerOfferOptions.Count}.");
            if (finerOfferOptions.Any(option =>
                    !IncreasedOfferGridSpecifications.Contains(ParseFocusedSpecification(
                        GetSetting(option, "Specification"))) ||
                    Math.Abs(option.CostsMultiplier - 1.0) > 1E-12))
                errors.Add(
                    "Integrated 15-offer rows must be a planned finer-offer specification " +
                    "at cost multiplier 1.");
            var finerOfferSpecificationGroups = finerOfferOptions
                .GroupBy(option => GetSetting(option, "Specification"), StringComparer.Ordinal)
                .ToList();
            if (finerOfferSpecificationGroups.Count != IncreasedOfferGridSpecifications.Count ||
                finerOfferSpecificationGroups.Any(group =>
                    !group.Select(option => GetSetting(option, "Fee Regime"))
                        .OrderBy(value => value, StringComparer.Ordinal)
                        .SequenceEqual(new[] { "American", "British" })))
                errors.Add(
                    "Each integrated 15-offer specification must contain one American and one British row.");

            var coreGroups = coreOptions
                .GroupBy(option => string.Join("|", new[]
                {
                    GetSetting(option, "Costs Multiplier"),
                    GetSetting(option, "Fee Regime"),
                }), StringComparer.Ordinal)
                .ToList();
            if (coreGroups.Count != FocusedCoreCombinationCount)
                errors.Add(
                    $"Expected {FocusedCoreCombinationCount} cost/fee combinations but found {coreGroups.Count}.");

            foreach (var group in coreGroups)
            {
                foreach (FocusedSpecificationDefinition specification in FocusedSpecifications)
                {
                    int count = group.Count(option =>
                        GetSetting(option, "Specification") == specification.Label);
                    if (count != 1)
                        errors.Add(
                            $"Core combination {group.Key} has {count} rows for specification " +
                            $"'{specification.Label}'; expected exactly one.");
                }

                if (group.Count() != FocusedSpecifications.Count)
                    errors.Add(
                        $"Core combination {group.Key} contains {group.Count()} rows instead of " +
                        $"{FocusedSpecifications.Count}.");
            }

            var specificationGroups = options
                .GroupBy(option => string.Join("|", new[]
                {
                    GetSetting(option, "Costs Multiplier"),
                    GetSetting(option, "Fee Regime"),
                    GetSetting(option, "Number of Offers"),
                }), StringComparer.Ordinal)
                .ToList();
            int specificationComparisons = specificationGroups.Sum(group =>
                group.Any(option => GetSetting(option, "Specification") == FocusedBaselineLabel)
                    ? group.Count() - 1
                    : 0);
            var feeGroups = options
                .GroupBy(option => string.Join("|", new[]
                {
                    GetSetting(option, "Specification"),
                    GetSetting(option, "Costs Multiplier"),
                    GetSetting(option, "Number of Offers"),
                }), StringComparer.Ordinal)
                .ToList();
            int feeComparisons = feeGroups.Count(group =>
                group.Count() == 2 &&
                group.Select(option => GetSetting(option, "Fee Regime"))
                    .OrderBy(value => value, StringComparer.Ordinal)
                    .SequenceEqual(new[] { "American", "British" }));

            if (specificationComparisons != FocusedSpecificationComparisonCount)
                errors.Add(
                    $"Expected {FocusedSpecificationComparisonCount} baseline specification comparisons " +
                    $"but found {specificationComparisons}.");
            if (feeComparisons != FocusedFeeRegimeComparisonCount)
                errors.Add(
                    $"Expected {FocusedFeeRegimeComparisonCount} American/British comparisons " +
                    $"but found {feeComparisons}.");

            if (errors.Count > 0)
                throw new InvalidOperationException(
                    "CS003 production matrix validation failed:" + Environment.NewLine +
                    string.Join(Environment.NewLine, errors.Select(error => "- " + error)));

            var counts = options
                .GroupBy(option => GetSetting(option, "Specification"), StringComparer.Ordinal)
                .OrderBy(group => group.Key, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);

            return new ProductionMatrixAudit(
                options.Count,
                coreGroups.Count,
                specificationComparisons,
                counts)
            {
                FeeRegimeComparisonCount = feeComparisons,
            };
        }

        private ProductionMatrixAudit ValidateRobustnessProductionMatrix(
            IReadOnlyList<LitigGameOptions> options,
            ICollection<string> errors)
        {
            int expectedOffers = RunPlan == ProductionRunPlan.IncreasedOfferGridRobustness
                ? IncreasedOfferGridOfferCount
                : 10;
            HashSet<FocusedSpecification> expectedSpecifications =
                RobustnessSpecifications().ToHashSet();

            foreach (LitigGameOptions option in options)
            {
                ValidateFocusedOptionSet(option, errors, expectedOffers);
                FocusedSpecification specification = ParseFocusedSpecification(
                    GetSetting(option, "Specification"));
                if (!expectedSpecifications.Contains(specification))
                    errors.Add($"{option.Name}: specification is outside the robustness design.");
                if (Math.Abs(option.CostsMultiplier - 1.0) > 1E-12 ||
                    GetSetting(option, "Costs Multiplier") != "1")
                    errors.Add($"{option.Name}: robustness runs must use the principal cost multiplier 1.");
                if (GetSetting(option, "Number of Offers") !=
                    expectedOffers.ToString(CultureInfo.InvariantCulture))
                    errors.Add($"{option.Name}: offer-count metadata does not match the configured grid.");

                if (RunPlan == ProductionRunPlan.MultipleEquilibriaRobustness)
                {
                    var settings = new EvolutionSettings();
                    option.ModifyEvolutionSettings?.Invoke(settings);
                    if (settings.SequenceFormNumPriorsToUseToGenerateEquilibria !=
                            MultipleEquilibriaInitializationCount ||
                        !settings.TryInexactArithmeticForAdditionalEquilibria ||
                        settings.ThrowIfNotPerfectEquilibrium)
                        errors.Add($"{option.Name}: multiple-equilibria solver settings are incomplete.");
                }
            }

            var feeGroups = options
                .GroupBy(option => GetSetting(option, "Specification"), StringComparer.Ordinal)
                .ToList();
            foreach (IGrouping<string, LitigGameOptions> group in feeGroups)
            {
                string[] regimes = group.Select(option => GetSetting(option, "Fee Regime"))
                    .OrderBy(value => value, StringComparer.Ordinal)
                    .ToArray();
                if (!regimes.SequenceEqual(new[] { "American", "British" }))
                    errors.Add($"Specification '{group.Key}' does not contain exactly one run under each fee regime.");
            }

            if (feeGroups.Count != expectedSpecifications.Count)
                errors.Add($"Expected {expectedSpecifications.Count} specification groups but found {feeGroups.Count}.");
            if (errors.Count > 0)
                throw new InvalidOperationException(
                    $"{MasterReportNameForDistributedProcessing} robustness matrix validation failed:" +
                    Environment.NewLine +
                    string.Join(Environment.NewLine, errors.Select(error => "- " + error)));

            Dictionary<string, int> counts = options
                .GroupBy(option => GetSetting(option, "Specification"), StringComparer.Ordinal)
                .OrderBy(group => group.Key, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
            return new ProductionMatrixAudit(
                options.Count,
                feeGroups.Count,
                feeGroups.Count,
                counts)
            {
                FeeRegimeComparisonCount = feeGroups.Count,
            };
        }

        public override List<SimulationSetsIdentifier> GetSimulationSetsIdentifiers(SimulationSetsTransformer transformer = null)
        {
            if (RunPlan is ProductionRunPlan.MultipleEquilibriaRobustness or
                ProductionRunPlan.IncreasedOfferGridRobustness)
            {
                List<SimulationSetsIdentifier> robustnessResults = RobustnessSpecifications()
                    .Select(specification =>
                    {
                        string label = GetFocusedSpecificationDefinition(specification).Label;
                        return new SimulationSetsIdentifier(
                            label,
                            GetOptionsSets().Cast<LitigGameOptions>()
                                .Where(option => GetSetting(option, "Specification") == label)
                                .OrderBy(option => GetSetting(option, "Fee Regime"), StringComparer.Ordinal)
                                .Select(option => CreateExactSimulationIdentifier(
                                    GetSetting(option, "Fee Regime"),
                                    option))
                                .ToList());
                    })
                    .ToList();
                return PerformArticleVariationInfoSetsTransformation(transformer, robustnessResults);
            }

            if (RunPlan == ProductionRunPlan.FocusedContinuousMerits)
            {
                List<SimulationSetsIdentifier> focusedResults = FocusedSpecifications
                    .Where(definition => definition.Specification != FocusedSpecification.Baseline)
                    .Select(definition => new SimulationSetsIdentifier(
                        definition.Label,
                        new List<SimulationIdentifier>
                        {
                            CreateFocusedSimulationIdentifier(
                                FocusedBaselineLabel,
                                FocusedSpecification.Baseline),
                            CreateFocusedSimulationIdentifier(
                                definition.Label,
                                definition.Specification),
                        }))
                    .ToList();
                List<LitigGameOptions> focusedOptions = GetOptionsSets()
                    .Cast<LitigGameOptions>()
                    .ToList();
                foreach (FocusedSpecification specification in IncreasedOfferGridSpecifications)
                {
                    string specificationLabel =
                        GetFocusedSpecificationDefinition(specification).Label;
                    List<LitigGameOptions> finerOfferComparison = focusedOptions
                        .Where(option =>
                            GetSetting(option, "Specification") == specificationLabel &&
                            GetSetting(option, "Costs Multiplier") == "1" &&
                            (option.NumOffers == 10 ||
                                option.NumOffers == IncreasedOfferGridOfferCount))
                        .OrderBy(option => option.NumOffers)
                        .ThenBy(option => GetSetting(option, "Fee Regime"), StringComparer.Ordinal)
                        .ToList();
                    focusedResults.Add(new SimulationSetsIdentifier(
                        $"{specificationLabel} offer-grid sensitivity",
                        finerOfferComparison
                            .Select(option => CreateExactSimulationIdentifier(
                                $"{GetSetting(option, "Fee Regime")}, {option.NumOffers} offers",
                                option))
                            .ToList()));
                }
                return PerformArticleVariationInfoSetsTransformation(transformer, focusedResults);
            }

            if (RunPlan == ProductionRunPlan.UniformBaselineSupplement)
            {
                var supplementalResults = new List<SimulationSetsIdentifier>
                {
                    new(
                        "Uniform Quality Baseline",
                        new List<SimulationIdentifier>
                        {
                            CreateSimulationIdentifier(
                                UniformQualityLabel,
                                ArticleSignalStructure.UniformQuality,
                                BaselineInformationLevelLabel,
                                "Risk Neutral"),
                        }),
                };
                return PerformArticleVariationInfoSetsTransformation(transformer, supplementalResults);
            }

            var results = new List<SimulationSetsIdentifier>
            {
                StructureComparison("0.5x"),
                StructureComparison("1x"),
                StructureComparison("2x"),
                InformationComparison(ArticleSignalStructure.CaseQuality),
                InformationComparison(ArticleSignalStructure.BinaryTruth),
                RiskComparison(ArticleSignalStructure.CaseQuality),
                RiskComparison(ArticleSignalStructure.BinaryTruth),
            };

            if (RunPlan == ProductionRunPlan.UnifiedThreeStructure)
            {
                results.Add(InformationComparison(ArticleSignalStructure.UniformQuality));
                results.Add(RiskComparison(ArticleSignalStructure.UniformQuality));
            }

            return PerformArticleVariationInfoSetsTransformation(transformer, results);
        }

        private SimulationIdentifier CreateExactSimulationIdentifier(
            string name,
            LitigGameOptions options) =>
            new(
                name,
                DefaultVariableValues
                    .Where(setting => setting.Item1 != "Fee Regime")
                    .Select(setting =>
                    (
                        setting.Item1,
                        Convert.ToString(
                            options.VariableSettings[setting.Item1],
                            CultureInfo.InvariantCulture)
                    )).ToList());

        private SimulationIdentifier CreateFocusedSimulationIdentifier(
            string name,
            FocusedSpecification specification)
        {
            LitigGameOptions metadata = LitigGameOptionsGenerator.CorrelatedSignalsBase(smallerTree: false);
            foreach ((string key, string value) in FocusedDefaultVariableValues())
                metadata.VariableSettings[key] = value;
            ConfigureFocusedSpecification(metadata, specification);

            List<(string, string)> matches = DefaultVariableValues
                .Where(setting => setting.Item1 != "Fee Regime")
                .Select(setting =>
                    (
                        setting.Item1,
                        metadata.VariableSettings.TryGetValue(setting.Item1, out object actual)
                            ? Convert.ToString(actual, CultureInfo.InvariantCulture)
                            : setting.Item2
                    ))
                .ToList();
            return new SimulationIdentifier(name, matches);
        }

        public static InformationLevel GetInformationLevel(string label) =>
            ProductionInformationLevels.Single(x => x.Label == label);

        public static double GetPartySigma(ArticleSignalStructure structure, string informationLevelLabel)
        {
            InformationLevel level = GetInformationLevel(informationLevelLabel);
            return structure switch
            {
                ArticleSignalStructure.CaseQuality => level.CaseQualityPartySigma,
                ArticleSignalStructure.BinaryTruth => level.BinaryTruthPartySigma,
                ArticleSignalStructure.UniformQuality => level.CaseQualityPartySigma,
                _ => throw new NotSupportedException(),
            };
        }

        public static double GetCourtSigma(ArticleSignalStructure structure, string informationLevelLabel)
        {
            InformationLevel level = GetInformationLevel(informationLevelLabel);
            return structure switch
            {
                ArticleSignalStructure.CaseQuality => level.CaseQualityPartySigma,
                ArticleSignalStructure.BinaryTruth => level.BinaryTruthCourtSigma,
                ArticleSignalStructure.UniformQuality => level.CaseQualityPartySigma,
                _ => throw new NotSupportedException(),
            };
        }

        public static string GetSignalStructureLabel(ArticleSignalStructure structure) => structure switch
        {
            ArticleSignalStructure.CaseQuality => CaseQualityLabel,
            ArticleSignalStructure.BinaryTruth => BinaryTruthLabel,
            ArticleSignalStructure.UniformQuality => UniformQualityLabel,
            _ => throw new NotSupportedException(),
        };

        private List<Func<LitigGameOptions, LitigGameOptions>> SignalStructureTransformations() =>
            IncludedSignalStructures
                .Select(structure => (Func<LitigGameOptions, LitigGameOptions>)(options =>
                    GetAndTransform_SignalStructure(options, structure)))
                .ToList();

        private List<Func<LitigGameOptions, LitigGameOptions>> InformationLevelTransformations()
        {
            // Index zero is the baseline and is intentionally skipped by the non-core generator.
            InformationLevel[] orderedLevels = RunPlan == ProductionRunPlan.UniformBaselineSupplement
                ? new[] { GetInformationLevel(BaselineInformationLevelLabel) }
                : new[]
            {
                GetInformationLevel(BaselineInformationLevelLabel),
                GetInformationLevel("0.5x"),
                GetInformationLevel("2x"),
            };
            return orderedLevels
                .Select(level => (Func<LitigGameOptions, LitigGameOptions>)(options =>
                    GetAndTransform_InformationLevel(options, level)))
                .ToList();
        }

        private LitigGameOptions GetAndTransform_SignalStructure(
            LitigGameOptions options,
            ArticleSignalStructure structure) =>
            GetAndTransform(options, " SignalStructure " + GetIdentifierLabel(structure), g =>
            {
                InformationLevel level = GetInformationLevel(GetSetting(g, "Information Level"));
                ConfigureSignalStructureAndInformation(g, structure, level);
            });

        private LitigGameOptions GetAndTransform_InformationLevel(
            LitigGameOptions options,
            InformationLevel level) =>
            GetAndTransform(options, " Information " + level.Label, g =>
            {
                ArticleSignalStructure structure = ParseSignalStructure(GetSetting(g, "Signal Structure"));
                ConfigureSignalStructureAndInformation(g, structure, level);
            });

        private void ConfigureSignalStructureAndInformation(
            LitigGameOptions options,
            ArticleSignalStructure structure,
            InformationLevel level)
        {
            double partySigma = GetPartySigma(structure, level.Label);
            double courtSigma = GetCourtSigma(structure, level.Label);

            options.NumOffers = 10;
            options.NumLiabilitySignals = 10;
            options.PLiabilityNoiseStdev = partySigma;
            options.DLiabilityNoiseStdev = partySigma;

            // Party sigma is calibrated on the unconditional 10x10 party joint distribution.
            // Holding that value fixed, binary-truth court sigma is separately calibrated on the
            // full unconditional 10x10x2 party-and-court joint distribution.
            options.CourtLiabilityNoiseStdev = courtSigma;
            options.LiabilitySignalShapeParameters = IdentitySignalShapeParameters();
            options.DamagesSignalShapeParameters = IdentitySignalShapeParameters();

            if (structure == ArticleSignalStructure.CaseQuality)
            {
                options.NumLiabilityStrengthPoints = 10;
                options.LitigGameDisputeGenerator = new LitigGameExogenousDisputeGenerator
                {
                    ExogenousProbabilityTrulyLiable = 0.5,
                    StdevNoiseToProduceLiabilityStrength = 0.35,
                };
            }
            else if (structure == ArticleSignalStructure.BinaryTruth)
            {
                options.NumLiabilityStrengthPoints = 2;
                options.LitigGameDisputeGenerator = new LitigGameExogenousDirectSignalDisputeGenerator
                {
                    ExogenousProbabilityTrulyLiable = 0.5,
                };
            }
            else if (structure == ArticleSignalStructure.UniformQuality)
            {
                options.NumLiabilityStrengthPoints = 2;
                options.LitigGameDisputeGenerator = new LitigGameUniformQualityDisputeGenerator
                {
                    QuadratureOrder = LitigGameUniformQualityDisputeGenerator.DefaultQuadratureOrder,
                };
            }
            else
            {
                throw new NotSupportedException();
            }

            options.VariableSettings["Signal Structure"] = GetSignalStructureLabel(structure);
            options.VariableSettings["Information Level"] = level.Label;
            options.VariableSettings["Party Signal Sigma"] = FormatSigma(partySigma);
            options.VariableSettings["Court Signal Sigma"] = FormatSigma(courtSigma);
            options.VariableSettings["Liability Signal Shaping"] = "Identity";
            options.VariableSettings["Damages Signal Shaping"] = "Identity";
            options.VariableSettings["Number of Signals"] =
                options.NumLiabilitySignals.ToString(CultureInfo.InvariantCulture);
            options.VariableSettings["Number of Court Signals"] =
                options.NumCourtLiabilitySignals.ToString(CultureInfo.InvariantCulture);
            options.VariableSettings["Number of Offers"] = "10";

            if (RunPlan != ProductionRunPlan.LegacyTwoStructure)
                SetModelMetadata(options, structure);
        }

        private static void SetModelMetadata(
            LitigGameOptions options,
            ArticleSignalStructure structure)
        {
            (string distribution, string truthLink, string integration, string order) = structure switch
            {
                ArticleSignalStructure.CaseQuality =>
                    ("Truth-conditioned 10-point quality", "T -> Q", "Finite sum", "N/A"),
                ArticleSignalStructure.BinaryTruth =>
                    ("Binary truth", "Q = T", "Finite sum", "N/A"),
                ArticleSignalStructure.UniformQuality =>
                    ("Uniform [0..1] continuous quality", "T | Q ~ Bernoulli(Q)", "Gauss-Legendre", LitigGameUniformQualityDisputeGenerator.DefaultQuadratureOrder.ToString(CultureInfo.InvariantCulture)),
                _ => throw new NotSupportedException(),
            };
            options.VariableSettings["Quality Distribution"] = distribution;
            options.VariableSettings["Quality-Truth Link"] = truthLink;
            options.VariableSettings["Integration Method"] = integration;
            options.VariableSettings["Quadrature Order"] = order;
        }

        private static SignalShapeParameters IdentitySignalShapeParameters() =>
            new() { Mode = SignalShapeMode.Identity };

        private string CreateStableOptionSetIdentifier(LitigGameOptions options)
        {
            if (IsFocusedFamilyRun)
            {
                FocusedSpecification specification = ParseFocusedSpecification(
                    GetSetting(options, "Specification"));
                return string.Join("__", new[]
                {
                    "Specification-" + specification,
                    "Cost-" + FormatNumber(options.CostsMultiplier),
                    "Fee-" + GetSetting(options, "Fee Regime"),
                    RunPlan == ProductionRunPlan.MultipleEquilibriaRobustness
                        ? "Starts-" + MultipleEquilibriaInitializationCount
                        : null,
                    options.NumOffers != 10
                        ? "Offers-" + options.NumOffers.ToString(CultureInfo.InvariantCulture)
                        : null,
                }.Where(component => component != null));
            }

            ArticleSignalStructure structure = ParseSignalStructure(GetSetting(options, "Signal Structure"));
            string risk = GetSetting(options, "Risk Aversion") == "Risk Neutral"
                ? "RiskNeutral"
                : "ModeratelyRiskAverse";
            return string.Join("__", new[]
            {
                "Structure-" + GetIdentifierLabel(structure),
                "Cost-" + FormatNumber(options.CostsMultiplier),
                "FeeShift-" + FormatNumber(options.LoserPaysMultiple),
                "Info-" + GetSetting(options, "Information Level"),
                "PartySigma-" + GetSetting(options, "Party Signal Sigma"),
                "CourtSigma-" + GetSetting(options, "Court Signal Sigma"),
                "Risk-" + risk,
            });
        }

        private static FocusedSpecification ParseFocusedSpecification(string label) =>
            FocusedSpecifications.SingleOrDefault(definition => definition.Label == label)?.Specification
            ?? throw new InvalidOperationException($"Unknown CS003 specification '{label}'.");

        private static void ValidateFocusedOptionSet(
            LitigGameOptions options,
            ICollection<string> errors,
            int expectedOffers = 10)
        {
            string prefix = options.Name + ": ";
            FocusedSpecification specification;
            try
            {
                specification = ParseFocusedSpecification(GetSetting(options, "Specification"));
            }
            catch (Exception ex)
            {
                errors.Add(prefix + ex.Message);
                return;
            }

            if (!new[] { 0.25, 0.5, 1.0, 2.0, 4.0 }.Contains(options.CostsMultiplier))
                errors.Add(prefix + "has an unintended litigation-cost multiplier.");
            if (options.LoserPaysMultiple is not 0.0 and not 1.0)
                errors.Add(prefix + "has an unintended fee-shifting multiplier.");
            string expectedFeeRegime = options.LoserPaysMultiple == 0.0 ? "American" : "British";
            if (GetSetting(options, "Fee Regime") != expectedFeeRegime ||
                GetSetting(options, "Fee Shifting Multiplier") != FormatNumber(options.LoserPaysMultiple))
                errors.Add(prefix + "fee-regime metadata does not match the configured rule.");
            if (!options.LoserPays || options.LoserPaysAfterAbandonment || options.Rule68 ||
                options.LoserPaysOnlyLargeMarginOfVictory)
                errors.Add(prefix + "contains an unintended fee-shifting interaction.");

            if (options.NumLiabilitySignals != 10 || options.NumCourtLiabilitySignals != 2 ||
                options.NumOffers != expectedOffers)
                errors.Add(prefix + $"does not retain 10 party signals, 2 court signals, and {expectedOffers} offers.");
            if (GetSetting(options, "Number of Signals") != "10" ||
                GetSetting(options, "Number of Court Signals") != "2" ||
                GetSetting(options, "Number of Offers") != expectedOffers.ToString(CultureInfo.InvariantCulture))
                errors.Add(prefix + "signal/offer count metadata does not match the configured game.");
            if (options.NumPotentialBargainingRounds != 1 || !options.BargainingRoundsSimultaneous)
                errors.Add(prefix + "does not retain the one-round simultaneous-offer bargaining model.");
            if (options.LiabilitySignalShapeParameters.Mode != SignalShapeMode.Identity ||
                options.DamagesSignalShapeParameters.Mode != SignalShapeMode.Identity)
                errors.Add(prefix + "does not retain identity signal shaping.");
            if (Math.Abs(options.PLiabilityNoiseStdev - options.DLiabilityNoiseStdev) > 1E-12)
                errors.Add(prefix + "contains asymmetric party-signal noise.");
            if (Math.Abs(options.PFilingCost - options.DAnswerCost) > 1E-12 ||
                Math.Abs(options.PTrialCosts - options.DTrialCosts) > 1E-12)
                errors.Add(prefix + "contains asymmetric litigation costs.");
            if (options.RoundSpecificBargainingCosts != null ||
                Math.Abs(options.PerPartyCostsLeadingUpToBargainingRound) > 1E-12)
                errors.Add(prefix + "does not retain the published zero bargaining-round cost.");

            bool riskAverse = specification is
                FocusedSpecification.ModerateRiskAversion or
                FocusedSpecification.LowNoiseModerateRiskAversion;
            if (riskAverse)
            {
                if (options.PUtilityCalculator is not CARARiskAverseUtilityCalculator pUtility ||
                    options.DUtilityCalculator is not CARARiskAverseUtilityCalculator dUtility ||
                    Math.Abs(pUtility.Alpha - 2.0) > 1E-12 ||
                    Math.Abs(dUtility.Alpha - 2.0) > 1E-12)
                    errors.Add(prefix + "does not use symmetric CARA alpha 2.");
            }
            else if (options.PUtilityCalculator is not RiskNeutralUtilityCalculator ||
                options.DUtilityCalculator is not RiskNeutralUtilityCalculator)
            {
                errors.Add(prefix + "contains unintended risk aversion.");
            }

            double expectedPartySigma = specification switch
            {
                FocusedSpecification.LowNoise or
                FocusedSpecification.LowNoiseModerateRiskAversion => 0.10,
                FocusedSpecification.HighNoise => 0.40,
                FocusedSpecification.DirectBinaryStateSignals =>
                    GetInformationLevel(BaselineInformationLevelLabel).BinaryTruthPartySigma,
                _ => 0.20,
            };
            double expectedCourtSigma = specification == FocusedSpecification.DirectBinaryStateSignals
                ? GetInformationLevel(BaselineInformationLevelLabel).BinaryTruthCourtSigma
                : expectedPartySigma;
            if (Math.Abs(options.PLiabilityNoiseStdev - expectedPartySigma) > 1E-12 ||
                Math.Abs(options.CourtLiabilityNoiseStdev - expectedCourtSigma) > 1E-12)
                errors.Add(prefix + "has signal noise from another specification.");

            double expectedCostTiming = specification switch
            {
                FocusedSpecification.AllCostsAvoidable => 0.0,
                FocusedSpecification.AllCostsSunk => 1.0,
                _ => 0.5,
            };
            if (GetSetting(options, "Proportion of Costs at Beginning") !=
                FormatNumber(expectedCostTiming) ||
                Math.Abs(options.PFilingCost - PublishedTotalPerPartyLitigationCosts * expectedCostTiming) > 1E-12 ||
                Math.Abs(options.PTrialCosts - PublishedTotalPerPartyLitigationCosts * (1.0 - expectedCostTiming)) > 1E-12)
                errors.Add(prefix + "has cost timing from another specification.");

            bool expectedMandatory = specification is
                FocusedSpecification.MandatoryFilingAndAnswering or
                FocusedSpecification.MandatoryFilingAnsweringNoExit;
            bool expectedExit = specification != FocusedSpecification.MandatoryFilingAnsweringNoExit;
            if (options.SkipFileAndAnswerDecisions != expectedMandatory ||
                options.AllowAbandonAndDefaults != expectedExit)
                errors.Add(prefix + "has filing/answering or later-exit behavior from another specification.");

            bool correctGenerator = specification switch
            {
                FocusedSpecification.DirectBinaryStateSignals =>
                    options.LitigGameDisputeGenerator is LitigGameExogenousDirectSignalDisputeGenerator &&
                    options.NumLiabilityStrengthPoints == 2,
                FocusedSpecification.TruthConditionedLatentMerits =>
                    options.LitigGameDisputeGenerator is LitigGameExogenousDisputeGenerator exogenous &&
                    Math.Abs(exogenous.ExogenousProbabilityTrulyLiable - 0.5) < 1E-12 &&
                    Math.Abs(exogenous.StdevNoiseToProduceLiabilityStrength - 0.35) < 1E-12 &&
                    options.NumLiabilityStrengthPoints == 10,
                FocusedSpecification.CenterWeightedContinuousMerits =>
                    options.LitigGameDisputeGenerator is LitigGameUniformQualityDisputeGenerator center &&
                    center.QualityDistribution == ContinuousQualityDistribution.BetaTwoTwo &&
                    options.NumLiabilityStrengthPoints == 2,
                FocusedSpecification.PolarizedContinuousMerits =>
                    options.LitigGameDisputeGenerator is LitigGameUniformQualityDisputeGenerator polarized &&
                    polarized.QualityDistribution == ContinuousQualityDistribution.BetaHalfHalf &&
                    options.NumLiabilityStrengthPoints == 2,
                _ =>
                    options.LitigGameDisputeGenerator is LitigGameUniformQualityDisputeGenerator uniform &&
                    uniform.QualityDistribution == ContinuousQualityDistribution.Uniform &&
                    options.NumLiabilityStrengthPoints == 2,
            };
            if (!correctGenerator)
                errors.Add(prefix + "has a latent-merits generator from another specification.");
        }

        private void ValidateOptionSet(LitigGameOptions options, ICollection<string> errors)
        {
            string prefix = options.Name + ": ";
            ArticleSignalStructure structure;
            try
            {
                structure = ParseSignalStructure(GetSetting(options, "Signal Structure"));
            }
            catch (Exception ex)
            {
                errors.Add(prefix + ex.Message);
                return;
            }

            string informationLabel = GetSetting(options, "Information Level");
            double expectedSigma = GetPartySigma(structure, informationLabel);
            double expectedCourtSigma = GetCourtSigma(structure, informationLabel);
            if (Math.Abs(options.PLiabilityNoiseStdev - expectedSigma) > 1E-12 ||
                Math.Abs(options.DLiabilityNoiseStdev - expectedSigma) > 1E-12)
                errors.Add(prefix + $"party sigma is not calibrated for {informationLabel}.");
            if (Math.Abs(options.CourtLiabilityNoiseStdev - expectedCourtSigma) > 1E-12)
                errors.Add(prefix + "court sigma does not match the court calibration.");
            if (GetSetting(options, "Party Signal Sigma") != FormatSigma(expectedSigma) ||
                GetSetting(options, "Court Signal Sigma") != FormatSigma(expectedCourtSigma))
                errors.Add(prefix + "reported raw sigma does not match the configured sigma.");

            if (options.NumOffers != 10)
                errors.Add(prefix + $"uses {options.NumOffers} offers instead of 10.");
            if (options.NumLiabilitySignals != 10)
                errors.Add(prefix + $"uses {options.NumLiabilitySignals} party signals instead of 10.");
            if (options.NumCourtLiabilitySignals != 2)
                errors.Add(prefix + $"uses {options.NumCourtLiabilitySignals} court signals instead of 2.");
            if (GetSetting(options, "Number of Signals") != "10" ||
                GetSetting(options, "Number of Court Signals") != "2" ||
                GetSetting(options, "Number of Offers") != "10")
                errors.Add(prefix + "signal/offer count metadata does not match the configured game.");
            if (options.LiabilitySignalShapeParameters.Mode != SignalShapeMode.Identity ||
                options.DamagesSignalShapeParameters.Mode != SignalShapeMode.Identity)
                errors.Add(prefix + "does not use identity signal shaping.");

            bool correctGenerator = structure switch
            {
                ArticleSignalStructure.CaseQuality =>
                    options.LitigGameDisputeGenerator is LitigGameExogenousDisputeGenerator &&
                    options.NumLiabilityStrengthPoints == 10,
                ArticleSignalStructure.BinaryTruth =>
                    options.LitigGameDisputeGenerator is LitigGameExogenousDirectSignalDisputeGenerator &&
                    options.NumLiabilityStrengthPoints == 2,
                ArticleSignalStructure.UniformQuality =>
                    options.LitigGameDisputeGenerator is LitigGameUniformQualityDisputeGenerator uniform &&
                    uniform.QuadratureOrder == LitigGameUniformQualityDisputeGenerator.DefaultQuadratureOrder &&
                    options.NumLiabilityStrengthPoints == 2,
                _ => false,
            };
            if (!correctGenerator)
                errors.Add(prefix + "uses the wrong dispute generator or latent-state count.");

            if (RunPlan != ProductionRunPlan.LegacyTwoStructure)
            {
                var expectedMetadata = new LitigGameOptions();
                SetModelMetadata(expectedMetadata, structure);
                foreach (string key in new[]
                {
                    "Quality Distribution",
                    "Quality-Truth Link",
                    "Integration Method",
                    "Quadrature Order",
                })
                {
                    if (!options.VariableSettings.TryGetValue(key, out object actual) ||
                        !string.Equals(
                            Convert.ToString(actual, CultureInfo.InvariantCulture),
                            Convert.ToString(expectedMetadata.VariableSettings[key], CultureInfo.InvariantCulture),
                            StringComparison.Ordinal))
                        errors.Add(prefix + $"has incorrect model metadata for '{key}'.");
                }
            }

            string risk = GetSetting(options, "Risk Aversion");
            if (risk is not "Risk Neutral" and not "Moderately Risk Averse")
                errors.Add(prefix + $"contains unintended risk-aversion value '{risk}'.");
        }

        private static void RequireExactlyOne(
            IEnumerable<LitigGameOptions> coreGroup,
            string informationLevel,
            string riskAversion,
            ICollection<string> errors)
        {
            int count = coreGroup.Count(x =>
                GetSetting(x, "Information Level") == informationLevel &&
                GetSetting(x, "Risk Aversion") == riskAversion);
            if (count != 1)
                errors.Add(
                    $"Core combination {CoreCombinationKey(coreGroup.First())} has {count} rows for " +
                    $"information={informationLevel}, risk={riskAversion}; expected exactly one.");
        }

        private static string CoreCombinationKey(LitigGameOptions options) =>
            string.Join("|", new[]
            {
                GetSetting(options, "Signal Structure"),
                GetSetting(options, "Costs Multiplier"),
                GetSetting(options, "Fee Shifting Multiplier"),
            });

        private static string PairedComparisonKey(LitigGameOptions options) =>
            string.Join("|", new[]
            {
                GetSetting(options, "Costs Multiplier"),
                GetSetting(options, "Fee Shifting Multiplier"),
                GetSetting(options, "Information Level"),
                GetSetting(options, "Risk Aversion"),
            });

        private SimulationSetsIdentifier StructureComparison(string informationLevel) =>
            new(
                $"Signal Structure ({informationLevel})",
                IncludedSignalStructures.Select(structure =>
                    CreateSimulationIdentifier(
                        GetSignalStructureLabel(structure),
                        structure,
                        informationLevel,
                        "Risk Neutral")).ToList());

        private SimulationSetsIdentifier InformationComparison(ArticleSignalStructure structure) =>
            new(
                $"Information Level ({GetSignalStructureLabel(structure)})",
                ProductionInformationLevels.Select(level =>
                    CreateSimulationIdentifier(level.Label, structure, level.Label, "Risk Neutral")).ToList());

        private SimulationSetsIdentifier RiskComparison(ArticleSignalStructure structure) =>
            new(
                $"Risk Aversion ({GetSignalStructureLabel(structure)})",
                new List<SimulationIdentifier>
                {
                    CreateSimulationIdentifier("Risk Neutral", structure, BaselineInformationLevelLabel, "Risk Neutral"),
                    CreateSimulationIdentifier("Moderately Risk Averse", structure, BaselineInformationLevelLabel, "Moderately Risk Averse"),
                });

        private SimulationIdentifier CreateSimulationIdentifier(
            string name,
            ArticleSignalStructure structure,
            string informationLevel,
            string riskAversion)
        {
            double sigma = GetPartySigma(structure, informationLevel);
            double courtSigma = GetCourtSigma(structure, informationLevel);
            List<(string, string)> matches = DefaultVariableValues
                .WithReplacement("Signal Structure", GetSignalStructureLabel(structure))
                .WithReplacement("Information Level", informationLevel)
                .WithReplacement("Party Signal Sigma", FormatSigma(sigma))
                .WithReplacement("Court Signal Sigma", FormatSigma(courtSigma))
                .WithReplacement("Risk Aversion", riskAversion);
            if (RunPlan != ProductionRunPlan.LegacyTwoStructure)
            {
                var metadata = new LitigGameOptions();
                SetModelMetadata(metadata, structure);
                foreach (string key in new[]
                {
                    "Quality Distribution",
                    "Quality-Truth Link",
                    "Integration Method",
                    "Quadrature Order",
                })
                {
                    matches = matches.WithReplacement(
                        key,
                        Convert.ToString(metadata.VariableSettings[key], CultureInfo.InvariantCulture));
                }
            }
            return new SimulationIdentifier(name, matches);
        }

        private static ArticleSignalStructure ParseSignalStructure(string label) => label switch
        {
            CaseQualityLabel => ArticleSignalStructure.CaseQuality,
            BinaryTruthLabel => ArticleSignalStructure.BinaryTruth,
            UniformQualityLabel => ArticleSignalStructure.UniformQuality,
            _ => throw new InvalidOperationException($"Unknown signal structure '{label}'."),
        };

        private static string GetIdentifierLabel(ArticleSignalStructure structure) => structure switch
        {
            ArticleSignalStructure.CaseQuality => "CaseQuality",
            ArticleSignalStructure.BinaryTruth => "BinaryTruth",
            ArticleSignalStructure.UniformQuality => "UniformQuality",
            _ => throw new NotSupportedException(),
        };

        private static string GetSetting(GameOptions options, string key) =>
            options.VariableSettings.TryGetValue(key, out object value)
                ? Convert.ToString(value, CultureInfo.InvariantCulture)
                : throw new InvalidOperationException($"Option set '{options.Name}' is missing variable setting '{key}'.");

        private static string FormatNumber(double value) =>
            value.ToString("0.############", CultureInfo.InvariantCulture);

        private static string FormatSigma(double value) =>
            value.ToString("0.0000000000", CultureInfo.InvariantCulture);
    }
}
