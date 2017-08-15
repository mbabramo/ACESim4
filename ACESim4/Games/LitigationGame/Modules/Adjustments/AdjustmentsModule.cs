using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ACESim
{
    [Serializable]
    public class AdjustmentsModule : GameModule
    {
        public AdjustmentsModuleProgress AdjustmentsProgress { get { return (AdjustmentsModuleProgress)GameModuleProgress; } set { GameModuleProgress = value; } }

        public override void CreateInstanceAndInitializeProgress(Game theGame, List<Strategy> theStrategies, object gameModuleSettings, out GameModule theGameModule, out GameModuleProgress theGameModuleProgress)
        {
            throw new Exception("This must be overridden. The overridden method should call SetGameAndStrategies.");
        }

        public override void ExecuteModule()
        { // by default, do nothing
        }

        public virtual List<double> GetStatusValues()
        {
            return new List<double>();
        }

        public virtual List<Tuple<string, string>> GetStatusValuesNamesAndAbbreviations()
        {
            return new List<Tuple<string, string>>();
        }

        public virtual void ActionBeforeTrial()
        {
        }

        public virtual void AdjustDamagesAmounts(ref double ultimateDamagesIfPWins, ref double paymentFromPToDIfDWins)
        {
        }

        public virtual void ActionWhenApplyingLitigationCosts()
        {
        }

        public override OrderingConstraint? DetermineOrderingConstraint(List<ActionGroup> originalList, ActionGroup actionGroupWithinThisModule, ActionGroup secondActionGroup, bool forEvolution)
        {
            if (!forEvolution && secondActionGroup.Name.Contains("EndDropOrDefaultModule"))
                return OrderingConstraint.Before; 
            //if (!forEvolution && secondActionGroup.Name.Contains("BargainingModule"))
            //    return OrderingConstraint.After;
            //if (!forEvolution && secondActionGroup.Name.Contains("TrialModule"))
            //    return OrderingConstraint.Before;
            if (forEvolution && secondActionGroup.Name.Contains("EndDropOrDefaultModule"))
                return OrderingConstraint.After;
            //if (forEvolution && secondActionGroup.Name.Contains("BargainingModule"))
            //    return OrderingConstraint.Before;
            if (forEvolution && secondActionGroup.Name.Contains("ProbabilityPWins"))
                return OrderingConstraint.CloseAfter;
            //if (forEvolution && secondActionGroup.Name.Contains("ForecastingModule"))
            //    return OrderingConstraint.Before;
            //if (forEvolution && actionGroupWithinThisModule.Name.Contains("AdjustmentsModule1") && secondActionGroup.Name.Contains("AdjustmentsModule2"))
            //    return OrderingConstraint.Before; // adjustments module 1 should evolve before module 2 rather than in reverse (as would occur with backward induction)
            return null;
        }

    }
}
