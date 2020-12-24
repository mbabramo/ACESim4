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
        public LitigGameDefinition MyDefinition => (LitigGameDefinition)GameDefinition;
        public LitigGameProgress MyProgress => (LitigGameProgress)Progress;

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
            switch (currentDecisionByteCode)
            {
                case (byte)LitigGameDecisions.PrePrimaryActionChance:
                    MyProgress.DisputeGeneratorActions.PrePrimaryChanceAction = action;
                    break;
                case (byte)LitigGameDecisions.PrimaryAction:
                    MyProgress.DisputeGeneratorActions.PrimaryAction = action;
                    if (MyDefinition.CheckCompleteAfterPrimaryAction && MyDefinition.Options.LitigGameDisputeGenerator.MarkComplete(MyDefinition, MyProgress.DisputeGeneratorActions.PrePrimaryChanceAction, MyProgress.DisputeGeneratorActions.PrimaryAction))
                        MyProgress.GameComplete = true;
                    break;
                case (byte)LitigGameDecisions.PostPrimaryActionChance:
                    MyProgress.DisputeGeneratorActions.PostPrimaryChanceAction = action;
                    if (MyDefinition.CheckCompleteAfterPostPrimaryAction && MyDefinition.Options.LitigGameDisputeGenerator.MarkComplete(MyDefinition, MyProgress.DisputeGeneratorActions.PrePrimaryChanceAction, MyProgress.DisputeGeneratorActions.PrimaryAction, MyProgress.DisputeGeneratorActions.PostPrimaryChanceAction))
                        MyProgress.GameComplete = true;
                    break;
                case (byte)LitigGameDecisions.LiabilityStrength:
                    MyProgress.LiabilityStrengthDiscrete = action;
                break;
                case (byte)LitigGameDecisions.PLiabilitySignal:
                    MyProgress.PLiabilitySignalDiscrete = action;
                    GameProgressLogger.Log(() => $"P: Liability Strength {MyProgress.LiabilityStrengthDiscrete} => signal {MyProgress.PLiabilitySignalDiscrete}");
                break;
                case (byte)LitigGameDecisions.DLiabilitySignal:
                    MyProgress.DLiabilitySignalDiscrete = action;
                    GameProgressLogger.Log(() => $"D: Liability Strength {MyProgress.LiabilityStrengthDiscrete} => signal {MyProgress.DLiabilitySignalDiscrete}");
                    break;
                case (byte)LitigGameDecisions.DamagesStrength:
                    MyProgress.DamagesStrengthDiscrete = action;
                    break;
                case (byte)LitigGameDecisions.PDamagesSignal:
                    MyProgress.PDamagesSignalDiscrete = action;
                    GameProgressLogger.Log(() => $"P: Damages Strength {MyProgress.DamagesStrengthDiscrete} => signal {MyProgress.PDamagesSignalDiscrete}");
                    break;
                case (byte)LitigGameDecisions.DDamagesSignal:
                    MyProgress.DDamagesSignalDiscrete = action;
                    GameProgressLogger.Log(() => $"D: Damages Strength {MyProgress.DamagesStrengthDiscrete} => signal {MyProgress.DDamagesSignalDiscrete}");
                    break;
                case (byte)LitigGameDecisions.PFile:
                    MyProgress.PFiles = action == 1;
                    if (!MyProgress.PFiles)
                        MyProgress.GameComplete = true;
                    break;
                case (byte)LitigGameDecisions.DAnswer:
                    MyProgress.DAnswers = action == 1;
                    if (!MyProgress.DAnswers)
                        MyProgress.GameComplete = true;
                    break;
                case (byte)LitigGameDecisions.PreBargainingRound:
                    if (MyDefinition.Options.SkipFileAndAnswerDecisions)
                    {
                        MyProgress.PFiles = MyProgress.DAnswers = true;
                    }
                    break;
                case (byte)LitigGameDecisions.PAgreeToBargain:
                    MyProgress.AddPAgreesToBargain(action == 1);
                    break;
                case (byte)LitigGameDecisions.DAgreeToBargain:
                    MyProgress.AddDAgreesToBargain(action == 1);
                    break;
                case (byte)LitigGameDecisions.POffer:
                    double offer = GetOfferBasedOnAction(action, true, MyDefinition.Options.IncludeEndpointsForOffers);
                    MyProgress.AddOffer(true, offer);
                    MyProgress.AddOfferMixedness(true, MyProgress.Mixedness);
                break;
                case (byte)LitigGameDecisions.DOffer:
                    offer = GetOfferBasedOnAction(action, false, MyDefinition.Options.IncludeEndpointsForOffers);
                    MyProgress.AddOffer(false, offer);
                    MyProgress.AddOfferMixedness(false, MyProgress.Mixedness);
                    if (MyDefinition.Options.BargainingRoundsSimultaneous || MyDefinition.Options.PGoesFirstIfNotSimultaneous[MyProgress.BargainingRoundsComplete])
                    {
                        MyProgress.ConcludeMainPortionOfBargainingRound(MyDefinition);
                    }
                    break;
                case (byte)LitigGameDecisions.PResponse:
                    MyProgress.AddResponse(true, action == 1); // 1 == accept, 2 == reject
                    MyProgress.ConcludeMainPortionOfBargainingRound(MyDefinition);
                    break;
                case (byte)LitigGameDecisions.DResponse:
                    MyProgress.AddResponse(false, action == 1); // 1 == accept, 2 == reject
                    MyProgress.ConcludeMainPortionOfBargainingRound(MyDefinition);
                    break;
                case (byte)LitigGameDecisions.PChips:
                    MyProgress.RunningSideBetsActions.TemporaryStoragePMixedness = MyProgress.Mixedness;
                    // don't do anything else yet -- the decision specifies that the action should be stored in the cache
                    break;
                case (byte)LitigGameDecisions.DChips:
                    MyDefinition.Options.LitigGameRunningSideBets.SaveRunningSideBets(MyDefinition, MyProgress, action);
                    break;
                case (byte)LitigGameDecisions.PAbandon:
                    MyProgress.PReadyToAbandon = action == 1;
                    break;
                case (byte)LitigGameDecisions.DDefault:
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
                case (byte)LitigGameDecisions.MutualGiveUp:
                    // both trying to give up simultaneously! revise with a coin flip
                    MyProgress.BothReadyToGiveUp = true;
                    MyProgress.PAbandons = action == 1;
                    MyProgress.DDefaults = !MyProgress.PAbandons;
                    MyProgress.BargainingRoundsComplete++;
                    MyProgress.GameComplete = true;
                    break;
                case (byte)LitigGameDecisions.PostBargainingRound:
                    MyProgress.BargainingRoundsComplete++;
                    break;
                case (byte)LitigGameDecisions.PPretrialAction:
                    MyDefinition.Options.LitigGamePretrialDecisionGeneratorGenerator.ProcessAction(MyDefinition, MyProgress, true, action);
                    break;
                case (byte)LitigGameDecisions.DPretrialAction:
                    MyDefinition.Options.LitigGamePretrialDecisionGeneratorGenerator.ProcessAction(MyDefinition, MyProgress, false, action);
                    break;
                case (byte)LitigGameDecisions.CourtDecisionLiability:
                    MyProgress.TrialOccurs = true;
                    MyProgress.PWinsAtTrial = MyDefinition.Options.NumLiabilitySignals == 1 /* IMPORTANT: This highlights that when there is only one liability signal, the court ALWAYS finds liability */ || 
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
                            MyProgress.DamagesAwarded = (double)MyDefinition.Options.DamagesMax;
                            MyProgress.GameComplete = true;
                        }
                    }
                    //System.Diagnostics.TabbedText.WriteLine($"Quality {MyProgress.LiabilityStrengthUniform} Court noise action {action} => {courtNoiseNormalDraw} => signal {courtLiabilitySignal} PWins {MyProgress.PWinsAtTrial}");
                    break;
                case (byte)LitigGameDecisions.CourtDecisionDamages:
                    double damagesProportion = ConvertActionToUniformDistributionDraw(action, true);
                    if (MyDefinition.Options.NumDamagesSignals == 1)
                        damagesProportion = 1.0;
                    MyProgress.DamagesAwarded = (double) (MyDefinition.Options.DamagesMin + (MyDefinition.Options.DamagesMax - MyDefinition.Options.DamagesMin) * damagesProportion);
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

        public static LitigGameOutcome CalculateGameOutcome(LitigGameDefinition gameDefinition, LitigGameDisputeGeneratorActions disputeGeneratorActions, LitigGamePretrialActions pretrialActions, LitigGameRunningSideBetsActions runningSideBetActions, double pInitialWealth, double dInitialWealth, bool pFiles, bool pAbandons, bool dAnswers, bool dDefaults, double? settlementValue, bool pWinsAtTrial, double? damagesAwarded, byte bargainingRoundsComplete, double? pFinalWealthWithBestOffer, double? dFinalWealthWithBestOffer, List<double> pOffers, List<bool> pResponses, List<double> dOffers, List<bool> dResponses)
        {
            LitigGameOutcome outcome = new LitigGameOutcome();

            double[] changeWealthOutsideLitigation = gameDefinition.Options.LitigGameDisputeGenerator.GetLitigationIndependentWealthEffects(gameDefinition, disputeGeneratorActions);
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

            if (gameDefinition.Options.LitigGamePretrialDecisionGeneratorGenerator != null)
            {
                gameDefinition.Options.LitigGamePretrialDecisionGeneratorGenerator.GetEffectOnPlayerWelfare(gameDefinition, outcome.TrialOccurs, pWinsAtTrial, gameDefinition.Options.DamagesMax, pretrialActions, out double effectOnP, out double effectOnD);
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

            double costsMultiplier = gameDefinition.Options.CostsMultiplier;
            double pFilingCostIncurred = pFiles ? gameDefinition.Options.PFilingCost * costsMultiplier : 0;
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
            MyProgress.CalculateGameOutcome();

            base.FinalProcessing();
        }
    }
}
