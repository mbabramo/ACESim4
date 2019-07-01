﻿using ACESim;
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

        public NodeActionsMultipleHistories(IGameState successor)
        {
            Histories = new List<NodeActionsHistory>()
            {
                new NodeActionsHistory() { Successor = successor }
            };
        }

        public override string ToString()
        {
            return String.Join("; ", Histories);
        }

        public NodeActionsMultipleHistories(List<NodeActionsHistory> histories)
        {
            Histories = histories;
        }

        public NodeActionsMultipleHistories(IEnumerable<NodeActionsHistory> histories)
        {
            Histories = histories.ToList();
        }

        /// <summary>
        /// Flattens sets of histories to combine into a single NodeActionsMultipleHistories, consisting of each of the histories contained within, optionally prepended by a specified node and a corresponding action value. 
        /// </summary>
        /// <returns></returns>
        public static NodeActionsMultipleHistories FlattenedWithPrepend(List<NodeActionsMultipleHistories> historiesToCombine, IGameState node, int distributorChanceInputs = -1, bool omitDistributedChanceDecisions = false)
        {
            List<NodeActionsHistory> histories = new List<NodeActionsHistory>();
            for (byte action = 1; action <= historiesToCombine.Count(); action++)
            {
                var historyToCombine = historiesToCombine[action - 1];
                foreach (var history in historyToCombine.Histories)
                {
                    var withPrepend = node == null ? history.DeepClone() : history.WithPrepended(node, action, distributorChanceInputs, omitDistributedChanceDecisions);
                    histories.Add(withPrepend);
                }
            }
            return new NodeActionsMultipleHistories(histories);
        }

        public override bool Equals(object obj)
        {
            NodeActionsMultipleHistories other = (NodeActionsMultipleHistories)obj;
            return other.Histories.SequenceEqual(Histories);
        }

        public override int GetHashCode()
        {
            return Histories.GetSequenceHashCode();
        }

        public (double bestResponseValue, double averageStrategyValue) GetProbabilityAdjustedValueOfPaths(byte playerIndex)
        {
            double cumulativeBestResponseValue = 0, cumulativeAverageStrategyValue = 0;
            foreach (var pathToSuccessorForAction in Histories)
            {
                var values = pathToSuccessorForAction.GetProbabilityAdjustedUtilityOfPath(playerIndex);
                cumulativeBestResponseValue += values.bestResponseValue;
                cumulativeAverageStrategyValue += values.averageStrategyValue;
            }
            return (cumulativeBestResponseValue, cumulativeAverageStrategyValue);
        }
    }
}
