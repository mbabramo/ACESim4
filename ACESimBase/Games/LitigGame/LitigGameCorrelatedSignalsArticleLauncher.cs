using ACESim.Util.DiscreteProbabilities;
using ACESimBase;
using ACESimBase.Games.LitigGame;
using ACESimBase.GameSolvingSupport.Settings;
using ACESimBase.Util.Collections;
using ACESimBase.Util.Combinatorics;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace ACESim
{
    /// <summary>
    /// Production launcher for the correlated-signals article. The production matrix crosses
    /// signal structure, costs, and fee shifting globally. Information and risk aversion are
    /// non-core robustness dimensions: each non-baseline value is evaluated against every global
    /// combination, but the two robustness dimensions are not crossed with one another.
    /// </summary>
    public class LitigGameCorrelatedSignalsArticleLauncher : LitigGameLauncherBase
    {
        public enum ProductionRunPlan
        {
            LegacyTwoStructure,
            UniformBaselineSupplement,
            UnifiedThreeStructure,
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
            IReadOnlyDictionary<string, int> CountsByInformationAndRisk);

        public const string CaseQualityLabel = "Case quality";
        public const string BinaryTruthLabel = "Binary truth";
        public const string UniformQualityLabel = "Uniform quality";
        public const string BaselineInformationLevelLabel = "1x";
        public const int ProductionOptionSetCount = 200;
        public const int ProductionCoreCombinationCount = 50;
        public const int ProductionPairedComparisonCount = 100;
        public const int SupplementalOptionSetCount = 25;
        public const int SupplementalCoreCombinationCount = 25;
        public const int SupplementalComparisonGroupCount = 25;
        public const int UnifiedOptionSetCount = 300;
        public const int UnifiedCoreCombinationCount = 75;
        public const int UnifiedComparisonGroupCount = 100;

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
            (value ?? "unified").Trim().ToLowerInvariant() switch
            {
                "legacy" or "cs001" => ProductionRunPlan.LegacyTwoStructure,
                "supplemental" or "supplement" or "uniform" or "cs002u" => ProductionRunPlan.UniformBaselineSupplement,
                "unified" or "all" or "cs002" => ProductionRunPlan.UnifiedThreeStructure,
                _ => throw new ArgumentException(
                    $"Unknown correlated-signals plan '{value}'. Expected legacy, supplemental, or unified."),
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
            _ => throw new NotSupportedException(),
        };

        private int ExpectedOptionSetCount => RunPlan switch
        {
            ProductionRunPlan.LegacyTwoStructure => ProductionOptionSetCount,
            ProductionRunPlan.UniformBaselineSupplement => SupplementalOptionSetCount,
            ProductionRunPlan.UnifiedThreeStructure => UnifiedOptionSetCount,
            _ => throw new NotSupportedException(),
        };

        private int ExpectedCoreCombinationCount => RunPlan switch
        {
            ProductionRunPlan.LegacyTwoStructure => ProductionCoreCombinationCount,
            ProductionRunPlan.UniformBaselineSupplement => SupplementalCoreCombinationCount,
            ProductionRunPlan.UnifiedThreeStructure => UnifiedCoreCombinationCount,
            _ => throw new NotSupportedException(),
        };

        private int ExpectedComparisonGroupCount => RunPlan switch
        {
            ProductionRunPlan.LegacyTwoStructure => ProductionPairedComparisonCount,
            ProductionRunPlan.UniformBaselineSupplement => SupplementalComparisonGroupCount,
            ProductionRunPlan.UnifiedThreeStructure => UnifiedComparisonGroupCount,
            _ => throw new NotSupportedException(),
        };

        public override double[] AdditionalCostsMultipliers => Array.Empty<double>();
        public override double[] AdditionalFeeShiftingMultipliers => Array.Empty<double>();

        public override List<(string, string)> DefaultVariableValues
        {
            get
            {
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

        public override List<(string criticalValueName, string[] criticalValueValues)> CriticalVariableValues =>
            new()
            {
                ("Signal Structure", IncludedSignalStructures.Select(GetSignalStructureLabel).ToArray()),
                ("Costs Multiplier", CriticalCostsMultipliers.Select(FormatNumber).ToArray()),
                ("Fee Shifting Multiplier", CriticalFeeShiftingMultipliers.Select(FormatNumber).ToArray()),
            };

        public override GameDefinition GetGameDefinition() => new LitigGameDefinition();

        public override GameOptions GetDefaultSingleGameOptions()
        {
            LitigGameOptions options = LitigGameOptionsGenerator.CorrelatedSignalsBase(smallerTree: false);
            options.NumOffers = 10;
            options.NumLiabilitySignals = 10;
            options.LiabilitySignalShapeParameters = IdentitySignalShapeParameters();
            options.DamagesSignalShapeParameters = IdentitySignalShapeParameters();
            ConfigureSignalStructureAndInformation(
                options,
                IncludedSignalStructures.First(),
                GetInformationLevel(BaselineInformationLevelLabel));
            return options;
        }

        public override List<GameOptions> GetOptionsSets()
        {
            var optionSets = new List<GameOptions>();
            AddToOptionsSets(optionSets);

            foreach (LitigGameOptions options in optionSets.Cast<LitigGameOptions>())
                options.Name = CreateStableOptionSetIdentifier(options);

            optionSets = optionSets.OrderBy(x => x.Name, StringComparer.Ordinal).ToList();
            ValidateProductionMatrix(optionSets);
            return optionSets;
        }

        public override List<VariableCombinationGenerator.Dimension<LitigGameOptions>> GetVariationSetsInfo() =>
            new()
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

        private List<Func<LitigGameOptions, LitigGameOptions>> RiskAversionTransformations() =>
            RunPlan == ProductionRunPlan.UniformBaselineSupplement
                ? new List<Func<LitigGameOptions, LitigGameOptions>> { GetAndTransform_RiskNeutral }
                : new List<Func<LitigGameOptions, LitigGameOptions>>
                {
                    GetAndTransform_RiskNeutral,
                    GetAndTransform_ModeratelyRiskAverse,
                };

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

        public override List<SimulationSetsIdentifier> GetSimulationSetsIdentifiers(SimulationSetsTransformer transformer = null)
        {
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

        private static string CreateStableOptionSetIdentifier(LitigGameOptions options)
        {
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
