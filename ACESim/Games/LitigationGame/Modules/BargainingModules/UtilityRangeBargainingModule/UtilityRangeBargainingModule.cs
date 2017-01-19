using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.ComponentModel.Composition;
using System.Diagnostics;

namespace ACESim
{
    [Export(typeof(ICodeBasedSettingGenerator))] // include the export and exportmetadata attributes if we implement GenerateSetting, setting up the GameModule by code instead of in the settings file.
    [ExportMetadata("CodeGeneratorName", "UtilityRangeBargainingModule")]
    [Serializable]
    public class UtilityRangeBargainingModule : BargainingModule, ICodeBasedSettingGenerator
    {

        #region Data access and enum

        public UtilityRangeBargainingModuleProgress UtilityRangeBargainingProgress { get { return (UtilityRangeBargainingModuleProgress)GameModuleProgress; } set { GameModuleProgress = value; } }

        public UtilityRangeBargainingInputs UtilityRangeBargainingInputs { get { return (UtilityRangeBargainingInputs)GameModuleInputs; } }

        public BargainingAggressivenessOverrideModule BargainingAggressivenessOverrideModule { get { return GetGameModuleThisModuleReliesOn(0) as BargainingAggressivenessOverrideModule; } }

        public UtilityRangeBargainingModuleSettings UtilityRangeBargainingModuleSettings { get { return (UtilityRangeBargainingModuleSettings)GameModuleSettings; } }

        public enum UtilityRangeBargainingDecisions
        { // Note: We can switch order of plaintiffdecision and defendantdecision here.
            PlaintiffProjectionOfPlaintiffDeltaUtilityIfBargainingIsBlocked,
            PlaintiffProjectionOfDefendantDeltaUtilityIfBargainingIsBlocked,
            DefendantProjectionOfPlaintiffDeltaUtilityIfBargainingIsBlocked,
            DefendantProjectionOfDefendantDeltaUtilityIfBargainingIsBlocked,
            PlaintiffProjectionOfEquivalentPlaintiffDeltaWealthIfBargainingIsBlocked,
            PlaintiffProjectionOfEquivalentDefendantDeltaWealthIfBargainingIsBlocked,
            DefendantProjectionOfEquivalentPlaintiffDeltaWealthIfBargainingIsBlocked,
            DefendantProjectionOfEquivalentDefendantDeltaWealthIfBargainingIsBlocked,
            PProjectionPAbsErrorInSettingThreatPoint, // as with the wealth projections, this is relative to the damages claim
            PProjectionDAbsErrorInSettingThreatPoint,
            DProjectionPAbsErrorInSettingThreatPoint,
            DProjectionDAbsErrorInSettingThreatPoint,
        }
        const int numberDecisionsBeforeLastToFindLastUtilityDeltaDecision = 8;

        #endregion

        #region Prebargaining forecasting

        public override void OtherPrebargainingSetup()
        {
            if (UtilityRangeBargainingProgress.CurrentBargainingSubclaimNumber == 1)
            {
                int numMiniRounds = UtilityRangeBargainingModuleSettings.DivideBargainingIntoMinirounds ? UtilityRangeBargainingModuleSettings.NumBargainingMinirounds : 1;
                RegisterExtraInvestigationRound(1.0 / (double)numMiniRounds, numMiniRounds == 1); // only update information here if there is only 1 mini round. Otherwise, it gets done in DetermineSettlement the last time we register an investigative round
                base.OtherPrebargainingSetup();
                RecordCurrentWealth();
                DetermineWhetherToBlockSettlement();
            }
            else
                RecordCurrentWealth(); // we still need to record current wealth, in part b/c we only preserve information when the current action group is currently evolving
        }

        private void DetermineWhetherToBlockSettlement()
        {
            var currentlyEvolvingActionGroup = Game.CurrentlyEvolvingActionGroup;
            bool mustBlockSettlementToMakeOptimizationWork = Game.CurrentlyEvolving &&
                (
                currentlyEvolvingActionGroup == Game.CurrentActionGroup
                ||
                currentlyEvolvingActionGroup.Name == "ProbabilityPWinsForecastingModule"
                ||
                (
                    Game.CurrentlyEvolvingModule is DropOrDefaultModule &&
                    Game.CurrentlyEvolvingDecisionIndex != null &&
                    AllStrategies[(int)Game.CurrentlyEvolvingDecisionIndex].CyclesStrategyDevelopment == 0 && // drop or default module hasn't previously evolved
                    Game.CurrentlyEvolvingDecisionIndex > Game.MostRecentDecisionIndex // drop or default module occurs LATER in game than this module (note that this takes place during an action point that is after the decisions and doesn't have its own decision index)
                )
                );
            bool mustBlockSettlementBecauseHasntEvolvedYet =
                !AllStrategies[(int)Game.CurrentActionGroup.LastDecisionNumber - numberDecisionsBeforeLastToFindLastUtilityDeltaDecision].StrategyDevelopmentInitiated; // we look to see whether the last strategy before the wealth equivalence decisions, since those decisions are not re-evolved after the first bargaining round.
            bool mustBlockSettlement = mustBlockSettlementBecauseHasntEvolvedYet || mustBlockSettlementToMakeOptimizationWork;
            if (GameProgressLogger.LoggingOn && mustBlockSettlementToMakeOptimizationWork)
                GameProgressLogger.Log("Blocking settlement to make optimization work.");
            if (GameProgressLogger.LoggingOn && mustBlockSettlementBecauseHasntEvolvedYet)
                GameProgressLogger.Log("Blocking settlement because evolution has not occurred yet.");
            if (UtilityRangeBargainingProgress.SettlementBlockedThisBargainingRoundBecauseOfEvolution == null)
                UtilityRangeBargainingProgress.SettlementBlockedThisBargainingRoundBecauseOfEvolution = new List<bool>();
            UtilityRangeBargainingProgress.SettlementBlockedThisBargainingRoundBecauseOfEvolution.Add(mustBlockSettlement);
            if (mustBlockSettlement && UtilityRangeBargainingProgress.SettlementProgress != null) /* add || true to prevent settlement when running reports -- todo add this as an option */
            {
                UtilityRangeBargainingProgress.SettlementProgress.BlockSettlementDuringOptimization = true;
            }
        }

        private void RecordCurrentWealth()
        {
            UtilityRangeBargainingProgress.PDecisionmakerWealthBeforeCurrentBargainingRound = GetCurrentWealth(true);
            UtilityRangeBargainingProgress.DWealthBeforeCurrentBargainingRound = GetCurrentWealth(false);
            if (Game.CurrentlyEvolvingModule == this && Game.CurrentlyEvolvingActionGroup == Game.CurrentActionGroup)
            {
                UtilityRangeBargainingProgress.PDecisionmakerWealthAtTimeOfDecisionBeingEvolved = UtilityRangeBargainingProgress.PDecisionmakerWealthBeforeCurrentBargainingRound;
                UtilityRangeBargainingProgress.DWealthAtTimeOfDecisionBeingEvolved = UtilityRangeBargainingProgress.DWealthBeforeCurrentBargainingRound;
            }
        }

        bool allowDirectCalculationOfEquivalentWealth = true; // if false, then equivalent wealth must be evolved, even when each player knows its and its opponent's utility function
        bool subsequentBargainingRoundsAreRelative = true; // if true, then the projections for bargaining rounds 2+ are relative to the first bargaining round, so we first calculate the first bargaining round projection given current information (which might have changed since then) and then we calculate the relative projection before adding it together. A possible improvement would be to make each bargaining round relative to the immediately previous one.

