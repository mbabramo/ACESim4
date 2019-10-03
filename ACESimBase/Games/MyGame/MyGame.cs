﻿using ACESim.Util;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ACESim
{
    /// <summary>
    /// A dummy Game subclass to show a basic Game implementation.
    /// </summary>
    public class MyGame : Game
    {
        public MyGameDefinition MyDefinition => (MyGameDefinition)GameDefinition;
        public MyGameProgress MyProgress => (MyGameProgress)Progress;

        public MyGame()
        {
            
        }

        public override void Initialize()
        {
            MyProgress.PInitialWealth = MyDefinition.Options.PInitialWealth;
            MyProgress.DInitialWealth = MyDefinition.Options.DInitialWealth;
        }

        public override void UpdateGameProgressFollowingAction(byte currentDecisionByteCode, byte action)
        {
            switch (currentDecisionByteCode)
            {
                case (byte)MyGameDecisions.PrePrimaryActionChance:
                    MyProgress.DisputeGeneratorActions.PrePrimaryChanceAction = action;
                    break;
                case (byte)MyGameDecisions.PrimaryAction:
                    MyProgress.DisputeGeneratorActions.PrimaryAction = action;
                    if (MyDefinition.CheckCompleteAfterPrimaryAction && MyDefinition.Options.MyGameDisputeGenerator.MarkComplete(MyDefinition, MyProgress.DisputeGeneratorActions.PrePrimaryChanceAction, MyProgress.DisputeGeneratorActions.PrimaryAction))
                        MyProgress.GameComplete = true;
                    break;
                case (byte)MyGameDecisions.PostPrimaryActionChance:
                    MyProgress.DisputeGeneratorActions.PostPrimaryChanceAction = action;
                    if (MyDefinition.CheckCompleteAfterPostPrimaryAction && MyDefinition.Options.MyGameDisputeGenerator.MarkComplete(MyDefinition, MyProgress.DisputeGeneratorActions.PrePrimaryChanceAction, MyProgress.DisputeGeneratorActions.PrimaryAction, MyProgress.DisputeGeneratorActions.PostPrimaryChanceAction))
                        MyProgress.GameComplete = true;
                    break;
                case (byte)MyGameDecisions.LiabilityStrength:
                    MyProgress.DamagesMin = MyDefinition.Options.DamagesMin;
                    MyProgress.DamagesMax = MyDefinition.Options.DamagesMax;
                    MyProgress.LiabilityStrengthDiscrete = action;
                    MyProgress.LiabilityStrengthUniform = ConvertActionToUniformDistributionDraw(action, false);
                    // If one or both parties have perfect information, then they can get their information about litigation quality now, since they don't need a signal. Note that we also specify in the game definition that the litigation quality should become part of their information set.
                    if (MyDefinition.Options.PLiabilityNoiseStdev == 0)
                        MyProgress.PLiabilitySignalUniform = (double) MyProgress.LiabilityStrengthUniform;
                    if (MyDefinition.Options.DLiabilityNoiseStdev == 0)
                        MyProgress.DLiabilitySignalUniform = (double) MyProgress.LiabilityStrengthUniform;
                break;
                case (byte)MyGameDecisions.PLiabilitySignal:
                    MyProgress.PLiabilitySignalDiscrete = action;
                    MyProgress.PLiabilitySignalUniform = action * (1.0 / MyDefinition.Options.NumLiabilitySignals);
                    GameProgressLogger.Log(() => $"P: Liability Strength {MyProgress.LiabilityStrengthUniform} => signal {MyProgress.PLiabilitySignalDiscrete} ({MyProgress.PLiabilitySignalUniform})");
                break;
                case (byte)MyGameDecisions.DLiabilitySignal:
                    MyProgress.DLiabilitySignalDiscrete = action;
                    MyProgress.DLiabilitySignalUniform = action * (1.0 / MyDefinition.Options.NumLiabilitySignals);
                    GameProgressLogger.Log(() => $"D: Liability Strength {MyProgress.LiabilityStrengthUniform} => signal {MyProgress.DLiabilitySignalDiscrete} ({MyProgress.DLiabilitySignalUniform})");
                    break;
                case (byte)MyGameDecisions.DamagesStrength:
                    MyProgress.DamagesMax = MyDefinition.Options.DamagesMax;
                    MyProgress.DamagesStrengthDiscrete = action;
                    MyProgress.DamagesStrengthUniform = ConvertActionToUniformDistributionDraw(action, true /* include endpoints so that we can have possibility of max or min damages */);
                    // If one or both parties have perfect information, then they can get their information about litigation quality now, since they don't need a signal. Note that we also specify in the game definition that the litigation quality should become part of their information set.
                    if (MyDefinition.Options.PDamagesNoiseStdev == 0)
                        MyProgress.PDamagesSignalUniform = (double)MyProgress.DamagesStrengthUniform;
                    if (MyDefinition.Options.DDamagesNoiseStdev == 0)
                        MyProgress.DDamagesSignalUniform = (double)MyProgress.DamagesStrengthUniform;
                    break;
                case (byte)MyGameDecisions.PDamagesSignal:
                    MyProgress.PDamagesSignalDiscrete = action;
                    MyProgress.PDamagesSignalUniform = action * (1.0 / MyDefinition.Options.NumDamagesSignals);
                    GameProgressLogger.Log(() => $"P: Damages Strength {MyProgress.DamagesStrengthUniform} => signal {MyProgress.PDamagesSignalDiscrete} ({MyProgress.PDamagesSignalUniform})");
                    break;
                case (byte)MyGameDecisions.DDamagesSignal:
                    MyProgress.DDamagesSignalDiscrete = action;
                    MyProgress.DDamagesSignalUniform = action * (1.0 / MyDefinition.Options.NumDamagesSignals);
                    GameProgressLogger.Log(() => $"D: Damages Strength {MyProgress.DamagesStrengthUniform} => signal {MyProgress.DDamagesSignalDiscrete} ({MyProgress.DDamagesSignalUniform})");
                    break;
                case (byte)MyGameDecisions.PFile:
                    MyProgress.PFiles = action == 1;
                    if (!MyProgress.PFiles)
                        MyProgress.GameComplete = true;
                    break;
                case (byte)MyGameDecisions.DAnswer:
                    MyProgress.DAnswers = action == 1;
                    if (!MyProgress.DAnswers)
                        MyProgress.GameComplete = true;
                    break;
                case (byte)MyGameDecisions.PreBargainingRound:
                    if (MyDefinition.Options.SkipFileAndAnswerDecisions)
                    {
                        MyProgress.PFiles = MyProgress.DAnswers = true;
                    }
                    break;
                case (byte)MyGameDecisions.PAgreeToBargain:
                    MyProgress.AddPAgreesToBargain(action == 1);
                    break;
                case (byte)MyGameDecisions.DAgreeToBargain:
                    MyProgress.AddDAgreesToBargain(action == 1);
                    break;
                case (byte)MyGameDecisions.POffer:
                    double offer = GetOfferBasedOnAction(action, true, MyDefinition.Options.IncludeEndpointsForOffers);
                    MyProgress.AddOffer(true, offer);
                    MyProgress.AddOfferMixedness(true, MyProgress.Mixedness);
                break;
                case (byte)MyGameDecisions.DOffer:
                    offer = GetOfferBasedOnAction(action, false, MyDefinition.Options.IncludeEndpointsForOffers);
                    MyProgress.AddOffer(false, offer);
                    MyProgress.AddOfferMixedness(false, MyProgress.Mixedness);
                    if (MyDefinition.Options.BargainingRoundsSimultaneous || MyDefinition.Options.PGoesFirstIfNotSimultaneous[MyProgress.BargainingRoundsComplete])
                    {
                        MyProgress.ConcludeMainPortionOfBargainingRound(MyDefinition);
                    }
                    break;
                case (byte)MyGameDecisions.PResponse:
                    MyProgress.AddResponse(true, action == 1); // 1 == accept, 2 == reject
                    MyProgress.ConcludeMainPortionOfBargainingRound(MyDefinition);
                    break;
                case (byte)MyGameDecisions.DResponse:
                    MyProgress.AddResponse(false, action == 1); // 1 == accept, 2 == reject
                    MyProgress.ConcludeMainPortionOfBargainingRound(MyDefinition);
                    break;
                case (byte)MyGameDecisions.PChips:
                    MyProgress.RunningSideBetsActions.TemporaryStoragePMixedness = MyProgress.Mixedness;
                    // don't do anything else yet -- the decision specifies that the action should be stored in the cache
                    break;
                case (byte)MyGameDecisions.DChips:
                    MyDefinition.Options.MyGameRunningSideBets.SaveRunningSideBets(MyDefinition, MyProgress, action);
                    break;
                case (byte)MyGameDecisions.PAbandon:
                    MyProgress.PReadyToAbandon = action == 1;
                    break;
                case (byte)MyGameDecisions.DDefault:
                    MyProgress.DReadyToAbandon = action == 1;
                    if (MyProgress.PReadyToAbandon ^ MyProgress.DReadyToAbandon)
                    {
                        // exactly one party gives up
                        MyProgress.PAbandons = MyProgress.PReadyToAbandon;
                        MyProgress.DDefaults = MyProgress.DReadyToAbandon;
                        MyProgress.TrialOccurs = false;
                        MyProgress.GameComplete = true;
                        MyProgress.BargainingRoundsComplete++;
                    }
                    break;
                case (byte)MyGameDecisions.MutualGiveUp:
                    // both trying to give up simultaneously! revise with a coin flip
                    MyProgress.BothReadyToGiveUp = true;
                    MyProgress.PAbandons = action == 1;
                    MyProgress.DDefaults = !MyProgress.PAbandons;
                    MyProgress.BargainingRoundsComplete++;
                    MyProgress.GameComplete = true;
                    break;
                case (byte)MyGameDecisions.PostBargainingRound:
                    MyProgress.BargainingRoundsComplete++;
                    break;
                case (byte)MyGameDecisions.PPretrialAction:
                    MyDefinition.Options.MyGamePretrialDecisionGeneratorGenerator.ProcessAction(MyDefinition, MyProgress, true, action);
                    break;
                case (byte)MyGameDecisions.DPretrialAction:
                    MyDefinition.Options.MyGamePretrialDecisionGeneratorGenerator.ProcessAction(MyDefinition, MyProgress, false, action);
                    break;
                case (byte)MyGameDecisions.CourtDecisionLiability:
                    MyProgress.TrialOccurs = true;
                    MyProgress.PWinsAtTrial =
                        action == 2;
                    if (MyProgress.PWinsAtTrial == false)
                    {
                        MyProgress.DamagesAwarded = 0;
                        MyProgress.GameComplete = true;
                    }
                    else
                    {
                        bool courtWouldDecideDamages = MyDefinition.Options.NumDamagesStrengthPoints > 1;
                        if (!courtWouldDecideDamages)
                        {
                            MyProgress.DamagesAwarded = (double) MyProgress.DamagesMax;
                            MyProgress.GameComplete = true;
                        }
                    }
                    //System.Diagnostics.TabbedText.WriteLine($"Quality {MyProgress.LiabilityStrengthUniform} Court noise action {action} => {courtNoiseNormalDraw} => signal {courtLiabilitySignal} PWins {MyProgress.PWinsAtTrial}");
                    break;
                case (byte)MyGameDecisions.CourtDecisionDamages:
                    MyProgress.DamagesAwarded = (double) (MyProgress.DamagesMin + (MyProgress.DamagesMax - MyProgress.DamagesMin) * ConvertActionToUniformDistributionDraw(action, true));
                    MyProgress.GameComplete = true;
                    break;
                default:
                    throw new NotImplementedException();
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
            if (MyProgress.BargainingRoundsComplete == 0 || !MyDefinition.Options.DeltaOffersOptions.SubsequentOffersAreDeltas)
                offer = ConvertActionToUniformDistributionDraw(action, includeEndpoints);
            else
            {
                double? previousOffer = plaintiffOffer ? MyProgress.PLastOffer : MyProgress.DLastOffer;
                if (previousOffer == null)
                    offer = ConvertActionToUniformDistributionDraw(action, includeEndpoints);
                else
                    offer = MyDefinition.Options.DeltaOffersCalculation.GetOfferValue((double) previousOffer, action);
            }
            return offer;
        }

        public class MyGameOutcome
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

        public static MyGameOutcome CalculateGameOutcome(MyGameDefinition gameDefinition, MyGameDisputeGeneratorActions disputeGeneratorActions, MyGamePretrialActions pretrialActions, MyGameRunningSideBetsActions runningSideBetActions, double pInitialWealth, double dInitialWealth, bool pFiles, bool pAbandons, bool dAnswers, bool dDefaults, double? settlementValue, bool pWinsAtTrial, double? damagesAwarded, byte bargainingRoundsComplete, double? pFinalWealthWithBestOffer, double? dFinalWealthWithBestOffer, List<double> pOffers, List<bool> pResponses, List<double> dOffers, List<bool> dResponses)
        {
            MyGameOutcome outcome = new MyGameOutcome();

            double[] changeWealthOutsideLitigation = gameDefinition.Options.MyGameDisputeGenerator.GetLitigationIndependentWealthEffects(gameDefinition, disputeGeneratorActions);
            double pWealthAfterPrimaryConduct = pInitialWealth + changeWealthOutsideLitigation[0];
            double dWealthAfterPrimaryConduct = dInitialWealth + changeWealthOutsideLitigation[1];

            if (!pFiles || pAbandons)
            {
                outcome.PChangeWealth = outcome.DChangeWealth = 0;
                outcome.TrialOccurs = false;
            }
            else if (!dAnswers || dDefaults)
            { // defendant pays full damages (but no trial costs)
                outcome.PChangeWealth += gameDefinition.Options.DamagesMax;
                outcome.DChangeWealth -= gameDefinition.Options.DamagesMax;
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

            if (gameDefinition.Options.MyGamePretrialDecisionGeneratorGenerator != null)
            {
                gameDefinition.Options.MyGamePretrialDecisionGeneratorGenerator.GetEffectOnPlayerWelfare(gameDefinition, outcome.TrialOccurs, pWinsAtTrial, gameDefinition.Options.DamagesMax, pretrialActions, out double effectOnP, out double effectOnD);
                outcome.PChangeWealth += effectOnP;
                outcome.DChangeWealth += effectOnD;
            }

            double trialCostsMultiplier = 1.0;
            if (gameDefinition.Options.MyGameRunningSideBets != null)
            {
                byte? roundOfAbandonment = (pAbandons || dDefaults) ? (byte?)bargainingRoundsComplete : null;
                gameDefinition.Options.MyGameRunningSideBets.GetEffectOnPlayerWelfare(gameDefinition, roundOfAbandonment, pAbandons, dDefaults, outcome.TrialOccurs, pWinsAtTrial, runningSideBetActions, out double effectOnP, out double effectOnD, out byte totalChipsThatCount);
                outcome.PChangeWealth += effectOnP;
                outcome.DChangeWealth += effectOnD;
                outcome.NumChips = totalChipsThatCount;
                if (outcome.TrialOccurs)
                    trialCostsMultiplier = gameDefinition.Options.MyGameRunningSideBets.GetTrialCostsMultiplier(gameDefinition, totalChipsThatCount);
            }

            if (gameDefinition.Options.ShootoutSettlements)
            {
                if (gameDefinition.Options.IncludeAgreementToBargainDecisions)
                    throw new NotSupportedException(); // shootout settlements require bargaining
                bool abandoned = (pAbandons || dDefaults);
                bool abandonedAfterLastRound = abandoned && bargainingRoundsComplete == gameDefinition.Options.NumPotentialBargainingRounds;
                bool applyShootout = outcome.TrialOccurs || (abandonedAfterLastRound && gameDefinition.Options.ShootoutsApplyAfterAbandonment) || (abandoned && gameDefinition.Options.ShootoutsApplyAfterAbandonment && gameDefinition.Options.ShootoutsAverageAllRounds);
                if (applyShootout)
                {
                    double shootoutPrice;
                    if (gameDefinition.Options.BargainingRoundsSimultaneous)
                    {
                        if (gameDefinition.Options.ShootoutsAverageAllRounds)
                        {
                            double averagePOffer = pOffers.Average();
                            double averageDOffer = dOffers.Average();
                            shootoutPrice = (averagePOffer + averageDOffer) / 2.0;
                        }
                        else
                        {
                            double lastPOffer = pOffers.Last();
                            double lastDOffer = dOffers.Last();
                            shootoutPrice = (lastPOffer + lastDOffer) / 2.0;
                        }
                    }
                    else
                    {
                        List<double> applicableOffers = new List<double>();
                        if (gameDefinition.Options.ShootoutsAverageAllRounds)
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
                        shootoutPrice = applicableOffers.Average();
                    }
                    double shootoutStrength = gameDefinition.Options.ShootoutStrength;
                    double costToP = shootoutPrice * gameDefinition.Options.DamagesMax * shootoutStrength;
                    double extraDamages = (dDefaults ? gameDefinition.Options.DamagesMax : (damagesAwarded ?? 0)) * shootoutStrength;
                    double netBenefitToP = extraDamages - costToP;
                    outcome.PChangeWealth += netBenefitToP;
                    outcome.DChangeWealth -= netBenefitToP;
                }
            }

            double pFilingCostIncurred = pFiles ? gameDefinition.Options.PFilingCost * gameDefinition.Options.CostsMultiplier : 0;
            double dAnswerCostIncurred = dAnswers ? gameDefinition.Options.DAnswerCost * gameDefinition.Options.CostsMultiplier : 0;
            double pTrialCostsIncurred = outcome.TrialOccurs ? gameDefinition.Options.PTrialCosts * gameDefinition.Options.CostsMultiplier * trialCostsMultiplier : 0;
            double dTrialCostsIncurred = outcome.TrialOccurs ? gameDefinition.Options.DTrialCosts * gameDefinition.Options.CostsMultiplier * trialCostsMultiplier : 0;
            double perPartyBargainingCostsIncurred = gameDefinition.Options.PerPartyCostsLeadingUpToBargainingRound * gameDefinition.Options.CostsMultiplier * bargainingRoundsComplete;
            double pCostsInitiallyIncurred = pFilingCostIncurred + perPartyBargainingCostsIncurred + pTrialCostsIncurred;
            double dCostsInitiallyIncurred = dAnswerCostIncurred + perPartyBargainingCostsIncurred + dTrialCostsIncurred;
            double pEffectOfExpenses = 0, dEffectOfExpenses = 0;
            bool loserPaysApplies = gameDefinition.Options.LoserPays && (outcome.TrialOccurs || (gameDefinition.Options.LoserPaysAfterAbandonment && (pAbandons || dDefaults)));
            if (loserPaysApplies)
            { // British Rule and it applies (contested litigation and no settlement)
                double loserPaysMultiple = gameDefinition.Options.LoserPaysMultiple;
                bool pLoses = (outcome.TrialOccurs && !pWinsAtTrial) || pAbandons;
                if (pLoses)
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
                pEffectOfExpenses -= pFilingCostIncurred + perPartyBargainingCostsIncurred + pTrialCostsIncurred;
                dEffectOfExpenses -= dAnswerCostIncurred + perPartyBargainingCostsIncurred + dTrialCostsIncurred;
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
            MyProgress.CalculateGameOutcome();

            CalculateSocialWelfareOutcomes();

            base.FinalProcessing();
        }

        private void CalculateSocialWelfareOutcomes()
        {
            MyProgress.TotalExpensesIncurred = 0 - MyProgress.PChangeWealth - MyProgress.DChangeWealth;
            MyProgress.PreDisputeSWelfare = MyDefinition.Options.MyGameDisputeGenerator.GetLitigationIndependentSocialWelfare(MyDefinition, MyProgress.DisputeGeneratorActions);
            if (!MyProgress.DisputeArises)
            {
                MyProgress.FalseNegativeShortfall = 0;
                MyProgress.FalsePositiveExpenditures = 0;
                return;
            }
            double correctDamagesIfTrulyLiable;
            if (MyDefinition.Options.NumDamagesStrengthPoints <= 1)
                correctDamagesIfTrulyLiable = (double) MyProgress.DamagesMax;
            else
                correctDamagesIfTrulyLiable = (double) (MyProgress.DamagesMin + MyProgress.DamagesStrengthUniform * (MyProgress.DamagesMax - MyProgress.DamagesMin));
            double falseNegativeShortfallIfTrulyLiable = Math.Max(0, correctDamagesIfTrulyLiable - MyProgress.PChangeWealth); // how much plaintiff's payment fell short (if at all)
            double falsePositiveExpendituresIfNotTrulyLiable = Math.Max(0, 0 - MyProgress.DChangeWealth); // how much defendant's payment was excessive (if at all), in the condition in which the defendant is NOT truly liable. In this case, the defendant ideally would pay 0.
            double falsePositiveExpendituresIfTrulyLiable = Math.Max(0, 0 - correctDamagesIfTrulyLiable - MyProgress.DChangeWealth); // how much defendant's payment was excessive (if at all), in the condition in which the defendant is truly liable. In this case, the defendant ideally would pay the correct amount of damages. E.g., if correct damages are 100 and defendant pays out 150 (including costs), then change in wealth is -150, we have -100 - -150, so we have 50.
            if (!MyDefinition.Options.MyGameDisputeGenerator.PotentialDisputeArises(MyDefinition, MyProgress.DisputeGeneratorActions))
                MyProgress.IsTrulyLiable = false;
            else
                MyProgress.IsTrulyLiable = MyDefinition.Options.MyGameDisputeGenerator.IsTrulyLiable(MyDefinition, MyProgress.DisputeGeneratorActions, MyProgress);
            if (MyProgress.IsTrulyLiable)
            {
                MyProgress.FalseNegativeShortfall = falseNegativeShortfallIfTrulyLiable;
                MyProgress.FalsePositiveExpenditures = falsePositiveExpendituresIfTrulyLiable;
            }
            else
            {
                MyProgress.FalseNegativeShortfall = 0;
                MyProgress.FalsePositiveExpenditures = falsePositiveExpendituresIfNotTrulyLiable;
            }
        }
    }
}
