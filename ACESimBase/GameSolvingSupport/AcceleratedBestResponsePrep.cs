using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ACESim;
using ACESim.Util;
using ACESimBase.Util;

namespace ACESimBase.GameSolvingSupport
{
    public class AcceleratedBestResponsePrep : ITreeNodeProcessor<NodeActionsHistory, List<NodeActionsMultipleHistories>>
    {
        bool DistributingChanceActions;
        byte NumNonChancePlayers;
        public bool Trace;

        public AcceleratedBestResponsePrep(bool distributingChanceActions, byte numNonChancePlayers, bool trace)
        {
            DistributingChanceActions = distributingChanceActions;
            NumNonChancePlayers = numNonChancePlayers;
            Trace = trace;
        }

        public NodeActionsHistory InformationSet_Forward(InformationSetNode informationSet, IGameState predecessor, byte predecessorAction, NodeActionsHistory fromPredecessor)
        {
            NodeActionsHistory historyToHere = predecessor == null ? fromPredecessor : fromPredecessor.WithAppended(predecessor, predecessorAction);
            (InformationSetNode predecessorInformationSetForPlayer, byte actionTakenThere) = historyToHere.GetLastInformationSetByPlayer(informationSet.PlayerIndex);
            informationSet.PredecessorInformationSetForPlayer = predecessorInformationSetForPlayer;
            informationSet.ActionTakenAtPredecessorSet = actionTakenThere;
            NodeActionsHistory fromLastInformationSet = historyToHere.GetIncrementalHistory(informationSet.PlayerIndex);
            if (Trace)
                TabbedText.WriteLine($"From predecessor information set {predecessorInformationSetForPlayer?.InformationSetNodeNumber}: {fromLastInformationSet}");
            ByteList actionsList = historyToHere.GetActionsList(informationSet.PlayerIndex, DistributingChanceActions);
            if (informationSet.PathsFromPredecessor == null)
                informationSet.PathsFromPredecessor = new List<InformationSetNode.PathFromPredecessorInfo>();
            informationSet.PathsFromPredecessor.Add(new InformationSetNode.PathFromPredecessorInfo() { ActionsList = actionsList, IndexInPredecessorsPathsFromPredecessor = (predecessorInformationSetForPlayer?.PathsFromPredecessor.Count() ?? 0) - 1, Path = fromLastInformationSet });
            return historyToHere;
        }

        public NodeActionsHistory ChanceNode_Forward(ChanceNode chanceNode, IGameState predecessor, byte predecessorAction, NodeActionsHistory fromPredecessor, int distributorChanceInputs)
        {
            if (predecessor == null)
                return fromPredecessor;
            int predecessorDistributorChanceInputs = distributorChanceInputs;
            ChanceNode predecessorChance = predecessor as ChanceNode;
            bool wasDistributorChanceInputDecision = DistributingChanceActions && predecessorChance != null && predecessorChance.Decision.DistributorChanceInputDecision;
            if (wasDistributorChanceInputDecision)
                predecessorDistributorChanceInputs -= predecessorAction * predecessorChance.Decision.DistributorChanceInputDecisionMultiplier;
            NodeActionsHistory historyToHere = fromPredecessor.WithAppended(predecessor, predecessorAction, predecessorDistributorChanceInputs);
            return historyToHere;
        }

        public List<NodeActionsMultipleHistories> FinalUtilities_TurnAround(FinalUtilitiesNode finalUtilities, IGameState predecessor, byte predecessorAction, NodeActionsHistory fromPredecessor)
        {
            NodeActionsHistory historyToHere = predecessor == null ? fromPredecessor : fromPredecessor.WithAppended(predecessor, predecessorAction);
            return Enumerable.Range(0, NumNonChancePlayers).Select(x => new NodeActionsMultipleHistories(finalUtilities)).ToList();
        }

