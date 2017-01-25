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
        /// The market rate of return. For example, 0.05 means that money will receive 5% interest. A participant earns this on any money not used for entry and/or investment.
        /// </summary>
        public double MarketRateOfReturn;
        /// <summary>
        /// The rate-of-return permitted for cost plus damages. For example, 0.05 means that investment (including entry) after risk adjustment will receive 5% interest on top of return of the cost of entry. 
        /// </summary>
        public double PermittedRateOfReturn;
        /// <summary>
        /// The cost of entry, which is required to have a chance to decide whether to engage in investment.
        /// </summary>
        public double CostOfEntry;
        /// <summary>
        /// The maximum number of potential inventors. We increase this gradually during evolution to above the maximum supported by AllInventorsInfo.
        /// </summary>
        public double MaxPotentialEntrants; 
        /// <summary>
        /// The actual value of the invention to the user. The user knows this, but others can only estimate it imprecisely.
        /// </summary>
        public double InventionValue;
        /// <summary>
        /// The standard distribution of the distribution of noise from which the noise that obfuscates the court's estimtae of InventionValue is drawn.
        /// </summary>
        public double InventionValueCourtNoiseStdev;
        /// <summary>
        /// The noise to be added to InventionValue to determine the court's signal of invention value. This signal is drawn from a distribution with standard deviation InventionValueCourtNoiseStdev.
        /// </summary>
        public double InventionValueCourtNoise;
        /// <summary>
        /// The standard deviation of the distribution of noise for all inventors. The actual noise resulting for each inventor is in AllInventorsInfo.
        /// </summary>
        public double InventionValueNoiseStdev; 
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
        /// The weight on cost-plus damages (as opposed to estimate of private value) for inadvertent infringement.
        /// </summary>
        public double WeightOnCostPlusDamagesForInadvertentInfringement;
        /// <summary>
        /// The weight on cost-plus damages (as opposed to estimate of private value) for intentional infringement.
        /// </summary>
        public double WeightOnCostPlusDamagesForIntentionalInfringement;
        /// <summary>
        /// A damages multiplier for intentional infringement. A multiplier of 1 means that there is no intentional infringement.
        /// </summary>
        public double DamagesMultiplierForIntentionalInfringement;
        /// <summary>
        /// Information specific to each inventor.
        /// </summary>
        public AllInventorsInfo AllInventorsInfo;
        
    }
}