        public override void MakeDecisionBasedOnBargainingInputs()
        {

            ActionGroup currentlyEvolvingActionGroup = Game.CurrentlyEvolvingActionGroup;
            ActionGroup currentlyExecutingActionGroup = Game.CurrentActionGroup; // i.e., the action group of which the current decision from this module is a part
            bool currentlyEvolvingThisActionGroup = Game.CurrentlyEvolvingActionGroup == Game.CurrentActionGroup;
            bool currentlyEvolvingProjectionInThisModuleOrImmediatelyPrecedingUpdatesSoMustBlockBargain = currentlyEvolvingThisActionGroup || (currentlyEvolvingActionGroup != null && currentlyEvolvingActionGroup.CorrespondingCounterfactualActionGroup == currentlyExecutingActionGroup);
            double randomMultiplier = 1.0;
            bool useFakeUtilityProjections = false; // we do this when evolving the equivalent wealth functions because we want to make sure they are defined over a broader range of utility
            if (Game.CurrentlyEvolvingActionGroup == Game.CurrentActionGroup && Game.CurrentlyEvolvingDecisionIndexWithinActionGroup >= (int)UtilityRangeBargainingDecisions.PlaintiffProjectionOfEquivalentPlaintiffDeltaWealthIfBargainingIsBlocked)
            {
                useFakeUtilityProjections = true;
                randomMultiplier = UtilityRangeBargainingInputs.RandomMultiplierForEvolvingEquivalentWealth;
            }
            int? currentDecisionIndexInFirstRepetitionOfModule = null;
            if (UtilityRangeBargainingProgress.FirstActionGroupForThisBargainingRoundIfDifferent != null)
                currentDecisionIndexInFirstRepetitionOfModule = UtilityRangeBargainingProgress.FirstActionGroupForThisBargainingRoundIfDifferent.FirstDecisionNumber + (Game.CurrentDecisionIndex - Game.CurrentActionGroup.FirstDecisionNumber);

            int? decisionNumberWithinModule = Game.CurrentDecisionIndexWithinModule;

            if (decisionNumberWithinModule == (int)UtilityRangeBargainingDecisions.PlaintiffProjectionOfPlaintiffDeltaUtilityIfBargainingIsBlocked && Game.PreparationPhase)
            {
                UtilityRangeBargainingProgress.PUtilityProjectionDecisionNumber = Game.CurrentDecisionIndex;
                if (UtilityRangeBargainingProgress.BargainingRound == null)
                    UtilityRangeBargainingProgress.BargainingRound = 1;
                else if (UtilityRangeBargainingProgress.CurrentBargainingSubclaimNumber == 1)
                    UtilityRangeBargainingProgress.BargainingRound = (int)UtilityRangeBargainingProgress.BargainingRound + 1;
                SpecifyInputs(BargainingProgress.PDecisionInputs);
            }
            else if (decisionNumberWithinModule == (int)UtilityRangeBargainingDecisions.PlaintiffProjectionOfPlaintiffDeltaUtilityIfBargainingIsBlocked && !Game.PreparationPhase)
            {
                UtilityRangeBargainingProgress.MostRecentPlaintiffProjectionOfPUtilityDeltaIfBargainingIsBlocked = Calculate();
                if (subsequentBargainingRoundsAreRelative && currentDecisionIndexInFirstRepetitionOfModule != null)
                {
                    SpecifyInputs(BargainingProgress.PDecisionInputs);
                    UtilityRangeBargainingProgress.MostRecentPlaintiffProjectionOfPUtilityDeltaIfBargainingIsBlocked += Calculate((int)currentDecisionIndexInFirstRepetitionOfModule);
                }
            }
            else if (decisionNumberWithinModule == (int)UtilityRangeBargainingDecisions.PlaintiffProjectionOfDefendantDeltaUtilityIfBargainingIsBlocked && Game.PreparationPhase)
            {
                SpecifyInputs(BargainingProgress.PDecisionInputs);
            }
            else if (decisionNumberWithinModule == (int)UtilityRangeBargainingDecisions.PlaintiffProjectionOfDefendantDeltaUtilityIfBargainingIsBlocked && !Game.PreparationPhase)
            {
                UtilityRangeBargainingProgress.MostRecentPlaintiffProjectionOfDUtilityDeltaIfBargainingIsBlocked = Calculate();

                if (subsequentBargainingRoundsAreRelative && currentDecisionIndexInFirstRepetitionOfModule != null)
                {
                    SpecifyInputs(BargainingProgress.PDecisionInputs);
                    UtilityRangeBargainingProgress.MostRecentPlaintiffProjectionOfDUtilityDeltaIfBargainingIsBlocked += Calculate((int)currentDecisionIndexInFirstRepetitionOfModule);
                }
            }
            else if (decisionNumberWithinModule == (int)UtilityRangeBargainingDecisions.DefendantProjectionOfPlaintiffDeltaUtilityIfBargainingIsBlocked && Game.PreparationPhase)
            {
                SpecifyInputs(BargainingProgress.DDecisionInputs);
            }
            else if (decisionNumberWithinModule == (int)UtilityRangeBargainingDecisions.DefendantProjectionOfPlaintiffDeltaUtilityIfBargainingIsBlocked && !Game.PreparationPhase)
            {
                UtilityRangeBargainingProgress.MostRecentDefendantProjectionOfPUtilityDeltaIfBargainingIsBlocked = Calculate();
                if (subsequentBargainingRoundsAreRelative && currentDecisionIndexInFirstRepetitionOfModule != null)
                {
                    SpecifyInputs(BargainingProgress.DDecisionInputs);
                    UtilityRangeBargainingProgress.MostRecentDefendantProjectionOfPUtilityDeltaIfBargainingIsBlocked += Calculate((int)currentDecisionIndexInFirstRepetitionOfModule);
                }
            }
            else if (decisionNumberWithinModule == (int)UtilityRangeBargainingDecisions.DefendantProjectionOfDefendantDeltaUtilityIfBargainingIsBlocked && Game.PreparationPhase)
            {
                UtilityRangeBargainingProgress.DUtilityProjectionDecisionNumber = Game.CurrentDecisionIndex;
                SpecifyInputs(BargainingProgress.DDecisionInputs);
            }
            else if (decisionNumberWithinModule == (int)UtilityRangeBargainingDecisions.DefendantProjectionOfDefendantDeltaUtilityIfBargainingIsBlocked && !Game.PreparationPhase)
            {
                UtilityRangeBargainingProgress.MostRecentDefendantProjectionOfDUtilityDeltaIfBargainingIsBlocked = Calculate();
                if (subsequentBargainingRoundsAreRelative && currentDecisionIndexInFirstRepetitionOfModule != null)
                {
                    SpecifyInputs(BargainingProgress.DDecisionInputs);
                    UtilityRangeBargainingProgress.MostRecentDefendantProjectionOfDUtilityDeltaIfBargainingIsBlocked += Calculate((int)currentDecisionIndexInFirstRepetitionOfModule);
                }
            }
            else if (decisionNumberWithinModule == (int)UtilityRangeBargainingDecisions.PlaintiffProjectionOfEquivalentPlaintiffDeltaWealthIfBargainingIsBlocked && Game.PreparationPhase)
            {
                UtilityRangeBargainingProgress.PWealthTranslationDecisionNumber = Game.CurrentDecisionIndex;
                // randomly change projected utility when evolving this decision to make sure that we get a broader range of possible inputs than we happen to receive; this will help with boundary conditions
                //if (Game.CurrentlyEvolvingDecisionIndexWithinActionGroup >= (int)UtilityRangeBargainingDecisions.PlaintiffProjectionOfEquivalentPlaintiffDeltaWealthIfBargainingIsBlocked) // note that all four decisions may evolve at same time
                //{
                //    UtilityRangeBargainingProgress.MostRecentPlaintiffProjectionOfPUtilityDeltaIfBargainingIsBlocked *= UtilityRangeBargainingInputs.RandomMultiplierForEvolvingEquivalentWealth;
                //    UtilityRangeBargainingProgress.MostRecentPlaintiffProjectionOfDUtilityDeltaIfBargainingIsBlocked *= UtilityRangeBargainingInputs.RandomMultiplierForEvolvingEquivalentWealth;
                //    UtilityRangeBargainingProgress.MostRecentDefendantProjectionOfPUtilityDeltaIfBargainingIsBlocked *= UtilityRangeBargainingInputs.RandomMultiplierForEvolvingEquivalentWealth;
                //    UtilityRangeBargainingProgress.MostRecentDefendantProjectionOfDUtilityDeltaIfBargainingIsBlocked *= UtilityRangeBargainingInputs.RandomMultiplierForEvolvingEquivalentWealth;
                //}
                SpecifyInputs(new List<double> { (double)UtilityRangeBargainingProgress.MostRecentPlaintiffProjectionOfPUtilityDeltaIfBargainingIsBlocked * randomMultiplier });
                if (useFakeUtilityProjections)
                    UtilityRangeBargainingProgress.FakePProjectionOfPUtilityDelta = GameModuleProgress.TemporaryInputsStorage[0];
            }
            else if (decisionNumberWithinModule == (int)UtilityRangeBargainingDecisions.PlaintiffProjectionOfEquivalentPlaintiffDeltaWealthIfBargainingIsBlocked && !Game.PreparationPhase)
            {
                if (UtilityRangeBargainingModuleSettings.CanDirectlyCalculateEquivalentWealth && allowDirectCalculationOfEquivalentWealth)
                {
                    UtilityRangeBargainingProgress.MostRecentPlaintiffProjectionOfPEquivalentWealthDeltaIfBargainingIsBlocked = GetEquivalentWealthDeltaRelativeToCurrentWealthAsProportionOfDamagesClaimGivenDeltaUtilityProjectionWhereUtilityParametersAreKnown(true, (double)UtilityRangeBargainingProgress.MostRecentPlaintiffProjectionOfPUtilityDeltaIfBargainingIsBlocked);
                }
                else
                {
                    UtilityRangeBargainingProgress.MostRecentPlaintiffProjectionOfPEquivalentWealthDeltaIfBargainingIsBlocked = Calculate() / LitigationGame.DisputeGeneratorModule.DGProgress.DamagesClaim;
                }
                if (UtilityRangeBargainingProgress.BargainingRound == 1)
                    UtilityRangeBargainingProgress.NEVForP = UtilityRangeBargainingProgress.MostRecentPlaintiffProjectionOfPEquivalentWealthDeltaIfBargainingIsBlocked < 0.0; // Note: This isn't quite right, given that we leave open the possibility that the lawsuit will be dropped later (unless we have disabled this). That is, even if bargaining is blocked, the lawsuit could still be dropped at the end of the process. To figure out if a suit is NEV, we need to figure out the expected value of the lawsuit if bargaining is blocked AND the lawsuit could not be dropped. But we would need to evolve an extra decision to do that. One possibility might be to evolve this very early in the cycle (when there is no settlement and no dropping of any cases), but based on a projection early in the game.
            }
            else if (decisionNumberWithinModule == (int)UtilityRangeBargainingDecisions.PlaintiffProjectionOfEquivalentDefendantDeltaWealthIfBargainingIsBlocked && Game.PreparationPhase)
            {
                SpecifyInputs(new List<double>() { (double)UtilityRangeBargainingProgress.MostRecentPlaintiffProjectionOfDUtilityDeltaIfBargainingIsBlocked * randomMultiplier });
                if (useFakeUtilityProjections)
                    UtilityRangeBargainingProgress.FakePProjectionOfDUtilityDelta = GameModuleProgress.TemporaryInputsStorage[0];
            }
            else if (decisionNumberWithinModule == (int)UtilityRangeBargainingDecisions.PlaintiffProjectionOfEquivalentDefendantDeltaWealthIfBargainingIsBlocked && !Game.PreparationPhase)
            {
                if (UtilityRangeBargainingModuleSettings.CanDirectlyCalculateEquivalentWealth && allowDirectCalculationOfEquivalentWealth)
                {
                    UtilityRangeBargainingProgress.MostRecentPlaintiffProjectionOfDEquivalentWealthDeltaIfBargainingIsBlocked = GetEquivalentWealthDeltaRelativeToCurrentWealthAsProportionOfDamagesClaimGivenDeltaUtilityProjectionWhereUtilityParametersAreKnown(false, (double)UtilityRangeBargainingProgress.MostRecentPlaintiffProjectionOfDUtilityDeltaIfBargainingIsBlocked);
                }
                else
                {
                    UtilityRangeBargainingProgress.MostRecentPlaintiffProjectionOfDEquivalentWealthDeltaIfBargainingIsBlocked = Calculate() / LitigationGame.DisputeGeneratorModule.DGProgress.DamagesClaim;
                }
            }
            else if (decisionNumberWithinModule == (int)UtilityRangeBargainingDecisions.DefendantProjectionOfEquivalentPlaintiffDeltaWealthIfBargainingIsBlocked && Game.PreparationPhase)
            {
                SpecifyInputs(new List<double> { (double)UtilityRangeBargainingProgress.MostRecentDefendantProjectionOfPUtilityDeltaIfBargainingIsBlocked * randomMultiplier });
                if (useFakeUtilityProjections)
                    UtilityRangeBargainingProgress.FakeDProjectionOfPUtilityDelta = GameModuleProgress.TemporaryInputsStorage[0];
            }
            else if (decisionNumberWithinModule == (int)UtilityRangeBargainingDecisions.DefendantProjectionOfEquivalentPlaintiffDeltaWealthIfBargainingIsBlocked && !Game.PreparationPhase)
            {
                if (UtilityRangeBargainingModuleSettings.CanDirectlyCalculateEquivalentWealth && allowDirectCalculationOfEquivalentWealth)
                {
                    UtilityRangeBargainingProgress.MostRecentDefendantProjectionOfPEquivalentWealthDeltaIfBargainingIsBlocked = GetEquivalentWealthDeltaRelativeToCurrentWealthAsProportionOfDamagesClaimGivenDeltaUtilityProjectionWhereUtilityParametersAreKnown(true, (double)UtilityRangeBargainingProgress.MostRecentDefendantProjectionOfPUtilityDeltaIfBargainingIsBlocked);
                }
                else
                {
                    UtilityRangeBargainingProgress.MostRecentDefendantProjectionOfPEquivalentWealthDeltaIfBargainingIsBlocked = Calculate() / LitigationGame.DisputeGeneratorModule.DGProgress.DamagesClaim;
                }
            }
            else if (decisionNumberWithinModule == (int)UtilityRangeBargainingDecisions.DefendantProjectionOfEquivalentDefendantDeltaWealthIfBargainingIsBlocked && Game.PreparationPhase)
            {
                UtilityRangeBargainingProgress.DWealthTranslationDecisionNumber = Game.CurrentDecisionIndex;
                SpecifyInputs(new List<double>() { (double)UtilityRangeBargainingProgress.MostRecentDefendantProjectionOfDUtilityDeltaIfBargainingIsBlocked * randomMultiplier });
                if (useFakeUtilityProjections)
                    UtilityRangeBargainingProgress.FakeDProjectionOfDUtilityDelta = GameModuleProgress.TemporaryInputsStorage[0];
            }
            else if (decisionNumberWithinModule == (int)UtilityRangeBargainingDecisions.DefendantProjectionOfEquivalentDefendantDeltaWealthIfBargainingIsBlocked && !Game.PreparationPhase)
            {
                if (UtilityRangeBargainingModuleSettings.CanDirectlyCalculateEquivalentWealth && allowDirectCalculationOfEquivalentWealth)
                {
                    UtilityRangeBargainingProgress.MostRecentDefendantProjectionOfDEquivalentWealthDeltaIfBargainingIsBlocked = GetEquivalentWealthDeltaRelativeToCurrentWealthAsProportionOfDamagesClaimGivenDeltaUtilityProjectionWhereUtilityParametersAreKnown(false, (double)UtilityRangeBargainingProgress.MostRecentDefendantProjectionOfDUtilityDeltaIfBargainingIsBlocked);
                }
                else
                {
                    UtilityRangeBargainingProgress.MostRecentDefendantProjectionOfDEquivalentWealthDeltaIfBargainingIsBlocked = Calculate() / LitigationGame.DisputeGeneratorModule.DGProgress.DamagesClaim;
                }
                
            }
            else if (decisionNumberWithinModule == (int)UtilityRangeBargainingDecisions.PProjectionPAbsErrorInSettingThreatPoint && Game.PreparationPhase)
            {
                SpecifyInputs(BargainingProgress.PDecisionInputs);
            }
            else if (decisionNumberWithinModule == (int)UtilityRangeBargainingDecisions.PProjectionPAbsErrorInSettingThreatPoint && !Game.PreparationPhase)
            {
                UtilityRangeBargainingProgress.MostRecentPProjectionPAbsErrorInSettingThreatPoint = Calculate();
            }
            else if (decisionNumberWithinModule == (int)UtilityRangeBargainingDecisions.PProjectionDAbsErrorInSettingThreatPoint && Game.PreparationPhase)
            {
                SpecifyInputs(BargainingProgress.PDecisionInputs);
            }
            else if (decisionNumberWithinModule == (int)UtilityRangeBargainingDecisions.PProjectionDAbsErrorInSettingThreatPoint && !Game.PreparationPhase)
            {
                UtilityRangeBargainingProgress.MostRecentPProjectionDAbsErrorInSettingThreatPoint = Calculate();
            }
            else if (decisionNumberWithinModule == (int)UtilityRangeBargainingDecisions.DProjectionPAbsErrorInSettingThreatPoint && Game.PreparationPhase)
            {
                SpecifyInputs(BargainingProgress.DDecisionInputs);
            }
            else if (decisionNumberWithinModule == (int)UtilityRangeBargainingDecisions.DProjectionPAbsErrorInSettingThreatPoint && !Game.PreparationPhase)
            {
                UtilityRangeBargainingProgress.MostRecentDProjectionPAbsErrorInSettingThreatPoint = Calculate();
            }
            else if (decisionNumberWithinModule == (int)UtilityRangeBargainingDecisions.DProjectionDAbsErrorInSettingThreatPoint && Game.PreparationPhase)
            {
                SpecifyInputs(BargainingProgress.DDecisionInputs);
            }
            else if (decisionNumberWithinModule == (int)UtilityRangeBargainingDecisions.DProjectionDAbsErrorInSettingThreatPoint && !Game.PreparationPhase)
            {
                UtilityRangeBargainingProgress.MostRecentDProjectionDAbsErrorInSettingThreatPoint = Calculate();

                if (UtilityRangeBargainingProgress.BargainingRound == 1)
                    UtilityRangeBargainingProgress.NEVForD = UtilityRangeBargainingProgress.MostRecentDefendantProjectionOfDEquivalentWealthDeltaIfBargainingIsBlocked < -1.0;

                if (currentlyEvolvingThisActionGroup && Game.CurrentlyEvolvingDecisionIndexWithinActionGroup >= (int)UtilityRangeBargainingDecisions.PProjectionPAbsErrorInSettingThreatPoint && Game.CurrentlyEvolvingDecisionIndexWithinActionGroup <= (int)UtilityRangeBargainingDecisions.DProjectionDAbsErrorInSettingThreatPoint)
                    DetermineErrorsInSettingThreatpoints();

                if (GameProgressLogger.LoggingOn)
                {
                    GameProgressLogger.Log("Bargaining round " + UtilityRangeBargainingProgress.BargainingRound + ": "
                        + "\n\t MostRecentPlaintiffProjectionOfPUtilityDeltaIfBargainingIsBlocked " + UtilityRangeBargainingProgress.MostRecentPlaintiffProjectionOfPUtilityDeltaIfBargainingIsBlocked
                        + "\n\t MostRecentPlaintiffProjectionOfDUtilityDeltaIfBargainingIsBlocked " + UtilityRangeBargainingProgress.MostRecentPlaintiffProjectionOfDUtilityDeltaIfBargainingIsBlocked
                        + "\n\t MostRecentDefendantProjectionOfPUtilityDeltaIfBargainingIsBlocked " + UtilityRangeBargainingProgress.MostRecentDefendantProjectionOfPUtilityDeltaIfBargainingIsBlocked
                        + "\n\t MostRecentDefendantProjectionOfDUtilityDeltaIfBargainingIsBlocked " + UtilityRangeBargainingProgress.MostRecentDefendantProjectionOfDUtilityDeltaIfBargainingIsBlocked
                        + "\n\t MostRecentPlaintiffProjectionOfPEquivalentWealthDeltaIfBargainingIsBlocked " + UtilityRangeBargainingProgress.MostRecentPlaintiffProjectionOfPEquivalentWealthDeltaIfBargainingIsBlocked
                        + "\n\t MostRecentPlaintiffProjectionOfDEquivalentWealthDeltaIfBargainingIsBlocked " + UtilityRangeBargainingProgress.MostRecentPlaintiffProjectionOfDEquivalentWealthDeltaIfBargainingIsBlocked
                        + "\n\t MostRecentDefendantProjectionOfPEquivalentWealthDeltaIfBargainingIsBlocked " + UtilityRangeBargainingProgress.MostRecentDefendantProjectionOfPEquivalentWealthDeltaIfBargainingIsBlocked
                        + "\n\t MostRecentDefendantProjectionOfDEquivalentWealthDeltaIfBargainingIsBlocked " + UtilityRangeBargainingProgress.MostRecentDefendantProjectionOfDEquivalentWealthDeltaIfBargainingIsBlocked
                        );
                }
            }
        }

