﻿using ACESim;
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
using ACESimBase.GameSolvingAlgorithms.ECTAAlgorithm;
using Rationals;
using JetBrains.Annotations;
using NumSharp.Utilities;

namespace ACESimBase.GameSolvingAlgorithms
{
    [Serializable]
    public partial class SequenceForm : StrategiesDeveloperBase
    {
        public enum SequenceFormApproach
        {
            Gambit,
            ECTA
        }
        SequenceFormApproach Approach = SequenceFormApproach.Gambit; 

        bool ProduceEFGFile = true; 


        public SequenceForm(List<Strategy> existingStrategyState, EvolutionSettings evolutionSettings, GameDefinition gameDefinition) : base(existingStrategyState, evolutionSettings, gameDefinition)
        {
        }

        public override IStrategiesDeveloper DeepCopy()
        {
            var created = new GeneticAlgorithm(Strategies, EvolutionSettings, GameDefinition);
            DeepCopyHelper(created);
            return created;
        }

        public override async Task Initialize()
        {
            GameDefinition.MakeAllChanceDecisionsKnowAllChanceActions(); // since there is just one chance player, each chance (and resolution) player must know all other chance decisions for ECTA algorithm to work properly
            AllowSkipEveryPermutationInitialization = false;
            StoreGameStateNodesInLists = true;
            await base.Initialize();
            InitializeInformationSets();
            if (!EvolutionSettings.CreateInformationSetCharts) // otherwise this will already have been run
                InformationSetNode.IdentifyNodeRelationships(InformationSets);
        }

        public override async Task<ReportCollection> RunAlgorithm(string optionSetName)
        {

            ReportCollection reportCollection = new ReportCollection();

            string filename = null;
            if (ProduceEFGFile)
                filename = CreateGambitFile();

            if (Approach == SequenceFormApproach.ECTA)
            {
                await ExecuteECTA(reportCollection);
            }
            else if (Approach == SequenceFormApproach.Gambit)
            {
                await UseGambitToCalculateEquilibrium(reportCollection, filename);
            }

            return reportCollection;
        }

        #region ECTA

        List<GameNodeRelationship> GameNodes;
        List<InformationSetInfo> InformationSetInfos;
        public List<int> MoveIndexToInfoSetIndex;
        public List<int> FirstInformationSetInfosIndexForPlayers;
        public List<int> FirstMovesIndexForPlayers;
        public Dictionary<(int informationSetIndex, int oneBasedMove), int> MoveIndexFromInfoSetIndexAndMoveWithinInfoSet;
        public List<(int informationSetIndex, int oneBasedMove)> NonChanceValidMoveIndexToInfoSetIndexAndMoveWithinInfoSet;


        public record InformationSetInfo(IGameState GameState, int ECTAPlayerID, int Index, int FirstMoveIndex)
        {
            public int NumPossibleMoves => GameState.GetNumPossibleActions();
            public bool IsChance => GameState is ChanceNode;
            public InformationSetNode InformationSetNode => IsChance ? null : (InformationSetNode)GameState;
            public ChanceNode ChanceNode => IsChance ? (ChanceNode)GameState : null;

            public override string ToString()
            {
                if (IsChance)
                    return ChanceNode.ShortString();
                else
                    return InformationSetNode.ToStringWithoutValues();
            }
        }
        List<FinalUtilitiesNode> Outcomes => GameNodes.Where(x => x != null && x.GameState is FinalUtilitiesNode).Select(x => (FinalUtilitiesNode)x.GameState).ToList();

