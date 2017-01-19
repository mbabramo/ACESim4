using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.ComponentModel.Composition;

namespace ACESim
{
    [Export(typeof(ICodeBasedSettingGenerator))] // include the export and exportmetadata attributes if we implement GenerateSetting, setting up the GameModule by code instead of in the settings file.
    [ExportMetadata("CodeGeneratorName", "PerfectEstimatesModule")]
    [Serializable]
    public class PerfectEstimatesModule : ValueAndErrorForecastingModule, ICodeBasedSettingGenerator
    {
        public PerfectEstimatesModuleProgress PerfectEstimatesModuleProgress { get { return (PerfectEstimatesModuleProgress)GameModuleProgress; } set { GameModuleProgress = value; } }


        public override void PostForecasting()
        {
            // Because there are no decisions, we must call SetForecasts from here.
            if (ForecastingProgress.IsProbabilityNotDamages)
                SetForecasts(LitigationGame.DisputeGeneratorModule.DGProgress.EvidentiaryStrengthLiability); // we use evidentiary strength rather than probability itself as the basis, so that noise will apply to evidentiary strength rather than to ultimate probability
            else
                SetForecasts(LitigationGame.DisputeGeneratorModule.DGProgress.BaseDamagesIfPWinsAsPctOfClaim);
        }

        public override void SetForecasts(double actualPResult)
        {
            PerfectEstimatesInputs perfectEstimatesInputs = (PerfectEstimatesInputs)GameModuleInputs;
            PerfectEstimatesModuleProgress.ActualPIssueStrength = actualPResult;
            if (ForecastingProgress.IsProbabilityNotDamages)
            {
                bool assumeSymmetry = false; // the effect of this option is to eliminate any slight asymmetries that might cause different bargaining behavior.
                if (assumeSymmetry)
                {
                    if (actualPResult >= 0.5)
                        PerfectEstimatesModuleProgress.ActualPResultTransformed = LitigationGame.ProbabilityPWinsForecastingModule.CalculateProbabilityPWins(actualPResult);
                    else
                        PerfectEstimatesModuleProgress.ActualPResultTransformed = 1.0 - LitigationGame.ProbabilityPWinsForecastingModule.CalculateProbabilityPWins(1.0 - actualPResult);
                }
                else
                    PerfectEstimatesModuleProgress.ActualPResultTransformed = LitigationGame.ProbabilityPWinsForecastingModule.CalculateProbabilityPWins(actualPResult);
            }
            else
                PerfectEstimatesModuleProgress.ActualPResultTransformed = actualPResult;
            PerfectEstimatesModuleProgress.PEstimatePResult = PerfectEstimatesModuleProgress.ActualPResultTransformed;
            PerfectEstimatesModuleProgress.DEstimatePResult = PerfectEstimatesModuleProgress.ActualPResultTransformed;
            PerfectEstimatesModuleProgress.PEstimateDResult = PerfectEstimatesModuleProgress.IsProbabilityNotDamages ? 1.0 - actualPResult : 0.0 - actualPResult;
            PerfectEstimatesModuleProgress.DEstimateDResult = PerfectEstimatesModuleProgress.IsProbabilityNotDamages ? 1.0 - actualPResult : 0.0 - actualPResult;
            PerfectEstimatesModuleProgress.PEstimatePError = 0;
            //PerfectEstimatesModuleProgress.PEstimateDError = 0;
            //PerfectEstimatesModuleProgress.DEstimatePError = 0;
            PerfectEstimatesModuleProgress.DEstimateDError = 0;
            PerfectEstimatesModuleProgress.CurrentEquivalentPNoiseLevel = 0;
            PerfectEstimatesModuleProgress.CurrentEquivalentDNoiseLevel = 0;
        }

        public override void GetForecastFromProxy(double proxy, double noiseLevel, PartyOfForecast partyOfForecast, out double valueEstimate, out double errorEstimate)
        {
            valueEstimate = proxy;
            errorEstimate = 0.0;
        }


        public override double GetInitialProxyForWhetherPartyExpectsToWin(PartyOfForecast partyOfForecast)
        {
            // no proxies, so return actual values
            if (partyOfForecast == PartyOfForecast.Plaintiff)
                return (double)PerfectEstimatesModuleProgress.PEstimatePResult;
            else
                return (double)PerfectEstimatesModuleProgress.DEstimateDResult;
        }

        public override double GetEstimateOfOtherError(double ownEstimate, double ownAbsoluteError, double otherNoiseLevel)
        {
            return 0.0; // note that as currently designed, if one player is using perfect estimates, other is as well.
        }

        public override void CreateInstanceAndInitializeProgress(Game theGame, List<Strategy> theStrategies, object gameModuleSettings, out GameModule theGameModule, out GameModuleProgress theGameModuleProgress)
        {
            PerfectEstimatesModule copy = new PerfectEstimatesModule();
            copy.SetGameAndStrategies(theGame, theStrategies, GameModuleName, GameModuleNamesThisModuleReliesOn, GameModuleNumbersThisModuleReliesOn, GameModuleSettings);
            SetGameModuleFields(copy);
            theGameModuleProgress = PerfectEstimatesModuleProgress.GetRecycledOrAllocate();
            theGameModule = copy;
        }
        

        public override object GenerateSetting(string options)
        {

            List<Decision> decisions = new List<Decision>();

            return new PerfectEstimatesModule()
            {
                DecisionsCore = decisions,
                ActionsAtBeginningOfModule = new List<string>() { "BeforeForecasting" },
                ActionsAtEndOfModule = new List<string>() { "AfterForecasting" },
                GameModuleName = options.Contains("Probability") ? "ProbabilityForecastingModule" : "DamagesForecastingModule",
                GameModuleNamesThisModuleReliesOn = new List<string>() { }
            };
        }

    }
}
