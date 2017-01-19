using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.ComponentModel.Composition;

namespace ACESim
{
    [Export(typeof(ICodeBasedSettingGenerator))] // include the export and exportmetadata attributes if we implement GenerateSetting, setting up the GameModule by code instead of in the settings file.
    [ExportMetadata("CodeGeneratorName", "ForbiddenActDisputeGeneratorModule")]
    [Serializable]
    public class ForbiddenActDisputeGeneratorModule : DisputeGeneratorModule, ICodeBasedSettingGenerator
    {
        public ForbiddenActDisputeGeneratorModuleProgress ForbiddenActDisputeGeneratorProgress { get { return (ForbiddenActDisputeGeneratorModuleProgress)GameModuleProgress; } set { GameModuleProgress = value; } }

        public override void Process(DisputeGeneratorInputs moduleInputs)
        {
            if (Game.CurrentDecisionIndexWithinActionGroup == 0 && Game.PreparationPhase)
            {
                ForbiddenActDisputeGeneratorProgress.ForbiddenActInputs = (ForbiddenActDisputeGeneratorInputs)moduleInputs;
                ForbiddenActDisputeGeneratorProgress.EvidentiaryStrengthIfDidNotDoIt = ConstrainToRange.Constrain(ForbiddenActDisputeGeneratorProgress.ForbiddenActInputs.UntruncatedEvidentiaryStrengthDidNotDoIt, 0.0, 1.0);
                ForbiddenActDisputeGeneratorProgress.EvidentiaryStrengthIfDidIt = ConstrainToRange.Constrain(ForbiddenActDisputeGeneratorProgress.EvidentiaryStrengthIfDidNotDoIt + ForbiddenActDisputeGeneratorProgress.ForbiddenActInputs.AdditiveEvidentiaryStrengthDidIt, 0.0, 1.0);
                ForbiddenActDisputeGeneratorProgress.NetSocialCost = ForbiddenActDisputeGeneratorProgress.ForbiddenActInputs.SocialCostOfAct - ForbiddenActDisputeGeneratorProgress.ForbiddenActInputs.PrivateBenefitOfAct;
                List<double> inputs = new List<double>() { ForbiddenActDisputeGeneratorProgress.ForbiddenActInputs.PrivateBenefitOfAct, ForbiddenActDisputeGeneratorProgress.ForbiddenActInputs.SocialCostOfAct };
                SpecifyInputs(inputs);
            }
            else if (Game.CurrentDecisionIndexWithinActionGroup == 0 && !Game.PreparationPhase)
            {
                double calculationResult = Calculate();
                ForbiddenActDisputeGeneratorProgress.DoesAct = calculationResult > 0.0;
                if (ForbiddenActDisputeGeneratorProgress.DoesAct)
                {
                    ForbiddenActDisputeGeneratorProgress.PrelitigationWelfareEffectOnD += ForbiddenActDisputeGeneratorProgress.ForbiddenActInputs.PrivateBenefitOfAct;
                    ForbiddenActDisputeGeneratorProgress.EvidentiaryStrengthLiability = ForbiddenActDisputeGeneratorProgress.EvidentiaryStrengthIfDidIt;
                    ForbiddenActDisputeGeneratorProgress.PShouldWin = true;
                    ForbiddenActDisputeGeneratorProgress.SocialLoss = ForbiddenActDisputeGeneratorProgress.ForbiddenActInputs.SocialCostOfAct - ForbiddenActDisputeGeneratorProgress.ForbiddenActInputs.PrivateBenefitOfAct;
                }
                else
                {
                    ForbiddenActDisputeGeneratorProgress.EvidentiaryStrengthLiability = ForbiddenActDisputeGeneratorProgress.EvidentiaryStrengthIfDidNotDoIt;
                    ForbiddenActDisputeGeneratorProgress.PShouldWin = false;
                    ForbiddenActDisputeGeneratorProgress.SocialLoss = 0;
                }
                ForbiddenActDisputeGeneratorProgress.BaseProbabilityPWins = 
                    ForbiddenActDisputeGeneratorProgress.EvidentiaryStrengthLiability = ForbiddenActDisputeGeneratorProgress.EvidentiaryStrengthLiability; // matters only if we are using exogenous probabilities

                ForbiddenActDisputeGeneratorProgress.PrelitigationWelfareEffectOnP = 0;
                if (ForbiddenActDisputeGeneratorProgress.ForbiddenActInputs.DamagesEqualPrivateBenefit)
                    ForbiddenActDisputeGeneratorProgress.BaseDamagesIfPWins = ForbiddenActDisputeGeneratorProgress.ForbiddenActInputs.PrivateBenefitOfAct;
                else if (ForbiddenActDisputeGeneratorProgress.ForbiddenActInputs.DamagesEqualSocialCost)
                    ForbiddenActDisputeGeneratorProgress.BaseDamagesIfPWins = ForbiddenActDisputeGeneratorProgress.ForbiddenActInputs.SocialCostOfAct;
                else
                    ForbiddenActDisputeGeneratorProgress.BaseDamagesIfPWins = Math.Max(ForbiddenActDisputeGeneratorProgress.ForbiddenActInputs.SocialCostOfAct, ForbiddenActDisputeGeneratorProgress.ForbiddenActInputs.PrivateBenefitOfAct); // higher of damages, disgorgement
                ForbiddenActDisputeGeneratorProgress.DamagesClaim = ForbiddenActDisputeGeneratorProgress.BaseDamagesIfPWins;
                ForbiddenActDisputeGeneratorProgress.BaseDamagesIfPWinsAsPctOfClaim = ForbiddenActDisputeGeneratorProgress.BaseDamagesIfPWins / ForbiddenActDisputeGeneratorProgress.DamagesClaim;
                ForbiddenActDisputeGeneratorProgress.DisputeExists = true;
            }
            else
                throw new Exception("Internal error. This decision is not defined within the game module.");
        }

        public override double DefaultBehaviorBeforeEvolution(List<double> inputs, int decisionNumber)
        {
            if (ForbiddenActDisputeGeneratorProgress.ForbiddenActInputs.RandSeedForPreEvolution > 0.5)
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
            ForbiddenActDisputeGeneratorProgress.SocialLoss += LitigationGame.LitigationCostModule.LitigationCostProgress.PTotalExpenses + LitigationGame.LitigationCostModule.LitigationCostProgress.DTotalExpenses;
        }

        public override void CreateInstanceAndInitializeProgress(Game theGame, List<Strategy> theStrategies, object gameModuleSettings, out GameModule theGameModule, out GameModuleProgress theGameModuleProgress)
        {
            ForbiddenActDisputeGeneratorModule copy = new ForbiddenActDisputeGeneratorModule();
            copy.SetGameAndStrategies(theGame, theStrategies, GameModuleName, GameModuleNamesThisModuleReliesOn, GameModuleNumbersThisModuleReliesOn, GameModuleSettings);
            SetGameModuleFields(copy);
            theGameModuleProgress = ForbiddenActDisputeGeneratorModuleProgress.GetRecycledOrAllocate();
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
                InputAbbreviations = new List<string>() {
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

            return new ForbiddenActDisputeGeneratorModule()
            {
                DecisionsCore = decisions,
                GameModuleName = "DisputeGeneratorModule",
                GameModuleNamesThisModuleReliesOn = new List<string>() { },
                UpdateCumulativeDistributionsAfterSingleActionGroup = false /* subsequent cumulative distributions were causing problems on subsequent repetitions */
            };
        }
    }
}
