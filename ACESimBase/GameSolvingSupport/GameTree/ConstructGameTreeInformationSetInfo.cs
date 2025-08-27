using ACESim;
using ACESimBase.Util.Debugging;
using ACESimBase.Util.NWayTreeStorage;
using ACESimBase.Util.Reporting;
using ACESimBase.Util.Tikz;
using Microsoft.FSharp.Linq;
using SixLabors.ImageSharp.Processing;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tensorflow.Framework;

namespace ACESimBase.GameSolvingSupport.GameTree
{
    public class ConstructGameTreeInformationSetInfo : ITreeNodeProcessor<ConstructGameTreeInformationSetInfo.ForwardInfo, ConstructGameTreeInformationSetInfo.MoveProbabilityTracker<(byte decisionByteCode, byte move)>>
    {
        Dictionary<int, double> ProbabilityOfReachingInformationSetForNonChancePlayer = new Dictionary<int, double>();
        Dictionary<int, double> ProbabilityOfReachingInformationSetForChance = new Dictionary<int, double>();
        Dictionary<int, double> ProbabilityOfReachingInformationSet(bool forNonChancePlayer) => forNonChancePlayer ? ProbabilityOfReachingInformationSetForNonChancePlayer : ProbabilityOfReachingInformationSetForChance;
        Dictionary<int, MoveProbabilityTracker<(byte decisionByteCode, byte move)>> StatisticsForNonChancePlayerNodes = new Dictionary<int, MoveProbabilityTracker<(byte decisionByteCode, byte move)>>();
        Dictionary<int, MoveProbabilityTracker<(byte decisionByteCode, byte move)>> StatisticsForChanceNodes = new Dictionary<int, MoveProbabilityTracker<(byte decisionByteCode, byte move)>>();
        Dictionary<int, MoveProbabilityTracker<(byte decisionByteCode, byte move)>> StatisticsForInformationSets(bool forNonChancePlayers) => forNonChancePlayers ? StatisticsForNonChancePlayerNodes : StatisticsForChanceNodes;

        Dictionary<(bool chancePlayer, int nodeNumber), IAnyNode> InformationSets = new Dictionary<(bool chancePlayer, int nodeNumber), IAnyNode>();
        NWayTreeStorageInternal<GamePointNode> TreeRoot;
        Stack<NWayTreeStorageInternal<GamePointNode>> ParentNodes = new Stack<NWayTreeStorageInternal<GamePointNode>>();
        Stack<double> ProbabilitiesToNode = new Stack<double>();
        byte NumNonChancePlayers = 0;
        GameDefinition GameDefinition;

        public record GamePointNode(IAnyNode anyNode, double gamePointReachProbability)
        {
            public EdgeInfo EdgeFromParent;
            public int NodeLevel => EdgeFromParent == null ? 0 : EdgeFromParent.parentLevel + 1;
            public bool IncludeInDiagram = true;
            public bool ExcludeBelow = false; // GenerateTikzDiagram allows a function as a parameter that allows this to be set to simplify complex diagrams. 
            internal bool ExcludedFromAbove;
            public List<GamePointNode> Children = new List<GamePointNode>();
            public double XLocation, YRangeStart, YRangeEnd;
            public double YLocation => 0.5 * (YRangeStart + YRangeEnd);
            public int TikzIndex;
            internal bool IncludeInRestrictedBelow;
            internal bool IncludeInRestrictedFromAbove;

            public string Ancestry(GameDefinition gameDefinition)
            {
                string thisLevel = NodePlayerString(gameDefinition) + $" {ExcludeBelow} {ExcludedFromAbove}";
                if (EdgeFromParent != null)
                {
                    string edgeFromParentInfo = EdgeFromParent.parentNameWithActionString(gameDefinition);
                    string parentAncestry = EdgeFromParent.parentNode.Ancestry(gameDefinition);
                    return parentAncestry + "\r\n" + edgeFromParentInfo + "\r\n" + thisLevel;
                }
                return thisLevel;
            }

