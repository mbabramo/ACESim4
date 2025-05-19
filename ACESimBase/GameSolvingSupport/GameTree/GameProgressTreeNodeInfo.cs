using ACESim;
using ACESimBase.Util.Reporting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ACESimBase.GameSolvingSupport
{
    public partial class GameProgressTree
    {
        public class GameProgressTreeNodeInfo
        {
            public IDirectGamePlayer DirectGamePlayer;
            public GameProgress GameProgress => DirectGamePlayer.GameProgress;

            public Decision CurrentDecision => DirectGamePlayer.CurrentDecision;
            public byte CurrentDecisionIndex => (byte)DirectGamePlayer.CurrentDecisionIndex;

            public List<(double[] explorationValues, GameProgressTreeNodeProbabilities probabilitiesInfo)> NodeProbabilityInfos = new List<(double[] explorationValues, GameProgressTreeNodeProbabilities probabilitiesInfo)>();
            byte NumDecisionIndices;

            public GameProgressTreeNodeInfo(IDirectGamePlayer directGamePlayer, (int, int) observationRange, double[] explorationValues, byte allocationIndex, double[] playToHereProbabilities, byte numDecisionIndices)
            {
                DirectGamePlayer = directGamePlayer;
                (double[] explorationValues, GameProgressTreeNodeProbabilities) npi = GetNodeProbabilityInfo(allocationIndex, observationRange, explorationValues, playToHereProbabilities, directGamePlayer.GameComplete, numDecisionIndices);
                NodeProbabilityInfos = new List<(double[] explorationValues, GameProgressTreeNodeProbabilities probabilitiesInfo)>()
                {
                    npi
                };
                NumDecisionIndices = numDecisionIndices;
            }

            private (double[] explorationValues, GameProgressTreeNodeProbabilities) GetNodeProbabilityInfo(byte allocationIndex, (int, int) observationRange, double[] explorationValues, double[] playToHereProbabilities, bool gameComplete, byte numDecisionIndices)
            {
                double[] actionProbabilitiesWithExploration = gameComplete ? null : GetActionProbabilitiesWithExploration(explorationValues);
                (double[] explorationValues, GameProgressTreeNodeProbabilities) npi = (explorationValues, new GameProgressTreeNodeProbabilities(allocationIndex, observationRange, playToHereProbabilities, actionProbabilitiesWithExploration, numDecisionIndices)
                {

                });
                return npi;
            }

            private double[] GetActionProbabilitiesWithExploration(double[] explorationValues)
            {
                double[] actionProbabilities = DirectGamePlayer.GetActionProbabilities();
                byte currentPlayer = DirectGamePlayer.CurrentPlayer.PlayerIndex;
                if (explorationValues != null && currentPlayer < explorationValues.Length && explorationValues[currentPlayer] > 0)
                {
                    double explorationValue = explorationValues[currentPlayer];
                    double equalProbabilities = 1.0 / (double)actionProbabilities.Length;
                    return actionProbabilities.Select(x => explorationValue * equalProbabilities + (1.0 - explorationValue) * x).ToArray();
                }

                return actionProbabilities.ToArray();
            }

            public GameProgressTreeNodeProbabilities GetProbabilitiesInfo(double[] explorationValues)
            {
                foreach (var npi in NodeProbabilityInfos)
                {
                    if ((npi.explorationValues == null && explorationValues == null) ||
(npi.explorationValues != null && explorationValues != null && npi.explorationValues.SequenceEqual(explorationValues)))
                        return npi.probabilitiesInfo;
                }
                return null;
            }

            public GameProgressTreeNodeProbabilities GetOrAddProbabilitiesInfo(byte allocationIndex, (int, int) observationRange, double[] explorationValues, double[] playToHereProbabilities, bool gameComplete)
            {
                GameProgressTreeNodeProbabilities value = GetProbabilitiesInfo(explorationValues);
                if (value == null)
                {
                    var result = GetNodeProbabilityInfo(allocationIndex, observationRange, explorationValues, playToHereProbabilities, gameComplete, NumDecisionIndices);
                    NodeProbabilityInfos.Add(result);
                    value = result.Item2;
                }
                return value;
            }

            public override string ToString()
            {
                bool basicTreeOnly = false;
                if (basicTreeOnly)
                    return ToString(null, 0);
                StringBuilder s = new StringBuilder();
                s.AppendLine(GetStatusString());
                foreach (var npi in NodeProbabilityInfos)
                {
                    if (npi.explorationValues != null)
                        s.Append(npi.explorationValues.ToSignificantFigures(4) + ": ");
                    s.AppendLine(npi.probabilitiesInfo.ToString());
                }
                return s.ToString();
            }

            public string ToString(double[] explorationValues, int allocationIndex)
            {
                GameProgressTreeNodeProbabilities value = GetProbabilitiesInfo(explorationValues);
                string statusString = GetStatusString();
                string result = $"{value.ToString(allocationIndex, true)} {statusString}";
                return result;
            }

            private string GetStatusString()
            {
                return GameProgress.GameComplete ? "Result: " + String.Join(",", GameProgress.GetNonChancePlayerUtilities().Select(x => x.ToSignificantFigures(5))) : "Incomplete"; //  $"Next decision: {DirectGamePlayer.CurrentDecision.Name} ({GameProgress.CurrentDecisionIndex})";
            }
        }
    }
}
