using ACESim.Util;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;

namespace ACESim
{
    /// <summary>
    /// A dummy Game subclass to show a basic Game implementation.
    /// </summary>
    public class LitigGame : Game
    {
        public LitigGameDefinition LitigGameDefinition => (LitigGameDefinition)GameDefinition;
        public LitigGameProgress LitigGameProgress => (LitigGameProgress)Progress;

        public LitigGame(List<Strategy> strategies,
            GameProgress progress,
            GameDefinition gameDefinition,
            bool recordReportInfo,
            bool restartFromBeginningOfGame,
            bool fullHistoryRequired
            ) : base(strategies, progress, gameDefinition, recordReportInfo, restartFromBeginningOfGame, fullHistoryRequired)
        {
            
        }

        public override void Initialize()
        {
        }

        public override void UpdateGameProgressFollowingAction(byte currentDecisionByteCode, byte action)
        {
            if (LitigGameDefinition.Options.LitigGameDisputeGenerator.HandleUpdatingGameProgress(LitigGameProgress, currentDecisionByteCode, action))
            {
                return;
            }
            switch (currentDecisionByteCode)
            {
                case (byte)LitigGameDecisions.LiabilityStrength:
                    LitigGameProgress.LiabilityStrengthDiscrete = action;
                break;
                case (byte)LitigGameDecisions.PLiabilitySignal:
                    LitigGameProgress.PLiabilitySignalDiscrete = action;
                    GameProgressLogger.Log(() => $"P: Liability Strength {LitigGameProgress.LiabilityStrengthDiscrete} => signal {LitigGameProgress.PLiabilitySignalDiscrete}");
                break;
                case (byte)LitigGameDecisions.DLiabilitySignal:
                    LitigGameProgress.DLiabilitySignalDiscrete = action;
                    GameProgressLogger.Log(() => $"D: Liability Strength {LitigGameProgress.LiabilityStrengthDiscrete} => signal {LitigGameProgress.DLiabilitySignalDiscrete}");
                    break;
                case (byte)LitigGameDecisions.DamagesStrength:
                    LitigGameProgress.DamagesStrengthDiscrete = action;
                    break;
                case (byte)LitigGameDecisions.PDamagesSignal:
                    LitigGameProgress.PDamagesSignalDiscrete = action;
                    GameProgressLogger.Log(() => $"P: Damages Strength {LitigGameProgress.DamagesStrengthDiscrete} => signal {LitigGameProgress.PDamagesSignalDiscrete}");
                    break;
                case (byte)LitigGameDecisions.DDamagesSignal:
                    LitigGameProgress.DDamagesSignalDiscrete = action;
                    GameProgressLogger.Log(() => $"D: Damages Strength {LitigGameProgress.DamagesStrengthDiscrete} => signal {LitigGameProgress.DDamagesSignalDiscrete}");
                    break;
                case (byte)LitigGameDecisions.PFile:
                    LitigGameProgress.PFiles = action == 1;
                    if (!LitigGameProgress.PFiles)
                        LitigGameProgress.GameComplete = true;
                    break;
                case (byte)LitigGameDecisions.DAnswer:
                    LitigGameProgress.DAnswers = action == 1;
                    if (!LitigGameProgress.DAnswers)
                        LitigGameProgress.GameComplete = true;
                    break;
                case (byte)LitigGameDecisions.PreBargainingRound:
                    if (LitigGameDefinition.Options.SkipFileAndAnswerDecisions)
                    {
                        LitigGameProgress.PFiles = LitigGameProgress.DAnswers = true;
                    }
                    break;
                case (byte)LitigGameDecisions.PAgreeToBargain:
                    LitigGameProgress.AddPAgreesToBargain(action == 1);
                    break;
                case (byte)LitigGameDecisions.DAgreeToBargain:
                    LitigGameProgress.AddDAgreesToBargain(action == 1);
                    break;
                case (byte)LitigGameDecisions.POffer:
                    double offer = GetOfferBasedOnAction(action, true, LitigGameDefinition.Options.IncludeEndpointsForOffers);
                    LitigGameProgress.AddOffer(true, offer);
                    LitigGameProgress.AddOfferMixedness(true, LitigGameProgress.Mixedness);
                break;
                case (byte)LitigGameDecisions.DOffer:
                    offer = GetOfferBasedOnAction(action, false, LitigGameDefinition.Options.IncludeEndpointsForOffers);
                    LitigGameProgress.AddOffer(false, offer);
                    LitigGameProgress.AddOfferMixedness(false, LitigGameProgress.Mixedness);
                    if (LitigGameDefinition.Options.BargainingRoundsSimultaneous || LitigGameDefinition.Options.PGoesFirstIfNotSimultaneous[LitigGameProgress.BargainingRoundsComplete])
                    {
                        ConcludeMainPortionOfBargainingRound();
                    }
                    break;
                case (byte)LitigGameDecisions.PResponse:
                    LitigGameProgress.AddResponse(true, action == 1); // 1 == accept, 2 == reject
                    ConcludeMainPortionOfBargainingRound();
                    break;
                case (byte)LitigGameDecisions.DResponse:
                    LitigGameProgress.AddResponse(false, action == 1); // 1 == accept, 2 == reject
                    ConcludeMainPortionOfBargainingRound();
                    break;
                case (byte)LitigGameDecisions.PChips:
                    LitigGameProgress.RunningSideBetsActions.TemporaryStoragePMixedness = LitigGameProgress.Mixedness;
                    // don't do anything else yet -- the decision specifies that the action should be stored in the cache
                    break;
                case (byte)LitigGameDecisions.DChips:
                    LitigGameDefinition.Options.LitigGameRunningSideBets.SaveRunningSideBets(LitigGameDefinition, LitigGameProgress, action);
                    break;
                case (byte)LitigGameDecisions.PAbandon:
                    LitigGameProgress.PReadyToAbandon = action == 1;
                    break;
                case (byte)LitigGameDecisions.DDefault:
                    LitigGameProgress.DReadyToDefault = action == 1;
                    if (!LitigGameDefinition.Options.PredeterminedAbandonAndDefaults)
                        CheckOnePartyGivesUp();
                    break;
                case (byte)LitigGameDecisions.MutualGiveUp:
                    if (!LitigGameProgress.GameComplete)
                    {
                        LitigGameProgress.ResolveMutualGiveUp(action);
                    }
                    break;
                case (byte)LitigGameDecisions.PostBargainingRound:
                    LitigGameProgress.BargainingRoundsComplete++; // executed if the game has NOT yet completed
                    break;
                case (byte)LitigGameDecisions.PPretrialAction:
                    LitigGameDefinition.Options.LitigGamePretrialDecisionGeneratorGenerator.ProcessAction(LitigGameDefinition, LitigGameProgress, true, action);
                    break;
                case (byte)LitigGameDecisions.DPretrialAction:
                    LitigGameDefinition.Options.LitigGamePretrialDecisionGeneratorGenerator.ProcessAction(LitigGameDefinition, LitigGameProgress, false, action);
                    break;
                case (byte)LitigGameDecisions.CourtDecisionLiability:
                    LitigGameProgress.CourtReceivesLiabilitySignal(action, LitigGameDefinition);
                    //TabbedText.WriteLine($"Quality {MyProgress.LiabilityStrengthUniform} Court noise action {action} => {courtNoiseNormalDraw} => signal {courtLiabilitySignal} PWins {MyProgress.PWinsAtTrial}");
                    break;
                case (byte)LitigGameDecisions.CourtDecisionDamages:
                    LitigGameProgress.CourtReceivesDamagesSignal(action, LitigGameDefinition);
                    break;
                default:
                    throw new NotImplementedException();
            }
        }


