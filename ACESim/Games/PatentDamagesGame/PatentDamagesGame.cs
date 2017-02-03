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
        InventorInfo MainInventorInfo => PDInputs.AllInventorsInfo.InventorToOptimize;
        InventorInfo InventorInfo(int i) => PDInputs.AllInventorsInfo.Inventor(i);
        int NumPotentialInventors => PDInputs.AllInventorsInfo.NumPotentialInventors;

        double GetCurvature => MonotonicCurve.CalculateCurvatureForThreePoints(1.0, PDInputs.SuccessProbabilityMinimumInvestment, 2.0, PDInputs.SuccessProbabilityDoubleInvestment, 10.0, PDInputs.SuccessProbabilityTenTimesInvestment);

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
                case (int)PatentDamagesDecision.Spend:
                    MakeSpendDecisions();
                    if (!PreparationPhase)
                        ConcludeGame();
                    break;
                default:
                    throw new Exception("Unknown decision.");
            }
        }

        private void ConcludeGame()
        {
            CalculateSuccessProbabilities();
            IdentifyPatentWinner();
            DetermineWelfareEffects();
            DoScoring();
            Progress.GameComplete = true;
        }

        public void CalculateInventorEstimates(IEnumerable<InventorInfo> inventorInfos, double actualHighestInventionValue, double stdDevOfNoiseDistribution)
        {
            PDProg.InventorEstimatesHighestInventionValue = new List<double>();
            foreach (var inventorInfo in inventorInfos)
            {
                double estimate = PatentDamagesGame.GetEstimateOfHighestInventionValue(stdDevOfNoiseDistribution, inventorInfo.HighestInventionValueNoise, actualHighestInventionValue);
                PDProg.InventorEstimatesHighestInventionValue.Add(estimate);
            }
        }

        public static double GetEstimateOfHighestInventionValue(double stdDevOfNoiseDistribution, double drawFromNoiseDistribution, double actualHighestInventionValue)
        {
            return ObfuscationGame.ObfuscationCorrectAnswer.Calculate(stdDevOfNoiseDistribution, drawFromNoiseDistribution + actualHighestInventionValue);
        }

        private void MakeEntryDecisions()
        {
            if (PreparationPhase)
            {
                PDProg.HighValue = PDInputs.HighestInventionValue > 0.8;
                CalculateInventorEstimates(PDInputs.AllInventorsInfo.AllInventors(), PDInputs.HighestInventionValue, PDInputs.HighestInventionValueNoiseStdev);
                GetDecisionInputs();
                return;
            }
            double fractionalEntry;
            int numEntering;
            if (CurrentlyEvolving && (CurrentlyEvolvingDecisionIndex == (int)PatentDamagesDecision.Spend))
                fractionalEntry = 1.0 + PDInputs.EntrantsRandomSeed * ((double)PDInputs.MaxNumEntrants - 1.0); // randomize the entry while evolving the spend decision
            else
                fractionalEntry = MakeDecision();
            numEntering = (int)Math.Floor(fractionalEntry);
            if (numEntering < 1)
                numEntering = 1;
            if (PDInputs.FractionalEntryRandomSeed < (fractionalEntry - numEntering))
                numEntering++;
            if (numEntering > PDInputs.MaxNumEntrants)
                numEntering = PDInputs.MaxNumEntrants;
            PDProg.NumberEntrants = numEntering;
            PDProg.MainInventorEnters = numEntering > 0;
            PDProg.InventorEntryDecisions = new List<bool>();
            for (int i = 0; i < PDInputs.AllInventorsInfo.NumPotentialInventors; i++)
                PDProg.InventorEntryDecisions.Add(i + 1 <= numEntering);
            PDProg.SomeoneEnters = PDProg.InventorEntryDecisions.Any(x => x == true);
        }

        private void MakeSpendDecisions()
        {
            if (PreparationPhase)
            {
                GetDecisionInputs();
                return;
            }
            PDProg.InventorTryToInventDecisions = new List<bool>();
            PDProg.InventorSpendDecisions = new List<double>();
            double mainInventorRawDecision = MakeDecision();
            double mainInventorSpendMultiple = mainInventorRawDecision >= 1.0 ? mainInventorRawDecision : 0; // must commit to at least minimum spend multiple to try at all
            PDProg.MainInventorTries = PDProg.MainInventorEnters && mainInventorSpendMultiple > 0;
            if (PDProg.MainInventorTries)
                PDProg.MainInventorSpendMultiple = mainInventorSpendMultiple;
            double mainInventorSpendLevel = mainInventorSpendMultiple * PDInputs.CostOfMinimumInvestmentBaseline * MainInventorInfo.CostOfMinimumInvestmentMultiplier;
            PDProg.InventorSpendDecisions.Add(mainInventorSpendLevel);
            if (!CurrentlyEvolving || CurrentlyEvolvingDecisionIndex != (int)PatentDamagesDecision.Spend)
            {
                if (PDInputs.UseExactCalculationForTryDecision)
                {
                    PDProg.MainInventorTries = PDInputs.CostOfMinimumInvestmentBaseline * MainInventorInfo.CostOfMinimumInvestmentMultiplier * 4.0 < PDInputs.HighestInventionValue; // special override, since neural networks won't work well for goldilocks scenario
                    if (!PDProg.MainInventorTries)
                        PDProg.InventorSpendDecisions[0] = 0;
                }
            }
            PDProg.InventorTryToInventDecisions.Add(PDProg.MainInventorTries);
            var strategy = Strategies[(int)PatentDamagesDecision.Spend].PreviousVersionOfThisStrategy ?? Strategies[(int)PatentDamagesDecision.Spend]; // use previous version of strategy or, on first iteration, assume everyone is using the same strategy
            int inventor = 1;
            foreach (var inventorInfo in PDInputs.AllInventorsInfo.InventorsNotBeingOptimized())
            {
                if (!PDProg.InventorEntryDecisions[inventor])
                    PDProg.InventorTryToInventDecisions.Add(false);
                else
                {
                    double otherInventorRawDecision = strategy.Calculate(new List<double> { PDInputs.CostOfMinimumInvestmentBaseline, inventorInfo.CostOfMinimumInvestmentMultiplier, PDProg.InventorEstimatesHighestInventionValue[inventor], PDProg.NumberEntrants }, this);
                    double otherInventorSpendMultiple = otherInventorRawDecision >= 1.0 ? otherInventorRawDecision : 0; // must commit to at least minimum spend multiple to try at all
                    bool otherInventorTries = otherInventorSpendMultiple > 0;
                    double otherInventorSpendLevel = otherInventorSpendMultiple * PDInputs.CostOfMinimumInvestmentBaseline * inventorInfo.CostOfMinimumInvestmentMultiplier;
                    PDProg.InventorTryToInventDecisions.Add(otherInventorTries);
                    PDProg.InventorSpendDecisions.Add(otherInventorSpendLevel);
                }
                inventor++;
            }
            PDProg.SomeoneTries = PDProg.InventorTryToInventDecisions.Any(x => x == true);
            PDProg.NumberTrying = PDProg.InventorTryToInventDecisions.Count(x => x == true);
            var totalSpending = PDProg.InventorSpendDecisions.Sum();
            PDProg.AverageSpendingOfTriers = totalSpending / (double)PDProg.NumberTrying;
            PDProg.TotalSpending = totalSpending + PDProg.NumberEntrants * PDInputs.CostOfEntry;

        }

        private void CalculateSuccessProbabilities()
        {
            // Calculate the probability that each inventor wins the patent
            PDProg.ProbabilityInventingSuccessfully = new double[PDProg.NumberEntrants];
            double curvature = GetCurvature;
            for (int i = 0; i < PDProg.NumberEntrants; i++)
                PDProg.ProbabilityInventingSuccessfully[i] = GetProbabilityOfInventionSuccess(curvature, i);
            if (PDInputs.SuccessIndependence == 1.0)
                PDProg.ProbabilityWinningPatent = ACESim.LotteryProbabilities.GetProbabilityOfBeingUltimateWinner_IndependentProbabilities(PDProg.ProbabilityInventingSuccessfully);
            else if (PDInputs.SuccessIndependence == 0.0)
                PDProg.ProbabilityWinningPatent = ACESim.LotteryProbabilities.GetProbabilityOfBeingUltimateWinner_DependentProbabilities(PDProg.ProbabilityInventingSuccessfully);
            else
            {
                var arrayInd = ACESim.LotteryProbabilities.GetProbabilityOfBeingUltimateWinner_IndependentProbabilities(PDProg.ProbabilityInventingSuccessfully);
                var arrayDep = ACESim.LotteryProbabilities.GetProbabilityOfBeingUltimateWinner_DependentProbabilities(PDProg.ProbabilityInventingSuccessfully);
                PDProg.ProbabilityWinningPatent = new double[arrayInd.Length];
                for (int i = 0; i < arrayInd.Length; i++)
                    PDProg.ProbabilityWinningPatent[i] = PDInputs.SuccessIndependence * arrayInd[i] + (1.0 - PDInputs.SuccessIndependence) * arrayDep[i];
            }
            PDProg.ProbabilitySomeoneWins = PDProg.ProbabilityWinningPatent.Sum();
            PDProg.ProbabilityFirstInventorWins = PDProg.ProbabilityWinningPatent[0];
        }

        private void IdentifyPatentWinner()
        {
            PDProg.InventorSucceedsAtInvention = new List<bool>();
            double curvature = GetCurvature;
            for (int i = 0; i < NumPotentialInventors; i++)
            {
                bool tries = PDProg.InventorTryToInventDecisions[i];
                if (tries)
                {
                    bool success = InventorSucceedsAtInventing(curvature, i);
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

        private bool InventorSucceedsAtInventing(double curvature, int i)
        {
            double weightedSeed, thresholdNeeded;
            GetWeightedSeedAndThresholdNeeded(curvature, i, out weightedSeed, out thresholdNeeded);
            bool success = weightedSeed < thresholdNeeded;
            return success;
        }

        private bool InventorSucceedsAtInventing(double curvature, double inventorRandomSeed, double commonSuccessRandomSeed, double spendDecision, double costOfMinimumInvestmentMultiplier)
        {
            double weightedSeed, thresholdNeeded;
            GetWeightedSeedAndThresholdNeeded(curvature, out weightedSeed, out thresholdNeeded, inventorRandomSeed, commonSuccessRandomSeed, spendDecision, costOfMinimumInvestmentMultiplier);
            bool success = weightedSeed < thresholdNeeded;
            return success;
        }

        private void GetWeightedSeedAndThresholdNeeded(double curvature, int i, out double weightedSeed, out double thresholdNeeded)
        {
            var inventorInfo = InventorInfo(i);
            var inventorRandomSeed = inventorInfo.RandomSeed;
            var commonSuccessRandomSeed = PDInputs.CommonSuccessRandomSeed;
            var spendDecision = PDProg.InventorSpendDecisions[i];
            var costOfMinimumInvestmentMultiplier = inventorInfo.CostOfMinimumInvestmentMultiplier;
            GetWeightedSeedAndThresholdNeeded(curvature, out weightedSeed, out thresholdNeeded, inventorRandomSeed, commonSuccessRandomSeed, spendDecision, costOfMinimumInvestmentMultiplier);
        }

        private void GetWeightedSeedAndThresholdNeeded(double curvature, out double weightedSeed, out double thresholdNeeded, double inventorRandomSeed, double commonSuccessRandomSeed, double spendDecision, double costOfMinimumInvestmentMultiplier)
        {
            weightedSeed = commonSuccessRandomSeed * (1.0 - PDInputs.SuccessIndependence) + inventorRandomSeed * PDInputs.SuccessIndependence;
            double multipleOfMinimumInvestment = spendDecision / (PDInputs.CostOfMinimumInvestmentBaseline * costOfMinimumInvestmentMultiplier); // the spend decision has been multiplied by cost of minimum investment, so we need to divide that back out to get the multiple of minimum investment
            thresholdNeeded = MonotonicCurve.CalculateValueBasedOnProportionOfWayBetweenValues(PDInputs.SuccessProbabilityMinimumInvestment, PDInputs.SuccessProbabilityTenTimesInvestment, curvature, (multipleOfMinimumInvestment - 1.0) / 9.0);
            if (double.IsNaN(thresholdNeeded))
                thresholdNeeded = PDInputs.SuccessProbabilityMinimumInvestment;
        }


        private double GetProbabilitySuccessfulInvention(double curvature, double commonSuccessRandomSeed, double spendDecision, double costOfMinimumInvestmentMultiplier)
        {
            double multipleOfMinimumInvestment = spendDecision / (PDInputs.CostOfMinimumInvestmentBaseline * costOfMinimumInvestmentMultiplier);
            double thresholdNeeded = MonotonicCurve.CalculateValueBasedOnProportionOfWayBetweenValues(PDInputs.SuccessProbabilityMinimumInvestment, PDInputs.SuccessProbabilityTenTimesInvestment, curvature, (multipleOfMinimumInvestment - 1.0) / 9.0);

            if (PDInputs.SuccessIndependence == 0)
                return commonSuccessRandomSeed; // it just depends on the common random seed if inventor's effort has nothing to do with it.
            if (PDInputs.SuccessIndependence == 1)
                return thresholdNeeded; // it's all a question of the inventor's own achievement
            // commonSuccessRandomSeed * (1.0 - PDInputs.SuccessIndependence) + inventorRandomSeedThreshold * PDInputs.SuccessIndependence < thresholdNeeded, so...
            double inventorRandomSeedThreshold = (thresholdNeeded - commonSuccessRandomSeed * (1.0 - PDInputs.SuccessIndependence)) / PDInputs.SuccessIndependence;
            return inventorRandomSeedThreshold;
        }

        private double GetProbabilityOfInventionSuccess(double curvature, int i)
        {
            double weightedSeed, thresholdNeeded;
            GetWeightedSeedAndThresholdNeeded(curvature, i, out weightedSeed, out thresholdNeeded);
            return thresholdNeeded;
        }

        private void DetermineWelfareEffects()
        {
            if (PDProg.InventionOccurs)
            {
                DeterminePermittedPriceEstimates();
                DetermineUserEffectsAndRevenues();
            }

            DetermineGlobalWelfareEffects();
        }

        private void DeterminePermittedPriceEstimates()
        {
            PDProg.InventorSetPrice = CalculatePermittedPrice(PerspectiveToUse.Inventor);
            PDProg.CourtSetPrice = CalculatePermittedPrice(PerspectiveToUse.Court);
            PDProg.UserAnticipatedPrice = CalculatePermittedPrice(PerspectiveToUse.ActualUserValue);
        }

        private void DetermineUserEffectsAndRevenues()
        {
            // Some users may find the price too high. Other users, if any, will all do the same thing -- either accept the price or intentionally infringe. Because all users have the same cost estimate, we will not have some accepting the price and others intentinoally infringing. 
            var adjHighestInventionValue = PDInputs.HighestInventionValue * PDInputs.HighestInventionValueMultiplier;
            bool priceAcceptableToSome = PDProg.InventorSetPrice < adjHighestInventionValue;
            var anticipatedCostIntentionalInfringement = PDProg.UserAnticipatedPrice + PDInputs.LitigationCostsEachParty;
            bool intentionalInfringementBySome = PDProg.InventorSetPrice > anticipatedCostIntentionalInfringement && anticipatedCostIntentionalInfringement < adjHighestInventionValue;

            ResultsBasedOnPrice awareUsersResults;
            if (intentionalInfringementBySome)
            {
                awareUsersResults = GetResultsBasedOnPrice_IntentionalInfringement((double)PDProg.UserAnticipatedPrice, (double)PDProg.CourtSetPrice);
                if (awareUsersResults.ProportionUsingProduct > 0 && PDInputs.InadvertentInfringementProbability != 1.0)
                    PDProg.SomeIntentionalInfringement = true;
                PDProg.ProportionIntentionallyInfringing = awareUsersResults.ProportionUsingProduct * (1.0 - PDInputs.InadvertentInfringementProbability);
            }
            else if (priceAcceptableToSome)
            {
                awareUsersResults = GetResultsBasedOnPrice_AssumingAgreement((double)PDProg.InventorSetPrice);
                if (awareUsersResults.ProportionUsingProduct > 0 && PDInputs.InadvertentInfringementProbability != 1.0)
                    PDProg.SomeUsersPay = true;
                PDProg.ProportionAgreeingOnPrice = awareUsersResults.ProportionUsingProduct * (1.0 - PDInputs.InadvertentInfringementProbability);
            }
            else
                awareUsersResults = GetResultsBasedOnPrice_NoUse();
            awareUsersResults.SetPopulationProportion(1.0 - PDInputs.InadvertentInfringementProbability);

            ResultsBasedOnPrice inadvertentInfringersResults = GetResultsBasedOnPrice_InadvertentInfringement((double)PDProg.CourtSetPrice);
            inadvertentInfringersResults.SetPopulationProportion(PDInputs.InadvertentInfringementProbability);
            PDProg.ProportionInadvertentlyInfringing = inadvertentInfringersResults.ProportionUsingProduct;

            ResultsBasedOnPrice overall = awareUsersResults.Combine(inadvertentInfringersResults);
            PDProg.AverageGrossUserBenefit = overall.PerUserBenefit;
            PDProg.AverageUserCost = overall.PerUserCost;
            PDProg.PerUserReceipts = overall.PerUserReceipts;
            PDProg.UserUtility = overall.TotalNetUserBenefit;
            PDProg.TotalReceipts = overall.InventorRevenues;
        }

        private void DetermineGlobalWelfareEffects()
        {
            // now, factor in wealth effects on inventors
            double allPrivateInvestments = 0; // includes money invested and money put to market
            int inventor = 0;
            double combinedWealthOfPotentialInventors = 0;
            double spillover = 0;
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
                double unspentMoney = wealth; // note that unspent money even from those who don't even enter enters into social welfare
                double marketReturn = unspentMoney * PDInputs.MarketRateOfReturn;
                wealth += marketReturn;
                if (inventor == PDProg.WinnerOfPatent)
                {
                    spillover = PDInputs.HighestInventionValue * PDInputs.HighestInventionValueMultiplier * PDInputs.SpilloverMultiplier; // note that spillover occurs regardless of whether invention is used
                    wealth += PDProg.TotalReceipts; // note that this is net of litigation cost
                }
                if (inventor == 0)
                    PDProg.MainInventorUtility = wealth;
                if (inventor < PDProg.NumberEntrants)
                    combinedWealthOfPotentialInventors += wealth;
                inventor++;
            }
            PDProg.AverageInventorUtility = combinedWealthOfPotentialInventors / (double)PDProg.NumberEntrants;
            PDProg.DeltaInventorsUtility = (PDProg.AverageInventorUtility - PDInputs.InitialWealthOfEntrants) * (double)PDProg.NumberEntrants;
            PDProg.PrivateWelfare = PDProg.UserUtility + PDProg.DeltaInventorsUtility;
            PDProg.SocialWelfare = PDProg.PrivateWelfare + spillover;
            PDProg.MainInventorLoses = PDProg.MainInventorUtility - 1.0 < 0;
        }

        public class ResultsBasedOnPrice
        {
            public double ProportionUsingProduct;
            public double PerUserBenefit;
            public double PerUserCost;
            public double PerUserReceipts;
            public double TotalNetUserBenefit => ProportionUsingProduct * (PerUserBenefit - PerUserCost);
            public double InventorRevenues => ProportionUsingProduct * PerUserReceipts;

            public void SetPopulationProportion(double proportion) => ProportionUsingProduct *= proportion;

            public ResultsBasedOnPrice Combine(ResultsBasedOnPrice other)
            {
                if (ProportionUsingProduct == 0)
                    return other;
                if (other.ProportionUsingProduct == 0)
                    return this;
                double weightOfThis = ProportionUsingProduct / (ProportionUsingProduct + other.ProportionUsingProduct);
                double weightOfOther = 1.0 - weightOfThis;
                ResultsBasedOnPrice combined = new ResultsBasedOnPrice()
                {
                    ProportionUsingProduct = ProportionUsingProduct + other.ProportionUsingProduct,
                    PerUserBenefit = weightOfThis * PerUserBenefit + weightOfOther * other.PerUserBenefit,
                    PerUserCost = weightOfThis * PerUserCost + weightOfOther * other.PerUserCost,
                    PerUserReceipts = weightOfThis * PerUserReceipts + weightOfOther * other.PerUserReceipts
                };
                return combined;
            }

        }

        private ResultsBasedOnPrice GetResultsBasedOnPrice_AssumingAgreement(double priceSetByInventor)
        {
            double v = PDInputs.HighestInventionValue * PDInputs.HighestInventionValueMultiplier;
            return new ResultsBasedOnPrice()
            {
                ProportionUsingProduct = (v - priceSetByInventor) / v,
                PerUserBenefit = (v + priceSetByInventor) / 2.0,
                PerUserCost = priceSetByInventor,
                PerUserReceipts = priceSetByInventor
            };
        }

        private ResultsBasedOnPrice GetResultsBasedOnPrice_IntentionalInfringement(double priceEstimatedByUser, double priceSetByCourt)
        {
            double v = PDInputs.HighestInventionValue * PDInputs.HighestInventionValueMultiplier;
            double c = PDInputs.LitigationCostsEachParty;
            return new ResultsBasedOnPrice()
            {
                ProportionUsingProduct = (v - (priceEstimatedByUser + c)) / v,
                PerUserBenefit = (v + (priceEstimatedByUser + c)) / 2.0,
                PerUserCost = priceSetByCourt + c,
                PerUserReceipts = priceSetByCourt - c
            };
        }

        private ResultsBasedOnPrice GetResultsBasedOnPrice_NoUse()
        {
            return new ResultsBasedOnPrice()
            {
                ProportionUsingProduct = 0,
                PerUserBenefit = 0,
                PerUserCost = 0,
                PerUserReceipts = 0
            };
        }

        private ResultsBasedOnPrice GetResultsBasedOnPrice_InadvertentInfringement(double priceSetByCourt)
        {
            double v = PDInputs.HighestInventionValue * PDInputs.HighestInventionValueMultiplier;
            double c = PDInputs.LitigationCostsEachParty;
            return new ResultsBasedOnPrice()
            {
                ProportionUsingProduct = 1.0,
                PerUserBenefit = v / 2.0,
                PerUserCost = priceSetByCourt + c,
                PerUserReceipts = priceSetByCourt - c
            };
        }

        private enum PerspectiveToUse
        {
            ActualUserValue,
            Court,
            Inventor
        }

        private double CalculatePermittedPrice(PerspectiveToUse perspective)
        {
            var winnerOfPatent = (int)PDProg.WinnerOfPatent;
            double highestInventionValueEstimate = GetHighestInventionValueEstimate(perspective, winnerOfPatent) * PDInputs.HighestInventionValueMultiplier;

            // Let p = price as a proportion of the maximum valuation V. Revenues = (1 - p) * pV = pV - p^2*V. This is maximized at p = 0.5 when marginal cost is zero. So, for valuation-based damages, we assume that the inventor chooses 0.5 * the inventor's estimate of maximum user's valuation. 
            double permittedPriceStandardDamages = highestInventionValueEstimate / 2.0;
            double anticipatedRevenuesStandardDamages = (1 - 0.5) * 0.5 * highestInventionValueEstimate;
            if (PDInputs.WeightOnCostPlusDamages == 0 && !PDInputs.DamagesAreLesserOfTwoApproaches)
                return permittedPriceStandardDamages;
            
            double forecastAfterInvestment = PDInputs.CombineInventorsForCostPlus ? PDProg.ProbabilitySomeoneWins : PDProg.ProbabilityWinningPatent[winnerOfPatent];
            double riskAdjustedInventionSpending = (PDInputs.CombineInventorsForCostPlus ? PDProg.TotalSpending : PDProg.InventorSpendDecisions[winnerOfPatent]) / forecastAfterInvestment;
            if (PDInputs.UseExpectedCostForCostPlus)
            {
                double inventionCost = PDInputs.CostOfMinimumInvestmentBaseline;
                if (PDInputs.ExpectedCostIsSpecificToInventor)
                    inventionCost *= InventorInfo(winnerOfPatent).CostOfMinimumInvestmentMultiplier;
                riskAdjustedInventionSpending = inventionCost / PDInputs.SuccessProbabilityMinimumInvestment;
            }
            double permissibleRecovery = riskAdjustedInventionSpending * (1.0 + PDInputs.PermittedRateOfReturn);
            double proportionOfHighestInventionValue;
            if (anticipatedRevenuesStandardDamages <= permissibleRecovery)
                proportionOfHighestInventionValue = permittedPriceStandardDamages;
            else
                // Now, we calculate the proportion of maximum invention value that we would need to get this permissible recovery. Continuing with the equations above, we can set pV - p^2*V = X, where X is the maximum cost recovery. We need solutions in p to -Vp^2 + Vp - X = 0. If there are two positive solutions (we could recover the amount with a tiny amount of users or with a lot of users), we take the lower price.
                proportionOfHighestInventionValue = GetSmallerPositiveSolutionToQuadraticEquation(0 - highestInventionValueEstimate, highestInventionValueEstimate, 0 - permissibleRecovery);
            double permittedPriceCostPlusDamages = highestInventionValueEstimate * proportionOfHighestInventionValue;
            double anticipatedRevenuesCostPlusDamages = (1 - proportionOfHighestInventionValue) * proportionOfHighestInventionValue * highestInventionValueEstimate; // should be less than or approx equal to permissible recovery

            if (PDInputs.DamagesAreLesserOfTwoApproaches)
            {
                return Math.Min(permittedPriceCostPlusDamages, permittedPriceStandardDamages);
            }

            double weight = PDInputs.WeightOnCostPlusDamages;
            return weight * permittedPriceCostPlusDamages + (1 - weight) * permittedPriceStandardDamages;
        }

        private double GetSmallerPositiveSolutionToQuadraticEquation(double a, double b, double c)
        {
            double sqrtpart = (b * b) - (4 * a * c);
            double answer1 = ((-1) * b + Math.Sqrt(sqrtpart)) / (2 * a);
            double answer2 = ((-1) * b - Math.Sqrt(sqrtpart)) / (2 * a);
            if (answer1 > 0 && answer2 > 0)
                return Math.Min(answer1, answer2);
            if (answer1 > 0)
                return answer1;
            if (answer2 > 0)
                return answer2;
            throw new Exception("Equation has no positive solutions");
        }

        private double GetHighestInventionValueEstimate(PerspectiveToUse perspective, int inventorIndex)
        {
            double highestInventionValue = 0;
            switch (perspective)
            {
                case PerspectiveToUse.ActualUserValue:
                    highestInventionValue = PDInputs.HighestInventionValue;
                    break;
                case PerspectiveToUse.Court:
                    highestInventionValue = GetEstimateOfHighestInventionValue(PDInputs.HighestInventionValueCourtNoiseStdev, PDInputs.HighestInventionValueCourtNoise, PDInputs.HighestInventionValue);
                    break;
                case PerspectiveToUse.Inventor:
                    highestInventionValue = PDProg.InventorEstimatesHighestInventionValue[inventorIndex];
                    break;
            }

            return highestInventionValue;
        }

        protected void DoScoring()
        {
            if (PreparationPhase || !CurrentlyEvolving)
                return;
            var squaredDifference = PDProg.AverageInventorUtility; // we're optimizing how close we can get the AVERAGE of all average utility scores to 1. We define this in the game definition.
            base.Score((int)PatentDamagesDecision.Enter, squaredDifference); // we're minimizing profits
            if (PDProg.MainInventorEnters)
            {
                Score((int)PatentDamagesDecision.Spend, PDInputs.SociallyOptimalSpending ? PDProg.SocialWelfare : PDProg.MainInventorUtility);
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
                case (int)PatentDamagesDecision.Spend:
                    inputs = new double[] { PDInputs.CostOfMinimumInvestmentBaseline, MainInventorInfo.CostOfMinimumInvestmentMultiplier, PDProg.InventorEstimatesHighestInventionValue.First(), PDProg.NumberEntrants };
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
                case (int)PatentDamagesDecision.Spend:
                    return 1.0;
                default:
                    throw new Exception("Unknown decision.");
            }
        }
    }
}
