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

        #region ECTA

        const int ECTA_MultiplyOutcomesByThisBeforeRounding = 1000;
        List<GameNodeRelationship> GameNodes;
        List<InformationSetInfo> InformationSetInfos;
        public List<int> MoveIndexToInfoSetIndex;
        public List<int> FirstInformationSetInfosIndexForPlayers;
        public List<int> FirstMovesIndexForPlayers;
        public Dictionary<(int informationSetIndex, int oneBasedMove), int> MoveIndexFromInfoSetIndexAndMoveWithinInfoSet;


        public record InformationSetInfo(IGameState GameState, int ECTAPlayerID, int Index, int FirstMoveIndex)
        {
            public int NumPossibleMoves => GameState.GetNumPossibleActions();
            public bool IsChance => GameState is ChanceNode;
            public InformationSetNode InformationSetNode => IsChance ? null : (InformationSetNode)GameState;
            public ChanceNode ChanceNode => IsChance ? (ChanceNode)GameState : null;
        }
        List<FinalUtilitiesNode> Outcomes => GameNodes.Where(x => x != null && x.GameState is FinalUtilitiesNode).Select(x => (FinalUtilitiesNode)x.GameState).ToList();
        int InformationSetInfoIndexForGameNode(int nodeIndex)
        {
            GameNodeRelationship gameNode = GameNodes[nodeIndex];
            var gameState = gameNode.GameState;
            return (int)gameState.AltNodeNumber;
            //for (int i = 0; i < InformationSetInfos.Count(); i++)
            //    if (gameState == InformationSetInfos[i].GameState)
            //        return i;
            //throw new Exception();
        }
        private int GetIndexOfMoveLeadingToNode(int nodeIndex)
        {
            int parentNodeID = (int)GameNodes[nodeIndex].ParentNodeID;
            int parentNodeInformationSetInfoIndex = InformationSetInfoIndexForGameNode(parentNodeID);
            byte actionAtParent = (byte)GameNodes[nodeIndex].ActionAtParent;
            int movesIndex = MoveIndexFromInfoSetIndexAndMoveWithinInfoSet[(parentNodeInformationSetInfoIndex, actionAtParent)];
            return movesIndex;
        }
        private (int moveIndex, int ectaPlayerID) GetIndexAndPlayerOfMoveLeadingToNode(int nodeIndex)
        {
            int parentNodeID = (int)GameNodes[nodeIndex].ParentNodeID;
            int parentNodeInformationSetInfoIndex = InformationSetInfoIndexForGameNode(parentNodeID);
            byte actionAtParent = (byte)GameNodes[nodeIndex].ActionAtParent;
            int movesIndex = MoveIndexFromInfoSetIndexAndMoveWithinInfoSet[(parentNodeInformationSetInfoIndex, actionAtParent)];
            int ectaPlayerID = InformationSetInfos[parentNodeInformationSetInfoIndex].ECTAPlayerID;
            return (movesIndex, ectaPlayerID);
        }
        private List<int> GetSequenceOfMovesLeadingToNode(int nodeIndex, int ectaPlayerID)
        {
            List<int> moves = new List<int>();
            while (nodeIndex != 1)
            {
                (int moveIndex, int previousECTAPlayerID) = GetIndexAndPlayerOfMoveLeadingToNode(nodeIndex);
                if (previousECTAPlayerID == ectaPlayerID)
                {
                    moves.Add(moveIndex);
                }
                nodeIndex = (int)GameNodes[nodeIndex].ParentNodeID;
            }
            moves.Reverse();
            return moves;
        }
        private List<IGrouping<int, int>> NodesGroupedByInformationSet()
        {
            var gameNodesWithInformationSets = GameNodes.Select((item, index) => (item, index)).Where(x => x.item != null && x.item.GameState is not FinalUtilitiesNode).Select(x => x.index).ToList();
            List<IGrouping<int, int>> grouped = gameNodesWithInformationSets.GroupBy(nodeIndex => InformationSetInfoIndexForGameNode(nodeIndex)).ToList();
            return grouped;
        }
        private void VerifyPerfectRecall()
        {
            // sequence must match for each node in information set
            var grouped = NodesGroupedByInformationSet();
            foreach (var group in grouped)
            {
                int firstNodeIndex = group.First();
                var informationSet = InformationSetInfos[InformationSetInfoIndexForGameNode(firstNodeIndex)];
                var ectaPlayerID = informationSet.ECTAPlayerID;
                bool verifyChancePlayer = false;
                if (ectaPlayerID == 0 && !verifyChancePlayer)
                    continue;
                List<int> sequence = GetSequenceOfMovesLeadingToNode(firstNodeIndex, ectaPlayerID);
                foreach (int additionalNodeIndex in group.Skip(1))
                {
                    List<int> additionalSequence = GetSequenceOfMovesLeadingToNode(additionalNodeIndex, ectaPlayerID);
                    if (!sequence.SequenceEqual(additionalSequence))
                        throw new Exception();
                }
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
            var originalOrder = GameNodes.ToList();

            // information sets must be in player order
            var orderedByPlayer = GameNodes.OrderByDescending(x => x.GameState is ChanceNode)
                .ThenByDescending(x => x.GameState is InformationSetNode)
                .ThenByDescending(x => x.GameState is FinalUtilitiesNode)
                .ThenBy(x => (x.GameState as InformationSetNode)?.PlayerIndex ?? 0)
                .ThenBy(x => x.NodeID)
                .ThenBy(x => x.ActionAtParent)
                .ToList();
            var chanceInformationSets = orderedByPlayer.Where(x => x != null && x.GameState is ChanceNode).Select(x => (ChanceNode)x.GameState).DistinctBy(x => x.ChanceNodeNumber).ToList();
            var playerInformationSets = orderedByPlayer.Where(x => x != null && x.GameState is InformationSetNode).Select(x => (InformationSetNode)x.GameState).DistinctBy(x => x.InformationSetNodeNumber).OrderBy(x => PlayerIDToECTA(((InformationSetNode)x).PlayerIndex)).ToList();
            InformationSetInfos = new List<InformationSetInfo>();
            int index = 0;
            foreach (var chanceInformationSet in chanceInformationSets)
                InformationSetInfos.Add(new InformationSetInfo(chanceInformationSet, 0, index++, -1));
            foreach (var playerInformationSet in playerInformationSets)
                InformationSetInfos.Add(new InformationSetInfo(playerInformationSet, PlayerIDToECTA(playerInformationSet.PlayerIndex), index++, -1));
            for (int i = 0; i < InformationSetInfos.Count(); i++)
                InformationSetInfos[i].GameState.AltNodeNumber = i; // set the alt node number so that we can match the numbering scheme expected of our ECTA code

            // game nodes must be in game order, but with outcomes all at end
            var orderedWithOutcomesLast = GameNodes
                .OrderBy(x => x.GameState is FinalUtilitiesNode)
                .ThenBy(x => x.NodeID)
                .ToList();
            orderedWithOutcomesLast.Insert(0, null); // the ECTA code skips 0
            // record map of original ID to new one-based ID given new order
            Dictionary<int, int> originalIDToRevised = new Dictionary<int, int>();
            for (int i = 1; i < orderedWithOutcomesLast.Count(); i++) // skip 0 as in ECTA code
            {
                originalIDToRevised[orderedWithOutcomesLast[i].NodeID] = i;
            }
            // now, fix IDs so that we are using the REVISED order of IDs. 
            for (int i = 1; i < orderedWithOutcomesLast.Count(); i++)
            {
                int? originalNodeID = orderedWithOutcomesLast[i].NodeID;
                orderedWithOutcomesLast[i] = orderedWithOutcomesLast[i] with
                {
                    OriginalNodeID = originalNodeID,
                    NodeID = originalIDToRevised[orderedWithOutcomesLast[i].NodeID],
                    ParentNodeID = orderedWithOutcomesLast[i].ParentNodeID is int originalParentID ? originalIDToRevised[originalParentID] : null
                };
            }
            GameNodes = orderedWithOutcomesLast;

            MoveIndexToInfoSetIndex = new List<int>();
            FirstInformationSetInfosIndexForPlayers = new List<int>();
            FirstMovesIndexForPlayers = new List<int>();
            MoveIndexFromInfoSetIndexAndMoveWithinInfoSet = new Dictionary<(int informationSetIndex, int oneBasedMove), int>();
            int lastPlayerID = -1;
            for (int i = 0; i < InformationSetInfos.Count; i++)
            {
                InformationSetInfo informationSetInfo = InformationSetInfos[i];
                InformationSetInfos[i] = informationSetInfo with
                {
                    FirstMoveIndex = MoveIndexToInfoSetIndex.Count()
                };
                if (lastPlayerID != informationSetInfo.ECTAPlayerID)
                {
                    lastPlayerID++;
                    FirstInformationSetInfosIndexForPlayers.Add(informationSetInfo.Index);
                    // The first move for a player is the empty sequence. We'll represent that by -1. We'll add the first non-empty move below.
                    FirstMovesIndexForPlayers.Add(MoveIndexToInfoSetIndex.Count());
                    MoveIndexToInfoSetIndex.Add(-1);
                }
                for (int move = 1; move <= informationSetInfo.NumPossibleMoves; move++)
                {
                    MoveIndexFromInfoSetIndexAndMoveWithinInfoSet[(informationSetInfo.Index, move)] = MoveIndexToInfoSetIndex.Count();
                    MoveIndexToInfoSetIndex.Add(informationSetInfo.Index); // Here's where we add the move.
                }
            }



            // printing
            StringBuilder asOriginallyOrdered = new StringBuilder();
            void PrintNodeAndChildren(int depth, int nodeID)
            {
                for (int j = 0; j < 5 * depth; j++)
                    asOriginallyOrdered.Append(" ");
                int nodeIndex = originalOrder.Select((item, index) => (item, index)).First(x => x.item.NodeID == nodeID).index;
                var node = originalOrder[nodeIndex];
                var correspondingNode = GameNodes.First(x => x != null && x.OriginalNodeID == node.NodeID);
                asOriginallyOrdered.AppendLine(correspondingNode.ToString());
                List<int> childrenNodeIDs = originalOrder.Select((item, index) => (item, index)).Where(x => x.item.ParentNodeID == nodeID).Select(x => x.index).ToList();
                foreach (var childNodeID in childrenNodeIDs)
                    PrintNodeAndChildren(depth + 1, childNodeID);
            }
            PrintNodeAndChildren(0, 0);

            StringBuilder revisedOrder = new StringBuilder();
            for (int i = 1; i < GameNodes.Count(); i++)
                revisedOrder.AppendLine(GameNodes[i].ToString());

            VerifyPerfectRecall();
        }

        // The goal here is to generate some string code that we can then paste into the ECTA C++ code from Github.
        public string GetECTACodeString()
        {
            var outcomes = Outcomes;
            StringBuilder s = new StringBuilder();
            string s1 = $@"    int pay[2][{outcomes.Count}] = {{ 
        {{ {String.Join(", ", outcomes.Select(x => Math.Round(x.Utilities[0] * ECTA_MultiplyOutcomesByThisBeforeRounding)))} }},
        {{ {String.Join(", ", outcomes.Select(x => Math.Round(x.Utilities[1] * ECTA_MultiplyOutcomesByThisBeforeRounding)))} }} 
    }};";
            s.AppendLine(s1);
            string s2 = $@"    alloctree({GameNodes.Count()},{InformationSetInfos.Count()},{MoveIndexToInfoSetIndex.Count()},{outcomes.Count});
    Outcome z = outcomes;
    firstiset[0] = isets + 0;
    firstiset[1] = isets + {FirstInformationSetInfosIndexForPlayers[1]};
    firstiset[2] = isets + {FirstInformationSetInfosIndexForPlayers[2]};
    firstmove[0] = moves + 0;
    firstmove[1] = moves + {FirstMovesIndexForPlayers[1]};
    firstmove[2] = moves + {FirstMovesIndexForPlayers[2]};
                
    // root node is at index 1 (index 0 is skipped)
    root = nodes + ROOT;
    root->father = NULL;
