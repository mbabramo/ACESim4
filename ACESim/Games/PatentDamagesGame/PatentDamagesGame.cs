using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Diagnostics;

namespace ACESim
{
    [Serializable]
    public class PatentDamagesGame : Game, IDefaultBehaviorBeforeEvolution
    {

        PatentDamagesGameInputs PDInputs => (PatentDamagesGameInputs)GameInputs;
        PatentDamagesGameProgress PDProg => (PatentDamagesGameProgress)Progress;
        InventorInfo InventorToOptimizeInfo => PDInputs.AllInventorsInfo.InventorToOptimize;
        InventorInfo InventorInfo(int i) => PDInputs.AllInventorsInfo.Inventor(i);
        int NumPotentialInventors => PDInputs.AllInventorsInfo.NumPotentialInventors;

        /// <summary>
        /// <para>
        /// This method implements gameplay for the PatentDamages game.
        /// </para>
        /// </summary>
        public override void PrepareForOrMakeCurrentDecision()
        {
            if (Progress.GameComplete)
                return;

            switch (CurrentDecisionIndex)
            {
                case (int)PatentDamagesDecision.Enter:
                    MakeEntryDecisions();
                    break;
                case (int)PatentDamagesDecision.SuccessProbabilityAfterEntry:
                    ForecastProbabilityOfSuccessAfterEntry();
                    break;
                case (int)PatentDamagesDecision.TryToInvent:
                    MakeTryToInventDecisions();
                    break;
                case (int)PatentDamagesDecision.Spend:
                    MakeSpendDecisions();
                    IdentifyPatentWinner();
                    break;
                case (int)PatentDamagesDecision.SuccessProbabilityAfterInvestment:
                    ForecastProbabilityOfSuccessAfterInvestment();
                    if (!PreparationPhase)
                    {
                        DetermineInadvertentInfringement();
                        DetermineInventorOffer();
                        DetermineUserOffer();
                        ResolveNegotiation();
                        DetermineDamages();
                        CalculateWelfareOutcomes();
                        DoScoring();
                        Progress.GameComplete = true;
                    }
                    break;
                default:
                    throw new Exception("Unknown decision.");
            }
        }

        public void CalculateInventorEstimates(IEnumerable<InventorInfo> inventorInfos, double actualInventionValue, double stdDevOfNoiseDistribution)
        {
            PDProg.InventorEstimatesInventionValue = new List<double>();
            foreach (var inventorInfo in inventorInfos)
            {
                double estimate = PatentDamagesGame.GetEstimateOfInventionValue(stdDevOfNoiseDistribution, inventorInfo.InventionValueNoise, actualInventionValue);
                PDProg.InventorEstimatesInventionValue.Add(estimate);
            }
        }

        public static double GetEstimateOfInventionValue(double stdDevOfNoiseDistribution, double drawFromNoiseDistribution, double actualInventionValue)
        {
            return ObfuscationGame.ObfuscationCorrectAnswer.Calculate(stdDevOfNoiseDistribution, drawFromNoiseDistribution + actualInventionValue);
        }

        private void MakeEntryDecisions()
        {
            if (PreparationPhase)
            {
                CalculateInventorEstimates(PDInputs.AllInventorsInfo.AllInventors(), PDInputs.InventionValue, PDInputs.InventionValueNoiseStdev);
                GetDecisionInputs();
                return;
            }
            int numEntering;
            if (CurrentlyEvolving && (CurrentlyEvolvingDecisionIndex == (int)PatentDamagesDecision.TryToInvent || CurrentlyEvolvingDecisionIndex == (int)PatentDamagesDecision.Spend))
                numEntering = 1; // we always want to evolve the decision to try and the decision to spend with a maximum of 1 inventor. Then, when we're evolving this decision (number of entrants), we make sure that there are enough entering to eliminate profits.
            else
            {
                double fractionalEntry = MakeDecision();
                numEntering = (int)Math.Floor(fractionalEntry);
                if (numEntering < 1)
                    numEntering = 1;
                if (PDInputs.FractionalEntryRandomSeed < (fractionalEntry - numEntering))
                    numEntering++;
            }
            if (!CurrentlyEvolving || CurrentlyEvolvingDecisionIndex != (int)PatentDamagesDecision.Enter)
            {
                if (numEntering > PDInputs.MaxNumEntrants)
                    numEntering = PDInputs.MaxNumEntrants;
            }
            PDProg.NumberEntrants = numEntering;
            PDProg.MainInventorEnters = numEntering > 0;
            PDProg.InventorEntryDecisions = new List<bool>();
            for (int i = 0; i < PDInputs.AllInventorsInfo.NumPotentialInventors; i++)
                PDProg.InventorEntryDecisions.Add(i + 1 <= numEntering);
            PDProg.SomeoneEnters = PDProg.InventorEntryDecisions.Any(x => x == true);
        }

