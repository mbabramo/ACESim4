using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.ComponentModel.Composition;

namespace ACESim
{
    [Export(typeof(ICodeBasedSettingGenerator))] // include the export and exportmetadata attributes if we implement GenerateSetting, setting up the GameModule by code instead of in the settings file.
    [ExportMetadata("CodeGeneratorName", "ForecastingModule")]
    [Serializable]
    public class DropOrDefaultModule : GameModule, ICodeBasedSettingGenerator
    {

        public LitigationGame LitigationGame { get { return (LitigationGame)Game; } }
        public DropOrDefaultModuleProgress DropOrDefaultProgress { get { return (DropOrDefaultModuleProgress)GameModuleProgress; } set { GameModuleProgress = value; } }
        public DropOrDefaultInputs DropOrDefaultInputs { get { return (DropOrDefaultInputs)GameModuleInputs; } }

        public override void ExecuteModule()
        {
            if (Game.CurrentActionPointName == "ActionBeforeDropOrDefault")
            {
                DetermineDropOrDefaultPeriod();
            }
            else
                MakeDecisions();
        }

        public virtual void MakeDecisions()
        {
        }

        public void DetermineDropOrDefaultPeriod()
        {
            if (GameModuleName.StartsWith("Beginning"))
                DropOrDefaultProgress.DropOrDefaultPeriod = DropOrDefaultPeriod.Beginning;
            else if (GameModuleName.StartsWith("Mid"))
                DropOrDefaultProgress.DropOrDefaultPeriod = DropOrDefaultPeriod.Mid;
            else if (GameModuleName.StartsWith("End"))
                DropOrDefaultProgress.DropOrDefaultPeriod = DropOrDefaultPeriod.End;
            else
                throw new Exception("Internal exception: Drop or default module had improperly formatted name.");
        }



        internal static DropOrDefaultPeriod GetDropOrDefaultPeriodFromCodeGeneratorOptions(string options)
        {
            DropOrDefaultPeriod period;
            bool isBeginning = GetStringCodeGeneratorOption(options, "DropOrDefaultPoint").Contains("Beginning");
            bool isMid = GetStringCodeGeneratorOption(options, "DropOrDefaultPoint").Contains("Mid");
            bool isEnd = GetStringCodeGeneratorOption(options, "DropOrDefaultPoint").Contains("End");
            if (isBeginning)
                period = DropOrDefaultPeriod.Beginning;
            else if (isMid)
                period = DropOrDefaultPeriod.Mid;
            else if (isEnd)
                period = DropOrDefaultPeriod.End;
            else
                throw new Exception("Unknown drop or default period.");
            return period;
        }


        public override void Score()
        {
            throw new NotImplementedException("This must be overridden.");
        }

        public override void CreateInstanceAndInitializeProgress(Game theGame, List<Strategy> theStrategies, object gameModuleSettings, out GameModule theGameModule, out GameModuleProgress theGameModuleProgress)
        {
            DropOrDefaultModule copy = new DropOrDefaultModule();
            copy.SetGameAndStrategies(theGame, theStrategies, GameModuleName, GameModuleNamesThisModuleReliesOn, GameModuleNumbersThisModuleReliesOn, GameModuleSettings);
            SetGameModuleFields(copy); 
            theGameModuleProgress = DropOrDefaultModuleProgress.GetRecycledOrAllocate();
            theGameModule = copy;
        }

        public virtual object GenerateSetting(string options)
        {
            throw new Exception("Override in subclass.");
        }

        internal virtual DropOrDefaultModule GenerateNewForecastingModule(List<Decision> decisions)
        {
            throw new Exception("Override in subclass.");
        }

        public override OrderingConstraint? DetermineOrderingConstraint(List<ActionGroup> originalList, ActionGroup actionGroupWithinThisModule, ActionGroup secondActionGroup, bool forEvolution)
        {
            if (!forEvolution && actionGroupWithinThisModule.Name.StartsWith("BeginningDropOrDefaultModule") && secondActionGroup.Name.Contains("Bargaining"))
                return OrderingConstraint.Before;
            if (!forEvolution && actionGroupWithinThisModule.Name.StartsWith("EndDropOrDefaultModule") && secondActionGroup.Name.Contains("BeginningDropOrDefaultModule"))
                return OrderingConstraint.After;
            if (!forEvolution && actionGroupWithinThisModule.Name.StartsWith("MidDropOrDefaultModule") && secondActionGroup.Name.StartsWith("BeginningDropOrDefaultModule"))
                return OrderingConstraint.After;
            if (!forEvolution && actionGroupWithinThisModule.Name.StartsWith("MidDropOrDefaultModule") && secondActionGroup.Name.StartsWith("EndDropOrDefaultModule"))
                return OrderingConstraint.Before;
            // we don't need to specify where MidDropOrDefaultModule goes with respect to bargaining rounds, because we haven't done the repetition yet

            if (forEvolution && actionGroupWithinThisModule.Name.StartsWith("BeginningDropOrDefaultModule") && secondActionGroup.Name.Contains("Bargaining"))
                return OrderingConstraint.After;
            if (forEvolution && actionGroupWithinThisModule.Name.StartsWith("EndDropOrDefaultModule") && secondActionGroup.Name.Contains("BeginningDropOrDefaultModule"))
                return OrderingConstraint.Before;
            if (forEvolution && actionGroupWithinThisModule.Name.StartsWith("MidDropOrDefaultModule") && secondActionGroup.Name.StartsWith("BeginningDropOrDefaultModule"))
                return OrderingConstraint.Before;
            if (forEvolution && actionGroupWithinThisModule.Name.StartsWith("MidDropOrDefaultModule") && secondActionGroup.Name.StartsWith("EndDropOrDefaultModule"))
                return OrderingConstraint.After;
            if (forEvolution && actionGroupWithinThisModule.Name.StartsWith("MidDropOrDefaultModule") && secondActionGroup.Name.Contains("Bargaining"))
            {
                int? thisActionGroupBargainingRound = actionGroupWithinThisModule.GetRepetitionNumberForTag("Bargaining round");
                int? secondActionGroupBargainingRound = secondActionGroup.GetRepetitionNumberForTag("Bargaining round");
                bool evolveLaterBargainingRoundsFirst = false;
                if (thisActionGroupBargainingRound != null && secondActionGroupBargainingRound != null)
                {
                    if (thisActionGroupBargainingRound < secondActionGroupBargainingRound == evolveLaterBargainingRoundsFirst)
                        return OrderingConstraint.After; // earlier bargaining rounds evolve later if later bargaining rounds evolve first
                    else if (thisActionGroupBargainingRound > secondActionGroupBargainingRound == evolveLaterBargainingRoundsFirst)
                        return OrderingConstraint.Before;
                    return OrderingConstraint.Before; // this is the SAME bargaining round and the mid-drop should evolve before the bargaining.
                }
            }
            return null;
        }

    }
}
