using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESimBase.Games.EFGFileGame
{


    public class EFGFileInformationSet
    {
        public string InformationSetName;
        public int InformationSetNumber;
        public int PlayerNumber;
        public EFGFileInformationSetID InformationSetID => new EFGFileInformationSetID(InformationSetNumber, PlayerNumber);
        public List<string> ActionNames;
        public List<EFGFileGameMove> InformationSetContents = new List<EFGFileGameMove>();
        public HashSet<int> PlayersToInform = new HashSet<int>();
        public HashSet<int> PlayersToDeferNotificationFor = new HashSet<int>();
        public bool DeferNotificationOfPlayers = false;
        public HashSet<EFGFileInformationSetNode> NodesWithInformationSet = new HashSet<EFGFileInformationSetNode>();
        internal bool CanTerminateGame;
        internal byte DecisionByteCode;

        public (bool isApparentlyCutByEarlierInformationSet, bool followsButIsNotCutByEarlierInformationSet) RelationshipToPotentiallyEarlierInformationSet(EFGFileInformationSetID earlierInformationSetID)
        {
            // If there is exactly one move from the earlier information set to any nodes in this information set, then that may indicate
            // (a) that there is only one move at that information set; (b) that other moves don't lead to another decision of this sort for
            // the player at this information set; or (c) that the action at the earlier information set becomes part of the information
            // known to the player at this information set and thus that the information set is "cut."
            int numMoves = MovesFromEarlierInformationSetToNodesInThisInformationSet(earlierInformationSetID).Count();
            return (numMoves == 1, numMoves > 1);
        }

        private List<int> MovesFromEarlierInformationSetToNodesInThisInformationSet(EFGFileInformationSetID earlierInformationSetID)
        {
            HashSet<int> h = new HashSet<int>();
            foreach (var node in NodesWithInformationSet)
            {
                if (node.PreviousMoves.Any(x => x.InformationSetID == earlierInformationSetID))
                    h.Add(node.PreviousMoves.First(x => x.InformationSetID == earlierInformationSetID).oneBasedAction);
            }
            return h.OrderBy(x => x).ToList();
        }

        public List<EFGFileInformationSetID> ImmediatelyPrecedingInformationSets()
        {
            HashSet<EFGFileInformationSetID> precedingSets = new HashSet<EFGFileInformationSetID>();
            foreach (var node in NodesWithInformationSet)
                if (node.ImmediatelyPrecedingMove != null)
                    precedingSets.Add(node.ImmediatelyPrecedingMove.InformationSetID);
            return precedingSets.OrderBy(x => x.playerNumber).ThenBy(x => x.informationSetNumber).ToList();
        }

        public List<EFGFileInformationSetID> ImmediatelySucceedingInformationSets()
        {
            HashSet<EFGFileInformationSetID> succeedingSets = new HashSet<EFGFileInformationSetID>();
            foreach (var node in NodesWithInformationSet)
                if (node.ChildNodes != null && node.ChildNodes.Any())
                    foreach (var childNode in node.ChildNodes)
                        if (childNode.GetInformationSet() is not null)
                            succeedingSets.Add(childNode.GetInformationSet().InformationSetID);
            return succeedingSets.OrderBy(x => x.playerNumber).ThenBy(x => x.informationSetNumber).ToList();
        }

        public List<EFGFileInformationSetID> TwoMoveLaterInformationSets()
        {
            HashSet<EFGFileInformationSetID> succeedingSets = new HashSet<EFGFileInformationSetID>();
            foreach (var node in NodesWithInformationSet)
                if (node.ChildNodes != null && node.ChildNodes.Any())
                    foreach (var childNode in node.ChildNodes)
                    {
                        EFGFileInformationSet childInformationSet = childNode.GetInformationSet();
                        if (childInformationSet != null)
                            foreach (var grandchildInformationSet in childInformationSet.ImmediatelySucceedingInformationSets())
                                succeedingSets.Add(grandchildInformationSet);
                    }
            return succeedingSets.OrderBy(x => x.playerNumber).ThenBy(x => x.informationSetNumber).ToList();
        }

        public (List<EFGFileInformationSetID> children, List<EFGFileInformationSetID> grandchildren) ChildrenAndGrandChildrenInformationSets()
        {
            var children = ImmediatelySucceedingInformationSets();
            var grandchildren = TwoMoveLaterInformationSets().Except(children).ToList();
            return (children, grandchildren);
        }



        public virtual int NumActions => ActionNames.Count();

        public bool AlwaysTerminatesGame { get; internal set; }
    }
}
