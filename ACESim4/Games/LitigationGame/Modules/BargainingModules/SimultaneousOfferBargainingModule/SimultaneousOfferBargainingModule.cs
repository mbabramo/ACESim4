using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.ComponentModel.Composition;

namespace ACESim
{
    [Export(typeof(ICodeBasedSettingGenerator))] // include the export and exportmetadata attributes if we implement GenerateSetting, setting up the GameModule by code instead of in the settings file.
    [ExportMetadata("CodeGeneratorName", "SimultaneousOfferBargainingModule")]
    [Serializable]
    public class SimultaneousOfferBargainingModule : BargainingModule, ICodeBasedSettingGenerator
    {

        public SimultaneousOfferBargainingModuleProgress SimultaneousOfferBProgress { get { return (SimultaneousOfferBargainingModuleProgress)GameModuleProgress; } set { GameModuleProgress = value; } }
        public SimultaneousOfferBargainingInputs SimultaneousOfferBargainingInputs { get { return (SimultaneousOfferBargainingInputs)GameModuleInputs; } } 

        public enum SimultaneousOfferBargainingDecisions
        { // Note: We can switch order of plaintiffdecision and defendantdecision here.
            PlaintiffOffer,
            DefendantOffer,
            PAcceptDOffer,
            DAcceptPOffer
        }