        public async Task ExecuteECTA(ReportCollection reportCollection)
        {
            DetermineGameNodeRelationships();
            var ecta = new ECTARunner();
            ecta.numPriors = EvolutionSettings.SequenceFormNumPriorsToUseToGenerateEquilibria;
            ecta.outputPrior = false;
            ecta.outputGameTreeSetup = false;
            ecta.outputInitialTableau = false;
            ecta.outputLCP = false;
            ecta.outputLCPSolution = false;
            ecta.outputPivotingSteps = false;
            ecta.outputPivotResults = false;
            ecta.outputEquilibrium = true;
            ecta.outputRealizationPlan = false;
            bool usePresetEquilibria = false; // use this as a shortcut to replay some equilibrium
            List<List<double>> equilibria = null; 
            if (usePresetEquilibria)
            {
                List<double> eq = new List<double> {

                    //1,0,0.522862,0.477138,1,0,0.975029,0.0249706,1,0,0.522862,0.477138,1,0,0.975029,0.0249706,1,0,0,1,0,1,0,1,1,0,0,1,0,1,0,1,1,0,0,1,1,0,1,0,1,0,0.500793,0.499207,1,0,1,0,1,0,0.5,0.5,0,1,0,1,1,0,0.5,0.5,0,1,0,1

                    1,0,0,1,1/2,1/2,0,1,1,0,0,1,1/2,1/2,1139690000.0/1152971127.0,13281127.0/1152971127.0,1,0,0,1,1.0/2.0,1.0/2.0,0,1,1,0,0,1,1.0/2.0,1.0/2.0,0,1,1,0,0,1,1.0/2.0,1.0/2.0,1,0,1,0,13513.0/26983.0,13470.0/26983.0,1,0,1,0,1,0,0,1,1.0/2.0,1.0/2.0,0,1,1,0,0,1,1.0/2.0,1.0/2.0,0,1

                    //1,0,1,0,0,0,1,0,0,0,0,0,0,0,0,0,0,0,0,0,0,1,0,0,0,0,0,0,0,0,0,0,0,0,1,0,1,0,0,0,0,0,0,0,0,0,0,0,0,0,1,0,0,0,0,1,0,0,0,0,0,0,0,0,0,0,0,0,1,0,1,0,0,0,0,0,0,0,0,0,0,0,0,0,1,0,0,0,0,0,0,0,0,0,0,0,0,0,0,1,0,0,1,0,1,0,0,0,0,0,0,0,0,0,0,0,0,0,1,0,0,0,0,0,0,0,0,0,0,0,0,0,0,1,0,0,1,0,0,1,0,0,0,0,0,0,0,0,0,0,0,0,1,0,0,0,0,0,0,0,0,0,0,0,0,0,0,1,0,0,1,0,0,1,0,0,0,0,0,0,0,0,0,0,0,0,0,0,1,0,0,1,0,0,0,0,0,0,0,0,0,0,0,0,1,0,0,1,0,0,0,0,0,0,0,0,0,0,0,0,0,0,1,0,0,1,0,0,0,0,0,0,0,0,0,0,0,0,1,0,0,1,0,0,0,0,0,0,0,0,0,0,0,0,0,0,1,0,0,0,0,0,0,0,0,0,0,0,0,0,0,1,1,0,0,1,0,0,0,0,0,0,0,0,0,0,0,0,0,0,1,0,0,0,0,0,0,0,0,0,0,0,0,1,0,0,0,1,0,1,0,0,0,0,0,0,0,0,0,0,0,0,0,0,1,0,0,0,0,0,0,0,0,0,0,0,0,0,0,1


                };
                equilibria = new List<List<double>>() { eq };
            }
            else
                equilibria = ecta.Execute_ReturningDoubles(t => SetupECTA(t));
            await GenerateReportsFromEquilibria(equilibria, reportCollection);
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
            var playerInformationSets = orderedByPlayer.Where(x => x != null && x.GameState is InformationSetNode).Select(x => (InformationSetNode)x.GameState).DistinctBy(x => x.InformationSetNodeNumber).OrderBy(x => PlayerIDToECTA(((InformationSetNode)x).PlayerIndex)).ThenBy(x => x.InformationSetNodeNumber).ToList();
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
            NonChanceValidMoveIndexToInfoSetIndexAndMoveWithinInfoSet = new List<(int informationSetIndex, int oneBasedMove)>();
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
                    if (lastPlayerID != 0) // omit chance moves
                        NonChanceValidMoveIndexToInfoSetIndexAndMoveWithinInfoSet.Add((informationSetInfo.Index, move));

                }
            }

            PrintRelationships(originalOrder);

            bool produceCCode = false;
            if (produceCCode)
            {
                string ectaCodeInC = GetECTACodeInC();
            }

