using ACESim;
using ACESimBase.Util;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NumSharp;
using System.IO;
using ACESimBase.GameSolvingSupport;
using System.Diagnostics;
using ACESim.Util;

namespace ACESimBase.GameSolvingAlgorithms
{
    [Serializable]
    public partial class SequenceForm : StrategiesDeveloperBase
    {
        double[,] E, F, A, B;
        bool UseGambit = true;
        bool UseECTA = true; // 2002 code from Stengel -- all rest is for that
        List<GameNodeRelationship> GameNodes; 
        List<InformationSetInfo> InformationSetInfos; 
        public List<int> InformationSetInfosIndexForMoves;
        public List<int> FirstInformationSetInfosIndexForPlayers; 
        public List<int> FirstMovesIndexForPlayers;
        public Dictionary<(int informationSetIndex, int oneBasedMove), int> MapInformationSetAndMoveToMoveIndex;


        public record InformationSetInfo(IGameState GameState, int ECTAPlayerID, int Index, int FirstMoveIndex)
        {
            public int NumPossibleMoves => GameState.GetNumPossibleActions();
            public bool IsChance => GameState is ChanceNode;
            public InformationSetNode InformationSetNode => IsChance ? null : (InformationSetNode)GameState;
            public ChanceNode ChanceNode => IsChance ? (ChanceNode)GameState : null;
        }
        List<FinalUtilitiesNode> Outcomes => GameNodes.Where(x => x.GameState is FinalUtilitiesNode).Select(x => (FinalUtilitiesNode)x.GameState).ToList();
        int InformationSetInfoIndexForGameNode(int nodeIndex)
        {
            GameNodeRelationship gameNode = GameNodes[nodeIndex];
            var gameState = gameNode.GameState;
            for (int i = 0; i < InformationSetInfos.Count(); i++)
                if (gameState == InformationSetInfos[i].GameState)
                    return i;
            throw new Exception();
        }


        public SequenceForm(List<Strategy> existingStrategyState, EvolutionSettings evolutionSettings, GameDefinition gameDefinition) : base(existingStrategyState, evolutionSettings, gameDefinition)
        {
            if (EvolutionSettings.DistributeChanceDecisions)
                throw new Exception("Distributing chance decisions is not supported.");
        }

        public override IStrategiesDeveloper DeepCopy()
        {
            var created = new GeneticAlgorithm(Strategies, EvolutionSettings, GameDefinition);
            DeepCopyHelper(created);
            return created;
        }
        public override async Task Initialize()
        {
            AllowSkipEveryPermutationInitialization = false;
            StoreGameStateNodesInLists = true;
            await base.Initialize();
            InitializeInformationSets();
            if (!EvolutionSettings.CreateInformationSetCharts) // otherwise this will already have been run
                InformationSetNode.IdentifyNodeRelationships(InformationSets);
            if (UseECTA)
            {
                DetermineGameNodeRelationships();
                string codeString = GetECTACodeString();
            }
        }

