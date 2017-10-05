﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.ComponentModel.Composition;

namespace ACESim
{
    [Export(typeof(ICodeBasedSettingGenerator))] // include the export and exportmetadata attributes if we implement GenerateSetting, setting up the GameModule by code instead of in the settings file.
    [ExportMetadata("CodeGeneratorName", "NoBargainingBargainingModule")]
    [Serializable]
    public class NoBargainingBargainingModule : BargainingModule, ICodeBasedSettingGenerator
    {

        public NoBargainingBargainingModuleProgress NoBargainingBargainingProgress { get { return (NoBargainingBargainingModuleProgress)GameModuleProgress; } set { GameModuleProgress = value; } }

        public enum NoBargainingBargainingDecisions
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
            NoBargainingBargainingProgress.SettlementProgress = null;
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
            NoBargainingBargainingModule copy = new NoBargainingBargainingModule();
            copy.SetGameAndStrategies(theGame, theStrategies, GameModuleName, GameModuleNamesThisModuleReliesOn, GameModuleNumbersThisModuleReliesOn, GameModuleSettings);
            SetGameModuleFields(copy); 
            theGameModuleProgress = NoBargainingBargainingModuleProgress.GetRecycledOrAllocate();
            theGameModule = copy;
        }
        

        public virtual object GenerateSetting(string options)
        {
            List<Decision> decisions = new List<Decision>();


            return new NoBargainingBargainingModule()
            {
                DecisionsCore = decisions,
                ActionsAtBeginningOfModule = new List<string>() { "BeforeBargaining" },
                ActionsAtEndOfModule = new List<string>() { "AfterBargaining" },
                GameModuleName = "BargainingModule",
                GameModuleNamesThisModuleReliesOn = new List<string>() { },
                UpdateCumulativeDistributionsAfterSingleActionGroup = false /* subsequent cumulative distributions were causing problems on subsequent repetitions */
            };
        }

        

        
    }
}