﻿using System;
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
                    break;
                case (int)PatentDamagesDecision.Price:
                    MakePricingDecision();
                    DetermineInadvertentInfringement();
                    break;
                case (int)PatentDamagesDecision.Accept:
                    DetermineAcceptance();
                    break;
                case (int)PatentDamagesDecision.Infringe:
                    DetermineIntentionalInfringement();
                    DetermineDamages();
                    CalculateWelfareOutcomes();
                    DoScoring();
                    if (!PreparationPhase)
                        Progress.GameComplete = true;
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
            if (PreparationPhase)
            {
                GetDecisionInputs();
                return;
            }
            if (PDProg.MainInventorEnters)
            {
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
            var strategy = CurrentlyEvolving ? Strategies[(int)PatentDamagesDecision.TryToInvent].PreviousVersionOfThisStrategy : Strategies[(int)PatentDamagesDecision.TryToInvent];
            int inventor = 1;
            foreach (var inventorInfo in PDInputs.AllInventorsInfo.InventorsNotBeingOptimized())
            {
                if (!PDProg.InventorEntryDecisions[inventor])
                    PDProg.InventorTryToInventDecisions.Add(false);
                else if (strategy == null)
                    PDProg.InventorTryToInventDecisions.Add(true);
                else
                {
                    double otherInventorTryToInventDecision = strategy.Calculate(new List<double> { inventorInfo.CostOfMinimumInvestment, PDProg.InventorEstimatesInventionValue[inventor] });
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
                GetDecisionInputs();
                return;
            }
            PDProg.InventorSpendDecisions = new List<double>();
            double mainInventorSpend = PDProg.MainInventorTries ? MakeDecision() : 0;
            mainInventorSpend *= InventorToOptimizeInfo.CostOfMinimumInvestment;
            PDProg.InventorSpendDecisions.Add(mainInventorSpend);
            var strategy = CurrentlyEvolving ? Strategies[(int)PatentDamagesDecision.Spend].PreviousVersionOfThisStrategy : Strategies[(int)PatentDamagesDecision.Spend];
            int inventor = 1;
            foreach (var inventorInfo in PDInputs.AllInventorsInfo.InventorsNotBeingOptimized())
            {
                if (!PDProg.InventorTryToInventDecisions[inventor])
                    PDProg.InventorSpendDecisions.Add(0);
                else if (strategy == null)
                    PDProg.InventorSpendDecisions.Add(1.0 * inventorInfo.CostOfMinimumInvestment); 
                else
                {
                    double otherInventorSpendDecision = strategy.Calculate(new List<double> { inventorInfo.CostOfMinimumInvestment, PDProg.InventorEstimatesInventionValue[inventor] });
                    otherInventorSpendDecision *= inventorInfo.CostOfMinimumInvestment;
                    PDProg.InventorSpendDecisions.Add(otherInventorSpendDecision);
                }
                inventor++;
            }
            PDProg.AverageSpendingOfEntrants = PDProg.InventorSpendDecisions.Sum() / (double) PDProg.NumberTrying;
        }

        private void ForecastProbabilityOfSuccessAfterInvestment()
        {
            if (PreparationPhase)
            {
                GetDecisionInputs();
                return;
            }
            if (PDProg.MainInventorTries)
            {
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
                    double multipleOfMinimumInvestment = PDProg.InventorSpendDecisions[i] / InventorInfo(i).CostOfMinimumInvestment; // the spend decision has been multiplied by cost of minimum investment, so we need to divide that back out to get the multiple of minimum investment
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

        private void MakePricingDecision()
        {
            if (PreparationPhase)
            {
                GetDecisionInputs();
                return;
            }
            if (PDProg.InventionOccurs)
            {
                InventorInfo winnerInfo = InventorInfo((int)PDProg.WinnerOfPatent);
                double winnerEstimateInventionValue = PDProg.InventorEstimatesInventionValue[(int)PDProg.WinnerOfPatent];
                if (PDProg.WinnerOfPatent == 0)
                {
                    PDProg.Price = MakeDecision() * winnerEstimateInventionValue;
                }
                else
                {
                    var strategy = CurrentlyEvolving ? Strategies[(int)PatentDamagesDecision.Price].PreviousVersionOfThisStrategy : Strategies[(int)PatentDamagesDecision.Price];
                    var inputs = new List<double> { winnerInfo.CostOfMinimumInvestment, winnerEstimateInventionValue };
                    if (strategy == null)
                        PDProg.Price = DefaultBehaviorBeforeEvolution(inputs, (int)PatentDamagesDecision.Price) * winnerEstimateInventionValue;
                    else
                        PDProg.Price = strategy.Calculate(inputs) * winnerEstimateInventionValue;
                }
                if (PDProg.Price > 10)
                {
                    var DEBUG = 0;
                }
            }
        }

        private void DetermineInadvertentInfringement()
        {
            if (PreparationPhase)
                return;
            if (PDProg.InventionOccurs)
            {
                PDProg.InadvertentInfringement = PDInputs.InadvertentInfringementRandomSeed < PDInputs.InadvertentInfringementProbability ;
            }
        }

        private void DetermineAcceptance()
        {
            if (PreparationPhase)
            {
                GetDecisionInputs();
                return;
            }
            if (PDProg.InventionOccurs && !PDProg.InadvertentInfringement)
            {
                PDProg.PriceAccepted = MakeDecision() > 0;
            }
        }

        private void DetermineIntentionalInfringement()
        {
            if (PreparationPhase)
            {
                GetDecisionInputs();
                return;
            }
            if (PDProg.InventionOccurs && !PDProg.InadvertentInfringement && !PDProg.PriceAccepted)
            {
                PDProg.IntentionalInfringement = MakeDecision() > 0;
            }
            PDProg.InventionUsed = PDProg.InadvertentInfringement || PDProg.PriceAccepted || PDProg.IntentionalInfringement;
        }

        private void DetermineDamages()
        {
            if (PreparationPhase)
                return;
            if (PDProg.InadvertentInfringement || PDProg.IntentionalInfringement)
            {
                double forecastAfterEntry = PDProg.ForecastAfterEntry == 0 ? 1 : PDProg.ForecastAfterEntry;
                double forecastAfterInvestment = PDProg.ForecastAfterInvestment == 0 ? 1 : PDProg.ForecastAfterInvestment;
                double riskAdjustedEntrySpending = PDInputs.CostOfEntry / forecastAfterEntry;
                double riskAdjustedInventionSpending = PDProg.InventorSpendDecisions[(int)PDProg.WinnerOfPatent] / forecastAfterInvestment;
                double costBasedDamages = (riskAdjustedEntrySpending + riskAdjustedInventionSpending) * (1.0 + PDInputs.PermittedRateOfReturn);
                double courtEstimateOfValue = GetEstimateOfInventionValue(PDInputs.InventionValueCourtNoiseStdev, PDInputs.InventionValueCourtNoise, PDInputs.InventionValue);
                double weight = PDProg.InadvertentInfringement ? PDInputs.WeightOnCostPlusDamagesForInadvertentInfringement : PDInputs.WeightOnCostPlusDamagesForIntentionalInfringement;
                double weightedDamages = weight * costBasedDamages + (1.0 - weight) * courtEstimateOfValue;
                double multiplier = PDProg.InadvertentInfringement ? 1.0 : PDInputs.DamagesMultiplierForIntentionalInfringement;
                double fullDamages = weightedDamages * multiplier;
                PDProg.Damages = fullDamages;
            }
            PDProg.AmountPaid = PDProg.PriceAccepted ? PDProg.Price ?? 0 : (PDProg.InventionUsed ? (double)PDProg.Damages : 0);
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
                var DEBUGX = combinedWealthOfPotentialInventors;
                if (inventor < PDProg.NumberEntrants)
                    combinedWealthOfPotentialInventors += wealth;
                inventor++;
            }
            PDProg.AverageInventorUtility = combinedWealthOfPotentialInventors / (double)PDProg.NumberEntrants;
            PDProg.SocialWelfare = socialBenefit;
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
                bool isSuccess = PDProg.WinnerOfPatent == 0 && PDProg.AmountPaid > 0; // note that not getting paid counts as a failure for our purposes -- although one might wonder about that. we could also develop a model in which commercialization might fail, i.e., there is only some probability that the invention will have value.
                double successNumber = isSuccess ? 1.0 : 0;
                double entrySuccessMeasure = (PDProg.ForecastAfterEntry - successNumber) * (PDProg.ForecastAfterEntry - successNumber); // we must square this, so that we're minimizing the square. That ensures an unbiased estimtae
                Score((int)PatentDamagesDecision.SuccessProbabilityAfterEntry, entrySuccessMeasure);
                if (PDProg.MainInventorTries)
                {
                    Score((int)PatentDamagesDecision.Spend, PDProg.MainInventorUtility);
                    Score((int)PatentDamagesDecision.Price, PDProg.MainInventorUtility);
                    double investmentSuccessMeasure = (PDProg.ForecastAfterInvestment - successNumber) * (PDProg.ForecastAfterInvestment - successNumber); // we must square this, so that we're minimizing the square. That ensures an unbiased estimtae
                    Score((int)PatentDamagesDecision.SuccessProbabilityAfterInvestment, PDProg.MainInventorUtility);
                }
            }
            if (PDProg.InventionOccurs && !PDProg.InadvertentInfringement)
            {
                Score((int)PatentDamagesDecision.Accept, PDProg.UserUtility);
                if (PDProg.IntentionalInfringement)
                    Score((int)PatentDamagesDecision.Infringe, PDProg.UserUtility);
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
                case (int)PatentDamagesDecision.Price:
                    inputs = new double[] { InventorToOptimizeInfo.CostOfMinimumInvestment, PDProg.InventorEstimatesInventionValue.First() };
                    break;
                case (int)PatentDamagesDecision.Accept:
                    inputs = new double[] { PDInputs.InventionValue, PDProg.Price ?? 0 };
                    break;
                case (int)PatentDamagesDecision.Infringe:
                    inputs = new double[] { PDInputs.InventionValue, PDProg.Price ?? 0 };
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
                case (int)PatentDamagesDecision.Price:
                    return 0.75;
                case (int)PatentDamagesDecision.Accept:
                    return 1.0;
                case (int)PatentDamagesDecision.Infringe:
                    return 0;
                default:
                    throw new Exception("Unknown decision.");
            }
        }
    }
}
