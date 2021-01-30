using ACESim;
using JetBrains.Annotations;
using Rationals;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESimBase.GameSolvingSupport
{
    public class CalculateRationalUtilitiesAtEachInformationSet : ITreeNodeProcessor<Rational, Rational[]>
    {
        Stack<Rational> probabilitiesToInformationSet = new Stack<Rational>();
        Dictionary<int, Rational> ProbabilityOfReachingInformationSet = new Dictionary<int, Rational>();
        Dictionary<int, Rational[]> WeightedUtilitiesAtInformationSet = new Dictionary<int, Rational[]>();
        Dictionary<int, List<Rational[]>> WeightedUtilitiesAtInformationSetSuccessors = new Dictionary<int, List<Rational[]>>();
        Dictionary<int, Rational[]> ChanceProbabilities;
        Dictionary<(int playerIndex, int nodeIndex), Rational[]> PlayerProbabilities;
        HashSet<int> InformationSetNodeNumbers = new HashSet<int>();

        public CalculateRationalUtilitiesAtEachInformationSet(Dictionary<int, Rational[]> chanceProbabilities, Dictionary<(int playerIndex, int nodeIndex), Rational[]> playerProbabilities)
        {
            ChanceProbabilities = chanceProbabilities;
            PlayerProbabilities = playerProbabilities;
        }

        public void VerifyPerfectEquilibrium(List<InformationSetNode> informationSetNodes)
        {
            return; // DEBUG
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
                Rational[] actionProbabilities = PlayerProbabilities[(informationSetNode.PlayerIndex, informationSetNode.GetNodeNumber())];
                Rational actionProbability = actionProbabilities[successorIndex];
                if (actionProbability != 0)
                { // this is an action played with positive probability
                    if (utilities[i] != utilitiesAtSuccessors[successorIndex][i])
                        throw new Exception($"Information set {informationSetNodeNumber} Verification of equal utilities failed.");
                }
            }
        }

        public (Rational[] utilities, List<Rational[]> utilitiesAtSuccessors, Rational reachProbability) GetUtilitiesAndReachProbability(int informationSetNodeNumber)
        {
            Rational reachProbability = ProbabilityOfReachingInformationSet[informationSetNodeNumber].CanonicalForm;
            var weightedUtilities = WeightedUtilitiesAtInformationSet[informationSetNodeNumber];
            var normalizedUtilities = weightedUtilities.Select(x => reachProbability == 0 ? x : (x / reachProbability).CanonicalForm).ToArray();
            var weightedUtilitiesAtSuccessors = WeightedUtilitiesAtInformationSetSuccessors[informationSetNodeNumber];
            var normalizedUtilitiesAtSuccessors = weightedUtilitiesAtSuccessors.Select(x => x.Select(y => reachProbability == 0 ? y : (y / reachProbability).CanonicalForm).ToArray()).ToList();
            return (normalizedUtilities, normalizedUtilitiesAtSuccessors, reachProbability);
        }

        public Rational ChanceNode_Forward(ChanceNode chanceNode, IGameState predecessor, byte predecessorAction, int predecessorDistributorChanceInputs, Rational fromPredecessor, int distributorChanceInputs)
        {
            return GetCumulativeReachProbability(fromPredecessor, predecessor, predecessorAction);
        }

        private Rational GetCumulativeReachProbability(Rational fromPredecessor, IGameState predecessor, byte predecessorAction)
        {
            Rational cumulativeProbability = fromPredecessor;
            if (predecessor == null)
                cumulativeProbability = (Rational)1;
            else if (predecessor is ChanceNode c)
                cumulativeProbability *= ChanceProbabilities[c.ChanceNodeNumber][predecessorAction - 1];
            else if (predecessor is InformationSetNode i)
                cumulativeProbability *= PlayerProbabilities[(i.PlayerIndex, i.GetNodeNumber())][predecessorAction - 1];
            return cumulativeProbability.CanonicalForm;
        }

        public Rational InformationSet_Forward(InformationSetNode informationSet, IGameState predecessor, byte predecessorAction, int predecessorDistributorChanceInputs, Rational fromPredecessor)
        {
            InformationSetNodeNumbers.Add(informationSet.GetNodeNumber());
            Rational cumulativeProbability = GetCumulativeReachProbability(fromPredecessor, predecessor, predecessorAction);
            probabilitiesToInformationSet.Push(cumulativeProbability);
            ProbabilityOfReachingInformationSet[informationSet.GetNodeNumber()] = ProbabilityOfReachingInformationSet.GetValueOrDefault(informationSet.GetNodeNumber()) + cumulativeProbability;
            return cumulativeProbability.CanonicalForm;
        }

        public Rational[] InformationSet_Backward(InformationSetNode informationSet, IEnumerable<Rational[]> fromSuccessors)
        {
            Rational reachProbability = probabilitiesToInformationSet.Pop();
            List<Rational> nextActionProbabilities = PlayerProbabilities[(informationSet.PlayerIndex, informationSet.GetNodeNumber())].ToList();
            Rational[] utilities = AggregateUtilitiesFromSuccessors(fromSuccessors, nextActionProbabilities);
            Rational[] reachWeightedUtilities = utilities.Select(x => x * reachProbability).ToArray();
            WeightedUtilitiesAtInformationSet[informationSet.GetNodeNumber()] =
                WeightedUtilitiesAtInformationSet.GetValueOrDefault<int, Rational[]>(
                    informationSet.GetNodeNumber(),
                    utilities.Select(x => (Rational)0).ToArray())
                .Zip(reachWeightedUtilities, (x, y) => x + y).ToArray();
            List<Rational[]> prerecordedUtilitiesAtSuccessors = WeightedUtilitiesAtInformationSetSuccessors.GetValueOrDefault<int, List<Rational[]>>(
                    informationSet.GetNodeNumber(),
                    fromSuccessors.Select(x => utilities.Select(x => (Rational)0).ToArray()).ToList());
            var fromSuccessorsList = fromSuccessors.ToList();
            for (int s = 0; s < fromSuccessorsList.Count(); s++)
            {
                Rational[] prerecordedUtilitiesAtSuccessor = prerecordedUtilitiesAtSuccessors[s];
                Rational[] reachWeightedUtilitiesAtSuccessor = fromSuccessorsList[s].Select(x => x * reachProbability).ToArray();
                prerecordedUtilitiesAtSuccessors[s] = prerecordedUtilitiesAtSuccessor.Zip(reachWeightedUtilitiesAtSuccessor, (x, y) => x + y).ToArray();
            }
            WeightedUtilitiesAtInformationSetSuccessors[informationSet.GetNodeNumber()] = prerecordedUtilitiesAtSuccessors;
            return utilities;
        }

        public Rational[] ChanceNode_Backward(ChanceNode chanceNode, IEnumerable<Rational[]> fromSuccessors, int distributorChanceInputs)
        {
            var probabilities = ChanceProbabilities[chanceNode.ChanceNodeNumber].ToList();
            Rational[] utilities = AggregateUtilitiesFromSuccessors(fromSuccessors, probabilities);
            return utilities;
        }

        private static Rational[] AggregateUtilitiesFromSuccessors(IEnumerable<Rational[]> fromSuccessors, List<Rational> probabilities)
        {
            Rational[] utilities = new Rational[fromSuccessors.First().Length];
            int j = 0;
            foreach (var utilitiesFromSuccessor in fromSuccessors)
            {
                for (int i = 0; i < utilities.Length; i++)
                {
                    utilities[i] += utilitiesFromSuccessor[i] * probabilities[j];
                    utilities[i] = utilities[i].CanonicalForm;
                }
                j++;
            }

            return utilities;
        }

        public Rational[] FinalUtilities_TurnAround(FinalUtilitiesNode finalUtilities, IGameState predecessor, byte predecessorAction, int predecessorDistributorChanceInputs, Rational fromPredecessor)
        {
            var rational = finalUtilities.Utilities.Select(x => (Rational)((int)x)).ToArray();
            return rational;
        }
    }
}
