using ACESim.Util;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ACESimBase.GameSolvingSupport
{
    public class NodeActionsMultipleHistories
    {
        public List<NodeActionsHistory> Histories;

        public override bool Equals(object obj)
        {
            NodeActionsMultipleHistories other = (NodeActionsMultipleHistories)obj;
            return other.Histories.SequenceEqual(Histories);
        }

        public override int GetHashCode()
        {
            return Histories.GetSequenceHashCode();
        }
    }
}