        private void ConcludeMainPortionOfBargainingRound()
        {
            LitigGameProgress.ConcludeMainPortionOfBargainingRound(LitigGameDefinition);
            if (!LitigGameProgress.GameComplete && LitigGameDefinition.Options.PredeterminedAbandonAndDefaults && LitigGameDefinition.Options.AllowAbandonAndDefaults) 
                CheckOnePartyGivesUp(); // note that this could be any bargaining round -- we haven't limited allow abandon and defaults to the last round
            CheckCollapseFinalChanceDecisions();
        }

        private void CheckCollapseFinalChanceDecisions()
        {
            if (LitigGameProgress.GameComplete)
                return;
            LitigGameOptions options = LitigGameDefinition.Options;
            if (options.CollapseChanceDecisions && options.CollapseAlternativeEndings)
            {
                if (options.NumPotentialBargainingRounds > 1)
                    throw new NotImplementedException(); // if changing, must also change ShouldMarkGameHistoryComplete. We need in that code to be able to figure out which bargaining round it is
                if (options.AllowAbandonAndDefaults && !options.PredeterminedAbandonAndDefaults)
                    throw new NotImplementedException(); // in this case, we need to call this method after DDefault or perhaps after MutualGiveUp

                int numBargainingRoundsComplete = LitigGameProgress.BargainingRoundsComplete + 1; // haven't yet registered the completed round
                if (numBargainingRoundsComplete < options.NumPotentialBargainingRounds)
                    return;

                bool isEffectivelyComplete;
                if (options.AllowAbandonAndDefaults)
                {
                    isEffectivelyComplete = options.PredeterminedAbandonAndDefaults; 
                }
                else
                {
                    isEffectivelyComplete = true; // we've completed offers, and there are no abandonment decisions.
                }

                if (isEffectivelyComplete)
                {
                    LitigGameProgress.BargainingRoundsComplete++;
                    if (LitigGameProgress.PReadyToAbandon && LitigGameProgress.DReadyToDefault)
                    {
                        // Note: We won't get this far if just one is ready to go. This is the scenario that would be handled, if we weren't collapsing final decisions, by MutualGiveUp.
                        var scenario1 = LitigGameProgress.DeepCopy();
                        scenario1.ResolveMutualGiveUp((byte)1);
                        scenario1.CalculateGameOutcome();
                        var scenario2 = LitigGameProgress.DeepCopy();
                        scenario2.ResolveMutualGiveUp((byte)2);
                        scenario2.CalculateGameOutcome();
                        LitigGameProgress.AlternativeEndings = new List<(LitigGameProgress completedGame, double weight)>() { (scenario1, 0.5), (scenario2, 0.5) };
                    }
                    else
                    {
                        LitigGameProgress.AlternativeEndings = new List<(LitigGameProgress completedGame, double weight)>();
                        double[] liabilityProbabilities, damagesProbabilities;
                        if (options.NumCourtLiabilitySignals == 1)
                        {
                            liabilityProbabilities = new double[] { 1.0 };
                            LitigGameProgress.PWinsAtTrial = true;
                        }
                        else
                            liabilityProbabilities = LitigGameDefinition.GetUnevenChanceActionProbabilities((byte)LitigGameDecisions.CourtDecisionLiability, LitigGameProgress);
                        if (options.NumDamagesSignals == 1)
                            damagesProbabilities = new double[] { 1.0 };
                        else
                            damagesProbabilities = LitigGameDefinition.GetUnevenChanceActionProbabilities((byte)LitigGameDecisions.CourtDecisionDamages, LitigGameProgress);

                        for (int liabilityProbabilityIndex = 0; liabilityProbabilityIndex < liabilityProbabilities.Length; liabilityProbabilityIndex++)
                        {
                            for (int damagesProbabilityIndex = 0; damagesProbabilityIndex < damagesProbabilities.Length; damagesProbabilityIndex++)
                            {
                                double weight = liabilityProbabilities[liabilityProbabilityIndex] * damagesProbabilities[damagesProbabilityIndex];
                                byte liabilityAction = (byte)(liabilityProbabilityIndex + 1);
                                byte damagesAction = (byte)(damagesProbabilityIndex + 1);
                                LitigGameProgress playedOut = LitigGameProgress.DeepCopy();
                                playedOut.AlternativeEndings = null;
                                playedOut.CourtReceivesLiabilitySignal(liabilityAction, LitigGameDefinition);
                                playedOut.CourtReceivesDamagesSignal(damagesAction, LitigGameDefinition);
                                if (!playedOut.GameComplete)
                                    throw new Exception();
                                playedOut.CalculateGameOutcome();
                                LitigGameProgress.AlternativeEndings.Add((playedOut, weight));
                                // TabbedText.WriteLine($"Collapsed ending {weight} {playedOut.PFinalWealth}, {playedOut.DFinalWealth}");
                            }
                        }
                    }
                    LitigGameProgress.WeightAlternativeEndings();
                    LitigGameProgress.GameComplete = true;
                }
            }
        }

