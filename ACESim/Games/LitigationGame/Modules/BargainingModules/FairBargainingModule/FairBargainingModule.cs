using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.ComponentModel.Composition;
using System.Threading;

namespace ACESim
{
    [Export(typeof(ICodeBasedSettingGenerator))] // include the export and exportmetadata attributes if we implement GenerateSetting, setting up the GameModule by code instead of in the settings file.
    [ExportMetadata("CodeGeneratorName", "FairBargainingModule")]
    [Serializable]
    public class FairBargainingModule : BargainingModule, ICodeBasedSettingGenerator
    {

        public FairBargainingModuleProgress FairBargainingProgress { get { return (FairBargainingModuleProgress)GameModuleProgress; } set { GameModuleProgress = value; } }

        public FairBargainingInputs FairBargainingInputs { get { return (FairBargainingInputs)GameModuleInputs; } }

        public enum FairBargainingDecisions
        { // Note: We can switch order of plaintiffdecision and defendantdecision here.
        }

        public override void ExecuteModule()
        {

            if (LitigationGame.DisputeContinues()) // continue only if there is a dispute and a settlement either does not exist or the settlement variable has not yet been set
            {
                // In the bargaining phase, each party takes into account its estimate of the base probability, its uncertainty, its opponent's noise level, and both sides' litigation costs. Note that this can be repeated, so before executing it, we see if we have already reached a settlement.
                if (Game.CurrentActionPointName == "BeforeBargaining")
                {
                    SharedPrebargainingSetup();
                    MakeDecisionBasedOnBargainingInputs();
                    if (BargainingProgress.SettlementExists == false)
                        LitigationGame.LitigationCostModule.RegisterSettlementFailure();
                }
            }
        }

        public override void MakeDecisionBasedOnBargainingInputs()
        {
            double probLiability;
            if (LitigationGame.BaseProbabilityForecastingModule is PerfectEstimatesModule)
                probLiability = LitigationGame.DisputeGeneratorModule.DGProgress.BaseProbabilityPWins;
            else
            {
                CumulativeDistribution liabilityDist = LitigationGame.MostRecentCumulativeDistributions[0];
                ValueFromSignalEstimator pLiability = (LitigationGame.BaseProbabilityForecastingModule as IndependentEstimatesModule).GetValueFromSignalEstimator(ValueAndErrorForecastingModule.PartyOfForecast.Plaintiff);
                ValueFromSignalEstimator dLiability = (LitigationGame.BaseProbabilityForecastingModule as IndependentEstimatesModule).GetValueFromSignalEstimator(ValueAndErrorForecastingModule.PartyOfForecast.Defendant);
                ValueFromSignalEstimator jointLiability = new ValueFromSignalEstimator(liabilityDist);
                foreach (var signal in pLiability.Signals)
                    jointLiability.AddSignal(signal);
                foreach (var signal in dLiability.Signals)
                    jointLiability.AddSignal(signal);
                jointLiability.UpdateSummaryStatistics();
                probLiability = jointLiability.ExpectedValueOrProbability(true);
            }

            double magnitudeDamages;
            if (LitigationGame.BaseDamagesForecastingModule is PerfectEstimatesModule)
                magnitudeDamages = LitigationGame.DisputeGeneratorModule.DGProgress.BaseDamagesIfPWinsAsPctOfClaim;
            else
            {
                CumulativeDistribution damagesDist = LitigationGame.MostRecentCumulativeDistributions[1];
                ValueFromSignalEstimator pDamages = (LitigationGame.BaseDamagesForecastingModule as IndependentEstimatesModule).GetValueFromSignalEstimator(ValueAndErrorForecastingModule.PartyOfForecast.Plaintiff);
                ValueFromSignalEstimator dDamages = (LitigationGame.BaseDamagesForecastingModule as IndependentEstimatesModule).GetValueFromSignalEstimator(ValueAndErrorForecastingModule.PartyOfForecast.Defendant);
                ValueFromSignalEstimator jointDamages = new ValueFromSignalEstimator(damagesDist);
                foreach (var signal in pDamages.Signals)
                    jointDamages.AddSignal(signal);
                foreach (var signal in dDamages.Signals)
                    jointDamages.AddSignal(signal);
                jointDamages.UpdateSummaryStatistics();
                magnitudeDamages = jointDamages.ExpectedValueOrProbability(false);
            }

            double fairSettlement = probLiability * magnitudeDamages;
            FairBargainingProgress.SettlementProgress = new GlobalSettlementProgress() { AgreedUponPaymentAsProportion = ConstrainToRange.Constrain(fairSettlement, 0.0, 1.0), GlobalSettlementAchieved = true, OriginalDamagesClaim = LitigationGame.DisputeGeneratorModule.DGProgress.DamagesClaim };
        }

        public override double DefaultBehaviorBeforeEvolution(List<double> inputs, int decisionNumber)
        {
            
            throw new Exception("Unknown decision.");
        }

        public override void Score()
        {
            // Note that we are projecting and calculating the eventual outcomes as the delta in wealth divided by the damages amount. We will have to change this to the damages claim once there is disagreement about damages.
        }

        public override void CreateInstanceAndInitializeProgress(Game theGame, List<Strategy> theStrategies, object gameModuleSettings, out GameModule theGameModule, out GameModuleProgress theGameModuleProgress)
        {
            FairBargainingModule copy = new FairBargainingModule();
            copy.SetGameAndStrategies(theGame, theStrategies, GameModuleName, GameModuleNamesThisModuleReliesOn, GameModuleNumbersThisModuleReliesOn, GameModuleSettings);
            SetGameModuleFields(copy); 
            theGameModuleProgress = FairBargainingModuleProgress.GetRecycledOrAllocate();
            theGameModule = copy;
        }
        

        public virtual object GenerateSetting(string options)
        {
            List<Decision> decisions = new List<Decision>();


            return new FairBargainingModule()
            {
                DecisionsCore = decisions,
                ActionsAtBeginningOfModule = new List<string>() { "BeforeBargaining" },
                ActionsAtEndOfModule = new List<string>() { "AfterBargaining" },
                GameModuleName = "BargainingModule",
                GameModuleNamesThisModuleReliesOn = new List<string>() { },
                UpdateCumulativeDistributionsAfterSingleActionGroup = false, /* currently updating only after dispute generation */
                Tags = new List<string>() { "Bargaining subclaim", "Bargaining round" }
            };
        }

        

        
    }
}
