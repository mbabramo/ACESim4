using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.ComponentModel.Composition;

namespace ACESim
{
    [Export(typeof(ICodeBasedSettingGenerator))] // include the export and exportmetadata attributes if we implement GenerateSetting, setting up the GameModule by code instead of in the settings file.
    [ExportMetadata("CodeGeneratorName", "NegligenceDisputeGeneratorModule")]
    [Serializable]
    public class NegligenceDisputeGeneratorModule : DisputeGeneratorModule, ICodeBasedSettingGenerator
    {
        public NegligenceDisputeGeneratorModuleProgress NDGProgress { get { return (NegligenceDisputeGeneratorModuleProgress)GameModuleProgress; } set { GameModuleProgress = value; } }

        public override void Process(DisputeGeneratorInputs moduleInputs)
        {
            if (Game.CurrentDecisionIndexWithinActionGroup == 0 && Game.PreparationPhase)
            {
                NDGProgress.NDGInputs = (NegligenceDisputeGeneratorInputs)moduleInputs;
                double proxyForProbabilityInjuryWithoutPrecaution = NDGProgress.NDGInputs.ProbabilityInjuryWithoutPrecaution + NDGProgress.NDGInputs.ProbabilityInjuryWithoutPrecautionNoise;
                List<double> inputs = new List<double>() { NDGProgress.NDGInputs.CostOfPrecaution, NDGProgress.NDGInputs.UndetectedCasesForEachDetectedCase, proxyForProbabilityInjuryWithoutPrecaution, NDGProgress.NDGInputs.MagnitudeOfInjury };
                SpecifyInputs(inputs);
            }
            else if (Game.CurrentDecisionIndexWithinActionGroup == 0 && !Game.PreparationPhase)
            {
                double calculationResult = Calculate();
                NDGProgress.TakePrecaution = calculationResult > 0.0;
                if (NDGProgress.TakePrecaution)
                    NDGProgress.PrelitigationWelfareEffectOnD -= NDGProgress.NDGInputs.CostOfPrecaution * (1.0 + NDGProgress.NDGInputs.UndetectedCasesForEachDetectedCase);
                double probabilityOfInjury = NDGProgress.TakePrecaution ? NDGProgress.NDGInputs.ProbabilityInjuryWithPrecaution : NDGProgress.NDGInputs.ProbabilityInjuryWithoutPrecaution;
                bool injuryOccurs = probabilityOfInjury > NDGProgress.NDGInputs.ProbabilityInjuryRandomSeed;

                NDGProgress.DisputeExists = injuryOccurs;
                if (NDGProgress.DisputeExists)
                {
                    NDGProgress.CostBenefitRatio = NDGProgress.NDGInputs.CostOfPrecaution / ((NDGProgress.NDGInputs.ProbabilityInjuryWithoutPrecaution - NDGProgress.NDGInputs.ProbabilityInjuryWithPrecaution) * NDGProgress.NDGInputs.MagnitudeOfInjury);
                    if (NDGProgress.NDGInputs.StrictLiability)
                    {
                        NDGProgress.PShouldWin = true;
                        NDGProgress.EvidentiaryStrengthLiability = 1.0;
                        NDGProgress.BaseProbabilityPWins = 1.0;
                    }
                    else
                    {

                        NDGProgress.PShouldWin = (!NDGProgress.TakePrecaution && NDGProgress.CostBenefitRatio < 1);
                        if (NDGProgress.TakePrecaution)
                        {
                            NDGProgress.EvidentiaryStrengthLiability = 0; /* we assume that if the precaution is taken, there is no evidence that it has not been taken, though parties may still misestimate the evidence and there is still some chance of liability */;
                            NDGProgress.BaseProbabilityPWins = NDGProgress.NDGInputs.BaseProbabilityLiabilityWithPrecaution;
                        }
                        else
                        {
                            if (NDGProgress.CostBenefitRatio <= 1.0)
                                NDGProgress.EvidentiaryStrengthLiability = 1.0 - 0.5 * NDGProgress.CostBenefitRatio; // 0 cost/benefit ratio means strongest possible plaintiff's case, and thus evidentiary strength of 1.0; 1 cost/benefit ratio is a case right in the middle, and thus 0.5
                            else
                                NDGProgress.EvidentiaryStrengthLiability = 0.5 - 0.5 * (1.0 - 1.0 / NDGProgress.CostBenefitRatio); // as cost benefit ratio becomes high, inverse becomes low, and 1.0 - inverse becomes close to 1.0, so at extreme high end of cost-benefit ratio, meaning that costs are prohibitive and precautions should not be undertaken, we have evidentiary strength of 0.0; when c/b = 1.0, then we have case right in the middle, and thus 0.5
                            NDGProgress.BaseProbabilityPWins =
                                HyperbolicTangentCurve.GetYValueTwoSided(
                                    1.0, // critical point occurs when c/b = 1 ...
                                    0.5, // ... jury is likely as not to find liability
                                    NDGProgress.NDGInputs.BaseProbabilityLiabilityWithoutPrecautionMaximum, // probability of liability when costs are tiny compared to benefits of precaution
                                    NDGProgress.NDGInputs.BaseProbabilityLiabilityWithoutPrecautionMinimum, // probability of liability when benefits are tiny compared to costs
                                    0.5, // when c/b = 1/2
                                    NDGProgress.NDGInputs.BaseProbabilityLiabilityWithoutPrecautionWhenCostsAreHalfBenefits, // this probability between 0.5 and max results
                                    2.0, // when c/b = 2
                                    NDGProgress.NDGInputs.BaseProbabilityLiabilityWithoutPrecautionWhenCostsAreTwiceBenefits, // this probability between min and 0.5 results
                                    NDGProgress.CostBenefitRatio // and this is the actual cost / benefit ratio (costs divided by reduced expected value of injury from taking the precaution)
                                    );
                        }
                    }
                    NDGProgress.PrelitigationWelfareEffectOnP -= NDGProgress.NDGInputs.MagnitudeOfInjury;
                    NDGProgress.BaseDamagesIfPWins = 0 - NDGProgress.PrelitigationWelfareEffectOnP;
                    NDGProgress.DamagesClaim = NDGProgress.NDGInputs.MagnitudeOfInjury * NDGProgress.NDGInputs.DamagesClaimMultiplier;
                    NDGProgress.BaseDamagesIfPWinsAsPctOfClaim = NDGProgress.BaseDamagesIfPWins / NDGProgress.DamagesClaim;
                }
                else
                    NDGProgress.DamagesClaim = 0;
                NDGProgress.SocialLoss = NDGProgress.TakePrecaution ? NDGProgress.NDGInputs.CostOfPrecaution * (1.0 + NDGProgress.NDGInputs.UndetectedCasesForEachDetectedCase) : 0;
                if (injuryOccurs)
                    NDGProgress.SocialLoss += NDGProgress.NDGInputs.MagnitudeOfInjury * (1.0 + NDGProgress.NDGInputs.UndetectedCasesForEachDetectedCase);
            }
            else
                throw new Exception("Internal error. This decision is not defined within the game module.");
        }