        /// <summary>
        /// Transforms the list of subsequent histories, in which the outer enumerable corresponds to different actions and the inner list corresponds to different players, so that the outer list corresponds to different players and the inner list corresponds to different actions.
        /// </summary>
        /// <param name="fromSuccessors"></param>
        /// <returns></returns>
        private List<List<NodeActionsMultipleHistories>> GetMultipleHistoriesByPlayer(IEnumerable<List<NodeActionsMultipleHistories>> fromSuccessors)
        {
            return fromSuccessors.ToList().Pivot(false);
        }

        public List<NodeActionsMultipleHistories> InformationSet_Backward(InformationSetNode informationSet, IEnumerable<List<NodeActionsMultipleHistories>> fromSuccessors)
        {
            List<NodeActionsMultipleHistories> returnList = new List<NodeActionsMultipleHistories>();
            var invertMultipleHistories = GetMultipleHistoriesByPlayer(fromSuccessors);
            for (byte playerIndex = 0; playerIndex < NumNonChancePlayers; playerIndex++)
            {
                List<NodeActionsMultipleHistories> successorsForPlayer = invertMultipleHistories[playerIndex];
                bool isOwnInformationSet = informationSet.PlayerIndex == playerIndex;
                if (isOwnInformationSet)
                {
                    if (Trace)
                    {
                        TabbedText.WriteLine($"Paths to successor(s): {String.Join(" | ", Enumerable.Range(1, successorsForPlayer.Count).Select(x => $"{x}: {successorsForPlayer[x - 1]}"))}");
                    }
                    // The PathsToSuccessors is a list of lists -- the outer list contains the relevant action, and the inner list contains the successors we may reach from this action, ordered in the order that this information set was visited in the tree walk. The successors must be weighed by the probability that opponents will play to the information set for each of these visits. 
                    if (informationSet.PathsToSuccessors == null)
                    { // initialize to list of empty lists, one for each action
                        informationSet.PathsToSuccessors = new List<List<NodeActionsMultipleHistories>>();
                        for (byte action = 1; action <= informationSet.NumPossibleActions; action++)
                            informationSet.PathsToSuccessors.Add(new List<NodeActionsMultipleHistories>());
                    }
                    for (byte action = 1; action <= informationSet.NumPossibleActions; action++)
                    {
                        var successorThisAction = successorsForPlayer[action - 1];
                        var pathsForAction = informationSet.PathsToSuccessors[action - 1];
                        pathsForAction.Add(successorThisAction);
                    }
                    returnList.Add(new NodeActionsMultipleHistories(informationSet)); // prior information sets of this player will play with this node as a successor, so they don't need to calculate into the entire tree
                }
                else
                {
                    NodeActionsMultipleHistories result = NodeActionsMultipleHistories.FlattenedWithPrepend(successorsForPlayer, informationSet);
                    returnList.Add(result);
                }
                if (Trace)
                    TabbedText.WriteLine($"From successor (player {playerIndex}): {returnList.Last()}");
            }
            return returnList;
        }

        public List<NodeActionsMultipleHistories> ChanceNode_Backward(ChanceNode chanceNode, IEnumerable<List<NodeActionsMultipleHistories>> fromSuccessors, int distributorChanceInputs)
        {
            List<NodeActionsMultipleHistories> returnList = new List<NodeActionsMultipleHistories>();
            var invertMultipleHistories = GetMultipleHistoriesByPlayer(fromSuccessors);
            for (byte playerIndex = 0; playerIndex < NumNonChancePlayers; playerIndex++)
            {
                List<NodeActionsMultipleHistories> successorsForPlayer = invertMultipleHistories[playerIndex];
                NodeActionsMultipleHistories result = NodeActionsMultipleHistories.FlattenedWithPrepend(successorsForPlayer, chanceNode, distributorChanceInputs, DistributingChanceActions);
                returnList.Add(result);
                if (Trace)
                    TabbedText.WriteLine($"From successor (player {playerIndex}): {returnList.Last()}");
            }
            return returnList;
        }
    }
}