        public override void MakeDecisionBasedOnBargainingInputs()
        {
            if (Game.CurrentDecisionIndexWithinActionGroup == (int)SimultaneousOfferBargainingDecisions.PlaintiffOffer && Game.PreparationPhase)
            {
                if (SimultaneousOfferBProgress.BargainingRound == null)
                    SimultaneousOfferBProgress.BargainingRound = 1;
                else if (SimultaneousOfferBProgress.CurrentBargainingSubclaimNumber == 1)
                    SimultaneousOfferBProgress.BargainingRound++;
                SpecifyInputs(BargainingProgress.PDecisionInputs);
            }
            else if (Game.CurrentDecisionIndexWithinActionGroup == (int)SimultaneousOfferBargainingDecisions.PlaintiffOffer && !Game.PreparationPhase)
            {
                bool currentlyEvolvingThisModule = Game.CurrentlyEvolvingCurrentlyExecutingModule;
                bool currentlyEvolvingOtherDecision = currentlyEvolvingThisModule && Game.CurrentlyEvolvingDecisionIndexWithinActionGroup == (int)SimultaneousOfferBargainingDecisions.DefendantOffer;
                SimultaneousOfferBProgress.MostRecentPlaintiffOffer = Calculate((int)LitigationGame.CurrentDecisionIndex, currentlyEvolvingOtherDecision, currentlyEvolvingOtherDecision); // i.e., how much defendant must pay to plaintiff
                AddToProgressList(ref SimultaneousOfferBProgress.POfferList, SimultaneousOfferBProgress.MostRecentPlaintiffOffer);
            }
            else if (Game.CurrentDecisionIndexWithinActionGroup == (int)SimultaneousOfferBargainingDecisions.DefendantOffer && Game.PreparationPhase)
            {
                SpecifyInputs(BargainingProgress.DDecisionInputs);
            }
            else if (Game.CurrentDecisionIndexWithinActionGroup == (int)SimultaneousOfferBargainingDecisions.DefendantOffer && !Game.PreparationPhase)
            {
                bool currentlyEvolvingThisModule = Game.CurrentlyEvolving && Game.CurrentlyEvolvingCurrentlyExecutingActionGroup;
                bool currentlyEvolvingOtherDecision = currentlyEvolvingThisModule && Game.CurrentlyEvolvingDecisionIndexWithinActionGroup == (int)SimultaneousOfferBargainingDecisions.PlaintiffOffer;
                SimultaneousOfferBProgress.MostRecentDefendantOffer = Calculate((int)LitigationGame.CurrentDecisionIndex, currentlyEvolvingOtherDecision, currentlyEvolvingOtherDecision); // i.e., how much defendant must pay to plaintiff
                AddToProgressList(ref SimultaneousOfferBProgress.DOfferList, SimultaneousOfferBProgress.MostRecentDefendantOffer);
            } 
            else if (Game.CurrentDecisionIndexWithinActionGroup == (int)SimultaneousOfferBargainingDecisions.PAcceptDOffer && Game.PreparationPhase)
            {
                List<double> inputs = BargainingProgress.PDecisionInputs.ToList();
                inputs.Add((double) SimultaneousOfferBProgress.MostRecentDefendantOffer);
                SpecifyInputs(inputs);
            }
            else if (Game.CurrentDecisionIndexWithinActionGroup == (int)SimultaneousOfferBargainingDecisions.PAcceptDOffer && !Game.PreparationPhase)
            {
                if (SimultaneousOfferBProgress.MostRecentDefendantOffer < SimultaneousOfferBProgress.MostRecentPlaintiffOffer) // no settlement yet!
                {
                    if (Game.CurrentlyEvolvingActionGroup == Game.CurrentActionGroup && Game.CurrentlyEvolvingDecisionIndexWithinActionGroup == (int)SimultaneousOfferBargainingDecisions.DAcceptPOffer)
                        SimultaneousOfferBProgress.PAcceptsDOffer = false;
                    else
                        SimultaneousOfferBProgress.PAcceptsDOffer = Calculate() > 0.0;
                }
            }
            else if (Game.CurrentDecisionIndexWithinActionGroup == (int)SimultaneousOfferBargainingDecisions.DAcceptPOffer && Game.PreparationPhase)
            {
                List<double> inputs = BargainingProgress.DDecisionInputs.ToList();
                inputs.Add((double) SimultaneousOfferBProgress.MostRecentPlaintiffOffer);
                SpecifyInputs(inputs);
            }
            else if (Game.CurrentDecisionIndexWithinActionGroup == (int)SimultaneousOfferBargainingDecisions.DAcceptPOffer && !Game.PreparationPhase)
            {
                if (SimultaneousOfferBProgress.MostRecentDefendantOffer < SimultaneousOfferBProgress.MostRecentPlaintiffOffer) // no settlement yet!
                {
                    if (Game.CurrentlyEvolvingActionGroup == Game.CurrentActionGroup && Game.CurrentlyEvolvingDecisionIndexWithinActionGroup == (int)SimultaneousOfferBargainingDecisions.PAcceptDOffer)
                        SimultaneousOfferBProgress.DAcceptsPOffer = false;
                    else
                        SimultaneousOfferBProgress.DAcceptsPOffer = Calculate() > 0.0;
                    if (SimultaneousOfferBProgress.PAcceptsDOffer && SimultaneousOfferBProgress.DAcceptsPOffer)
                    {
                        if (SimultaneousOfferBargainingInputs.RandomSeedForSimultaneousAcceptanceOfOtherOffers > 0.5)
                            SimultaneousOfferBProgress.DAcceptsPOffer = false;
                        else
                            SimultaneousOfferBProgress.PAcceptsDOffer = false;
                    }
                }
            }
        }

