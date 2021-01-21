using JetBrains.Annotations;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace ACESimBase.GameSolvingSupport
{
    /// <summary>
    ///  A class for reading information from EFG files. 
    /// </summary>
    public class EFGFileProcess
    {
        public List<InformationSet> InformationSets = new List<InformationSet>();

        public class EFGFileNode
        {
            public string NodeName;
            public EFGFileNode[] ChildNodes;
            public virtual InformationSet GetInformationSet() => null;
            public virtual int NumChildNodes => 0;
            public EFGFileNode CreateTree(List<EFGFileNode> childNodes, ref int listIndex)
            {
                if (NumChildNodes > 0)
                    ChildNodes = new EFGFileNode[NumChildNodes];
                for (int childIndex = 0; childIndex < NumChildNodes; childIndex++)
                {
                    listIndex++;
                    ChildNodes[childIndex] = childNodes[listIndex].CreateTree(childNodes, ref listIndex);
                }
                return this;
            }

            public IEnumerable<EFGFileNode> EnumerateNodes(bool includeThis, bool topDown)
            {
                if (includeThis && topDown)
                    yield return this;
                if (ChildNodes != null)
                    foreach (var childNode in ChildNodes)
                        foreach (var descendant in childNode.EnumerateNodes(true, topDown))
                            yield return descendant;
                if (includeThis && !topDown)
                    yield return this;
            }

            public IEnumerable<InformationSetEFGNode> EnumerateInformationSetNodes(bool includeThis = false, bool topDown = true) => EnumerateNodes(includeThis, topDown).Where(x => x is InformationSetEFGNode).Select(x => (InformationSetEFGNode) x);

            public IEnumerable<InformationSet> EnumerateInformationSets(bool includeThis = false, bool topDown = true) => EnumerateNodes(includeThis, topDown).Select(x => x.GetInformationSet()).Where(x => x != null);


            public void AddActionsToDescendants()
            {
                // We need to figure out which information sets below each node should receive information about the action taken at the node.
                // If the same information set appears below the node for more than one action at the node, that must mean that the node doesn't
                // receive the information -- which means that the player doesn't receive the information. 
                var informationSet = GetInformationSet();
                if (ChildNodes != null)
                {
                    var informationSetsForChildren = ChildNodes.Select(x => x.EnumerateInformationSets(true, true)).ToList();
                    HashSet<int> allPlayers = new HashSet<int>();
                    HashSet<int> playersAppearingMultipleTimes = new HashSet<int>();
                    for (int i = 0; i < ChildNodes.Length; i++)
                        for (int j = i + 1; j < ChildNodes.Length; j++)
                        {
                            var iInformationSets = informationSetsForChildren[i];
                            var jInformationSets = informationSetsForChildren[j];
                            foreach (var iInformationSet in iInformationSets)
                                foreach (var jInformationSet in jInformationSets)
                                {
                                    if (iInformationSet == jInformationSet)
                                        playersAppearingMultipleTimes.Add(iInformationSet.PlayerNumber);
                                    allPlayers.Add(iInformationSet.PlayerNumber);
                                }
                        }
                    informationSet.PlayersReceivingInfo = new HashSet<int>(allPlayers.Except(playersAppearingMultipleTimes));
                    if (informationSet.PlayersReceivingInfo.Any())
                    {
                        for (int i = 0; i < ChildNodes.Length; i++)
                        {
                            int action = i + 1;
                            var information = new Information(informationSet.InformationSetNumber, informationSet.PlayerNumber, action);
                            var informationSetsForChild = informationSetsForChildren[0];
                            foreach (var informationSetForChild in informationSetsForChild)
                                if (informationSet.PlayersReceivingInfo.Contains(informationSetForChild.InformationSetNumber))
                                {
                                    informationSetForChild.InformationSetContents.Add(information);
                                }
                        }
                    }
                }
            }

        }

        public record Information(int informationSetNumber, int playerNumber, int oneBasedAction);

        public class InformationSet
        {
            public string InformationSetName;
            public int InformationSetNumber;
            public int PlayerNumber;
            public List<string> ActionNames;
            public List<Information> InformationSetContents = new List<Information>();
            public HashSet<int> PlayersReceivingInfo = new HashSet<int>();
            public virtual int NumActions => ActionNames.Count();
        }

        public class ChanceInformationSet : InformationSet
        {
            public double[] ChanceProbabilities;
            public override int NumActions => ChanceProbabilities.Length;
        }

        public class InformationSetEFGNode : EFGFileNode
        {
            public InformationSet InformationSet;
            public override InformationSet GetInformationSet() => InformationSet;
            public override int NumChildNodes => InformationSet.NumActions;

        }

        public class OutcomeEFGNode : EFGFileNode
        {
            public double[] Values;
            public string OutcomesName;
        }

        public EFGFileNode GetEFGFileNodesTree(string sourcefileText)
        {
            var list = GetEFGFileNodesList(sourcefileText);
            int listIndex = 0;
            var tree = list[0].CreateTree(list, ref listIndex);
            tree.AddActionsToDescendants();
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

            InformationSet GetOrCreateInformationSet(int playerNumber, int informationSetNumber, string informationSetName, List<string> actionNames, List<double> probabilities = null)
            {
                bool alwaysCreateNewInformationSet = false;
                var informationSet = InformationSets.FirstOrDefault(x => x.InformationSetNumber == informationSetNumber && x.PlayerNumber == playerNumber);
                if (alwaysCreateNewInformationSet || informationSet == null)
                {
                    if (probabilities == null)
                        informationSet = new InformationSet()
                        {
                            InformationSetNumber = informationSetNumber,
                            InformationSetName = informationSetName,
                            PlayerNumber = playerNumber,
                            ActionNames = actionNames,
                        };
                    else
                        informationSet = new ChanceInformationSet()
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
                        break;
                    case "p":
                        string nodeName = line[++j];
                        int playerNumber = Convert.ToInt32(line[++j]);
                        int informationSetNumber = Convert.ToInt32(line[++j]);
                        string informationSetName = line[++j];
                        j++;
                        List<string> actionNames = GetNamesInBrackets(line, ref j);
                        int outcome = Convert.ToInt32(line[++j]);
                        InformationSet informationSet = GetOrCreateInformationSet(playerNumber, informationSetNumber, informationSetName, actionNames);
                        InformationSetEFGNode playerNode = new InformationSetEFGNode()
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
                        InformationSet informationSet2 = GetOrCreateInformationSet(0 /* in efg algorithms, chance player is generally 0 */, informationSetNumber2, informationSetName2, namesAndProbabilities.names, namesAndProbabilities.numbers);
                        InformationSetEFGNode chanceNode = new InformationSetEFGNode()
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
                        var outcomesNode = new OutcomeEFGNode()
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