        #endregion

        #region Settlement

        private void GetAggressivenessOverrides()
        {
            if (LitigationGame.LGP.PAggressivenessOverride == null || LitigationGame.LGP.DAggressivenessOverride == null)
                throw new Exception("Aggressiveness override not set. Must run at least PresetAggressivenessOverrideModule.");

            UtilityRangeBargainingProgress.PAggressivenessOverrideModified = (double)LitigationGame.LGP.PAggressivenessOverride;
            UtilityRangeBargainingProgress.DAggressivenessOverrideModified = (double)LitigationGame.LGP.DAggressivenessOverride;
            if (UtilityRangeBargainingModuleSettings.DivideBargainingIntoMinirounds)
            {
                UtilityRangeBargainingProgress.PAggressivenessOverrideModifiedFinal = (double)LitigationGame.LGP.PAggressivenessOverrideFinal;
                UtilityRangeBargainingProgress.DAggressivenessOverrideModifiedFinal = (double)LitigationGame.LGP.DAggressivenessOverrideFinal;
            }

            if (UtilityRangeBargainingInputs.AggressivenessContagion != 0)
            {
                if (UtilityRangeBargainingModuleSettings.InterpretAggressivenessRelativeToOwnUncertainty)
                    throw new NotImplementedException("Cannot use aggressiveness contagion yet when interpreting aggressiveness relative to own uncertainty.");

                double newPValue = (double)(UtilityRangeBargainingProgress.PAggressivenessOverrideModified * (1.0 - UtilityRangeBargainingInputs.AggressivenessContagion) + UtilityRangeBargainingProgress.DAggressivenessOverrideModified * UtilityRangeBargainingInputs.AggressivenessContagion);
                double newDValue = (double)(UtilityRangeBargainingProgress.DAggressivenessOverrideModified * (1.0 - UtilityRangeBargainingInputs.AggressivenessContagion) + UtilityRangeBargainingProgress.PAggressivenessOverrideModified * UtilityRangeBargainingInputs.AggressivenessContagion);
                UtilityRangeBargainingProgress.PAggressivenessOverrideModified = newPValue;
                UtilityRangeBargainingProgress.DAggressivenessOverrideModified = newDValue;


                if (UtilityRangeBargainingModuleSettings.DivideBargainingIntoMinirounds)
                {
                    double newPValueFinal = (double)(UtilityRangeBargainingProgress.PAggressivenessOverrideModifiedFinal * (1.0 - UtilityRangeBargainingInputs.AggressivenessContagion) + UtilityRangeBargainingProgress.DAggressivenessOverrideModifiedFinal * UtilityRangeBargainingInputs.AggressivenessContagion);
                    double newDValueFinal = (double)(UtilityRangeBargainingProgress.DAggressivenessOverrideModifiedFinal * (1.0 - UtilityRangeBargainingInputs.AggressivenessContagion) + UtilityRangeBargainingProgress.PAggressivenessOverrideModifiedFinal * UtilityRangeBargainingInputs.AggressivenessContagion);
                    UtilityRangeBargainingProgress.PAggressivenessOverrideModifiedFinal = newPValueFinal;
                    UtilityRangeBargainingProgress.DAggressivenessOverrideModifiedFinal = newDValueFinal;
                }
            }
        }

        private void DetermineErrorsInSettingThreatpoints()
        {
            Tuple<double, double> pAndDCorrectThreatpoints = GetEquivalentWealthDeltaProjectionIfPartiesHadCorrectInformation();
            UtilityRangeBargainingProgress.MostRecentPAbsErrorInSettingThreatPoint = Math.Abs((double)UtilityRangeBargainingProgress.MostRecentPlaintiffProjectionOfPEquivalentWealthDeltaIfBargainingIsBlocked /* plaintiff threat point */ - (double)pAndDCorrectThreatpoints.Item1 /* threat point if P had correct information */);
            UtilityRangeBargainingProgress.MostRecentDAbsErrorInSettingThreatPoint = Math.Abs((double)UtilityRangeBargainingProgress.MostRecentDefendantProjectionOfDEquivalentWealthDeltaIfBargainingIsBlocked /* defendant threat point */ - (double)pAndDCorrectThreatpoints.Item2 /* threat point if D had correct information */);     
        }

        public override void DetermineSettlement()
        {
            GetAggressivenessOverrides();

            // first, we will figure out each player's perceived bargaining range. This is the range of potential offers that would leave both players better off relative to their expectations, given the current information. then we figure out the minimum offer that each party will make, divided by the damages amount.
            double pPerceptionMiddleOfBargainingRange, dPerceptionMiddleOfBargainingRange;
            double pPerceivedBargainingRange, dPerceivedBargainingRange;
            bool usingSubclaims = UtilityRangeBargainingModuleSettings.NegotiateSeparatelyOverProbabilityAndMagnitude; // for now, this means a probability subclaim and a magnitude subclaim
            double pOfferOrSuboffer, dOfferOrSuboffer; // a suboffer is an offer as to a subclaim
            bool mustBlockSettlement = UtilityRangeBargainingProgress.SettlementBlockedThisBargainingRoundBecauseOfEvolution.Last();
            int numMiniRounds = UtilityRangeBargainingModuleSettings.DivideBargainingIntoMinirounds ? UtilityRangeBargainingModuleSettings.NumBargainingMinirounds : 1;
            for (int miniRound = 1; miniRound <= numMiniRounds; miniRound++)
            {
                CalculatePlaintiffOffer(out pPerceptionMiddleOfBargainingRange, out pPerceivedBargainingRange, out pOfferOrSuboffer, miniRound, numMiniRounds);
                CalculateDefendantOffer(out dPerceptionMiddleOfBargainingRange, out dPerceivedBargainingRange, out dOfferOrSuboffer, miniRound, numMiniRounds);
                double mostRecentBargainingDistance = (dOfferOrSuboffer - pOfferOrSuboffer);
                bool isLastSubclaim = true;
                if (!mustBlockSettlement)
                {
                    if (usingSubclaims)
                    {
                        isLastSubclaim = BargainingProgress.CurrentBargainingSubclaimNumber == 2;

                        //if (BargainingProgress.CurrentBargainingSubclaimNumber == 1 && BargainingProgress.CurrentlyEvolvingBargainingSubclaimNumber == 2 && !AllStrategies[(int) ((DecisionPoint)Game.CurrentActionGroup.ActionPoints[1]).DecisionNumber].StrategyDevelopmentInitiated)
                        //{ // since we start by evolving subclaim 2, we need to have an agreement on subclaim 1 in the first repetition, so we override the above. This will allow us to figure out the optimal level of aggressiveness in subclaim 2. Note that one effect of this is that if we are doing probability and then damages, then we will be evolving damages first, and if that is fixed, then there is nothing really to negotiate over (but the parties may use this as an opportunity to prevent an agreement).
                        //    pOfferOrSuboffer = 0.5;
                        //    dOfferOrSuboffer = 0.5;
                        //    mostRecentBargainingDistance = 0;
                        //}
                        // NOTE:
                        // IF USING SUBCLAIMS, THE APPROACH OF FIGURING OUT BARGAINING RANGES DOES NOT WORK, B/C WE CAN'T PAY ATTENTION TO THE BARGAINING RANGE WHEN WE ARE SETTLING THE FIRST OF TWO CLAIMS. SO, INSTEAD
                        // WE MUST USE AGGRESSIVENESS VALUES AS THE OFFERS AND IGNORE THE UTLITY RANGES FOR THIS TO WORK. A MORE ELABORATE APPROACH WOULD INVOLVE FIGURING OUT
                        // WHAT THE PLAYERS' UTILITY WOULD BE IF THE SUBCLAIM DID SETTLE, AS A FUNCTION OF THE SETTLEMENT VALUE.
                        //DetermineSubclaimPartialSettlement(pPerceivedBargainingRange, dPerceivedBargainingRange, pOfferOrSuboffer, dOfferOrSuboffer, mostRecentBargainingDistance);
                        DetermineSubclaimPartialSettlement(pPerceivedBargainingRange, dPerceivedBargainingRange,
                            (double)UtilityRangeBargainingProgress.PAggressivenessOverrideModified,
                            1.0 - (double)UtilityRangeBargainingProgress.DAggressivenessOverrideModified, mostRecentBargainingDistance);
                    }
                    else
                    {


                                DetermineGlobalSettlement(pPerceivedBargainingRange, dPerceivedBargainingRange, pOfferOrSuboffer, dOfferOrSuboffer, mostRecentBargainingDistance);
                    }
                }
                if (isLastSubclaim)
                {
                    bool isLastMiniRound = miniRound == numMiniRounds || (mostRecentBargainingDistance > 0 && !mustBlockSettlement);
                    if (isLastMiniRound)
                    {
                        DetermineSettlementFinalSteps();
                    }
                    else
                    {
                        // since we are registering BEFORE the mini round (including in OtherPrebargainingSetup), we don't register the last miniround
                        // meanwhile, the last time we are doing this, we must give the parties an opportunity to update their information (e.g., about p's probability of winning)
                        RegisterExtraInvestigationRound(1.0 / (double)numMiniRounds, miniRound == numMiniRounds - 1);
                    }
                }

                if (mostRecentBargainingDistance > 0 && !mustBlockSettlement)
                    break;
            }
        }

        private void DetermineSettlementFinalSteps()
        {
            if (UtilityRangeBargainingProgress.SettlementProgress != null && UtilityRangeBargainingProgress.SettlementProgress.CompleteSettlementReached())
                ProcessSettlement();
            else
            {
                UtilityRangeBargainingProgress.SettlementExistsAfterFirstBargainingRound = false;
                UtilityRangeBargainingProgress.SettlementFailedAfterBargainingRoundNum = (int)UtilityRangeBargainingProgress.BargainingRound;
            }
        }

        private void DetermineGlobalSettlement(double pPerceivedBargainingRange, double dPerceivedBargainingRange, double pOfferOrSuboffer, double dOfferOrSuboffer, double mostRecentBargainingDistance)
        {
            bool fullSettlementExists = mostRecentBargainingDistance > 0;

            if (GameProgressLogger.LoggingOn)
                GameProgressLogger.Log("Settlement " + (fullSettlementExists ? "exists" : "does not exist") + ": pOffer " + pOfferOrSuboffer + " dOffer: " + dOfferOrSuboffer + " bargain distance: " + mostRecentBargainingDistance);
            if (fullSettlementExists)
            {
                double midpointOfOffers = (pOfferOrSuboffer + dOfferOrSuboffer) / 2.0;
                UtilityRangeBargainingProgress.SettlementProgress = new GlobalSettlementProgress() { OriginalDamagesClaim = LitigationGame.DisputeGeneratorModule.DGProgress.DamagesClaim, AgreedUponPaymentAsProportion = midpointOfOffers, GlobalSettlementAchieved = true };
            }
            RecordDataConcerningBargaining(pPerceivedBargainingRange, dPerceivedBargainingRange, pOfferOrSuboffer, dOfferOrSuboffer, mostRecentBargainingDistance);
        }