        public override void DetermineSettlement()
        {
            RegisterExtraInvestigationRound();
            SimultaneousOfferBProgress.MostRecentBargainingDistance = SimultaneousOfferBProgress.MostRecentDefendantOffer - SimultaneousOfferBProgress.MostRecentPlaintiffOffer;
            AddToProgressList(ref SimultaneousOfferBProgress.BargainingDistanceList, SimultaneousOfferBProgress.MostRecentBargainingDistance);
            bool settlementExists = SimultaneousOfferBProgress.MostRecentBargainingDistance > 0;
            if (SimultaneousOfferBProgress.SettlementExists == false && (SimultaneousOfferBProgress.PAcceptsDOffer || SimultaneousOfferBProgress.DAcceptsPOffer) && SimultaneousOfferBargainingInputs.RandomSeedToDetermineWhetherAcceptanceOfOtherOffersAllowed < SimultaneousOfferBargainingInputs.ChanceLastMomentAcceptanceAllowed)
            {
                SimultaneousOfferBProgress.SettlementExistsAfterFirstBargainingRound = SimultaneousOfferBProgress.BargainingRound == 1;
                SimultaneousOfferBProgress.BargainingRoundInWhichSettlementReached = SimultaneousOfferBProgress.BargainingRound;
                SimultaneousOfferBProgress.SettlementProgress = new GlobalSettlementProgress() { AgreedUponPaymentAsProportion = SimultaneousOfferBProgress.DAcceptsPOffer ? SimultaneousOfferBProgress.MostRecentPlaintiffOffer : SimultaneousOfferBProgress.MostRecentDefendantOffer, GlobalSettlementAchieved = true, OriginalDamagesClaim = LitigationGame.DisputeGeneratorModule.DGProgress.DamagesClaim };
            }
            else if (SimultaneousOfferBProgress.SettlementExists == true)
            {
                SimultaneousOfferBProgress.SettlementExistsAfterFirstBargainingRound = SimultaneousOfferBProgress.BargainingRound == 1;
                SimultaneousOfferBProgress.BargainingRoundInWhichSettlementReached = SimultaneousOfferBProgress.BargainingRound;
                SimultaneousOfferBProgress.SettlementProgress = new GlobalSettlementProgress() { AgreedUponPaymentAsProportion = (SimultaneousOfferBProgress.MostRecentPlaintiffOffer + SimultaneousOfferBProgress.MostRecentDefendantOffer) / 2.0, GlobalSettlementAchieved = true, OriginalDamagesClaim = LitigationGame.DisputeGeneratorModule.DGProgress.DamagesClaim };
            }
            else
            {
                SimultaneousOfferBProgress.SettlementExistsAfterFirstBargainingRound = false;
                SimultaneousOfferBProgress.SettlementFailedAfterBargainingRoundNum = (int)SimultaneousOfferBProgress.BargainingRound;
            }

            SimultaneousOfferBProgress.POfferForDToConsiderInNextRound = SimultaneousOfferBProgress.MostRecentPlaintiffOffer;
            SimultaneousOfferBProgress.DOfferForPToConsiderInNextRound = SimultaneousOfferBProgress.MostRecentDefendantOffer;
        }

        public override double DefaultBehaviorBeforeEvolution(List<double> inputs, int decisionNumber)
        {
            int decisionNumberWithinActionGroup = (int)Game.DecisionNumberWithinActionGroupForDecisionNumber(decisionNumber);
            if (Game.CurrentlyEvolvingActionGroup == Game.CurrentActionGroup) 
            {
                if (decisionNumberWithinActionGroup == (int)SimultaneousOfferBargainingDecisions.PlaintiffOffer)
                    return inputs[0];
                else if (decisionNumberWithinActionGroup == (int)SimultaneousOfferBargainingDecisions.DefendantOffer)
                    return 1.0 - inputs[0];
                else
                    return -1.0; // don't accept other's offer
            }
            else
            {
                if (decisionNumberWithinActionGroup == (int)SimultaneousOfferBargainingDecisions.PlaintiffOffer)
                    return 1.0; 
                else if (decisionNumberWithinActionGroup == (int)SimultaneousOfferBargainingDecisions.DefendantOffer)
                    return 0; 
                else
                    return -1.0; // don't accept other's offer
            }
        }

        public override void Score()
        {
            if (LitigationGame.CurrentlyEvolvingDecisionIndexWithinActionGroup == (int)SimultaneousOfferBargainingDecisions.PlaintiffOffer || LitigationGame.CurrentlyEvolvingDecisionIndexWithinActionGroup == (int)SimultaneousOfferBargainingDecisions.PAcceptDOffer)
                Game.Score((int)LitigationGame.CurrentlyEvolvingDecisionIndex, LitigationGame.LitigationCostModule.GetFinalWealthSubjectiveUtilityValue(true));
            else if (LitigationGame.CurrentlyEvolvingDecisionIndexWithinActionGroup == (int)SimultaneousOfferBargainingDecisions.DefendantOffer || LitigationGame.CurrentlyEvolvingDecisionIndexWithinActionGroup == (int)SimultaneousOfferBargainingDecisions.DAcceptPOffer)
                Game.Score((int)LitigationGame.CurrentlyEvolvingDecisionIndex, LitigationGame.LitigationCostModule.GetFinalWealthSubjectiveUtilityValue(false));
        }

