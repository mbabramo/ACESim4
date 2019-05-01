﻿using ACESim.Util;

namespace ACESim
{
    public partial class CounterfactualRegretMinimization
    {
        public struct HedgeVanillaUtilities
        {
            /// <summary>
            /// The utility from the player being optimized playing Hedge against Hedge. This is used to set counterfactual values and regret.
            /// </summary>
            public double HedgeVsHedge;
            /// <summary>
            /// The utility from the player being optimized playing an average strategy against the other player playing an average strategy. This can be used to compare with best response to average strategies; in an epsilon-equilibrium, they should be very close.
            /// </summary>
            public double AverageStrategyVsAverageStrategy;
            /// <summary>
            /// The utility from the player being optimized playing an approximate best response to the other player's use of average strategies. This can be compared to average strategy performance against average strategies; this will be higher, but not much in Epsilon equilibrium. In addition, this can be used in CFR-BR for the player not being optimized, when we have skipped iterations of the player being optimized.
            /// </summary>
            public double BestResponseToAverageStrategy;

            public override string ToString()
            {
                return $"Playing hedge: {HedgeVsHedge} avgstrat {AverageStrategyVsAverageStrategy} approxbestres {BestResponseToAverageStrategy}";
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
}