            public string NodePlayerString(GameDefinition gameDefinition)
            {
                if (anyNode.IsChanceNode)
                    return "C";
                if (anyNode.IsUtilitiesNode)
                    return "";
                return gameDefinition.Players[anyNode.Decision.PlayerIndex].PlayerName + anyNode.GetInformationSetNodeNumber();
            }
            public string MainNodeText()
            {
                if (anyNode.IsUtilitiesNode)
                    return "(" + string.Join(", ", anyNode.GetNodeValues().Select(x => x.ToDecimalPlaces(2))) + ")";
                return anyNode.Decision.Name;
            }
        }

        public record EdgeInfo(GamePointNode parentNode, byte action, bool parentIncludedInDiagram, int parentLevel)
        {
            public byte parentDecisionByteCode => parentNode.anyNode.Decision.DecisionByteCode;
            public string parentName => parentNode.anyNode.Decision.Name;
            public string parentNameWithActionString(GameDefinition gameDefinition) => parentName + ": " + gameDefinition.GetActionString(action, parentDecisionByteCode);

            public string probabilityString => parentName == "Accident" ? probabilityStringScientificNotation : probabilityStringUsual; // This is a special hack for the precaution negligence game

            public string probabilityStringUsual => "Probability: " + parentNode.anyNode.GetNodeValues()[action - 1] switch
            {
                1.0 => "1",
                0 => "0",
                _ => parentNode.anyNode.GetNodeValues()[action - 1].ToDecimalPlaces(2)
            };

            public string probabilityStringScientificNotation
            {
                get
                {
                    double value = parentNode.anyNode.GetNodeValues()[action - 1];
                    if (0.999 < value && value < 1.0001) // number would round off to 1 but we need more precision
                    {
                        if (value < 1)
                        {
                            double amountBelowZero = 1.0 - value;
                            return "Pr.: 1 -- " + amountBelowZero.ToSignificantFigures_WithSciNotationForVerySmall_LaTeX(3);
                        }
                        else if (value > 1)
                        {
                            double amountAboveZero = value - 1.0;
                            return "Pr.: 1 + " + amountAboveZero.ToSignificantFigures_WithSciNotationForVerySmall_LaTeX(3);
                        }
                    }
                    string s = value.ToSignificantFigures_WithSciNotationForVerySmall_LaTeX(3);
                    return "Pr.: " + s;
                }
            }
        }

        public record ForwardInfo(MoveProbabilityTracker<(byte decisionByteCode, byte move)> moveProbabilities, double reachProbability)
        {
        }

        public ConstructGameTreeInformationSetInfo(GameDefinition gameDefinition)
        {
            GameDefinition = gameDefinition;
        }

