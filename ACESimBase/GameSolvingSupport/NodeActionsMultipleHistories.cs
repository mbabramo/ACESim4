using ACESim;
using ACESim.Util;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ACESimBase.GameSolvingSupport
{
    [Serializable]
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

        public (double bestResponseValue, double utilityValue, FloatSet customResult) GetProbabilityAdjustedValueOfPaths(byte playerIndex, bool useCurrentStrategyForPathProbabilities)
        {
            double cumulativeBestResponseValue = 0, cumulativeUtilityValue = 0;
            FloatSet cumulativeCustomResult = new FloatSet();
            foreach (var pathToSuccessorForAction in Histories)
            {
                var (bestResponseValue, utilityValue, customResult) = pathToSuccessorForAction.GetProbabilityAdjustedUtilityOfPath(playerIndex, useCurrentStrategyForPathProbabilities);
                if (double.IsNaN(utilityValue))
                    throw new Exception();
                cumulativeBestResponseValue += bestResponseValue;
                cumulativeUtilityValue += utilityValue;
                cumulativeCustomResult = cumulativeCustomResult.Plus(customResult);
            }
            return (cumulativeBestResponseValue, cumulativeUtilityValue, cumulativeCustomResult);
        }
    }
}