        private void ForecastProbabilityOfSuccessAfterEntry()
        {
            if (PDProg.MainInventorEnters)
            {
                if (PreparationPhase)
                {
                    GetDecisionInputs();
                    return;
                }
                PDProg.ForecastAfterEntry = MakeDecision();
            }
        }

        private void MakeTryToInventDecisions()
        {
            if (PreparationPhase)
            {
                GetDecisionInputs();
                return;
            }
            PDProg.InventorTryToInventDecisions = new List<bool>();
            PDProg.MainInventorTries = PDProg.MainInventorEnters && MakeDecision() > 0;
            PDProg.InventorTryToInventDecisions.Add(PDProg.MainInventorTries);
            var strategy = Strategies[(int)PatentDamagesDecision.TryToInvent];
            int inventor = 1;
            foreach (var inventorInfo in PDInputs.AllInventorsInfo.InventorsNotBeingOptimized())
            {
                if (!PDProg.InventorEntryDecisions[inventor])
                    PDProg.InventorTryToInventDecisions.Add(false);
                else
                {
                    double otherInventorTryToInventDecision = strategy.Calculate(new List<double> { PDInputs.CostOfMinimumInvestmentBaseline, inventorInfo.CostOfMinimumInvestmentMultiplier, PDProg.InventorEstimatesInventionValue[inventor] }, this);
                    PDProg.InventorTryToInventDecisions.Add(otherInventorTryToInventDecision > 0);
                }
                inventor++;
            }
            PDProg.SomeoneTries = PDProg.InventorTryToInventDecisions.Any(x => x == true);
            PDProg.NumberTrying = PDProg.InventorTryToInventDecisions.Count(x => x == true);
        }

        private void MakeSpendDecisions()
        {
            if (PreparationPhase)
            {
                if (!PDProg.MainInventorTries)
                    return;
                GetDecisionInputs();
                return;
            }
            PDProg.InventorSpendDecisions = new List<double>();
            double mainInventorSpend = PDProg.MainInventorTries ? MakeDecision() : 0;
            mainInventorSpend *= PDInputs.CostOfMinimumInvestmentBaseline * InventorToOptimizeInfo.CostOfMinimumInvestmentMultiplier;
            PDProg.InventorSpendDecisions.Add(mainInventorSpend);
            var strategy = Strategies[(int)PatentDamagesDecision.Spend];
            int inventor = 1;
            foreach (var inventorInfo in PDInputs.AllInventorsInfo.InventorsNotBeingOptimized())
            {
                if (!PDProg.InventorTryToInventDecisions[inventor])
                    PDProg.InventorSpendDecisions.Add(0);
                else
                {
                    double otherInventorSpendDecision = strategy.Calculate(new List<double> { PDInputs.CostOfMinimumInvestmentBaseline, inventorInfo.CostOfMinimumInvestmentMultiplier, PDProg.InventorEstimatesInventionValue[inventor] }, this);
                    otherInventorSpendDecision *= PDInputs.CostOfMinimumInvestmentBaseline * inventorInfo.CostOfMinimumInvestmentMultiplier;
                    PDProg.InventorSpendDecisions.Add(otherInventorSpendDecision);
                }
                inventor++;
            }
            PDProg.AverageSpendingOfEntrants = PDProg.InventorSpendDecisions.Sum() / (double) PDProg.NumberTrying;
        }

