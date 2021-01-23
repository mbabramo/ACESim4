using ACESim;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace ACESimBase.GameSolvingSupport
{
    public class EFGFileWriter : ITreeNodeProcessor<bool, bool> // bools are ignored
    {
        public StringBuilder FileText = new StringBuilder();
        int Level = 0;
        bool DistributeChanceDecisions;


        public EFGFileWriter(string gameName, IEnumerable<string> playerNames, bool distributeChanceDecisions)
        {
            FileText.AppendLine($"EFG 2 R \"{gameName}\" {{ {String.Join(" ", playerNames.Select(x => $"\"{x}\""))} }} ");
            DistributeChanceDecisions = distributeChanceDecisions;
        }

        int numDecimalPlaces = 4;
        string FormattedDecimal(decimal d)
        {
            string formatString = String.Format("F{0:D}", numDecimalPlaces);
            return d.ToString(formatString);
        }

        private void AppendTabs()
        {
            for (int i = 0; i < Level; i++)
                FileText.Append("\t");
        }

        private string Quotes(string s) => $"\"{s}\"";

        public bool ChanceNode_Backward(ChanceNode chanceNode, IEnumerable<bool> fromSuccessors, int distributorChanceInputs)
        {
            if (chanceNode.Decision.NumPossibleActions > 1)
                Level--;
            return true;
        }

        public bool ChanceNode_Forward(ChanceNode chanceNode, IGameState predecessor, byte predecessorAction, int predecessorDistributorChanceInputs, bool fromPredecessor, int distributorChanceInputs)
        {
            if (chanceNode.Decision.NumPossibleActions > 1)
            {
                Level++;
                AppendTabs();
                string chanceActionsList;
                if (DistributeChanceDecisions && chanceNode.Decision.DistributedChanceDecision)
                {
                    chanceActionsList = "{ \"1\" 1.000 }";
                }
                else
                    chanceActionsList = GetChanceActionsList(chanceNode.GetActionProbabilitiesDecimal(distributorChanceInputs));
                FileText.AppendLine($"c \"C:{chanceNode.Decision.Abbreviation};Node{chanceNode.ChanceNodeNumber + 1}\" {chanceNode.ChanceNodeNumber + 1} \"\" {chanceActionsList} 0");
                // TabbedText.WriteLine($"{chanceNode.Decision} {chanceNode.ChanceNodeNumber} distributor chance inputs {distributorChanceInputs} probabilities {String.Join(",", chanceNode.GetActionProbabilitiesDecimal(distributorChanceInputs))}");
            }
            return true; // ignored
        }

        private string GetChanceActionsList(IEnumerable<decimal> values)
        {
            return $"{{ {String.Join(" ", values.Select((item, index) => $"{Quotes((index + 1).ToString())} {FormattedDecimal(item)}"))} }}";
        }

        public bool FinalUtilities_TurnAround(FinalUtilitiesNode finalUtilities, IGameState predecessor, byte predecessorAction, int predecessorDistributorChanceInputs, bool fromPredecessor)
        {
            Level++;
            AppendTabs();
            FileText.AppendLine($"t \"\" {finalUtilities.FinalUtilitiesNodeNumber + 1} \"Outcome {finalUtilities.FinalUtilitiesNodeNumber + 1}\" {GetOutcomesList(finalUtilities.Utilities)}");
            Level--;
            return true;
        }

        private string GetOutcomesList(IEnumerable<double> values)
        {
            return "{ " + String.Join(" ", values.Select(x => FormattedDecimal((decimal) x))) + " }";
        }

        public bool InformationSet_Backward(InformationSetNode informationSet, IEnumerable<bool> fromSuccessors)
        {
            Level--;
            return true;
        }

        public bool InformationSet_Forward(InformationSetNode informationSet, IGameState predecessor, byte predecessorAction, int predecessorDistributorChanceInputs, bool fromPredecessor)
        {
            Level++;
            AppendTabs();
            string name = $"\"P{informationSet.PlayerIndex + 1}S{informationSet.InformationSetNodeNumber + 1}({informationSet.Decision.Abbreviation}):{informationSet.InformationSetContentsString}\"";
            FileText.AppendLine($"p {name} {informationSet.PlayerIndex + 1} {informationSet.InformationSetNodeNumber + 1} {name} {GetActionNames(informationSet.NumPossibleActions)} 0");
            return true;
        }

        public string GetActionNames(int numActions) =>  "{ " + String.Join(" ", Enumerable.Range(1, numActions).Select(x => $"\"{x}\"")) + "} ";
    }
}
