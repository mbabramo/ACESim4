using ACESim;
using ACESimBase.Util;
using JetBrains.Annotations;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace ACESimBase.Games.EFGFileGame
{
    /// <summary>
    ///  A class for reading information from EFG files.
    /// </summary>
    public class EFGFileReader
    {
        public EFGFileNode EFGTreeRoot;
        public List<EFGFileInformationSet> InformationSets = new List<EFGFileInformationSet>();
        List<string> PlayerNames;
        public List<PlayerInfo> PlayerInfo = new List<PlayerInfo>();
        public List<int> NumDecisionsPerPlayer;
        public List<Decision> Decisions;

        public EFGFileReader()
        {

        }

        public EFGFileReader(string sourcefileText)
        {
            EFGTreeRoot = GetEFGFileNodesTree(sourcefileText);
            MoveChanceToEnd();
            DeterminePlayerDecisions();
        }

        public bool IsComplete(IEnumerable<int> actions)
        {
            return GetOutcomes(actions) != null;
        }

        public byte NextPlayer(IEnumerable<int> actions)
        {
            EFGFileNode current = GetEFGFileNode(actions);
            if (current is EFGFileInformationSetNode informationSetNode)
                return (byte) informationSetNode.InformationSet.PlayerNumber;
            throw new Exception("Game is complete.");
        }

        public double[] GetOutcomes(IEnumerable<int> actions)
        {
            EFGFileNode current = GetEFGFileNode(actions);
            if (current is EFGFileOutcomeNode outcome)
                return outcome.Values;
            return null;
        }

        public EFGFileNode GetEFGFileNode(IEnumerable<int> actions)
        {
            var current = EFGTreeRoot;
            foreach (var action in actions)
                current = current.ChildNodes[action - 1];
            return current;
        }

        public void MoveChanceToEnd()
        {
            // In efg files, the chance player will usually be player 0. But we need to switch chance player (or in theory players) to the end).
            var chancePlayers = InformationSets.Where(x => x is EFGFileChanceInformationSet).Select(x => x.PlayerNumber).Distinct().OrderBy(x => x).ToList();
            var nonchancePlayers = InformationSets.Where(x => x is not EFGFileChanceInformationSet).Select(x => x.PlayerNumber).Distinct().OrderBy(x => x).ToList();
            var allPlayers = nonchancePlayers.ToList();
            allPlayers.AddRange(chancePlayers);
            Dictionary<int, int> map = new Dictionary<int, int>();
            for (int i = 0; i < allPlayers.Count(); i++)
                map[i] = allPlayers[i];
            foreach (var informationSet in InformationSets)
            {
                informationSet.PlayerNumber = map[informationSet.PlayerNumber];
                informationSet.PlayersToInform = new HashSet<int>(informationSet.PlayersToInform.Select(x => map[x]));
            }
            foreach (var efgFileNode in EFGTreeRoot.EnumerateInformationSetNodes(true, true))
            {
                efgFileNode.PreviousMoves = efgFileNode.PreviousMoves.Select(x => new EFGFileGameMove(x.informationSetNumber, map[x.playerNumber], x.oneBasedAction)).ToList();
            }
        }

        public void DeterminePlayerDecisions()
        {
            List<int> players = InformationSets.Select(x => x.PlayerNumber).Distinct().OrderBy(x => x).ToList();
            PlayerInfo = players.Select(x => new ACESim.PlayerInfo(PlayerNames[x], (byte) x, PlayerNames[x] != "Chance", true)).ToList();
            // Now, we need to create the decision list. We can do a topological sort of information sets -- that is, get a sorting based on knowledge like information set x precedes information set y. 
            // Then, we can look to see whether there is a consecutive set of information sets with the same player and the same inputs and outputs. That constitutes a decision. 
            List<TopologicalSorter.Field<int>> constraints = new List<TopologicalSorter.Field<int>>();
            foreach (var informationSet in InformationSets)
            {
                var dependencies = informationSet.ImmediatelyPrecedingInformationSets();
                if (!dependencies.Any())
                    dependencies = null;
                constraints.Add(new TopologicalSorter.Field<int>() { Item = informationSet.InformationSetNumber, EarlierItems = dependencies.ToArray() });
            }
            int[] orderedInformationSetNumbers = TopologicalSorter.GetTopologicalSortItems(constraints);
            Decisions = new List<Decision>();
            Decision lastDecisionAdded = null;
            int lastNumInputs = -1;
            byte decisionByteCode = 0;
            foreach (int informationSetNumber in orderedInformationSetNumbers)
            {
                var informationSet = InformationSets.Single(x => x.InformationSetNumber == informationSetNumber);
                int numInputs = informationSet.InformationSetContents.Count();
                int numOutputs = informationSet.NumActions;
                if (lastDecisionAdded == null || lastNumInputs != numInputs || lastDecisionAdded.NumPossibleActions != numOutputs)
                {
                    lastDecisionAdded = new Decision()
                    {
                        Abbreviation = "D" + decisionByteCode.ToString(),
                        Name = "D" + decisionByteCode.ToString(),
                        IsChance = informationSet is EFGFileChanceInformationSet,
                        AlwaysTerminatesGame = informationSet.AlwaysTerminatesGame,
                        CanTerminateGame = informationSet.CanTerminateGame,
                        PlayerIndex = (byte)informationSet.PlayerNumber,
                        PlayersToInform = informationSet.PlayersToInform.OrderBy(x => x).Select(x => (byte)x).ToArray(),
                        DeferNotificationOfPlayers = informationSet.DeferNotificationOfPlayers,
                        IsReversible = true,
                        NumPossibleActions = (byte)informationSet.NumActions,
                        UnevenChanceActions = informationSet is EFGFileChanceInformationSet,
                        IsAlwaysPlayersLastDecision = false, // may not be so, but it doesn't matter
                        DecisionTypeCode = decisionByteCode.ToString(),
                        DecisionByteCode = decisionByteCode,
                        InformationSetAbbreviations = informationSet.InformationSetContents.Select(x => $"P{x.playerNumber}I{x.informationSetNumber}").ToList(),
                    };
                    Decisions.Add(lastDecisionAdded);
                    lastNumInputs = numOutputs;
                }
                informationSet.DecisionByteCode = decisionByteCode++;
            }
            NumDecisionsPerPlayer = players.Select(x => Decisions.Count(y => y.PlayerIndex == x)).ToList();
        }

        public EFGFileNode GetEFGFileNodesTree(string sourcefileText)
        {
            var list = GetEFGFileNodesList(sourcefileText);
            int listIndex = 0;
            var tree = list[0].CreateTree(list, ref listIndex);
            tree.AddActionsToDescendants(InformationSets);
            return tree;
        }

        private List<EFGFileNode> GetEFGFileNodesList(string sourcefileText)
        {
            List<string> GetNamesInBrackets(List<string> line, ref int currentIndex)
            {
                List<string> items = new List<string>();
                currentIndex++;
                while (line[currentIndex] != "}")
                {
                    items.Add(line[currentIndex]);
                    currentIndex++;
                }
                return items;
            }

            List<double> GetNumbersInBrackets(List<string> line, ref int currentIndex) => GetNamesInBrackets(line, ref currentIndex).Select(x => Convert.ToDouble(x)).ToList();


            (List<string> names, List<double> numbers) GetNamesAndNumbersInBrackets(List<string> line, ref int currentIndex)
            {
                var asStrings = GetNamesInBrackets(line, ref currentIndex);
                List<string> names = new List<string>();
                List<double> numbers = new List<double>();
                for (int indexInStrings = 0; indexInStrings < asStrings.Count; indexInStrings += 2)
                {
                    names.Add(asStrings[indexInStrings]);
                    numbers.Add(Convert.ToDouble(asStrings[indexInStrings + 1]));
                }
                return (names, numbers);
            }

            EFGFileInformationSet GetOrCreateInformationSet(int playerNumber, int informationSetNumber, string informationSetName, List<string> actionNames, List<double> probabilities = null)
            {
                bool alwaysCreateNewInformationSet = false;
                var informationSet = InformationSets.FirstOrDefault(x => x.InformationSetNumber == informationSetNumber && x.PlayerNumber == playerNumber);
                if (alwaysCreateNewInformationSet || informationSet == null)
                {
                    if (probabilities == null)
                        informationSet = new EFGFileInformationSet()
                        {
                            InformationSetNumber = informationSetNumber,
                            InformationSetName = informationSetName,
                            PlayerNumber = playerNumber,
                            ActionNames = actionNames,
                        };
                    else
                        informationSet = new EFGFileChanceInformationSet()
                        {
                            InformationSetNumber = informationSetNumber,
                            InformationSetName = informationSetName,
                            PlayerNumber = playerNumber,
                            ActionNames = actionNames,
                            ChanceProbabilities = probabilities.ToArray()
                        };
                    InformationSets.Add(informationSet);
                }

                return informationSet;
            }

            List<EFGFileNode> efgFileNodes = new List<EFGFileNode>();
            var lines = GetCommandsHelper(sourcefileText);
            for (int i = 0; i < lines.Count; i++)
            {
                List<string> line = lines[i];
                int j = 0;
                string commandName = line[j];
                switch (commandName)
                {
                    case "EFG":
                        string versionNumber = line[++j]; // should be 2
                        string rationalIndicator = line[++j]; // should be R
                        string gameName = line[++j];
                        j++;
                        PlayerNames = GetNamesInBrackets(line, ref j);
                        PlayerNames.Add("Chance"); // assume 1 chance player, which will eventually be the last decision
                        break;
                    case "p":
                        string nodeName = line[++j];
                        int playerNumber = Convert.ToInt32(line[++j]);
                        int informationSetNumber = Convert.ToInt32(line[++j]);
                        string informationSetName = line[++j];
                        j++;
                        List<string> actionNames = GetNamesInBrackets(line, ref j);
                        int outcome = Convert.ToInt32(line[++j]);
                        EFGFileInformationSet informationSet = GetOrCreateInformationSet(playerNumber, informationSetNumber, informationSetName, actionNames);
                        EFGFileInformationSetNode playerNode = new EFGFileInformationSetNode()
                        {
                            InformationSet = informationSet,
                            NodeName = nodeName
                        };
                        efgFileNodes.Add(playerNode);
                        break;
                    case "c":
                        string nodeName2 = line[++j];
                        int informationSetNumber2 = Convert.ToInt32(line[++j]);
                        string informationSetName2 = line[++j];
                        j++;
                        var namesAndProbabilities = GetNamesAndNumbersInBrackets(line, ref j);
                        int outcome2 = Convert.ToInt32(line[++j]);
                        EFGFileInformationSet informationSet2 = GetOrCreateInformationSet(0 /* in efg algorithms, chance player is generally 0 */, informationSetNumber2, informationSetName2, namesAndProbabilities.names, namesAndProbabilities.numbers);
                        EFGFileInformationSetNode chanceNode = new EFGFileInformationSetNode()
                        {
                            InformationSet = informationSet2,
                            NodeName = nodeName2
                        };
                        efgFileNodes.Add(chanceNode);
                        break;
                    case "t":
                        string nodeName3 = line[++j];
                        int outcomesInformationSetNumber = Convert.ToInt32(line[++j]);
                        string outcomesName = line[++j];
                        j++;
                        var outcomeValues = GetNumbersInBrackets(line, ref j);
                        var outcomesNode = new EFGFileOutcomeNode()
                        {
                            Values = outcomeValues.ToArray(),
                            NodeName = nodeName3,
                            OutcomesName = outcomesName
                        };
                        efgFileNodes.Add(outcomesNode);
                        break;
                    default:
                        throw new NotImplementedException();
                }
            }
            return efgFileNodes;
        }

        private List<List<string>> GetCommandsHelper (string sourcefileText)
        {
            List<string> ungrouped = sourcefileText.Split("\r\n").SelectMany(x => Regex.Split(x, "(?<=^[^\"]*(?:\"[^\"]*\"[^\"]*)*) (?=(?:[^\"]*\"[^\"]*\")*[^\"]*$)")).ToList(); // sourcefileText.Split(null).ToList();
            while (ungrouped[0] == "")
                ungrouped.RemoveAt(0);
            ungrouped = ungrouped.Select(x => x.StartsWith("\r\n") ? x.Substring(2) : x).ToList();
            List<List<string>> results = new List<List<string>>();
            List<string> currentLine = null;
            foreach (string item in ungrouped)
            {
                if (item is "EFG" or "c" or "p" or "t")
                {
                    if (currentLine != null)
                        results.Add(currentLine);
                    currentLine = new List<string>() { };
                }
                currentLine.Add(item);
            }
            if (currentLine.Any())
                results.Add(currentLine);
            return results;
        }
    }
}
