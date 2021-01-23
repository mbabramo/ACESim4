using JetBrains.Annotations;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESimBase.Games.EFGFileGame
{


    public class EFGFileNode
    {
        public string NodeName;
        public EFGFileNode[] ChildNodes;
        public virtual EFGFileInformationSet GetInformationSet() => null;
        public virtual int NumChildNodes => 0;

        /// <summary>
        /// Prior moves in the game, whether or not the player knows about them.
        /// </summary>
        public List<EFGFileGameMove> PreviousMoves = new List<EFGFileGameMove>();
        public EFGFileGameMove ImmediatelyPrecedingMove => PreviousMoves.LastOrDefault();

        /// <summary>
        /// Identify the relevant place of the two nodes in the tree.
        /// </summary>
        /// <param name="other"></param>
        /// <returns></returns>
        public int? RelativeOrder(EFGFileNode other)
        {
            int previousMovesCount = PreviousMoves.Count();
            int otherPreviousMovesCount = other.PreviousMoves.Count();
            if (previousMovesCount == otherPreviousMovesCount)
            {
                if (PreviousMoves.SequenceEqual(other.PreviousMoves))
                    return 0;
                else
                    return null;
            }
            else if (previousMovesCount > otherPreviousMovesCount)
            {
                if (PreviousMoves.Take(otherPreviousMovesCount).SequenceEqual(other.PreviousMoves))
                    return previousMovesCount - otherPreviousMovesCount;
                else
                    return null;
            }
            else
            {
                if (other.PreviousMoves.Take(previousMovesCount).SequenceEqual(PreviousMoves))
                    return previousMovesCount - otherPreviousMovesCount; // negative number indicates that the other one is longer
                else
                    return null;
            }
        }

        public EFGFileNode CreateTree(List<EFGFileNode> allNodes, ref int indexInAllNodes)
        {
            if (NumChildNodes > 0)
                ChildNodes = new EFGFileNode[NumChildNodes];
            var informationSet = GetInformationSet();
            if (informationSet != null)
                informationSet.NodesWithInformationSet.Add((EFGFileInformationSetNode)this);
            for (int childIndex = 0; childIndex < NumChildNodes; childIndex++)
            {
                indexInAllNodes++;
                EFGFileNode childNode = allNodes[indexInAllNodes];
                childNode.PreviousMoves.Add(new EFGFileGameMove(informationSet.InformationSetNumber, informationSet.PlayerNumber, childIndex + 1));
                ChildNodes[childIndex] = childNode.CreateTree(allNodes, ref indexInAllNodes);
            }
            informationSet.CanTerminateGame = ChildNodes.Any(x => x is EFGFileOutcomeNode);
            informationSet.AlwaysTerminatesGame = ChildNodes.All(x => x is EFGFileOutcomeNode);
            return this;
        }

        public IEnumerable<EFGFileNode> EnumerateNodes(bool includeThis, bool topDown)
        {
            if (includeThis && topDown)
                yield return this;
            if (ChildNodes != null)
                foreach (var childNode in ChildNodes)
                    foreach (var descendant in childNode.EnumerateNodes(true, topDown))
                        yield return descendant;
            if (includeThis && !topDown)
                yield return this;
        }

        public IEnumerable<EFGFileInformationSetNode> EnumerateInformationSetNodes(bool includeThis = false, bool topDown = true) => EnumerateNodes(includeThis, topDown).Where(x => x is EFGFileInformationSetNode).Select(x => (EFGFileInformationSetNode)x);

        public IEnumerable<EFGFileInformationSet> EnumerateInformationSets(bool includeThis = false, bool topDown = true) => EnumerateNodes(includeThis, topDown).Select(x => x.GetInformationSet()).Where(x => x != null);

        public void AddActionsToDescendants(List<EFGFileInformationSet> informationSets)
        {
            // We need to figure out which information sets below each node should receive information about the action taken at the node.
            // If the same information set appears below the node for more than one action at the node, that must mean that the node doesn't
            // receive the information -- which means that the player doesn't receive the information. 
            // There is, however, a complication. Sometimes, information might be provided to another player but deferred. This is used to
            // model simultaneous decisionmaking. For example, a plaintiff and defendant might each simultaneously submit offers. The plaintiff
            // goes first, which means that the defendant will not immediately know the plaintiff's decision when the defendant makes its decision.
            // In principle, we could imagine deferring decisions longer, but ACESim doesn't support that yet. Indeed, ACESim doesn't support specifying
            // exactly which later information sets should get the action taken at an earlier information set. It just has a simple setting for deferring
            // notification of players by one turn.

            List<int> playerNumbers = informationSets.Select(x => x.PlayerNumber).Distinct().OrderBy(x => x).ToList();
            foreach (int playerNumberForLaterInformationSet in playerNumbers)
            {
                foreach (var informationSet1 in informationSets)
                {
                    bool earlierCutsThroughSomeLaterInformationSet = false;
                    bool earlierPrecedesButDoesNotCutThroughSomeLaterInformationSet = false;
                    foreach (var informationSet2 in informationSets.Where(x => x.PlayerNumber == playerNumberForLaterInformationSet))
                    {
                        var (isCutByEarlierInformationSet, followsButIsNotCutByEarlierInformationSet) = informationSet2.RelationshipToPotentiallyEarlierInformationSet(informationSet1.InformationSetNumber);
                        if (isCutByEarlierInformationSet)
                            earlierCutsThroughSomeLaterInformationSet = true;
                        if (followsButIsNotCutByEarlierInformationSet)
                            earlierPrecedesButDoesNotCutThroughSomeLaterInformationSet = true;
                    }
                    if (earlierCutsThroughSomeLaterInformationSet)
                        informationSet1.PlayersToInform.Add(playerNumberForLaterInformationSet);
                    if (earlierPrecedesButDoesNotCutThroughSomeLaterInformationSet)
                        informationSet1.DeferNotificationOfPlayers = true;
                }
            }
            foreach (var node in EnumerateInformationSetNodes(true, true))
            {
                var informationSet = node.GetInformationSet();
                if (informationSet.PlayersToInform.Any())
                {
                    for (int i = 0; i < ChildNodes.Length; i++)
                    {
                        int action = i + 1;
                        var information = new EFGFileGameMove(informationSet.InformationSetNumber, informationSet.PlayerNumber, action);
                        var informationSetsForChild = ChildNodes[i].EnumerateInformationSets(false, true);
                        foreach (var informationSetForChild in informationSetsForChild)
                        {
                            if (informationSet.PlayersToInform.Contains(informationSetForChild.InformationSetNumber))
                            {
                                informationSetForChild.InformationSetContents.Add(information);
                            }
                        }
                    }
                }
            }
        }

    }
}