        public void DetermineGameNodeRelationships()
        {
            if (NumNonChancePlayers != 2)
                throw new NotImplementedException();

            IGameState rootState = GetGameState(GetStartOfGameHistoryPoint());
            GameNodeRelationshipsFinder finder = new GameNodeRelationshipsFinder(rootState);
            TreeWalk_Tree(finder, 0);

            GameNodes = finder.Relationships;
            var reordered = GameNodes.OrderByDescending(x => x.GameState is ChanceNode)
                .ThenByDescending(x => x.GameState is InformationSetNode)
                .ThenByDescending(x => x.GameState is FinalUtilitiesNode)
                .ThenByDescending(x => (x.GameState as InformationSetNode)?.PlayerIndex ?? 0)
                .ThenBy(x => x.NodeID)
                .ThenBy(x => x.ActionAtParent)
                .ToList();
            // record map of original ID to new one-based ID given new order
            Dictionary<int, int> originalIDToRevised = new Dictionary<int, int>();
            for (int i = 0; i < reordered.Count(); i++)
            {
                originalIDToRevised[reordered[i].NodeID] = i + 1; // note that our revised numbering system is 1-based (we just skip 0)
            }
            // now, fix IDs
            for (int i = 0; i < reordered.Count(); i++)
            {
                reordered[i] = reordered[i] with
                {
                    NodeID = originalIDToRevised[reordered[i].NodeID],
                    ParentNodeID = reordered[i].ParentNodeID is int originalParentID ? originalIDToRevised[originalParentID] : null
                };
            }
            GameNodes = reordered;

            var chanceInformationSets = GameNodes.Where(x => x.GameState is ChanceNode).Select(x => (ChanceNode)x.GameState).DistinctBy(x => x.ChanceNodeNumber).ToList();
            var playerInformationSets = GameNodes.Where(x => x.GameState is InformationSetNode).Select(x => (InformationSetNode)x.GameState).DistinctBy(x => x.InformationSetNodeNumber).OrderBy(x => PlayerIDToECTA(((InformationSetNode)x).PlayerIndex)).ToList();
            InformationSetInfos = new List<InformationSetInfo>();
            int index = 0;
            foreach (var chanceInformationSet in chanceInformationSets)
                InformationSetInfos.Add(new InformationSetInfo(chanceInformationSet, 0, index++, -1));
            foreach (var playerInformationSet in playerInformationSets)
                InformationSetInfos.Add(new InformationSetInfo(playerInformationSet, PlayerIDToECTA(playerInformationSet.PlayerIndex), index++, -1));
            InformationSetInfosIndexForMoves = new List<int>();
            FirstInformationSetInfosIndexForPlayers = new List<int>();
            FirstMovesIndexForPlayers = new List<int>();
            MapInformationSetAndMoveToMoveIndex = new Dictionary<(int informationSetIndex, int oneBasedMove), int>();
            int lastPlayerID = -1;
            for (int i = 0; i < InformationSetInfos.Count; i++)
            {
                InformationSetInfo informationSetInfo = InformationSetInfos[i];
                InformationSetInfos[i] = informationSetInfo with
                {
                    FirstMoveIndex = InformationSetInfosIndexForMoves.Count()
                };
                if (lastPlayerID != informationSetInfo.ECTAPlayerID)
                {
                    lastPlayerID++;
                    FirstInformationSetInfosIndexForPlayers.Add(informationSetInfo.Index);
                    FirstMovesIndexForPlayers.Add(InformationSetInfosIndexForMoves.Count());
                }
                for (int move = 1; move <= informationSetInfo.NumPossibleMoves; move++)
                {
                    MapInformationSetAndMoveToMoveIndex[(informationSetInfo.Index, move)] = InformationSetInfosIndexForMoves.Count();
                    InformationSetInfosIndexForMoves.Add(informationSetInfo.Index);
                }
            }
        }