        public override double DefaultBehaviorBeforeEvolution(List<double> inputs, int decisionNumber)
        {
            if (NDGProgress.NDGInputs.RandSeedForPreEvolution > 0.5)
                return -1.0;
            else
                return 1.0;
        }

        public override void Score()
        {
            Game.Score((int)Game.CurrentlyEvolvingDecisionIndex, LitigationGame.Defendant.GetSubjectiveUtilityForWealthLevel(LitigationGame.LGP.DFinalWealth));
        }

        public override void CalculateSocialLoss()
        {
            NDGProgress.SocialLoss += LitigationGame.LitigationCostModule.LitigationCostProgress.PTotalExpenses + LitigationGame.LitigationCostModule.LitigationCostProgress.DTotalExpenses;
        }

        public override void CreateInstanceAndInitializeProgress(Game theGame, List<Strategy> theStrategies, object gameModuleSettings, out GameModule theGameModule, out GameModuleProgress theGameModuleProgress)
        {
            NegligenceDisputeGeneratorModule copy = new NegligenceDisputeGeneratorModule();
            copy.SetGameAndStrategies(theGame, theStrategies, GameModuleName, GameModuleNamesThisModuleReliesOn, GameModuleNumbersThisModuleReliesOn, GameModuleSettings);
            SetGameModuleFields(copy);
            theGameModuleProgress = NegligenceDisputeGeneratorModuleProgress.GetRecycledOrAllocate();
            theGameModule = copy;
        }

        public virtual object GenerateSetting(string options)
        {
            List<Decision> decisions = new List<Decision>();

            decisions.Add(new Decision() {
                UseOversampling = true,
                Name="TakePrecaution",
                Abbreviation="tp",
                DecisionTypeCode = "D",
                DynamicNumberOfInputs = true,
                InformationSetAbbreviations = new List<string>() {
                    "C", // Cost of precaution
                    "P", // Probability of injury with precaution
                    "M"  // Expected magnitude of injury that would result
                },
                InputNames = new List<string>() {
                    "Cost of precaution",
                    "Probability of injury with precaution",
                    "Magnitude of injury"
                },
                StrategyBounds = new StrategyBounds() {
                    LowerBound = -1.0,
                    UpperBound = 1.0
                },
                Bipolar = true,
                StrategyGraphInfos = new List<StrategyGraphInfo>(),
                HighestIsBest = true,
                AverageInPreviousVersion = true,
                MaxEvolveRepetitions = 99999
            });

            return new NegligenceDisputeGeneratorModule()
            {
                DecisionsCore = decisions,
                GameModuleName = "DisputeGeneratorModule",
                GameModuleNamesThisModuleReliesOn = new List<string>() { },
                UpdateCumulativeDistributionsAfterSingleActionGroup = false /* subsequent cumulative distributions were causing problems on subsequent repetitions */
            };
        }
    }
}
