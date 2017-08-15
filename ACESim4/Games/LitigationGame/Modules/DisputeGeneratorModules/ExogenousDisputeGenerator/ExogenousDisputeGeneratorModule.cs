using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.ComponentModel.Composition;

namespace ACESim
{
    [Export(typeof(ICodeBasedSettingGenerator))] // include the export and exportmetadata attributes if we implement GenerateSetting, setting up the GameModule by code instead of in the settings file.
    [ExportMetadata("CodeGeneratorName", "ExogenousDisputeGeneratorModule")]
    [Serializable]
    public class ExogenousDisputeGeneratorModule : DisputeGeneratorModule, ICodeBasedSettingGenerator
    {

        public ExogenousDisputeGeneratorModuleProgress ExogenousProgress { get { return (ExogenousDisputeGeneratorModuleProgress)GameModuleProgress; } set { GameModuleProgress = value; } }

        public override void Process(DisputeGeneratorInputs moduleInputs)
        {

            ExogenousProgress.ExogenousInputs = (ExogenousDisputeGeneratorInputs)moduleInputs;
            ExogenousProgress.DamagesClaim = ExogenousProgress.ExogenousInputs.DamagesClaim;
            ExogenousProgress.BaseDamagesIfPWins = ExogenousProgress.ExogenousInputs.Damages;
            ExogenousProgress.BaseDamagesIfPWinsAsPctOfClaim = ExogenousProgress.BaseDamagesIfPWins / ExogenousProgress.DamagesClaim; 
            ExogenousProgress.BaseProbabilityPWins = ExogenousProgress.EvidentiaryStrengthLiability = ExogenousProgress.ExogenousInputs.EvidentiaryStrengthLiability;

            // curve distribution from (0.5, 0.5) through (0.75, ProbabilityCorrectGivenThreeQuartersAgreement [t]) to (1.0, ProbabilityCorrectGivenUnanimity [u])
            // First, determine curvature. 0.5 + (u - 0.5) * 0.5^(1/c) = t. So, 0.5^(1/c) = (t - 0.5)/(u - 0.5), and 1/c ln 0.5 = ln ((t - 0.5)/(u - 0.5)). Thus c = ln 0.5 / ln ((t - 0.5)/(u - 0.5)). 
            // Given c, we now take bp and calculate: 0.5 + (u - 0.5) * ((bp - 0.5)/0.5)^1/c. 

            double curvature = CurveDistribution.CalculateCurvature(0.5, ExogenousProgress.ExogenousInputs.ProbabilityCorrectGivenUnanimity, ExogenousProgress.ExogenousInputs.ProbabilityCorrectGivenThreeQuartersAgreement); // Math.Log(0.5) / (Math.Log((ExogenousProgress.ExogenousInputs.ProbabilityCorrectGivenThreeQuartersAgreement - 0.5)/(ExogenousProgress.ExogenousInputs.ProbabilityCorrectGivenUnanimity - 0.5)));
            double input = Math.Abs(ExogenousProgress.BaseProbabilityPWins - 0.5) * 2.0;
            double probabilityResultIsCorrect = MonotonicCurve.CalculateValueBasedOnProportionOfWayBetweenValues(0.5, ExogenousProgress.ExogenousInputs.ProbabilityCorrectGivenUnanimity, curvature, input);
            if (ExogenousProgress.BaseProbabilityPWins >= 0.5)
                ExogenousProgress.PShouldWin = ExogenousProgress.ExogenousInputs.RandSeedForDeterminingCorrectness < probabilityResultIsCorrect;
            else
                ExogenousProgress.PShouldWin = ExogenousProgress.ExogenousInputs.RandSeedForDeterminingCorrectness < (1 - probabilityResultIsCorrect);

            ExogenousProgress.AdjustedErrorWeight = HyperbolicTangentCurve.GetYValue(0, 0, 1.0, 0.25, 0.80, Math.Abs(ExogenousProgress.BaseProbabilityPWins - 0.5));

            ExogenousProgress.DisputeExists = true;
            ExogenousProgress.PrelitigationWelfareEffectOnD = 0;
            ExogenousProgress.PrelitigationWelfareEffectOnP = 0;
        }

        public override void CalculateSocialLoss()
        {
            double pDeltaWealth = LitigationGame.LGP.PFinalWealth - LitigationGame.Plaintiff.InitialWealth;
            double dDeltaWealth = LitigationGame.LGP.DFinalWealth - LitigationGame.Defendant.InitialWealth;
            double pOptimalDeltaWealth;
            double dOptimalDeltaWealth;
            if (ExogenousProgress.PShouldWin)
            {
                pOptimalDeltaWealth = ExogenousProgress.ExogenousInputs.Damages;
                dOptimalDeltaWealth = 0 - ExogenousProgress.ExogenousInputs.Damages;
            }
            else
            {
                pOptimalDeltaWealth = 0;
                dOptimalDeltaWealth = 0;
            }
            double pError = Math.Abs(pDeltaWealth - pOptimalDeltaWealth);
            double dError = Math.Abs(dDeltaWealth - dOptimalDeltaWealth);
            double pErrorWeight = ExogenousProgress.ExogenousInputs.SocialLossWeightOnPCompensationInaccuracy;
            double dErrorWeight = ExogenousProgress.ExogenousInputs.SocialLossWeightOnDDeterrenceInaccuracy;
            double lossFromLitigation = LitigationGame.LitigationCostModule.LitigationCostProgress.PTotalExpenses + LitigationGame.LitigationCostModule.LitigationCostProgress.DTotalExpenses; // always receives weight of 1.0 since this is pure deadweight
            ExogenousProgress.SocialLoss = lossFromLitigation + pErrorWeight * pError + dErrorWeight * dError;
        }

        public override void Score()
        {
            // nothing to score
        }

        public override void CreateInstanceAndInitializeProgress(Game theGame, List<Strategy> theStrategies, object gameModuleSettings, out GameModule theGameModule, out GameModuleProgress theGameModuleProgress)
        {
            ExogenousDisputeGeneratorModule copy = new ExogenousDisputeGeneratorModule();
            copy.SetGameAndStrategies(theGame, theStrategies, GameModuleName, GameModuleNamesThisModuleReliesOn, GameModuleNumbersThisModuleReliesOn, GameModuleSettings);
            SetGameModuleFields(copy);
            theGameModuleProgress = ExogenousDisputeGeneratorModuleProgress.GetRecycledOrAllocate();
            theGameModule = copy;
        }

        public virtual object GenerateSetting(string options)
        {
            List<Decision> decisions = new List<Decision>();

            return new ExogenousDisputeGeneratorModule()
            {
                DecisionsCore = decisions,
                GameModuleName = "DisputeGeneratorModule",
                GameModuleNamesThisModuleReliesOn = new List<string>() { },
                ActionsAtBeginningOfModule = new List<string>() { "ExogenousProcessing" },
                UpdateCumulativeDistributionsAfterSingleActionGroup = true
            };
        }
    }
}
