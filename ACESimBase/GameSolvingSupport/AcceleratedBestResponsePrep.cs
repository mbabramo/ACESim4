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
            (InformationSetNode lastInformationSetForPlayer, byte actionTakenThere) = historyToHere.GetLastInformationSetByPlayer(informationSet.PlayerIndex);
            informationSet.PredecessorInformationSetForPlayer = lastInformationSetForPlayer;
            informationSet.ActionTakenAtPredecessorSet = actionTakenThere;
            NodeActionsHistory fromLastInformationSet = historyToHere.GetIncrementalHistory(informationSet.PlayerIndex);
            if (Trace)
                TabbedText.WriteLine($"From predecessor information set {lastInformationSetForPlayer?.InformationSetNodeNumber}: {fromLastInformationSet}");
            ByteList actionsList = historyToHere.GetActionsList(informationSet.PlayerIndex, DistributingChanceActions);
            if (informationSet.PathsFromPredecessor == null)
                informationSet.PathsFromPredecessor = new Dictionary<ByteList, NodeActionsHistory>();
            if (informationSet.PathsFromPredecessor.ContainsKey(actionsList))
                throw new Exception("There should be a unique path from the last information set.");
            informationSet.PathsFromPredecessor[actionsList] = fromLastInformationSet;
            return historyToHere;
        }

        public NodeActionsHistory ChanceNode_Forward(ChanceNode chanceNode, IGameState predecessor, byte predecessorAction, NodeActionsHistory fromPredecessor, int distributorChanceInputs)
        {
            NodeActionsHistory historyToHere = predecessor == null ? fromPredecessor : fromPredecessor.WithAppended(predecessor, predecessorAction, distributorChanceInputs);
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
                        TabbedText.WriteLine($"Paths to successor: {String.Join(" | ", Enumerable.Range(1, successorsForPlayer.Count).Select(x => $"{x}: {successorsForPlayer[x - 1]}"))}");
                    }
                    informationSet.PathsToSuccessors = successorsForPlayer;
                    returnList.Add(new NodeActionsMultipleHistories(informationSet));
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
                NodeActionsMultipleHistories result = NodeActionsMultipleHistories.FlattenedWithPrepend(successorsForPlayer, chanceNode, distributorChanceInputs);
                returnList.Add(result);
                if (Trace)
                    TabbedText.WriteLine($"From successor (player {playerIndex}): {returnList.Last()}");
            }
            return returnList;
        }
    }
}