        private void CheckOnePartyGivesUp()
        {
            if (LitigGameProgress.PReadyToAbandon ^ LitigGameProgress.DReadyToDefault)
            {
                // exactly one party gives up
                LitigGameProgress.PAbandons = LitigGameProgress.PReadyToAbandon;
                LitigGameProgress.DDefaults = LitigGameProgress.DReadyToDefault;
                LitigGameProgress.TrialOccurs = false;
                LitigGameProgress.GameComplete = true;
                LitigGameProgress.BargainingRoundsComplete++; // we won't get to PostBargainingRound
            }
        }

        /// <summary>
        /// Converts a discrete action representing an offer into a dollar offer by one of the parties
        /// </summary>
        /// <param name="action"></param>
        /// <param name="plaintiffOffer">True if this is the plaintiffs offer, false otherwise</param>
        /// <returns></returns>
        private double GetOfferBasedOnAction(byte action, bool plaintiffOffer, bool includeEndpoints)
        {
            double offer;
            if (LitigGameProgress.BargainingRoundsComplete == 0 || !LitigGameDefinition.Options.DeltaOffersOptions.SubsequentOffersAreDeltas)
                offer = ConvertActionToUniformDistributionDraw(action, includeEndpoints);
            else
            {
                double? previousOffer = plaintiffOffer ? LitigGameProgress.PLastOffer : LitigGameProgress.DLastOffer;
                if (previousOffer == null)
                    offer = ConvertActionToUniformDistributionDraw(action, includeEndpoints);
                else
                    offer = LitigGameDefinition.Options.DeltaOffersCalculation.GetOfferValue((double) previousOffer, action);
            }
            return offer;
        }