        private void ForecastProbabilityOfSuccessAfterInvestment()
        {
            if (PDProg.MainInventorTries)
            {
                if (PreparationPhase)
                {
                    GetDecisionInputs();
                    return;
                }
                PDProg.ForecastAfterInvestment = MakeDecision();
            }
        }

        private void IdentifyPatentWinner()
        {
            if (PreparationPhase)
                return;
            PDProg.InventorSucceedsAtInvention = new List<bool>();
            double curvature = MonotonicCurve.CalculateCurvatureForThreePoints(1.0, PDInputs.SuccessProbabilityMinimumInvestment, 2.0, PDInputs.SuccessProbabilityDoubleInvestment, 10.0, PDInputs.SuccessProbabilityTenTimesInvestment);
            for (int i = 0; i < NumPotentialInventors; i++)
            {
                bool tries = PDProg.InventorTryToInventDecisions[i];
                if (tries)
                {
                    var inventorInfo = InventorInfo(i);
                    double weightedSeed = PDInputs.CommonSuccessRandomSeed * (1.0 - PDInputs.SuccessIndependence) + inventorInfo.RandomSeed * PDInputs.SuccessIndependence;
                    double multipleOfMinimumInvestment = PDProg.InventorSpendDecisions[i] / (PDInputs.CostOfMinimumInvestmentBaseline * InventorInfo(i).CostOfMinimumInvestmentMultiplier); // the spend decision has been multiplied by cost of minimum investment, so we need to divide that back out to get the multiple of minimum investment
                    double thresholdNeeded = MonotonicCurve.CalculateValueBasedOnProportionOfWayBetweenValues(PDInputs.SuccessProbabilityMinimumInvestment, PDInputs.SuccessProbabilityTenTimesInvestment, curvature, (multipleOfMinimumInvestment - 1.0)/9.0); 
                    bool success = weightedSeed < thresholdNeeded;
                    PDProg.InventorSucceedsAtInvention.Add(success);
                }
                else
                    PDProg.InventorSucceedsAtInvention.Add(false);
            }
            int numSuccessful = PDProg.InventorSucceedsAtInvention.Count(x => x == true);
            if (numSuccessful > 0)
            {
                int winnerIndex = (int)(PDInputs.PickWinnerRandomSeed * (double)numSuccessful);
                int succeedersFound = 0;
                for (int i = 0; i < NumPotentialInventors; i++)
                {
                    if (PDProg.InventorSucceedsAtInvention[i])
                    {
                        if (winnerIndex == succeedersFound)
                        {
                            PDProg.WinnerOfPatent = i;
                            PDProg.InventionOccurs = true;
                            break;
                        }
                        else
                            succeedersFound++;
                    }
                }
            }
        }

        private void DetermineInadvertentInfringement()
        {
            if (PDProg.InventionOccurs)
            {
                PDProg.InadvertentInfringement = PDInputs.InadvertentInfringementRandomSeed < PDInputs.InadvertentInfringementProbability;
            }
        }

        private void DetermineInventorOffer()
        {
            if (PDProg.InventionOccurs)
            {
                PDProg.FirstInventorWinsPatent = PDProg.WinnerOfPatent == 0 ? 1.0 : 0;
                InventorInfo winnerInfo = InventorInfo((int)PDProg.WinnerOfPatent);
                PDProg.WinnerPredictionUserWelfareChangeIntentionalInfringement = PredictWelfareChangeFromIntentionalInfringement(InventionEstimateToUse.Inventor);
                // Inventor say: If I lose, then maybe user will intentionally infringe. In that case, I would receive damages - costs. So, then I should be willing to take any offer at least that great. Since Welfare Change = Value - Damages - Costs, it follows that Value - Welfare Change - 2 * costs = Value - (Value - Damages - Costs) - 2 * costs = Damages + Costs - 2 * Costs = Damages - Costs. So, Value - Welfare Change - 2 * costs is my best offer. 
                if (PDProg.WinnerPredictionUserWelfareChangeIntentionalInfringement > 0)
                    PDProg.InventorOffer = PDProg.InventorEstimatesInventionValue[(int)PDProg.WinnerOfPatent] - PDProg.WinnerPredictionUserWelfareChangeIntentionalInfringement - 2 * PDInputs.LitigationCostsEachParty;
                else
                    PDProg.InventorOffer = 0; // it doesn't look like user would intentionally infringe, so anything inventor can get is gravy.
            }
        }

