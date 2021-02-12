using ACESim;
using JetBrains.Annotations;
using Microsoft.FSharp.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESimBase.GameSolvingSupport
{
    public class CalculateUtilitiesAtEachInformationSet_MaybeExact<T> : ITreeNodeProcessor<MaybeExact<T>, MaybeExact<T>[]> where T : MaybeExact<T>, new()
    {
        Stack<MaybeExact<T>> probabilitiesToInformationSet = new Stack<MaybeExact<T>>();
        Dictionary<int, MaybeExact<T>> ProbabilityOfReachingInformationSet = new Dictionary<int, MaybeExact<T>>();
        Dictionary<int, MaybeExact<T>[]> WeightedUtilitiesAtInformationSet = new Dictionary<int, MaybeExact<T>[]>();
        Dictionary<int, List<MaybeExact<T>[]>> WeightedUtilitiesAtInformationSetSuccessors = new Dictionary<int, List<MaybeExact<T>[]>>();
        Dictionary<int, MaybeExact<T>[]> ChanceProbabilities;
        Dictionary<(int playerIndex, int nodeIndex), MaybeExact<T>[]> PlayerProbabilities;
        Dictionary<int, MaybeExact<T>[]> Utilities;
        HashSet<int> InformationSetNodeNumbers = new HashSet<int>();
        MaybeExact<T> ErrorTolerance;

        public CalculateUtilitiesAtEachInformationSet_MaybeExact(Dictionary<int, MaybeExact<T>[]> chanceProbabilities, Dictionary<(int playerIndex, int nodeIndex), MaybeExact<T>[]> playerProbabilities, Dictionary<int, MaybeExact<T>[]> utilities, MaybeExact<T> errorTolerance)
        {
            ChanceProbabilities = chanceProbabilities;
            PlayerProbabilities = playerProbabilities;
            Utilities = utilities;
            ErrorTolerance = errorTolerance;
        }

        public bool VerifyPerfectEquilibrium(List<InformationSetNode> informationSetNodes, bool throwOnFail)
        {
            bool perfect = true;
            foreach (var informationSetNode in informationSetNodes)
                perfect = perfect && VerifyPerfectEquilibrium(informationSetNode, throwOnFail);
            return perfect;
        }

        public bool VerifyPerfectEquilibrium(InformationSetNode informationSetNode, bool throwOnFail)
        {
            int informationSetNodeNumber = informationSetNode.GetInformationSetNodeNumber();
            var (utilities, utilitiesAtSuccessors, reachProbability) = GetUtilitiesAndReachProbability(informationSetNodeNumber);
            int i = informationSetNode.PlayerIndex;
            int numSuccessors = utilitiesAtSuccessors.Count();
            bool perfect = true;
            for (int successorIndex = 0; successorIndex < numSuccessors; successorIndex++)
            {
                MaybeExact<T>[] actionProbabilities = PlayerProbabilities[(informationSetNode.PlayerIndex, informationSetNode.GetInformationSetNodeNumber())];
                MaybeExact<T> actionProbability = actionProbabilities[successorIndex];
                if (!actionProbability.IsZero())
                { // this is an action played with positive probability
                    MaybeExact<T> utility = utilities[i];
                    MaybeExact<T> utilityAtSuccessor = utilitiesAtSuccessors[successorIndex][i];
                    if (!(utility.IsCloseTo(utilityAtSuccessor, ErrorTolerance)))
                    {
                        string matchFailure = $"Verification of equal utilities failed. {utility.AsDouble} != {utilityAtSuccessor.AsDouble} (i.e., {utility} != {utilityAtSuccessor}) at {informationSetNode}";
                        TabbedText.WriteLine(matchFailure);
                        if (throwOnFail)
                            throw new Exception(matchFailure);
                        perfect = false;
                    }
                }
            }
            return perfect;
        }

        public (MaybeExact<T>[] utilities, List<MaybeExact<T>[]> utilitiesAtSuccessors, MaybeExact<T> reachProbability) GetUtilitiesAndReachProbability(int informationSetNodeNumber)
        {
            MaybeExact<T> reachProbability = ProbabilityOfReachingInformationSet[informationSetNodeNumber];
            var weightedUtilities = WeightedUtilitiesAtInformationSet[informationSetNodeNumber];
            var normalizedUtilities = weightedUtilities.Select(x => reachProbability.IsZero() ? x : (x.DividedBy(reachProbability))).ToArray();
            var weightedUtilitiesAtSuccessors = WeightedUtilitiesAtInformationSetSuccessors[informationSetNodeNumber];
            var normalizedUtilitiesAtSuccessors = weightedUtilitiesAtSuccessors.Select(x => x.Select(y => reachProbability.IsZero() ? y : (y.DividedBy(reachProbability))).ToArray()).ToList();
            return (normalizedUtilities, normalizedUtilitiesAtSuccessors, reachProbability);
        }

        public MaybeExact<T> ChanceNode_Forward(ChanceNode chanceNode, IGameState predecessor, byte predecessorAction, int predecessorDistributorChanceInputs, MaybeExact<T> fromPredecessor, int distributorChanceInputs)
        {
            return GetCumulativeReachProbability(fromPredecessor, predecessor, predecessorAction);
        }

        private MaybeExact<T> GetCumulativeReachProbability(MaybeExact<T> fromPredecessor, IGameState predecessor, byte predecessorAction)
        {
            MaybeExact<T> cumulativeProbability = fromPredecessor;
            if (predecessor == null)
                cumulativeProbability = MaybeExact<T>.One();
            else if (predecessor is ChanceNode c)
                cumulativeProbability = cumulativeProbability.Times(ChanceProbabilities[c.ChanceNodeNumber][predecessorAction - 1]);
            else if (predecessor is InformationSetNode i)
                cumulativeProbability = cumulativeProbability.Times(PlayerProbabilities[(i.PlayerIndex, i.GetInformationSetNodeNumber())][predecessorAction - 1]);
            return cumulativeProbability;
        }

        public MaybeExact<T> InformationSet_Forward(InformationSetNode informationSet, IGameState predecessor, byte predecessorAction, int predecessorDistributorChanceInputs, MaybeExact<T> fromPredecessor)
        {
            InformationSetNodeNumbers.Add(informationSet.GetInformationSetNodeNumber());
            MaybeExact<T> cumulativeProbability = GetCumulativeReachProbability(fromPredecessor, predecessor, predecessorAction);
            probabilitiesToInformationSet.Push(cumulativeProbability);
            ProbabilityOfReachingInformationSet[informationSet.GetInformationSetNodeNumber()] = ProbabilityOfReachingInformationSet.GetValueOrDefault(informationSet.GetInformationSetNodeNumber(), MaybeExact<T>.Zero()).Plus(cumulativeProbability);
            return cumulativeProbability;
        }

        public MaybeExact<T>[] InformationSet_Backward(InformationSetNode informationSet, IEnumerable<MaybeExact<T>[]> fromSuccessors)
        {
            MaybeExact<T> reachProbability = probabilitiesToInformationSet.Pop();
            List<MaybeExact<T>> nextActionProbabilities = PlayerProbabilities[(informationSet.PlayerIndex, informationSet.GetInformationSetNodeNumber())].ToList();
            MaybeExact<T>[] utilities = AggregateUtilitiesFromSuccessors(fromSuccessors, nextActionProbabilities);
            MaybeExact<T>[] reachWeightedUtilities = utilities.Select(x => x.Times(reachProbability)).ToArray();
            WeightedUtilitiesAtInformationSet[informationSet.GetInformationSetNodeNumber()] =
                WeightedUtilitiesAtInformationSet.GetValueOrDefault<int, MaybeExact<T>[]>(
                    informationSet.GetInformationSetNodeNumber(),
                    utilities.Select(x => MaybeExact<T>.Zero()).ToArray())
                .Zip(reachWeightedUtilities, (x, y) => x.Plus(y)).ToArray();
            List<MaybeExact<T>[]> prerecordedUtilitiesAtSuccessors = WeightedUtilitiesAtInformationSetSuccessors.GetValueOrDefault<int, List<MaybeExact<T>[]>>(
                    informationSet.GetInformationSetNodeNumber(),
                    fromSuccessors.Select(x => utilities.Select(x => MaybeExact<T>.Zero()).ToArray()).ToList());
            var fromSuccessorsList = fromSuccessors.ToList();
            for (int s = 0; s < fromSuccessorsList.Count(); s++)
            {
                MaybeExact<T>[] prerecordedUtilitiesAtSuccessor = prerecordedUtilitiesAtSuccessors[s];
                MaybeExact<T>[] reachWeightedUtilitiesAtSuccessor = fromSuccessorsList[s].Select(x => x.Times(reachProbability)).ToArray();
                prerecordedUtilitiesAtSuccessors[s] = prerecordedUtilitiesAtSuccessor.Zip(reachWeightedUtilitiesAtSuccessor, (x, y) => x.Plus(y)).ToArray();
            }
            WeightedUtilitiesAtInformationSetSuccessors[informationSet.GetInformationSetNodeNumber()] = prerecordedUtilitiesAtSuccessors;
            return utilities;
        }

        public MaybeExact<T>[] ChanceNode_Backward(ChanceNode chanceNode, IEnumerable<MaybeExact<T>[]> fromSuccessors, int distributorChanceInputs)
        {
            var probabilities = ChanceProbabilities[chanceNode.ChanceNodeNumber].ToList();
            MaybeExact<T>[] utilities = AggregateUtilitiesFromSuccessors(fromSuccessors, probabilities);
            return utilities;
        }

        private static MaybeExact<T>[] AggregateUtilitiesFromSuccessors(IEnumerable<MaybeExact<T>[]> fromSuccessors, List<MaybeExact<T>> probabilities)
        {
            MaybeExact<T>[] utilities = new MaybeExact<T>[fromSuccessors.First().Length];
            for (int i = 0; i < utilities.Length; i++)
            {
                utilities[i] = MaybeExact<T>.Zero();
            }
            int j = 0;
            foreach (var utilitiesFromSuccessor in fromSuccessors)
            {
                for (int i = 0; i < utilities.Length; i++)
                {
                    utilities[i] = utilities[i].Plus(utilitiesFromSuccessor[i].Times(probabilities[j]));
                }
                j++;
            }

            return utilities;
        }

        public MaybeExact<T>[] FinalUtilities_TurnAround(FinalUtilitiesNode finalUtilities, IGameState predecessor, byte predecessorAction, int predecessorDistributorChanceInputs, MaybeExact<T> fromPredecessor)
        {
            var rational = Utilities[finalUtilities.GetInformationSetNodeNumber()].ToArray();
            return rational;
        }
    }
}