        private void DetermineSubclaimPartialSettlement(double pPerceivedBargainingRange, double dPerceivedBargainingRange, double pOfferOrSuboffer, double dOfferOrSuboffer, double mostRecentBargainingDistance)
        {
            int subclaimNumber = (int)BargainingProgress.CurrentBargainingSubclaimNumber;
            bool partialSettlementExists;
            if (subclaimNumber == 2 && UtilityRangeBargainingProgress.SettlementProgress == null)
                partialSettlementExists = false; // we won't settle as to second part if first part isn't settled
            else
                partialSettlementExists = mostRecentBargainingDistance >= 0;
            if (partialSettlementExists)
            {
                double midpointOfOffers = (pOfferOrSuboffer + dOfferOrSuboffer) / 2.0;
                ProbabilityAndMagnitudeSettlementProgress settlementProgress = UtilityRangeBargainingProgress.SettlementProgress == null ? new ProbabilityAndMagnitudeSettlementProgress() { OriginalDamagesClaim = LitigationGame.DisputeGeneratorModule.DGProgress.DamagesClaim } : (ProbabilityAndMagnitudeSettlementProgress)UtilityRangeBargainingProgress.SettlementProgress;
                UtilityRangeBargainingProgress.SettlementProgress = settlementProgress;
                bool partialSettlementIsDamages = (subclaimNumber == 1 && !UtilityRangeBargainingModuleSettings.NegotiateProbabilityBeforeMagnitude) || (subclaimNumber == 2 && UtilityRangeBargainingModuleSettings.NegotiateProbabilityBeforeMagnitude);
                double? otherComponent = null;
                if (partialSettlementIsDamages)
                {
                    settlementProgress.AgreedUponDamagesProportion = midpointOfOffers;
                    otherComponent = settlementProgress.AgreedUponProbability;
                }
                else
                {
                    settlementProgress.TryToSetProposedAgreedUponProbability(midpointOfOffers, UtilityRangeBargainingModuleSettings);
                    if (settlementProgress.AgreedUponProbability == null)
                    {
                        if (subclaimNumber == 1)
                            UtilityRangeBargainingProgress.SettlementProgress = null;
                        partialSettlementExists = false; // the partial settlement was rejected (out-of-bounds)
                    }
                    otherComponent = settlementProgress.AgreedUponDamagesProportion;
                }
                if (subclaimNumber == 2 && partialSettlementExists)
                {
                    RecordDataConcerningBargaining(pPerceivedBargainingRange, dPerceivedBargainingRange, pOfferOrSuboffer * (double)otherComponent, dOfferOrSuboffer * (double)otherComponent, mostRecentBargainingDistance); // for offers (which may affect regret aversion and the shootout), we record the offer by multiplying the two components together
                }
            }
            else if (subclaimNumber == 1) // note that this will be the final subclaim processed since settlement failed
                RecordDataConcerningBargaining(pPerceivedBargainingRange, dPerceivedBargainingRange, pOfferOrSuboffer * 0.5, dOfferOrSuboffer * 0.5, mostRecentBargainingDistance); // this is a bit arbitrary, but for purposes of regret aversion, we record the offers by assuming the other half of the claim will settle right in the middle

        }


        private void RecordDataConcerningBargaining(double pPerceivedBargainingRange, double dPerceivedBargainingRange, double pOfferOrSuboffer, double dOfferOrSuboffer, double mostRecentBargainingDistance)
        {
            UtilityRangeBargainingProgress.MostRecentPlaintiffOffer = pOfferOrSuboffer;
            UtilityRangeBargainingProgress.MostRecentDefendantOffer = dOfferOrSuboffer;
            UtilityRangeBargainingProgress.MostRecentPPerceivedBargainingRange = pPerceivedBargainingRange;
            UtilityRangeBargainingProgress.MostRecentDPerceivedBargainingRange = dPerceivedBargainingRange;
            AddToProgressList(ref UtilityRangeBargainingProgress.POfferList, UtilityRangeBargainingProgress.MostRecentPlaintiffOffer);
            AddToProgressList(ref UtilityRangeBargainingProgress.PPerceivedBargainingRangeList, UtilityRangeBargainingProgress.MostRecentPPerceivedBargainingRange);
            AddToProgressList(ref UtilityRangeBargainingProgress.DOfferList, UtilityRangeBargainingProgress.MostRecentDefendantOffer);
            AddToProgressList(ref UtilityRangeBargainingProgress.DPerceivedBargainingRangeList, UtilityRangeBargainingProgress.MostRecentDPerceivedBargainingRange);
            UtilityRangeBargainingProgress.MostRecentBargainingDistance = UtilityRangeBargainingProgress.MostRecentDefendantOffer - UtilityRangeBargainingProgress.MostRecentPlaintiffOffer;
            AddToProgressList(ref UtilityRangeBargainingProgress.BargainingDistanceList, UtilityRangeBargainingProgress.MostRecentBargainingDistance);

            UtilityRangeBargainingProgress.PEstimatePResultMostRecentBargainingRound = (double)LitigationGame.BaseProbabilityForecastingModule.ForecastingProgress.PEstimatePResult;
            UtilityRangeBargainingProgress.DEstimatePResultMostRecentBargainingRound = (double)LitigationGame.BaseProbabilityForecastingModule.ForecastingProgress.DEstimatePResult;

            UtilityRangeBargainingProgress.ChangeInPlaintiffOffer = UtilityRangeBargainingProgress.MostRecentPlaintiffOffer - UtilityRangeBargainingProgress.POfferList[0];
            UtilityRangeBargainingProgress.ChangeInDefendantOffer = UtilityRangeBargainingProgress.MostRecentDefendantOffer - UtilityRangeBargainingProgress.DOfferList[0];
        }

        
        private void CalculatePlaintiffOffer(out double pPerceptionMiddleOfBargainingRange, out double pPerceivedBargainingRange, out double pOfferOrSuboffer, int miniRound, int numMiniRounds)
        {
            double pAggressiveness;
            if (numMiniRounds == 1)
                pAggressiveness = (double)UtilityRangeBargainingProgress.PAggressivenessOverrideModified;
            else
                pAggressiveness = (double)(UtilityRangeBargainingProgress.PAggressivenessOverrideModified + ((double)(((double)miniRound - 1.0) / ((double)numMiniRounds - 1.0)) * (UtilityRangeBargainingProgress.PAggressivenessOverrideModifiedFinal - UtilityRangeBargainingProgress.PAggressivenessOverrideModified)));


            pPerceivedBargainingRange = 0 - ((double)UtilityRangeBargainingProgress.MostRecentPlaintiffProjectionOfPEquivalentWealthDeltaIfBargainingIsBlocked + (double)UtilityRangeBargainingProgress.MostRecentPlaintiffProjectionOfDEquivalentWealthDeltaIfBargainingIsBlocked);
            bool pPerceivesUtilityRangeExists = pPerceivedBargainingRange > 0;
            pPerceptionMiddleOfBargainingRange = 0;
            if (UtilityRangeBargainingModuleSettings.InterpretAggressivenessRelativeToOwnUncertainty)
            {
                if (!pPerceivesUtilityRangeExists)
                    pPerceivedBargainingRange = 0.0;
                pPerceptionMiddleOfBargainingRange = ((double)UtilityRangeBargainingProgress.MostRecentPlaintiffProjectionOfPEquivalentWealthDeltaIfBargainingIsBlocked + 0.5 * (double)pPerceivedBargainingRange); // Equivalent to ((0 - (double)UtilityRangeBargainingProgress.MostRecentPlaintiffProjectionOfDEquivalentWealthDeltaIfBargainingIsBlocked) - 0.5 * (double)pPerceivedBargainingRange);
                pOfferOrSuboffer = pPerceptionMiddleOfBargainingRange + pAggressiveness * (double)UtilityRangeBargainingProgress.MostRecentPProjectionPAbsErrorInSettingThreatPoint;
            }
            else
            {
                if (pPerceivesUtilityRangeExists)
                {
                    // Note that the plaintiff offer is as a percentage of damages.
                    pOfferOrSuboffer = (double)(UtilityRangeBargainingProgress.MostRecentPlaintiffProjectionOfPEquivalentWealthDeltaIfBargainingIsBlocked + pAggressiveness * pPerceivedBargainingRange);
                    pPerceptionMiddleOfBargainingRange = ((double)UtilityRangeBargainingProgress.MostRecentPlaintiffProjectionOfPEquivalentWealthDeltaIfBargainingIsBlocked + 0.5 * (double)pPerceivedBargainingRange);
                }
                else
                {
                    // since there is no bargaining range at all, plaintiff will only agree to something unexpected, i.e. something that leaves plaintiff better off
                    if (UtilityRangeBargainingInputs.PartiesWillAcceptIncrementalBenefitIfNoUtilityRangePerceived)
                        pOfferOrSuboffer = pPerceptionMiddleOfBargainingRange = (double)UtilityRangeBargainingProgress.MostRecentPlaintiffProjectionOfPEquivalentWealthDeltaIfBargainingIsBlocked;
                    else
                        pOfferOrSuboffer = 1.0; // insist on maximum possible benefit -- note that we'll end up here when this module hasn't yet begun evolving for this bargaining repetition
                }
            }
            pOfferOrSuboffer += UtilityRangeBargainingInputs.RandomChangeToPOffer;
            if (UtilityRangeBargainingModuleSettings.ConstrainOffersBetween0And1 && pOfferOrSuboffer > 1.0)
                pOfferOrSuboffer = 1.0;
            else if (UtilityRangeBargainingModuleSettings.ConstrainOffersBetween0And1 && pOfferOrSuboffer < 0.0)
                pOfferOrSuboffer = 0.0;
        }

        private void CalculateDefendantOffer(out double dPerceptionMiddleOfBargainingRange, out double dPerceivedBargainingRange, out double dOfferOrSuboffer, int miniRound, int numMiniRounds)
        {
            double dAggressiveness;
            if (numMiniRounds == 1)
                dAggressiveness = (double)UtilityRangeBargainingProgress.DAggressivenessOverrideModified;
            else
                dAggressiveness = (double)(UtilityRangeBargainingProgress.DAggressivenessOverrideModified + ((double)(((double)miniRound - 1.0) / ((double)numMiniRounds - 1.0)) * (UtilityRangeBargainingProgress.DAggressivenessOverrideModifiedFinal - UtilityRangeBargainingProgress.DAggressivenessOverrideModified)));

            dPerceivedBargainingRange = 0 - ((double)UtilityRangeBargainingProgress.MostRecentDefendantProjectionOfPEquivalentWealthDeltaIfBargainingIsBlocked + (double)UtilityRangeBargainingProgress.MostRecentDefendantProjectionOfDEquivalentWealthDeltaIfBargainingIsBlocked);
            bool dPerceivesUtilityRangeExists = dPerceivedBargainingRange > 0;
            dPerceptionMiddleOfBargainingRange = 0;
            if (UtilityRangeBargainingModuleSettings.InterpretAggressivenessRelativeToOwnUncertainty)
            {
                if (!dPerceivesUtilityRangeExists)
                    dPerceivedBargainingRange = 0.0;
                dPerceptionMiddleOfBargainingRange = ((0 - (double)UtilityRangeBargainingProgress.MostRecentDefendantProjectionOfDEquivalentWealthDeltaIfBargainingIsBlocked) - 0.5 * (double)dPerceivedBargainingRange);
                dOfferOrSuboffer = dPerceptionMiddleOfBargainingRange - dAggressiveness * (double) UtilityRangeBargainingProgress.MostRecentDProjectionDAbsErrorInSettingThreatPoint;
            }
            else
            {
                if (dPerceivesUtilityRangeExists)
                {
                    dOfferOrSuboffer = (double)((0 - UtilityRangeBargainingProgress.MostRecentDefendantProjectionOfDEquivalentWealthDeltaIfBargainingIsBlocked) - dAggressiveness * dPerceivedBargainingRange);
                    dPerceptionMiddleOfBargainingRange = ((0 - (double)UtilityRangeBargainingProgress.MostRecentDefendantProjectionOfDEquivalentWealthDeltaIfBargainingIsBlocked) - 0.5 * (double)dPerceivedBargainingRange);
                }
                else
                { // since there is no bargaining range at all, defendant will only agree to something unexpected, i.e. something that leaves defendant better off
                    if (UtilityRangeBargainingInputs.PartiesWillAcceptIncrementalBenefitIfNoUtilityRangePerceived)
                        dOfferOrSuboffer = dPerceptionMiddleOfBargainingRange = (double)(0 - UtilityRangeBargainingProgress.MostRecentDefendantProjectionOfDEquivalentWealthDeltaIfBargainingIsBlocked);
                    else
                        dOfferOrSuboffer = 0.0; // insist on maximum possible benefit -- note that we'll end up here when this module hasn't yet begun evolving for this bargaining repetition
                }
            }
            dOfferOrSuboffer += UtilityRangeBargainingInputs.RandomChangeToDOffer;
            if (UtilityRangeBargainingModuleSettings.ConstrainOffersBetween0And1 && dOfferOrSuboffer < 0.0)
                dOfferOrSuboffer = 0.0;
            else if (UtilityRangeBargainingModuleSettings.ConstrainOffersBetween0And1 && dOfferOrSuboffer > 1.0)
                dOfferOrSuboffer = 1.0;
        }

        private void ProcessSettlement()
        {
            UtilityRangeBargainingProgress.SettlementExistsAfterFirstBargainingRound = UtilityRangeBargainingProgress.BargainingRound == 1;
            UtilityRangeBargainingProgress.BargainingRoundInWhichSettlementReached = UtilityRangeBargainingProgress.BargainingRound;

            if (UtilityRangeBargainingProgress.SettlementProgress is GlobalSettlementProgress)
            {
                GlobalSettlementProgress gsp = (GlobalSettlementProgress)UtilityRangeBargainingProgress.SettlementProgress;
                double pPerceptionOfSuccess = ((double)gsp.AgreedUponPaymentAsProportion - (double)UtilityRangeBargainingProgress.MostRecentPlaintiffProjectionOfPEquivalentWealthDeltaIfBargainingIsBlocked) / (double)UtilityRangeBargainingProgress.MostRecentPPerceivedBargainingRange;
                double dPerceptionOfSuccess = (0 - (double)UtilityRangeBargainingProgress.MostRecentDefendantProjectionOfDEquivalentWealthDeltaIfBargainingIsBlocked - (double)gsp.AgreedUponPaymentAsProportion) / (double)UtilityRangeBargainingProgress.MostRecentDPerceivedBargainingRange;
                if (UtilityRangeBargainingInputs.ConsiderTasteForFairness)
                {
                    double pPerceptionfUnfairness = 0, dPerceptionOfUnfairness = 0;
                    if (pPerceptionOfSuccess < 0.5 || !UtilityRangeBargainingInputs.TasteForFairnessOnlySelfRegarding)
                        pPerceptionfUnfairness = Math.Abs(pPerceptionOfSuccess - 0.5);
                    if (dPerceptionOfSuccess < 0.5 || !UtilityRangeBargainingInputs.TasteForFairnessOnlySelfRegarding)
                        dPerceptionOfUnfairness = Math.Abs(dPerceptionOfSuccess - 0.5);

                    double pTasteForFairnessEffect = LitigationGame.DisputeGeneratorModule.DGProgress.DamagesClaim * (0 - (double)UtilityRangeBargainingInputs.PTasteForFairness * (double)UtilityRangeBargainingProgress.MostRecentPPerceivedBargainingRange * pPerceptionfUnfairness);
                    double dTasteForFairnessEffect = LitigationGame.DisputeGeneratorModule.DGProgress.DamagesClaim * (0 - (double)UtilityRangeBargainingInputs.DTasteForFairness * (double)UtilityRangeBargainingProgress.MostRecentDPerceivedBargainingRange * dPerceptionOfUnfairness);
                    LitigationGame.LGP.PFinalWealth += pTasteForFairnessEffect;
                    LitigationGame.LGP.DFinalWealth += dTasteForFairnessEffect;
                }
            }
        }

