using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESimBase.GameSolvingSupport.GameTree
{
    public class TreeNodeCountingProcessor
        : ITreeNodeProcessor<object, TreeNodeCountingProcessor.NodeCounts>
    {
        /// <summary>
        /// A struct to hold counts for Chance, InfoSet, and Final nodes.
        /// We’ll accumulate these and return them up the tree.
        /// </summary>
        public struct NodeCounts
        {
            public int ChanceNodes;
            public int InfoSetNodes;
            public int FinalNodes;

            public void Add(in NodeCounts other)
            {
                ChanceNodes += other.ChanceNodes;
                InfoSetNodes += other.InfoSetNodes;
                FinalNodes += other.FinalNodes;
            }
        }

        // --------------------------------------------------
        //  FINAL UTILITIES node
        // --------------------------------------------------
        public NodeCounts FinalUtilities_TurnAround(
            FinalUtilitiesNode finalUtilities,
            IGameState predecessor,
            byte predecessorAction,
            int predecessorDistributorChanceInputs,
            object fromPredecessor)
        {
            // Leaf node => return (0, 0, 1)
            NodeCounts counts = default;
            counts.FinalNodes++;
            return counts;
        }

        // --------------------------------------------------
        //  CHANCE node
        // --------------------------------------------------
        public object ChanceNode_Forward(
            ChanceNode chanceNode,
            IGameState predecessor,
            byte predecessorAction,
            int predecessorDistributorChanceInputs,
            object fromPredecessor,
            int distributorChanceInputs)
        {
            // We don’t accumulate anything going forward
            return null;
        }

        public NodeCounts ChanceNode_Backward(
            ChanceNode chanceNode,
            IEnumerable<NodeCounts> fromSuccessors,
            int distributorChanceInputs)
        {
            // Sum all children’s NodeCounts
            NodeCounts aggregated = default;
            foreach (var childCounts in fromSuccessors)
                aggregated.Add(childCounts);

            // Count this Chance node
            aggregated.ChanceNodes++;

            // Return the total to the parent
            return aggregated;
        }

        // --------------------------------------------------
        //  INFORMATION‐SET node
        // --------------------------------------------------
        public object InformationSet_Forward(
            InformationSetNode informationSet,
            IGameState predecessor,
            byte predecessorAction,
            int predecessorDistributorChanceInputs,
            object fromPredecessor)
        {
            // Nothing to do on the way down
            return null;
        }

        public NodeCounts InformationSet_Backward(
            InformationSetNode informationSet,
            IEnumerable<NodeCounts> fromSuccessors)
        {
            // Sum child NodeCounts
            NodeCounts aggregated = default;
            foreach (var childCounts in fromSuccessors)
                aggregated.Add(childCounts);

            // Count this InfoSet node
            aggregated.InfoSetNodes++;
            return aggregated;
        }
    }


}
