using ACESimBase.GameSolvingSupport.ExactValues;
using ACESimBase.Util.Debugging;
using JetBrains.Annotations;
using Microsoft.FSharp.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESimBase.GameSolvingSupport.GameTree
{
    public class CalculateUtilitiesAtEachInformationSet_MaybeExact<T> : ITreeNodeProcessor<IMaybeExact<T>, IMaybeExact<T>[]> where T : IMaybeExact<T>, new()
    {
        Stack<IMaybeExact<T>> probabilitiesToInformationSet = new Stack<IMaybeExact<T>>();
        Dictionary<int, IMaybeExact<T>> ProbabilityOfReachingInformationSet = new Dictionary<int, IMaybeExact<T>>();
        Dictionary<int, IMaybeExact<T>[]> WeightedUtilitiesAtInformationSet = new Dictionary<int, IMaybeExact<T>[]>();
        Dictionary<int, List<IMaybeExact<T>[]>> WeightedUtilitiesAtInformationSetSuccessors = new Dictionary<int, List<IMaybeExact<T>[]>>();
        Dictionary<int, IMaybeExact<T>[]> ChanceProbabilities;
        Dictionary<(int playerIndex, int nodeIndex), IMaybeExact<T>[]> PlayerProbabilities;
        Dictionary<int, IMaybeExact<T>[]> Utilities;
        HashSet<int> InformationSetNodeNumbers = new HashSet<int>();
        IMaybeExact<T> ErrorTolerance;

        public CalculateUtilitiesAtEachInformationSet_MaybeExact(Dictionary<int, IMaybeExact<T>[]> chanceProbabilities, Dictionary<(int playerIndex, int nodeIndex), IMaybeExact<T>[]> playerProbabilities, Dictionary<int, IMaybeExact<T>[]> utilities, IMaybeExact<T> errorTolerance)
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
                IMaybeExact<T>[] actionProbabilities = PlayerProbabilities[(informationSetNode.PlayerIndex, informationSetNode.GetInformationSetNodeNumber())];
                IMaybeExact<T> actionProbability = actionProbabilities[successorIndex];
                if (!actionProbability.IsZero())
                { // this is an action played with positive probability
                    IMaybeExact<T> utility = utilities[i];
                    IMaybeExact<T> utilityAtSuccessor = utilitiesAtSuccessors[successorIndex][i];
                    if (!utility.IsCloseTo(utilityAtSuccessor, ErrorTolerance))
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

        public (IMaybeExact<T>[] utilities, List<IMaybeExact<T>[]> utilitiesAtSuccessors, IMaybeExact<T> reachProbability) GetUtilitiesAndReachProbability(int informationSetNodeNumber)
        {
            IMaybeExact<T> reachProbability = ProbabilityOfReachingInformationSet[informationSetNodeNumber];
            var weightedUtilities = WeightedUtilitiesAtInformationSet[informationSetNodeNumber];
            var normalizedUtilities = weightedUtilities.Select(x => reachProbability.IsZero() ? x : x.DividedBy(reachProbability)).ToArray();
            var weightedUtilitiesAtSuccessors = WeightedUtilitiesAtInformationSetSuccessors[informationSetNodeNumber];
            var normalizedUtilitiesAtSuccessors = weightedUtilitiesAtSuccessors.Select(x => x.Select(y => reachProbability.IsZero() ? y : y.DividedBy(reachProbability)).ToArray()).ToList();
            return (normalizedUtilities, normalizedUtilitiesAtSuccessors, reachProbability);
        }

        public IMaybeExact<T> ChanceNode_Forward(ChanceNode chanceNode, IGameState predecessor, byte predecessorAction, int predecessorDistributorChanceInputs, IMaybeExact<T> fromPredecessor, int distributorChanceInputs)
        {
            return GetCumulativeReachProbability(fromPredecessor, predecessor, predecessorAction);
        }

        private IMaybeExact<T> GetCumulativeReachProbability(IMaybeExact<T> fromPredecessor, IGameState predecessor, byte predecessorAction)
        {
            IMaybeExact<T> cumulativeProbability = fromPredecessor;
            if (predecessor == null)
                cumulativeProbability = IMaybeExact<T>.One();
            else if (predecessor is ChanceNode c)
            {
                if (ChanceProbabilities.ContainsKey(c.ChanceNodeNumber))
                    cumulativeProbability = cumulativeProbability.Times(ChanceProbabilities[c.ChanceNodeNumber][predecessorAction - 1]);
                else
                    cumulativeProbability = IMaybeExact<T>.Zero(); // using SequenceFormCutOffProbabilityZeroNodes
            }
            else if (predecessor is InformationSetNode i)
                cumulativeProbability = cumulativeProbability.Times(PlayerProbabilities[(i.PlayerIndex, i.GetInformationSetNodeNumber())][predecessorAction - 1]);
            return cumulativeProbability;
        }

        public IMaybeExact<T> InformationSet_Forward(InformationSetNode informationSet, IGameState predecessor, byte predecessorAction, int predecessorDistributorChanceInputs, IMaybeExact<T> fromPredecessor)
        {
            InformationSetNodeNumbers.Add(informationSet.GetInformationSetNodeNumber());
            IMaybeExact<T> cumulativeProbability = GetCumulativeReachProbability(fromPredecessor, predecessor, predecessorAction);
            probabilitiesToInformationSet.Push(cumulativeProbability);
            ProbabilityOfReachingInformationSet[informationSet.GetInformationSetNodeNumber()] = ProbabilityOfReachingInformationSet.GetValueOrDefault(informationSet.GetInformationSetNodeNumber(), IMaybeExact<T>.Zero()).Plus(cumulativeProbability);
            return cumulativeProbability;
        }

        public IMaybeExact<T>[] InformationSet_Backward(InformationSetNode informationSet, IEnumerable<IMaybeExact<T>[]> fromSuccessors)
        {
            IMaybeExact<T> reachProbability = probabilitiesToInformationSet.Pop();
            List<IMaybeExact<T>> nextActionProbabilities = PlayerProbabilities[(informationSet.PlayerIndex, informationSet.GetInformationSetNodeNumber())].ToList();
            IMaybeExact<T>[] utilities = AggregateUtilitiesFromSuccessors(fromSuccessors, nextActionProbabilities);
            IMaybeExact<T>[] reachWeightedUtilities = utilities.Select(x => x.Times(reachProbability)).ToArray();
            WeightedUtilitiesAtInformationSet[informationSet.GetInformationSetNodeNumber()] =
                WeightedUtilitiesAtInformationSet.GetValueOrDefault(
                    informationSet.GetInformationSetNodeNumber(),
                    utilities.Select(x => IMaybeExact<T>.Zero()).ToArray())
                .Zip(reachWeightedUtilities, (x, y) => x.Plus(y)).ToArray();
            List<IMaybeExact<T>[]> prerecordedUtilitiesAtSuccessors = WeightedUtilitiesAtInformationSetSuccessors.GetValueOrDefault(
                    informationSet.GetInformationSetNodeNumber(),
                    fromSuccessors.Select(x => utilities.Select(x => IMaybeExact<T>.Zero()).ToArray()).ToList());
            var fromSuccessorsList = fromSuccessors.ToList();
            for (int s = 0; s < fromSuccessorsList.Count(); s++)
            {
                IMaybeExact<T>[] prerecordedUtilitiesAtSuccessor = prerecordedUtilitiesAtSuccessors[s];
                IMaybeExact<T>[] reachWeightedUtilitiesAtSuccessor = fromSuccessorsList[s].Select(x => x.Times(reachProbability)).ToArray();
                prerecordedUtilitiesAtSuccessors[s] = prerecordedUtilitiesAtSuccessor.Zip(reachWeightedUtilitiesAtSuccessor, (x, y) => x.Plus(y)).ToArray();
            }
            WeightedUtilitiesAtInformationSetSuccessors[informationSet.GetInformationSetNodeNumber()] = prerecordedUtilitiesAtSuccessors;
            return utilities;
        }

        public IMaybeExact<T>[] ChanceNode_Backward(ChanceNode chanceNode, IEnumerable<IMaybeExact<T>[]> fromSuccessors, int distributorChanceInputs)
        {
            if (!ChanceProbabilities.ContainsKey(chanceNode.ChanceNodeNumber))
            {
                // This is a zero probability path, so we can just return 0 utility. 
                return Enumerable.Range(0, fromSuccessors.First().Length).Select(x => IMaybeExact<T>.Zero()).ToArray();
            }
            List<IMaybeExact<T>> probabilities = ChanceProbabilities[chanceNode.ChanceNodeNumber].ToList();
            IMaybeExact<T>[] utilities = AggregateUtilitiesFromSuccessors(fromSuccessors, probabilities);
            return utilities;
        }

        private static IMaybeExact<T>[] AggregateUtilitiesFromSuccessors(IEnumerable<IMaybeExact<T>[]> fromSuccessors, List<IMaybeExact<T>> probabilities)
        {
            IMaybeExact<T>[] utilities = new IMaybeExact<T>[fromSuccessors.First().Length];
            for (int i = 0; i < utilities.Length; i++)
            {
                utilities[i] = IMaybeExact<T>.Zero();
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

        public IMaybeExact<T>[] FinalUtilities_TurnAround(FinalUtilitiesNode finalUtilities, IGameState predecessor, byte predecessorAction, int predecessorDistributorChanceInputs, IMaybeExact<T> fromPredecessor)
        {
            var rational = Utilities[finalUtilities.GetInformationSetNodeNumber()].ToArray();
            return rational;
        }
    }
}
