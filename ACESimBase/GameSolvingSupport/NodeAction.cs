using ACESim;
using System;
using System.Collections.Generic;
using System.Text;

namespace ACESimBase.GameSolvingSupport
{
    public class NodeAction
    {
        public IGameState Node;
        public byte ActionAtNode;
        public int DistributorChanceInputs;

        public NodeAction(IGameState node, byte actionAtNode, int distributorChanceInputs = -1)
        {
            Node = node;
            ActionAtNode = actionAtNode;
            DistributorChanceInputs = distributorChanceInputs;
        }

        public NodeAction Clone()
        {
            return new NodeAction(Node, ActionAtNode, DistributorChanceInputs);
        }

        public override bool Equals(object obj)
        {
            NodeAction other = (NodeAction) obj;
            return (Node == other.Node) && (ActionAtNode == other.ActionAtNode) && (DistributorChanceInputs == other.DistributorChanceInputs);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Node, ActionAtNode, DistributorChanceInputs);
        }
    }
}
