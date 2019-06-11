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

        public NodeActionsHistory InformationSet_Forward(InformationSetNode informationSet, IGameState predecessor, byte predecessorAction, int predecessorDistributorChanceInputs, NodeActionsHistory fromPredecessor)
        {
            // Get a history from the same information set by the same player to here.
            NodeActionsHistory historyToHere = predecessor == null ? fromPredecessor : fromPredecessor.WithAppended(predecessor, predecessorAction, predecessorDistributorChanceInputs); // The information from the predecessor does not include the predecessor itself and the action taken there, so we add it. 
            (InformationSetNode predecessorInformationSetForPlayer, byte actionTakenThere) = historyToHere.GetLastInformationSetByPlayer(informationSet.PlayerIndex); // The predecessor may not be an information set from the same player, so we need to identify the predecessor for the same player as well as the action taken there. 
            informationSet.PredecessorInformationSetForPlayer = predecessorInformationSetForPlayer;
            informationSet.ActionTakenAtPredecessorSet = actionTakenThere;
            NodeActionsHistory fromLastInformationSet = historyToHere.GetIncrementalHistory(informationSet.PlayerIndex);
            if (Trace)
                TabbedText.WriteLine($"From predecessor information set {predecessorInformationSetForPlayer?.InformationSetNodeNumber}: {fromLastInformationSet}");
            // Now, add this history to a list of paths from the predecessor information set.
            ByteList actionsList = historyToHere.GetActionsList(informationSet.PlayerIndex, DistributingChanceActions);
            if (informationSet.PathsFromPredecessor == null)
                informationSet.PathsFromPredecessor = new List<InformationSetNode.PathFromPredecessorInfo>();
            informationSet.PathsFromPredecessor.Add(new InformationSetNode.PathFromPredecessorInfo() { ActionsList = actionsList, IndexInPredecessorsPathsFromPredecessor = (predecessorInformationSetForPlayer?.PathsFromPredecessor.Count() ?? 0) - 1, Path = fromLastInformationSet });
            return historyToHere;
        }

        public NodeActionsHistory ChanceNode_Forward(ChanceNode chanceNode, IGameState predecessor, byte predecessorAction, int predecessorDistributorChanceInputs, NodeActionsHistory fromPredecessor, int distributorChanceInputs)
        {
            if (predecessor == null)
                return fromPredecessor;
            NodeActionsHistory historyToHere = fromPredecessor.WithAppended(predecessor, predecessorAction, predecessorDistributorChanceInputs);
            return historyToHere;
        }

        public List<NodeActionsMultipleHistories> FinalUtilities_TurnAround(FinalUtilitiesNode finalUtilities, IGameState predecessor, byte predecessorAction, int predecessorDistributorChanceInputs, NodeActionsHistory fromPredecessor)
        {
            NodeActionsHistory historyToHere = predecessor == null ? fromPredecessor : fromPredecessor.WithAppended(predecessor, predecessorAction, predecessorDistributorChanceInputs);
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
            var multipleSuccessorsForPlayers = GetMultipleHistoriesByPlayer(fromSuccessors);
            for (byte playerIndex = 0; playerIndex < NumNonChancePlayers; playerIndex++)
            {
                List<NodeActionsMultipleHistories> successorsForPlayer = multipleSuccessorsForPlayers[playerIndex];
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
                        var successorsForThisActionForPlayer = successorsForPlayer[action - 1];
                        var pathsForAction = informationSet.PathsToSuccessors[action - 1];
                        pathsForAction.Add(successorsForThisActionForPlayer);
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
            var multipleHistoriesByPlayer = GetMultipleHistoriesByPlayer(fromSuccessors);
            for (byte playerIndex = 0; playerIndex < NumNonChancePlayers; playerIndex++)
            {
                List<NodeActionsMultipleHistories> successorsForPlayer = multipleHistoriesByPlayer[playerIndex];
                NodeActionsMultipleHistories result = NodeActionsMultipleHistories.FlattenedWithPrepend(successorsForPlayer, chanceNode, distributorChanceInputs, DistributingChanceActions);
                returnList.Add(result);
                if (Trace)
                    TabbedText.WriteLine($"From successor (player {playerIndex}): {returnList.Last()}");
            }
            return returnList;
        }
    }
}