        public string GetECTACodeString()
        {
            var outcomes = Outcomes;
            StringBuilder s = new StringBuilder();
            string s2 = $@"\tOutcome z;
    alloctree({GameNodes.Count()},{InformationSetInfos.Count()},{InformationSetInfosIndexForMoves.Count()},{outcomes.Count});
    firstiset[0] = isets;
    firstiset[1] = isets + {FirstInformationSetInfosIndexForPlayers[1]};
    firstiset[2] = isets + {FirstInformationSetInfosIndexForPlayers[2]};
    firstmove[0] = moves;
    firstMove[1] = moves + {FirstMovesIndexForPlayers[1]};
    firstMove[2] = moves + {FirstMovesIndexForPlayers[2]};
                
    // root node is at index 1 (index 0 is skipped)
    root = nodes + ROOT;
    root->father = NULL;
            ";
            s.Append(s2);
            for (int n = 2; n < GameNodes.Count(); n++)
            {
                s.AppendLine($@"\tnodes[{n}].father = nodes + {GameNodes[n].ParentNodeID};
                    ");
                if (GameNodes[n].GameState is FinalUtilitiesNode outcome)
                {
                    s.AppendLine($@"nodes[{n}].terminal = 1;
    nodes[i].outcome = z;
    z->whichnode = nodes + {n};
    z->pay[0] = ratfromi(pay[0][{n}]);
    z->pay[1] = ratfromi(pay[1][{n}]);
    z++;");
                }
            }
            for (int n = 1; n < GameNodes.Count(); n++)
            {
                if (GameNodes[n].GameState is not FinalUtilitiesNode)
                    s.AppendLine($"\tnodes[{n}].iset = isets + {InformationSetInfoIndexForGameNode(n)};");
            }
            for (int n = 2; n < GameNodes.Count(); n++)
            {
                if (GameNodes[n].GameState is not FinalUtilitiesNode)
                {
                    int informationSetInfoIndexForGameNode = InformationSetInfoIndexForGameNode(n);
                    int parentNodeID = (int)GameNodes[n].ParentNodeID;
                    byte actionAtParent = (byte)GameNodes[n].ActionAtParent;
                    int movesIndex = MapInformationSetAndMoveToMoveIndex[(informationSetInfoIndexForGameNode, actionAtParent)];
                    s.AppendLine($"\tnodes[{n}].reachedby = moves + {movesIndex};");
                }
            }
            for (int i = 0; i < InformationSetInfos.Count(); i++)
            {
                s.AppendLine($@"\tisets[{i}].player = {InformationSetInfos[i].ECTAPlayerID};
    isets[{i}].move0 = moves + {MapInformationSetAndMoveToMoveIndex[(i, 1)]};
    isets[{i}].nmoves = {InformationSetInfos[i].NumPossibleMoves};");
            }

            return s.ToString();
        }

        private int PlayerIDToECTA(int playerID) => playerID switch
        {
            0 => 1,
            1 => 2,
            _ => 0, // chance players (we're assuming a two-player game)
        };

        public override async Task<ReportCollection> RunAlgorithm(string optionSetName)
        {

            ReportCollection reportCollection = new ReportCollection();

            if (UseGambit)
            {
                await UseGambitToCalculateEquilibrium(reportCollection);
            }
            else
            {
                CreateConstraintMatrices();
                CreatePayoffMatrices();
                ExportMatrices();
            }

            return reportCollection;
        }

        private async Task UseGambitToCalculateEquilibrium(ReportCollection reportCollection)
        {
            string filename = CreateGambitFile();
            string output = RunGambit(filename);
            var results = ProcessGambitResults(reportCollection, output);
            await SetEquilibria(results, reportCollection);
        }

        private List<List<double>> ProcessGambitResults(ReportCollection reportCollection, string output)
        {
            string[] result = output.Split("\n\r".ToCharArray(), StringSplitOptions.RemoveEmptyEntries);
            List<List<double>> resultsAsDoubles = new List<List<double>>();
            if (result.Any())
            {
                int numEquilibria = result.Length;
                for (int eqNum = 0; eqNum < result.Length; eqNum++)
                {
                    string anEquilibriumString = (string)result[eqNum];
                    if (anEquilibriumString.StartsWith("NE,"))
                    {
                        string numbersOnly = anEquilibriumString[3..];
                        string[] rationalNumbers = numbersOnly.Split(',');
                        List<double> numbers = new List<double>();
                        foreach (string rationalNumberString in rationalNumbers)
                        {
                            if (rationalNumberString.Contains("/"))
                            {
                                string[] fractionComponents = rationalNumberString.Split('/');
                                double rationalConverted = double.Parse(fractionComponents[0]) / double.Parse(fractionComponents[1]);
                                numbers.Add(rationalConverted);
                            }
                            else
                            {
                                numbers.Add(double.Parse(rationalNumberString));
                            }
                        }
                        resultsAsDoubles.Add(numbers);
                    }
                }
            }
            return resultsAsDoubles;
        }

        private async Task SetEquilibria(List<List<double>> equilibria, ReportCollection reportCollection)
        {
            bool useCorrelatedEquilibrium = true;
            int numEquilibria = equilibria.Count();
            for (int eqNum = 0; eqNum < numEquilibria; eqNum++)
            {
                bool isLast = eqNum == numEquilibria - 1;
                var numbers = equilibria[eqNum];
                var infoSets = InformationSets.OrderBy(x => x.PlayerIndex).ThenBy(x => x.InformationSetNodeNumber).ToList();
                if (infoSets.Sum(x => x.Decision.NumPossibleActions) != numbers.Count())
                    throw new Exception();
                int totalNumbersProcessed = 0;
                for (int i = 0; i < infoSets.Count(); i++)
                {
                    for (byte a = 1; a <= infoSets[i].Decision.NumPossibleActions; a++)
                    {
                        infoSets[i].SetActionToProbabilityValue(a, numbers[totalNumbersProcessed++], true);
                    }
                    if (useCorrelatedEquilibrium)
                        infoSets[i].RecordProbabilitiesAsPastValues();
                }
                if (useCorrelatedEquilibrium)
                    EvolutionSettings.ActionStrategiesToUseInReporting = new List<ActionStrategies>() { ActionStrategies.CorrelatedEquilibrium };
                

                if (!useCorrelatedEquilibrium || isLast)
                {
                    var reportResult = await GenerateReports(EvolutionSettings.ReportEveryNIterations ?? 0,
                        () =>
                            $"{GameDefinition.OptionSetName}{(numEquilibria > 1 && !useCorrelatedEquilibrium ? $"Eq{eqNum + 1}" : "")}");
                    reportCollection.Add(reportResult);
                }
            }
        }

        private string RunGambit(string filename)
        {
            Stopwatch s = new Stopwatch();
            s.Start();
            string output = RunGambitLCP(filename);
            TabbedText.WriteLine($"Gambit output for {filename} ({s.ElapsedMilliseconds} ms): {output}");
            return output;
        }

        private string CreateGambitFile()
        {
            EFGFileCreator efgCreator = new EFGFileCreator(GameDefinition.OptionSetName, GameDefinition.NonChancePlayerNames);
            TreeWalk_Tree(efgCreator);
            string efgResult = efgCreator.FileText.ToString();
            DirectoryInfo folder = FolderFinder.GetFolderToWriteTo("ReportResults");
            var folderFullName = folder.FullName;
            string filename = Path.Combine(folderFullName, GameDefinition.OptionSetName + ".efg");
            TextFileCreate.CreateTextFile(filename, efgResult);
            return filename;
        }

        private string RunGambitLCP(string filename)
        {
            // Start the child process.
            Process p = new Process();
            // Redirect the output stream of the child process.
            p.StartInfo.UseShellExecute = false;
            p.StartInfo.RedirectStandardOutput = true;
            p.StartInfo.FileName = "C:\\Program Files (x86)\\Gambit\\gambit-lcp.exe";
            // -D gives more detailed info.
            // -q suppresses the banner
            // Note that -d is supposed to use decimals instead of rationals, but it doesn't work.
            // -P limits to subgame perfect

            bool suppressBanner = true;
            bool subgamePerfectOnly = true;
            bool useDecimals = true;
            int numDecimals = 6;
            string argumentsString = "";
            if (suppressBanner)
                argumentsString += " -q";
            if (subgamePerfectOnly)
                argumentsString += " -P";
            if (useDecimals)
                argumentsString += " " + "-d " + numDecimals.ToString();
            argumentsString += " " + filename;
            p.StartInfo.Arguments = argumentsString; 
            p.Start();
            // Do not wait for the child process to exit before
            // reading to the end of its redirected stream.
            // p.WaitForExit();
            // Read the output stream first and then wait.
            string output = p.StandardOutput.ReadToEnd();
            p.WaitForExit();
            return output;
        }

        private void CreatePayoffMatrices()
        {
            SequenceFormPayoffs c = new SequenceFormPayoffs(E.GetLength(1), F.GetLength(1));
            TreeWalk_Tree(c);
            c.FinalizeMatrices();
            A = c.A;
            B = c.B;
        }

        private void ExportMatrices()
        {
            string path = FolderFinder.GetFolderToWriteTo("Strategies").FullName;
            bool convertToSparse = false;
            double[,] maybeConvert(double[,] x) => convertToSparse ? x.ConvertToSparse() : x;
            SaveMultidimensionalMatrix(path, "A.npy", maybeConvert(A));
            SaveMultidimensionalMatrix(path, "B.npy", maybeConvert(B));
            SaveMultidimensionalMatrix(path, "E.npy", maybeConvert(E));
            SaveMultidimensionalMatrix(path, "F.npy", maybeConvert(F));
        }

        private static void SaveMultidimensionalMatrix(string path, string filename, double[,] original)
        {
            bool verify = false;
            if (verify)
                VerifyNotAllZero(original);
            np.Save(original, Path.Combine(path, filename));
            if (verify)
                VerifyMatrixSerialization(path, filename, original);
        }

        private static void VerifyNotAllZero(double[,] original)
        {
            int dim0 = original.GetLength(0);
            int dim1 = original.GetLength(1);
            bool allZero = true;
            for (int counter0 = 0; counter0 < dim0; counter0++)
            {
                for (int counter1 = 0; counter1 < dim1; counter1++)
                {
                    if (original[counter0, counter1] != 0)
                    {
                        allZero = false;
                        break;
                    }
                }
                if (!allZero)
                    break;
            }
            if (allZero)
                throw new Exception("Matrix is entirely zeros.");
        }

        private static void VerifyMatrixSerialization(string path, string filename, double[,] original)
        {
            var reloaded = np.Load<double[,]>(Path.Combine(path, filename));
            bool isEqual = true;
            int dim0 = original.GetLength(0);
            int dim1 = original.GetLength(1);
            if (dim0 != reloaded.GetLength(0) || dim1 != reloaded.GetLength(1))
                throw new Exception("Serialization failed -- inconsistent array sizes");
            for (int counter0 = 0; counter0 < dim0; counter0++)
            {
                for (int counter1 = 0; counter1 < dim1; counter1++)
                {
                    if (original[counter0, counter1] != reloaded[counter0, counter1])
                    {
                        isEqual = false;
                    }
                }
            }
            if (!isEqual)
                throw new Exception("Serialization failed");
        }

        private void CreateConstraintMatrices()
        {
            int[] cumulativeChoiceNumber = new int[NumNonChancePlayers];
            for (int p = 0; p < NumNonChancePlayers; p++)
            {
                var informationSets = InformationSets.Where(x => x.PlayerIndex == p).OrderBy(x => x.InformationSetContents.Length).ThenBy(x => x.InformationSetContents, new ArrayComparer<byte>()).ToList();
                for (int perPlayerInformationSetNumber = 1; perPlayerInformationSetNumber <= informationSets.Count(); perPlayerInformationSetNumber++)
                {
                    var informationSet = informationSets[perPlayerInformationSetNumber - 1];
                    informationSet.PerPlayerNodeNumber = perPlayerInformationSetNumber;
                    informationSet.CumulativeChoiceNumber = cumulativeChoiceNumber[p];
                    cumulativeChoiceNumber[p] += informationSet.NumPossibleActions;
                }
                double[,] constraintMatrix = new double[informationSets.Count() + 1, cumulativeChoiceNumber[p] + 1];
                constraintMatrix[0, 0] = 1; // empty sequence
                for (int perPlayerInformationSetNumber = 1; perPlayerInformationSetNumber <= informationSets.Count(); perPlayerInformationSetNumber++)
                {
                    var informationSet = informationSets[perPlayerInformationSetNumber - 1];
                    int columnOfParent = 0;
                    if (informationSet.ParentInformationSet is InformationSetNode parent)
                    {
                        byte actionAtParent = informationSet.InformationSetContentsSinceParent[0]; // one-based action
                        columnOfParent = parent.CumulativeChoiceNumber + actionAtParent;
                    }
                    constraintMatrix[perPlayerInformationSetNumber, columnOfParent] = -1;
                    for (int a = 1; a <= informationSet.NumPossibleActions; a++)
                    {
                        constraintMatrix[perPlayerInformationSetNumber, informationSet.CumulativeChoiceNumber + a] = 1;
                    }
                }
                if (p == 0)
                    E = constraintMatrix;
                else
                    F = constraintMatrix;
            }
        }
    }
}
