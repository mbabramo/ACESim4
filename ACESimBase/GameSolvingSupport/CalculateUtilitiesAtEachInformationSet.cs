using ACESim;
using JetBrains.Annotations;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESimBase.GameSolvingSupport
{
    public class CalculateUtilitiesAtEachInformationSet : ITreeNodeProcessor<double, double[]>
    {
        Stack<double> probabilitiesToInformationSet = new Stack<double>();
        Dictionary<int, double> ProbabilityOfReachingInformationSet = new Dictionary<int, double>();
        Dictionary<int, double[]> WeightedUtilitiesAtInformationSet = new Dictionary<int, double[]>();
        Dictionary<int, List<double[]>> WeightedUtilitiesAtInformationSetSuccessors = new Dictionary<int, List<double[]>>();

        public void VerifyPerfectEquilibrium(List<InformationSetNode> informationSetNodes)
        {
            foreach (var informationSetNode in informationSetNodes)
                VerifyPerfectEquilibrium(informationSetNode);
        }

        public void VerifyPerfectEquilibrium(InformationSetNode informationSetNode)
        {
            int informationSetNodeNumber = informationSetNode.GetNodeNumber();
            var (utilities, utilitiesAtSuccessors, reachProbability) = GetUtilitiesAndReachProbability(informationSetNodeNumber);
            int i = informationSetNode.PlayerIndex;
            int numSuccessors = utilitiesAtSuccessors.Count();
            for (int successorIndex = 0; successorIndex < numSuccessors; successorIndex++)
            {
                double[] actionProbabilities = informationSetNode.GetCurrentProbabilitiesAsArray();
                double actionProbability = actionProbabilities[successorIndex];
                if (actionProbability != 0)
                { // this is an action played with positive probability
                    const double tolerance = 1E-5;
                    double absDifference = Math.Abs(utilities[i] - utilitiesAtSuccessors[successorIndex][i]);
                    if (absDifference > tolerance)
                        throw new Exception($"Information set {informationSetNodeNumber} Verification of equal utilities failed.");
                }
            }
        }

        public (double[] utilities, List<double[]> utilitiesAtSuccessors, double reachProbability) GetUtilitiesAndReachProbability(int informationSetNodeNumber)
        {
            double reachProbability = ProbabilityOfReachingInformationSet[informationSetNodeNumber];
            var weightedUtilities = WeightedUtilitiesAtInformationSet[informationSetNodeNumber];
            var normalizedUtilities = weightedUtilities.Select(x => x / reachProbability).ToArray();
            var weightedUtilitiesAtSuccessors = WeightedUtilitiesAtInformationSetSuccessors[informationSetNodeNumber];
            var normalizedUtilitiesAtSuccessors = weightedUtilitiesAtSuccessors.Select(x => x.Select(y => y / reachProbability).ToArray()).ToList();
            return (normalizedUtilities, normalizedUtilitiesAtSuccessors, reachProbability);
        }

        public double ChanceNode_Forward(ChanceNode chanceNode, IGameState predecessor, byte predecessorAction, int predecessorDistributorChanceInputs, double fromPredecessor, int distributorChanceInputs)
        {
            return GetCumulativeReachProbability(fromPredecessor, predecessor, predecessorAction);
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

        public double InformationSet_Forward(InformationSetNode informationSet, IGameState predecessor, byte predecessorAction, int predecessorDistributorChanceInputs, double fromPredecessor)
        {
            double cumulativeProbability = GetCumulativeReachProbability(fromPredecessor, predecessor, predecessorAction);
            probabilitiesToInformationSet.Push(cumulativeProbability);
            ProbabilityOfReachingInformationSet[informationSet.GetNodeNumber()] = ProbabilityOfReachingInformationSet.GetValueOrDefault(informationSet.GetNodeNumber()) + cumulativeProbability;
            return cumulativeProbability;
        }

        public double[] InformationSet_Backward(InformationSetNode informationSet, IEnumerable<double[]> fromSuccessors)
        {
            double reachProbability = probabilitiesToInformationSet.Pop();
            var nextActionProbabilities = informationSet.GetCurrentProbabilitiesAsArray().ToList();
            double[] utilities = AggregateUtilitiesFromSuccessors(fromSuccessors, nextActionProbabilities);
            double[] reachWeightedUtilities = utilities.Select(x => x * reachProbability).ToArray();
            WeightedUtilitiesAtInformationSet[informationSet.GetNodeNumber()] =
                WeightedUtilitiesAtInformationSet.GetValueOrDefault<int, double[]>(
                    informationSet.GetNodeNumber(), 
                    utilities.Select(x => (double) 0).ToArray())
                .Zip(reachWeightedUtilities, (x, y) => x + y).ToArray();
            List<double[]> prerecordedUtilitiesAtSuccessors = WeightedUtilitiesAtInformationSetSuccessors.GetValueOrDefault<int, List<double[]>>(
                    informationSet.GetNodeNumber(),
                    fromSuccessors.Select(x => utilities.Select(x => (double)0).ToArray()).ToList());
            var fromSuccessorsList = fromSuccessors.ToList();
            for (int s = 0; s < fromSuccessorsList.Count(); s++)
            {
                double[] prerecordedUtilitiesAtSuccessor = prerecordedUtilitiesAtSuccessors[s];
                double[] reachWeightedUtilitiesAtSuccessor = fromSuccessorsList[s].Select(x => x * reachProbability).ToArray();
                prerecordedUtilitiesAtSuccessors[s] = prerecordedUtilitiesAtSuccessor.Zip(reachWeightedUtilitiesAtSuccessor, (x, y) => x + y).ToArray();
            }
            WeightedUtilitiesAtInformationSetSuccessors[informationSet.GetNodeNumber()] = prerecordedUtilitiesAtSuccessors;
            return utilities;
        }

        public double[] ChanceNode_Backward(ChanceNode chanceNode, IEnumerable<double[]> fromSuccessors, int distributorChanceInputs)
        {
            var probabilities = chanceNode.GetActionProbabilities().ToList();
            double[] utilities = AggregateUtilitiesFromSuccessors(fromSuccessors, probabilities);
            return utilities;
        }

        private static double[] AggregateUtilitiesFromSuccessors(IEnumerable<double[]> fromSuccessors, List<double> probabilities)
        {
            double[] utilities = new double[fromSuccessors.First().Length];
            int j = 0;
            foreach (var utilitiesFromSuccessor in fromSuccessors)
            {
                for (int i = 0; i < utilities.Length; i++)
                    utilities[i] += utilitiesFromSuccessor[i] * probabilities[j];
                j++;
            }

            return utilities;
        }

        public double[] FinalUtilities_TurnAround(FinalUtilitiesNode finalUtilities, IGameState predecessor, byte predecessorAction, int predecessorDistributorChanceInputs, double fromPredecessor)
        {
            var utilities = finalUtilities.Utilities;
            return utilities;
        }
    }
}
