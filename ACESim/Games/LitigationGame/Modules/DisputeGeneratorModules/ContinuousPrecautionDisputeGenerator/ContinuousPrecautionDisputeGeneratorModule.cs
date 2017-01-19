using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.ComponentModel.Composition;

namespace ACESim
{
    [Export(typeof(ICodeBasedSettingGenerator))] // include the export and exportmetadata attributes if we implement GenerateSetting, setting up the GameModule by code instead of in the settings file.
    [ExportMetadata("CodeGeneratorName", "ContinuousPrecautionDisputeGeneratorModule")]
    [Serializable]
    public class ContinuousPrecautionDisputeGeneratorModule : DisputeGeneratorModule, ICodeBasedSettingGenerator
    {

        public ContinuousPrecautionDisputeGeneratorModuleProgress CPDGProgress { get { return (ContinuousPrecautionDisputeGeneratorModuleProgress)GameModuleProgress; } set { GameModuleProgress = value; } }

        public override void Process(DisputeGeneratorInputs moduleInputs)
        {
            if (Game.CurrentDecisionIndexWithinActionGroup == 0 && Game.PreparationPhase)
            {
                CPDGProgress.CPDGInputs = (ContinuousPrecautionDisputeGeneratorInputs)moduleInputs;
                List<double> inputs = new List<double>() { CPDGProgress.CPDGInputs.PrecautionLevelThatProducesHalfOfGainsOfInfinitePrecaution, CPDGProgress.CPDGInputs.MagnitudeOfInjury };
                SpecifyInputs(inputs);
            }
            else if (Game.CurrentDecisionIndexWithinActionGroup == 0 && !Game.PreparationPhase)
            {
                CPDGProgress.LevelOfPrecaution = Calculate();
                CPDGProgress.ProbabilityOfInjury = HyperbolicTangentCurve.GetYValue(0, CPDGProgress.CPDGInputs.ProbabilityInjuryWithZeroPrecaution, CPDGProgress.CPDGInputs.ProbabilityInjuryWithInfinitePrecaution, CPDGProgress.CPDGInputs.PrecautionLevelThatProducesHalfOfGainsOfInfinitePrecaution, (CPDGProgress.CPDGInputs.ProbabilityInjuryWithZeroPrecaution + CPDGProgress.CPDGInputs.ProbabilityInjuryWithInfinitePrecaution) / 2.0, CPDGProgress.LevelOfPrecaution);
                CPDGProgress.PrelitigationWelfareEffectOnD -= CPDGProgress.LevelOfPrecaution * (1.0 + CPDGProgress.CPDGInputs.UndetectedCasesForEachDetectedCase);

                // Figure out marginal cost-benefit ratio
                const double precautionIncrementForMeasuringMargins = 0.01;
                double probabilityWithSlightlyHigherLevelOfPrecaution = HyperbolicTangentCurve.GetYValue(0, CPDGProgress.CPDGInputs.ProbabilityInjuryWithZeroPrecaution, CPDGProgress.CPDGInputs.ProbabilityInjuryWithInfinitePrecaution, CPDGProgress.CPDGInputs.PrecautionLevelThatProducesHalfOfGainsOfInfinitePrecaution, (CPDGProgress.CPDGInputs.ProbabilityInjuryWithZeroPrecaution + CPDGProgress.CPDGInputs.ProbabilityInjuryWithInfinitePrecaution) / 2.0, CPDGProgress.LevelOfPrecaution + precautionIncrementForMeasuringMargins);
                double marginalBenefit = (CPDGProgress.ProbabilityOfInjury - probabilityWithSlightlyHigherLevelOfPrecaution) * CPDGProgress.CPDGInputs.MagnitudeOfInjury;
                CPDGProgress.MarginalCostMarginalBenefitRatio = precautionIncrementForMeasuringMargins / marginalBenefit;

                if (CPDGProgress.MarginalCostMarginalBenefitRatio <= 1.0)
                    CPDGProgress.EvidentiaryStrengthLiability = 1.0 - 0.5 * CPDGProgress.MarginalCostMarginalBenefitRatio; // 0 cost/benefit ratio means strongest possible plaintiff's case, and thus evidentiary strength of 1.0; 1 cost/benefit ratio is a case right in the middle, and thus 0.5
                else
                    CPDGProgress.EvidentiaryStrengthLiability = 0.5 - 0.5 * (1.0 - 1.0 / CPDGProgress.MarginalCostMarginalBenefitRatio); // as cost benefit ratio becomes high, inverse becomes low, and 1.0 - inverse becomes close to 1.0, so at extreme high end of cost-benefit ratio, meaning that costs are prohibitive and precautions should not be undertaken, we have evidentiary strength of 0.0; when c/b = 1.0, then we have case right in the middle, and thus 0.5

                bool injuryOccurs = CPDGProgress.ProbabilityOfInjury > CPDGProgress.CPDGInputs.ProbabilityInjuryRandomSeed;

                CPDGProgress.DisputeExists = injuryOccurs;
                if (CPDGProgress.DisputeExists)
                {
                    CPDGProgress.PShouldWin = CPDGProgress.MarginalCostMarginalBenefitRatio < 1.0;
                   
                    CPDGProgress.BaseProbabilityPWins = 
                        HyperbolicTangentCurve.GetYValueTwoSided(
                            1.0, // critical point occurs when c/b = 1 ...
                            0.5, // ... jury is likely as not to find liability
                            CPDGProgress.CPDGInputs.BaseProbabilityLiabilityMaximum, // probability of liability when costs are tiny compared to benefits of precaution
                            CPDGProgress.CPDGInputs.BaseProbabilityLiabilityMinimum, // probability of liability when benefits are tiny compared to costs
                            0.5, // when c/b = 1/2
                            CPDGProgress.CPDGInputs.BaseProbabilityLiabilityWhenMarginalCostsAreHalfMarginalBenefits, // this probability between 0.5 and max results
                            2.0, // when c/b = 2
                            CPDGProgress.CPDGInputs.BaseProbabilityLiabilityWhenMarginalCostsAreTwiceMarginalBenefits, // this probability between min and 0.5 results
                            CPDGProgress.MarginalCostMarginalBenefitRatio // and this is the actual marginal cost / marginal benefit ratio 
                            );
                    CPDGProgress.PrelitigationWelfareEffectOnP -= CPDGProgress.CPDGInputs.MagnitudeOfInjury;
                    CPDGProgress.BaseDamagesIfPWins = 0 - CPDGProgress.PrelitigationWelfareEffectOnP;
                    CPDGProgress.DamagesClaim = CPDGProgress.CPDGInputs.MagnitudeOfInjury * CPDGProgress.CPDGInputs.DamagesClaimMultiplier;
                    CPDGProgress.BaseDamagesIfPWinsAsPctOfClaim = CPDGProgress.BaseDamagesIfPWins / CPDGProgress.DamagesClaim;
                }
                else
                    CPDGProgress.DamagesClaim = 0;

                CPDGProgress.SocialLoss = CPDGProgress.LevelOfPrecaution * (1.0 + CPDGProgress.CPDGInputs.UndetectedCasesForEachDetectedCase);
                if (injuryOccurs)
                    CPDGProgress.SocialLoss += CPDGProgress.CPDGInputs.MagnitudeOfInjury * (1.0 + CPDGProgress.CPDGInputs.UndetectedCasesForEachDetectedCase);
            }
            else
                throw new Exception("Internal error. This decision is not defined within the game module.");
        }

