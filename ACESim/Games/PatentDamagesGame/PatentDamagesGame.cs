﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Diagnostics;

namespace ACESim
{
    [Serializable]
    public class PatentDamagesGame : Game
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
            CalculateInventorEstimates(PDInputs.AllInventorsInfo.AllInventors(), PDInputs.InventionValue, PDInputs.InventionValueNoiseStdev);
            PDProg.InventorEntryDecisions = new List<bool>();
            bool mainInventorEnters = MakeDecision() > 0;
            PDProg.InventorEntryDecisions.Add(mainInventorEnters);
            var strategy = CurrentlyEvolving ? Strategies[(int)PatentDamagesDecision.Enter].PreviousVersionOfThisStrategy : Strategies[(int)PatentDamagesDecision.Enter];
            int inventor = 1;
            foreach (var inventorInfo in PDInputs.AllInventorsInfo.InventorsNotBeingOptimized())
            {
                if (CurrentlyEvolving && inventor > PDInputs.MaxPotentialEntrants)
                    PDProg.InventorEntryDecisions.Add(true); // during evolution, we limit the number of potential entrants so that we can evolve strategy gradually
                else if (strategy == null)
                    PDProg.InventorEntryDecisions.Add(true);
                else
                {
                    double otherInventorEntryDecision = strategy.Calculate(new List<double> { inventorInfo.CostOfMinimumInvestment });
                    PDProg.InventorEntryDecisions.Add(otherInventorEntryDecision > 0);
                }
            }
        }

        private void ForecastProbabilityOfSuccessAfterEntry()
        {
            if (PDProg.MainInventorEnters)
            {
                PDProg.ForecastAfterEntry = MakeDecision();
            }
        }

        private void MakeTryToInventDecisions()
        {
            PDProg.InventorTryToInventDecisions = new List<bool>();
            bool mainInventorTries = PDProg.MainInventorEnters && MakeDecision() > 0;
            PDProg.InventorTryToInventDecisions.Add(mainInventorTries);
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
        }

        private void MakeSpendDecisions()
        {
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
                    PDProg.InventorSpendDecisions.Add(1.0); // in first round, 
                else
                {
                    double otherInventorSpendDecision = strategy.Calculate(new List<double> { inventorInfo.CostOfMinimumInvestment, PDProg.InventorEstimatesInventionValue[inventor] });
                    otherInventorSpendDecision *= inventorInfo.CostOfMinimumInvestment;
                    PDProg.InventorSpendDecisions.Add(otherInventorSpendDecision);
                }
                inventor++;
            }
        }

        private void ForecastProbabilityOfSuccessAfterInvestment()
        {
            if (PDProg.MainInventorTries)
            {
                PDProg.ForecastAfterInvestment = MakeDecision();
            }
        }

        private void IdentifyPatentWinner()
        {
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
                    double thresholdNeeded = MonotonicCurve.CalculateYValueForX(1.0, 1.0, curvature, multipleOfMinimumInvestment); 
                    bool success = weightedSeed < thresholdNeeded;
                    PDProg.InventorSucceedsAtInvention.Add(success);
                }
                else
                    PDProg.InventorSucceedsAtInvention.Add(false);
            }
            int numSuccessful = PDProg.InventorSucceedsAtInvention.Count();
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
                    PDProg.Price = strategy.Calculate(new List<double> { winnerInfo.CostOfMinimumInvestment, winnerEstimateInventionValue }) * winnerEstimateInventionValue;
                }
            }
        }

        private void DetermineInadvertentInfringement()
        {
            if (PDProg.InventionOccurs)
            {
                PDProg.InadvertentInfringement = PDInputs.InadvertentInfringementRandomSeed < PDInputs.InadvertentInfringementProbability ;
            }
        }

        private void DetermineAcceptance()
        {
            if (PDProg.InventionOccurs && !PDProg.InadvertentInfringement)
            {
                PDProg.PriceAccepted = MakeDecision() > 0;
            }
        }

        private void DetermineIntentionalInfringement()
        {
            if (PDProg.InventionOccurs && !PDProg.InadvertentInfringement && !PDProg.PriceAccepted)
            {
                PDProg.IntentionalInfringement = MakeDecision() > 0;
            }
        }

        private void DetermineDamages()
        {
            if (PDProg.InadvertentInfringement || PDProg.IntentionalInfringement)
            {
                double riskAdjustedEntrySpending = PDInputs.CostOfEntry / PDProg.ForecastAfterEntry;
                double riskAdjustedInventionSpending = PDProg.InventorSpendDecisions[(int)PDProg.WinnerOfPatent] / PDProg.ForecastAfterInvestment;
                double costBasedDamages = (riskAdjustedEntrySpending + riskAdjustedInventionSpending) * (1.0 + PDInputs.PermittedRateOfReturn);
                double courtEstimateOfValue = GetEstimateOfInventionValue(PDInputs.InventionValueCourtNoiseStdev, PDInputs.InventionValueCourtNoise, PDInputs.InventionValue);
                double weight = PDProg.InadvertentInfringement ? PDInputs.WeightOnCostPlusDamagesForInadvertentInfringement : PDInputs.WeightOnCostPlusDamagesForIntentionalInfringement;
                double weightedDamages = weight * costBasedDamages + (1.0 - weight) * courtEstimateOfValue;
                double multiplier = PDProg.InadvertentInfringement ? 1.0 : PDInputs.DamagesMultiplierForIntentionalInfringement;
                double fullDamages = weightedDamages * multiplier;
                PDProg.Damages = fullDamages;
            }
        }

        public void CalculateWelfareOutcomes()
        {
            double allPrivateInvestments = 0; // includes money invested and money put to market
            double socialBenefit = 0;
            int inventor = 0;
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
                double unspentMoney = wealth;
                double marketReturn = unspentMoney * PDInputs.MarketRateOfReturn;
                wealth += marketReturn;
                socialBenefit += wealth;
                if (inventor == (int)PDProg.WinnerOfPatent)
                {
                    double spillover = PDInputs.InventionValue * PDInputs.SpilloverMultiplier; // note that spillover occurs regardless of whether invention is used
                    socialBenefit += spillover;
                    if (PDProg.InventionUsed)
                    {
                        socialBenefit += PDInputs.InventionValue; // if user doesn't use invention, there is no social benefit from it
                        PDProg.UserUtility = PDInputs.InventionValue - PDProg.AmountPaid;
                    }
                    else
                    {
                        PDProg.UserUtility = 0;
                        double patentRevenues = (double)PDProg.AmountPaid;
                        wealth += patentRevenues;
                    }
                }
                if (inventor == 0)
                    PDProg.InventorUtility = wealth;
                inventor++;
            }
        }

        protected void DoScoring()
        {
            Score((int)PatentDamagesDecision.Enter, PDProg.InventorUtility);
            if (PDProg.MainInventorEnters)
            {
                Score((int)PatentDamagesDecision.TryToInvent, PDProg.InventorUtility);
                bool isSuccess = PDProg.WinnerOfPatent == 0 && PDProg.AmountPaid > 0; // note that not getting paid counts as a failure for our purposes -- although one might wonder about that. we could also develop a model in which commercialization might fail, i.e., there is only some probability that the invention will have value.
                double successNumber = isSuccess ? 1.0 : 0;
                double entrySuccessMeasure = (PDProg.ForecastAfterEntry - successNumber) * (PDProg.ForecastAfterEntry - successNumber); // we must square this, so that we're minimizing the square. That ensures an unbiased estimtae
                Score((int)PatentDamagesDecision.SuccessProbabilityAfterEntry, entrySuccessMeasure);
                if (PDProg.MainInventorTries)
                {
                    Score((int)PatentDamagesDecision.Spend, PDProg.InventorUtility);
                    Score((int)PatentDamagesDecision.Price, PDProg.InventorUtility);
                    double investmentSuccessMeasure = (PDProg.ForecastAfterInvestment - successNumber) * (PDProg.ForecastAfterInvestment - successNumber); // we must square this, so that we're minimizing the square. That ensures an unbiased estimtae
                    Score((int)PatentDamagesDecision.SuccessProbabilityAfterInvestment, PDProg.InventorUtility);
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
                    inputs = new double[] { InventorToOptimizeInfo.CostOfMinimumInvestment };
                    break;
                case (int)PatentDamagesDecision.SuccessProbabilityAfterEntry:
                    inputs = new double[] { InventorToOptimizeInfo.CostOfMinimumInvestment };
                    break;
                case (int)PatentDamagesDecision.TryToInvent:
                case (int)PatentDamagesDecision.SuccessProbabilityAfterInvestment:
                case (int)PatentDamagesDecision.Spend:
                case (int)PatentDamagesDecision.Price:
                    inputs = new double[] { InventorToOptimizeInfo.CostOfMinimumInvestment, PDProg.InventorEstimatesInventionValue.First() };
                    break;
                case (int)PatentDamagesDecision.Accept:
                    inputs = new double[] { PDInputs.InventionValue, (double) PDProg.Price };
                    break;
                case (int)PatentDamagesDecision.Infringe:
                    inputs = new double[] { PDInputs.InventionValue, (double)PDProg.Price };
                    break;
                default:
                    throw new Exception("Unknown decision.");
            }
            
            RecordInputsIfNecessary(inputs.ToList());

            return inputs.ToList();
        }

    }
}
