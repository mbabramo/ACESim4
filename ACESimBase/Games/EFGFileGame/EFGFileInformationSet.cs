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
        public HashSet<EFGFileInformationSetNode> NodesWithInformationSet;
        internal bool CanTerminateGame;
        internal byte DecisionByteCode;

        public (bool isCutByEarlierInformationSet, bool followsButIsNotCutByEarlierInformationSet) RelationshipToPotentiallyEarlierInformationSet(int earlierInformationSetNumber)
        {
            int numMoves = MovesMadeAtEarlierInformationSet(earlierInformationSetNumber).Count();
            return (numMoves > 1, numMoves == 1);
        }

        private List<int> MovesMadeAtEarlierInformationSet(int earlierInformationSetNumber)
        {
            HashSet<int> h = new HashSet<int>();
            foreach (var node in NodesWithInformationSet)
            {
                if (node.PreviousMoves.Any(x => x.informationSetNumber == earlierInformationSetNumber))
                    h.Add(node.PreviousMoves.First(x => x.informationSetNumber == earlierInformationSetNumber).informationSetNumber);
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
