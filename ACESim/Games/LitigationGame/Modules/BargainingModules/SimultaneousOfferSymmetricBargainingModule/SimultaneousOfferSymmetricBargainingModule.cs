using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.ComponentModel.Composition;

namespace ACESim
{
    [Export(typeof(ICodeBasedSettingGenerator))] // include the export and exportmetadata attributes if we implement GenerateSetting, setting up the GameModule by code instead of in the settings file.
    [ExportMetadata("CodeGeneratorName", "SimultaneousOfferSymmetricBargainingModule")]
    [Serializable]
    public class SimultaneousOfferSymmetricBargainingModule : BargainingModule, ICodeBasedSettingGenerator
    {
        public SimultaneousOfferSymmetricBargainingModuleProgress SimultaneousOfferSymmetricBProgress { get { return (SimultaneousOfferSymmetricBargainingModuleProgress)GameModuleProgress; } set { GameModuleProgress = value; } }

        public override void MakeDecisionBasedOnBargainingInputs()
        {
            if (Game.CurrentDecisionIndexWithinActionGroup == 0 && Game.PreparationPhase)
            {
                SpecifyInputs(BargainingProgress.PDecisionInputs);
            }
            else if (Game.CurrentDecisionIndexWithinActionGroup == 0 && !Game.PreparationPhase)
            {
                double mainPlayerSimultaneousOfferSymmetric = Calculate();
                double opponentsSimultaneousOfferSymmetricInOpponentsTerms = CalculateWithoutAffectingEvolution(BargainingProgress.DDecisionInputs, (int)Game.CurrentDecisionIndex, true, true);
                double opponentsSimultaneousOfferSymmetricFromMainPlayersPerspective = 1.0 - opponentsSimultaneousOfferSymmetricInOpponentsTerms; // proportion main player would receive from opponent
                SimultaneousOfferSymmetricBProgress.DistanceBetweenOffers = opponentsSimultaneousOfferSymmetricFromMainPlayersPerspective - mainPlayerSimultaneousOfferSymmetric;
                bool settlementExists = SimultaneousOfferSymmetricBProgress.DistanceBetweenOffers > 0;
                if (settlementExists)
                    SimultaneousOfferSymmetricBProgress.SettlementProgress = new GlobalSettlementProgress() { AgreedUponPaymentAsProportion = (mainPlayerSimultaneousOfferSymmetric + opponentsSimultaneousOfferSymmetricFromMainPlayersPerspective) / 2.0, GlobalSettlementAchieved = true, OriginalDamagesClaim = LitigationGame.DisputeGeneratorModule.DGProgress.DamagesClaim };

                SimultaneousOfferSymmetricBProgress.POfferForDToConsiderInNextRound = mainPlayerSimultaneousOfferSymmetric;
                SimultaneousOfferSymmetricBProgress.DOfferForPToConsiderInNextRound = opponentsSimultaneousOfferSymmetricFromMainPlayersPerspective;
            }
        }

        public override void Score()
        {
            Game.Score((int)Game.CurrentlyEvolvingDecisionIndex, LitigationGame.LitigationCostModule.GetFinalWealthSubjectiveUtilityValue(true));
        }

        public override void CreateInstanceAndInitializeProgress(Game theGame, List<Strategy> theStrategies, object gameModuleSettings, out GameModule theGameModule, out GameModuleProgress theGameModuleProgress)
        {
            SimultaneousOfferSymmetricBargainingModule copy = new SimultaneousOfferSymmetricBargainingModule();
            copy.SetGameAndStrategies(theGame, theStrategies, GameModuleName, GameModuleNamesThisModuleReliesOn, GameModuleNumbersThisModuleReliesOn, GameModuleSettings);
            SetGameModuleFields(copy); 
            theGameModuleProgress = SimultaneousOfferSymmetricBargainingModuleProgress.GetRecycledOrAllocate();
            theGameModule = copy;
        }
        

        public virtual object GenerateSetting(string options)
        {
            List<Decision> decisions = new List<Decision>();

            decisions.Add(new Decision() {
                Name="SimultaneousOffer",
                Abbreviation="so",
                DynamicNumberOfInputs = true,
                UseOversampling = true,
                SuccessReplicationIfSuccessAttemptRatioIsBelowThis = 0.1,
                InputAbbreviations = null,
                InputNames = null,
                StrategyBounds = new StrategyBounds() {
                    LowerBound = 0.0,
                    UpperBound = 1.0
                },
                Bipolar = false,
                StrategyGraphInfos = new List<StrategyGraphInfo>(),
                HighestIsBest = true,
                MaxEvolveRepetitions = 99999,
                AverageInPreviousVersion = true,
                PreservePreviousVersionWhenOptimizing = true
            });

            return new SimultaneousOfferSymmetricBargainingModule()
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