        private void DetermineUserOffer()
        {
            if (PDProg.InventionOccurs && !PDProg.InadvertentInfringement)
            {
                // User says: What will my welfare be if negotiation fails? If I would intentionally infringe, it will be Welfare Change = Value - damages - costs. So, anticipating that, I should be willing to pay up to Damages + Costs to get the value without litigation, i.e. value - (value - damages - costs). If I would not intentionally infringe, then the most I would be willing to pay is the value itself.
                PDProg.UserPredictedWelfareChangeIntentionalInfringement = PredictWelfareChangeFromIntentionalInfringement(InventionEstimateToUse.ActualUserValue);
                if (PDProg.UserPredictedWelfareChangeIntentionalInfringement > 0)
                    PDProg.UserOffer = PDInputs.InventionValue - PDProg.UserPredictedWelfareChangeIntentionalInfringement; // Constraint is: Value - Payment > Welfare Absent Payment; So, Value - Welfare > Payment. 
                else
                    PDProg.UserOffer = PDInputs.InventionValue; // I won't intentionally infringe, since that would hurt me. So, most I'll pay is the value that I'll receive from use. 
            }
        }

        private void ResolveNegotiation()
        {
            if (PDProg.InventionOccurs)
            {
                if (PDProg.UserOffer > PDProg.InventorOffer)
                {
                    PDProg.AgreementReached = true;
                    PDProg.AgreedOnPrice = (PDProg.UserOffer + PDProg.InventorOffer) / 2.0;
                }
                else
                {
                    PDProg.AgreementReached = false;
                    if (PDProg.UserPredictedWelfareChangeIntentionalInfringement > 0)
                    {
                        PDProg.IntentionalInfringement = true;
                    }
                }
            }
            PDProg.InventionUsed = PDProg.InadvertentInfringement || PDProg.AgreementReached || PDProg.IntentionalInfringement;
        }

        private double PredictWelfareChangeFromIntentionalInfringement(InventionEstimateToUse inventionEstimateToUse)
        {
            double anticipatedDamages = CalculateHypotheticalDamages(inventionEstimateToUse, false);
            double welfareChange = PDInputs.InventionValue - anticipatedDamages - PDInputs.LitigationCostsEachParty;
            return welfareChange;
        }

        private void DetermineDamages()
        {
            if (PreparationPhase)
                return;

            if (PDProg.WinnerOfPatent != null)
            {
                PDProg.HypotheticalDamages = CalculateHypotheticalDamages(InventionEstimateToUse.Court, PDProg.InadvertentInfringement);
            }
            if (PDProg.InadvertentInfringement || PDProg.IntentionalInfringement)
                PDProg.DamagesPaid = (double) PDProg.HypotheticalDamages;
            else
                PDProg.DamagesPaid = 0;
            PDProg.AmountPaid = PDProg.AgreementReached ? PDProg.AgreedOnPrice ?? 0 : PDProg.DamagesPaid;
        }

        private enum InventionEstimateToUse
        {
            ActualUserValue,
            Court,
            Inventor
        }

