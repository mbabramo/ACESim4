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
        public List<string> ActionNames;
        public List<EFGFileGameMove> InformationSetContents = new List<EFGFileGameMove>();
        public HashSet<int> PlayersToInform = new HashSet<int>();
        public bool DeferNotificationOfPlayers = false;
        public HashSet<EFGFileInformationSetNode> NodesWithInformationSet = new HashSet<EFGFileInformationSetNode>();
        internal bool CanTerminateGame;
        internal byte DecisionByteCode;

        public (bool isApparentlyCutByEarlierInformationSet, bool followsButIsNotCutByEarlierInformationSet) RelationshipToPotentiallyEarlierInformationSet(int earlierInformationSetNumber)
        {
            // If there is exactly one move from the earlier information set to any nodes in this information set, then that may indicate
            // (a) that there is only one move at that information set; (b) that other moves don't lead to another decision of this sort for
            // the player at this information set; or (c) that the action at the earlier information set becomes part of the information
            // known to the player at this information set and thus that the information set is "cut."
            int numMoves = MovesFromEarlierInformationSetToNodesInThisInformationSet(earlierInformationSetNumber).Count();
            return (numMoves == 1, numMoves > 1);
        }

        private List<int> MovesFromEarlierInformationSetToNodesInThisInformationSet(int earlierInformationSetNumber)
        {
            HashSet<int> h = new HashSet<int>();
            foreach (var node in NodesWithInformationSet)
            {
                if (node.PreviousMoves.Any(x => x.informationSetNumber == earlierInformationSetNumber))
                    h.Add(node.PreviousMoves.First(x => x.informationSetNumber == earlierInformationSetNumber).oneBasedAction);
            }
            return h.OrderBy(x => x).ToList();
        }

        public List<int> ImmediatelyPrecedingInformationSets()
        {
            HashSet<int> precedingSets = new HashSet<int>();
            foreach (var node in NodesWithInformationSet)
                precedingSets.Add(node.ImmediatelyPrecedingMove.informationSetNumber);
            return precedingSets.OrderBy(x => x).ToList();
        }

        public virtual int NumActions => ActionNames.Count();

        public bool AlwaysTerminatesGame { get; internal set; }
    }
}
