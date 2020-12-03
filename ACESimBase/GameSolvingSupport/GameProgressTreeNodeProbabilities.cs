using ACESim;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ACESimBase.GameSolvingSupport
{
    public partial class GameProgressTree
    {
        /// <summary>
        /// Information on the probability of playing to and from this node, given the exploration scheme in force.
        /// </summary>
        public class GameProgressTreeNodeProbabilities
        {
            /// <summary>
            /// The probability that each player will play to this node given the exploration probabilities for each player. Note that this does not reflect the child proportions that are used.
            /// </summary>
            public double[] PlayToHereProbabilities;
            /// <summary>
            /// The probability that all players combined will play to this node.
            /// </summary>
            public double PlayToHereCombinedProbability;
            /// <summary>
            /// The probability that the player at this node will choose each of the child actions, taking into account the players' exploration values.
            /// </summary>
            public double[] ActionProbabilities;
            /// <summary>
            /// The allocations of observations pursuant to these probabilities. Each allocation must be narrower than the previous, so once we have an allocation that has null for ChildProportions, there will be no further allocations.
            /// </summary>
            public List<GameProgressTreeNodeAllocation> Allocations = new List<GameProgressTreeNodeAllocation>();

            public string ToString(int allocationIndex, bool includeActionProbabilities)
            {
                string s = (Allocations[allocationIndex]?.ToString() ?? "");
                if (includeActionProbabilities)
                    return s + $" action probabilities {ActionProbabilities.ToSignificantFigures_WithSciNotationForVerySmall(4)} reach probabilities {PlayToHereProbabilities.ToSignificantFigures_WithSciNotationForVerySmall(4)}";
                return s;
            }


            public override string ToString()
            {
                StringBuilder s = new StringBuilder();
                s.AppendLine($"Action probabilities {ActionProbabilities.ToSignificantFigures_WithSciNotationForVerySmall(4)}");
                for (int i = 0; i < Allocations.Count(); i++)
                {
                    string s2 = ToString(i, false).Trim();
                    if (s2 != null && s2 != "")
                        s.AppendLine("Allocation " + i + ": " + s2);
                }
                return s.ToString();
            }

            public GameProgressTreeNodeProbabilities(byte allocationIndex, (int, int) observationRange, double[] playToHereProbabilities, double[] actionProbabilities, byte numDecisionIndices)
            {
                var allocation = new GameProgressTreeNodeAllocation()
                {
                    ObservationRange = observationRange,
                    ChildProportions = null,
                    ChildProportionContribution = 0.0
                };
                AddAllocation(allocationIndex, allocation);
                PlayToHereProbabilities = playToHereProbabilities;
                PlayToHereCombinedProbability = playToHereProbabilities.Aggregate((a, x) => a * x);
                ActionProbabilities = actionProbabilities;
            }

            public GameProgressTreeNodeAllocation GetAllocation(int allocationIndex)
            {
                if (Allocations.Count > allocationIndex)
                    return Allocations[allocationIndex];
                return null;
            }

            internal GameProgressTreeNodeAllocation CreateNextAllocation_BasedOnChildren(int allocationIndexToCreate, double[] childReachProbabilities, double? reachProbabilityAtDecision)
            {
                if (Allocations.Count() != allocationIndexToCreate)
                    throw new Exception();
                int previousAllocationIndex = allocationIndexToCreate - 1;
                GameProgressTreeNodeAllocation previousAllocation = Allocations[previousAllocationIndex];

                GameProgressTreeNodeAllocation newAllocation = null;
                double sum = childReachProbabilities.Sum();
                if (sum != 0 || reachProbabilityAtDecision != null)
                {
                    double[] childProportions = null;
                    if (reachProbabilityAtDecision == null)
                        childProportions = childReachProbabilities.Select(x => x / sum).ToArray();

                    newAllocation = new GameProgressTreeNodeAllocation()
                    {
                        ChildProportions = childProportions,
                        ChildProportionContribution = reachProbabilityAtDecision ?? sum,
                        // Note that observation range cannot yet be set, since it depends on cumulative reach probabilities elsewhere in the tree. We will thus set this going downward through the tree.
                    };
                }

                AddAllocation(allocationIndexToCreate, newAllocation);
                return newAllocation;
            }

            internal GameProgressTreeNodeAllocation CreateNextAllocation_BasedOnParent(int allocationIndexToCreate, (int, int) observationRange)
            {
                GameProgressTreeNodeAllocation newAllocation = new GameProgressTreeNodeAllocation()
                {
                    ObservationRange = observationRange
                };
                AddAllocation(allocationIndexToCreate, newAllocation);
                return newAllocation;
            }

            private void AddAllocation(int allocationIndex, GameProgressTreeNodeAllocation allocation)
            {
                while (Allocations.Count() - 1 < allocationIndex)
                    Allocations.Add(null);
                Allocations[allocationIndex] = allocation;
            }
        }
    }
}