        private double CalculateHypotheticalDamages(InventionEstimateToUse inventionValueToUse, bool inadvertentInfringement)
        {
            double forecastAfterEntry = PDProg.ForecastAfterEntry == 0 ? 1 : PDProg.ForecastAfterEntry;
            double forecastAfterInvestment = PDProg.ForecastAfterInvestment == 0 ? 1 : PDProg.ForecastAfterInvestment;
            double riskAdjustedEntrySpending = PDInputs.CostOfEntry / forecastAfterEntry;
            double riskAdjustedInventionSpending = PDProg.InventorSpendDecisions[(int)PDProg.WinnerOfPatent] / forecastAfterInvestment;
            double costBasedDamages = (riskAdjustedEntrySpending + riskAdjustedInventionSpending) * (1.0 + PDInputs.PermittedRateOfReturn);
            double inventionValue = 0;
            switch (inventionValueToUse)
            {
                case InventionEstimateToUse.ActualUserValue:
                    inventionValue = PDInputs.InventionValue;
                    break;
                case InventionEstimateToUse.Court:
                    inventionValue = GetEstimateOfInventionValue(PDInputs.InventionValueCourtNoiseStdev, PDInputs.InventionValueCourtNoise, PDInputs.InventionValue);
                    break;
                case InventionEstimateToUse.Inventor:
                    inventionValue = PDProg.InventorEstimatesInventionValue[(int)PDProg.WinnerOfPatent];
                    break;
            }
            double adjustedInventionValue = PDInputs.ProportionOfValueForStandardDamages * inventionValue; // 1.0 would be a disgorgement remedy; 0.5 would be trying to reflect what the parties would decide.
            double weight = inadvertentInfringement ? PDInputs.WeightOnCostPlusDamagesForInadvertentInfringement : PDInputs.WeightOnCostPlusDamagesForIntentionalInfringement;
            double weightedDamages = weight * costBasedDamages + (1.0 - weight) * adjustedInventionValue;
            double multiplier = inadvertentInfringement ? 1.0 : PDInputs.DamagesMultiplierForIntentionalInfringement;
            double fullDamages = weightedDamages * multiplier;
            return fullDamages;
        }

        public void CalculateWelfareOutcomes()
        {
            if (PreparationPhase)
                return;
            double allPrivateInvestments = 0; // includes money invested and money put to market
            double socialBenefit = 0, privateBenefit = 0;
            int inventor = 0;
            double combinedWealthOfPotentialInventors = 0;
            foreach (InventorInfo inventorInfo in PDInputs.AllInventorsInfo.AllInventors())
            {
                double wealth = PDInputs.InitialWealthOfEntrants;
                allPrivateInvestments += wealth;
                double spending = 0;
                if (PDProg.InventorEntryDecisions[inventor])
                    spending += PDInputs.CostOfEntry;
                if (PDProg.InventorTryToInventDecisions[inventor])
                    spending += PDProg.InventorSpendDecisions[inventor];
                wealth -= spending;
                double unspentMoney = wealth; // note that unspent money from those who don't even enter is considered in social welfare
                double marketReturn = unspentMoney * PDInputs.MarketRateOfReturn;
                wealth += marketReturn;
                socialBenefit += marketReturn;
                privateBenefit += marketReturn;
                if (inventor == PDProg.WinnerOfPatent)
                {
                    double spillover = PDInputs.InventionValue * PDInputs.SpilloverMultiplier; // note that spillover occurs regardless of whether invention is used
                    socialBenefit += spillover; // no effect on private benefit
                    if (PDProg.InventionUsed)
                    {
                        socialBenefit += PDInputs.InventionValue; // if user doesn't use invention, there is no social benefit from the use itself
                        privateBenefit += PDInputs.InventionValue;
                        PDProg.UserUtility = PDInputs.InventionValue - PDProg.AmountPaid;
                        bool litigation = (PDProg.InadvertentInfringement || PDProg.IntentionalInfringement);
                        if (litigation)
                        {
                            wealth -= PDInputs.LitigationCostsEachParty;
                            socialBenefit -= 2 * PDInputs.LitigationCostsEachParty;
                            privateBenefit -= 2 * PDInputs.LitigationCostsEachParty;
                            PDProg.UserUtility -= PDInputs.LitigationCostsEachParty;
                        }
                        double patentRevenues = (double)PDProg.AmountPaid;
                        wealth += patentRevenues;
                    }
                    else
                    {
                        PDProg.UserUtility = 0;
                    }
                }
                if (inventor == 0)
                    PDProg.MainInventorUtility = wealth;
                if (inventor < PDProg.NumberEntrants)
                    combinedWealthOfPotentialInventors += wealth;
                inventor++;
            }
            PDProg.AverageInventorUtility = combinedWealthOfPotentialInventors / (double)PDProg.NumberEntrants;
            PDProg.SocialWelfare = socialBenefit;
            PDProg.PrivateWelfare = privateBenefit;
        }