            VerifyPerfectRecall();
        }

        private void PrintRelationships(List<GameNodeRelationship> originalOrder)
        {
            bool printRelationships = false;
            // printing
            if (printRelationships)
            {
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
            }
        }

        int[] ConvertedToDesiredRange(IEnumerable<double> original)
        {
            var origMax = original.Max();
            var origMin = original.Min();
            var range = origMax - origMin;
            var fromZeroToOne = original.Select(x => (x - origMin) / range);
            if (fromZeroToOne.Any(x => double.IsNaN(x)))
                throw new Exception();
            return fromZeroToOne.Select(x => (int)Math.Round(x * EvolutionSettings.SequenceFormTopOfUtilityRange)).ToArray();
        }

        public void SetupECTA(ECTATreeDefinition t)
        {

            int[][] pay = new int[2][];
            pay[0] = ConvertedToDesiredRange(Outcomes.Select(x => x.Utilities[0]));
            pay[1] = ConvertedToDesiredRange(Outcomes.Select(x => x.Utilities[1]));

            t.alloctree(GameNodes.Count(),InformationSetInfos.Count(),MoveIndexToInfoSetIndex.Count(), Outcomes.Count);

            t.firstiset[0] = 0;
            t.firstiset[1] = FirstInformationSetInfosIndexForPlayers[1];
            t.firstiset[2] = FirstInformationSetInfosIndexForPlayers[2];
            t.firstmove[0] = 0;
            t.firstmove[1] = FirstMovesIndexForPlayers[1];
            t.firstmove[2] = FirstMovesIndexForPlayers[2];

            int zindex = 0;
            var z = t.outcomes[0];

            int firstOutcome = -1;
            t.nodes[ECTATreeDefinition.rootindex].father = -1;
            for (int n = 2; n < t.nodes.Length; n++)
            {
                t.nodes[n].father = (int) GameNodes[n].ParentNodeID;
                if (GameNodes[n].GameState is FinalUtilitiesNode outcome)
                {
                    if (firstOutcome == -1)
                        firstOutcome = n;
                    t.nodes[n].terminal = true;
                    t.nodes[n].outcome = zindex;
                    z.whichnode = n;
                    z.pay[0] = (Rational) pay[0][zindex];
                    z.pay[1] = (Rational) pay[1][zindex];
                    if (zindex < t.outcomes.Length - 1)
                        z = t.outcomes[++zindex];
                }
            }
                
            for (int n = 1; n < GameNodes.Count(); n++)
            {
                if (GameNodes[n].GameState is not FinalUtilitiesNode)
                    t.nodes[n].iset = InformationSetInfoIndexForGameNode(n);
            }
            for (int n = 2; n < GameNodes.Count(); n++)
            {
                int movesIndex = GetIndexOfMoveLeadingToNode(n);
                t.nodes[n].reachedby = movesIndex;
            }
            for (int i = 0; i < InformationSetInfos.Count(); i++)
            {
                InformationSetInfo iinfo = InformationSetInfos[i];
                var orignode = iinfo.InformationSetNode;
                t.isets[i].player = iinfo.ECTAPlayerID;
                t.isets[i].move0 = MoveIndexFromInfoSetIndexAndMoveWithinInfoSet[(i, 1)];
                t.isets[i].nmoves = iinfo.NumPossibleMoves;
                t.isets[i].name = iinfo.ToString();
            }

            int playerIndexForMove = -1;
            for (int moveIndex = 0; moveIndex < MoveIndexToInfoSetIndex.Count(); moveIndex++)
            {
                int infoSetIndex = MoveIndexToInfoSetIndex[moveIndex];
                if (infoSetIndex == -1)
                {
                    playerIndexForMove++;
                    // move moveIndex is empty sequence for player playerIndexForMove
                }
                else
                {
                    int moveIndexForFirstMove = MoveIndexFromInfoSetIndexAndMoveWithinInfoSet[(infoSetIndex, 1)];
                    int moveNumber = moveIndex - moveIndexForFirstMove + 1;
                    t.moves[moveIndex].atiset = infoSetIndex;
                    if (playerIndexForMove == 0)
                    {
                        // chance player
                        var chance = InformationSetInfos[infoSetIndex].ChanceNode;
                        var rational = chance.GetActionProbabilityAsRational(1000, moveNumber);
                        if (chance.Decision.DistributedChanceDecision && EvolutionSettings.DistributeChanceDecisions)
                        {
                            t.moves[moveIndex].behavprob = moveNumber == 1 ? (Rational)1 : (Rational)0;
                        }
                        else
                            t.moves[moveIndex].behavprob = rational.Item1 / (Rational) rational.Item2;
                    }
                }
            }
        }

        private int PlayerIDToECTA(int playerID) => playerID switch
        {
            0 => 1,
            1 => 2,
            _ => 0, // chance players (we're assuming a two-player game)
        };


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

        // This is to generate code that can be pasted into the original C code
        public string GetECTACodeInC()
        {
            const int ECTA_MultiplyOutcomesByThisBeforeRounding = 10_000;
            var outcomes = Outcomes;
            var player0Rounded = ConvertedToDesiredRange(outcomes.Select(x => x.Utilities[0]));
            var player1Rounded = ConvertedToDesiredRange(outcomes.Select(x => x.Utilities[1]));
            StringBuilder s = new StringBuilder();
            string s1 = $@"    int pay[2][{outcomes.Count}] = {{ 
        {{ {String.Join(", ", player0Rounded)} }},
        {{ {String.Join(", ", player1Rounded)} }} 
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
    z->pay[0] = ratfromi(pay[0][{n - firstOutcome}]);
    z->pay[1] = ratfromi(pay[1][{n - firstOutcome}]);
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


        #endregion

        #region Gambit

        private async Task UseGambitToCalculateEquilibrium(ReportCollection reportCollection, string filename)
        {
            string output = await RunGambit(filename);
            var results = ProcessGambitResults(reportCollection, output);
            await GenerateReportsFromEquilibria(results, reportCollection);
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

        private async Task<string> RunGambit(string filename)
        {
            Stopwatch s = new Stopwatch();
            s.Start();
            string output = await CreateGambitProcess(filename);
            TabbedText.WriteLine($"Gambit output for {filename} ({s.ElapsedMilliseconds} ms): {output}");
            return output;
        }

        private string CreateGambitFile()
        {
            EFGFileWriter efgCreator = new EFGFileWriter(GameDefinition.OptionSetName, GameDefinition.NonChancePlayerNames, EvolutionSettings.DistributeChanceDecisions);
            TreeWalk_Tree(efgCreator);
            string efgResult = efgCreator.FileText.ToString();
            DirectoryInfo folder = FolderFinder.GetFolderToWriteTo("ReportResults");
            var folderFullName = folder.FullName;
            string filename = Path.Combine(folderFullName, GameDefinition.OptionSetName + ".efg");
            TextFileCreate.CreateTextFile(filename, efgResult);
            return filename;
        }

        private async Task<string> CreateGambitProcess(string filename)
        {
            // Start the child process.
            Process p = new Process();
            // Redirect the output stream of the child process.
            p.StartInfo.UseShellExecute = false;
            p.StartInfo.RedirectStandardOutput = true;
            // -D gives more detailed info.
            // -q suppresses the banner
            // Note that -d is supposed to use decimals instead of rationals, but it doesn't work.
            // -P limits to subgame perfect

            bool useDecimals = false;
            int numDecimals = 6;
            string argumentsString = "";
            bool useLCP = true; 
            if (useLCP)
            {
                p.StartInfo.FileName = "C:\\Program Files (x86)\\Gambit\\gambit-lcp.exe";
                bool suppressBanner = false;
                bool subgamePerfectOnly = false; // DEBUG
                if (suppressBanner)
                    argumentsString += " -q";
                if (subgamePerfectOnly)
                    argumentsString += " -P";
            }
            else
            {
                p.StartInfo.FileName = "C:\\Program Files (x86)\\Gambit\\gambit-logit.exe";
            }
            if (useDecimals)
                argumentsString += " " + "-d " + numDecimals.ToString();
            argumentsString += " " + filename;
            p.StartInfo.Arguments = argumentsString;
            p.StartInfo.RedirectStandardOutput = true; 

            // Do not wait for the child process to exit before
            // reading to the end of its redirected stream.
            // p.WaitForExit();
            // Read the output stream first and then wait.

            string output = null;
            DateTime? nashEquilibriumFound = null;
            bool done = false;

            p.OutputDataReceived += new DataReceivedEventHandler(NetOutputDataHandler);
            p.Start();
            p.BeginOutputReadLine();

            void NetOutputDataHandler(object sendingProcess, DataReceivedEventArgs outLine)
            {
                string? data = outLine.Data;
                if (data != null)
                {
                    TabbedText.WriteLine(data);
                    output += data;
                }
            }

            while (!done)
            {
                if (output != null)
                {
                    if (nashEquilibriumFound == null && output.Contains("NE,"))
                        nashEquilibriumFound = DateTime.Now;
                    bool processHasCompleted = p.HasExited;
                    done = processHasCompleted || (nashEquilibriumFound is DateTime foundTime && DateTime.Now > foundTime + TimeSpan.FromSeconds(1));
                    if (!done)
                        await Task.Delay(100);
                }
            }
            p.Kill(true); // don't wait for rest of equilibria
            return output;
        }

        #endregion

        #region Equilibria and reporting

        private async Task GenerateReportsFromEquilibria(List<List<double>> equilibria, ReportCollection reportCollection)
        {
            bool includeCorrelatedEquilibriumReport = true;
            if (includeCorrelatedEquilibriumReport)
                SaveWeightedGameProgressesAfterEachReport = true;
            bool includeReportForFirstEquilibrium = true;
            bool includeReportForEachEquilibrium = true;
            int numEquilibria = equilibria.Count();
            var infoSets = InformationSets.OrderBy(x => x.PlayerIndex).ThenBy(x => x.InformationSetNodeNumber).ToList();
            var infoSetNames = infoSets.Select(x => x.ToStringWithoutValues()).ToArray();
            for (int eqNum = 0; eqNum < numEquilibria; eqNum++)
            {
                bool isFirst = eqNum == 0;
                bool isLast = eqNum == numEquilibria - 1;
                var actionProbabilities = equilibria[eqNum];
                if (infoSets.Sum(x => x.Decision.NumPossibleActions) != actionProbabilities.Count())
                {
                    TabbedText.WriteLine($"Equilibrium {eqNum + 1}: mismatch in number of possible actions; skipping"); // Should not happen
                    throw new Exception("Unexpected mismatch");
                }
                else
                    await ProcessEquilibrium(reportCollection, includeCorrelatedEquilibriumReport, includeReportForFirstEquilibrium, includeReportForEachEquilibrium, numEquilibria, infoSets, eqNum, isFirst, isLast, actionProbabilities);
            }
        }

        private async Task ProcessEquilibrium(ReportCollection reportCollection, bool includeCorrelatedEquilibriumReport, bool includeReportForFirstEquilibrium, bool includeReportForEachEquilibrium, int numEquilibria, List<InformationSetNode> infoSets, int eqNum, bool isFirst, bool isLast, List<double> actionProbabilities)
        {
            int totalNumbersProcessed = 0;
            for (int i = 0; i < infoSets.Count(); i++)
            {
                double total = 0;
                InformationSetNode infoSet = infoSets[i];
                for (byte a = 1; a <= infoSet.Decision.NumPossibleActions; a++)
                {
                    double v = actionProbabilities[totalNumbersProcessed++];
                    total += v;
                    infoSet.SetActionToProbabilityValue(a, v, true);
                }
                if (total == 0)
                {
                    bool setArbitraryActionTo1 = true;
                    bool firstAction = false;
                    if (setArbitraryActionTo1)
                        infoSet.SetActionToProbabilityValue(firstAction ? 1 : infoSet.Decision.NumPossibleActions, 1.0, true);
                    else
                    {
                        // This information set cannot be reached. Use even probabilities.
                        double p = 1.0 / (double)infoSet.Decision.NumPossibleActions;
                        for (byte a = 1; a <= infoSet.Decision.NumPossibleActions; a++)
                        {
                            infoSet.SetActionToProbabilityValue(a, p, true);
                        }
                    }
                }
                if (includeCorrelatedEquilibriumReport)
                    infoSet.RecordProbabilitiesAsPastValues();
            }

            //double[] utils = GetAverageUtilities(false);
            //double[] maxPurifiedUtils = GetMaximumUtilitiesFromPurifiedStrategies();
            //if (Enumerable.Range(0, NumNonChancePlayers).Any(p => maxPurifiedUtils[p] > utils[p] + 0.001))
            //    TabbedText.WriteLine($"Maximum not achieved {String.Join(",", utils)} vs. {String.Join(",", maxPurifiedUtils)}"); // DEBUG

            if ((includeReportForFirstEquilibrium && isFirst) || includeReportForEachEquilibrium)
            {
                await AddReportForEquilibrium(reportCollection, numEquilibria, eqNum);
            }
            if (includeCorrelatedEquilibriumReport && isLast)
            {
                AddCorrelatedEquilibriumReport(reportCollection);
            }
        }

        private async Task AddReportForEquilibrium(ReportCollection reportCollection, int numEquilibria, int eqNum)
        {
            Stopwatch s = new Stopwatch();
            s.Start();
            EvolutionSettings.ActionStrategiesToUseInReporting = new List<ActionStrategies>() { ActionStrategies.CurrentProbability }; // will use latest equilibrium 
            var reportResult = await GenerateReports(EvolutionSettings.ReportEveryNIterations ?? 0,
                () =>
                    $"{GameDefinition.OptionSetName}{(EvolutionSettings.SequenceFormNumPriorsToUseToGenerateEquilibria > 1 ? $"Eq{eqNum + 1}" : "")}");
            reportCollection.Add(reportResult, false, true);
            TabbedText.WriteLine($"Elapsed milliseconds generating report: {s.ElapsedMilliseconds}");
        }

        private void AddCorrelatedEquilibriumReport(ReportCollection reportCollection)
        {
            Stopwatch s = new Stopwatch();
            s.Start();
            var reportResult = GenerateReportFromSavedWeightedGameProgresses(true);
            reportResult.AddName($"{GameDefinition.OptionSetName}{("Corr")}");
            PrintReportsToScreenIfNotSuppressed(reportResult);
            reportCollection.Add(reportResult, false, true);
            TabbedText.WriteLine($"Elapsed milliseconds generating report: {s.ElapsedMilliseconds}");
        }

        #endregion
    }
}