";
            s.Append(s2);
            int firstOutcome = -1;
            for (int n = 2; n < GameNodes.Count(); n++)
            {
                s.AppendLine($@"    nodes[{n}].father = nodes + {GameNodes[n].ParentNodeID};
                    ");
                if (GameNodes[n].GameState is FinalUtilitiesNode outcome)
                {
                    if (firstOutcome == -1)
                        firstOutcome = n;
                    s.AppendLine($@"    nodes[{n}].terminal = 1;
    nodes[{n}].outcome = z;
    z->whichnode = nodes + {n};
    z->pay[0] = ratfromi(pay[0][{n-firstOutcome}]);
    z->pay[1] = ratfromi(pay[1][{n-firstOutcome}]);
    z++;");
                }
            }
            for (int n = 1; n < GameNodes.Count(); n++)
            {
                if (GameNodes[n].GameState is not FinalUtilitiesNode)
                    s.AppendLine($"    nodes[{n}].iset = isets + {InformationSetInfoIndexForGameNode(n)};");
            }
            for (int n = 2; n < GameNodes.Count(); n++)
            {
                int movesIndex = GetIndexOfMoveLeadingToNode(n);
                s.AppendLine($"    nodes[{n}].reachedby = moves + {movesIndex};");
            }
            for (int i = 0; i < InformationSetInfos.Count(); i++)
            {
                s.AppendLine($@"    isets[{i}].player = {InformationSetInfos[i].ECTAPlayerID};
    isets[{i}].move0 = moves + {MoveIndexFromInfoSetIndexAndMoveWithinInfoSet[(i, 1)]};
    isets[{i}].nmoves = {InformationSetInfos[i].NumPossibleMoves};");
            }
            int playerIndexForMove = -1;
            for (int moveIndex = 0; moveIndex < MoveIndexToInfoSetIndex.Count(); moveIndex++)
            {
                int infoSetIndex = MoveIndexToInfoSetIndex[moveIndex];
                if (infoSetIndex == -1)
                {
                    playerIndexForMove++;
                    s.AppendLine($"    // move {moveIndex} is empty sequence for player {playerIndexForMove}");
                }
                else
                {
                    s.AppendLine($"    moves[{moveIndex}].atiset = isets + {infoSetIndex};");
                    if (playerIndexForMove == 0)
                    {
                        // chance player
                        var chance = InformationSetInfos[infoSetIndex].ChanceNode;
                        int moveIndexForFirstMove = MoveIndexFromInfoSetIndexAndMoveWithinInfoSet[(infoSetIndex, 1)];
                        int moveNumber = moveIndex - moveIndexForFirstMove + 1;
                        var rational = chance.GetActionProbabilityAsRational(ECTA_MultiplyOutcomesByThisBeforeRounding, moveNumber);
                        s.AppendLine($@"    moves[{moveIndex}].behavprob.num = {rational.Item1};
    moves[{moveIndex}].behavprob.den = {rational.Item2};");
                    }
                }
            }

            return s.ToString();
        }

        private int PlayerIDToECTA(int playerID) => playerID switch
        {
            0 => 1,
            1 => 2,
            _ => 0, // chance players (we're assuming a two-player game)
        };
        #endregion

        #region Gambit

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

        #endregion

        #region Create matrices (deprecated)

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

        #endregion
    }
}