        public class LitigGameOutcome
        {
            public double PChangeWealth;
            public double DChangeWealth;
            public double PFinalWealth;
            public double DFinalWealth;
            public double PWelfare;
            public double DWelfare;
            public bool TrialOccurs;
            public bool PWinsAtTrial;
            public double DamagesAwarded;
            public byte NumChips;
        }

        public static LitigGameOutcome CalculateGameOutcome(LitigGameDefinition gameDefinition, LitigGameStandardDisputeGeneratorActions disputeGeneratorActions, LitigGamePretrialActions pretrialActions, LitigGameRunningSideBetsActions runningSideBetActions, double pInitialWealth, double dInitialWealth, bool pFiles, bool pAbandons, bool dAnswers, bool dDefaults, double? settlementValue, bool pWinsAtTrial, bool largeMarginAtTrial, double? damagesAwarded, byte bargainingRoundsComplete, double? pFinalWealthWithBestOffer, double? dFinalWealthWithBestOffer, List<double> pOffers, List<bool> pResponses, List<double> dOffers, List<bool> dResponses, LitigGameProgress gameProgress)
        {
            LitigGameOutcome outcome = new LitigGameOutcome();

            double[] changeWealthOutsideLitigation = gameDefinition.Options.LitigGameDisputeGenerator.GetLitigationIndependentWealthEffects(gameDefinition, disputeGeneratorActions, gameProgress);
            double pWealthAfterPrimaryConduct = pInitialWealth + changeWealthOutsideLitigation[0];
            double dWealthAfterPrimaryConduct = dInitialWealth + changeWealthOutsideLitigation[1];

            if (!pFiles || pAbandons)
            {
                outcome.PChangeWealth = outcome.DChangeWealth = 0;
                outcome.TrialOccurs = false;
            }
            else if (!dAnswers || dDefaults)
            { // defendant pays full damages (but no trial costs)
                outcome.PChangeWealth += gameDefinition.Options.DamagesMax * gameDefinition.Options.DamagesMultiplier;
                outcome.DChangeWealth -= gameDefinition.Options.DamagesMax * gameDefinition.Options.DamagesMultiplier;
                outcome.TrialOccurs = false;
            }
            else if (settlementValue != null)
            {
                outcome.PChangeWealth = (double)settlementValue;
                outcome.DChangeWealth = 0 - (double)settlementValue;
                outcome.TrialOccurs = false;
            }
            else
            {
                outcome.TrialOccurs = true;
                outcome.DamagesAwarded = pWinsAtTrial ? (double) damagesAwarded : 0;
                outcome.PChangeWealth = (pWinsAtTrial ? outcome.DamagesAwarded : 0);
                outcome.DChangeWealth = (pWinsAtTrial ? -outcome.DamagesAwarded : 0);
                outcome.PWinsAtTrial = pWinsAtTrial;
            }

            if (gameDefinition.Options.LitigGamePretrialDecisionGeneratorGenerator != null)
            {
                gameDefinition.Options.LitigGamePretrialDecisionGeneratorGenerator.GetEffectOnPlayerWelfare(gameDefinition, outcome.TrialOccurs, pWinsAtTrial, gameDefinition.Options.DamagesMax * gameDefinition.Options.DamagesMultiplier, pretrialActions, out double effectOnP, out double effectOnD);
                outcome.PChangeWealth += effectOnP;
                outcome.DChangeWealth += effectOnD;
            }

            double trialCostsMultiplier = 1.0;
            if (gameDefinition.Options.LitigGameRunningSideBets != null)
            {
                byte? roundOfAbandonment = (pAbandons || dDefaults) ? (byte?)bargainingRoundsComplete : null;
                gameDefinition.Options.LitigGameRunningSideBets.GetEffectOnPlayerWelfare(gameDefinition, roundOfAbandonment, pAbandons, dDefaults, outcome.TrialOccurs, pWinsAtTrial, runningSideBetActions, out double effectOnP, out double effectOnD, out byte totalChipsThatCount);
                outcome.PChangeWealth += effectOnP;
                outcome.DChangeWealth += effectOnD;
                outcome.NumChips = totalChipsThatCount;
                if (outcome.TrialOccurs)
                    trialCostsMultiplier = gameDefinition.Options.LitigGameRunningSideBets.GetTrialCostsMultiplier(gameDefinition, totalChipsThatCount);
            }

            if (gameDefinition.Options.ShootoutSettlements)
            {
                if (gameDefinition.Options.IncludeAgreementToBargainDecisions)
                    throw new NotSupportedException(); // shootout settlements require bargaining
                bool abandoned = (pAbandons || dDefaults);
                bool abandonedAfterLastRound = abandoned && bargainingRoundsComplete == gameDefinition.Options.NumPotentialBargainingRounds;
                bool applyShootout = outcome.TrialOccurs || (abandonedAfterLastRound && gameDefinition.Options.ShootoutsApplyAfterAbandonment) || (abandoned && gameDefinition.Options.ShootoutsApplyAfterAbandonment && gameDefinition.Options.ShootoutOfferValueIsAveraged);
                if (applyShootout)
                {
                    double shootoutOffer;
                    if (gameDefinition.Options.BargainingRoundsSimultaneous)
                    {
                        if (gameDefinition.Options.ShootoutOfferValueIsAveraged)
                        {
                            double averagePOffer = pOffers.Average();
                            double averageDOffer = dOffers.Average();
                            shootoutOffer = (averagePOffer + averageDOffer) / 2.0;
                        }
                        else
                        {
                            double lastPOffer = pOffers.Last();
                            double lastDOffer = dOffers.Last();
                            shootoutOffer = (lastPOffer + lastDOffer) / 2.0;
                        }
                    }
                    else
                    {
                        List<double> applicableOffers = new List<double>();
                        if (gameDefinition.Options.ShootoutOfferValueIsAveraged)
                        {
                            if (pOffers != null)
                                applicableOffers.AddRange(pOffers);
                            if (dOffers != null)
                                applicableOffers.AddRange(dOffers);
                        }
                        else
                        {
                            if (pOffers != null && pOffers.Any())
                                applicableOffers.Add(pOffers.Last());
                            if (dOffers != null && dOffers.Any())
                                applicableOffers.Add(dOffers.Last());
                        }
                        shootoutOffer = applicableOffers.Average();
                    }
                    double shootoutStrength = gameDefinition.Options.ShootoutStrength;
                    double costToP = shootoutOffer * gameDefinition.Options.DamagesMax * gameDefinition.Options.DamagesMultiplier * shootoutStrength;
                    double extraDamages = (dDefaults ? gameDefinition.Options.DamagesMax * gameDefinition.Options.DamagesMultiplier : (damagesAwarded ?? 0)) * shootoutStrength;
                    double netBenefitToP = extraDamages - costToP;
                    outcome.PChangeWealth += netBenefitToP;
                    outcome.DChangeWealth -= netBenefitToP;
                }
            }

            double costsMultiplier = gameDefinition.Options.CostsMultiplier;
            double pFilingCostIncurred = 0;
            if (pFiles)
            {
                pFilingCostIncurred = gameDefinition.Options.PFilingCost * costsMultiplier;
                if (!dAnswers)
                    pFilingCostIncurred -= pFilingCostIncurred * gameDefinition.Options.PFilingCost_PortionSavedIfDDoesntAnswer;
            }
            double dAnswerCostIncurred = dAnswers ? gameDefinition.Options.DAnswerCost * costsMultiplier : 0;
            double pTrialCostsIncurred = outcome.TrialOccurs ? gameDefinition.Options.PTrialCosts * costsMultiplier * trialCostsMultiplier : 0;
            double dTrialCostsIncurred = outcome.TrialOccurs ? gameDefinition.Options.DTrialCosts * costsMultiplier * trialCostsMultiplier : 0;
            double pBargainingCostsIncurred = 0, dBargainingCostsIncurred = 0;
            if (gameDefinition.Options.RoundSpecificBargainingCosts is (double pCosts, double dCosts)[] roundSpecific)
            {
                for (int i = 0; i < bargainingRoundsComplete; i++)
                {
                    pBargainingCostsIncurred += roundSpecific[i].pCosts * costsMultiplier;
                    dBargainingCostsIncurred += roundSpecific[i].dCosts * costsMultiplier;
                }
            }
            else
            {
                pBargainingCostsIncurred = dBargainingCostsIncurred = gameDefinition.Options.PerPartyCostsLeadingUpToBargainingRound * costsMultiplier * bargainingRoundsComplete;
            }
            double pCostsInitiallyIncurred = pFilingCostIncurred + pBargainingCostsIncurred + pTrialCostsIncurred;
            double dCostsInitiallyIncurred = dAnswerCostIncurred + dBargainingCostsIncurred + dTrialCostsIncurred;
            double pEffectOfExpenses = 0, dEffectOfExpenses = 0;
            bool loserPaysApplies = false;
            bool punishPlaintiffUnderRule68 = false;
            if (gameDefinition.Options.Rule68 && outcome.TrialOccurs && outcome.PWinsAtTrial)
            {
                if (dOffers != null && dOffers.Any())
                { // there might not be offers if there was a refusal to bargain or if plaintiff is giving an offer and defendant is replying
                    double rule68Offer = dOffers.Last();
                    punishPlaintiffUnderRule68 = rule68Offer >= damagesAwarded; // NOTE: We are saying punish plaintiff if amount ends up being equal. But this will not occur if the only issue is liability.
                }
            }
            if (gameDefinition.Options.LoserPays && gameDefinition.Options.LoserPaysMultiple > 0)
            {
                loserPaysApplies = ((outcome.TrialOccurs && (!gameDefinition.Options.LoserPaysOnlyLargeMarginOfVictory || largeMarginAtTrial)) 
                    || 
                    (gameDefinition.Options.LoserPaysAfterAbandonment && (pAbandons || dDefaults)));
                // NOTE: If punishPlaintiffUnderRule68, then plaintiff has won and usually would be entitled to fee shifting, but because of Rule 68, now defendant is entitled to fee shifting. So, loser pays still applies.
            }
            else
            {
                if (punishPlaintiffUnderRule68)
                { // plaintiff has won but still has to pay the defendant's fees.
                    loserPaysApplies = true;
                }
            }
            if (loserPaysApplies)
            { // British Rule and it applies (contested litigation and no settlement) -- or punishing plaintiff under Rule 68 despite American rule, in which case we still use the loser pays multiple
                double loserPaysMultiple = gameDefinition.Options.LoserPaysMultiple;
                bool pLoses = (outcome.TrialOccurs && !pWinsAtTrial) || pAbandons;
                if (pLoses || punishPlaintiffUnderRule68)
                {
                    pEffectOfExpenses -= pCostsInitiallyIncurred + loserPaysMultiple * dCostsInitiallyIncurred;
                    dEffectOfExpenses -= dCostsInitiallyIncurred - loserPaysMultiple * dCostsInitiallyIncurred;
                }
                else
                {
                    pEffectOfExpenses -= pCostsInitiallyIncurred - loserPaysMultiple * pCostsInitiallyIncurred;
                    dEffectOfExpenses -= dCostsInitiallyIncurred + loserPaysMultiple * pCostsInitiallyIncurred;
                }
            }
            else
            { // American rule
                pEffectOfExpenses -= pFilingCostIncurred + pBargainingCostsIncurred + pTrialCostsIncurred;
                dEffectOfExpenses -= dAnswerCostIncurred + dBargainingCostsIncurred + dTrialCostsIncurred;
            }

            outcome.PChangeWealth += pEffectOfExpenses;
            outcome.DChangeWealth += dEffectOfExpenses;

            outcome.PFinalWealth = pWealthAfterPrimaryConduct + outcome.PChangeWealth;
            outcome.DFinalWealth = dWealthAfterPrimaryConduct + outcome.DChangeWealth;
            double pPerceivedFinalWealth = outcome.PFinalWealth;
            double dPerceivedFinalWealth = outcome.DFinalWealth;
            if (gameDefinition.Options.RegretAversion != 0)
            {
                if (pFinalWealthWithBestOffer > outcome.PFinalWealth)
                    pPerceivedFinalWealth -= gameDefinition.Options.RegretAversion * ((double) pFinalWealthWithBestOffer - outcome.PFinalWealth);
                if (dFinalWealthWithBestOffer > outcome.DFinalWealth)
                    dPerceivedFinalWealth -= gameDefinition.Options.RegretAversion * ((double)dFinalWealthWithBestOffer - outcome.DFinalWealth);
            }
            outcome.PWelfare =
                gameDefinition.Options.PUtilityCalculator.GetSubjectiveUtilityForWealthLevel(pPerceivedFinalWealth);
            outcome.DWelfare =
                gameDefinition.Options.DUtilityCalculator.GetSubjectiveUtilityForWealthLevel(dPerceivedFinalWealth);
            return outcome;
        }

        public override void FinalProcessing()
        {
            LitigGameProgress.CalculateGameOutcome();

            base.FinalProcessing();
        }
    }
}
