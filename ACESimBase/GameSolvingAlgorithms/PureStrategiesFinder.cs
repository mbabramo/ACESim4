using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace ACESim
{
    [Serializable]
    public class PureStrategiesFinder : StrategiesDeveloperBase
    {
        // Find pure equilibria:
        // 1. Fully initialize game tree
        // 2. For (P, D), enumerate information sets. Define a global strategy index that specifies a pure strategy in each information set for each player. 
        // 3. For each pair of information sets, set the pure strategy for each player, playing all chance strategies. Record the average resulting utilities.
        // 4. Using the matrix, eliminated dominated strategies. That is, for each column A, look to see if there is another column B that is always at least as good for column player. If so, eliminate A. Do same for rows (for row player). Repeat until a cycle produces no changes.


        public PureStrategiesFinder(List<Strategy> existingStrategyState, EvolutionSettings evolutionSettings, GameDefinition gameDefinition) : base(existingStrategyState, evolutionSettings, gameDefinition)
        {

        }

        public override IStrategiesDeveloper DeepCopy()
        {
            var created = new PureStrategiesFinder(Strategies, EvolutionSettings, GameDefinition);
            DeepCopyHelper(created);
            return created;
        }

        public override async Task<ReportCollection> RunAlgorithm(string optionSetName)
        {
            await FindPureStrategies();
            return new ReportCollection(); // currently results are just written to console. 
        }

        public async Task FindPureStrategies()
        {
            if (NumNonChancePlayers != 2)
                throw new NotImplementedException();
            List<(InformationSetNode, int)> player0InformationSets =
                Strategies[0].GetTallyNodes(GameDefinition);
            List<(InformationSetNode, int)> player1InformationSets =
                Strategies[1].GetTallyNodes(GameDefinition);
            GetUtilitiesForStrategyCombinations(player0InformationSets, player1InformationSets,
                out int player0Permutations, out int player1Permutations, out double[,] player0Utilities,
                out double[,] player1Utilities);
            List<(int player0Strategy, int player1Strategy)> nashEquilibria = ComputeNashEquilibria(player0Utilities, player1Utilities);
            await PrintAllEquilibriumStrategies(player0InformationSets, player1InformationSets, player0Permutations,
                player1Permutations, nashEquilibria);
        }

        public static List<(int player0Strategy, int player1Strategy)> ComputeNashEquilibria(double[,] player0Utilities, double[,] player1Utilities, bool removePayoffDominatedEquilibria=true)
        {
            int player0Permutations = player0Utilities.GetLength(0);
            int player1Permutations = player0Utilities.GetLength(1);
            bool[] player0StrategyEliminated = new bool[player0Permutations];
            bool[] player1StrategyEliminated = new bool[player1Permutations];
            EliminateDominatedStrategies(player0Utilities, player1Utilities,
                player0StrategyEliminated, player1StrategyEliminated);
            List<(int player0Strategy, int player1Strategy)> nashEquilibria =
                NarrowToNashEquilibria(player0Permutations, player1Permutations, player0Utilities, player1Utilities,
                    player0StrategyEliminated, player1StrategyEliminated, removePayoffDominatedEquilibria);
            return nashEquilibria;
        }

        private void PrintMatrix(double[,] arr)
        {
            int rowLength = arr.GetLength(0);
            int colLength = arr.GetLength(1);

            for (int i = 0; i < rowLength; i++)
            {
                for (int j = 0; j < colLength; j++)
                {
                    Debug.Write($"{arr[i, j]:N2} ");
                }
                Debug.Write(Environment.NewLine + Environment.NewLine);
            }
        }

        private async Task PrintAllEquilibriumStrategies(List<(InformationSetNode, int)> player0InformationSets,
            List<(InformationSetNode, int)> player1InformationSets, int player0Permutations,
            int player1Permutations, List<(int player0Strategy, int player1Strategy)> nashEquilibria)
        {
            int numPrinted = 0;
            for (int player0StrategyIndex = 0; player0StrategyIndex < player0Permutations; player0StrategyIndex++)
            {
                SetPureStrategyBasedOnIndex(player0InformationSets, player0StrategyIndex, player0Permutations);
                for (int player1StrategyIndex = 0; player1StrategyIndex < player1Permutations; player1StrategyIndex++)
                {
                    if (nashEquilibria.Any(x => x.player0Strategy == player0StrategyIndex &&
                                                x.player1Strategy == player1StrategyIndex))
                    {
                        SetPureStrategyBasedOnIndex(player1InformationSets, player1StrategyIndex, player1Permutations);
                        TabbedText.WriteLine(
                            $"Player0StrategyIndex {player0StrategyIndex} Player1StrategyIndex {player1StrategyIndex}");
                        await GenerateReports(0, () => "");
                        PrintGameTree();
                        TabbedText.WriteLine("");
                        numPrinted++;
                    }
                }
            }
            TabbedText.WriteLine($"Total equilibria: {numPrinted}");
        }

        private void GetUtilitiesForStrategyCombinations(List<(InformationSetNode, int)> player0InformationSets,
            List<(InformationSetNode, int)> player1InformationSets, out int player0Permutations,
            out int player1Permutations, out double[,] player0Utilities, out double[,] player1Utilities)
        {
            long p0P = player0InformationSets.Aggregate(1L, (acc, val) => acc * (long) val.Item2);
            long p1P = player1InformationSets.Aggregate(1L, (acc, val) => acc * (long) val.Item2);
            if (p0P == 0 || p1P == 0 || p0P > 10000000 || p1P > 10000000 || p0P * p1P > 10000000)
                throw new Exception("Too many combinations."); // note that aggregate will put 0 as overflow result
            player0Permutations = (int) p0P;
            player1Permutations = (int) p1P;
            player0Utilities = new double[player0Permutations, player1Permutations];
            player1Utilities = new double[player0Permutations, player1Permutations];
            for (int player0StrategyIndex = 0; player0StrategyIndex < player0Permutations; player0StrategyIndex++)
            {
                SetPureStrategyBasedOnIndex(player0InformationSets, player0StrategyIndex, player0Permutations);
                for (int player1StrategyIndex = 0; player1StrategyIndex < player1Permutations; player1StrategyIndex++)
                {
                    SetPureStrategyBasedOnIndex(player1InformationSets, player1StrategyIndex, player1Permutations);
                    double[] utils = GetAverageUtilities();
                    player0Utilities[player0StrategyIndex, player1StrategyIndex] = utils[0];
                    player1Utilities[player0StrategyIndex, player1StrategyIndex] = utils[1];
                }
            }
        }

        public static void EliminateDominatedStrategies(
            double[,] player0Utilities, double[,] player1Utilities, bool[] player0StrategyEliminated,
            bool[] player1StrategyEliminated)
        {
            int player0Permutations = player0Utilities.GetLength(0);
            int player1Permutations = player0Utilities.GetLength(1);
            bool atLeastOneEliminated = true;
            while (atLeastOneEliminated)
            {
                atLeastOneEliminated = EliminateDominatedStrategies(player0Permutations, player1Permutations,
                    (player0Index, player1Index) => player0Utilities[player0Index, player1Index],
                    player0StrategyEliminated, player1StrategyEliminated);
                atLeastOneEliminated = atLeastOneEliminated | EliminateDominatedStrategies(player1Permutations,
                                           player0Permutations,
                                           (player1Index, player0Index) => player1Utilities[player0Index, player1Index],
                                           player1StrategyEliminated, player0StrategyEliminated);
            }
        }

        public static bool EliminateDominatedStrategies(int thisPlayerPermutations, int otherPlayerPermutations,
            Func<int, int, double> getUtilityFn, bool[] thisPlayerStrategyEliminated,
            bool[] otherPlayerStrategyEliminated)
        {
            const bool requireStrictDominance = true;
            bool atLeastOneEliminated = false;
            // compare pairs of strategies by this player to see if one dominates the other
            bool doParallel = true;
            Parallelizer.Go(doParallel, 0, thisPlayerPermutations, thisPlayerStrategyIndex1 =>
            {
                for (int thisPlayerStrategyIndex2 = 0;
                    thisPlayerStrategyIndex2 < thisPlayerPermutations;
                    thisPlayerStrategyIndex2++)
                {
                    if (thisPlayerStrategyIndex1 == thisPlayerStrategyIndex2 ||
                        thisPlayerStrategyEliminated[thisPlayerStrategyIndex1] ||
                        thisPlayerStrategyEliminated[thisPlayerStrategyIndex2])
                        continue; // go to next pair to compare
                    bool index1SometimesBetter = false, index2SometimesBetter = false, sometimesEqual = false;
                    for (int opponentStrategyIndex = 0;
                        opponentStrategyIndex < otherPlayerPermutations;
                        opponentStrategyIndex++)
                    {
                        if (otherPlayerStrategyEliminated[opponentStrategyIndex])
                            continue;
                        double thisPlayerStrategyIndex1Utility =
                            getUtilityFn(thisPlayerStrategyIndex1, opponentStrategyIndex);
                        double thisPlayerStrategyIndex2Utility =
                            getUtilityFn(thisPlayerStrategyIndex2, opponentStrategyIndex);
                        if (thisPlayerStrategyIndex1Utility == thisPlayerStrategyIndex2Utility)
                        {
                            sometimesEqual = true;
                            if (requireStrictDominance)
                                break;
                        }
                        if (thisPlayerStrategyIndex1Utility > thisPlayerStrategyIndex2Utility)
                            index1SometimesBetter = true;
                        else
                            index2SometimesBetter = true;
                        if (index1SometimesBetter && index2SometimesBetter)
                            break;
                    }
                    if (requireStrictDominance && sometimesEqual)
                        continue; // we are not eliminating weakly dominant strategies
                    if (index1SometimesBetter && !index2SometimesBetter)
                    {
                        thisPlayerStrategyEliminated[thisPlayerStrategyIndex2] = true;
                        atLeastOneEliminated = true;
                    }
                    else if (!index1SometimesBetter && index2SometimesBetter)
                    {
                        atLeastOneEliminated = true;
                        thisPlayerStrategyEliminated[thisPlayerStrategyIndex1] = true;
                    }
                }
            });
            return atLeastOneEliminated;
        }

        public static (int player0Strategy, int player1Strategy) GetApproximateNashEquilibrium(double[,] player0Utilities, double[,] player1Utilities, out double nashDistance)
        {
            int numRows, numColumns;
            numRows = player0Utilities.GetLength(0);
            numColumns = player0Utilities.GetLength(1);
            double[,] DistanceFromNash;
            DistanceFromNash = GetApproximateNashEquilibriumValuesMatrix(player0Utilities, player1Utilities);
            int bestRow = -1;
            int bestCol = -1;
            nashDistance = double.MaxValue;
            for (int r = 0; r < numRows; r++)
                for (int c = 0; c < numColumns; c++)
                {
                    double distance = DistanceFromNash[r, c];
                    if (distance < nashDistance)
                    {
                        bestRow = r;
                        bestCol = c;
                        nashDistance = distance;
                    }
                }
            nashDistance = Math.Sqrt(nashDistance); // lowestTotal is sum of squares
            return (bestRow, bestCol);
        }

        public static double[,] GetApproximateNashEquilibriumValuesMatrix(double[,] player0Utilities, double[,] player1Utilities)
        {
            int numRows = player0Utilities.GetLength(0);
            int numColumns = player0Utilities.GetLength(1);
            double[] player0MaxValueInColumn = Enumerable.Range(0, numColumns).Select(c => Enumerable.Range(0, numRows).Select(r => player0Utilities[r, c]).Max()).ToArray();
            double[] player1MaxValueInRow = Enumerable.Range(0, numRows).Select(r => Enumerable.Range(0, numColumns).Select(c => player1Utilities[r, c]).Max()).ToArray();

            double[,] DistanceFromNash = new double[numRows, numColumns];
            double distanceFromNash(int r, int c)
            {
                double total = 0;
                if (player0Utilities[r, c] < player0MaxValueInColumn[c])
                    total += Math.Pow(player0Utilities[r, c] - player0MaxValueInColumn[c], 2);
                if (player1Utilities[r, c] < player1MaxValueInRow[r])
                    total += Math.Pow(player1Utilities[r, c] - player1MaxValueInRow[r], 2);
                return total;
            }
            for (int r = 0; r < numRows; r++)
                for (int c = 0; c < numColumns; c++)
                    DistanceFromNash[r, c] = distanceFromNash(r, c);
            return DistanceFromNash;
        }

        private static IEnumerable<(int player0Strategy, int player1Strategy)> GetAllStrategyPermutations(double[,] player0Utilities, double[,] player1Utilities)
        {
            int numRows = player0Utilities.GetLength(0);
            int numColumns = player0Utilities.GetLength(1);
            for (int r = 0; r < numRows; r++)
                for (int c = 0; c < numColumns; c++)
                    yield return (r, c);
        }

        public static List<(int player0Strategy, int player1Strategy)> GetCorrelatedEquilibrium_OrderingByApproxNashValue(double[,] player0Utilities, double[,] player1Utilities)
        {
            double[,] matrix = GetApproximateNashEquilibriumValuesMatrix(player0Utilities, player1Utilities);
            Func<List<(int player0Strategy, int player1Strategy)>, List<(int player0Strategy, int player1Strategy)>, List<(int player0Strategy, int player1Strategy)>> ordering = (candidates, admittees) => candidates.OrderBy(x => matrix[x.player0Strategy, x.player1Strategy]).ToList();
            List<(int player0Strategy, int player1Strategy)> candidates = GetAllStrategyPermutations(player0Utilities, player1Utilities).ToList();
            var admittees = GetCorrelatedEquilibrium(player0Utilities, player1Utilities, ordering, candidates, false);
            return admittees;
        }

        public static List<(int player0Strategy, int player1Strategy)> GetCorrelatedEquilibrium_OrderingByFarthestDistanceFromAdmittees_StartingWithRandomStrategy(double[,] player0Utilities, double[,] player1Utilities, ConsistentRandomSequenceProducer randomizer)
        {
            int numRows = player0Utilities.GetLength(0);
            int numColumns = player0Utilities.GetLength(1);
            int player0Strategy = randomizer.NextInt(numRows);
            int player1Strategy = randomizer.NextInt(numColumns);
            return GetCorrelatedEquilibrium_OrderingByFarthestDistanceFromAdmittees(player0Utilities, player1Utilities, (player0Strategy, player1Strategy));
        }

        public static List<(int player0Strategy, int player1Strategy)> GetCorrelatedEquilibrium_OrderingByFarthestDistanceFromAdmittees(double[,] player0Utilities, double[,] player1Utilities, (int player0Strategy, int player1Strategy) initialStrategy)
        {
            double closestDistance((int player0Strategy, int player1Strategy) item, List<(int player0Strategy, int player1Strategy)> otherItems)
            {
                double distance((int player0Strategy, int player1Strategy) first, (int player0Strategy, int player1Strategy) second)
                {
                    double player0FirstUtility = player0Utilities[first.player0Strategy, first.player1Strategy];
                    double player0SecondUtility = player0Utilities[second.player0Strategy, second.player1Strategy];
                    double player0Difference = player0FirstUtility - player0SecondUtility;
                    double player1FirstUtility = player1Utilities[first.player0Strategy, first.player1Strategy];
                    double player1SecondUtility = player1Utilities[second.player0Strategy, second.player1Strategy];
                    double player1Difference = player1FirstUtility - player1SecondUtility;
                    return player0Difference * player0Difference + player1Difference * player1Difference;
                }
                return otherItems.Min(x => distance(x, item));
            }

            List<(int player0Strategy, int player1Strategy)> candidates = GetAllStrategyPermutations(player0Utilities, player1Utilities).ToList();
            candidates.Remove(initialStrategy);
            candidates.Insert(0, initialStrategy);
            Func<List<(int player0Strategy, int player1Strategy)>, List<(int player0Strategy, int player1Strategy)>, List<(int player0Strategy, int player1Strategy)>> ordering = (candidates, admittees) => admittees.Any() ? candidates.OrderByDescending(x => closestDistance(x, admittees)).ToList() : candidates;
            var admittees = GetCorrelatedEquilibrium(player0Utilities, player1Utilities, ordering, candidates, true);
            return admittees;
        }

        private static List<(int player0Strategy, int player1Strategy)> GetCorrelatedEquilibrium(double[,] player0Utilities, double[,] player1Utilities, Func<List<(int player0Strategy, int player1Strategy)>, List<(int player0Strategy, int player1Strategy)>, List<(int player0Strategy, int player1Strategy)>> ordering, List<(int player0Strategy, int player1Strategy)> candidates, bool reorderAfterAdmittance)
        {
            List<(int player0Strategy, int player1Strategy)> admittees = new List<(int player0Strategy, int player1Strategy)>();
            candidates = ordering(candidates, admittees);
            while (candidates.Any())
            {
                int numCandidatesProcessed = 0;
                foreach (var candidate in candidates)
                {
                    numCandidatesProcessed++;
                    bool admissible = true;
                    // Check for compatibility with EVERY existing admittee.
                    foreach (var admittee in admittees)
                    {
                        bool defect = !StrategiesAreCompatibleInCorrelatedEquilibrium(player0Utilities, player1Utilities, candidate, admittee);
                        if (defect)
                        {
                            admissible = false;
                            break;
                        }
                    }
                    if (admissible)
                    {
                        admittees.Add(candidate);
                        if (reorderAfterAdmittance)
                            break;
                    }
                }
                candidates = candidates.Skip(numCandidatesProcessed).ToList();
                if (reorderAfterAdmittance)
                    candidates = ordering(candidates, admittees);
            }
            return admittees;
        }

        // We have four things to worry about. Player 0 might defect from admittee to candidate or vice-versa, and same for Player 1.
        // In each of the four, we can see the equilibrium potentially being defected from on the right, so the left looks to see whether one player
        // can get a higher utility by switching to the strategy represented by the other strategy. 
        // Because we are looking for a correlated equilibrium, it doesn't matter whether the player might defect to some OTHER strategy not
        // admitted to the correlated equilibrium.
        private static bool StrategiesAreCompatibleInCorrelatedEquilibrium(double[,] player0Utilities, double[,] player1Utilities, (int player0Strategy, int player1Strategy) candidate, (int player0Strategy, int player1Strategy) admittee)
        {
            bool defect = player0Utilities[candidate.player0Strategy, admittee.player1Strategy] > player0Utilities[admittee.player0Strategy, admittee.player1Strategy] ||
                                    player0Utilities[admittee.player0Strategy, candidate.player1Strategy] > player0Utilities[candidate.player0Strategy, candidate.player1Strategy] ||
                                    player1Utilities[admittee.player0Strategy, candidate.player1Strategy] > player1Utilities[admittee.player0Strategy, admittee.player1Strategy] ||
                                    player1Utilities[candidate.player0Strategy, admittee.player1Strategy] > player1Utilities[candidate.player0Strategy, candidate.player1Strategy];
            return !defect;
        }

        public static double DistanceFromNash_SingleStrategy(int r, int c, double[,] player0Utilities, double[,] player1Utilities)
        {
            // This calculates the max value in column and row for the specific row and column, respectively. We don't use it above because such calculations would be repetitive.
            int numRows = player0Utilities.GetLength(0);
            int numColumns = player0Utilities.GetLength(1);
            double player0MaxValueInColumn = Enumerable.Range(0, numRows).Select(r => player0Utilities[r, c]).Max();
            double player1MaxValueInRow = Enumerable.Range(0, numColumns).Select(c => player1Utilities[r, c]).Max();
            double total = 0;
            if (player0Utilities[r, c] < player0MaxValueInColumn)
                total += Math.Pow(player0Utilities[r, c] - player0MaxValueInColumn, 2);
            if (player1Utilities[r, c] < player1MaxValueInRow)
                total += Math.Pow(player1Utilities[r, c] - player1MaxValueInRow, 2);
            return total;
        }

        private static List<(int player0Strategy, int player1Strategy)> NarrowToNashEquilibria(int player0Permutations,
            int player1Permutations, double[,] player0Utilities, double[,] player1Utilities,
            bool[] player0StrategyEliminated, bool[] player1StrategyEliminated, bool removePayoffDominatedEquilibria)
        {
            // Eliminate any strategy where a player could improve his score by changing strategies.
            List<int> player0Strategies = player0StrategyEliminated
                .Select((eliminated, index) => new {eliminated, index}).Where(x => !x.eliminated).Select(x => x.index)
                .ToList();
            int player0StrategiesMaxIndex = player0Strategies.Max();
            List<int> player1Strategies = player1StrategyEliminated
                .Select((eliminated, index) => new {eliminated, index}).Where(x => !x.eliminated).Select(x => x.index)
                .ToList();
            int player1StrategiesMaxIndex = player1Strategies.Max();

            double[] player1MaxUtilityGivenPlayer0Strategy = new double[player0StrategiesMaxIndex + 1];
            foreach (int player0Strategy in player0Strategies)
            {
                player1MaxUtilityGivenPlayer0Strategy[player0Strategy] = player1Strategies.Select(x => player1Utilities[player0Strategy, x]).Max();
            }
            double[] player0MaxUtilityGivenPlayer1Strategy = new double[player1StrategiesMaxIndex + 1];
            foreach (int player1Strategy in player1Strategies)
            {
                player0MaxUtilityGivenPlayer1Strategy[player1Strategy] = player0Strategies.Select(x => player0Utilities[x, player1Strategy]).Max();
            }

            List<(int player0Strategy, int player1Strategy)> candidates = player0Strategies
                .SelectMany(x => player1Strategies.Select(y => (x, y))).ToList();

            //double[] Payoff(int player0Strategy, int player1Strategy) => new double[] {player0Utilities[player0Strategy, player1Strategy], player1Utilities[player0Strategy, player1Strategy] };
            //bool IsPayoffDominant
            //((int player0Strategy, int player1Strategy) firstPayoffs,
            //    (int player0Strategy, int player1Strategy) secondPayoffs) => UtilitiesArePayoffDominant((player0Utilities[firstPayoffs.player0Strategy, firstPayoffs.player1Strategy], player1Utilities[firstPayoffs.player0Strategy, firstPayoffs.player1Strategy]), (player0Utilities[secondPayoffs.player0Strategy, secondPayoffs.player1Strategy], player1Utilities[secondPayoffs.player0Strategy, secondPayoffs.player1Strategy]));

            bool UtilitiesArePayoffDominant
            ((double player0Utils, double player1Utils) firstPayoffs,
                (double player0Utils, double player1Utils) secondPayoffs)
            {
                bool atLeastOneBetter = firstPayoffs.player0Utils > secondPayoffs.player0Utils || firstPayoffs.player1Utils > secondPayoffs.player1Utils;
                bool neitherWorse = firstPayoffs.player0Utils >= secondPayoffs.player0Utils && firstPayoffs.player1Utils >= secondPayoffs.player1Utils;
                return atLeastOneBetter && neitherWorse;
            }



            bool Player0WillChangeStrategy((int player0Strategy, int player1Strategy) candidate)
            {
                double p0CurrentUtility = player0Utilities[candidate.player0Strategy, candidate.player1Strategy];
                double bestAchievableUtility = player0MaxUtilityGivenPlayer1Strategy[candidate.player1Strategy];
                return bestAchievableUtility > p0CurrentUtility;
            }

            bool Player1WillChangeStrategy((int player0Strategy, int player1Strategy) candidate)
            {
                double p1CurrentUtility = player1Utilities[candidate.player0Strategy, candidate.player1Strategy];
                double bestAchievableUtility = player1MaxUtilityGivenPlayer0Strategy[candidate.player0Strategy];
                return bestAchievableUtility > p1CurrentUtility;
            }

            ;
            var nashEquilibria = candidates.Where(x => !Player0WillChangeStrategy(x) && !Player1WillChangeStrategy(x))
                .ToList();
            var nashEquilibriaUtils = nashEquilibria.Select((item, index) => ((player0Utilities[item.player0Strategy, item.player1Strategy], player1Utilities[item.player0Strategy, item.player1Strategy]), index)).ToArray(); // convert from int indices to utilities, along with index into original list. That way, we can work with utilities for a bit and then recover the original nash equilibria.
            if (removePayoffDominatedEquilibria)
            {
                // narrow to a set that aren't dominated by anything else
                var dominantSet = new HashSet<(double p0Util, double p1Util)>();
                foreach (var utils in nashEquilibriaUtils)
                {
                    if (!dominantSet.Contains(utils.Item1) && !dominantSet.Any(x => UtilitiesArePayoffDominant(x, utils.Item1)))
                        dominantSet.Add(utils.Item1);
                }
                // now look at the original equilibria. find all that are not dominated by anything in the set (and therefore are not dominated by any). 
                nashEquilibria = nashEquilibriaUtils.Where(x => !dominantSet.Any(y => UtilitiesArePayoffDominant(y, x.Item1))).Select(x => nashEquilibria[x.index]).ToList();
            }
            return nashEquilibria;
        }

        private void SetPureStrategyBasedOnIndex(List<(InformationSetNode tally, int numPossible)> tallies,
            int strategyIndex, int totalStrategyPermutations)
        {
            int cumulative = 1;
            foreach (var tally in tallies)
            {
                cumulative *= tally.numPossible;
                int q = totalStrategyPermutations / cumulative;
                int indexForThisDecision = 0;
                while (strategyIndex >= q)
                {
                    strategyIndex -= q;
                    indexForThisDecision++;
                }
                byte action = (byte) (indexForThisDecision + 1);
                tally.tally.SetActionToCertainty(action, (byte) tally.numPossible);
            }
        }
    }
}