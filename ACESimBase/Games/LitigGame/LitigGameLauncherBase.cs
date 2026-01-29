using ACESim;
using ACESimBase.GameSolvingSupport.DeepCFRSupport;
using ACESimBase.GameSolvingSupport.Settings;
using ACESimBase.Util.Collections;
using ACESimBase.Util.Mathematics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESimBase.Games.LitigGame
{
    public abstract class LitigGameLauncherBase : PermutationalLauncher
    {

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

        public virtual FeeShiftingRule[] FeeShiftingModes => new[] { FeeShiftingRule.English_LiabilityIssue, FeeShiftingRule.MarginOfVictory_LiabilityIssue, FeeShiftingRule.Rule68_DamagesIssue, FeeShiftingRule.Rule68English_DamagesIssue };
        public virtual double[] CriticalCostsMultipliers => new double[] { 1.0, 0.25, 0.5, 2.0, 4.0 };
        public virtual double[] AdditionalCostsMultipliers => new double[] { 1.0, 0.125, 8.0 }; // NOTE: If restoring this, also change NamesOfFeeShiftingArticleSets
        public virtual (double pNoiseMultiplier, double dNoiseMultiplier)[] NoiseMultipliers => new (double pNoiseMultiplier, double dNoiseMultiplier)[] { (1.0, 1.0), (0.50, 0.50), (0.5, 2.0), (2.0, 2.0), (2.0, 0.5), (0.25, 0.25), (4.0, 4.0) };
        public virtual double[] CriticalFeeShiftingMultipliers => new double[] { 0.0, 1.0, 0.5, 1.5, 2.0 };
        public virtual double[] AdditionalFeeShiftingMultipliers => new double[] { 0.0 }; // added all but 0 //, 0.25, 4.0 };// NOTE: If restoring this, also change NamesOfFeeShiftingArticleSets
        public virtual double[] RelativeCostsMultipliers => new double[] { 1.0, 0.5, 2.0 };
        public virtual double[] ProbabilitiesTrulyLiable => new double[] { 0.5, 0.1, 0.9 };
        public virtual double[] StdevsNoiseToProduceLiabilityStrength => new double[] { 0.35, 0.175, 0.70 };
        public virtual double[] ProportionOfCostsAtBeginning => new double[] { 0.5, 0.75, 0.25, 1.0, 0.0 };
        public virtual double[] DamagesMultipliers => new double[] { 1.0, 0.5, 2.0, 4.0 };




        #region Definitions
        public override string ReportPrefix => MasterReportNameForDistributedProcessing;
        
        public override GameDefinition GetGameDefinition() => new LitigGameDefinition();


        public override GameOptions GetDefaultSingleGameOptions()
        {
            return LitigGameOptionsGenerator.GetLitigGameOptions();
        }

        #endregion

        #region Transformations lists

        protected List<Func<LitigGameOptions, LitigGameOptions>> Transform<T>(Func<LitigGameOptions, T, LitigGameOptions> transformer, IEnumerable<T> values)
        {
            List<Func<LitigGameOptions, LitigGameOptions>> results = new List<Func<LitigGameOptions, LitigGameOptions>>();
            foreach (T value in values)
            {
                results.Add(o => transformer(o, value));
            }
            return results;
        }

        public List<Func<LitigGameOptions, LitigGameOptions>> FeeShiftingModeTransformations() => Transform(GetAndTransform_FeeShiftingMode, FeeShiftingModes);

        public List<Func<LitigGameOptions, LitigGameOptions>> CriticalCostsMultiplierTransformations()
            => Transform(GetAndTransform_CostsMultiplier, CriticalCostsMultipliers);

        public List<Func<LitigGameOptions, LitigGameOptions>> AdditionalCostsMultiplierTransformations()
            => Transform(GetAndTransform_CostsMultiplier, AdditionalCostsMultipliers);

        public List<Func<LitigGameOptions, LitigGameOptions>> CriticalFeeShiftingMultiplierTransformations()
            => Transform(GetAndTransform_FeeShiftingMultiplier, CriticalFeeShiftingMultipliers);

        public List<Func<LitigGameOptions, LitigGameOptions>> AdditionalFeeShiftingMultiplierTransformations()
            => Transform(GetAndTransform_FeeShiftingMultiplier, AdditionalFeeShiftingMultipliers);

        public List<Func<LitigGameOptions, LitigGameOptions>> EssentialFeeShiftingMultiplierTransformations()
            => Transform(GetAndTransform_FeeShiftingMultiplier, new double[] { 0, 1 });

        public List<Func<LitigGameOptions, LitigGameOptions>> DamagesMultiplierTransformations()
            => Transform(GetAndTransform_DamagesMultiplier, DamagesMultipliers);

        public List<Func<LitigGameOptions, LitigGameOptions>> PRelativeCostsTransformations()
            => Transform(GetAndTransform_PRelativeCosts, RelativeCostsMultipliers);

        public List<Func<LitigGameOptions, LitigGameOptions>> ProportionOfCostsAtBeginningTransformations()
            => Transform(GetAndTransform_ProportionOfCostsAtBeginning, ProportionOfCostsAtBeginning);

        public List<Func<LitigGameOptions, LitigGameOptions>> NoiseTransformations()
            => Transform((o, t) => GetAndTransform_Noise(o, t.pNoiseMultiplier, t.dNoiseMultiplier), NoiseMultipliers);

        public List<Func<LitigGameOptions, LitigGameOptions>> AllowAbandonAndDefaultsTransformations()
            => Transform(GetAndTransform_AllowAbandonAndDefaults, new[] { true, false });

        public List<Func<LitigGameOptions, LitigGameOptions>> ProbabilityTrulyLiableTransformations()
            => Transform(GetAndTransform_ProbabilityTrulyLiable, ProbabilitiesTrulyLiable);

        public List<Func<LitigGameOptions, LitigGameOptions>> NoiseToProduceCaseStrengthTransformations()
            => Transform(GetAndTransform_NoiseToProduceCaseStrength, StdevsNoiseToProduceLiabilityStrength);

        public List<Func<LitigGameOptions, LitigGameOptions>> LiabilityVsDamagesTransformations()
            => Transform(GetAndTransform_LiabilityVsDamages, new[] { true, false });
        
        public List<Func<LitigGameOptions, LitigGameOptions>> CriticalRiskAversionTransformations() => 
            new List<Func<LitigGameOptions, LitigGameOptions>>() 
            { 
                GetAndTransform_RiskNeutral, 
                GetAndTransform_ModeratelyRiskAverse 
            }.ToList();

        public List<Func<LitigGameOptions, LitigGameOptions>> AdditionalRiskAversionTransformations() => 
            new List<Func<LitigGameOptions, LitigGameOptions>>() 
            { 
                GetAndTransform_RiskNeutral, 
                GetAndTransform_MildlyRiskAverse, 
                GetAndTransform_VeryRiskAverse, 
                GetAndTransform_POnlyRiskAverse, 
                GetAndTransform_DOnlyRiskAverse, 
                GetAndTransform_PMoreRiskAverse, 
                GetAndTransform_DMoreRiskAverse 
            }.ToList();


        #endregion

        #region Transformations definitions

        public LitigGameOptions GetAndTransform_FeeShiftingMode(LitigGameOptions options, FeeShiftingRule mode) => GetAndTransform(options, " Fee Rule " + mode switch
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
        public LitigGameOptions GetAndTransform_CostsMultiplier(LitigGameOptions options, double multiplier) => GetAndTransform(options, " CM " + multiplier, g =>
        {
            g.CostsMultiplier = multiplier;
            g.VariableSettings["Costs Multiplier"] = multiplier;
        });

        public LitigGameOptions GetAndTransform_FeeShiftingMultiplier(LitigGameOptions options, double multiplier) => GetAndTransform(options, " FSM " + multiplier, g =>
        {
            g.LoserPays = true; // note that we're not using American rule, we're just varying the multiplier, so we should always set this to TRUE, unless we're using Rule 68 American, but that will then override this
            g.LoserPaysMultiple = multiplier;
            g.VariableSettings["Fee Shifting Multiplier"] = multiplier;
        });

        public LitigGameOptions GetAndTransform_DamagesMultiplier(LitigGameOptions options, double multiplier) => GetAndTransform(options, " DM " + multiplier, g =>
        {
            g.DamagesMultiplier = multiplier;
            g.VariableSettings["Damages Multiplier"] = multiplier;
        });

        public LitigGameOptions GetAndTransform_PRelativeCosts(LitigGameOptions options, double pRelativeCosts) => GetAndTransform(options, " RC " + pRelativeCosts, g =>
        {
            // Note: Currently, this does not affect per-round bargaining costs (not relevant in fee shifting article anyway).
            g.PFilingCost = g.DAnswerCost * pRelativeCosts;
            g.PTrialCosts = g.DTrialCosts * pRelativeCosts;
            g.VariableSettings["Relative Costs"] = pRelativeCosts;
        });

        public LitigGameOptions GetAndTransform_ProportionOfCostsAtBeginning(LitigGameOptions options, double proportionAtBeginning) => GetAndTransform(options, " TIME " + proportionAtBeginning, g =>
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

        public LitigGameOptions GetAndTransform_Noise(LitigGameOptions options, double pNoiseMultiplier, double dNoiseMultiplier) => GetAndTransform(options, " Noise " + pNoiseMultiplier + "," + dNoiseMultiplier, g =>
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

        public LitigGameOptions GetAndTransform_AllowAbandonAndDefaults(LitigGameOptions options, bool allowAbandonAndDefaults) => GetAndTransform(options, " ABAN " + allowAbandonAndDefaults, g =>
        {
            g.AllowAbandonAndDefaults = allowAbandonAndDefaults;
            g.VariableSettings["Allow Abandon and Defaults"] = allowAbandonAndDefaults;
        });

        public static bool UseDirectSignalExogenousDisputeGenerator = false;

        // DEBUG -- delete this
        protected static void EnsureExogenousDisputeGeneratorSelection(LitigGameOptions options)
        {
            if (options == null)
                throw new ArgumentNullException(nameof(options));

            // Generator selection is now performed during options construction (e.g., in GetLitigGameOptions and its helpers).
            // This method is intentionally a no-op to avoid post-construction generator swapping.
        }

        private static double GetNoiseToProduceCaseStrengthOrDefault(LitigGameOptions options)
        {
            if (options?.VariableSettings != null &&
                options.VariableSettings.TryGetValue("Noise to Produce Case Strength", out object value) &&
                value != null)
            {
                if (value is double d)
                    return d;
                if (value is float f)
                    return f;
                if (value is string s && double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double parsed))
                    return parsed;

                if (value is IConvertible convertible)
                {
                    try
                    {
                        return convertible.ToDouble(System.Globalization.CultureInfo.InvariantCulture);
                    }
                    catch
                    {
                    }
                }
            }

            return 0.35;
        }

        public LitigGameOptions GetAndTransform_ProbabilityTrulyLiable(LitigGameOptions options, double probability) => GetAndTransform(options, " TLP " + probability, g =>
        {
            if (g.LitigGameDisputeGenerator is LitigGameExogenousDisputeGenerator exogenous)
                exogenous.ExogenousProbabilityTrulyLiable = probability;
            else if (g.LitigGameDisputeGenerator is LitigGameExogenousDirectSignalDisputeGenerator direct)
                direct.ExogenousProbabilityTrulyLiable = probability;
            else
                throw new InvalidOperationException("ProbabilityTrulyLiableTransformations requires an exogenous dispute generator.");

            g.VariableSettings["Probability Truly Liable"] = probability;
        });

        public LitigGameOptions GetAndTransform_NoiseToProduceCaseStrength(LitigGameOptions options, double noise) => GetAndTransform(options, " CSN " + noise, g =>
        {
            if (g.LitigGameDisputeGenerator is LitigGameExogenousDisputeGenerator exogenous)
                exogenous.StdevNoiseToProduceLiabilityStrength = noise;

            g.VariableSettings["Noise to Produce Case Strength"] = noise;
        });

        public LitigGameOptions GetAndTransform_LiabilityVsDamages(LitigGameOptions options, bool liabilityIsUncertain) => GetAndTransform(options, liabilityIsUncertain ? " LIAB" : " DAM", g =>
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


        public LitigGameOptions GetAndTransform_ModeratelyRiskAverse(LitigGameOptions options) => GetAndTransform(options, " MODRA", g =>
        {
            g.PUtilityCalculator = new CARARiskAverseUtilityCalculator() { InitialWealth = g.PInitialWealth, Alpha = 2, LinearTransformation = true };
            g.DUtilityCalculator = new CARARiskAverseUtilityCalculator() { InitialWealth = g.DInitialWealth, Alpha = 2, LinearTransformation = true };
            g.VariableSettings["Risk Aversion"] = "Moderately Risk Averse";
        });
        public LitigGameOptions GetAndTransform_MildlyRiskAverse(LitigGameOptions options) => GetAndTransform(options, " MILDRA", g =>
        {
            g.PUtilityCalculator = new CARARiskAverseUtilityCalculator() { InitialWealth = g.PInitialWealth, Alpha = 1, LinearTransformation = true };
            g.DUtilityCalculator = new CARARiskAverseUtilityCalculator() { InitialWealth = g.DInitialWealth, Alpha = 1, LinearTransformation = true };
            g.VariableSettings["Risk Aversion"] = "Mildly Risk Averse";
        });
        public LitigGameOptions GetAndTransform_VeryRiskAverse(LitigGameOptions options) => GetAndTransform(options, " HIGHRA", g =>
        {
            g.PUtilityCalculator = new CARARiskAverseUtilityCalculator() { InitialWealth = g.PInitialWealth, Alpha = 4, LinearTransformation = true };
            g.DUtilityCalculator = new CARARiskAverseUtilityCalculator() { InitialWealth = g.DInitialWealth, Alpha = 4, LinearTransformation = true };
            g.VariableSettings["Risk Aversion"] = "Highly Risk Averse";
        });
        public LitigGameOptions GetAndTransform_PMoreRiskAverse(LitigGameOptions options) => GetAndTransform(options, " PMORERA", g =>
        {
            g.PUtilityCalculator = new CARARiskAverseUtilityCalculator() { InitialWealth = g.PInitialWealth, Alpha = 4, LinearTransformation = true };
            g.DUtilityCalculator = new CARARiskAverseUtilityCalculator() { InitialWealth = g.DInitialWealth, Alpha = 2, LinearTransformation = true };
            g.VariableSettings["Risk Aversion"] = "P More Risk Averse";
        });
        public LitigGameOptions GetAndTransform_DMoreRiskAverse(LitigGameOptions options) => GetAndTransform(options, " DMORERA", g =>
        {
            g.PUtilityCalculator = new CARARiskAverseUtilityCalculator() { InitialWealth = g.PInitialWealth, Alpha = 2, LinearTransformation = true };
            g.DUtilityCalculator = new CARARiskAverseUtilityCalculator() { InitialWealth = g.DInitialWealth, Alpha = 4, LinearTransformation = true };
            g.VariableSettings["Risk Aversion"] = "D More Risk Averse";
        });

        public LitigGameOptions GetAndTransform_RiskNeutral(LitigGameOptions options) => GetAndTransform(options, " Risk Neutral", g =>
        {
            g.PUtilityCalculator = new RiskNeutralUtilityCalculator() { InitialWealth = g.PInitialWealth };
            g.DUtilityCalculator = new RiskNeutralUtilityCalculator() { InitialWealth = g.DInitialWealth };
            g.VariableSettings["Risk Aversion"] = "Risk Neutral";
        });
        public LitigGameOptions GetAndTransform_POnlyRiskAverse(LitigGameOptions options) => GetAndTransform(options, " P Risk Averse", g =>
        {
            g.PUtilityCalculator = new CARARiskAverseUtilityCalculator() { InitialWealth = g.PInitialWealth, Alpha = 2, LinearTransformation = true };
            g.DUtilityCalculator = new RiskNeutralUtilityCalculator() { InitialWealth = g.DInitialWealth };
            g.VariableSettings["Risk Aversion"] = "P Risk Averse";
        });
        public LitigGameOptions GetAndTransform_DOnlyRiskAverse(LitigGameOptions options) => GetAndTransform(options, " D Risk Averse", g =>
        {
            g.PUtilityCalculator = new RiskNeutralUtilityCalculator() { InitialWealth = g.PInitialWealth };
            g.DUtilityCalculator = new CARARiskAverseUtilityCalculator() { InitialWealth = g.DInitialWealth, Alpha = 2, LinearTransformation = true };
            g.VariableSettings["Risk Aversion"] = "D Risk Averse";
        });

        #endregion

        #region Helpers

        public record class SimulationSetsTransformer(Func<SimulationSetsIdentifier, bool> includeSet, List<(string nameOfSet, string nameForSimulation)> changesToMake);

        public static SimulationSetsTransformer RequireModerateRiskAversion = new SimulationSetsTransformer(
                x => !x.nameOfSet.StartsWith("Risk"),
                [("Risk Aversion", "Moderately Risk Averse")]
                );

        public abstract List<SimulationSetsIdentifier> GetSimulationSetsIdentifiers(SimulationSetsTransformer transformer = null);

        protected static List<SimulationSetsIdentifier> PerformArticleVariationInfoSetsTransformation(SimulationSetsTransformer transformer, List<SimulationSetsIdentifier> tentativeResults)
        {
            if (transformer is not null)
            {
                // eliminate risk-related reports
                tentativeResults = tentativeResults.Where(transformer.includeSet).ToList();
                for (int i = 0; i < tentativeResults.Count; i++)
                {
                    SimulationSetsIdentifier variationSetInfo = tentativeResults[i];
                    tentativeResults[i] = variationSetInfo;
                    foreach (var changeToMake in transformer.changesToMake)
                    {
                        tentativeResults[i] = tentativeResults[i] with
                        {
                            simulationIdentifiers = variationSetInfo.simulationIdentifiers.Select(x => x with
                            {
                                columnMatches = x.columnMatches.WithReplacement(changeToMake.nameOfSet, changeToMake.nameForSimulation)
                            }).ToList()
                        };
                    }
                }
            }

            return tentativeResults;
        }

        public static LitigGameProgress PlayLitigGameOnce(LitigGameOptions options,
            Func<Decision, GameProgress, byte> actionsOverride, LitigGameDefinition gameDefinition = null)
        {
            if (gameDefinition == null)
                gameDefinition = new LitigGameDefinition();
            gameDefinition.Setup(options);
            List<Strategy> starterStrategies = Strategy.GetStarterStrategies(gameDefinition);

            if (GameProgressLogger.LoggingOn)
                gameDefinition.PrintOutOrderingInformation();

            bool useDirectGamePlayer = false; // potentially useful to test directgameplayer, but slower
            LitigGameProgress gameProgress = null;
            if (useDirectGamePlayer)
            {
                gameProgress = (LitigGameProgress)gameDefinition.CreateLitigGameFactory().CreateNewGameProgress(gameDefinition, true, new IterationID());
                DirectGamePlayer directGamePlayer = new DeepCFRDirectGamePlayer(DeepCFRMultiModelMode.DecisionSpecific, gameDefinition, gameProgress, true, false, default);
                gameProgress = (LitigGameProgress)directGamePlayer.PlayWithActionsOverride(actionsOverride);
            }
            else
            {
                GamePlayer gamePlayer = new GamePlayer(starterStrategies, false, gameDefinition, true);
                gameProgress = (LitigGameProgress)gamePlayer.PlayUsingActionOverride(actionsOverride);
            }

            return gameProgress;
        }
        #endregion

    }
}