        public void CollectTreeInfo(List<Decision> decisions, bool print)
        {
            void Write(string s)
            {
                if (print)
                    TabbedText.WriteLine(s);
            }
            Stack<EdgeInfo> edgeFromParentStack = new Stack<EdgeInfo>();
            TreeRoot.ExecuteActions((gamePointNode) =>
            {
                EdgeInfo edgeFromParent = null;
                if (edgeFromParentStack.Any())
                {
                    edgeFromParent = edgeFromParentStack.Pop();
                    edgeFromParent = edgeFromParent with { action = (byte)(edgeFromParent.action + 1) };
                    edgeFromParentStack.Push(edgeFromParent);
                }
                int nodeLevel = edgeFromParent == null ? 0 : edgeFromParent.parentLevel + 1;
                gamePointNode.EdgeFromParent = edgeFromParent;
                IAnyNode gameNode = gamePointNode.anyNode;
                double[] values = gameNode.GetNodeValues();
                if (edgeFromParent != null && values.Length > 1)
                    edgeFromParent.parentNode.Children.Add(gamePointNode);
                if (gameNode.IsUtilitiesNode)
                {
                    TabbedText.TabIndent();
                    if (edgeFromParent != null)
                        Write($"--- {edgeFromParent.parentName}: {GameDefinition.GetActionString(edgeFromParent.action, edgeFromParent.parentDecisionByteCode)} -->");
                    Write("Utilities: " + string.Join(",", values.Select(x => x.ToDecimalPlaces(2))));
                    edgeFromParentStack.Push(new EdgeInfo(gamePointNode, 0, true, nodeLevel)); // must push, so we can pop later, even though this won't be printed
                }
                else
                {
                    if (values.Length == 1)
                    { // skip this node -- and don't remember it
                        gamePointNode.IncludeInDiagram = false;
                        var previous = edgeFromParentStack.Peek();
                        edgeFromParentStack.Push(new EdgeInfo(previous.parentNode, 0, false, previous.parentLevel));
                    }
                    else
                    {
                        edgeFromParentStack.Push(new EdgeInfo(gamePointNode, 0, true, nodeLevel));
                        TabbedText.TabIndent();
                        if (edgeFromParent != null)
                            Write($"--- {edgeFromParent.parentName}: {GameDefinition.GetActionString(edgeFromParent.action, edgeFromParent.parentDecisionByteCode)} -->");
                        Write($"Decision: {gameNode.Decision.Name} (Information set {gameNode.GetInformationSetNodeNumber()})");
                        Write("Value probabilities: " + string.Join(",", values.Select(x => x.ToDecimalPlaces(2))));
                        Write($"Game point reach probability: {gamePointNode.gamePointReachProbability.ToDecimalPlaces(2)}");
                        var otherMoves = GetProbabilitiesOfOtherInformationSetMoves(!gameNode.IsChanceNode, gameNode.GetInformationSetNodeNumber(), decisions);
                        foreach (var entry in otherMoves.OrderBy(x => x.Key))
                        {
                            Write($"{entry.Key}: {string.Join(",", entry.Value.Select(x => x.ToDecimalPlaces(2)))}");
                        }
                    }
                }
            },
            (gamePointNode) =>
            {
                IAnyNode gameNode = gamePointNode.anyNode;
                EdgeInfo parentInfo = null;
                if (edgeFromParentStack.Any())
                    parentInfo = edgeFromParentStack.Pop();
                if (parentInfo?.parentIncludedInDiagram == true)
                    TabbedText.TabUnindent();
            });
        }

