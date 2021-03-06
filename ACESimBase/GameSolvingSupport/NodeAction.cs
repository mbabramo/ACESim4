﻿using ACESim;
using System;
using System.Collections.Generic;
using System.Text;

namespace ACESimBase.GameSolvingSupport
{
    [Serializable]
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

        public override string ToString()
        {
            return Node switch
            {
                null => "null",
                _ => $"{Node.GetGameStateType()} {Node.GetInformationSetNodeNumber()}: {ActionAtNode}{(DistributorChanceInputs != -1 && DistributorChanceInputs != 0 && Node is ChanceNodeUnequalProbabilities u && u.Decision.DistributorChanceDecision ? $"({DistributorChanceInputs})" : "")}"
            };
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
            return (Node, ActionAtNode, DistributorChanceInputs).GetHashCode();
        }
    }
}
