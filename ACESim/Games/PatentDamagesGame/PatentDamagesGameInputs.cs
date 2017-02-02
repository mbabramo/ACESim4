using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ACESim
{
    [Serializable]
    public class PatentDamagesGameInputs : GameInputs
    {
        /// <summary>
        /// The initial wealth of each entrant. This should not matter so long as actors are risk-neutral.
        /// </summary>
        public double InitialWealthOfEntrants;
        /// <summary>
        /// The cost of entry for each entrant. 
        /// </summary>
        public double CostOfEntry;
        /// <summary>
        /// The market rate of return. For example, 0.05 means that money will receive 5% interest. A participant earns this on any money not used for entry and/or investment.
        /// </summary>
        public double MarketRateOfReturn;
        /// <summary>
        /// The rate-of-return permitted for cost plus damages. For example, 0.05 means that investment (including entry) after risk adjustment will receive 5% interest on top of return of the cost of entry. 
        /// </summary>
        public double PermittedRateOfReturn;
        /// <summary>
        /// The maximum number of entrants. If greater than the number that would eliminate profits, this will control.
        /// </summary>
        public int MaxNumEntrants;
        /// <summary>
        /// The baseline cost of minimum investment. Individual inventors' costs will be relative to this baseline.
        /// </summary>
        public double CostOfMinimumInvestmentBaseline;
        /// <summary>
        /// The invention value is multiplied by this. (This allows the HighestInventionValue to be drawn from a distribution from 0 to 1, but then multiplied to some higher number).
        /// </summary>
        public double HighestInventionValueMultiplier;
        /// <summary>
        /// The actual value of the invention to the user. The user knows this, but others can only estimate it imprecisely.
        /// </summary>
        public double HighestInventionValue;
        /// <summary>
        /// The standard distribution of the distribution of noise from which the noise that obfuscates the court's estimtae of HighestInventionValue is drawn.
        /// </summary>
        public double HighestInventionValueCourtNoiseStdev;
        /// <summary>
        /// The noise to be added to HighestInventionValue to determine the court's signal of invention value. This signal is drawn from a distribution with standard deviation HighestInventionValueCourtNoiseStdev.
        /// </summary>
        public double HighestInventionValueCourtNoise;
        /// <summary>
        /// The standard deviation of the distribution of noise for all inventors. The actual noise resulting for each inventor is in AllInventorsInfo.
        /// </summary>
        public double HighestInventionValueNoiseStdev; 
        /// <summary>
        /// The spillover multiplier. A spillover of 0.10 indicates that there is social value not captured by the user equal to 10% of the user's value.
        /// </summary>
        public double SpilloverMultiplier;
        /// <summary>
        /// The success probability for an inventor making the minimum investment.
        /// </summary>
        public double SuccessProbabilityMinimumInvestment;
        /// <summary>
        /// The success probability for an inventor making twice the minimum investment.
        /// </summary>
        public double SuccessProbabilityDoubleInvestment;
        /// <summary>
        /// The success probability for an inventor making ten times the minimum investment.
        /// </summary>
        public double SuccessProbabilityTenTimesInvestment;
        /// <summary>
        /// The degree to which inventors' successes are independent. If 0, then each inventor investing a minimum investment would have the same probability. If 1, then there is no correlation among inventors' success, though the success probabilities above apply to each inventor.
        /// </summary>
        public double SuccessIndependence;
        /// <summary>
        /// The probability that a user will inadvertently infringe.
        /// </summary>
        public double InadvertentInfringementProbability;
        /// <summary>
        /// The amount that each side must spend if infringement occurs.
        /// </summary>
        public double LitigationCostsEachParty;
        /// <summary>
        /// The random seed that determines how many entrants there are during evolution of the spend decision.
        /// </summary>
        public double EntrantsRandomSeed;
        /// <summary>
        /// The random seed that determines whether the last entrant enters. If the optimal amoung of entry is 1.7, that means there is a 70% chance that there will be two entrants.
        /// </summary>
        public double FractionalEntryRandomSeed;
        /// <summary>
        /// The random seed that determines all inventors' success when inventors' successes are not independent and that has less of a role as inventors' successes become more independent. Each inventor also has his/her own random seed.
        /// </summary>
        public double CommonSuccessRandomSeed;
        /// <summary>
        /// The random succeed that determines which inventor whens when multiple inventors succeed.
        /// </summary>
        public double PickWinnerRandomSeed;
        /// <summary>
        /// The random seed used to determine whether inadvertent infringement occurs, given the occurrence of an invention.
        /// </summary>
        public double InadvertentInfringementRandomSeed;
        /// <summary>
        /// The weight on cost-plus damages (as opposed to estimate of private value) when the user infringes.
        /// </summary>
        public double WeightOnCostPlusDamages;
        /// <summary>
        /// If true, the damages are the lesser of standard and cost-plus damages (so the weighting variable is ignored).
        /// </summary>
        internal bool DamagesAreLesserOfTwoApproaches;
        /// <summary>
        /// A damages multiplier for intentional infringement. A multiplier of 1 means that there is no extra penalty for intentional infringement.
        /// </summary>
        public double DamagesMultiplierForIntentionalInfringement;
        /// <summary>
        /// If true, all inventors' expenditures are aggregated together to determine allowable recovery on cost-plus damages. Meanwhile, the probability is correspondingly lower.
        /// </summary>
        public bool CombineInventorsForCostPlus;
        /// <summary>
        /// If true, inventors' expenditures are reimbursed based on the cost of a minimum investment and the corresponding probability thereof.
        /// </summary>
        public bool UseExpectedCostForCostPlus;
        /// <summary>
        /// If true, then the expected cost takes into account the inventor's cost (rather than the typical inventor baseline cost).
        /// </summary>
        public bool ExpectedCostIsSpecificToInventor;
        /// <summary>
        /// The inventor spends the socially optimal amount, if true, instead of maximizing her own utility on the assumption that she will be the only inventor.
        /// </summary>
        public bool SociallyOptimalSpending;
        /// <summary>
        /// If true, then we will calculate whether the inventor should try based on a comparison of costs and the highest invention value. This is valid (and preferable to neural networks, which won't do well when there is expected to be a very small benefit with perfect information) under certain assumptions, such as perfect information and cost-plus damages with expected costs.
        /// </summary>
        public bool UseExactCalculationForTryDecision;
        /// <summary>
        /// Information specific to each inventor.
        /// </summary>
        public AllInventorsInfo AllInventorsInfo;
    }
}
