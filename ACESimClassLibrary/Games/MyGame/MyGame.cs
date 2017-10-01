using ACESim.Util;
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
                case (byte)MyGameDecisions.LitigationQuality:
                    MyProgress.DamagesAlleged = MyDefinition.Options.DamagesToAllege;
                    MyProgress.LitigationQualityDiscrete = action;
                    MyProgress.LitigationQualityUniform = ConvertActionToUniformDistributionDraw(action, false);
                    // If one or both parties have perfect information, then they can get their information about litigation quality now, since they don't need a signal. Note that we also specify in the game definition that the litigation quality should become part of their information set.
                    if (MyDefinition.Options.PNoiseStdev == 0)
                        MyProgress.PSignalUniform = (double) MyProgress.LitigationQualityUniform;
                    if (MyDefinition.Options.DNoiseStdev == 0)
                        MyProgress.DSignalUniform = (double) MyProgress.LitigationQualityUniform;

                break;
                case (byte)MyGameDecisions.PNoise:
                    MyProgress.PNoiseDiscrete = action;
                    MyDefinition.ConvertNoiseToSignal(MyProgress.LitigationQualityDiscrete, action, true,
                        out MyProgress.PSignalDiscrete, out MyProgress.PSignalUniform);
                    GameProgressLogger.Log(() => $"P: Quality {MyProgress.LitigationQualityUniform} Noise action {action} => signal {MyProgress.PSignalDiscrete} ({MyProgress.PSignalUniform})");
                break;
                case (byte)MyGameDecisions.DNoise:
                    MyProgress.DNoiseDiscrete = action;
                    MyDefinition.ConvertNoiseToSignal(MyProgress.LitigationQualityDiscrete, action, false,
                        out MyProgress.DSignalDiscrete, out MyProgress.DSignalUniform);
                    //System.Diagnostics.Console.WriteLine($"D: Quality {MyProgress.LitigationQualityUniform} Noise action {action} => signal {MyProgress.DSignalDiscrete} ({MyProgress.DSignalUniform})");
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
                    break;
                case (byte)MyGameDecisions.PAgreeToBargain:
                    MyProgress.AddPAgreesToBargain(action == 1);
                    break;
                case (byte)MyGameDecisions.DAgreeToBargain:
                    MyProgress.AddDAgreesToBargain(action == 1);
                    break;
                case (byte)MyGameDecisions.POffer:
                    double offer = GetOfferBasedOnAction(action, true);
                    MyProgress.AddOffer(true, offer);
                break;
                case (byte)MyGameDecisions.DOffer:
                    offer = GetOfferBasedOnAction(action, false);
                    MyProgress.AddOffer(false, offer);
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
                    // don't do anything yet -- the decision specifies that the action should be stored in the cache
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
                case (byte)MyGameDecisions.CourtDecision:
                    MyProgress.TrialOccurs = true;
                    double courtNoiseUniformDistribution =
                        EquallySpaced.GetLocationOfEquallySpacedPoint(action - 1 /* make it zero-based */,
                            MyDefinition.Options.NumCourtNoiseValues, false);
                    double courtNoiseNormalDraw = InvNormal.Calculate(courtNoiseUniformDistribution) *
                                                    MyDefinition.Options.CourtNoiseStdev;
                    double courtSignal = (double) MyProgress.LitigationQualityUniform + courtNoiseNormalDraw;
                    MyProgress.PWinsAtTrial =
                        courtSignal >
                        0.5; // we'll assume that P has burden of proof in case courtSignal is exactly equal to 0.5.
                    //System.Diagnostics.Console.WriteLine($"Quality {MyProgress.LitigationQualityUniform} Court noise action {action} => {courtNoiseNormalDraw} => signal {courtSignal} PWins {MyProgress.PWinsAtTrial}");
                    break;
                default:
                    throw new NotImplementedException();
            }
        }

        public static void ConvertNoiseActionToDiscreteAndUniformSignal(byte action, double trueValue, byte numNoiseValues, double noiseStdev, byte numSignals, out byte discreteSignal, out double uniformSignal)
        {
            // This is an equal probabilities decision. 
            discreteSignal = DiscreteValueSignal.ConvertNoiseToSignal(trueValue, action, numNoiseValues, noiseStdev, numSignals);
            if (discreteSignal == 1)
                uniformSignal = -1.0; // just a sign indicating that the signal is negative
            else if (discreteSignal == numSignals)
                uniformSignal = 2.0; // again, just a sign that it's out of range
            else
                uniformSignal = EquallySpaced.GetLocationOfEquallySpacedPoint(
                    discreteSignal -
                    2 /* make it zero-based, but also account for the fact that we have a signal for values less than 0 */,
                    numSignals - 2,
                    false /* signals, unlike offers, do not use endpoints */);
        }

        private double GetOfferBasedOnAction(byte action, bool plaintiffOffer)
        {
            double offer;
            if (MyProgress.BargainingRoundsComplete == 0 || !MyDefinition.Options.DeltaOffersOptions.SubsequentOffersAreDeltas)
                offer = ConvertActionToUniformDistributionDraw(action, true);
            else
            {
                double? previousOffer = plaintiffOffer ? MyProgress.PLastOffer : MyProgress.DLastOffer;
                if (previousOffer == null)
                    offer = ConvertActionToUniformDistributionDraw(action, true);
                else
                    offer = MyDefinition.Options.DeltaOffersCalculation.GetOfferValue((double) previousOffer, action);
            }
            return offer;
        }

        private byte GetDiscreteSignal(int action, double noiseStdev, DiscreteValueSignalParameters dvsp)
        {
            var noise = ConvertActionToNormalDistributionDraw(action, noiseStdev);
            var valuePlusNoise = MyProgress.LitigationQualityUniform + noise;
            byte discreteSignal = (byte)DiscreteValueSignal.GetDiscreteSignal((double) valuePlusNoise, dvsp); // note that this is a 1-based signal
            return discreteSignal;
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
            public byte NumChips;
        }

        public static MyGameOutcome CalculateGameOutcome(MyGameDefinition gameDefinition, MyGameDisputeGeneratorActions disputeGeneratorActions, MyGamePretrialActions pretrialActions, MyGameRunningSideBetsActions runningSideBetActions, double pInitialWealth, double dInitialWealth, double damagesAlleged, bool pFiles, bool pAbandons, bool dAnswers, bool dDefaults, double? settlementValue, bool pWinsAtTrial, byte bargainingRoundsComplete, double? pFinalWealthWithBestOffer, double? dFinalWealthWithBestOffer)
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
                outcome.PChangeWealth += gameDefinition.Options.DamagesToAllege;
                outcome.DChangeWealth -= gameDefinition.Options.DamagesToAllege;
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
                outcome.PChangeWealth = (pWinsAtTrial ? damagesAlleged : 0);
                outcome.DChangeWealth = (pWinsAtTrial ? -damagesAlleged : 0);
            }
            double pFilingCostIncurred = pFiles ? gameDefinition.Options.PFilingCost * gameDefinition.Options.CostsMultiplier : 0;
            double dAnswerCostIncurred = dAnswers ? gameDefinition.Options.DAnswerCost * gameDefinition.Options.CostsMultiplier : 0;
            double pTrialCostsIncurred = outcome.TrialOccurs ? gameDefinition.Options.PTrialCosts * gameDefinition.Options.CostsMultiplier : 0;
            double dTrialCostsIncurred = outcome.TrialOccurs ? gameDefinition.Options.DTrialCosts * gameDefinition.Options.CostsMultiplier : 0;
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

            if (gameDefinition.Options.MyGamePretrialDecisionGeneratorGenerator != null)
            {
                gameDefinition.Options.MyGamePretrialDecisionGeneratorGenerator.GetEffectOnPlayerWelfare(gameDefinition, outcome.TrialOccurs, pWinsAtTrial, damagesAlleged, pretrialActions, out double effectOnP, out double effectOnD);
                outcome.PChangeWealth += effectOnP;
                outcome.DChangeWealth += effectOnD;
            }

            if (gameDefinition.Options.MyGameRunningSideBets != null)
            {
                byte? roundOfAbandonment = (pAbandons || dDefaults) ? (byte?) bargainingRoundsComplete : null;
                gameDefinition.Options.MyGameRunningSideBets.GetEffectOnPlayerWelfare(gameDefinition, roundOfAbandonment, pAbandons, dDefaults, outcome.TrialOccurs, pWinsAtTrial, runningSideBetActions, out double effectOnP, out double effectOnD, out byte totalChipsThatCount);
                outcome.PChangeWealth += effectOnP;
                outcome.DChangeWealth += effectOnD;
                outcome.NumChips = totalChipsThatCount;
            }

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
            var outcome = CalculateGameOutcome(MyDefinition, MyProgress.DisputeGeneratorActions, MyProgress.PretrialActions, MyProgress.RunningSideBetsActions, MyProgress.PInitialWealth, MyProgress.DInitialWealth, MyProgress.DamagesAlleged ?? 0, MyProgress.PFiles, MyProgress.PAbandons, MyProgress.DAnswers, MyProgress.DDefaults, MyProgress.SettlementValue, MyProgress.PWinsAtTrial, MyProgress.BargainingRoundsComplete, MyProgress.PFinalWealthWithBestOffer, MyProgress.DFinalWealthWithBestOffer);
            MyProgress.DisputeArises = MyDefinition.Options.MyGameDisputeGenerator.PotentialDisputeArises(MyDefinition, MyProgress.DisputeGeneratorActions);
            MyProgress.PChangeWealth = outcome.PChangeWealth;
            MyProgress.DChangeWealth = outcome.DChangeWealth;
            MyProgress.PFinalWealth = outcome.PFinalWealth;
            MyProgress.DFinalWealth = outcome.DFinalWealth;
            MyProgress.PWelfare = outcome.PWelfare;
            MyProgress.DWelfare = outcome.DWelfare;
            MyProgress.TrialOccurs = outcome.TrialOccurs;
            MyProgress.NumChips = outcome.NumChips;

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
            double falseNegativeShortfallIfTrulyLiable = Math.Max(0, (double)MyProgress.DamagesAlleged - MyProgress.PChangeWealth); // how much plaintiff's payment fell short (if at all)
            double falsePositiveExpendituresIfNotTrulyLiable = Math.Max(0, 0 - MyProgress.DChangeWealth); // how much defendant's payment was excessive (if at all)
            if (!MyDefinition.Options.MyGameDisputeGenerator.PotentialDisputeArises(MyDefinition, MyProgress.DisputeGeneratorActions))
                MyProgress.IsTrulyLiable = false;
            else
                MyProgress.IsTrulyLiable = MyDefinition.Options.MyGameDisputeGenerator.IsTrulyLiable(MyDefinition, MyProgress.DisputeGeneratorActions, MyProgress);
            if (MyProgress.IsTrulyLiable)
            {
                MyProgress.FalseNegativeShortfall = falseNegativeShortfallIfTrulyLiable;
                MyProgress.FalsePositiveExpenditures = 0;
            }
            else
            {
                MyProgress.FalseNegativeShortfall = 0;
                MyProgress.FalsePositiveExpenditures = falsePositiveExpendituresIfNotTrulyLiable;
            }
        }
    }
}