        private double GetCurrentWealth(bool plaintiff)
        {
            var LCP = LitigationGame.LitigationCostModule.LitigationCostProgress;
            if (plaintiff)
            {
                if (LitigationGame.LitigationCostModule is LitigationCostStandardModule && ((LitigationCostStandardInputs)LitigationGame.LitigationCostModule.GameModuleInputs).UseContingencyFees)
                    return 0 - LCP.PTotalExpenses;
                else
                    return LitigationGame.Plaintiff.InitialWealth + LitigationGame.DisputeGeneratorModule.DGProgress.PrelitigationWelfareEffectOnP - LCP.PTotalExpenses;
            }
            else
                return LitigationGame.Defendant.InitialWealth + LitigationGame.DisputeGeneratorModule.DGProgress.PrelitigationWelfareEffectOnD - LCP.DTotalExpenses;
        }

        #endregion

        #region Defaults

        public override double BuiltInCalculationResult(int decisionNumberWithinActionGroup)
        {
            double resultVal;
            if (decisionNumberWithinActionGroup == (int)UtilityRangeBargainingDecisions.PlaintiffProjectionOfEquivalentPlaintiffDeltaWealthIfBargainingIsBlocked || decisionNumberWithinActionGroup == (int)UtilityRangeBargainingDecisions.DefendantProjectionOfEquivalentPlaintiffDeltaWealthIfBargainingIsBlocked)
                resultVal = LitigationGame.Plaintiff.GetDeltaFromSpecifiedWealthProducingSpecifiedDeltaSubjectiveUtility((double)UtilityRangeBargainingProgress.PDecisionmakerWealthBeforeCurrentBargainingRound, UtilityRangeBargainingProgress.TemporaryInputsStorage[0]);
            else
                resultVal = LitigationGame.Defendant.GetDeltaFromSpecifiedWealthProducingSpecifiedDeltaSubjectiveUtility((double)UtilityRangeBargainingProgress.DWealthBeforeCurrentBargainingRound, UtilityRangeBargainingProgress.TemporaryInputsStorage[0]);
            if (GameProgressLogger.LoggingOn)
                GameProgressLogger.Log("Built in calculation result: " + resultVal);
            return resultVal;
        }

        public override double DefaultBehaviorBeforeEvolution(List<double> inputs, int decisionNumber)
        {
            int decisionNumberWithinActionGroup;
            if (decisionNumber >= (int)UtilityRangeBargainingProgress.PUtilityProjectionDecisionNumber)
                decisionNumberWithinActionGroup = decisionNumber - (int)UtilityRangeBargainingProgress.PUtilityProjectionDecisionNumber;
            else // must figure out decision number relative to a previous repetition -- presumably, the first one (if not, we'll get an exception)
                decisionNumberWithinActionGroup = decisionNumber - (int) UtilityRangeBargainingProgress.FirstActionGroupForThisBargainingRoundIfDifferent.FirstDecisionNumber;
            if (decisionNumberWithinActionGroup == (int)UtilityRangeBargainingDecisions.PProjectionPAbsErrorInSettingThreatPoint
                || decisionNumberWithinActionGroup == (int)UtilityRangeBargainingDecisions.PProjectionDAbsErrorInSettingThreatPoint
                || decisionNumberWithinActionGroup == (int)UtilityRangeBargainingDecisions.DProjectionPAbsErrorInSettingThreatPoint
                || decisionNumberWithinActionGroup == (int)UtilityRangeBargainingDecisions.DProjectionDAbsErrorInSettingThreatPoint)
                return 0.5; // doesn't matter much
            switch (decisionNumberWithinActionGroup)
            {
                case (int)UtilityRangeBargainingDecisions.PlaintiffProjectionOfPlaintiffDeltaUtilityIfBargainingIsBlocked:
                case (int)UtilityRangeBargainingDecisions.PlaintiffProjectionOfEquivalentPlaintiffDeltaWealthIfBargainingIsBlocked:
                    return 1.0;

                case (int)UtilityRangeBargainingDecisions.PlaintiffProjectionOfDefendantDeltaUtilityIfBargainingIsBlocked:
                case (int)UtilityRangeBargainingDecisions.PlaintiffProjectionOfEquivalentDefendantDeltaWealthIfBargainingIsBlocked:
                    return -1.0;

                case (int)UtilityRangeBargainingDecisions.DefendantProjectionOfPlaintiffDeltaUtilityIfBargainingIsBlocked:
                case (int)UtilityRangeBargainingDecisions.DefendantProjectionOfEquivalentPlaintiffDeltaWealthIfBargainingIsBlocked:
                    return 0.0;

                case (int)UtilityRangeBargainingDecisions.DefendantProjectionOfDefendantDeltaUtilityIfBargainingIsBlocked:
                case (int)UtilityRangeBargainingDecisions.DefendantProjectionOfEquivalentDefendantDeltaWealthIfBargainingIsBlocked:
                    return 0.0;

                case (int)UtilityRangeBargainingDecisions.PProjectionPAbsErrorInSettingThreatPoint:
                case (int)UtilityRangeBargainingDecisions.PProjectionDAbsErrorInSettingThreatPoint:
                case (int)UtilityRangeBargainingDecisions.DProjectionPAbsErrorInSettingThreatPoint:
                case (int)UtilityRangeBargainingDecisions.DProjectionDAbsErrorInSettingThreatPoint:
                    return 0.0;

                default:
                    throw new NotImplementedException();
            }
            //if (decisionNumberWithinActionGroup < (int)UtilityRangeBargainingDecisions.PlaintiffProjectionOfEquivalentPlaintiffDeltaWealthIfBargainingIsBlocked)
            //{
            //    bool plaintiffProjection = decisionNumberWithinActionGroup == (int)UtilityRangeBargainingDecisions.PlaintiffProjectionOfPlaintiffDeltaUtilityIfBargainingIsBlocked || decisionNumberWithinActionGroup == (int)UtilityRangeBargainingDecisions.PlaintiffProjectionOfDefendantDeltaUtilityIfBargainingIsBlocked;
            //    double partysEstimateOwnResultProbability = inputs[0]; // NOTE: This is a probability, because the ValueFromSignalEstimator already calls CalculateProbabilityPWins, so we shouldn't do it here. plaintiffProjection ? LitigationGame.ProbabilityPWinsForecastingModule.CalculateProbabilityPWins(inputs[0]) : 1.0 - LitigationGame.ProbabilityPWinsForecastingModule.CalculateProbabilityPWins(1.0 - inputs[0]);
            //    double partysEstimateOwnResultDamagesChangeAsProportionOfClaim = inputs[2];
            //    double damagesClaim = LitigationGame.DisputeGeneratorModule.DGProgress.DamagesClaim;
            //    double ownAnticipatedTrialExpenses = inputs[4];
            //    double opponentAnticipatedTrialExpenses = inputs[5];
            //    // todo: adjust the plaintiff numbers for contingency fees


            //    // estimate of own result, opponent's result, own error ==> we'll initialize by demanding something better than own result at trial (thus ignoring litigation costs and the possibility of later bargaining rounds, even where applicable)
            //    // for each bargaining round.
            //    if (plaintiffProjection)
            //    {
            //        double plaintiffProjectionItsWealth = partysEstimateOwnResultProbability * partysEstimateOwnResultDamagesChangeAsProportionOfClaim * damagesClaim;
            //        double plaintiffProjectionOfPlaintiffDeltaWealthIfBargainingIsBlocked = plaintiffProjectionItsWealth - ownAnticipatedTrialExpenses;
            //        double plaintiffProjectionOfDefendantDeltaWealthIfBargainingIsBlocked = 0 - plaintiffProjectionItsWealth - opponentAnticipatedTrialExpenses;
            //        if (decisionNumberWithinActionGroup == (int)UtilityRangeBargainingDecisions.PlaintiffProjectionOfPlaintiffDeltaUtilityIfBargainingIsBlocked)
            //            return LitigationGame.Plaintiff.GetSubjectiveUtilityForWealthLevel((double)LitigationGame.Plaintiff.InitialWealth + plaintiffProjectionOfPlaintiffDeltaWealthIfBargainingIsBlocked) - LitigationGame.Plaintiff.GetSubjectiveUtilityForWealthLevel((double)LitigationGame.Plaintiff.InitialWealth);
            //        else if (decisionNumberWithinActionGroup == (int)UtilityRangeBargainingDecisions.PlaintiffProjectionOfDefendantDeltaUtilityIfBargainingIsBlocked)
            //            return LitigationGame.Defendant.GetSubjectiveUtilityForWealthLevel((double)LitigationGame.Defendant.InitialWealth + plaintiffProjectionOfDefendantDeltaWealthIfBargainingIsBlocked) - LitigationGame.Defendant.GetSubjectiveUtilityForWealthLevel((double)LitigationGame.Defendant.InitialWealth);
            //    }
            //    else
            //    {
            //        double defendantProjectionPsWealth = (1.0 - partysEstimateOwnResultProbability) * (0 - partysEstimateOwnResultDamagesChangeAsProportionOfClaim) * damagesClaim;
            //        double defendantProjectionOfPlaintiffDeltaWealthIfBargainingIsBlocked = defendantProjectionPsWealth - opponentAnticipatedTrialExpenses;
            //        double defendantProjectionOfDefendantDeltaWealthIfBargainingIsBlocked = 0 - defendantProjectionPsWealth - ownAnticipatedTrialExpenses;
            //        if (decisionNumberWithinActionGroup == (int)UtilityRangeBargainingDecisions.DefendantProjectionOfPlaintiffDeltaUtilityIfBargainingIsBlocked)
            //            return LitigationGame.Plaintiff.GetSubjectiveUtilityForWealthLevel((double)LitigationGame.Plaintiff.InitialWealth + defendantProjectionOfPlaintiffDeltaWealthIfBargainingIsBlocked) - LitigationGame.Plaintiff.GetSubjectiveUtilityForWealthLevel((double)LitigationGame.Plaintiff.InitialWealth);
            //        else if (decisionNumberWithinActionGroup == (int)UtilityRangeBargainingDecisions.DefendantProjectionOfDefendantDeltaUtilityIfBargainingIsBlocked)
            //            return LitigationGame.Defendant.GetSubjectiveUtilityForWealthLevel((double)LitigationGame.Defendant.InitialWealth + defendantProjectionOfDefendantDeltaWealthIfBargainingIsBlocked) - LitigationGame.Defendant.GetSubjectiveUtilityForWealthLevel((double)LitigationGame.Defendant.InitialWealth);
            //    }
            //}
            //else
            //{ // the inputs are now simply the corresponding projection of change in utility
            //    if (decisionNumberWithinActionGroup == (int)UtilityRangeBargainingDecisions.PlaintiffProjectionOfEquivalentPlaintiffDeltaWealthIfBargainingIsBlocked)
            //        return GetEquivalentWealthDeltaForSpecifiedLaterUtility(LitigationGame.Plaintiff, GetCurrentWealth(true), (double)UtilityRangeBargainingProgress.MostRecentPlaintiffProjectionOfPUtilityDeltaIfBargainingIsBlocked + LitigationGame.Plaintiff.GetSubjectiveUtilityForWealthLevel(GetCurrentWealth(true)));
            //    else if (decisionNumberWithinActionGroup == (int)UtilityRangeBargainingDecisions.PlaintiffProjectionOfEquivalentDefendantDeltaWealthIfBargainingIsBlocked)
            //        return GetEquivalentWealthDeltaForSpecifiedLaterUtility(LitigationGame.Defendant, GetCurrentWealth(false), (double)UtilityRangeBargainingProgress.MostRecentPlaintiffProjectionOfDUtilityDeltaIfBargainingIsBlocked + LitigationGame.Defendant.GetSubjectiveUtilityForWealthLevel(GetCurrentWealth(false)));
            //    else if (decisionNumberWithinActionGroup == (int)UtilityRangeBargainingDecisions.DefendantProjectionOfEquivalentPlaintiffDeltaWealthIfBargainingIsBlocked)
            //        return GetEquivalentWealthDeltaForSpecifiedLaterUtility(LitigationGame.Plaintiff, GetCurrentWealth(true), (double)UtilityRangeBargainingProgress.MostRecentDefendantProjectionOfPUtilityDeltaIfBargainingIsBlocked  + LitigationGame.Plaintiff.GetSubjectiveUtilityForWealthLevel(GetCurrentWealth(true)));
            //    else if (decisionNumberWithinActionGroup == (int)UtilityRangeBargainingDecisions.DefendantProjectionOfEquivalentDefendantDeltaWealthIfBargainingIsBlocked)
            //        return GetEquivalentWealthDeltaForSpecifiedLaterUtility(LitigationGame.Defendant, GetCurrentWealth(false), (double)UtilityRangeBargainingProgress.MostRecentDefendantProjectionOfDUtilityDeltaIfBargainingIsBlocked + LitigationGame.Defendant.GetSubjectiveUtilityForWealthLevel(GetCurrentWealth(false)));
            //}
            //throw new Exception("Unknown decision.");
        }

