using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.ComponentModel.Composition;

namespace ACESim
{
    [Export(typeof(ICodeBasedSettingGenerator))] // include the export and exportmetadata attributes if we implement GenerateSetting, setting up the GameModule by code instead of in the settings file.
    [ExportMetadata("CodeGeneratorName", "OfferThresholdBargainingModule")]
    [Serializable]
    public class OfferThresholdBargainingModule : BargainingModule, ICodeBasedSettingGenerator
    {

        public OfferThresholdBargainingModuleProgress OfferThresholdBProgress { get { return (OfferThresholdBargainingModuleProgress)GameModuleProgress; } set { GameModuleProgress = value; } }

        public enum OfferThresholdBargainingDecisions
        {
            PlaintiffOffer,
            DefendantOffer,
            PlaintiffThreshold,
            DefendantThreshold
        }

        public override void MakeDecisionBasedOnBargainingInputs()
        {
            OfferThresholdBProgress.RandomlyPickingOneOffer = ((OfferThresholdBargainingInputs)GameModuleInputs).RandomlyPickOnePartysOfferToConsider;
            OfferThresholdBProgress.ConsiderPlaintiffsOffer = OfferThresholdBProgress.RandomlyPickingOneOffer == false || ((OfferThresholdBargainingInputs)GameModuleInputs).RandomPickingCoefficient < 0.5;
            OfferThresholdBProgress.ConsiderDefendantsOffer = OfferThresholdBProgress.RandomlyPickingOneOffer == false || ((OfferThresholdBargainingInputs)GameModuleInputs).RandomPickingCoefficient >= 0.5;
            if (Game.CurrentDecisionIndexWithinActionGroup == (int)OfferThresholdBargainingDecisions.PlaintiffOffer && Game.PreparationPhase)
            {
                if (OfferThresholdBProgress.BargainingRound == null)
                    OfferThresholdBProgress.BargainingRound = 1;
                else if (OfferThresholdBProgress.CurrentBargainingSubclaimNumber == 1)
                    OfferThresholdBProgress.BargainingRound++;
                SpecifyInputs(BargainingProgress.PDecisionInputs);
            }
            else if (Game.CurrentDecisionIndexWithinActionGroup == (int)OfferThresholdBargainingDecisions.PlaintiffOffer && !Game.PreparationPhase)
            {
                OfferThresholdBProgress.MostRecentPlaintiffOffer = Calculate(); // i.e., how much defendant must pay to plaintiff
                AddToProgressList(ref OfferThresholdBProgress.ListPlaintiffOffer, OfferThresholdBProgress.MostRecentPlaintiffOffer);
            }
            else if (Game.CurrentDecisionIndexWithinActionGroup == (int)OfferThresholdBargainingDecisions.DefendantOffer && Game.PreparationPhase)
            {
                SpecifyInputs(BargainingProgress.DDecisionInputs);
            }
            else if (Game.CurrentDecisionIndexWithinActionGroup == (int)OfferThresholdBargainingDecisions.DefendantOffer && !Game.PreparationPhase)
            {
                OfferThresholdBProgress.MostRecentDefendantOffer = Calculate(); // i.e., how much defendant must pay to plaintiff
                AddToProgressList(ref OfferThresholdBProgress.ListDefendantOffer, OfferThresholdBProgress.MostRecentDefendantOffer);
            }
            else if (Game.CurrentDecisionIndexWithinActionGroup == (int)OfferThresholdBargainingDecisions.PlaintiffThreshold && Game.PreparationPhase)
            {
                SpecifyInputs(BargainingProgress.PDecisionInputs);
            }
            else if (Game.CurrentDecisionIndexWithinActionGroup == (int)OfferThresholdBargainingDecisions.PlaintiffThreshold && !Game.PreparationPhase)
            {
                OfferThresholdBProgress.MostRecentPlaintiffThreshold = Calculate(); // i.e., how much defendant must pay to plaintiff
                AddToProgressList(ref OfferThresholdBProgress.ListPlaintiffThreshold, OfferThresholdBProgress.MostRecentPlaintiffThreshold);
            }
            else if (Game.CurrentDecisionIndexWithinActionGroup == (int)OfferThresholdBargainingDecisions.DefendantThreshold && Game.PreparationPhase)
            {
                SpecifyInputs(BargainingProgress.DDecisionInputs);
            }
            else if (Game.CurrentDecisionIndexWithinActionGroup == (int)OfferThresholdBargainingDecisions.DefendantThreshold && !Game.PreparationPhase)
            {
                OfferThresholdBProgress.MostRecentDefendantThreshold = Calculate(); // i.e., how much defendant must pay to plaintiff
                AddToProgressList(ref OfferThresholdBProgress.ListDefendantThreshold, OfferThresholdBProgress.MostRecentDefendantThreshold);
            }
        }

