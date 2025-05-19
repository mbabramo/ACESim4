using ACESimBase.Util.Reporting;

namespace ACESimBase.GameSolvingSupport
{
    public partial class GameProgressTree
    {
        /// <summary>
        /// Information on a node in the GameProgressTree, pursuant to particular criteria for allocating observations.  Observations may be allocated (1) pursuant to particular exploration probabilities for each player; OR (2) pursuant to specified allocation proportions. After we initially explore using a set of exploration probabilities (which may be zero, if the players are playing on-policy), we then calculate the next criterion by aggregating up the tree the cumulative reach probability pursuant to the original criterion of all game progresses that reach the first decision not reached in all game paths.
        /// </summary>
        public class GameProgressTreeNodeAllocation
        {
            public (int, int) ObservationRange;
            public int NumObservations => ObservationRange.Item2 - ObservationRange.Item1 + 1;
            /// <summary>
            /// Where encompassing all game progresses (regardless of the decision being played up to), the action probabilities at this game point; these will be set by the algorithm allocating observations. When encompassing only game progresses that reach at least a particular decision index, before the allocation algorithm begins, these are set to reflect the cumulative reach probability of game progresses lower in the tree reach the necessary point, and then when the algorithm elaborates the tree, this will reflect the action probabilities.
            /// </summary>
            public double[] ChildProportions;
            /// <summary>
            /// This is stored temporarily to determine the child proportions of the parent node when
            /// creating a new allocation. For nodes at the decision index being used to generate the
            /// new allocation, the ChildProportionContribution will be equal to the PlayToHereCombinedProbability.
            /// For nodes earlier in the tree, the ChildProportionContribution will be the sum of the child
            /// ChildProportionContributions. Meanwhile, ChildProportions will be set to the relative 
            /// weight of the children's ChildProportionContribution values.
            /// </summary>
            public double ChildProportionContribution;

            public override string ToString() => $"{ObservationRange}: {(ChildProportions == null ? "" : $"ChildProportions {ChildProportions.ToSignificantFigures_WithSciNotationForVerySmall(4)}")} {(ChildProportionContribution == 0 ? "" : $"ChildProportionContribution {ChildProportionContribution.ToSignificantFigures_WithSciNotationForVerySmall(4)}")}";
        }
    }
}