        #endregion

        #region Scoring and equivalent wealth

        public override double GetScoreForParticularDecision(int decisionIndexWithinActionGroup)
        {
            int? correspondingDecisionIndexInFirstRepetitionActionGroup = null;
            if (UtilityRangeBargainingProgress.FirstActionGroupForBargainingRoundBeingEvolvedIfDifferent != null)
                correspondingDecisionIndexInFirstRepetitionActionGroup = UtilityRangeBargainingProgress.FirstActionGroupForBargainingRoundBeingEvolvedIfDifferent.FirstDecisionNumber + decisionIndexWithinActionGroup;

            if (decisionIndexWithinActionGroup == (int)UtilityRangeBargainingDecisions.PlaintiffProjectionOfPlaintiffDeltaUtilityIfBargainingIsBlocked || decisionIndexWithinActionGroup == (int)UtilityRangeBargainingDecisions.DefendantProjectionOfPlaintiffDeltaUtilityIfBargainingIsBlocked)
            {
                double pDeltaUtility = LitigationGame.LitigationCostModule.GetFinalWealthSubjectiveUtilityValue(true, useSocialWelfareMeasureIfOptionIsSet: false) - LitigationGame.Plaintiff.GetSubjectiveUtilityForWealthLevel((double)UtilityRangeBargainingProgress.PDecisionmakerWealthAtTimeOfDecisionBeingEvolved);
                //if (decisionIndexWithinActionGroup == (int)UtilityRangeBargainingDecisions.PlaintiffProjectionOfPlaintiffDeltaUtilityIfBargainingIsBlocked)
                //    pDeltaUtility = -250.0 + 1000.0 * BargainingProgress.PDecisionInputs[0];
                //else
                //    pDeltaUtility = -250.0 + 1000.0 * (1.0 - BargainingProgress.DDecisionInputs[0]);

                if (subsequentBargainingRoundsAreRelative && correspondingDecisionIndexInFirstRepetitionActionGroup != null)
                {
                    SpecifyInputs(decisionIndexWithinActionGroup == (int)UtilityRangeBargainingDecisions.PlaintiffProjectionOfPlaintiffDeltaUtilityIfBargainingIsBlocked ? BargainingProgress.PDecisionInputs : BargainingProgress.DDecisionInputs);
                    pDeltaUtility -= Calculate((int)correspondingDecisionIndexInFirstRepetitionActionGroup); // we must subtract what we added in above so that we're only scoring the difference between the delta utility projection and the corresponding projection in the first bargaining round
                }
                return pDeltaUtility;
            }
            else if (decisionIndexWithinActionGroup == (int)UtilityRangeBargainingDecisions.PlaintiffProjectionOfDefendantDeltaUtilityIfBargainingIsBlocked || decisionIndexWithinActionGroup == (int)UtilityRangeBargainingDecisions.DefendantProjectionOfDefendantDeltaUtilityIfBargainingIsBlocked)
            {
                double dDeltaUtility = LitigationGame.LitigationCostModule.GetFinalWealthSubjectiveUtilityValue(false, useSocialWelfareMeasureIfOptionIsSet: false) - LitigationGame.Defendant.GetSubjectiveUtilityForWealthLevel((double)UtilityRangeBargainingProgress.DWealthAtTimeOfDecisionBeingEvolved);
                //if (decisionIndexWithinActionGroup == (int)UtilityRangeBargainingDecisions.DefendantProjectionOfDefendantDeltaUtilityIfBargainingIsBlocked)
                //    dDeltaUtility = -1250.0 + 1000.0 * BargainingProgress.DDecisionInputs[0];
                //else
                //    dDeltaUtility = -1250.0 + 1000.0 * (1.0 - BargainingProgress.PDecisionInputs[0]);
                if (subsequentBargainingRoundsAreRelative && correspondingDecisionIndexInFirstRepetitionActionGroup != null)
                {
                    SpecifyInputs(decisionIndexWithinActionGroup == (int)UtilityRangeBargainingDecisions.PlaintiffProjectionOfDefendantDeltaUtilityIfBargainingIsBlocked ? BargainingProgress.PDecisionInputs : BargainingProgress.DDecisionInputs);
                    dDeltaUtility -= Calculate((int)correspondingDecisionIndexInFirstRepetitionActionGroup); // we must subtract what we added in above so that we're only scoring the difference between the delta utility projection and the corresponding projection in the first bargaining round
                }
                return dDeltaUtility;
            }
            else if (decisionIndexWithinActionGroup == (int)UtilityRangeBargainingDecisions.PProjectionPAbsErrorInSettingThreatPoint)
            {
                return (double)UtilityRangeBargainingProgress.MostRecentPAbsErrorInSettingThreatPoint;
            }
            else if (decisionIndexWithinActionGroup == (int)UtilityRangeBargainingDecisions.PProjectionDAbsErrorInSettingThreatPoint)
            {
                return (double)UtilityRangeBargainingProgress.MostRecentDAbsErrorInSettingThreatPoint;
            }
            else if (decisionIndexWithinActionGroup == (int)UtilityRangeBargainingDecisions.DProjectionPAbsErrorInSettingThreatPoint)
            {
                return (double)UtilityRangeBargainingProgress.MostRecentPAbsErrorInSettingThreatPoint;
            }
            else if (decisionIndexWithinActionGroup == (int)UtilityRangeBargainingDecisions.DProjectionDAbsErrorInSettingThreatPoint)
            {
                return (double)UtilityRangeBargainingProgress.MostRecentDAbsErrorInSettingThreatPoint;
            }
            else if (decisionIndexWithinActionGroup == (int)UtilityRangeBargainingDecisions.PlaintiffProjectionOfEquivalentPlaintiffDeltaWealthIfBargainingIsBlocked)
            {
                //  we don't want to know the actual wealth differential. If we do that, we're not paying any attention to the utility figures. We want to figure out the wealth differential that would have corresponded to the projection of delta utility. Note that we need to have a decision here (we can't simply invert it) because of the possibility that a player may have incomplete information about how risk averse or loss averse it or its opponent is. If there were complete information about that, we could just use GetDeltaFromInitialWealthProducingSpecifiedSubjectiveUtility, but then we wouldn't need to evolve this decision.
                double equivWealth = GetEquivalentWealthDeltaRelativeToEvolutionTimeGivenDeltaUtilityProjection(true, (double)UtilityRangeBargainingProgress.FakePProjectionOfPUtilityDelta);
                return equivWealth;
            }
            else if (decisionIndexWithinActionGroup == (int)UtilityRangeBargainingDecisions.PlaintiffProjectionOfEquivalentDefendantDeltaWealthIfBargainingIsBlocked)
            {
                double equivWealth = GetEquivalentWealthDeltaRelativeToEvolutionTimeGivenDeltaUtilityProjection(false, (double)UtilityRangeBargainingProgress.FakePProjectionOfDUtilityDelta);
                return equivWealth;
            }
            else if (decisionIndexWithinActionGroup == (int)UtilityRangeBargainingDecisions.DefendantProjectionOfEquivalentPlaintiffDeltaWealthIfBargainingIsBlocked)
            {
                double equivWealth = GetEquivalentWealthDeltaRelativeToEvolutionTimeGivenDeltaUtilityProjection(true, (double)UtilityRangeBargainingProgress.FakeDProjectionOfPUtilityDelta);
                return equivWealth;
            }
            else if (decisionIndexWithinActionGroup == (int)UtilityRangeBargainingDecisions.DefendantProjectionOfEquivalentDefendantDeltaWealthIfBargainingIsBlocked)
            {
                double equivWealth = GetEquivalentWealthDeltaRelativeToEvolutionTimeGivenDeltaUtilityProjection(false, (double)UtilityRangeBargainingProgress.FakeDProjectionOfDUtilityDelta);
                return equivWealth;
            }
            throw new Exception("Unknown decision.");
        }

        private double GetEquivalentWealthDeltaCorrespondingToProjectedDeltaUtility(bool plaintiffsProjection, bool plaintiffsUtility, bool multiplyDeltaUtilityByRandomVariableForScoringEquivalentWealth = false)
        {
            // This is not used.

            // Note: We're returning the absolute equivalent wealth rather than a proportion here, because the damages claim is not an input to the decision,
            // and we have no reason to take into account in this function. (The player's estimate of the damages is taken into account calculating utility.)
            if (LitigationGame.CurrentlyEvolving == false)
                throw new Exception("Internal error: This method is designed to be called only during evolution, after the projection of delta utility has been set and the wealth at the time of evolution has been recorded.");

            double deltaUtilityProjection = 0.0;
            if (plaintiffsProjection == true && plaintiffsUtility == true)
                deltaUtilityProjection = (double)UtilityRangeBargainingProgress.MostRecentPlaintiffProjectionOfPUtilityDeltaIfBargainingIsBlocked;
            else if (plaintiffsProjection == true && plaintiffsUtility == false)
                deltaUtilityProjection = (double)UtilityRangeBargainingProgress.MostRecentPlaintiffProjectionOfDUtilityDeltaIfBargainingIsBlocked;
            else if (plaintiffsProjection == false && plaintiffsUtility == true)
                deltaUtilityProjection = (double)UtilityRangeBargainingProgress.MostRecentDefendantProjectionOfPUtilityDeltaIfBargainingIsBlocked;
            else if (plaintiffsProjection == false && plaintiffsUtility == false)
                deltaUtilityProjection = (double)UtilityRangeBargainingProgress.MostRecentDefendantProjectionOfDUtilityDeltaIfBargainingIsBlocked;
            if (multiplyDeltaUtilityByRandomVariableForScoringEquivalentWealth)
                deltaUtilityProjection *= UtilityRangeBargainingInputs.RandomMultiplierForEvolvingEquivalentWealth;
            return GetEquivalentWealthDeltaRelativeToEvolutionTimeGivenDeltaUtilityProjection(plaintiffsUtility, deltaUtilityProjection);
        }

        public Tuple<double, double> GetEquivalentWealthDeltaProjectionIfPartiesHadCorrectInformation(bool getFiftyPercentBaselineInstead = false)
        {
            List<double> pInputs, dInputs;
            GetInputsForBargainingWithCorrectInformationSubstituted(out pInputs, out dInputs, getFiftyPercentBaselineInstead);
            return new Tuple<double, double>(GetEquivalentWealthDeltaProjectionGivenInformation(true, pInputs, dInputs), GetEquivalentWealthDeltaProjectionGivenInformation(false, pInputs, dInputs));
        }

        private double GetEquivalentWealthDeltaProjectionIfPartyHadCorrectInformation(bool plaintiff)
        {
            List<double> pInputs, dInputs;
            GetInputsForBargainingWithCorrectInformationSubstituted(out pInputs, out dInputs);
            return GetEquivalentWealthDeltaProjectionGivenInformation(plaintiff, pInputs, dInputs);
        }

        private double GetEquivalentWealthDeltaProjectionGivenInformation(bool plaintiff, List<double> pInputs, List<double> dInputs)
        {
            if (plaintiff)
            {
                double pCorrectUtilityProjection = CalculateWithoutAffectingEvolution(pInputs, (int)UtilityRangeBargainingProgress.PUtilityProjectionDecisionNumber);
                if (UtilityRangeBargainingProgress.FirstActionGroupForThisBargainingRoundIfDifferent != null)
                    pCorrectUtilityProjection += CalculateWithoutAffectingEvolution(pInputs, (int)UtilityRangeBargainingProgress.FirstActionGroupForThisBargainingRoundIfDifferent.FirstDecisionNumber + (int)UtilityRangeBargainingProgress.PUtilityProjectionDecisionNumber - (int)Game.CurrentActionGroup.FirstDecisionNumber); // should be equal to just the first decision number unless order is changed
                double pEquivalentWealth;
                if (UtilityRangeBargainingModuleSettings.CanDirectlyCalculateEquivalentWealth && allowDirectCalculationOfEquivalentWealth)
                    pEquivalentWealth = GetEquivalentWealthDeltaRelativeToCurrentWealthAsProportionOfDamagesClaimGivenDeltaUtilityProjectionWhereUtilityParametersAreKnown(true, pCorrectUtilityProjection) ;
                else
                    pEquivalentWealth = CalculateWithoutAffectingEvolution(new List<double> { pCorrectUtilityProjection }, (int)
UtilityRangeBargainingProgress.PWealthTranslationDecisionNumber) / LitigationGame.DisputeGeneratorModule.DGProgress.DamagesClaim;
                return pEquivalentWealth;
            }
            else
            {
                double dCorrectUtilityProjection = CalculateWithoutAffectingEvolution(dInputs, (int)UtilityRangeBargainingProgress.DUtilityProjectionDecisionNumber);
                if (UtilityRangeBargainingProgress.FirstActionGroupForThisBargainingRoundIfDifferent != null)
                    dCorrectUtilityProjection += CalculateWithoutAffectingEvolution(dInputs,(int)UtilityRangeBargainingProgress.FirstActionGroupForThisBargainingRoundIfDifferent.FirstDecisionNumber + (int)UtilityRangeBargainingProgress.DUtilityProjectionDecisionNumber - (int)Game.CurrentActionGroup.FirstDecisionNumber); // should be equal to the second decision number for first round
                double dEquivalentWealth;
                if (UtilityRangeBargainingModuleSettings.CanDirectlyCalculateEquivalentWealth && allowDirectCalculationOfEquivalentWealth)
                    dEquivalentWealth = GetEquivalentWealthDeltaRelativeToCurrentWealthAsProportionOfDamagesClaimGivenDeltaUtilityProjectionWhereUtilityParametersAreKnown(false, dCorrectUtilityProjection);
                else
                    dEquivalentWealth = CalculateWithoutAffectingEvolution(new List<double> { dCorrectUtilityProjection }, (int)
UtilityRangeBargainingProgress.DWealthTranslationDecisionNumber) / LitigationGame.DisputeGeneratorModule.DGProgress.DamagesClaim;
                return dEquivalentWealth;
            }
        }