        public override void DetermineSettlement()
        {
            RegisterExtraInvestigationRound();
            ResolveOffers();
            OfferThresholdBProgress.POfferForDToConsiderInNextRound = OfferThresholdBProgress.MostRecentPlaintiffOffer;
            OfferThresholdBProgress.DOfferForPToConsiderInNextRound = OfferThresholdBProgress.MostRecentPlaintiffOffer;
        }

        private void ResolveOffers()
        {
            OfferThresholdBProgress.MostRecentBargainingDistancePlaintiffOffer = OfferThresholdBProgress.MostRecentDefendantThreshold - OfferThresholdBProgress.MostRecentPlaintiffOffer; // i.e., plaintiff offers less than maximum defendant will pay
            OfferThresholdBProgress.MostRecentBargainingDistanceDefendantOffer = OfferThresholdBProgress.MostRecentDefendantOffer - OfferThresholdBProgress.MostRecentPlaintiffThreshold; // i.e., defendant offers more than minimum plaintiff will take

            AddToProgressList(ref OfferThresholdBProgress.ListBargainingDistancePlaintiffOffer, OfferThresholdBProgress.MostRecentBargainingDistancePlaintiffOffer);
            AddToProgressList(ref OfferThresholdBProgress.ListBargainingDistanceDefendantOffer, OfferThresholdBProgress.MostRecentBargainingDistanceDefendantOffer);
            bool settlementExists = (OfferThresholdBProgress.MostRecentBargainingDistancePlaintiffOffer > 0 && OfferThresholdBProgress.ConsiderPlaintiffsOffer == true) || (OfferThresholdBProgress.MostRecentBargainingDistanceDefendantOffer > 0 && OfferThresholdBProgress.ConsiderDefendantsOffer == true);
            if (settlementExists)
            {
                OfferThresholdBProgress.SettlementExistsAfterFirstBargainingRound = OfferThresholdBProgress.BargainingRound == 1;
                OfferThresholdBProgress.BargainingRoundInWhichSettlementReached = OfferThresholdBProgress.BargainingRound;
                if (OfferThresholdBProgress.MostRecentBargainingDistancePlaintiffOffer > 0 && OfferThresholdBProgress.ConsiderPlaintiffsOffer == true && OfferThresholdBProgress.MostRecentBargainingDistanceDefendantOffer > 0 && OfferThresholdBProgress.ConsiderDefendantsOffer == true)
                {
                    OfferThresholdBProgress.SettlementIsOnBothOffers = true;
                    OfferThresholdBProgress.SettlementIsOnPlaintiffOfferOnly = false;
                    OfferThresholdBProgress.SettlementIsOnDefendantOfferOnly = false;
                    OfferThresholdBProgress.SettlementProgress = new GlobalSettlementProgress() { AgreedUponPaymentAsProportion = (OfferThresholdBProgress.MostRecentPlaintiffOffer + OfferThresholdBProgress.MostRecentDefendantOffer) / 2.0, GlobalSettlementAchieved = true, OriginalDamagesClaim = LitigationGame.DisputeGeneratorModule.DGProgress.DamagesClaim };
                }
                else if (OfferThresholdBProgress.MostRecentBargainingDistancePlaintiffOffer > 0 && OfferThresholdBProgress.ConsiderPlaintiffsOffer == true)
                {
                    OfferThresholdBProgress.SettlementIsOnBothOffers = false;
                    OfferThresholdBProgress.SettlementIsOnPlaintiffOfferOnly = true;
                    OfferThresholdBProgress.SettlementIsOnDefendantOfferOnly = false;
                    OfferThresholdBProgress.SettlementProgress = new GlobalSettlementProgress() { AgreedUponPaymentAsProportion = OfferThresholdBProgress.MostRecentPlaintiffOffer, GlobalSettlementAchieved = true, OriginalDamagesClaim = LitigationGame.DisputeGeneratorModule.DGProgress.DamagesClaim };
                }
                else
                {
                    OfferThresholdBProgress.SettlementIsOnBothOffers = false;
                    OfferThresholdBProgress.SettlementIsOnPlaintiffOfferOnly = false;
                    OfferThresholdBProgress.SettlementIsOnDefendantOfferOnly = true;
                    OfferThresholdBProgress.SettlementProgress = new GlobalSettlementProgress() { AgreedUponPaymentAsProportion = OfferThresholdBProgress.MostRecentDefendantOffer, GlobalSettlementAchieved = true, OriginalDamagesClaim = LitigationGame.DisputeGeneratorModule.DGProgress.DamagesClaim };
                }
            }
            else
            {
                OfferThresholdBProgress.SettlementExistsAfterFirstBargainingRound = false;

                OfferThresholdBProgress.SettlementFailedAfterBargainingRoundNum = (int)OfferThresholdBProgress.BargainingRound;
            }
        }