        public string GenerateTikzDiagram(Func<GamePointNode, bool> excludeBelowNode, Func<GamePointNode, bool> includeBelowNode)
        {
            const double xSpaceForNode = 6.0, ySpaceForLeaf = 0.5, circleSize = 0.4, straightAdjacentArrow = 0.4, utilitiesShiftRight = -0.45;
            StringBuilder b = new StringBuilder();
            bool onlyIncludableRegionFound = false;
            TreeRoot.ExecuteActionsOnTree(gamePointNode =>
            {
                if (excludeBelowNode != null && excludeBelowNode(gamePointNode.StoredValue))
                {
                    gamePointNode.StoredValue.ExcludeBelow = true;
                }
                if (gamePointNode.Parent?.StoredValue?.ExcludeBelow == true || gamePointNode.Parent?.StoredValue?.ExcludedFromAbove == true)
                {
                    gamePointNode.StoredValue.ExcludedFromAbove = true;
                }
                if (includeBelowNode != null && !onlyIncludableRegionFound && includeBelowNode(gamePointNode.StoredValue))
                {
                    gamePointNode.StoredValue.IncludeInRestrictedBelow = true; // we are making a diagram near the leaves of the tree. Only this section of the diagram will be included
                    onlyIncludableRegionFound = true;
                }
                if (gamePointNode.Parent?.StoredValue?.IncludeInRestrictedBelow == true || gamePointNode.Parent?.StoredValue?.IncludeInRestrictedFromAbove == true)
                {
                    gamePointNode.StoredValue.IncludeInRestrictedFromAbove = true;
                }
            },
            gamePointNode => { }
            );
            int tikzIndex = 0;
            bool done = false;
            do
            {
                done = true;
                TreeRoot.ExecuteActionsOnTree(gamePointNode => { }, gamePointNode =>
                {
                    GamePointNode storedValue = gamePointNode.StoredValue;
                    if (!storedValue.IncludeInDiagram)
                        return;
                    if (storedValue.ExcludedFromAbove)
                        return;
                    if (onlyIncludableRegionFound && !storedValue.IncludeInRestrictedBelow && !storedValue.IncludeInRestrictedFromAbove)
                        return;
                    if (storedValue.Children.Any(x => x.YRangeStart != x.YRangeEnd)) // i.e., has initialized children
                    {
                        for (int i = 1; i < storedValue.Children.Count; i++)
                        {
                            var c0 = storedValue.Children[i - 1];
                            var c1 = storedValue.Children[i];
                            if (c1.YRangeStart > c0.YRangeEnd - ySpaceForLeaf)
                            {
                                done = false;
                                double distanceDown = c1.YRangeStart - (c0.YRangeEnd - ySpaceForLeaf);
                                var gamePointNodeInternal = (NWayTreeStorageInternal<GamePointNode>)gamePointNode;
                                gamePointNodeInternal.Branches[i].ExecuteActions(gamePointNode2 => { }, gamePointNode2 =>
                                {
                                    if (gamePointNode2.Children.Any())
                                    {

                                        gamePointNode2.YRangeStart = gamePointNode2.Children.Max(c => c.YRangeStart);
                                        gamePointNode2.YRangeEnd = gamePointNode2.Children.Min(c => c.YRangeEnd);
                                    }
                                    else
                                    {
                                        gamePointNode2.YRangeStart -= distanceDown;
                                        gamePointNode2.YRangeEnd = gamePointNode2.YRangeStart - ySpaceForLeaf;
                                    }
                                });
                            }
                        }
                        storedValue.YRangeStart = storedValue.Children.Max(c => c.YRangeStart);
                        storedValue.YRangeEnd = storedValue.Children.Min(c => c.YRangeEnd);
                        double childrenXLocation = storedValue.Children.Min(x => x.XLocation);
                        foreach (var child in storedValue.Children)
                            child.XLocation = childrenXLocation;
                        storedValue.XLocation = childrenXLocation - xSpaceForNode;
                    }
                    else
                    {
                        // start this at the top -- then it will be moved down
                        if (storedValue.YRangeStart == storedValue.YRangeEnd)
                        {
                            storedValue.YRangeStart = 0;
                            storedValue.YRangeEnd = -ySpaceForLeaf;
                        }
                    }
                }
                );
            }
            while (!done);

            var includedNodes = TreeRoot.EnumerateNodes().Select(x => x.StoredValue).Where(x => x.IncludeInDiagram && !x.ExcludedFromAbove && (!onlyIncludableRegionFound || x.IncludeInRestrictedFromAbove)).ToList();
            foreach (var node in includedNodes)
            {
                string arrow = null;
                bool isUtilitiesNode = node.anyNode.IsUtilitiesNode;
                StringBuilder nodeStringBuilder = new StringBuilder();
                nodeStringBuilder.AppendLine($@"
    \draw[color=black] ({node.XLocation}, {node.YLocation}) {(isUtilitiesNode ? "" : $"circle ({circleSize}cm) ")}node[draw=none] (N{tikzIndex}) {{{node.NodePlayerString(GameDefinition)}}};");
                if (isUtilitiesNode)
                    nodeStringBuilder.AppendLine($@"\node[draw=none, right={utilitiesShiftRight}cm of N{tikzIndex}] {{{node.MainNodeText()}}};");
                bool excludingBelow = node.ExcludeBelow && node.IncludeInDiagram;
                if (excludingBelow)
                    nodeStringBuilder.AppendLine($@"\node[draw=none, right=0cm of N{tikzIndex},font=\huge] {{...}};");

                if (node.EdgeFromParent != null)
                {
                    arrow = $@"\draw ({node.EdgeFromParent.parentNode.XLocation + circleSize}, {node.EdgeFromParent.parentNode.YLocation}) -- ({node.EdgeFromParent.parentNode.XLocation + circleSize + straightAdjacentArrow}, {node.EdgeFromParent.parentNode.YLocation}) -- ({node.EdgeFromParent.parentNode.XLocation + circleSize + straightAdjacentArrow}, {node.YLocation}) -- ({node.XLocation - circleSize}, {node.YLocation}) node [midway, above, sloped] (E{tikzIndex++}) {{{node.EdgeFromParent.parentNameWithActionString(GameDefinition)}}} node [midway, below, sloped] (E{tikzIndex++}) {{{node.EdgeFromParent.probabilityString}}} ;
";
                    //                    arrow = $@"\draw ({node.EdgeFromParent.parentNode.XLocation + circleSize}, {node.EdgeFromParent.parentNode.YLocation}) -- ({node.EdgeFromParent.parentNode.XLocation + circleSize + straightAdjacentArrow}, {node.EdgeFromParent.parentNode.YLocation}) -- ({node.XLocation - circleSize - straightAdjacentArrow}, {node.YLocation}) node [midway, above, sloped] (E{tikzIndex++}) {{{node.EdgeFromParent.parentName}: {GameDefinition.GetActionString(node.EdgeFromParent.action, node.EdgeFromParent.parentDecisionByteCode)}}} -- ({node.XLocation - circleSize}, {node.YLocation});
                    //";
                    nodeStringBuilder.AppendLine(arrow);
                }
                b.Append(nodeStringBuilder.ToString());
                // here is where we would put text under node if desired
                //                b.AppendLine($@"\node[draw=none, below=0cm of N{tikzIndex}] {{
                //\begin{{tabular}}{{c}}
                //{node.MainNodeText()} \\
                //\end{{tabular}}
                //}};
                //{arrow}");
            }
            string tikzDocument = TikzHelper.GetStandaloneDocument(b.ToString(), additionalTikzLibraries: new List<string>() { "shapes.geometric" });
            return tikzDocument;
        }

        public Dictionary<string, double[]> GetProbabilitiesOfOtherInformationSetMoves(bool nonChancePlayer, int atNodeNumber, List<Decision> decisions)
        {
            Dictionary<string, double[]> d = new Dictionary<string, double[]>();
            foreach (var decision in decisions)
            {
                double[] probabilities = GetProbabilitiesOfOtherInformationSetMoves(nonChancePlayer, atNodeNumber, decision);
                if (probabilities != null)
                    d[decision.Name] = probabilities;
            }
            d["Utilities"] = GetProbabilitiesOfUtilities(nonChancePlayer, atNodeNumber, NumNonChancePlayers);
            return d;
        }

        public double[] GetProbabilitiesOfOtherInformationSetMoves(bool nonChancePlayer, int atNodeNumber, Decision sourceDecision)
        {
            double[] results = new double[sourceDecision.NumPossibleActions];
            var statistics = StatisticsForInformationSets(nonChancePlayer);
            var statisticsAtNode = statistics.GetValueOrDefault(atNodeNumber);
            if (statisticsAtNode == null)
                return null;
            for (byte a = 1; a <= sourceDecision.NumPossibleActions; a++)
                results[a - 1] = statisticsAtNode.GetWeight((sourceDecision.DecisionByteCode, a));
            double sum = results.Sum();
            return results.Select(x => x / sum).ToArray();
        }

        public double[] GetProbabilitiesOfUtilities(bool nonChancePlayer, int atNodeNumber, int numNonChancePlayers)
        {
            double[] results = new double[numNonChancePlayers];
            var statistics = StatisticsForInformationSets(nonChancePlayer);
            var statisticsAtNode = statistics.GetValueOrDefault(atNodeNumber);
            if (statisticsAtNode == null)
                return null;
            for (byte a = 1; a <= numNonChancePlayers; a++)
                results[a - 1] = statisticsAtNode.GetWeight((255, a));
            double sum = results.Sum();
            return results.Select(x => x / sum).ToArray();
        }

        public class MoveProbabilityTracker<T>
        {
            Dictionary<T, double> Values = new Dictionary<T, double>();


            public MoveProbabilityTracker()
            {

            }
            public MoveProbabilityTracker(List<MoveProbabilityTracker<T>> laterMoves, double[] weights)
            {
                for (int i = 0; i < weights.Count(); i++)
                    Aggregate(laterMoves[i], weights[i]);
            }

            public MoveProbabilityTracker<T> CloneWithWeight(double w)
            {
                var tracker = new MoveProbabilityTracker<T>();
                foreach (var v in Values)
                    tracker.AddMove(v.Key, v.Value * w);
                return tracker;
            }

            public void Aggregate(MoveProbabilityTracker<T> other, double weight)
            {
                foreach (var v in other.Values)
                    AddMove(v.Key, v.Value * weight);
            }

            public void AddMove(T t, double weight)
            {
                if (!Values.ContainsKey(t))
                    Values[t] = 0;
                Values[t] += weight;
            }

            public double GetWeight(T t)
            {
                if (!Values.ContainsKey(t))
                    Values[t] = 0;
                return Values[t];
            }

            public double[] GetWeights()
            {
                return Values.OrderBy(x => x.Key).Select(x => x.Value).ToArray();
            }
        }

        private MoveProbabilityTracker<(byte decisionByteCode, byte move)> AddToTracker(bool nonChancePlayer, int nodeNumber, double reachProbability, MoveProbabilityTracker<(byte decisionByteCode, byte move)> toAddToTracker)
        {
            var tracker = nonChancePlayer ? StatisticsForNonChancePlayerNodes : StatisticsForChanceNodes;
            var moveProbabilityTracker = tracker.GetValueOrDefault(nodeNumber, new MoveProbabilityTracker<(byte decisionByteCode, byte move)>());
            moveProbabilityTracker.Aggregate(toAddToTracker, reachProbability);
            tracker[nodeNumber] = moveProbabilityTracker.CloneWithWeight(1.0);
            return moveProbabilityTracker;
        }

        private static double GetCumulativeReachProbability(double fromPredecessor, IGameState predecessor, byte predecessorAction)
        {
            double cumulativeProbability = fromPredecessor;
            if (predecessor == null)
                cumulativeProbability = 1.0;
            else if (predecessor is ChanceNode c)
                cumulativeProbability *= c.GetActionProbability(predecessorAction);
            else if (predecessor is InformationSetNode i)
                cumulativeProbability *= i.GetCurrentProbability(predecessorAction, false);
            return cumulativeProbability;
        }

        private ForwardInfo AnyNode_Forward(IAnyNode anyNode, IGameState predecessor, byte predecessorAction, ForwardInfo fromPredecessor)
        {
            double reachProbability = fromPredecessor == null ? 1.0 : GetCumulativeReachProbability(fromPredecessor.reachProbability, predecessor, predecessorAction);
            ProbabilitiesToNode.Push(reachProbability);

            AddNodeToTree(anyNode, predecessorAction, reachProbability);

            ProbabilityOfReachingInformationSet(!anyNode.IsChanceNode)[anyNode.GetInformationSetNodeNumber()] = ProbabilityOfReachingInformationSet(!anyNode.IsChanceNode).GetValueOrDefault(anyNode.GetInformationSetNodeNumber()) + reachProbability;
            MoveProbabilityTracker<(byte decisionByteCode, byte move)> toAddToTracker = fromPredecessor == null ? new MoveProbabilityTracker<(byte decisionByteCode, byte move)>() : fromPredecessor.moveProbabilities.CloneWithWeight(1.0);
            for (int a = 1; a <= anyNode.Decision.NumPossibleActions; a++)
            {
                toAddToTracker.AddMove((anyNode.Decision.DecisionByteCode, (byte)a), 1.0);
            }
            int nodeNumber = anyNode.GetInformationSetNodeNumber();
            return new ForwardInfo(AddToTracker(!anyNode.IsChanceNode, nodeNumber, reachProbability, toAddToTracker), reachProbability);
        }

        private void AddNodeToTree(IAnyNode anyNode, byte predecessorAction, double reachProbability)
        {
            NWayTreeStorageInternal<GamePointNode> treeNode = null;
            if (TreeRoot == null)
                treeNode = TreeRoot = new NWayTreeStorageInternal<GamePointNode>(null, anyNode.Decision.NumPossibleActions);
            else
            {
                var parentNode = ParentNodes.Peek();
                parentNode.SetBranch(predecessorAction, new NWayTreeStorageInternal<GamePointNode>(parentNode, anyNode.Decision?.NumPossibleActions ?? 0));
                treeNode = (NWayTreeStorageInternal<GamePointNode>)parentNode.GetBranch(predecessorAction);
            }
            treeNode.StoredValue = new GamePointNode(anyNode, reachProbability);

            if (!anyNode.IsUtilitiesNode)
                ParentNodes.Push(treeNode);
        }

        public ForwardInfo ChanceNode_Forward(ChanceNode chanceNode, IGameState predecessor, byte predecessorAction, int predecessorDistributorChanceInputs, ForwardInfo fromPredecessor, int distributorChanceInputs) => AnyNode_Forward(chanceNode, predecessor, predecessorAction, fromPredecessor);

        public ForwardInfo InformationSet_Forward(InformationSetNode informationSet, IGameState predecessor, byte predecessorAction, int predecessorDistributorChanceInputs, ForwardInfo fromPredecessor) => AnyNode_Forward(informationSet, predecessor, predecessorAction, fromPredecessor);

        public MoveProbabilityTracker<(byte decisionByteCode, byte move)> FinalUtilities_TurnAround(FinalUtilitiesNode finalUtilities, IGameState predecessor, byte predecessorAction, int predecessorDistributorChanceInputs, ForwardInfo fromPredecessor)
        {
            double reachProbability = GetCumulativeReachProbability(fromPredecessor.reachProbability, predecessor, predecessorAction);
            AddNodeToTree(finalUtilities, predecessorAction, reachProbability);
            var toReturn = new MoveProbabilityTracker<(byte decisionByteCode, byte move)>();
            NumNonChancePlayers = (byte)finalUtilities.Utilities.Count();
            for (int i = 0; i < NumNonChancePlayers; i++)
                toReturn.AddMove((255, (byte)(i + 1)), finalUtilities.Utilities[i]);
            return toReturn;
        }

        private MoveProbabilityTracker<(byte decisionByteCode, byte move)> AnyNode_Backward(IAnyNode node, IEnumerable<MoveProbabilityTracker<(byte decisionByteCode, byte move)>> fromSuccessors)
        {
            ParentNodes.Pop();
            double reachProbability = ProbabilitiesToNode.Pop();
            var probabilitiesFromHere = node.GetNodeValues();
            MoveProbabilityTracker<(byte decisionByteCode, byte move)> toAddToTracker = new MoveProbabilityTracker<(byte decisionByteCode, byte move)>(fromSuccessors.ToList(), probabilitiesFromHere);
            for (int a = 1; a <= node.Decision.NumPossibleActions; a++)
            {
                toAddToTracker.AddMove((node.Decision.DecisionByteCode, (byte)a), probabilitiesFromHere[a - 1]);
            }
            int nodeNumber = node.GetInformationSetNodeNumber();
            return AddToTracker(!node.IsChanceNode, nodeNumber, reachProbability, toAddToTracker);
        }

        public MoveProbabilityTracker<(byte decisionByteCode, byte move)> ChanceNode_Backward(ChanceNode chanceNode, IEnumerable<MoveProbabilityTracker<(byte decisionByteCode, byte move)>> fromSuccessors, int distributorChanceInputs) => AnyNode_Backward(chanceNode, fromSuccessors);

        public MoveProbabilityTracker<(byte decisionByteCode, byte move)> InformationSet_Backward(InformationSetNode informationSet, IEnumerable<MoveProbabilityTracker<(byte decisionByteCode, byte move)>> fromSuccessors) => AnyNode_Backward(informationSet, fromSuccessors);
    }
}