        protected void DoScoring()
        {
            if (PreparationPhase || !CurrentlyEvolving)
                return;
            var squaredDifference = (PDProg.AverageInventorUtility - PDInputs.InitialWealthOfEntrants) * (PDProg.AverageInventorUtility - PDInputs.InitialWealthOfEntrants);
            base.Score((int)PatentDamagesDecision.Enter, squaredDifference); // we're minimizing profits
            if (PDProg.MainInventorEnters)
            {
                Score((int)PatentDamagesDecision.TryToInvent, PDProg.MainInventorUtility);
                bool isSuccess = PDProg.WinnerOfPatent == 0; // ALTERNATIVE -- under this approach not getting paid by some increases the amount others pay: && PDProg.AmountPaid > 0;
                double successNumber = isSuccess ? 1.0 : 0;
                double entrySuccessMeasure = (PDProg.ForecastAfterEntry - successNumber) * (PDProg.ForecastAfterEntry - successNumber); // we must square this, so that we're minimizing the square. That ensures an unbiased estimtae
                Score((int)PatentDamagesDecision.SuccessProbabilityAfterEntry, entrySuccessMeasure);
                if (PDProg.MainInventorTries)
                {
                    Score((int)PatentDamagesDecision.Spend, PDProg.MainInventorUtility);
                    double investmentSuccessMeasure = (PDProg.ForecastAfterInvestment - successNumber) * (PDProg.ForecastAfterInvestment - successNumber); // we must square this, so that we're minimizing the square. That ensures an unbiased estimtae
                    Score((int)PatentDamagesDecision.SuccessProbabilityAfterInvestment, PDProg.MainInventorUtility);
                }
            }
        }

        /// <summary>
        /// This method returns the strategy inputs for the current decision being calculated.
        /// </summary>
        protected override List<double> GetDecisionInputs()
        {

            double[] inputs = null;

            switch (CurrentDecisionIndex)
            {
                case (int)PatentDamagesDecision.Enter:
                    inputs = new double[] { };
                    break;
                case (int)PatentDamagesDecision.SuccessProbabilityAfterEntry:
                    inputs = new double[] { };
                    break;
                case (int)PatentDamagesDecision.TryToInvent:
                case (int)PatentDamagesDecision.SuccessProbabilityAfterInvestment:
                case (int)PatentDamagesDecision.Spend:
                    inputs = new double[] { PDInputs.CostOfMinimumInvestmentBaseline, InventorToOptimizeInfo.CostOfMinimumInvestmentMultiplier, PDProg.InventorEstimatesInventionValue.First() };
                    break;
                default:
                    throw new Exception("Unknown decision.");
            }
            
            RecordInputsIfNecessary(inputs.ToList());

            return inputs.ToList();
        }

        public double DefaultBehaviorBeforeEvolution(List<double> inputs, int decisionNumber)
        {
            switch (CurrentDecisionIndex)
            {
                case (int)PatentDamagesDecision.Enter:
                    return 1.0; // assume just one entrant
                case (int)PatentDamagesDecision.SuccessProbabilityAfterEntry:
                    return 0.5; 
                case (int)PatentDamagesDecision.TryToInvent:
                    return 1.0;
                case (int)PatentDamagesDecision.SuccessProbabilityAfterInvestment:
                    return 0.5;
                case (int)PatentDamagesDecision.Spend:
                    return 1.0;
                default:
                    throw new Exception("Unknown decision.");
            }
        }
    }
}
