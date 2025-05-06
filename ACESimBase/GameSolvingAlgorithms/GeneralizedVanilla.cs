using ACESimBase.GameSolvingSupport;
using ACESimBase.Util.ArrayProcessing;
using ACESimBase.Util.Debugging;
using ACESimBase.Util.Parallelization;
using JetBrains.Annotations;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace ACESim
{
    [Serializable]
    public partial class GeneralizedVanilla : CounterfactualRegretMinimization
    {
        double AverageStrategyAdjustment, AverageStrategyAdjustmentAsPctOfMax;
        PostIterationUpdaterBase PostIterationUpdater;
        Dictionary<InformationSetNode, InformationSetNode> InformationSetSymmetryMap;
        public bool TakeShortcutInSymmetricGames = true;
        bool VerifySymmetry = false; // if true, symmetry is verified instead of used as a way of saving time

        public GeneralizedVanilla(List<Strategy> existingStrategyState, EvolutionSettings evolutionSettings, GameDefinition gameDefinition, PostIterationUpdaterBase postIterationUpdater) : base(existingStrategyState, evolutionSettings, gameDefinition)
        {
            PostIterationUpdater = postIterationUpdater;
        }

        #region Game state management

        public override IStrategiesDeveloper DeepCopy()
        {
            var created = new GeneralizedVanilla(Strategies, EvolutionSettings, GameDefinition, PostIterationUpdater);
            DeepCopyHelper(created);
            return created;
        }

        public void UpdateInformationSets(int iteration)
        {
            HandleSymmetry(iteration);

            int numInformationSets = InformationSets.Count;
            PostIterationUpdater.PrepareForUpdating(iteration, EvolutionSettings);
            double? pruneOpponentStrategyBelow = !EvolutionSettings.CFR_OpponentSampling && EvolutionSettings.PruneOnOpponentStrategy && !TakeShortcutInSymmetricGames && !EvolutionSettings.PredeterminePrunabilityBasedOnRelativeContributions ? EvolutionSettings.PruneOnOpponentStrategyThreshold : (double?)null;
            bool predeterminePrunability = !EvolutionSettings.CFR_OpponentSampling && EvolutionSettings.PruneOnOpponentStrategy && !TakeShortcutInSymmetricGames && EvolutionSettings.PredeterminePrunabilityBasedOnRelativeContributions;

            Func<int, double?> randomNumberToSelectSingleOpponentAction = n => null;
            if (EvolutionSettings.CFR_OpponentSampling)
                randomNumberToSelectSingleOpponentAction = n => (new Random(iteration * 997 + n * 283 + GameNumber * 719)).NextDouble();

            if (EvolutionSettings.SimulatedAnnealing_UseRandomAverageStrategyAdjustment)
            {
                Parallel.For(0, numInformationSets, n => InformationSets[n].PostIterationUpdates(iteration, PostIterationUpdater, EvolutionSettings.SimulatedAnnealing_RandomAverageStrategyAdjustment(iteration, InformationSets[n]), false, false, 1.0, pruneOpponentStrategyBelow, predeterminePrunability, EvolutionSettings.GeneralizedVanillaAddTremble, EvolutionSettings.Algorithm == GameApproximationAlgorithm.RegretMatching && EvolutionSettings.CFR_OpponentSampling, randomNumberToSelectSingleOpponentAction(n)));
                return;
            }

            bool normalizeCumulativeStrategyIncrements = false;
            bool resetPreviousCumulativeStrategyIncrements = false;

            if (EvolutionSettings.UseStandardDiscounting)
            {
                if (EvolutionSettings.UseContinuousRegretsDiscounting)
                    throw new Exception("Can't use both discounting approaches together.");
                int maxIterationToDiscount = EvolutionSettings.StopDiscountingAtIteration;
                if (iteration < maxIterationToDiscount || EvolutionSettings.DiscountingTarget_ConstantAfterProportionOfIterations == 1.0)
                {
                    normalizeCumulativeStrategyIncrements = true;
                    resetPreviousCumulativeStrategyIncrements = false;
                }
                else if (iteration == maxIterationToDiscount)
                {
                    normalizeCumulativeStrategyIncrements = false;
                    resetPreviousCumulativeStrategyIncrements = true;
                }
                else
                {
                    normalizeCumulativeStrategyIncrements = false;
                    resetPreviousCumulativeStrategyIncrements = false;
                }
            }

            double averageStrategyAdjustment = EvolutionSettings.UseStandardDiscounting ? AverageStrategyAdjustment : 1.0;
            double continuousRegretsDiscountingAdjustment = EvolutionSettings.ContinuousRegretsDiscountPerIteration;

            Parallelizer.Go(EvolutionSettings.ParallelOptimization, 0, numInformationSets, n => InformationSets[n].PostIterationUpdates(iteration, PostIterationUpdater, averageStrategyAdjustment, normalizeCumulativeStrategyIncrements, resetPreviousCumulativeStrategyIncrements, continuousRegretsDiscountingAdjustment, pruneOpponentStrategyBelow, predeterminePrunability, EvolutionSettings.GeneralizedVanillaAddTremble, EvolutionSettings.Algorithm == GameApproximationAlgorithm.RegretMatching && EvolutionSettings.CFR_OpponentSampling, randomNumberToSelectSingleOpponentAction(n)));
        }

        private void PrintSpecfiedInformationSets(int iteration)
        {
            if (iteration != 1 && (iteration < 6950 || iteration > 7000) & !(iteration % 25 == 0))
                return;
            int pDamagesSignal = GameDefinition.DecisionPointsExecutionOrder.Select((item, index) => (item, index)).First(x => x.item.Name == "PlaintiffDamagesSignal").index;
            int pOffer2Index = GameDefinition.DecisionPointsExecutionOrder.Select((item, index) => (item, index)).First(x => x.item.Name == "PlaintiffOffer2").index;
            var matchingSets = InformationSets.Where(x => x.DecisionIndex == pOffer2Index && x.LabeledInformationSet.Any(x => x.decisionIndex == pDamagesSignal && x.information == 4)).ToList();
            foreach (var set in matchingSets)
                TabbedText.WriteLine(iteration + ": " + set.GetCumulativeRegretsString() + " ==> " + set.GetCurrentProbabilitiesAsString());
            TabbedText.WriteLine("");
        }

        private void HandleSymmetry(int iteration)
        {
            bool symmetric = GameDefinition.GameIsSymmetric() && TakeShortcutInSymmetricGames;
            if (symmetric)
            {
                if (iteration == 1)
                    InformationSetSymmetryMap = InformationSetNode.IdentifySymmetricInformationSets(InformationSets, GameDefinition);
                CopySymmetricInformationSets();
            }
        }

        public void CopySymmetricInformationSets()
        {
            if (VerifySymmetry && EvolutionSettings.PruneOnOpponentStrategy)
                throw new Exception("Should not verify symmetry with pruning."); // the reason is that one player may have twice as many VISITS to information sets as the other player because of decision sequencing. Without pruning, these visits will add up to the same effect if symmetry is working correctly, but pruning may wipe out the unlikely half of these decisions. One can still USE pruning with enforcement of symmetry, after one has verified that it is working without pruning. Indeed, enforcing symmetry helps prevent mild asymmetries from the combination of sequencing and pruning.
            foreach (var dictionaryEntry in InformationSetSymmetryMap)
            {
                InformationSetNode informationSet = dictionaryEntry.Key;
                if (informationSet.PlayerIndex == 1)
                {
                    InformationSetNode correspondingPlayer0InformationSet = dictionaryEntry.Value;
                    informationSet.CopyFromSymmetricInformationSet(correspondingPlayer0InformationSet, GameDefinition.DecisionsExecutionOrder[dictionaryEntry.Key.DecisionIndex].SymmetryMap.decision,  VerifySymmetry);
                }
            }
        }
        public override void CompleteReinitializeForScenario(bool warmupVersion, int previousScenarioIndex, double previousWeightOnOpponentP0, double previousWeightOnOpponentOtherPlayers, int updatedScenarioIndex, bool alwaysReinitialize)
        {
            if (warmupVersion || !GameDefinition.UseDifferentWarmup || alwaysReinitialize)
            {
                ReinitializeInformationSets();
            }
            var firstFinalUtilitiesNode = FinalUtilitiesNodes?.FirstOrDefault();
            if (FinalUtilitiesNodes != null && firstFinalUtilitiesNode != null && (previousScenarioIndex != updatedScenarioIndex || previousWeightOnOpponentP0 != GameDefinition.CurrentWeightOnOpponentP0 || previousWeightOnOpponentOtherPlayers != GameDefinition.CurrentWeightOnOpponentOtherPlayers))
            {
                foreach (var node in FinalUtilitiesNodes)
                {
                    node.CurrentInitializedScenarioIndex = updatedScenarioIndex;
                    node.WeightOnOpponentsUtilityP0 = GameDefinition.CurrentWeightOnOpponentP0;
                    node.WeightOnOpponentsUtilityOtherPlayers = GameDefinition.CurrentWeightOnOpponentOtherPlayers;
                }
                CalculateMinMax();
            }
            if (GameDefinition.CurrentWeightOnOpponentP0 > 0 && !EvolutionSettings.UseContinuousRegretsDiscounting)
                throw new Exception("Using current weight on opponent for warmup has been shown to work only with continuous regrets discounting.");
        }

        #endregion


        #region Unrolled preparation

        // We can achieve considerable improvements in performance by unrolling the algorithm. Instead of traversing the tree, we simply have a series of simple commands that can be processed on an array. The challenge is that we need to create this series of commands. This section prepares for the copying of data between information sets and the array. We can compare the outcomes of the regular algorithm and the unrolled version (which should always be equal) by using TraceCFR = true.

        private ArrayCommandList Unroll_Commands;
        private ArrayCommandListRunner Unroll_CommandListRunner;
        private static ArrayCommandList Unroll_Commands_Cached = null;
        private static (int Chance, int Decision, int Final) GameTreeNodeCount_Cached = default;
        private int Unroll_SizeOfArray;
        private static int Unroll_SizeOfArray_Cached = -1;

        int UnrollCheckpointIteration = -1; // if using checkpoints to debug unrolling, set an iteration (such as 1) here

        public async Task<ReportCollection> Unroll_SolveGeneralizedVanillaCFR()
        {
            ReportCollection reportCollection = new ReportCollection();
            double[] array = new double[Unroll_SizeOfArray];
            bool targetMet = false;
            Stopwatch s = new Stopwatch();
            s.Start();
            long lastElapsedSeconds = -1;
            for (int iteration = 1; iteration <= EvolutionSettings.TotalIterations && !targetMet; iteration++)
            {
                if (Unroll_Commands.UseCheckpoints && iteration == UnrollCheckpointIteration)
                {
                    Unroll_Commands.ResetCheckpoints();
                }
                long elapsedSeconds = s.ElapsedMilliseconds / 1000;
                if (elapsedSeconds != lastElapsedSeconds)
                    TabbedText.SetConsoleProgressString($"Iteration {iteration} (elapsed seconds: {s.ElapsedMilliseconds / 1000})");
                lastElapsedSeconds = elapsedSeconds;
                // uncomment to skip a player
                //if (iteration == 5001)
                //    Unroll_Commands.SetSkip("Optimizing player 0", true); 
                if (iteration % 50 == 1 && EvolutionSettings.DynamicSetParallel)
                    DynamicallySetParallel();
                Status.IterationNumDouble = iteration;
                Status.IterationNum = iteration;
                StrategiesDeveloperStopwatch.Start();
                if (EvolutionSettings.CFRBR)
                    CalculateBestResponse(false);
                Unroll_ExecuteUnrolledCommands(array, iteration == 1 || iteration == GameDefinition.IterationsForWarmupScenario + 1);
                StrategiesDeveloperStopwatch.Stop();
                if (Unroll_Commands.UseCheckpoints)
                {
                    Unroll_Commands.LoadCheckpoints(array);
                    var checkpoints = String.Join("\r\n", Enumerable.Range(0, Unroll_Commands.Checkpoints.Count).Select(x => $"{x}: {Unroll_Commands.Checkpoints[x]}"));
                }
                UpdateInformationSets(iteration);
                await PostIterationWorkForPrincipalComponentsAnalysis(iteration, reportCollection);
                SimulatedAnnealing(iteration);
                MiniReport(iteration, Unroll_IterationResultForPlayers);
                bool addGeneticAlgorithm = false;
                if (addGeneticAlgorithm && iteration == EvolutionSettings.TotalIterations)
                {
                    int numGeneticIterations = 1_000;
                    ACESimBase.GameSolvingAlgorithms.GeneticAlgorithm.RunFromAnotherAlgorithm(InformationSets, numGeneticIterations, CalculateBestResponseAndGetFitnessAndUtilities);
                }
#pragma warning disable CA1416
                if (iteration == EvolutionSettings.TotalIterations && EvolutionSettings.GenerateManualReports)
                {
                    SaveWeightedGameProgressesAfterEachReport = true;
                    SavedWeightedGameProgresses = new();
                }
                else
                    SaveWeightedGameProgressesAfterEachReport = false;
                var result = await GenerateReports(iteration,
                    () =>
                        $"{GameDefinition.OptionSetName} Iteration {iteration} Overall milliseconds per iteration {((StrategiesDeveloperStopwatch.ElapsedMilliseconds / ((double)iteration)))}");
                ConsiderModelSuccessTrackingForIteration(iteration);
                reportCollection.Add(result);
                targetMet = Status.BestResponseTargetMet(EvolutionSettings.BestResponseTarget);
                if (TraceCFR)
                { // only trace through iteration
                    // There are a number of advanced settings in ArrayCommandList that must be disabled for this feature to work properly. Parallelize || RepeatIdenticalRanges || UseOrderedDestinations || UseOrderedSources must be false.
                    string resultWithReplacementOfArray = TraceCommandList(array);
                }
                if (EvolutionSettings.PruneOnOpponentStrategy && EvolutionSettings.PredeterminePrunabilityBasedOnRelativeContributions)
                    CalculateReachProbabilitiesAndPrunability(EvolutionSettings.ParallelOptimization);
                ReinitializeInformationSetsIfNecessary(iteration);
            }
            TabbedText.SetConsoleProgressString(null);
            if (EvolutionSettings.GenerateManualReports)
                GenerateManualReports("");
            return reportCollection;
        }

        private void ReinitializeInformationSetsIfNecessary(int iteration)
        {
            if (EvolutionSettings.IsIterationResetPoint(iteration))
            {
                ReinitializeInformationSets();
            }

            int iterationToReturnToBaselineScenario = GameDefinition.IterationsForWarmupScenario ?? -1;
            if (iteration == iterationToReturnToBaselineScenario)
            {
                ReinitializeForScenario(GameDefinition.CurrentOverallScenarioIndex, false);
                ResetBestExploitability();
            }
        }

        public string TraceCommandList(double[] array)
        {
            string traceStringWithArrayStubs = TabbedText.AccumulatedText.ToString();
            string replaced = StringUtil.ReplaceArrayDesignationWithArrayItem(traceStringWithArrayStubs, array);
            return replaced;
        }

        private void Unroll_CreateUnrolledCommandList()
        {
            int numRetries = 0;
            Stopwatch s = new Stopwatch();
            s.Start();
        retry:
            try
            {
                const int max_num_commands = 150_000_000;
                Unroll_InitializeInitialArrayIndices();
                if (EvolutionSettings.ReuseUnrolledAlgorithm && Unroll_Commands_Cached != null)
                {
                    (int Chance, int Decision, int Final) gameTreeNodeCount = CountGameTreeNodes();
                    if (gameTreeNodeCount == GameTreeNodeCount_Cached)
                    {
                        Unroll_Commands = Unroll_Commands_Cached;
                        Unroll_SizeOfArray = Unroll_SizeOfArray_Cached;
                        TabbedText.WriteLine($"Using cached unrolled commands.");
                        return;
                    }
                }
                Unroll_Commands_Cached = null; // free memory
                TabbedText.WriteLine($"Unrolling commands...");
                Unroll_Commands = new ArrayCommandList(max_num_commands, Unroll_InitialArrayIndex);
                ActionStrategy = ActionStrategies.CurrentProbability;
                HistoryPoint historyPoint = GetStartOfGameHistoryPoint();
                List<int> resultIndices = new List<int>();

                Unroll_Commands.StartCommandChunk(false, null, "Iteration");
                if (EvolutionSettings.IncludeCommentsWhenUnrolling)
                    Unroll_Commands.InsertComment("--- BEGIN ITERATION ARRAY BUILD ---");

                bool takeSymmetryShortcut = NumNonChancePlayers == 2 && GameDefinition.GameIsSymmetric() && TakeShortcutInSymmetricGames;
                for (byte p = 0; p < NumNonChancePlayers; p++)
                {
                    if (takeSymmetryShortcut && p == 1)
                        continue;
                    Unroll_Commands.StartCommandChunk(false, null, "Optimizing player " + p.ToString());
                    if (EvolutionSettings.IncludeCommentsWhenUnrolling)
                        Unroll_Commands.InsertComment($"Player {p}: optimization block start");
                    if (TraceCFR)
                        TabbedText.WriteLine($"Unrolling for Player {p}");
                    Unroll_GeneralizedVanillaCFR(in historyPoint, p, Unroll_InitialPiValuesIndices, Unroll_InitialAvgStratPiValuesIndices, Unroll_IterationResultForPlayersIndices[p], true, 0, takeSymmetryShortcut || p == NumNonChancePlayers - 1);

                    if (EvolutionSettings.IncludeCommentsWhenUnrolling)
                        Unroll_Commands.InsertComment($"Player {p}: optimization block end");
                    Unroll_Commands.EndCommandChunk();
                }
                Unroll_Commands.EndCommandChunk();
                Unroll_SizeOfArray = Unroll_Commands.VirtualStackSize;
                if (EvolutionSettings.ReuseUnrolledAlgorithm)
                {
                    Unroll_Commands_Cached = Unroll_Commands;
                    Unroll_SizeOfArray_Cached = Unroll_SizeOfArray;
                    GameTreeNodeCount_Cached = CountGameTreeNodes();
                }
                Unroll_CommandListRunner = Unroll_Commands.GetCompiledRunner(kind: EvolutionSettings.Unroll_ChunkExecutorKind, null);
            }
            catch (OutOfMemoryException)
            {
                numRetries++;
                if (numRetries <= 10)
                {
                    int delayPeriod = 60_000 + (int)(60000.0 * new Random((int)DateTime.Now.Ticks).Next());
                    TabbedText.WriteLine($"Delaying {delayPeriod} milliseconds");
                    Task.Delay(delayPeriod).Wait(); // wait a minute or so before retrying
                    goto retry;
                }
            }
            TabbedText.WriteLine($"... {s.ElapsedMilliseconds} milliseconds (using {Unroll_Commands.VirtualStackSize} array size and {Unroll_Commands.MaxCommandIndex} commands)");
        }

        private void Unroll_ExecuteUnrolledCommands(double[] array, bool copyChanceAndFinalUtilities)
        {
            try
            {
                Unroll_CopyInformationSetsToArray(array, copyChanceAndFinalUtilities);
                Unroll_CommandListRunner.Run(Unroll_Commands, array, TraceCFR);
                Unroll_CopyArrayToInformationSets(array);
                Unroll_DetermineIterationResultForEachPlayer(array);
            }
            catch (Exception ex)
            {
                throw new UnrollingException(ex); // abort -- this may be a bug likely due to the game tree being the wrong size as a result of caching
            }
        }

        private void Unroll_DetermineIterationResultForEachPlayer(double[] array)
        {
            Unroll_IterationResultForPlayers = new GeneralizedVanillaUtilities[NumNonChancePlayers]; // array items from indices above will be copied here
            for (byte p = 0; p < NumNonChancePlayers; p++)
                Unroll_IterationResultForPlayers[p] = new GeneralizedVanillaUtilities()
                {
                    CurrentVsCurrent = array[Unroll_IterationResultForPlayersIndices[p][Unroll_Result_CurrentVsCurrentIndex]],
                    AverageStrategyVsAverageStrategy = array[Unroll_IterationResultForPlayersIndices[p][Unroll_Result_AverageStrategyIndex]],
                    BestResponseToAverageStrategy = array[Unroll_IterationResultForPlayersIndices[p][Unroll_Result_BestResponseIndex]],
                };
        }

        // Let's store the index in the array at which we will place the various types of information set information.

        private const int Unroll_NumPiecesInfoPerInformationSetAction = 8;
        private const int Unroll_InformationSetPerActionOrder_AverageStrategy = 0;
        private const int Unroll_InformationSetPerActionOrder_CurrentProbability = 1;
        private const int Unroll_InformationSetPerActionOrder_CurrentProbability_Opponent = 2; // if we vary the current probability that opponent will play against
        private const int Unroll_InformationSetPerActionOrder_LastRegretNumerator = 3;
        private const int Unroll_InformationSetPerActionOrder_LastRegretDenominator = 4;
        private const int Unroll_InformationSetPerActionOrder_BestResponseNumerator = 5;
        private const int Unroll_InformationSetPerActionOrder_BestResponseDenominator = 6;
        private const int Unroll_InformationSetPerActionOrder_LastCumulativeStrategyIncrement = 7;

        private int[][] Unroll_IterationResultForPlayersIndices;
        private GeneralizedVanillaUtilities[] Unroll_IterationResultForPlayers;
        private int[] Unroll_InformationSetsIndices;
        private int[] Unroll_ChanceNodesIndices;
        private Dictionary<(int chanceNodeNumber, int distributorChanceInputs), int> Unroll_ChanceNodesIndices_distributorChanceInputs;
        private int[] Unroll_FinalUtilitiesNodesIndices;
        private int[] Unroll_InitialPiValuesIndices = null;
        private int[] Unroll_InitialAvgStratPiValuesIndices = null;
        private int Unroll_OneIndex = -1;
        private int Unroll_ZeroIndex = -1;
        private int Unroll_SmallestValuePossibleIndex = -1;
        private int Unroll_SmallestProbabilityRepresentedIndex = -1;
        private int Unroll_AverageStrategyAdjustmentIndex = -1;
        private int Unroll_InitialArrayIndex = -1;

        // The following indices correspond to the order in GeneralizedVanillaUtilities
        private const int Unroll_Result_CurrentVsCurrentIndex = 0;
        private const int Unroll_Result_AverageStrategyIndex = 1;
        private const int Unroll_Result_BestResponseIndex = 2;

        private int Unroll_GetInformationSetIndex_LastBestResponse(int informationSetNumber, byte numPossibleActions) => Unroll_InformationSetsIndices[informationSetNumber] + (Unroll_NumPiecesInfoPerInformationSetAction * numPossibleActions);

        private int Unroll_GetInformationSetIndex_AverageStrategy(int informationSetNumber, byte action) => Unroll_InformationSetsIndices[informationSetNumber] + (Unroll_NumPiecesInfoPerInformationSetAction * (action - 1)) + Unroll_InformationSetPerActionOrder_AverageStrategy;

        private int[] Unroll_GetInformationSetIndex_AverageProbabilities_All(int informationSetNumber, byte numPossibleActions)
        {
            int[] probabilities = new int[numPossibleActions];
            int initialIndex = Unroll_InformationSetsIndices[informationSetNumber];
            for (int action = 1; action <= numPossibleActions; action++)
            {
                probabilities[action - 1] = initialIndex + (Unroll_NumPiecesInfoPerInformationSetAction * (action - 1)) + Unroll_InformationSetPerActionOrder_AverageStrategy;
            }
            return probabilities;
        }

        private int Unroll_GetInformationSetIndex_CurrentProbability(int informationSetNumber, byte action) => Unroll_InformationSetsIndices[informationSetNumber] + (Unroll_NumPiecesInfoPerInformationSetAction * (action - 1)) + Unroll_InformationSetPerActionOrder_CurrentProbability;

        private int[] Unroll_GetInformationSetIndex_CurrentProbabilities_All(int informationSetNumber, byte numPossibleActions)
        {
            int[] probabilities = new int[numPossibleActions];
            int initialIndex = Unroll_InformationSetsIndices[informationSetNumber];
            for (int action = 1; action <= numPossibleActions; action++)
            {
                probabilities[action - 1] = initialIndex + (Unroll_NumPiecesInfoPerInformationSetAction * (action - 1)) + Unroll_InformationSetPerActionOrder_CurrentProbability;
            }
            return probabilities;
        }

        private int Unroll_GetInformationSetIndex_CurrentProbabilityOpponent(int informationSetNumber, byte action) => Unroll_InformationSetsIndices[informationSetNumber] + (Unroll_NumPiecesInfoPerInformationSetAction * (action - 1)) + Unroll_InformationSetPerActionOrder_CurrentProbability_Opponent;

        private int[] Unroll_GetInformationSetIndex_CurrentProbabilitiesOpponent_All(int informationSetNumber, byte numPossibleActions)
        {
            int[] probabilities = new int[numPossibleActions];
            int initialIndex = Unroll_InformationSetsIndices[informationSetNumber];
            for (int action = 1; action <= numPossibleActions; action++)
            {
                probabilities[action - 1] = initialIndex + (Unroll_NumPiecesInfoPerInformationSetAction * (action - 1)) + Unroll_InformationSetPerActionOrder_CurrentProbability_Opponent;
            }
            return probabilities;
        }

        private int Unroll_GetInformationSetIndex_LastRegretNumerator(int informationSetNumber, byte action) => Unroll_InformationSetsIndices[informationSetNumber] + (Unroll_NumPiecesInfoPerInformationSetAction * (action - 1)) + Unroll_InformationSetPerActionOrder_LastRegretNumerator;

        private int Unroll_GetInformationSetIndex_LastRegretDenominator(int informationSetNumber, byte action) => Unroll_InformationSetsIndices[informationSetNumber] + (Unroll_NumPiecesInfoPerInformationSetAction * (action - 1)) + Unroll_InformationSetPerActionOrder_LastRegretDenominator;

        private int Unroll_GetInformationSetIndex_BestResponseNumerator(int informationSetNumber, byte action) => Unroll_InformationSetsIndices[informationSetNumber] + (Unroll_NumPiecesInfoPerInformationSetAction * (action - 1)) + Unroll_InformationSetPerActionOrder_BestResponseNumerator;

        private int Unroll_GetInformationSetIndex_BestResponseDenominator(int informationSetNumber, byte action) => Unroll_InformationSetsIndices[informationSetNumber] + (Unroll_NumPiecesInfoPerInformationSetAction * (action - 1)) + Unroll_InformationSetPerActionOrder_BestResponseDenominator;
        private int Unroll_GetInformationSetIndex_LastCumulativeStrategyIncrement(int informationSetNumber, byte action) => Unroll_InformationSetsIndices[informationSetNumber] + (Unroll_NumPiecesInfoPerInformationSetAction * (action - 1)) + Unroll_InformationSetPerActionOrder_LastCumulativeStrategyIncrement;

        private int Unroll_GetChanceNodeIndex(int chanceNodeNumber) => Unroll_ChanceNodesIndices[chanceNodeNumber];
        private int Unroll_GetChanceNodeIndex_ProbabilityForAction(int chanceNodeNumber, byte action) => Unroll_ChanceNodesIndices[chanceNodeNumber] + (byte)(action - 1);

        private int Unroll_GetChanceNodeIndex_ProbabilityForAction(int chanceNodeNumber, int distributorChanceInputs, byte action)
        {
            var key = (chanceNodeNumber, distributorChanceInputs);
            if (distributorChanceInputs == -1 || Unroll_ChanceNodesIndices_distributorChanceInputs == null || !Unroll_ChanceNodesIndices_distributorChanceInputs.ContainsKey(key))
            {
                return Unroll_GetChanceNodeIndex_ProbabilityForAction(chanceNodeNumber, action);
            }
            else
                return Unroll_ChanceNodesIndices_distributorChanceInputs[key] + (byte)(action - 1);
        }

        private int[] Unroll_GetChanceNodeIndices(int chanceNodeNumber, byte numPossibleActions)
        {
            int firstIndex = Unroll_GetChanceNodeIndex(chanceNodeNumber);
            return Enumerable.Range(0, numPossibleActions).Select(x => firstIndex + x).ToArray();
        }
        private int Unroll_GetFinalUtilitiesNodesIndex(int finalUtilitiesNodeNumber, byte playerBeingOptimized) => Unroll_FinalUtilitiesNodesIndices[finalUtilitiesNodeNumber] + playerBeingOptimized; // NOTE: If finalUtilitiesNodeNumber is -1, that may be because we are using PlayUnderlyingGame. This isn't supported, since we need to have all final utilities nodes generated in advance.

        private void Unroll_InitializeInitialArrayIndices()
        {
            int index = 1; // skip index 0 because we want to be able to identify references to index 0 as errors
            Unroll_ChanceNodesIndices = new int[ChanceNodes.Count];
            if (EvolutionSettings.DistributeChanceDecisions)
                Unroll_ChanceNodesIndices_distributorChanceInputs = new Dictionary<(int chanceNodeNumber, int distributorChanceInputs), int>();
            for (int i = 0; i < ChanceNodes.Count; i++)
            {
                int numItems = ChanceNodes[i].Decision.NumPossibleActions;
                Unroll_ChanceNodesIndices[i] = index;
                index += numItems;
                if (ChanceNodes[i] is ChanceNodeUnequalProbabilities unequal && unequal.ProbabilitiesForDistributorChanceInputs != null)
                {
                    // This node has information relevant to distributed chance actions. We thus need to remember where the indices are for the chance node 
                    var keys = unequal.ProbabilitiesForDistributorChanceInputs.Keys.OrderBy(x => x).ToList();
                    foreach (int distributorChanceInputs in keys)
                    {
                        Unroll_ChanceNodesIndices_distributorChanceInputs[(i /* == ChanceNodes[i].ChanceNodeNumber */, distributorChanceInputs)] = index;
                        index += numItems;
                    }
                }
            }
            Unroll_FinalUtilitiesNodesIndices = new int[FinalUtilitiesNodes.Count];
            for (int i = 0; i < FinalUtilitiesNodes.Count; i++)
            {
                Unroll_FinalUtilitiesNodesIndices[i] = index;
                int numItems = FinalUtilitiesNodes[i].Utilities.Length;
                index += numItems;
            }
            Unroll_InformationSetsIndices = new int[InformationSets.Count];
            for (int i = 0; i < InformationSets.Count; i++)
            {
                Unroll_InformationSetsIndices[i] = index;
                int numItems = Unroll_NumPiecesInfoPerInformationSetAction * InformationSets[i].NumPossibleActions + 1; // the plus 1 is for the last best response
                index += numItems;
            }
            Unroll_InitialPiValuesIndices = new int[NumNonChancePlayers];
            Unroll_InitialAvgStratPiValuesIndices = new int[NumNonChancePlayers];
            Unroll_IterationResultForPlayersIndices = new int[NumNonChancePlayers][];
            for (int p = 0; p < NumNonChancePlayers; p++)
            {
                Unroll_InitialPiValuesIndices[p] = index++;
                Unroll_InitialAvgStratPiValuesIndices[p] = index++;
                Unroll_IterationResultForPlayersIndices[p] = new int[3];
                for (int i = 0; i < 3; i++)
                    Unroll_IterationResultForPlayersIndices[p][i] = index++;
            }
            Unroll_OneIndex = index++;
            Unroll_ZeroIndex = index++;
            Unroll_SmallestValuePossibleIndex = index++;
            Unroll_SmallestProbabilityRepresentedIndex = index++;
            Unroll_AverageStrategyAdjustmentIndex = index++;
            Unroll_InitialArrayIndex = index;
        }

        private void Unroll_CopyInformationSetsToArray(double[] array, bool copyChanceAndFinalUtilitiesNodes)
        {
            if (copyChanceAndFinalUtilitiesNodes)
            { // these only need to be copied once -- not every iteration
                Parallelizer.Go(EvolutionSettings.ParallelOptimization, 0, ChanceNodes.Count, i =>
                {
                    var chanceNode = ChanceNodes[i];
                    int initialIndex = Unroll_GetChanceNodeIndex(chanceNode.ChanceNodeNumber);
                    for (byte a = 1; a <= chanceNode.Decision.NumPossibleActions; a++)
                    {
                        array[initialIndex++] = chanceNode.GetActionProbability(a);
                    }
                    if (ChanceNodes[i] is ChanceNodeUnequalProbabilities unequal && unequal.ProbabilitiesForDistributorChanceInputs != null)
                    {
                        // This node has information relevant to distributed chance actions. We thus need to remember where the indices are for the chance node 
                        var keys = unequal.ProbabilitiesForDistributorChanceInputs.Keys.OrderBy(x => x).ToList();
                        foreach (int distributorChanceInputs in keys)
                        {
                            for (byte a = 1; a <= chanceNode.Decision.NumPossibleActions; a++)
                            {
                                array[initialIndex++] = chanceNode.GetActionProbability(a, distributorChanceInputs);
                            }
                        }
                    }
                });
                Parallelizer.Go(EvolutionSettings.ParallelOptimization, 0, FinalUtilitiesNodes.Count, x =>
                {
                    var finalUtilitiesNode = FinalUtilitiesNodes[x];
                    int initialIndex = Unroll_GetFinalUtilitiesNodesIndex(finalUtilitiesNode.FinalUtilitiesNodeNumber, 0);
                    for (byte p = 0; p < finalUtilitiesNode.Utilities.Length; p++)
                    {
                        double utility = finalUtilitiesNode.Utilities[p];
                        array[initialIndex++] = utility;
                    }
                });
            }
            Parallelizer.Go(EvolutionSettings.ParallelOptimization, 0, InformationSets.Count, x =>
            {
                var infoSet = InformationSets[x];
                int initialIndex = Unroll_InformationSetsIndices[infoSet.InformationSetNodeNumber];
                for (byte action = 1; action <= infoSet.NumPossibleActions; action++)
                {
                    array[initialIndex++] = infoSet.GetAverageStrategy(action);
                    array[initialIndex++] = infoSet.GetCurrentProbability(action, false);
                    array[initialIndex++] = infoSet.GetCurrentProbability(action, true);
                    array[initialIndex++] = 0; // initialize last regret to zero
                    array[initialIndex++] = 0; // initialize last regret denominator to zero
                    array[initialIndex++] = 0; // initialize best response numerator to zero
                    array[initialIndex++] = 0; // initialize best response denominator to zero
                    array[initialIndex++] = 0; // initialize last cumulative strategy increment to zero
                }
                array[initialIndex] = infoSet.BestResponseAction;
            });
            for (byte p = 0; p < NumNonChancePlayers; p++)
            {
                array[Unroll_InitialPiValuesIndices[p]] = 1.0;
                array[Unroll_InitialAvgStratPiValuesIndices[p]] = 1.0;
                for (int i = 0; i < 3; i++)
                    array[Unroll_IterationResultForPlayersIndices[p][i]] = 0;
            }
            CalculateDiscountingAdjustments();
            array[Unroll_OneIndex] = 1.0;
            array[Unroll_ZeroIndex] = 0.0;
            array[Unroll_SmallestValuePossibleIndex] = double.Epsilon;
            array[Unroll_SmallestProbabilityRepresentedIndex] = InformationSetNode.SmallestProbabilityRepresented;
            array[Unroll_AverageStrategyAdjustmentIndex] = AverageStrategyAdjustment;
        }

        private void Unroll_CopyArrayToInformationSets(double[] array)
        {
            //for (int i = 0; i < array.Length; i++)
            //    System.Diagnostics.Debug.WriteLine($"{i}: {array[i]}");
            Parallelizer.Go(EvolutionSettings.ParallelOptimization, 0, InformationSets.Count, x =>
            {
                var infoSet = InformationSets[x];
                for (byte action = 1; action <= infoSet.NumPossibleActions; action++)
                {
                    int index = Unroll_GetInformationSetIndex_LastRegretNumerator(infoSet.InformationSetNodeNumber, action);
                    int index2 = Unroll_GetInformationSetIndex_LastRegretDenominator(infoSet.InformationSetNodeNumber, action);
                    infoSet.IncrementLastRegret(action, array[index], array[index2]);
                    index = Unroll_GetInformationSetIndex_LastCumulativeStrategyIncrement(infoSet.InformationSetNodeNumber, action);
                    infoSet.IncrementLastCumulativeStrategyIncrements(action, array[index]);
                    int indexNumerator = Unroll_GetInformationSetIndex_BestResponseNumerator(infoSet.InformationSetNodeNumber, action);
                    int indexDenominator = Unroll_GetInformationSetIndex_BestResponseDenominator(infoSet.InformationSetNodeNumber, action);
                    infoSet.SetBestResponse_NumeratorAndDenominator(action, array[indexNumerator], array[indexDenominator]); // this is the final value based on the probability-adjusted increments within the algorithm
                }
            });
        }

        #endregion

        #region Unrolled algorithm

        public void Unroll_GeneralizedVanillaCFR(in HistoryPoint historyPoint, byte playerBeingOptimized, int[] piValues, int[] avgStratPiValues, int[] resultArray, bool algorithmIsLowestDepth, int distributorChanceInputs, bool completeCommandList)
        {
            Unroll_Commands.IncrementDepth();
            IGameState gameStateForCurrentPlayer = GetGameState(in historyPoint);
            GameStateTypeEnum gameStateType = gameStateForCurrentPlayer.GetGameStateType();
            if (gameStateType == GameStateTypeEnum.FinalUtilities)
            {
                FinalUtilitiesNode finalUtilities = (FinalUtilitiesNode)gameStateForCurrentPlayer;
                // Note: An alternative approach would be to add the utility value found here to the unrolled commands, instead of looking it up in the array. But this approach makes it possible to change some game parameters and thus the final utilities without regenerating commands.
                int finalUtilIndex = Unroll_Commands.CopyToNew(Unroll_GetFinalUtilitiesNodesIndex(finalUtilities.FinalUtilitiesNodeNumber, playerBeingOptimized), true);
                // Note: We must copy this so that we don't change the final utilities themselves.
                Unroll_Commands.CopyToExisting(resultArray[0], finalUtilIndex);
                Unroll_Commands.CopyToExisting(resultArray[1], finalUtilIndex);
                Unroll_Commands.CopyToExisting(resultArray[2], finalUtilIndex);
            }
            else if (gameStateType == GameStateTypeEnum.Chance)
            {
                Unroll_GeneralizedVanillaCFR_ChanceNode(in historyPoint, playerBeingOptimized, piValues, avgStratPiValues, resultArray, algorithmIsLowestDepth, distributorChanceInputs);
            }
            else
                Unroll_GeneralizedVanillaCFR_DecisionNode(in historyPoint, playerBeingOptimized, piValues, avgStratPiValues, resultArray, algorithmIsLowestDepth, distributorChanceInputs);
            Unroll_Commands.DecrementDepth(completeCommandList);
        }

        private void Unroll_GeneralizedVanillaCFR_DecisionNode(in HistoryPoint historyPoint, byte playerBeingOptimized, int[] piValues, int[] avgStratPiValues, int[] resultArray, bool algorithmIsLowestDepth, int distributorChanceInputs)
        {
            if (algorithmIsLowestDepth)
            {
                piValues = Unroll_Commands.CopyToNew(piValues, true);
                avgStratPiValues = Unroll_Commands.CopyToNew(avgStratPiValues, true);
            }
            Unroll_Commands.IncrementDepth();
            int inversePi = Unroll_Commands.NewZero();
            Unroll_GetInversePiValue(piValues, playerBeingOptimized, inversePi);
            int inversePiAvgStrat = Unroll_Commands.NewZero();
            Unroll_GetInversePiValue(avgStratPiValues, playerBeingOptimized, inversePiAvgStrat);
            //var actionsToHere = historyPoint.GetActionsToHere(Navigation);
            //var historyPointString = historyPoint.ToString();

            IGameState gameStateForCurrentPlayer = GetGameState(in historyPoint);
            var informationSet = (InformationSetNode)gameStateForCurrentPlayer;
            byte decisionNum = informationSet.DecisionIndex;
            byte playerMakingDecision = informationSet.PlayerIndex;
            byte numPossibleActions = NumPossibleActionsAtDecision(decisionNum);
            int[] actionProbabilities;

            if (EvolutionSettings.IncludeCommentsWhenUnrolling)
                Unroll_Commands.InsertComment($"Decision  {decisionNum} InfoSet {informationSet.InformationSetNodeNumber} player {playerMakingDecision}");
            if (EvolutionSettings.CFRBR && playerMakingDecision != playerBeingOptimized)
            {
                actionProbabilities = Unroll_Commands.NewZeroArray(informationSet.NumPossibleActions);
                for (byte action = 1; action <= informationSet.NumPossibleActions; action++)
                {
                    int lastBestResponseActionIndex = Unroll_Commands.CopyToNew(Unroll_GetInformationSetIndex_LastBestResponse(informationSet.InformationSetNodeNumber, (byte)informationSet.NumPossibleActions), true);
                    Unroll_Commands.InsertEqualsValueCommand(lastBestResponseActionIndex, (int)action);
                    Unroll_Commands.InsertIfCommand();
                    int one = Unroll_Commands.CopyToNew(Unroll_OneIndex, true);
                    Unroll_Commands.CopyToExisting(actionProbabilities[action - 1], one);
                    Unroll_Commands.InsertEndIfCommand();
                }
            }
            else
            {
                actionProbabilities = Unroll_Commands.CopyToNew(playerMakingDecision == playerBeingOptimized ?
                    Unroll_GetInformationSetIndex_CurrentProbabilities_All(informationSet.InformationSetNodeNumber, numPossibleActions) :
                    Unroll_GetInformationSetIndex_CurrentProbabilitiesOpponent_All(informationSet.InformationSetNodeNumber, numPossibleActions),
                true);
            }
            int[] expectedValueOfAction = Unroll_Commands.NewZeroArray(numPossibleActions);
            int expectedValue = Unroll_Commands.NewZero();
            bool pruningPossible = (EvolutionSettings.PruneOnOpponentStrategy || EvolutionSettings.CFRBR) && playerBeingOptimized != playerMakingDecision;
            int opponentPruningThresholdIndex = -1;
            if (pruningPossible)
            {
                opponentPruningThresholdIndex = Unroll_Commands.CopyToNew(Unroll_SmallestValuePossibleIndex, true);
            }
            for (byte action = 1; action <= numPossibleActions; action++)
            {
                if (EvolutionSettings.IncludeCommentsWhenUnrolling)
                    Unroll_Commands.InsertComment($"Decision  {decisionNum} InfoSet {informationSet.InformationSetNodeNumber} player {playerMakingDecision} Action {action}");
                int distributorChanceInputsNext = distributorChanceInputs;
                if (informationSet.Decision.DistributorChanceInputDecision)
                    distributorChanceInputsNext += action * informationSet.Decision.DistributorChanceInputDecisionMultiplier;
                int probabilityOfAction = actionProbabilities[action - 1];
                if (pruningPossible)
                {
                    // NOTE: When we insert this IF command, we need to ensure that all of the subsequent commands before the "end if"
                    // are in the same command chunk. If all chance decisions precede all player decisions, then that should be possible.
                    // We are setting up the IF in a player decision, but we are only starting the new command chunk in the chance nodes. 
                    // Thus, even if we have multiple IF/THENs, they will all be contained within the same command chunk.
                    // But if we have a different structure, then we need to make sure that we don't break up the command chunk.
                    // The ArrayCommandList currently takes care of this by keeping commands together within the if/then loop.

                    Unroll_Commands.InsertGreaterThanOtherArrayIndexCommand(probabilityOfAction, opponentPruningThresholdIndex); // if less than or equal to then prune, so if greater than, don't prune (continue into if block)
                    Unroll_Commands.InsertIfCommand();
                }

                //if (EvolutionSettings.PruneOnOpponentStrategy && playerBeingOptimized != playerMakingDecision && probabilityOfAction < EvolutionSettings.PruneOnOpponentStrategyThreshold)
                //    continue;
                int probabilityOfActionAvgStrat = Unroll_Commands.CopyToNew(Unroll_GetInformationSetIndex_AverageStrategy(informationSet.InformationSetNodeNumber, action), true);
                int[] nextPiValues = Unroll_Commands.NewZeroArray(NumNonChancePlayers);
                Unroll_GetNextPiValues(piValues, playerMakingDecision, probabilityOfAction, false, nextPiValues); // reduce probability associated with player being optimized, without changing probabilities for other players
                int[] nextAvgStratPiValues = Unroll_Commands.NewZeroArray(NumNonChancePlayers);
                Unroll_GetNextPiValues(avgStratPiValues, playerMakingDecision, probabilityOfActionAvgStrat, false, nextAvgStratPiValues);
                if (TraceCFR)
                {
                    int probabilityOfActionCopy = Unroll_Commands.CopyToNew(probabilityOfAction, false);
                    TabbedText.WriteLine(
                        $"({GameDefinition.DecisionsExecutionOrder.FirstOrDefault(x => x.DecisionByteCode == informationSet.DecisionByteCode)?.Name}) code {informationSet.DecisionByteCode} optimizing player {playerBeingOptimized}  {(playerMakingDecision == playerBeingOptimized ? "own decision" : "opp decision")} action {action} probability ARRAY{probabilityOfActionCopy} ...");
                    TabbedText.TabIndent();
                }
                HistoryPoint nextHistoryPoint = historyPoint.GetBranch(Navigation, action, informationSet.Decision, informationSet.DecisionIndex);
                int[] innerResult = Unroll_Commands.NewZeroArray(3);
                Unroll_GeneralizedVanillaCFR(in nextHistoryPoint, playerBeingOptimized, nextPiValues, nextAvgStratPiValues, innerResult, false, distributorChanceInputsNext, playerBeingOptimized == NumNonChancePlayers - 1);
                Unroll_Commands.CopyToExisting(expectedValueOfAction[action - 1], innerResult[Unroll_Result_CurrentVsCurrentIndex]);
                if (playerMakingDecision == playerBeingOptimized)
                {
                    int lastBestResponseActionIndex = Unroll_Commands.CopyToNew(Unroll_GetInformationSetIndex_LastBestResponse(informationSet.InformationSetNodeNumber, (byte)informationSet.NumPossibleActions), true);
                    Unroll_Commands.InsertEqualsValueCommand(lastBestResponseActionIndex, (int)action);
                    Unroll_Commands.InsertIfCommand();
                    Unroll_Commands.Increment(resultArray[Unroll_Result_BestResponseIndex], algorithmIsLowestDepth, innerResult[Unroll_Result_BestResponseIndex]);
                    Unroll_Commands.InsertEndIfCommand();
                    // Get the best response indices to write to -- note that we're not reading the value in
                    int bestResponseNumerator = Unroll_GetInformationSetIndex_BestResponseNumerator(informationSet.InformationSetNodeNumber, action);
                    int bestResponseDenominator = Unroll_GetInformationSetIndex_BestResponseDenominator(informationSet.InformationSetNodeNumber, action);


                    if (EvolutionSettings.IncludeCommentsWhenUnrolling)
                        Unroll_Commands.InsertComment($"Decision  {decisionNum} InfoSet {informationSet.InformationSetNodeNumber} player {playerMakingDecision} updating regrets");
                    Unroll_Commands.IncrementByProduct(bestResponseNumerator, true, inversePiAvgStrat, innerResult[Unroll_Result_BestResponseIndex]); // since we are not using these internal to the tree walk, we target originals
                    Unroll_Commands.Increment(bestResponseDenominator, true, inversePiAvgStrat);
                    Unroll_Commands.IncrementByProduct(resultArray[Unroll_Result_CurrentVsCurrentIndex], algorithmIsLowestDepth, probabilityOfAction, innerResult[Unroll_Result_CurrentVsCurrentIndex]);
                    Unroll_Commands.IncrementByProduct(resultArray[Unroll_Result_AverageStrategyIndex], algorithmIsLowestDepth, probabilityOfActionAvgStrat, innerResult[Unroll_Result_AverageStrategyIndex]);
                }
                else
                {
                    Unroll_Commands.IncrementByProduct(resultArray[Unroll_Result_CurrentVsCurrentIndex], algorithmIsLowestDepth, probabilityOfAction, innerResult[Unroll_Result_CurrentVsCurrentIndex]);
                    Unroll_Commands.IncrementByProduct(resultArray[Unroll_Result_AverageStrategyIndex], algorithmIsLowestDepth, probabilityOfActionAvgStrat, innerResult[Unroll_Result_AverageStrategyIndex]);
                    Unroll_Commands.IncrementByProduct(resultArray[Unroll_Result_BestResponseIndex], algorithmIsLowestDepth, probabilityOfActionAvgStrat, innerResult[Unroll_Result_BestResponseIndex]);
                }
                Unroll_Commands.IncrementByProduct(expectedValue, false, probabilityOfAction, expectedValueOfAction[action - 1]);

                if (TraceCFR)
                {
                    int expectedValueOfActionCopy = Unroll_Commands.CopyToNew(expectedValueOfAction[action - 1], false);
                    int bestResponseExpectedValueCopy = Unroll_Commands.CopyToNew(resultArray[Unroll_Result_BestResponseIndex], false);
                    int cumExpectedValueCopy = Unroll_Commands.CopyToNew(expectedValue, false);
                    TabbedText.TabUnindent();
                    TabbedText.WriteLine(
                        $"... action {action} expected value ARRAY{expectedValueOfActionCopy} best response expected value ARRAY{bestResponseExpectedValueCopy} cum expected value ARRAY{cumExpectedValueCopy}{(action == numPossibleActions && IncludeAsteriskForBestResponseInTrace ? "*" : "")}");
                }

                if (pruningPossible)
                {
                    Unroll_Commands.InsertEndIfCommand();
                }
            }
            if (playerMakingDecision == playerBeingOptimized)
            {
                int smallestPossible = Unroll_Commands.CopyToNew(Unroll_SmallestProbabilityRepresentedIndex, true);
                for (byte action = 1; action <= numPossibleActions; action++)
                {
                    if (EvolutionSettings.IncludeCommentsWhenUnrolling)
                        Unroll_Commands.InsertComment($"Decision  {decisionNum} InfoSet {informationSet.InformationSetNodeNumber} player {playerMakingDecision} Action {action} incrementing regrets");
                    int pi = Unroll_Commands.CopyToNew(piValues[playerBeingOptimized], false); 
                    Unroll_Commands.CreateCheckpoint(pi);
                    Unroll_Commands.InsertLessThanOtherArrayIndexCommand(pi, smallestPossible);
                    Unroll_Commands.InsertIfCommand();
                    Unroll_Commands.CopyToExisting(pi, smallestPossible);
                    Unroll_Commands.InsertEndIfCommand();
                    int regret = Unroll_Commands.CopyToNew(expectedValueOfAction[action - 1], false); 
                    Unroll_Commands.Decrement(regret, expectedValue);
                    int lastRegretNumerator = Unroll_GetInformationSetIndex_LastRegretNumerator(informationSet.InformationSetNodeNumber, action);
                    int lastRegretDenominator = Unroll_GetInformationSetIndex_LastRegretDenominator(informationSet.InformationSetNodeNumber, action);
                    Unroll_Commands.IncrementByProduct(lastRegretNumerator, true, regret, inversePi);
                    Unroll_Commands.Increment(lastRegretDenominator, true, inversePi);
                    // now contribution to average strategy
                    int contributionToAverageStrategy = Unroll_Commands.CopyToNew(pi, false);
                    Unroll_Commands.MultiplyBy(contributionToAverageStrategy, actionProbabilities[action - 1]); // note: we don't multiply by average strategy adjustment here -- we do so at end of iteration
                    int lastCumulativeStrategyIncrement = Unroll_GetInformationSetIndex_LastCumulativeStrategyIncrement(informationSet.InformationSetNodeNumber, action);
                    Unroll_Commands.Increment(lastCumulativeStrategyIncrement, true, contributionToAverageStrategy);
                    if (TraceCFR || Unroll_Commands.UseCheckpoints)
                    {
                        int piCopy = Unroll_Commands.CopyToNew(pi, false);
                        int piValuesZeroCopy = Unroll_Commands.CopyToNew(piValues[0], false);
                        int piValuesOneCopy = Unroll_Commands.CopyToNew(piValues[1], false);
                        int regretCopy = Unroll_Commands.CopyToNew(regret, false);
                        int inversePiCopy = Unroll_Commands.CopyToNew(inversePi, false);
                        int contributionToAverageStrategyCopy = Unroll_Commands.CopyToNew(contributionToAverageStrategy, false);
                        int cumulativeStrategyCopy = Unroll_Commands.CopyToNew(lastCumulativeStrategyIncrement, true);
                        if (TraceCFR)
                        {
                            TabbedText.WriteLine($"PiValues ARRAY{piValuesZeroCopy} ARRAY{piValuesOneCopy} pi for optimized ARRAY{piCopy}");
                            TabbedText.WriteLine(
                                $"Regrets ({informationSet.Decision.Name} {informationSet.InformationSetNodeNumber}): Action {action} probability ARRAY{actionProbabilities[action - 1]} regret ARRAY{regretCopy} inversePi ARRAY{inversePiCopy} avg_strat_increment ARRAY{contributionToAverageStrategyCopy} cum_strategy ARRAY{cumulativeStrategyCopy}");
                        }
                        if (Unroll_Commands.UseCheckpoints)
                        {
                            Unroll_Commands.CreateCheckpoint(actionProbabilities[action - 1]);
                            Unroll_Commands.CreateCheckpoint(regretCopy);
                            Unroll_Commands.CreateCheckpoint(inversePiCopy);
                            Unroll_Commands.CreateCheckpoint(piCopy); 
                            // IMPORTANT NOTE: must exclude cumulativeStrategyCopy, because it's a copy of something targeting original, so the value will not be available.
                        }
                    }
                }
            }
            Unroll_Commands.DecrementDepth();
        }

        private void Unroll_GeneralizedVanillaCFR_ChanceNode(in HistoryPoint historyPoint, byte playerBeingOptimized, int[] piValues, int[] avgStratPiValues, int[] resultArray, bool algorithmIsLowestDepth, int distributorChanceInputs)
        {
            if (algorithmIsLowestDepth)
            {
                piValues = Unroll_Commands.CopyToNew(piValues, true);
                avgStratPiValues = Unroll_Commands.CopyToNew(avgStratPiValues, true);
            }
            Unroll_Commands.IncrementDepth();
            IGameState gameStateForCurrentPlayer = GetGameState(in historyPoint);
            ChanceNode chanceNode = (ChanceNode)gameStateForCurrentPlayer;

            if (EvolutionSettings.IncludeCommentsWhenUnrolling)
                Unroll_Commands.InsertComment($"Chance node {chanceNode.Decision.Name} (node {chanceNode.ChanceNodeNumber})");
            byte numPossibleActions = chanceNode.Decision.NumPossibleActions;
            byte numPossibleActionsToExplore = numPossibleActions;
            if (EvolutionSettings.DistributeChanceDecisions && chanceNode.Decision.DistributedChanceDecision)
                numPossibleActionsToExplore = 1;
            var historyPointCopy = historyPoint; // can't use historyPoint in anonymous method below. This is costly, so it might be worth optimizing if we use GeneralizedVanillaCFR much.
            int[] probabilityAdjustedInnerResult = Unroll_Commands.NewZeroArray(3); // must allocate this outside the parallel loop, because if we have commands writing to an array created in the parallel loop, the array indices will change
            if (chanceNode.Decision.Unroll_Parallelize && EvolutionSettings.UnrollAllowParallelize)
            {
                //TabbedText.WriteLine($"Starting command chunk parallel {Unroll_Commands.NextCommandIndex}");
                Unroll_Commands.StartCommandChunk(true, null, chanceNode.Decision.Name);
                Unroll_Commands.InsertBlankCommand(); // to separate from next one
            }
            int? firstCommandToRepeat = null;
            for (byte action = 1; action <= numPossibleActionsToExplore; action++)
            {
                if (EvolutionSettings.IncludeCommentsWhenUnrolling)
                    Unroll_Commands.InsertComment($"Chance node {chanceNode.Decision.Name} (node {chanceNode.ChanceNodeNumber}) action {action}");
                if (chanceNode.Decision.Unroll_Parallelize && EvolutionSettings.UnrollAllowParallelize)
                {
                    //TabbedText.WriteLine($"Starting command chunk serial {Unroll_Commands.NextCommandIndex}");
                    Unroll_Commands.StartCommandChunk(false /* inner commands are run sequentially */, firstCommandToRepeat, chanceNode.Decision.Name + "=" + action.ToString());
                    if (action == 1 && chanceNode.Decision.Unroll_Parallelize_Identical)
                        firstCommandToRepeat = Unroll_Commands.NextCommandIndex;
                }
                var historyPointCopy2 = historyPointCopy; // Need to do this because we need a separate copy for each thread
                Unroll_Commands.ZeroExisting(probabilityAdjustedInnerResult);
                Unroll_GeneralizedVanillaCFR_ChanceNode_NextAction(in historyPointCopy2,
                    playerBeingOptimized, piValues, avgStratPiValues,
                        chanceNode, action, probabilityAdjustedInnerResult, false, distributorChanceInputs);
                Unroll_Commands.IncrementArrayBy(resultArray, algorithmIsLowestDepth, probabilityAdjustedInnerResult);

                if (chanceNode.Decision.Unroll_Parallelize && EvolutionSettings.UnrollAllowParallelize)
                    Unroll_Commands.EndCommandChunk(action != 1);
            }
            if (chanceNode.Decision.Unroll_Parallelize && EvolutionSettings.UnrollAllowParallelize)
                Unroll_Commands.EndCommandChunk();

            Unroll_Commands.DecrementDepth();
        }

        private void Unroll_GeneralizedVanillaCFR_ChanceNode_NextAction(in HistoryPoint historyPoint, byte playerBeingOptimized, int[] piValues, int[] avgStratPiValues, ChanceNode chanceNode, byte action, int[] resultArray, bool algorithmIsLowestDepth, int distributorChanceInputs)
        {
            if (EvolutionSettings.IncludeCommentsWhenUnrolling)
                Unroll_Commands.InsertComment($"Chance node {chanceNode.Decision.Name} (node {chanceNode.ChanceNodeNumber}) action {action} -> next action");
            Unroll_Commands.IncrementDepth();
            int actionProbabilityIndex = Unroll_GetChanceNodeIndex_ProbabilityForAction(chanceNode.ChanceNodeNumber, distributorChanceInputs, action);
            int actionProbability = Unroll_Commands.CopyToNew(actionProbabilityIndex, true);
            int distributorChanceInputsNext = distributorChanceInputs;
            if (chanceNode.Decision.DistributorChanceInputDecision)
                distributorChanceInputsNext += action * chanceNode.Decision.DistributorChanceInputDecisionMultiplier;
            if (EvolutionSettings.DistributeChanceDecisions && chanceNode.Decision.DistributedChanceDecision)
                actionProbability = Unroll_Commands.CopyToNew(Unroll_OneIndex, true);
            int[] nextPiValues = Unroll_Commands.NewZeroArray(NumNonChancePlayers);
            Unroll_GetNextPiValues(piValues, playerBeingOptimized, actionProbability, true, nextPiValues);
            int[] nextAvgStratPiValues = Unroll_Commands.NewZeroArray(NumNonChancePlayers);
            Unroll_GetNextPiValues(avgStratPiValues, playerBeingOptimized, actionProbability, true, nextAvgStratPiValues);
            HistoryPoint nextHistoryPoint = historyPoint.GetBranch(Navigation, action, chanceNode.Decision, chanceNode.DecisionIndex);

            if (TraceCFR)
            {
                int actionProbabilityCopy = Unroll_Commands.CopyToNew(actionProbability, false);
                TabbedText.WriteLine(
                    $"Chance code {chanceNode.DecisionByteCode} ({chanceNode.Decision.Name}) action {action} probability ARRAY{actionProbabilityCopy} ...");
                TabbedText.TabIndent();
            }
            int[] innerResult = Unroll_Commands.NewZeroArray(3);
            Unroll_GeneralizedVanillaCFR(in nextHistoryPoint, playerBeingOptimized, nextPiValues, nextAvgStratPiValues, innerResult, false, distributorChanceInputsNext, playerBeingOptimized == NumNonChancePlayers - 1);
            Unroll_Commands.CopyToExisting(resultArray, innerResult);
            if (TraceCFR)
            {
                // save current result before multiplying
                int beforeMultipleCurrentCopy = Unroll_Commands.CopyToNew(resultArray[Unroll_Result_CurrentVsCurrentIndex], false);
                int actionProbabilityCopy = Unroll_Commands.CopyToNew(actionProbability, false);

                Unroll_Commands.MultiplyArrayBy(resultArray, actionProbability);

                int resultCurrentCopy = Unroll_Commands.CopyToNew(resultArray[Unroll_Result_CurrentVsCurrentIndex], false);

                TabbedText.TabUnindent();
                TabbedText.WriteLine(
                    $"... action {action} value ARRAY{beforeMultipleCurrentCopy} probability ARRAY{actionProbabilityCopy} expected value contribution ARRAY{resultCurrentCopy}");
            }
            else
                Unroll_Commands.MultiplyArrayBy(resultArray, actionProbability);

            Unroll_Commands.DecrementDepth();
        }

        private void Unroll_GetNextPiValues(int[] currentPiValues, byte playerIndex, int probabilityToMultiplyBy, bool changeOtherPlayers, int[] resultArray)
        {
            if (EvolutionSettings.IncludeCommentsWhenUnrolling)
                Unroll_Commands.InsertComment($"Compute next π: current pi Values: {String.Join(",", currentPiValues)} player index {playerIndex}");
            Unroll_Commands.IncrementDepth();
            for (byte p = 0; p < NumNonChancePlayers; p++)
            {
                int currentPiValue = currentPiValues[p];
                Unroll_Commands.CopyToExisting(resultArray[p], currentPiValue);
                if (p == playerIndex)
                {
                    if (!changeOtherPlayers)
                        Unroll_Commands.MultiplyBy(resultArray[p], probabilityToMultiplyBy);
                }
                else
                {
                    if (changeOtherPlayers)
                        Unroll_Commands.MultiplyBy(resultArray[p], probabilityToMultiplyBy);
                }
            }
            Unroll_Commands.DecrementDepth();
        }

        private void Unroll_GetInversePiValue(int[] piValues, byte playerIndex, int inversePiValueResult)
        {
            Unroll_Commands.IncrementDepth();
            if (NumNonChancePlayers == 2)
                Unroll_Commands.CopyToExisting(inversePiValueResult, piValues[(byte)1 - playerIndex]);
            else
            {
                bool firstPlayerOtherThanMainFound = false;
                for (byte p = 0; p < NumNonChancePlayers; p++)
                    if (p != playerIndex)
                    {
                        if (firstPlayerOtherThanMainFound)
                        {
                            Unroll_Commands.MultiplyBy(inversePiValueResult, piValues[p]);
                        }
                        else
                        {
                            Unroll_Commands.CopyToExisting(inversePiValueResult, piValues[p]);
                            firstPlayerOtherThanMainFound = true;
                        }
                    }
            }
            Unroll_Commands.DecrementDepth();
        }

        #endregion

        #region Core algorithm

        public override async Task Initialize()
        {
            if (EvolutionSettings.UnrollAlgorithm && Navigation.LookupApproach == InformationSetLookupApproach.PlayGameDirectly)
            { // override -- combination is not currently supported
                LookupApproach = InformationSetLookupApproach.CachedGameHistoryOnly;
                Navigation = Navigation.WithLookupApproach(LookupApproach);
            }
            await base.Initialize();
            InitializeInformationSets();
            if (EvolutionSettings.UnrollAlgorithm)
            {
                Unroll_CreateUnrolledCommandList();
            }
        }

        public override async Task<ReportCollection> RunAlgorithm(string optionSetName)
        {
            if (EvolutionSettings.UnrollAlgorithm)
                return await Unroll_SolveGeneralizedVanillaCFR();
            return await SolveGeneralizedVanillaCFR();
        }

        private async Task<ReportCollection> SolveGeneralizedVanillaCFR()
        {
            ReportCollection reportCollection = new ReportCollection();
            bool targetMet = false;
            Stopwatch s = new Stopwatch();
            s.Start();
            long lastElapsedSeconds = -1;
            for (int iteration = 1; iteration <= EvolutionSettings.TotalIterations && !targetMet; iteration++)
            {
                long elapsedSeconds = s.ElapsedMilliseconds / 1000;
                if (!TraceCFR && elapsedSeconds != lastElapsedSeconds)
                    TabbedText.SetConsoleProgressString($"Iteration {iteration} (elapsed seconds: {s.ElapsedMilliseconds / 1000})");
                lastElapsedSeconds = elapsedSeconds;
                if (iteration % 50 == 1 && EvolutionSettings.DynamicSetParallel)
                    DynamicallySetParallel();
                if (EvolutionSettings.CFRBR)
                    CalculateBestResponse(false);
                var result = await GeneralizedVanillaCFRIteration(iteration);
                reportCollection.Add(result);
                targetMet = Status.BestResponseTargetMet(EvolutionSettings.BestResponseTarget);
                if (EvolutionSettings.PruneOnOpponentStrategy && EvolutionSettings.PredeterminePrunabilityBasedOnRelativeContributions)
                    CalculateReachProbabilitiesAndPrunability(EvolutionSettings.ParallelOptimization);
                ReinitializeInformationSetsIfNecessary(iteration);
            }
            TabbedText.SetConsoleProgressString(null);
            return reportCollection;
        }

        private async Task<ReportCollection> GeneralizedVanillaCFRIteration(int iteration)
        {
            Status.IterationNumDouble = iteration;
            Status.IterationNum = iteration;
            CalculateDiscountingAdjustments();

            ReportCollection reportCollection = new ReportCollection();
            double[] lastUtilities = new double[NumNonChancePlayers];

            ActionStrategy = ActionStrategies.CurrentProbability;

            GeneralizedVanillaUtilities[] results = new GeneralizedVanillaUtilities[NumNonChancePlayers];
            for (byte playerBeingOptimized = 0; playerBeingOptimized < NumNonChancePlayers; playerBeingOptimized++)
            {
                if (playerBeingOptimized == 1 && GameDefinition.GameIsSymmetric() && TakeShortcutInSymmetricGames && !VerifySymmetry)
                    continue;
                if (TraceCFR)
                    TabbedText.WriteLine($"Optimizing for player {playerBeingOptimized}");
                GeneralizedVanillaCFRIteration_OptimizePlayer(iteration, results, playerBeingOptimized);
            }
            UpdateInformationSets(iteration);
            await PostIterationWorkForPrincipalComponentsAnalysis(iteration, reportCollection);
            SimulatedAnnealing(iteration);
            MiniReport(iteration, results);

            var result = await GenerateReports(iteration,
                () =>
                    $"{GameDefinition.OptionSetName} Iteration {iteration} Overall milliseconds per iteration {((StrategiesDeveloperStopwatch.ElapsedMilliseconds / ((double)iteration)))}");
            reportCollection.Add(result);

            return reportCollection;
        }

        private void GeneralizedVanillaCFRIteration_OptimizePlayer(int iteration, GeneralizedVanillaUtilities[] results, byte playerBeingOptimized)
        {
            double[] initialPiValues = new double[MaxNumMainPlayers];
            double[] initialAvgStratPiValues = new double[MaxNumMainPlayers];
            GetInitialPiValues(initialPiValues);
            GetInitialPiValues(initialAvgStratPiValues);
            if (TraceCFR)
                TabbedText.WriteLine($"{GameDefinition.OptionSetName} Iteration {iteration} Player {playerBeingOptimized}");
            StrategiesDeveloperStopwatch.Start();
            HistoryPoint historyPoint = GetStartOfGameHistoryPoint();
            results[playerBeingOptimized] = GeneralizedVanillaCFR(in historyPoint, playerBeingOptimized, initialPiValues, initialAvgStratPiValues, 0);
            StrategiesDeveloperStopwatch.Stop();
        }

        private void SimulatedAnnealing(int iteration)
        {
            if (iteration % EvolutionSettings.SimulatedAnnealingEveryNIterations == 0)
            {
                if (BestBecomesResult)
                    throw new NotSupportedException(); // both use backup functionality
                if (!EvolutionSettings.UseAcceleratedBestResponse)
                    throw new NotSupportedException(); // we need the average strategy result, which for now we only have with accelerated best response
                CalculateBestResponse(false);
                double sumBestResponseImprovements = Status.BestResponseImprovement.Sum();
                if (LastBestResponseImprovement != null)
                {
                    double lastSumBestResponseImprovements = LastBestResponseImprovement.Sum();
                    if (sumBestResponseImprovements > lastSumBestResponseImprovements)
                    {
                        // Things got worse! Consider rejecting.
                        if (!EvolutionSettings.AcceptSimulatedAnnealingIfWorse(iteration, EvolutionSettings.TotalIterations))
                        {
                            // Reject -- that is, revert to previous backup. We will then have a different epsilon value in the next set (since iterations keep marching forward), so we may have a different outcome, and even if we don't, we may accept.
                            Parallel.ForEach(InformationSets, informationSet => informationSet.RestoreBackup());
                            Status.BestResponseImprovement = LastBestResponseImprovement.ToArray();
                            return;
                        }
                    }
                }
                Parallel.ForEach(InformationSets, informationSet => informationSet.CreateBackup());
                LastBestResponseImprovement = Status.BestResponseImprovement.ToArray();
            }
        }

        private void MiniReport(int iteration, GeneralizedVanillaUtilities[] results)
        {
            if (iteration % EvolutionSettings.MiniReportEveryPIterations == 0)
            {
                TabbedText.WriteLine($"{GameDefinition.OptionSetName} Iteration {iteration} (relative contribution {AverageStrategyAdjustmentAsPctOfMax})");
                TabbedText.TabIndent();
                for (byte playerBeingOptimized = 0; playerBeingOptimized < NumNonChancePlayers; playerBeingOptimized++)
                    TabbedText.WriteLine($"Player {playerBeingOptimized} {results[playerBeingOptimized]}");
                TabbedText.WriteLine($"Cumulative milliseconds per iteration {((StrategiesDeveloperStopwatch.ElapsedMilliseconds / ((double)iteration)))}");
                TabbedText.TabUnindent();
            }
        }

        private void CalculateDiscountingAdjustments()
        {
            EvolutionSettings.CalculateGamma();
            double positivePower = Math.Pow(Status.IterationNumDouble, EvolutionSettings.Discounting_Alpha);
            double negativePower = Math.Pow(Status.IterationNumDouble, EvolutionSettings.Discounting_Beta);
            AverageStrategyAdjustment = EvolutionSettings.Discounting_Gamma_ForIteration(Status.IterationNum);
            AverageStrategyAdjustmentAsPctOfMax = EvolutionSettings.Discounting_Gamma_AsPctOfMax(Status.IterationNum);
            if (AverageStrategyAdjustment < 1E-100)
                AverageStrategyAdjustment = 1E-100;
        }

        /// <summary>
        /// Performs an iteration of vanilla counterfactual regret minimization.
        /// </summary>
        /// <param name="historyPoint">The game tree, pointing to the particular point in the game where we are located</param>
        /// <param name="playerBeingOptimized">0 for first player, etc. Note that this corresponds in Lanctot to 1, 2, etc. We are using zero-basing for player index (even though we are 1-basing actions).</param>
        /// <returns></returns>
        public GeneralizedVanillaUtilities GeneralizedVanillaCFR(in HistoryPoint historyPoint, byte playerBeingOptimized, Span<double> piValues, Span<double> avgStratPiValues, int distributorChanceInputs)
        {
            //if (usePruning && ShouldPruneIfPruning(piValues))
            //    return new GeneralizedVanillaUtilities { AverageStrategyVsAverageStrategy = 0, BestResponseToAverageStrategy = 0, CurrentVsCurrent = 0 };
            IGameState gameStateForCurrentPlayer = GetGameState(in historyPoint);
            GameStateTypeEnum gameStateType = gameStateForCurrentPlayer.GetGameStateType();
            if (gameStateType == GameStateTypeEnum.FinalUtilities)
            {
                FinalUtilitiesNode finalUtilities = (FinalUtilitiesNode)gameStateForCurrentPlayer;
                double playerBeingOptimizedUtility = finalUtilities.Utilities[playerBeingOptimized];
                if (double.IsNaN(playerBeingOptimizedUtility))
                    throw new Exception();
                return new GeneralizedVanillaUtilities { AverageStrategyVsAverageStrategy = playerBeingOptimizedUtility, BestResponseToAverageStrategy = playerBeingOptimizedUtility, CurrentVsCurrent = playerBeingOptimizedUtility };
            }
            else if (gameStateType == GameStateTypeEnum.Chance)
            {
                return GeneralizedVanillaCFR_ChanceNode(in historyPoint, playerBeingOptimized, piValues, avgStratPiValues, distributorChanceInputs);
            }
            else
                return GeneralizedVanillaCFR_DecisionNode(in historyPoint, playerBeingOptimized, piValues, avgStratPiValues, distributorChanceInputs);
        }

        bool IncludeAsteriskForBestResponseInTrace = false;

        private GeneralizedVanillaUtilities GeneralizedVanillaCFR_DecisionNode(in HistoryPoint historyPoint, byte playerBeingOptimized,
            Span<double> piValues, Span<double> avgStratPiValues, int distributorChanceInputs)
        {
            double inversePi = GetInversePiValue(piValues, playerBeingOptimized);
            double inversePiAvgStrat = GetInversePiValue(avgStratPiValues, playerBeingOptimized);
            Span<double> nextPiValues = stackalloc double[MaxNumMainPlayers];
            Span<double> nextAvgStratPiValues = stackalloc double[MaxNumMainPlayers];
            //var actionsToHere = historyPoint.GetActionsToHere(Navigation);
            //var historyPointString = historyPoint.ToString();

            IGameState gameStateForCurrentPlayer = GetGameState(in historyPoint);
            var informationSet = (InformationSetNode)gameStateForCurrentPlayer;
            byte decisionNum = informationSet.DecisionIndex;
            byte playerMakingDecision = informationSet.PlayerIndex;
            byte numPossibleActions = NumPossibleActionsAtDecision(decisionNum);

            Span<double> actionProbabilities = stackalloc double[numPossibleActions];
            byte? alwaysDoAction = GameDefinition.DecisionsExecutionOrder[decisionNum].AlwaysDoAction;
            if (alwaysDoAction != null)
                ActionProbabilityUtilities.SetProbabilitiesToAlwaysDoParticularAction(numPossibleActions,
                    actionProbabilities, (byte)alwaysDoAction);
            else
            {
                if (EvolutionSettings.CFRBR && playerMakingDecision != playerBeingOptimized)
                    informationSet.GetBestResponseProbabilities(actionProbabilities);
                else
                    informationSet.GetCurrentProbabilities(actionProbabilities, playerMakingDecision != playerBeingOptimized);
            }
            Span<double> expectedValueOfAction = stackalloc double[numPossibleActions];
            double expectedValue = 0;
            GeneralizedVanillaUtilities result = default;
            for (byte action = 1; action <= numPossibleActions; action++)
            {
                int distributorChanceInputsNext = distributorChanceInputs;
                if (informationSet.Decision.DistributorChanceInputDecision)
                    distributorChanceInputsNext += action * informationSet.Decision.DistributorChanceInputDecisionMultiplier;
                double probabilityOfAction = actionProbabilities[action - 1];
                bool prune = playerBeingOptimized != playerMakingDecision && probabilityOfAction == 0;
                if (!prune)
                {
                    double probabilityOfActionAvgStrat = informationSet.GetAverageStrategy(action);
                    GetNextPiValues(piValues, playerMakingDecision, probabilityOfAction, false,
                        nextPiValues); // reduce probability associated with player being optimized, without changing probabilities for other players
                    GetNextPiValues(avgStratPiValues, playerMakingDecision, probabilityOfActionAvgStrat, false, nextAvgStratPiValues);
                    if (TraceCFR)
                    {
                        TabbedText.WriteLine(
                            $"({informationSet.Decision.Name}) code {informationSet.DecisionByteCode} optimizing player {playerBeingOptimized}  {(playerMakingDecision == playerBeingOptimized ? "own decision" : "opp decision")} action {action} probability {probabilityOfAction} ...");
                        TabbedText.TabIndent();
                    }
                    HistoryPoint nextHistoryPoint;
                    if (Navigation.LookupApproach != InformationSetLookupApproach.PlayGameDirectly && informationSet.Decision.IsReversible)
                        nextHistoryPoint = historyPoint.SwitchToBranch(Navigation, action, informationSet.Decision, informationSet.DecisionIndex);
                    else
                        nextHistoryPoint = historyPoint.GetBranch(Navigation, action, informationSet.Decision, informationSet.DecisionIndex);
                    GeneralizedVanillaUtilities innerResult = GeneralizedVanillaCFR(in nextHistoryPoint, playerBeingOptimized, nextPiValues, nextAvgStratPiValues, distributorChanceInputsNext);
                    expectedValueOfAction[action - 1] = innerResult.CurrentVsCurrent;

                    if (playerMakingDecision == playerBeingOptimized)
                    {
                        if (informationSet.BestResponseAction == action)
                        {
                            // Because this is the best response action, the best response utility that we get should be propagated back directly.
                            result.BestResponseToAverageStrategy = innerResult.BestResponseToAverageStrategy;
                        }
                        // Meanwhile, we need to determine the best response action in the next iteration. To do this, we need to figure out which action, when weighted by the probability we play to this information set, produces the highest best response on average. Note that we may get different inner results for the same action, because the next information set will differ depending on the other player's information set.
                        informationSet.IncrementBestResponse(action, inversePiAvgStrat, innerResult.BestResponseToAverageStrategy);
                        // The other result utilities are just the probability adjusted utilities. 
                        result.CurrentVsCurrent += probabilityOfAction * innerResult.CurrentVsCurrent;
                        result.AverageStrategyVsAverageStrategy += probabilityOfActionAvgStrat * innerResult.AverageStrategyVsAverageStrategy;
                    }
                    else
                    {
                        // This isn't the decision being optimized, so we essentially just need to pass through the player being optimized's utilities, weighting by the probability for each action (which will depend on whether we are using average strategy or current to calculate the utilities).
                        result.IncrementBasedOnNotYetProbabilityAdjusted(ref innerResult, probabilityOfActionAvgStrat, probabilityOfAction);
                    }
                    expectedValue += probabilityOfAction * expectedValueOfAction[action - 1];

                    if (TraceCFR)
                    {
                        TabbedText.TabUnindent();
                        TabbedText.WriteLine(
                            $"... action {action}{(informationSet.BestResponseAction == action && IncludeAsteriskForBestResponseInTrace ? "*" : "")} expected value {expectedValueOfAction[action - 1]} best response expected value {result.BestResponseToAverageStrategy} cum expected value {expectedValue}{(action == numPossibleActions && IncludeAsteriskForBestResponseInTrace ? "*" : "")}");

                    }
                    if (Navigation.LookupApproach != InformationSetLookupApproach.PlayGameDirectly && informationSet.Decision.IsReversible)
                    {
                        GameDefinition.ReverseSwitchToBranchEffects(informationSet.Decision, in nextHistoryPoint);
                    }
                } // not pruning
            } // for each action
            if (playerMakingDecision == playerBeingOptimized)
            {
                for (byte action = 1; action <= numPossibleActions; action++)
                {
                    double pi = piValues[playerBeingOptimized];
                    var regret = (expectedValueOfAction[action - 1] - expectedValue);
                    // NOTE: With multiplicative weights, we do NOT discount regrets, because we're normalizing regrets at the end of each iteration.
                    double piAdj = pi;
                    if (pi < InformationSetNode.SmallestProbabilityRepresented)
                        piAdj = InformationSetNode.SmallestProbabilityRepresented;
                    double contributionToAverageStrategy = piAdj * actionProbabilities[action - 1];
                    if (EvolutionSettings.ParallelOptimization)
                    {
                        informationSet.IncrementLastRegret_Parallel(action, regret * inversePi, inversePi);
                        informationSet.IncrementLastCumulativeStrategyIncrements_Parallel(action, contributionToAverageStrategy);
                    }
                    else
                    {
                        informationSet.IncrementLastRegret(action, regret * inversePi, inversePi);
                        informationSet.IncrementLastCumulativeStrategyIncrements(action, contributionToAverageStrategy);
                    }
                    if (TraceCFR)
                    {
                        TabbedText.WriteLine($"PiValues {piValues[0]} {piValues[1]} pi for optimized {pi}");
                        //TabbedText.WriteLine($"Regrets: Action {action} regret {regret} prob-adjust {inversePi * regret} new regret {informationSet.GetCumulativeRegret(action)} strategy inc {pi * actionProbabilities[action - 1]} new cum strategy {informationSet.GetCumulativeStrategy(action)}");
                        TabbedText.WriteLine(
                            $"Regrets ({informationSet.Decision.Name} {informationSet.InformationSetNodeNumber}): Action {action} probability {actionProbabilities[action - 1]} regret {regret} inversePi {inversePi} avg_strat_increment {contributionToAverageStrategy} cum_strategy {informationSet.GetLastCumulativeStrategyIncrement(action)}");
                    }
                }
            }
            return result;
        }

        private GeneralizedVanillaUtilities GeneralizedVanillaCFR_ChanceNode(in HistoryPoint historyPoint, byte playerBeingOptimized,
            Span<double> piValues, Span<double> avgStratPiValues, int distributorChanceInputs)
        {
            GeneralizedVanillaUtilities result = default;
            IGameState gameStateForCurrentPlayer = GetGameState(in historyPoint);
            ChanceNode chanceNode = (ChanceNode)gameStateForCurrentPlayer;
            byte numPossibleActions = NumPossibleActionsAtDecision(chanceNode.DecisionIndex);
            byte numPossibleActionsToExplore = numPossibleActions;
            if (EvolutionSettings.DistributeChanceDecisions && chanceNode.Decision.DistributedChanceDecision)
                numPossibleActionsToExplore = 1;

            bool doParallel = numPossibleActionsToExplore > 1 && EvolutionSettings.ParallelOptimization && Parallelizer.ParallelDepth < EvolutionSettings.MaxParallelDepth;
            if (doParallel)
            {
                var historyPointCopy = historyPoint.ToStorable(); // This is costly but needed given anonymous method below (because ref struct can't be accessed there), so we do this only if really parallelizing.
                var piValues2 = piValues.ToArray();
                var avgStratPiValues2 = avgStratPiValues.ToArray();
                Parallelizer.GoByte(doParallel, 1,
                    (byte)(numPossibleActionsToExplore + 1),
                    action =>
                    {
                        var historyPointCopy2 = historyPointCopy.DeepCopyToRefStruct(); // we need to do a deep copy (despite the costly copy above) because each thread needs its own copy
                        //Debug.WriteLine($"{action}: Chance node for {chanceNode.DecisionIndex}: {historyPointCopy2.HistoryToPoint.ToString()}");
                        GeneralizedVanillaUtilities probabilityAdjustedInnerResult = GeneralizedVanillaCFR_ChanceNode_NextAction(in historyPointCopy2, playerBeingOptimized, piValues2,
                                avgStratPiValues2, chanceNode, action, distributorChanceInputs);
                        result.IncrementBasedOnProbabilityAdjusted(ref probabilityAdjustedInnerResult);

                    });
            }
            else
            {
                for (byte action = 1; action < (byte)numPossibleActionsToExplore + 1; action++)
                {
                    GeneralizedVanillaUtilities probabilityAdjustedInnerResult = GeneralizedVanillaCFR_ChanceNode_NextAction(in historyPoint, playerBeingOptimized, piValues,
                            avgStratPiValues, chanceNode, action, distributorChanceInputs);
                    result.IncrementBasedOnProbabilityAdjusted(ref probabilityAdjustedInnerResult);
                }
            }

            return result;
        }

        private GeneralizedVanillaUtilities GeneralizedVanillaCFR_ChanceNode_NextAction(in HistoryPoint historyPoint, byte playerBeingOptimized, Span<double> piValues, Span<double> avgStratPiValues, ChanceNode chanceNode, byte action, int distributorChanceInputs)
        {
            Span<double> nextPiValues = stackalloc double[MaxNumMainPlayers];
            Span<double> nextAvgStratPiValues = stackalloc double[MaxNumMainPlayers];
            double actionProbability = chanceNode.GetActionProbability(action, distributorChanceInputs);
            int distributorChanceInputsNext = distributorChanceInputs;
            if (chanceNode.Decision.DistributorChanceInputDecision)
                distributorChanceInputsNext += action * chanceNode.Decision.DistributorChanceInputDecisionMultiplier;
            if (EvolutionSettings.DistributeChanceDecisions && chanceNode.Decision.DistributedChanceDecision)
                actionProbability = 1.0;
            GetNextPiValues(piValues, playerBeingOptimized, actionProbability, true,
                nextPiValues);
            GetNextPiValues(avgStratPiValues, playerBeingOptimized, actionProbability, true,
                nextAvgStratPiValues);
            HistoryPoint nextHistoryPoint;
            if (Navigation.LookupApproach != InformationSetLookupApproach.PlayGameDirectly && chanceNode.Decision.IsReversible)
                nextHistoryPoint = historyPoint.SwitchToBranch(Navigation, action, chanceNode.Decision, chanceNode.DecisionIndex);
            else
                nextHistoryPoint = historyPoint.GetBranch(Navigation, action, chanceNode.Decision, chanceNode.DecisionIndex);
            if (TraceCFR)
            {
                TabbedText.WriteLine(
                    $"Chance code {chanceNode.DecisionByteCode} ({chanceNode.Decision.Name}) action {action} probability {actionProbability} ...");
                TabbedText.TabIndent();
            }
            GeneralizedVanillaUtilities result =
                GeneralizedVanillaCFR(in nextHistoryPoint, playerBeingOptimized, nextPiValues, nextAvgStratPiValues, distributorChanceInputsNext);
            if (TraceCFR)
            {
                TabbedText.TabUnindent();
                TabbedText.WriteLine(
                    $"... action {action} value {result.CurrentVsCurrent} probability {actionProbability} expected value contribution {result.CurrentVsCurrent * actionProbability}");
            }
            result.MakeProbabilityAdjusted(actionProbability);
            if (Navigation.LookupApproach != InformationSetLookupApproach.PlayGameDirectly && chanceNode.Decision.IsReversible)
                GameDefinition.ReverseSwitchToBranchEffects(chanceNode.Decision, in nextHistoryPoint);

            return result;
        }

        #endregion

        #region Principal component analysis

        /// <summary>
        /// Copies the model variables for each player into a separate float array for that player. 
        /// </summary>
        /// <returns></returns>
        public override float[][] GetCurrentModelVariablesForPCA()
        {
            float[][] result = new float[NumNonChancePlayers][];
            for (int p = 0; p < NumNonChancePlayers; p++)
            {
                List<float> modelVariablesForPlayer = new List<float>();
                foreach (var informationSet in InformationSets)
                {
                    if (informationSet.PlayerIndex == p)
                    {
                        for (int a = 1; a <= informationSet.NumPossibleActions; a++)
                        {
                            bool useRegrets = true; // the reason for using regrets is that it improves the model for determining whether something should be 0. still, regrets may be different orders of magnitude
                            if (useRegrets)
                            {
                                double regret = informationSet.GetCumulativeRegret(a);
                                modelVariablesForPlayer.Add((float)regret);
                            }
                            else
                            {
                                double probability = Math.Round(informationSet.GetCurrentProbability((byte) a, false), 2); // TODO: Make sure all the probabilities add up to 1. Maybe only round at the extremes (<0.01, >0.99) and always balance it out, so that we round the same number high and low. 
                                modelVariablesForPlayer.Add((float)probability);
                            }
                        }
                    }
                }
                result[p] = modelVariablesForPlayer.ToArray();
            }
            return result;
        }

        /// <summary>
        /// Update the model to use the specified principal component weights. In general, this should effectively do
        /// the reverse of <see cref="GetCurrentModelVariablesForPCA"/>, using the principal component results to
        /// update the current model.
        /// </summary>
        /// <param name="principalComponentWeightsForEachPlayer"></param>
        public override Task SetModelToPrincipalComponentWeights(List<double>[] principalComponentWeightsForEachPlayer)
        {
            for (int p = 0; p < NumNonChancePlayers; p++)
            {
                double[] modelVariablesForPlayer = PCAResultsForEachPlayer[p].PrincipalComponentsToVariable(principalComponentWeightsForEachPlayer[p].ToArray());
                int index = 0;
                foreach (var informationSet in InformationSets)
                {
                    if (informationSet.PlayerIndex == p)
                    {
                        for (int a = 1; a <= informationSet.NumPossibleActions; a++)
                        {
                            informationSet.SetCumulativeRegret(a, modelVariablesForPlayer[index++]);
                        }
                        informationSet.SetToMixedStrategyBasedOnRegretMatchingCumulativeRegrets(true);
                    }
                }
            }
            return Task.CompletedTask;
        }
        #endregion

    }
}