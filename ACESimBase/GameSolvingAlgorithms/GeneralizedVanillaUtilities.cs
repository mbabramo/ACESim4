using ACESim.Util;
using System;

namespace ACESim
{
    [Serializable]
    public struct GeneralizedVanillaUtilities
    {
        /// <summary>
        /// The utility from the player being optimized playing its current strategy vs. an opponent playing its current strategy. This is used to set counterfactual values and regret.
        /// </summary>
        public double CurrentVsCurrent;
        /// <summary>
        /// The utility from the player being optimized playing an average strategy against the other player playing an average strategy. This can be used to compare with best response to average strategies; in an epsilon-equilibrium, they should be very close. Note that this is an exact result based on the average strategies of the prior player at the time of the most recent player's update, but it will not be up to date after the most recent player updates.
        /// </summary>
        public double AverageStrategyVsAverageStrategy;
        /// <summary>
        /// The utility from the player being optimized playing an approximate best response to the other player's use of average strategies. This can be compared to average strategy performance against average strategies; this will be higher, but not much in Nash Epsilon equilibrium. Also, best response (to correlated equilibrium strategy over time) can be compared to correlated equilibrium strategy; it should be better too, but not by much
        /// </summary>
        public double BestResponseToAverageStrategy;

        public override string ToString()
        {
            return $"Playing current: {CurrentVsCurrent} approx avgstrat {AverageStrategyVsAverageStrategy} approx bestres {BestResponseToAverageStrategy} diff {BestResponseToAverageStrategy - AverageStrategyVsAverageStrategy}";
        }

        public void IncrementBasedOnNotYetProbabilityAdjusted(ref GeneralizedVanillaUtilities other, double averageStrategyProbability, double hedgeProbability)
        {
            CurrentVsCurrent += other.CurrentVsCurrent * hedgeProbability;
            AverageStrategyVsAverageStrategy += other.AverageStrategyVsAverageStrategy * averageStrategyProbability;
            BestResponseToAverageStrategy += other.BestResponseToAverageStrategy * averageStrategyProbability;
        }

        public void IncrementBasedOnProbabilityAdjusted(ref GeneralizedVanillaUtilities other)
        {
            // supports parallelism, since we may increment by multiple probability adjusted items at once
            Interlocking.Add(ref CurrentVsCurrent, other.CurrentVsCurrent);
            Interlocking.Add(ref AverageStrategyVsAverageStrategy, other.AverageStrategyVsAverageStrategy);
            Interlocking.Add(ref BestResponseToAverageStrategy, other.BestResponseToAverageStrategy);
        }

        public void MakeProbabilityAdjusted(double actionProbability)
        {
            CurrentVsCurrent *= actionProbability;
            AverageStrategyVsAverageStrategy *= actionProbability;
            BestResponseToAverageStrategy *= actionProbability;
        }
    }
}