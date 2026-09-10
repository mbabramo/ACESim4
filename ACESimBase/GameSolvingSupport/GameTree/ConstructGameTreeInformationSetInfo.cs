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

        /// <summary>
        /// Renders a fresh view on every call. A restricted subtree includes its root but not
        /// the incoming edge from the omitted portion of the tree.
        /// </summary>
        public string GenerateTikzDiagram(
            Func<GamePointNode, bool> excludeBelowNode,
            Func<GamePointNode, bool> includeBelowNode,
            bool includeBehavioralProbabilities = true,
            string caption = null,
            Func<EdgeInfo, string> edgeLabel = null,
            int payoffDecimalPlaces = 4)
        {
            if (payoffDecimalPlaces < 0 || payoffDecimalPlaces > 15)
                throw new ArgumentOutOfRangeException(nameof(payoffDecimalPlaces));
            var allNodes = TreeRoot.EnumerateNodes().Select(x => x.StoredValue).ToList();
            GamePointNode root = includeBelowNode == null
                ? allNodes.First(x => x.IncludeInDiagram)
                : allNodes.FirstOrDefault(x => x.IncludeInDiagram && includeBelowNode(x));
            if (root == null)
                throw new ArgumentException("No game-tree node matches the requested subtree.");

            const double xSpacing = 5.8, leafSpacing = 1.0, radius = 0.3;
            var visible = new List<GamePointNode>();
            var children = new Dictionary<GamePointNode, List<GamePointNode>>(ReferenceEqualityComparer.Instance);
            double nextLeaf = 0;
            void Layout(GamePointNode node, int depth)
            {
                node.TikzIndex = visible.Count;
                visible.Add(node);
                node.XLocation = depth * xSpacing;
                node.ExcludeBelow = excludeBelowNode?.Invoke(node) == true;
                var displayedChildren = node.ExcludeBelow
                    ? new List<GamePointNode>()
                    : node.Children.Where(x => x.IncludeInDiagram).ToList();
                children[node] = displayedChildren;
                foreach (var child in displayedChildren)
                    Layout(child, depth + 1);
                double y = displayedChildren.Count == 0
                    ? nextLeaf--
                    : (displayedChildren.First().YLocation + displayedChildren.Last().YLocation) / (2 * leafSpacing);
                node.YRangeStart = node.YRangeEnd = y * leafSpacing;
            }
            Layout(root, 0);
            string Number(double value) => value.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);
            string Escape(string value) => value.Replace(@"\", @"\textbackslash{}")
                .Replace("&", @"\&").Replace("%", @"\%").Replace("_", @"\_")
                .Replace("#", @"\#").Replace("$", @"\$");

            var b = new StringBuilder();
            // Separate node identities from edge identities (the old renderer could reuse N0).
            foreach (var node in visible)
            {
                string location = $"({Number(node.XLocation)}, {Number(node.YLocation)})";
                if (node.anyNode.IsUtilitiesNode)
                {
                    string utilities = "(" + string.Join(", ", node.anyNode.GetNodeValues().Select(
                        value => value.ToString("0." + new string('#', payoffDecimalPlaces),
                            System.Globalization.CultureInfo.InvariantCulture))) + ")";
                    b.AppendLine($@"\node[anchor=west, inner sep=0pt] (N{node.TikzIndex}) at {location} {{{utilities}}};");
                }
                else
                    b.AppendLine($@"\node[circle, draw, minimum size={Number(radius * 2)}cm, inner sep=0pt, font=\scriptsize] (N{node.TikzIndex}) at {location} {{{Escape(node.NodePlayerString(GameDefinition))}}};");
                if (node.ExcludeBelow && !node.anyNode.IsUtilitiesNode)
                    b.AppendLine($@"\node[anchor=west] at ({Number(node.XLocation + radius)}, {Number(node.YLocation)}) {{$\cdots$}};");
            }
            foreach (var parent in visible)
                foreach (var node in children[parent])
                {
                    var edge = node.EdgeFromParent;
                    string probability = "";
                    if (parent.anyNode.IsChanceNode || includeBehavioralProbabilities)
                    {
                        double value = parent.anyNode.GetNodeValues()[edge.action - 1];
                        // Preserve the rare-event formatting used by the endogenous model.
                        // In particular, do not label a possible accident as probability zero.
                        string label = edge.parentName == "Accident" || (value > 0 && value < 0.0005) ||
                            (value > 0.9995 && value < 1)
                            ? edge.probabilityStringScientificNotation
                            : "Pr.: " + value.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture);
                        probability = $@" node[midway, below, font=\small] {{{label}}}";
                    }
                    b.AppendLine($@"\draw[->] (N{parent.TikzIndex}.east) -- ({Number(parent.XLocation + 0.6)}, {Number(parent.YLocation)}) -- ({Number(parent.XLocation + 0.6)}, {Number(node.YLocation)}) -- (N{node.TikzIndex}.west) node[midway, above, font=\small] {{{Escape(edgeLabel?.Invoke(edge) ?? edge.parentNameWithActionString(GameDefinition))}}}{probability};");
                }
            if (!string.IsNullOrWhiteSpace(caption))
                b.AppendLine($@"\node[anchor=south west, align=left, text width={Number(Math.Max(17, visible.Max(x => x.XLocation)))}cm, font=\small] at (0, 0.8) {{{Escape(caption)}}};");
            return TikzHelper.GetStandaloneDocument(b.ToString())
                .Replace(@"\documentclass{standalone}", @"\documentclass[border=3pt]{standalone}");
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

        public ForwardInfo ChanceNode_Forward(ChanceNode chanceNode, IGameState predecessor, byte predecessorAction, ForwardInfo fromPredecessor) => AnyNode_Forward(chanceNode, predecessor, predecessorAction, fromPredecessor);

        public ForwardInfo InformationSet_Forward(InformationSetNode informationSet, IGameState predecessor, byte predecessorAction, ForwardInfo fromPredecessor) => AnyNode_Forward(informationSet, predecessor, predecessorAction, fromPredecessor);

        public MoveProbabilityTracker<(byte decisionByteCode, byte move)> FinalUtilities_TurnAround(FinalUtilitiesNode finalUtilities, IGameState predecessor, byte predecessorAction, ForwardInfo fromPredecessor)
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

        public MoveProbabilityTracker<(byte decisionByteCode, byte move)> ChanceNode_Backward(ChanceNode chanceNode, IEnumerable<MoveProbabilityTracker<(byte decisionByteCode, byte move)>> fromSuccessors) => AnyNode_Backward(chanceNode, fromSuccessors);

        public MoveProbabilityTracker<(byte decisionByteCode, byte move)> InformationSet_Backward(InformationSetNode informationSet, IEnumerable<MoveProbabilityTracker<(byte decisionByteCode, byte move)>> fromSuccessors) => AnyNode_Backward(informationSet, fromSuccessors);
    }
}