        public override double DefaultBehaviorBeforeEvolution(List<double> inputs, int decisionNumber)
        {
            int decisionNumberWithinActionGroup = (int)Game.DecisionNumberWithinActionGroupForDecisionNumber(decisionNumber);
            // estimate of own result, opponent's result, own error ==> we'll initialize by demanding something better than own result (thus ignoring litigation costs)
            // for each bargaining round. 
            if (decisionNumberWithinActionGroup == (int)OfferThresholdBargainingDecisions.PlaintiffOffer || decisionNumberWithinActionGroup == (int)OfferThresholdBargainingDecisions.PlaintiffThreshold)
                return inputs[0];
            else if (decisionNumberWithinActionGroup == (int)OfferThresholdBargainingDecisions.DefendantOffer || decisionNumberWithinActionGroup == (int)OfferThresholdBargainingDecisions.DefendantThreshold)
                return inputs[0];
            throw new Exception("Unknown decision.");
        }

        public override void Score()
        {
            if (LitigationGame.CurrentlyEvolvingDecisionIndexWithinActionGroup == (int)OfferThresholdBargainingDecisions.PlaintiffOffer || LitigationGame.CurrentlyEvolvingDecisionIndexWithinActionGroup == (int)OfferThresholdBargainingDecisions.PlaintiffThreshold)
                Game.Score((int)LitigationGame.CurrentlyEvolvingDecisionIndex, LitigationGame.LitigationCostModule.GetFinalWealthSubjectiveUtilityValue(true));
            else if (LitigationGame.CurrentlyEvolvingDecisionIndexWithinActionGroup == (int)OfferThresholdBargainingDecisions.DefendantOffer || LitigationGame.CurrentlyEvolvingDecisionIndexWithinActionGroup == (int)OfferThresholdBargainingDecisions.DefendantThreshold)
                Game.Score((int)LitigationGame.CurrentlyEvolvingDecisionIndex, LitigationGame.LitigationCostModule.GetFinalWealthSubjectiveUtilityValue(false));
        }

        public override void CreateInstanceAndInitializeProgress(Game theGame, List<Strategy> theStrategies, object gameModuleSettings, out GameModule theGameModule, out GameModuleProgress theGameModuleProgress)
        {
            OfferThresholdBargainingModule copy = new OfferThresholdBargainingModule();
            copy.SetGameAndStrategies(theGame, theStrategies, GameModuleName, GameModuleNamesThisModuleReliesOn, GameModuleNumbersThisModuleReliesOn, GameModuleSettings);
            SetGameModuleFields(copy);
            theGameModuleProgress = OfferThresholdBargainingModuleProgress.GetRecycledOrAllocate();
            theGameModule = copy;
        }

        public virtual object GenerateSetting(string options)
        {
            List<Decision> decisions = new List<Decision>();

            decisions.Add(GetDecision("POffer", "po", true, true));
            decisions.Add(GetDecision("DOffer", "do", false, false));
            decisions.Add(GetDecision("PThreshold", "pt", false, true));
            decisions.Add(GetDecision("DThreshold", "dt", false, false));

            return new OfferThresholdBargainingModule()
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

        private static Decision GetDecision(string name, string abbreviation, bool isFirstInRound, bool isPDecision)
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
                    LowerBound = 0.0,
                    UpperBound = 1.0
                },
                Bipolar = false,
                StrategyGraphInfos = new List<StrategyGraphInfo>(),
                HighestIsBest = true,
                MaxEvolveRepetitions = 99999,
                PhaseOutDefaultBehaviorOverRepetitions = 0,
                PreservePreviousVersionWhenOptimizing = true,
                EvolveThisDecisionEvenWhenSkippingByDefault = false,
                AverageInPreviousVersion = true,
                TestInputs = null, // new List<double>() { 0.5, 0.07, 0.07 }, 
                TestOutputs = null // new List<double>() { 0.1, 0.2, 0.3, 0.4, 0.5 }
            };
        }
    }
}