        public override void CreateInstanceAndInitializeProgress(Game theGame, List<Strategy> theStrategies, object gameModuleSettings, out GameModule theGameModule, out GameModuleProgress theGameModuleProgress)
        {
            SimultaneousOfferBargainingModule copy = new SimultaneousOfferBargainingModule();
            copy.SetGameAndStrategies(theGame, theStrategies, GameModuleName, GameModuleNamesThisModuleReliesOn, GameModuleNumbersThisModuleReliesOn, GameModuleSettings);
            SetGameModuleFields(copy); 
            theGameModuleProgress = SimultaneousOfferBargainingModuleProgress.GetRecycledOrAllocate();
            theGameModule = copy;
        }
        

        public virtual object GenerateSetting(string options)
        {
            List<Decision> decisions = new List<Decision>();
            bool addOptionToAcceptOthersOfferWhenNoOverlap = GetBoolCodeGeneratorOption(options, "AddOptionToAcceptOthersOfferWhenNoOverlap");

            decisions.Add(GetOfferDecision("POffer", "po", true, true));
            decisions.Add(GetOfferDecision("DOffer", "do", false, false));
            if (addOptionToAcceptOthersOfferWhenNoOverlap)
            {
                decisions.Add(GetOfferDecision("PAcceptD", "pad", true, true));
                decisions.Add(GetOfferDecision("DAcceptP", "dap", false, false));
            }

            return new SimultaneousOfferBargainingModule()
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

        private static Decision GetOfferDecision(string name, string abbreviation, bool isFirstInRound, bool isPDecision)
        {
            return new Decision()
            {
                Name = name,
                Abbreviation = abbreviation,
                DecisionTypeCode = isPDecision ? "P" : "D",
                DynamicNumberOfInputs = true,
                UseOversampling = true,
                SuccessReplicationIfSuccessAttemptRatioIsBelowThis = 0.1,
                InformationSetAbbreviations = null,
                InputNames = null,
                StrategyBounds = new StrategyBounds()
                {
                    LowerBound = -0.5,
                    UpperBound = 1.5
                },
                Bipolar = false,
                StrategyGraphInfos = new List<StrategyGraphInfo>(),
                HighestIsBest = true,
                PhaseOutDefaultBehaviorOverRepetitions = 0,
                MaxEvolveRepetitions = 99999,
                PreservePreviousVersionWhenOptimizing = true,
                AverageInPreviousVersion = true,
                EvolveThisDecisionEvenWhenSkippingByDefault = false,
                TestInputs = null, // new List<double>() { 0.5 }, 
                TestOutputs = null // new List<double>() { 0.001, 0.1, 0.2, 0.3, 0.4, 0.5, 0.6, 0.7, 0.8 }
            };
        }

        private static Decision GetAcceptDecision(string name, string abbreviation, bool isFirstInRound, bool isPDecision)
        {
            return new Decision()
            {
                Name = name,
                Abbreviation = abbreviation,
                DecisionTypeCode = isPDecision ? "PAcD" : "DAcP",
                DynamicNumberOfInputs = true,
                UseOversampling = true,
                SuccessReplicationIfSuccessAttemptRatioIsBelowThis = 0.1,
                InformationSetAbbreviations = null,
                InputNames = null,
                Bipolar = true,
                StrategyBounds = new StrategyBounds()
                {
                    LowerBound = -1.0,
                    UpperBound = 1.0
                },
                StrategyGraphInfos = new List<StrategyGraphInfo>(),
                HighestIsBest = true,
                PhaseOutDefaultBehaviorOverRepetitions = 0,
                MaxEvolveRepetitions = 99999,
                PreservePreviousVersionWhenOptimizing = true,
                AverageInPreviousVersion = true,
                EvolveThisDecisionEvenWhenSkippingByDefault = false,
                TestInputs = null, // new List<double>() { 0.52, 0.18, 0.16, 80.0, 97.1 }, 
                TestOutputs = null // new List<double>() { 0.1, 0.2, 0.3, 0.4, 0.5, 0.6, 0.7, 0.8 }
            };
        }
    }
}