        private double GetEquivalentWealthDeltaRelativeToCurrentWealthAsProportionOfDamagesClaimGivenDeltaUtilityProjectionWhereUtilityParametersAreKnown(bool plaintiffsUtility, double deltaUtilityProjection)
        {
            double currentWealth = GetCurrentWealth(plaintiffsUtility);
            UtilityMaximizer um = plaintiffsUtility ? LitigationGame.Plaintiff : LitigationGame.Defendant;
            double currentUtility = um.GetSubjectiveUtilityForWealthLevel(currentWealth);
            return GetEquivalentWealthDeltaForSpecifiedLaterUtility(um, currentWealth, deltaUtilityProjection + currentUtility) / LitigationGame.DisputeGeneratorModule.DGProgress.DamagesClaim;
        }

        private double GetEquivalentWealthDeltaRelativeToEvolutionTimeGivenDeltaUtilityProjection(bool plaintiffsUtility, double deltaUtilityProjection)
        {
            double wealthAtTimeOfEvolution = plaintiffsUtility ? (double)UtilityRangeBargainingProgress.PDecisionmakerWealthAtTimeOfDecisionBeingEvolved : (double)UtilityRangeBargainingProgress.DWealthAtTimeOfDecisionBeingEvolved;
            UtilityMaximizer um = plaintiffsUtility ? LitigationGame.Plaintiff : LitigationGame.Defendant;
            double equivWealthDelta = 0;
            if (plaintiffsUtility && LitigationGame.LitigationCostModule is LitigationCostStandardModule && ((LitigationCostStandardInputs)(LitigationGame.LitigationCostModule.GameModuleInputs)).UseContingencyFees)
            {
                double expectedLawyerEquivalentWealthDelta = deltaUtilityProjection;
                double correspondingPlaintiffEquivalentDeltaWealth = expectedLawyerEquivalentWealthDelta / ((LitigationCostStandardInputs)(LitigationGame.LitigationCostModule.GameModuleInputs)).ContingencyFeeRate; // since we are bargaining on behalf of the plaintiff, we must convert expected lawyer's wealth to plaintiff's.
                equivWealthDelta = correspondingPlaintiffEquivalentDeltaWealth;
            }
            else
            {
                double utilityAtTimeOfEvolution = um.GetSubjectiveUtilityForWealthLevel(wealthAtTimeOfEvolution);
                double laterUtility = utilityAtTimeOfEvolution + deltaUtilityProjection;
                equivWealthDelta = GetEquivalentWealthDeltaForSpecifiedLaterUtility(um, wealthAtTimeOfEvolution, laterUtility);
            }
            return equivWealthDelta;
        }

        private static double GetEquivalentWealthDeltaForSpecifiedLaterUtility(UtilityMaximizer um, double wealthAtTimeOfEvolution, double laterUtility)
        {
            double deltaFromInitialWealthToTarget = um.GetDeltaFromInitialWealthProducingSpecifiedSubjectiveUtility(laterUtility);
            double deltaFromInitialWealthToCurrentWealth = wealthAtTimeOfEvolution - um.InitialWealth;
            double deltaFromCurrentWealthToTarget = deltaFromInitialWealthToTarget - deltaFromInitialWealthToCurrentWealth;
            return deltaFromCurrentWealthToTarget;
        }

        #endregion

        #region Module setup

        public override List<Tuple<string, string>> GetInputNamesAndAbbreviations(int decisionNumberWithinActionGroup)
        {
            List<Tuple<string, string>> pNAndA, dNAndA;
            LitigationGame.BargainingModule.GetInputNamesAndAbbreviationsForBargaining(out pNAndA, out dNAndA);
            if (decisionNumberWithinActionGroup == (int)UtilityRangeBargainingDecisions.PlaintiffProjectionOfPlaintiffDeltaUtilityIfBargainingIsBlocked || decisionNumberWithinActionGroup == (int)UtilityRangeBargainingDecisions.PlaintiffProjectionOfDefendantDeltaUtilityIfBargainingIsBlocked)
                return pNAndA;
            else if (decisionNumberWithinActionGroup == (int)UtilityRangeBargainingDecisions.DefendantProjectionOfPlaintiffDeltaUtilityIfBargainingIsBlocked || decisionNumberWithinActionGroup == (int)UtilityRangeBargainingDecisions.DefendantProjectionOfDefendantDeltaUtilityIfBargainingIsBlocked)
                return dNAndA;
            else if (decisionNumberWithinActionGroup == (int)UtilityRangeBargainingDecisions.PProjectionPAbsErrorInSettingThreatPoint || decisionNumberWithinActionGroup == (int)UtilityRangeBargainingDecisions.PProjectionDAbsErrorInSettingThreatPoint)
            {
                return pNAndA;
                //List<Tuple<string, string>> na = new List<Tuple<string,string>>();
                //// When evolving the range uncertainty settings, we'll focus on just a single variable
                //// that we expect to have an overwhelming influence on the optimal degree of aggressiveness.
                //// Once we've evolved the optimal degree of aggressiveness, we won't be evolving range
                //// uncertainty settings any longer, and we must include other factors that could have an effect. 
                //if (!UtilityRangeBargainingModuleSettings.EvolvingRangeUncertaintySettings)
                //    na = pNAndA;
                //bool ownNoiseLevel = decisionNumberWithinActionGroup == (int)UtilityRangeBargainingDecisions.PProjectionPAbsErrorInSettingThreatPoint;
                //AddInputNamesAndAbbreviationsForNoiseLevel(na, true, true, true); // ownNoiseLevel, !ownNoiseLevel);
                //return na;
            }
            else if (decisionNumberWithinActionGroup == (int)UtilityRangeBargainingDecisions.DProjectionDAbsErrorInSettingThreatPoint || decisionNumberWithinActionGroup == (int)UtilityRangeBargainingDecisions.DProjectionPAbsErrorInSettingThreatPoint)
            {
                return dNAndA;
                //List<Tuple<string, string>> na = new List<Tuple<string, string>>();
                //if (!UtilityRangeBargainingModuleSettings.EvolvingRangeUncertaintySettings)
                //    na = dNAndA;
                //bool ownNoiseLevel = decisionNumberWithinActionGroup == (int)UtilityRangeBargainingDecisions.DProjectionDAbsErrorInSettingThreatPoint;
                //AddInputNamesAndAbbreviationsForNoiseLevel(na, false, true, true); // ownNoiseLevel, !ownNoiseLevel);
                //return na;
            }
            else
                return new List<Tuple<string, string>> { new Tuple<string, string>("DeltaUtility", "deltaU") };
        }

        public override void CreateInstanceAndInitializeProgress(Game theGame, List<Strategy> theStrategies, object gameModuleSettings, out GameModule theGameModule, out GameModuleProgress theGameModuleProgress)
        {
            UtilityRangeBargainingModule copy = new UtilityRangeBargainingModule();
            copy.SetGameAndStrategies(theGame, theStrategies, GameModuleName, GameModuleNamesThisModuleReliesOn, GameModuleNumbersThisModuleReliesOn, GameModuleSettings);
            SetGameModuleFields(copy);
            theGameModuleProgress = UtilityRangeBargainingModuleProgress.GetRecycledOrAllocate();
            theGameModule = copy;
        }

        public virtual object GenerateSetting(string options)
        {
            bool evolvingRangeUncertaintySettings = GetBoolCodeGeneratorOption(options, "EvolvingRangeUncertaintySettings");
            int? iterationsOverride = null;
            int? smoothingPointsOverride = null;
            if (evolvingRangeUncertaintySettings)
            {
                iterationsOverride = GetIntCodeGeneratorOption(options, "RangeUncertaintyAggressivenessIterationsOverrideMax");
                smoothingPointsOverride = GetIntCodeGeneratorOption(options, "RangeUncertaintyAggressivenessSmoothingPointsOverrideMax");
            }
            bool constrainOffersBetween0And1 = GetBoolCodeGeneratorOption(options, "ConstrainOffersBetween0And1");
            bool interpretAggressivenessRelativeToOwnUncertainty = GetBoolCodeGeneratorOption(options, "InterpretAggressivenessRelativeToOwnUncertainty");
            bool fullInformationAboutUtilityFunctions = GetBoolCodeGeneratorOption(options, "FullInformationAboutUtilityFunctions");
            int maxRepetitionsProjectionDecision = GetIntCodeGeneratorOption(options, "MaxRepetitionsProjectionDecision");
            bool negotiateSeparatelyOverProb = GetBoolCodeGeneratorOption(options, "NegotiateSeparatelyOverProbabilityAndMagnitude");
            bool negotiateProbabilityBeforeMagnitude = GetBoolCodeGeneratorOption(options, "NegotiateProbabilityBeforeMagnitude");
            bool rejectLowProbabilitySettlements = GetBoolCodeGeneratorOption(options, "RejectLowProbabilitySettlements");
            bool rejectHighProbabilitySettlements = GetBoolCodeGeneratorOption(options, "RejectHighProbabilitySettlements");
            double lowProbabilityThreshold = GetDoubleCodeGeneratorOption(options, "LowProbabilityThreshold");
            double highProbabilityThreshold = GetDoubleCodeGeneratorOption(options, "HighProbabilityThreshold");
            bool partialSettlementEnforced = GetBoolCodeGeneratorOption(options, "PartialSettlementEnforced");
            bool divideBargainingIntoMinirounds = GetBoolCodeGeneratorOption(options, "DivideBargainingIntoMinirounds");
            int numBargainingMinirounds = GetIntCodeGeneratorOption(options, "NumBargainingMinirounds");
            bool chunkIterationsForRemoting = GetBoolCodeGeneratorOption(options, "ChunkIterationsForRemoting");
            bool remotingCanSeparateFindingAndSmoothing = GetBoolCodeGeneratorOption(options, "RemotingCanSeparateFindingAndSmoothing");
            if (divideBargainingIntoMinirounds && numBargainingMinirounds > 1 && negotiateSeparatelyOverProb)
                throw new Exception("Internal error: Cannot simultaneously use the miniround feature and the subclaim feature, because the miniround loop needs to contain all subclaims, yet the subclaims are divided into different decisions, so this would be complex.");
            object gameModuleSettings = new UtilityRangeBargainingModuleSettings() { CanDirectlyCalculateEquivalentWealth = fullInformationAboutUtilityFunctions, NegotiateSeparatelyOverProbabilityAndMagnitude = negotiateSeparatelyOverProb, NegotiateProbabilityBeforeMagnitude = negotiateProbabilityBeforeMagnitude, PartialSettlementEnforced = partialSettlementEnforced, RejectLowProbabilitySettlements = rejectLowProbabilitySettlements, RejectHighProbabilitySettlements = rejectHighProbabilitySettlements, LowProbabilityThreshold = lowProbabilityThreshold, HighProbabilityThreshold = highProbabilityThreshold, DivideBargainingIntoMinirounds = divideBargainingIntoMinirounds, NumBargainingMinirounds = numBargainingMinirounds, EvolvingRangeUncertaintySettings = evolvingRangeUncertaintySettings, ConstrainOffersBetween0And1 = constrainOffersBetween0And1, InterpretAggressivenessRelativeToOwnUncertainty = interpretAggressivenessRelativeToOwnUncertainty }; // note that when we can directly calculate equivalent wealth, the BuiltInCalculationResult function above will be called
            GameModuleSettings = gameModuleSettings;

            List<Decision> decisions = new List<Decision>();

            decisions.Add(GetProjectionDecision("PProjectionOfPUtil", "pputil", true, true, false, maxRepetitionsProjectionDecision, false, false));
            decisions.Add(GetProjectionDecision("PProjectionOfDUtil", "pdutil", false, true, true, maxRepetitionsProjectionDecision, false, false));
            decisions.Add(GetProjectionDecision("DProjectionOfPUtil", "dputil", false, false, false, maxRepetitionsProjectionDecision, false, false));
            if (chunkIterationsForRemoting)
                decisions.Last().DisableCachingForThisDecision = true;
            decisions.Add(GetProjectionDecision("DProjectionOfDUtil", "ddutil", false, false, true, maxRepetitionsProjectionDecision, false, false));

            bool canBeCalculatedWithoutOptimizing = fullInformationAboutUtilityFunctions && allowDirectCalculationOfEquivalentWealth;
            decisions.Add(GetProjectionDecision("PProjectionOfPEquivWealth", "ppew", false, true, false, 1, true, canBeCalculatedWithoutOptimizing));
            decisions.Last().DisableCachingForThisDecision = true; // disable caching for this decision because we need the projections of utility to be evolved first
            decisions.Add(GetProjectionDecision("PProjectionOfDEquivWealth", "pdew", false, true, false, 1, true, canBeCalculatedWithoutOptimizing));
            if (chunkIterationsForRemoting)
                decisions.Last().DisableCachingForThisDecision = true;
            decisions.Add(GetProjectionDecision("DProjectionOfPEquivWealth", "dpew", false, false, false, 1, true, canBeCalculatedWithoutOptimizing));
            if (chunkIterationsForRemoting)
                decisions.Last().DisableCachingForThisDecision = true;
            decisions.Add(GetProjectionDecision("DProjectionOfDEquivWealth", "ddew", false, false, false, 1, true, canBeCalculatedWithoutOptimizing));
            if (chunkIterationsForRemoting)
                decisions.Last().DisableCachingForThisDecision = true;

            decisions.Add(GetProjectionDecision("PProjectionPAbsErrorInSettingThreatPoint", "pperr", false, true, false, maxRepetitionsProjectionDecision, false, false, smoothingPointsOverride, iterationsOverride));
            decisions.Add(GetProjectionDecision("PProjectionDAbsErrorInSettingThreatPoint", "pderr", false, true, false, maxRepetitionsProjectionDecision, false, false, smoothingPointsOverride, iterationsOverride));
            decisions.Add(GetProjectionDecision("DProjectionPAbsErrorInSettingThreatPoint", "pperr", false, false, false, maxRepetitionsProjectionDecision, false, false, smoothingPointsOverride, iterationsOverride));
            decisions.Add(GetProjectionDecision("DProjectionDAbsErrorInSettingThreatPoint", "pderr", false, false, false, maxRepetitionsProjectionDecision, false, false, smoothingPointsOverride, iterationsOverride));

            return new UtilityRangeBargainingModule()
            {
                DecisionsCore = decisions,
                ActionsAtBeginningOfModule = new List<string>() { "BeforeBargaining" },
                ActionsAtEndOfModule = new List<string>() { "AfterBargaining" }, // note: these will be set by GetActionGroupsForModule to be in a separate action group
                GameModuleName = "BargainingModule",
                GameModuleNamesThisModuleReliesOn = new List<string>() { "BargainingAggressivenessModule2" }, // It is the LAST bargaining aggressiveness module that determines the level of aggressiveness. If there is an earlier one, that is designed only to affect the later one.
                GameModuleSettings = gameModuleSettings,
                UpdateCumulativeDistributionsAfterSingleActionGroup = false, /* currently updating only after dispute generation */
                Tags = new List<string>() { "Bargaining subclaim", "Bargaining round" }
            };
        }