        public override double DefaultBehaviorBeforeEvolution(List<double> inputs, int decisionNumber)
        {
            return  0 + CPDGProgress.CPDGInputs.RandSeedForPreEvolution * 100.0;
        }

        public override void Score()
        {
            Game.Score((int)Game.CurrentlyEvolvingDecisionIndex, LitigationGame.Defendant.GetSubjectiveUtilityForWealthLevel(LitigationGame.LGP.DFinalWealth));
        }

        public override void CalculateSocialLoss()
        {
            CPDGProgress.SocialLoss += LitigationGame.LitigationCostModule.LitigationCostProgress.PTotalExpenses + LitigationGame.LitigationCostModule.LitigationCostProgress.DTotalExpenses;
        }

        public override void CreateInstanceAndInitializeProgress(Game theGame, List<Strategy> theStrategies, object gameModuleSettings, out GameModule theGameModule, out GameModuleProgress theGameModuleProgress)
        {
            ContinuousPrecautionDisputeGeneratorModule copy = new ContinuousPrecautionDisputeGeneratorModule();
            copy.SetGameAndStrategies(theGame, theStrategies, GameModuleName, GameModuleNamesThisModuleReliesOn, GameModuleNumbersThisModuleReliesOn, GameModuleSettings);
            SetGameModuleFields(copy);
            theGameModuleProgress = ContinuousPrecautionDisputeGeneratorModuleProgress.GetRecycledOrAllocate();
            theGameModule = copy;
        }

        public virtual object GenerateSetting(string options)
        {
            List<Decision> decisions = new List<Decision>();

            decisions.Add(new Decision() {
                UseOversampling = true, 
                Name="PrecautionLevel",
                Abbreviation="pl",
                DecisionTypeCode = "D",
                DynamicNumberOfInputs = true,
                StrategyBounds = new StrategyBounds() {
                    LowerBound = 0,
                    UpperBound = 10000
                },
                Bipolar = false,
                StrategyGraphInfos = new List<StrategyGraphInfo>(),
                AverageInPreviousVersion = true,
                HighestIsBest = true,
                MaxEvolveRepetitions = 99999
            });

            return new ContinuousPrecautionDisputeGeneratorModule()
            {
                DecisionsCore = decisions,
                GameModuleName = "DisputeGeneratorModule",
                GameModuleNamesThisModuleReliesOn = new List<string>() { },
                UpdateCumulativeDistributionsAfterSingleActionGroup = false /* subsequent cumulative distributions were causing problems on subsequent repetitions */
            };
        }
    }
}
