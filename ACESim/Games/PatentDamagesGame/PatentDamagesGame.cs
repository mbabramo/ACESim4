using System;
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
                    CalculateDamages();
                    break;
                default:
                    // Do Nothing; there are only two decisions
                    Progress.GameComplete = true;
                    break;
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
                    double thresholdNeeded = MonotonicCurve.CalculateYValueForX(1.0, 1.0, curvature, PDProg.InventorSpendDecisions[i]);
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
                if (PDProg.WinnerOfPatent == 0)
                    PDProg.Price = MakeDecision();
                else
                {
                    var strategy = CurrentlyEvolving ? Strategies[(int)PatentDamagesDecision.Price].PreviousVersionOfThisStrategy : Strategies[(int)PatentDamagesDecision.Price];
                    PDProg.Price = strategy.Calculate(new List<double> { InventorInfo((int) PDProg.WinnerOfPatent).CostOfMinimumInvestment, PDProg.InventorEstimatesInventionValue[(int) PDProg.WinnerOfPatent] });
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
            asdf
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