        private Decision GetProjectionDecision(string name, string abbreviation, bool isFirstInRound, bool isPDecision, bool usesSameInputsAsPreviousDecision, int maxRepetitionsProjectionDecision, bool evolveOnlyOnFirstRepetition, bool canBeCalculatedWithoutOptimizing, int? smoothingPointsOverrideWhenEvolvingRangeUncertaintyThreatPoint = null, int? iterationsOverrideWhenEvolvingRangeUncertaintyThreatPoint = null)
        {
            Decision theDecision = new Decision()
            {
                Name = name,
                Abbreviation = abbreviation,
                DecisionTypeCode = isPDecision ? "P" : "D",
                DummyDecisionRequiringNoOptimization = canBeCalculatedWithoutOptimizing,
                SmoothingPointsOverride = name.Contains("ThreatPoint") && UtilityRangeBargainingModuleSettings.EvolvingRangeUncertaintySettings ? smoothingPointsOverrideWhenEvolvingRangeUncertaintyThreatPoint : (int?) null,
                IterationsOverride = name.Contains("ThreatPoint") && UtilityRangeBargainingModuleSettings.EvolvingRangeUncertaintySettings ? iterationsOverrideWhenEvolvingRangeUncertaintyThreatPoint : null, 
                DynamicNumberOfInputs = true,
                EvolveOnlyFirstRepetitionInExecutionOrder = evolveOnlyOnFirstRepetition,
                InputsAndOccurrencesAlwaysSameAsPreviousDecision = usesSameInputsAsPreviousDecision,
                UseOversampling = true,
                OversamplingWillAlwaysBeSameAsPreviousDecision = !isFirstInRound,
                SuccessReplicationIfSuccessAttemptRatioIsBelowThis = 0.3,
                InputAbbreviations = null,
                InputNames = null,
                StrategyBounds = new StrategyBounds()
                { // not relevant because score represents correct answer
                    LowerBound = 0.0,
                    UpperBound = 1.0
                },
                Bipolar = false,
                StrategyGraphInfos = new List<StrategyGraphInfo>(),
                HighestIsBest = true,
                PhaseOutDefaultBehaviorOverRepetitions = 0,
                MaxEvolveRepetitions = maxRepetitionsProjectionDecision,
                AlwaysUseLatestVersion = true,
                PreservePreviousVersionWhenOptimizing = false,
                AverageInPreviousVersion = false,
                EvolveThisDecisionEvenWhenSkippingByDefault = false,
                ScoreRepresentsCorrectAnswer = true,
                TestInputs = null, // (name == "PProjectionOfPUtil" || name == "DProjectionOfDUtil") ? new List<double>() { 0.1 } : null,
                TestOutputs = new List<double>() { 0.1, 0.2, 0.3, 0.4, 0.5, 0.6, 0.7, 0.8, 0.9 },
                DecisionCounterfactuallyAvoidsNarrowingOfResults = false
            };
            if (name == "PProjectionOfPUtil" || name == "DProjectionOfDUtil")
            {
                theDecision.StrategyGraphInfos = new List<StrategyGraphInfo>()
                {
                    new StrategyGraphInfo()
                    {
                        InputsToGraph = new List<InputValueVaryInGraph>()
                        {
                            new InputValueVaryInGraph() { InputAbbreviation= isPDecision ? "PEstProb" : "DEstProbDWin", MinValue=0.0, MaxValue=1.0, NumValues = 41 }
                        },
                        InputsToFix = new List<InputValueFixInGraph>()
                        {
                        },
                        OutputReportFilename="UtilRange.txt",
                        SettingsFor2DGraph = new Graph2DSettings() { xMin = 0, xMax = 1.0,
                            xAxisLabel="Party's estimate of probability it wins",
                            yAxisLabel="Party's projection of change in own utility",
                            graphName="Projection of own delta utility (if settlement blocked at this stage)",
                            seriesName= (isPDecision  ? "Plaintiff projection" : "Defendant projection"), replaceSeriesOfSameName=false, fadeSeriesOfSameName=true, exportFramesOfMovies= false},
                           
                        ReportAfterEachEvolutionStep = true
                    }
                };
            }
            if (name == "PProjectionOfDUtil" || name == "DProjectionOfPUtil")
            {
                theDecision.StrategyGraphInfos = new List<StrategyGraphInfo>()
                {
                    new StrategyGraphInfo()
                    {
                        InputsToGraph = new List<InputValueVaryInGraph>()
                        {
                            new InputValueVaryInGraph() { InputAbbreviation= isPDecision ? "PEstProb" : "DEstProbDWin", MinValue=0.0, MaxValue=1.0, NumValues = 41 }
                        },
                        InputsToFix = new List<InputValueFixInGraph>()
                        {
                        },
                        OutputReportFilename="UtilRange.txt",
                        SettingsFor2DGraph = new Graph2DSettings() { xMin = 0, xMax = 1.0,
                            xAxisLabel="Party's estimate of probability it wins",
                            yAxisLabel="Party's projection of change in opponent's utility",
                            graphName="Projection of opponent's delta utility (if settlement blocked at this stage)",
                            seriesName= (isPDecision  ? "Plaintiff projection" : "Defendant projection"), replaceSeriesOfSameName=false, fadeSeriesOfSameName=true, exportFramesOfMovies= false},
                           
                        ReportAfterEachEvolutionStep = true
                    }
                };
            }
            if (name.Contains("Wealth"))
            {
                theDecision.StrategyGraphInfos = new List<StrategyGraphInfo>()
                {
                    new StrategyGraphInfo()
                    {
                        InputsToGraph = new List<InputValueVaryInGraph>()
                        {
                            new InputValueVaryInGraph() { InputAbbreviation= "deltaU", MinValue=-10.0, MaxValue=10.0, NumValues = 41 }
                        },
                        InputsToFix = new List<InputValueFixInGraph>()
                        {
                        },
                        OutputReportFilename="WealthRange.txt",
                        SettingsFor2DGraph = new Graph2DSettings() { xMin = -10.0, xMax = 10.0,
                            xAxisLabel="Delta utility",
                            yAxisLabel="Delta wealth",
                            graphName="Wealth equivalent of delta utility (if settlement blocked at this stage)",
                            seriesName= name, replaceSeriesOfSameName=false, fadeSeriesOfSameName=true, exportFramesOfMovies= false },
                        ReportAfterEachEvolutionStep = true
                    }
                };
            }
            if (name == "PProjectionPAbsErrorInSettingThreatPoint" || name == "DProjectionDAbsErrorInSettingThreatPoint")
            {
                theDecision.StrategyGraphInfos = new List<StrategyGraphInfo>()
                {
                    new StrategyGraphInfo()
                    {
                        
                        InputsToGraph = new List<InputValueVaryInGraph>()
                        {
                            UtilityRangeBargainingModuleSettings.EvolvingRangeUncertaintySettings ?
                                new InputValueVaryInGraph() { InputAbbreviation= isPDecision ? "PNoiseProb" : "DNoiseProb", MinValue=0.0, MaxValue= 1.0, NumValues = 41 }
                                :
                                                           
                                new InputValueVaryInGraph() { InputAbbreviation= isPDecision ? "PEstProb" : "DEstProbDWin", MinValue=0.0, MaxValue=1.0, NumValues = 41 }

                        },
                        OutputReportFilename="Uncertainty.txt",
                        SettingsFor2DGraph = new Graph2DSettings() { 
                            xAxisLabel=UtilityRangeBargainingModuleSettings.EvolvingRangeUncertaintySettings ? "Party's noise level" : "Party's estimate of probability it wins",
                            yAxisLabel="Est of own abs err in setting threat point",
                            graphName="Uncertainty in determining own threat point",
                            seriesName= (isPDecision  ? "Plaintiff projection" : "Defendant projection"), 
                            replaceSeriesOfSameName=false, 
                            fadeSeriesOfSameName=true, 
                            exportFramesOfMovies= false
                        },
                            
                           
                        ReportAfterEachEvolutionStep = true
                    }
                };
            }
            if (name == "PProjectionDAbsErrorInSettingThreatPoint" || name == "DProjectionPAbsErrorInSettingThreatPoint")
            {
                theDecision.StrategyGraphInfos = new List<StrategyGraphInfo>()
                {
                    new StrategyGraphInfo()
                    {
                        InputsToGraph = new List<InputValueVaryInGraph>()
                        {
                            UtilityRangeBargainingModuleSettings.EvolvingRangeUncertaintySettings ?
                                new InputValueVaryInGraph() { InputAbbreviation= isPDecision ? "PEstDNoiseProb" : "DEstPNoiseProb", MinValue=0.0, MaxValue= 1.0, NumValues = 41 }
                                :
                                                           
                                new InputValueVaryInGraph() { InputAbbreviation= isPDecision ? "PEstProb" : "DEstProbDWin", MinValue=0.0, MaxValue=1.0, NumValues = 41 }

                        },
                        OutputReportFilename="Uncertainty2.txt",
                        SettingsFor2DGraph = new Graph2DSettings() { 
                            xAxisLabel=UtilityRangeBargainingModuleSettings.EvolvingRangeUncertaintySettings ? "Opponent's noise level" : "Party's estimate of probability it wins",
                            yAxisLabel="Est of opponent's abs err in determining threat point ",
                            graphName="Estimated uncertainty of opponent's threat point",
                            seriesName= (isPDecision  ? "Plaintiff projection" : "Defendant projection"), 
                            replaceSeriesOfSameName=false, 
                            fadeSeriesOfSameName=true, 
                            exportFramesOfMovies= false
                        },
                           
                        ReportAfterEachEvolutionStep = true
                    }
                };

            }
            return theDecision;
        }
        public override List<ActionGroup> GetActionGroupsForModule()
        {
            ActionGroup actionGroup1 = CreateActionGroup(DecisionsCore, ActionsAtBeginningOfModule, null);
            actionGroup1.Name = "BargainingModulePrepAndProject";


            ActionGroup actionGroup2 = CreateActionGroup(null, null, ActionsAtEndOfModule);
            actionGroup2.Name = "BargainingModuleSettleAfterAggressiveness";

            // relative order between this and other modules is set in BargainingAggressivenessModule, b/c this pattern could be repeated

            return new List<ActionGroup>() { actionGroup1, actionGroup2 };
        }

        // Determine relative order within BargainingModulePrepAndProject modules for evolution, where we want threat points done last
        public override OrderingConstraint? DetermineOrderingConstraint(List<ActionGroup> originalList, ActionGroup actionGroupWithinThisModule, ActionGroup secondActionGroup, bool forEvolution)
        {
            if (forEvolution && actionGroupWithinThisModule.Name.Contains("BargainingModulePrepAndProject") && secondActionGroup.Name.Contains("BargainingModulePrepAndProject") && actionGroupWithinThisModule.RepetitionTagString() == secondActionGroup.RepetitionTagString())
            {
                int evolutionOrderThisModule = Convert.ToInt32(actionGroupWithinThisModule.Name.Substring(actionGroupWithinThisModule.Name.Length - 1));
                int evolutionOrderSecondActionGroup = Convert.ToInt32(secondActionGroup.Name.Substring(secondActionGroup.Name.Length - 1));
                if (evolutionOrderThisModule == evolutionOrderSecondActionGroup - 1)
                    return OrderingConstraint.ImmediatelyBefore;
                else if (evolutionOrderThisModule < evolutionOrderSecondActionGroup)
                    return OrderingConstraint.Before;
                else
                    return null;
            }
            if (forEvolution && secondActionGroup.Name.Contains("BeginningDropOrDefault"))
                return OrderingConstraint.Before;
            if (forEvolution && secondActionGroup.Name.Contains("EndDropOrDefault"))
                return OrderingConstraint.After;
            if (forEvolution && secondActionGroup.Name.Contains("Adjustments"))
                return OrderingConstraint.Before;
            return null;
        }
        #endregion

    }
}
