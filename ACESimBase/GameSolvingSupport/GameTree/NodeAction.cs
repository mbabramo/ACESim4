using System;
using System.Collections.Generic;
using System.Text;

namespace ACESimBase.GameSolvingSupport.GameTree
{
    [Serializable]
    public class NodeAction
    {
        public IGameState Node;
        public byte ActionAtNode;

        public NodeAction(IGameState node, byte actionAtNode)
        {
            Node = node;
            ActionAtNode = actionAtNode;
        }

        public override string ToString()
        {
            return Node switch
            {
                null => "null",
                _ => $"{Node.GetGameStateType()} {Node.GetInformationSetNodeNumber()}: {ActionAtNode}"
            };
        }

        public NodeAction Clone()
        {
            return new NodeAction(Node, ActionAtNode);
        }

        public override bool Equals(object obj)
        {
            NodeAction other = (NodeAction)obj;
            return Node == other.Node && ActionAtNode == other.ActionAtNode;
        }

        public override int GetHashCode()
        {
            return (Node, ActionAtNode).GetHashCode();
        }
    }
}
