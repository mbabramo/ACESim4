using ACESim;
using ACESimBase.GameSolvingSupport.DeepCFRSupport;
using ACESimBase.GameSolvingSupport.Settings;
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

        public bool IncludeNonCriticalTransformations = true;
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

        protected List<Func<LitigGameOptions, LitigGameOptions>> Transform<T>(Func<LitigGameOptions, T, LitigGameOptions> transformer, IEnumerable<T> values, bool includeBaselineValue)
        {
            List<Func<LitigGameOptions, LitigGameOptions>> results = new List<Func<LitigGameOptions, LitigGameOptions>>();
            foreach (T value in values.Skip(includeBaselineValue ? 0 : 1))
            {
                results.Add(o => transformer(o, value));
            }
            return results;
        }

        public List<Func<LitigGameOptions, LitigGameOptions>> FeeShiftingModeTransformations(bool includeBaselineValue) => Transform(GetAndTransform_FeeShiftingMode, FeeShiftingModes, includeBaselineValue);

        public List<Func<LitigGameOptions, LitigGameOptions>> CriticalCostsMultiplierTransformations(bool includeBaselineValue)
            => Transform(GetAndTransform_CostsMultiplier, CriticalCostsMultipliers, includeBaselineValue);

        public List<Func<LitigGameOptions, LitigGameOptions>> AdditionalCostsMultiplierTransformations(bool includeBaselineValue)
            => Transform(GetAndTransform_CostsMultiplier, AdditionalCostsMultipliers, includeBaselineValue);

        public List<Func<LitigGameOptions, LitigGameOptions>> CriticalFeeShiftingMultiplierTransformations(bool includeBaselineValue)
            => Transform(GetAndTransform_FeeShiftingMultiplier, CriticalFeeShiftingMultipliers, includeBaselineValue);

        public List<Func<LitigGameOptions, LitigGameOptions>> AdditionalFeeShiftingMultiplierTransformations(bool includeBaselineValue)
            => Transform(GetAndTransform_FeeShiftingMultiplier, AdditionalFeeShiftingMultipliers, includeBaselineValue);

        public List<Func<LitigGameOptions, LitigGameOptions>> EssentialFeeShiftingMultiplierTransformations()
            => Transform(GetAndTransform_FeeShiftingMultiplier, new double[] { 0, 1 }, true);

        public List<Func<LitigGameOptions, LitigGameOptions>> DamagesMultiplierTransformations(bool includeBaselineValue)
            => Transform(GetAndTransform_DamagesMultiplier, DamagesMultipliers, includeBaselineValue);

        public List<Func<LitigGameOptions, LitigGameOptions>> PRelativeCostsTransformations(bool includeBaselineValue)
            => Transform(GetAndTransform_PRelativeCosts, RelativeCostsMultipliers, includeBaselineValue);

        public List<Func<LitigGameOptions, LitigGameOptions>> ProportionOfCostsAtBeginningTransformations(bool includeBaselineValue)
            => Transform(GetAndTransform_ProportionOfCostsAtBeginning, ProportionOfCostsAtBeginning, includeBaselineValue);

        public List<Func<LitigGameOptions, LitigGameOptions>> NoiseTransformations(bool includeBaselineValue)
            => Transform((o, t) => GetAndTransform_Noise(o, t.pNoiseMultiplier, t.dNoiseMultiplier), NoiseMultipliers, includeBaselineValue);

        public List<Func<LitigGameOptions, LitigGameOptions>> AllowAbandonAndDefaultsTransformations(bool includeBaselineValue)
            => Transform(GetAndTransform_AllowAbandonAndDefaults, new[] { true, false }, includeBaselineValue);

        public List<Func<LitigGameOptions, LitigGameOptions>> ProbabilityTrulyLiableTransformations(bool includeBaselineValue)
            => Transform(GetAndTransform_ProbabilityTrulyLiable, ProbabilitiesTrulyLiable, includeBaselineValue);

        public List<Func<LitigGameOptions, LitigGameOptions>> NoiseToProduceCaseStrengthTransformations(bool includeBaselineValue)
            => Transform(GetAndTransform_NoiseToProduceCaseStrength, StdevsNoiseToProduceLiabilityStrength, includeBaselineValue);

        public List<Func<LitigGameOptions, LitigGameOptions>> LiabilityVsDamagesTransformations(bool includeBaselineValue)
            => Transform(GetAndTransform_LiabilityVsDamages, new[] { true, false }, includeBaselineValue);
        
        public List<Func<LitigGameOptions, LitigGameOptions>> CriticalRiskAversionTransformations(bool includeBaselineValue) => 
            new List<Func<LitigGameOptions, LitigGameOptions>>() 
            { 
                GetAndTransform_RiskNeutral, 
                GetAndTransform_ModeratelyRiskAverse 
            }.Skip(includeBaselineValue ? 0 : 1).ToList();

        public List<Func<LitigGameOptions, LitigGameOptions>> AdditionalRiskAversionTransformations(bool includeBaselineValue) => 
            new List<Func<LitigGameOptions, LitigGameOptions>>() 
            { 
                GetAndTransform_RiskNeutral, 
                GetAndTransform_MildlyRiskAverse, 
                GetAndTransform_VeryRiskAverse, 
                GetAndTransform_POnlyRiskAverse, 
                GetAndTransform_DOnlyRiskAverse, 
                GetAndTransform_PMoreRiskAverse, 
                GetAndTransform_DMoreRiskAverse 
            }.Skip(includeBaselineValue ? 0 : 1).ToList();


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
        public LitigGameOptions GetAndTransform_CostsMultiplier(LitigGameOptions options, double multiplier) => GetAndTransform(options, " Costs Multiplier " + multiplier, g =>
        {
            g.CostsMultiplier = multiplier;
            g.VariableSettings["Costs Multiplier"] = multiplier;
        });

        public LitigGameOptions GetAndTransform_FeeShiftingMultiplier(LitigGameOptions options, double multiplier) => GetAndTransform(options, " Fee Shifting Multiplier " + multiplier, g =>
        {
            g.LoserPays = true; // note that we're not using American rule, we're just varying the multiplier, so we should always set this to TRUE, unless we're using Rule 68 American, but that will then override this
            g.LoserPaysMultiple = multiplier;
            g.VariableSettings["Fee Shifting Multiplier"] = multiplier;
        });

        public LitigGameOptions GetAndTransform_DamagesMultiplier(LitigGameOptions options, double multiplier) => GetAndTransform(options, " Damages Multiplier " + multiplier, g =>
        {
            g.DamagesMultiplier = multiplier;
            g.VariableSettings["Damages Multiplier"] = multiplier;
        });

        public LitigGameOptions GetAndTransform_PRelativeCosts(LitigGameOptions options, double pRelativeCosts) => GetAndTransform(options, " Relative Costs " + pRelativeCosts, g =>
        {
            // Note: Currently, this does not affect per-round bargaining costs (not relevant in fee shifting article anyway).
            g.PFilingCost = g.DAnswerCost * pRelativeCosts;
            g.PTrialCosts = g.DTrialCosts * pRelativeCosts;
            g.VariableSettings["Relative Costs"] = pRelativeCosts;
        });

        public LitigGameOptions GetAndTransform_ProportionOfCostsAtBeginning(LitigGameOptions options, double proportionAtBeginning) => GetAndTransform(options, " Timing " + proportionAtBeginning, g =>
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

        public LitigGameOptions GetAndTransform_AllowAbandonAndDefaults(LitigGameOptions options, bool allowAbandonAndDefaults) => GetAndTransform(options, " Abandonable " + allowAbandonAndDefaults, g =>
        {
            g.AllowAbandonAndDefaults = allowAbandonAndDefaults;
            g.VariableSettings["Allow Abandon and Defaults"] = allowAbandonAndDefaults;
        });

        public LitigGameOptions GetAndTransform_ProbabilityTrulyLiable(LitigGameOptions options, double probability) => GetAndTransform(options, " Truly Liable Probability " + probability, g =>
        {
            ((LitigGameExogenousDisputeGenerator)g.LitigGameDisputeGenerator).ExogenousProbabilityTrulyLiable = probability;

            g.VariableSettings["Probability Truly Liable"] = probability;
        });

        public LitigGameOptions GetAndTransform_NoiseToProduceCaseStrength(LitigGameOptions options, double noise) => GetAndTransform(options, " Case Strength Noise " + noise, g =>
        {
            ((LitigGameExogenousDisputeGenerator)g.LitigGameDisputeGenerator).StdevNoiseToProduceLiabilityStrength = noise;
            g.VariableSettings["Noise to Produce Case Strength"] = noise;
        });

        public LitigGameOptions GetAndTransform_LiabilityVsDamages(LitigGameOptions options, bool liabilityIsUncertain) => GetAndTransform(options, liabilityIsUncertain ? " Liability Dispute" : " Damages Dispute", g =>
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

        public LitigGameOptions GetAndTransform_ModeratelyRiskAverse(LitigGameOptions options) => GetAndTransform(options, " Moderately Risk Averse", g =>
        {
            g.PUtilityCalculator = new CARARiskAverseUtilityCalculator() { InitialWealth = g.PInitialWealth, Alpha = 2, LinearTransformation = true };
            g.DUtilityCalculator = new CARARiskAverseUtilityCalculator() { InitialWealth = g.DInitialWealth, Alpha = 2, LinearTransformation = true };
            g.VariableSettings["Risk Aversion"] = "Moderately Risk Averse";
        });
        public LitigGameOptions GetAndTransform_MildlyRiskAverse(LitigGameOptions options) => GetAndTransform(options, " Mildly Risk Averse", g =>
        {
            g.PUtilityCalculator = new CARARiskAverseUtilityCalculator() { InitialWealth = g.PInitialWealth, Alpha = 1, LinearTransformation = true };
            g.DUtilityCalculator = new CARARiskAverseUtilityCalculator() { InitialWealth = g.DInitialWealth, Alpha = 1, LinearTransformation = true };
            g.VariableSettings["Risk Aversion"] = "Mildly Risk Averse";
        });
        public LitigGameOptions GetAndTransform_VeryRiskAverse(LitigGameOptions options) => GetAndTransform(options, " Highly Risk Averse", g =>
        {
            g.PUtilityCalculator = new CARARiskAverseUtilityCalculator() { InitialWealth = g.PInitialWealth, Alpha = 4, LinearTransformation = true };
            g.DUtilityCalculator = new CARARiskAverseUtilityCalculator() { InitialWealth = g.DInitialWealth, Alpha = 4, LinearTransformation = true };
            g.VariableSettings["Risk Aversion"] = "Highly Risk Averse";
        });
        public LitigGameOptions GetAndTransform_PMoreRiskAverse(LitigGameOptions options) => GetAndTransform(options, " P More Risk Averse", g =>
        {
            g.PUtilityCalculator = new CARARiskAverseUtilityCalculator() { InitialWealth = g.PInitialWealth, Alpha = 4, LinearTransformation = true };
            g.DUtilityCalculator = new CARARiskAverseUtilityCalculator() { InitialWealth = g.DInitialWealth, Alpha = 2, LinearTransformation = true };
            g.VariableSettings["Risk Aversion"] = "P More Risk Averse";
        });
        public LitigGameOptions GetAndTransform_DMoreRiskAverse(LitigGameOptions options) => GetAndTransform(options, " D More Risk Averse", g =>
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

        #region Helper methods

        protected List<List<GameOptions>> PerformTransformations(List<List<Func<LitigGameOptions, LitigGameOptions>>> allTransformations, int numCritical, bool useAllPermutationsOfTransformations, bool includeBaselineValueForNoncritical)
        {
            List<List<LitigGameOptions>> listOfOptionSets = new List<List<LitigGameOptions>>();
            List<List<Func<LitigGameOptions, LitigGameOptions>>> criticalTransformations = allTransformations.Take(numCritical).ToList();
            List<List<Func<LitigGameOptions, LitigGameOptions>>> noncriticalTransformations = allTransformations.Skip(IncludeNonCriticalTransformations ? numCritical : allTransformations.Count()).ToList();
            (List<Func<LitigGameOptions, LitigGameOptions>> noncritical, List<Func<LitigGameOptions, LitigGameOptions>> critical)[] nonCriticalCriticalPairs =
                Enumerable.Range(0, numCritical)
                    .Select(i => (
                        noncritical: allTransformations[numCritical + i],
                        critical: allTransformations[i]
                    ))
                    .ToArray();
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
                    foreach ((List<Func<LitigGameOptions, LitigGameOptions>> noncritical, List<Func<LitigGameOptions, LitigGameOptions>> critical) in nonCriticalCriticalPairs)
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
                    List<LitigGameOptions> noncriticalOptions = ApplyPermutationsOfTransformations(LitigGameOptionsGenerator.GetLitigGameOptions, transformLists);
                    List<(string, string)> defaultNonCriticalValues = DefaultVariableValues;
                    foreach (var optionSet in noncriticalOptions)
                    {
                        foreach (var defaultPair in defaultNonCriticalValues)
                            if (!optionSet.VariableSettings.ContainsKey(defaultPair.Item1))
                                optionSet.VariableSettings[defaultPair.Item1] = defaultPair.Item2;
                    }

                    //var optionSetNames = noncriticalOptions.Select(x => x.Name).OrderBy(x => x).ToList();  
                    listOfOptionSets.Add(noncriticalOptions);
                }
            }
            var result = listOfOptionSets.Select(innerList => innerList.Cast<GameOptions>().ToList()).ToList();
            return result;
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
