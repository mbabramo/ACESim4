using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Diagnostics;

namespace ACESim
{
    /// <summary>
    /// A dummy Game subclass to show a basic Game implementation.
    /// </summary>
    public class LitigationGame : ModularGame
    {
        public LitigationGameProgress LGP;
        public LitigationGameInputs LitigationGameInputs { get { return (LitigationGameInputs) GameInputs; } }

        /* Some essential modules and their corresponding inputs are listed here to make it easier for modules to access one another's data (e.g., LitigationGame.LitigationCostModule). Beyond this mechanism, modules can access other modules by specifying the other modules for which information is needed in the module definition. This allows development of mutually interdependent modules that do not need to be specified in the game definition itself. */

        public AdjustmentsModule AdjustmentsModule1;
        public AdjustmentsModule AdjustmentsModule2;
        public DisputeGeneratorModule DisputeGeneratorModule;
        public LitigationCostModule LitigationCostModule;
        public ValueAndErrorForecastingModule BaseProbabilityForecastingModule;
        public ValueAndErrorForecastingModule BaseDamagesForecastingModule;
        public DropOrDefaultModule BeginningDropOrDefaultModule;
        public BargainingAggressivenessOverrideModule BargainingAggressivenessModule1;
        public BargainingAggressivenessOverrideModule BargainingAggressivenessModule2;
        public BargainingModule BargainingModule;
        public DropOrDefaultModule MidDropOrDefaultModule;
        public DropOrDefaultModule EndDropOrDefaultModule;
        public TrialModule TrialModule;
        public ProbabilityPWinsForecastingModule ProbabilityPWinsForecastingModule;

        public AdjustmentsModuleInputs AdjustmentsModuleInputs;
        public DisputeGeneratorInputs DisputeGeneratorInputs;
        public LitigationCostInputs LitigationCostInputs;
        public ValueAndErrorForecastingInputs BaseProbabilityForecastingInputs;
        public ValueAndErrorForecastingInputs BaseDamagesForecastingInputs;
        public BargainingInputs BargainingInputs;
        public TrialInputs TrialInputs;
        public UtilityMaximizer Plaintiff;
        public UtilityMaximizer Defendant;

        public bool DisputeContinues()
        {
            return !DisputeGeneratorModule.DGProgress.DisputeGeneratorInitiated || 
                (
                    DisputeGeneratorModule.DGProgress.DisputeExists && 
                    LGP.DropInfo == null && 
                    !(BargainingModule.BargainingProgress.SettlementExists == true)
                );
        }

        // NOTE: These must match the order of the bargaining modules referred to in LitigationGameDefinitions.
        public enum ModuleNumbers
        {
            DisputeGenerator,
            BaseProbabilityForecasting,
            BaseDamagesForecasting,
            LitigationCost,
            BeginningDropOrDefault,
            Bargaining,
            MidDropOrDefault,
            Adjustments1,
            Adjustments2,
            EndDropOrDefault,
            Trial,
            ProbabilityPWinsForecasting
        }

        public override void PlaySetup(
            List<Strategy> strategies,
            GameProgress progress,
            GameInputs gameInputs,
            StatCollectorArray recordedInputs,
            GameDefinition gameDefinition,
            bool recordReportInfo,
            double weightOfObservation)
        {
            base.PlaySetup(strategies, progress, gameInputs, recordedInputs, gameDefinition, recordReportInfo, weightOfObservation);
            LGP = (LitigationGameProgress)Progress;
            LGP.Inputs = (LitigationGameInputs)gameInputs;
            DisputeGeneratorModule = (DisputeGeneratorModule)GetModule((int)ModuleNumbers.DisputeGenerator);
            LitigationCostModule = (LitigationCostModule)GetModule((int)ModuleNumbers.LitigationCost);
            BaseProbabilityForecastingModule = (ValueAndErrorForecastingModule)GetModule((int)ModuleNumbers.BaseProbabilityForecasting);
            BaseDamagesForecastingModule = (ValueAndErrorForecastingModule)GetModule((int)ModuleNumbers.BaseDamagesForecasting);
            BeginningDropOrDefaultModule = (DropOrDefaultModule)GetModule((int)ModuleNumbers.BeginningDropOrDefault);
            BargainingModule = (BargainingModule)GetModule((int)ModuleNumbers.Bargaining);
            MidDropOrDefaultModule = (DropOrDefaultModule)GetModule((int)ModuleNumbers.MidDropOrDefault);
            AdjustmentsModule1 = (AdjustmentsModule)GetModule((int)ModuleNumbers.Adjustments1);
            AdjustmentsModule2 = (AdjustmentsModule)GetModule((int)ModuleNumbers.Adjustments2);
            EndDropOrDefaultModule = (DropOrDefaultModule)GetModule((int)ModuleNumbers.EndDropOrDefault);
            TrialModule = (TrialModule)GetModule((int)ModuleNumbers.Trial);
            ProbabilityPWinsForecastingModule = (ProbabilityPWinsForecastingModule)GetModule((int)ModuleNumbers.ProbabilityPWinsForecasting);
            DisputeGeneratorInputs = DisputeGeneratorModule.GameModuleInputs as DisputeGeneratorInputs;
            LitigationCostInputs = LitigationCostModule.GameModuleInputs as LitigationCostInputs;
            BaseProbabilityForecastingInputs = BaseProbabilityForecastingModule.GameModuleInputs as ValueAndErrorForecastingInputs;
            BaseDamagesForecastingInputs = BaseDamagesForecastingModule.GameModuleInputs as ValueAndErrorForecastingInputs;
            BargainingInputs = BargainingModule.GameModuleInputs as BargainingInputs;
            TrialInputs = TrialModule.GameModuleInputs as TrialInputs;
            LGP.DisputeGeneratorModuleProgress = (DisputeGeneratorModuleProgress)DisputeGeneratorModule.GameModuleProgress;
            LGP.LitigationCostModuleProgress = (LitigationCostModuleProgress)LitigationCostModule.GameModuleProgress;
            LGP.BaseProbabilityForecastingModuleProgress = (ValueAndErrorForecastingModuleProgress)BaseProbabilityForecastingModule.GameModuleProgress;
            LGP.BaseDamagesForecastingModuleProgress = (ValueAndErrorForecastingModuleProgress)BaseDamagesForecastingModule.GameModuleProgress;
            LGP.BeginningDropOrDefaultModuleProgress = (DropOrDefaultModuleProgress)BeginningDropOrDefaultModule.GameModuleProgress;
            LGP.BargainingModuleProgress = (BargainingModuleProgress)BargainingModule.GameModuleProgress;
            LGP.MidDropOrDefaultModuleProgress = (DropOrDefaultModuleProgress)MidDropOrDefaultModule.GameModuleProgress;
            LGP.EndDropOrDefaultModuleProgress = (DropOrDefaultModuleProgress)EndDropOrDefaultModule.GameModuleProgress;
            LGP.TrialModuleProgress = (TrialModuleProgress)TrialModule.GameModuleProgress;
            Plaintiff = LGP.InputsPlaintiff;
            Defendant = LGP.InputsDefendant;
        }



    }
}
