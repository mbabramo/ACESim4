using ACESim.Util;

namespace ACESim
{
    public struct HedgeVanillaUtilities
    {
        /// <summary>
        /// The utility from the player being optimized playing Hedge against Hedge. This is used to set counterfactual values and regret.
        /// </summary>
        public double HedgeVsHedge;
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
            return $"Playing hedge: {HedgeVsHedge} approx avgstrat {AverageStrategyVsAverageStrategy} approx bestres {BestResponseToAverageStrategy} diff {BestResponseToAverageStrategy - AverageStrategyVsAverageStrategy}";
        }

        public void IncrementBasedOnNotYetProbabilityAdjusted(ref HedgeVanillaUtilities other, double averageStrategyProbability, double hedgeProbability)
        {
            HedgeVsHedge += other.HedgeVsHedge * hedgeProbability;
            AverageStrategyVsAverageStrategy += other.AverageStrategyVsAverageStrategy * averageStrategyProbability;
            BestResponseToAverageStrategy += other.BestResponseToAverageStrategy * averageStrategyProbability;
        }

        public void IncrementBasedOnProbabilityAdjusted(ref HedgeVanillaUtilities other)
        {
            // supports parallelism, since we may increment by multiple probability adjusted items at once
            Interlocking.Add(ref HedgeVsHedge, other.HedgeVsHedge);
            Interlocking.Add(ref AverageStrategyVsAverageStrategy, other.AverageStrategyVsAverageStrategy);
            Interlocking.Add(ref BestResponseToAverageStrategy, other.BestResponseToAverageStrategy);
        }

        public void MakeProbabilityAdjusted(double actionProbability)
        {
            HedgeVsHedge *= actionProbability;
            AverageStrategyVsAverageStrategy *= actionProbability;
            BestResponseToAverageStrategy *= actionProbability;
        }
    }
}