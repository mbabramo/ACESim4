using ACESimBase.GameSolvingSupport;
using ACESimBase.GameSolvingSupport.GameTree;
using ACESimBase.GameSolvingSupport.PostIterationUpdater;
using ACESimBase.GameSolvingSupport.Settings;
using ACESimBase.GameSolvingSupport.SolverSpecificSupport;
using ACESimBase.Util.ArrayProcessing;
using ACESimBase.Util.ArrayProcessing.Slots;
using ACESimBase.Util.Debugging;
using ACESimBase.Util.Parallelization;
using JetBrains.Annotations;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
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
        bool VerifySymmetry = false; // if true, symmetry is verified instead of used as a way of saving time,

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
                Parallel.For(0, numInformationSets, n => InformationSets[n].PostIterationUpdates(iteration, PostIterationUpdater, EvolutionSettings.SimulatedAnnealing_RandomAverageStrategyAdjustment(iteration, InformationSets[n]), false, false, 1.0, pruneOpponentStrategyBelow, predeterminePrunability, EvolutionSettings.GeneralizedVanillaAddTremble, EvolutionSettings.Algorithm == GameApproximationAlgorithm.GeneralizedVanilla && EvolutionSettings.CFR_OpponentSampling, randomNumberToSelectSingleOpponentAction(n)));
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

            Parallelizer.Go(EvolutionSettings.ParallelOptimization, 0, numInformationSets, n => InformationSets[n].PostIterationUpdates(iteration, PostIterationUpdater, averageStrategyAdjustment, normalizeCumulativeStrategyIncrements, resetPreviousCumulativeStrategyIncrements, continuousRegretsDiscountingAdjustment, pruneOpponentStrategyBelow, predeterminePrunability, EvolutionSettings.GeneralizedVanillaAddTremble, EvolutionSettings.Algorithm == GameApproximationAlgorithm.GeneralizedVanilla && EvolutionSettings.CFR_OpponentSampling, randomNumberToSelectSingleOpponentAction(n)));
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


        public double[] GetInformationSetValues()
        {
            List<double> result = new List<double>();
            for (int x = 0; x < InformationSets.Count; x++)
            {
                var infoSet = InformationSets[x];
                for (byte action = 1; action <= infoSet.NumPossibleActions; action++)
                {
                    result.Add(infoSet.GetCurrentProbability(action, false));
                }
            };
            return result.ToArray();
        }

        public void SetInformationSetValues(double[] array)
        {
            int i = 0;
            for (int x = 0; x < InformationSets.Count; x++)
            {
                var infoSet = InformationSets[x];
                for (byte action = 1; action <= infoSet.NumPossibleActions; action++)
                {
                    double value = array[i++];
                    infoSet.SetCurrentAndAverageStrategyValues(action, value, value);
                }
            }
        }

        #endregion


        #region Unrolled preparation

            // We can achieve considerable improvements in performance by unrolling the algorithm. Instead of traversing the tree, we simply have a series of simple commands that can be processed on an array. The challenge is that we need to create this series of commands. This section prepares for the copying of data between information sets and the array. We can compare the outcomes of the regular algorithm and the unrolled version (which should always be equal) by using EvolutionSettings.TraceCFR = true.

        private ArraySlots Unroll_Slots;
        private ArrayCommandList Unroll_Commands;
        private ArrayCommandListRunner Unroll_CommandListRunner;
        private static ArrayCommandList Unroll_Commands_Cached = null;
        private static ArrayCommandListRunner Unroll_CommandListRunner_Cached = null;
        private static (int Chance, int Decision, int Final) GameTreeNodeCount_Cached = default;
        private int Unroll_SizeOfArray;
        private static int Unroll_SizeOfArray_Cached = -1;
        private int[] Unroll_RepeatedRoundParamPiIndices;
        private int[] Unroll_RepeatedRoundParamAvgStratPiIndices;


        int UnrollCheckpointIteration = -1; // if using checkpoints to debug unrolling, set an iteration (such as 1) here
        public static void ClearCache()
        {
            Unroll_Commands_Cached = null;
            Unroll_CommandListRunner_Cached = null;
            Unroll_SizeOfArray_Cached = -1;
            GameTreeNodeCount_Cached = default;
        }

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
                    var checkpointsValuesOnly = String.Join("\r\n", Enumerable.Range(0, Unroll_Commands.Checkpoints.Count).Select(x => $"{x}: {Unroll_Commands.Checkpoints[x].Value}"));
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
                if (EvolutionSettings.TraceCFR)
                { // only trace through iteration
                    string resultWithReplacementOfArray = TraceCommandList(array);
                    TabbedText.WriteLine(resultWithReplacementOfArray);
                }
                var result = await ConsiderGeneratingReports(iteration,
                    () =>
                        $"{GameDefinition.OptionSetName} Iteration {iteration} Overall milliseconds per iteration {((StrategiesDeveloperStopwatch.ElapsedMilliseconds / ((double)iteration)))}");
                ConsiderModelSuccessTrackingForIteration(iteration);
                reportCollection.Add(result);
                targetMet = Status.BestResponseTargetMet(EvolutionSettings.BestResponseTarget);
                if (EvolutionSettings.PruneOnOpponentStrategy && EvolutionSettings.PredeterminePrunabilityBasedOnRelativeContributions)
                    CalculateReachProbabilitiesAndPrunability(EvolutionSettings.ParallelOptimization);
                ReinitializeInformationSetsIfNecessary(iteration);
            }
            TabbedText.SetConsoleProgressString(null);
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

        List<(string name, double value)> CurrentCheckpoints = new List<(string name, double value)>();
        public void RecordCheckpoint(string name, double value)
        {
            CurrentCheckpoints.Add((name, value));
        }

        public string GetCheckpointValuesOnly()
        {
            return String.Join("\r\n", Enumerable.Range(0, CurrentCheckpoints.Count).Select(x => $"{x}: {CurrentCheckpoints[x].value}"));
        }

        string TraceCFRTemplate = "";
        public string TraceCommandList(double[] array)
        {
            string replaced = StringUtil.ReplaceArrayDesignationWithArrayItem(TraceCFRTemplate, array);
            return replaced;
        }

        private void Unroll_CreateUnrolledCommandList()
        {
            int numRetries = 0;
            PerformanceTimer s = new();
            s.Start();
        retry:
            try
            {
                const int max_num_commands = 10_000_000;
                Unroll_InitializeInitialArrayIndices();
                if (EvolutionSettings.ReuseUnrolledAlgorithm && Unroll_CommandListRunner_Cached != null)
                {
                    (int Chance, int Decision, int Final) gameTreeNodeCount = CountGameTreeNodes();
                    if (gameTreeNodeCount == GameTreeNodeCount_Cached)
                    {
                        Unroll_Commands = Unroll_Commands_Cached;
                        Unroll_CommandListRunner = Unroll_CommandListRunner_Cached;
                        Unroll_SizeOfArray = Unroll_SizeOfArray_Cached;
                        TabbedText.WriteLine($"Using cached unrolled commands.");
                        return;
                    }
                }
                Unroll_CommandListRunner_Cached = null; // free memory
                TabbedText.WriteLine($"Unrolling commands...");
                Unroll_Commands = new ArrayCommandList(max_num_commands, Unroll_InitialArrayIndex);
                Unroll_Commands.UseOrderedSourcesAndDestinations = EvolutionSettings.UnrollTemplateIdenticalRanges || EvolutionSettings.UnrollTemplateRepeatedRanges;
                Unroll_Commands.Recorder.OnEmit = null;
                //    (ci, cmd, isReplay) =>
                //{
                //    if (ci == 1257 || ci == 1297)
                //        ACESimBase.Util.Debugging.TabbedText.WriteLine(
                //            $"[EMIT] ci={ci} {cmd.CommandType} idx={cmd.Index} src={cmd.SourceIndex} replay={isReplay}");
                //};
                Unroll_Commands.Recorder.BreakOnPredicate = null; //  (ci, cmd) => ci == 1257; // breakpoint on authoring
                Unroll_Commands.Recorder.EnableConditionalEmitLogging(
                    includeCommandType: t =>
                        t == ArrayCommandType.IncrementDepth ||
                        t == ArrayCommandType.DecrementDepth ||
                        t == ArrayCommandType.If ||
                        t == ArrayCommandType.EndIf,
                    extraPredicate: null,           // or narrow by command index
                    includeReplayFlag: true,
                    logEveryNth: 1
                ); // DEBUG

                // Following is DEBUG
                // EXPERIMENT A (prove/disprove hypothesis #1): turn on tracing only, keep behavior the same
                Unroll_Commands.Recorder.StructuralReplayTracing = true;
                Unroll_Commands.Recorder.StructuralPolicy = CommandRecorder.ReplayStructuralPolicy.Legacy;

                // EXPERIMENT B (potential fix trial): guard structural ops to recorded stream during replay
                // (comment the next line out to return to legacy behavior)
                // Unroll_Commands.Recorder.StructuralPolicy = CommandRecorder.ReplayStructuralPolicy.GuardStructuralOps;

                // Alternative EXPERIMENT C (hard proof): assert/fail immediately if recorded next != expected
                // Unroll_Commands.Recorder.StructuralPolicy = CommandRecorder.ReplayStructuralPolicy.AssertStructuralOpsMatch;


                ActionStrategy = ActionStrategies.CurrentProbability;
                HistoryPoint historyPoint = GetStartOfGameHistoryPoint();
                List<int> resultIndices = new List<int>();

                Unroll_Commands.StartCommandChunk(false, null, "Iteration");
                if (EvolutionSettings.IncludeCommentsWhenUnrolling)
                    Unroll_Commands.InsertComment("--- BEGIN ITERATION ARRAY BUILD ---");

                bool takeSymmetryShortcut = NumNonChancePlayers == 2 && GameDefinition.GameIsSymmetric() && TakeShortcutInSymmetricGames;
                int stuffToDeleteTracing = TabbedText.AccumulatedText.Length;
                for (byte p = 0; p < NumNonChancePlayers; p++)
                {
                    if (takeSymmetryShortcut && p == 1)
                        continue;
                    Unroll_Commands.StartCommandChunk(false, null, "Optimizing player " + p.ToString());
                    if (EvolutionSettings.IncludeCommentsWhenUnrolling)
                        Unroll_Commands.InsertComment($"Player {p}: optimization block start");
                    if (EvolutionSettings.TraceCFR)
                        TabbedText.WriteLine($"Unrolling for Player {p}");
                    Unroll_GeneralizedVanillaCFR(in historyPoint, p, Unroll_InitialPiValuesIndices, Unroll_InitialAvgStratPiValuesIndices, Unroll_IterationResultForPlayersIndices[p], true, takeSymmetryShortcut || p == NumNonChancePlayers - 1);

                    if (EvolutionSettings.IncludeCommentsWhenUnrolling)
                        Unroll_Commands.InsertComment($"Player {p}: optimization block end");
                    Unroll_Commands.EndCommandChunk();
                }
                Unroll_Commands.EndCommandChunk();
                if (EvolutionSettings.TraceCFR)
                {
                    TraceCFRTemplate = TabbedText.AccumulatedText.ToString()[stuffToDeleteTracing..];
                }
                Unroll_SizeOfArray = Unroll_Commands.VirtualStackSize;
                Unroll_CommandListRunner = Unroll_Commands.GetCompiledRunner(kind: EvolutionSettings.Unroll_ChunkExecutorKind, null);
                Unroll_CommandListRunner.DebugBreakOnCommandIndices = null; // new HashSet<int> { 1257, 1297 };
                if (EvolutionSettings.ReuseUnrolledAlgorithm)
                {
                    Unroll_Commands_Cached = Unroll_Commands;
                    Unroll_CommandListRunner_Cached = Unroll_CommandListRunner;
                    Unroll_SizeOfArray_Cached = Unroll_SizeOfArray;
                    GameTreeNodeCount_Cached = CountGameTreeNodes();
                }
            }
            catch (OutOfMemoryException ex)
            {
                numRetries++;
                if (numRetries <= 100)
                {
                    int delayPeriod = 60_000 + (int)(60000.0 * new Random((int)DateTime.Now.Ticks).NextDouble());
                    TabbedText.WriteLine($"Delaying {delayPeriod} milliseconds following out-of-memory exception");
                    Task.Delay(delayPeriod).Wait(); // wait a minute or so before retrying
                    goto retry;
                }
                else throw new Exception("Out of memory, retries failed", ex);
            }
            string performanceString = s.End();
            TabbedText.WriteLine($"... {performanceString} (using {Unroll_Commands.VirtualStackSize} array size and {Unroll_Commands.MaxCommandIndex} commands)");
        }

        private void Unroll_ExecuteUnrolledCommands(double[] array, bool copyChanceAndFinalUtilities)
        {
            try
            {
                Unroll_CopyInformationSetsToArray(array, copyChanceAndFinalUtilities);
                Unroll_CommandListRunner.Run(Unroll_Commands, array, copyBackToOriginalData: true, trace: EvolutionSettings.TraceCFR);
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
            for (int i = 0; i < ChanceNodes.Count; i++)
            {
                int numItems = ChanceNodes[i].Decision.NumPossibleActions;
                Unroll_ChanceNodesIndices[i] = index;
                index += numItems;
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

            Unroll_RepeatedRoundParamPiIndices = new int[NumNonChancePlayers];
            for (int p = 0; p < NumNonChancePlayers; p++)
                Unroll_RepeatedRoundParamPiIndices[p] = index++;

            Unroll_RepeatedRoundParamAvgStratPiIndices = new int[NumNonChancePlayers];
            for (int p = 0; p < NumNonChancePlayers; p++)
                Unroll_RepeatedRoundParamAvgStratPiIndices[p] = index++;

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

    public void Unroll_GeneralizedVanillaCFR(
        in HistoryPoint historyPoint,
        byte playerBeingOptimized,
        int[] piValues,
        int[] avgStratPiValues,
        int[] resultArray,
        bool algorithmIsLowestDepth,
        bool completeCommandList)
    {
        //Unroll_Commands.InsertComment("[DEPTH_OPEN] Top:Unroll_GeneralizedVanillaCFR"); 
        //Unroll_Commands.Recorder.MarkNextDepth("Top:Unroll_GeneralizedVanillaCFR");
        Unroll_Commands.IncrementDepth();

        IGameState gameStateForCurrentPlayer = GetGameState(in historyPoint);
        GameStateTypeEnum gameStateType = gameStateForCurrentPlayer.GetGameStateType();

        if (gameStateType == GameStateTypeEnum.FinalUtilities)
        {
            _repeatTpl?.CloseAtBoundary();

            FinalUtilitiesNode finalUtilities = (FinalUtilitiesNode)gameStateForCurrentPlayer;

            var finalUtil = Unroll_Commands.CopyToNew(
                new OsIndex(Unroll_GetFinalUtilitiesNodesIndex(finalUtilities.FinalUtilitiesNodeNumber, playerBeingOptimized)));

            Unroll_Commands.CopyToExisting(new VsIndex(resultArray[0]), finalUtil);
            Unroll_Commands.CopyToExisting(new VsIndex(resultArray[1]), finalUtil);
            Unroll_Commands.CopyToExisting(new VsIndex(resultArray[2]), finalUtil);
        }
        else if (gameStateType == GameStateTypeEnum.Chance)
        {
            Unroll_GeneralizedVanillaCFR_ChanceNode(
                in historyPoint,
                playerBeingOptimized,
                piValues,
                avgStratPiValues,
                resultArray,
                algorithmIsLowestDepth);
        }
        else
        {
            Unroll_GeneralizedVanillaCFR_DecisionNode(
                in historyPoint,
                playerBeingOptimized,
                piValues,
                avgStratPiValues,
                resultArray,
                algorithmIsLowestDepth);
        }

        Unroll_Commands.InsertComment("[DEPTH_CLOSE] Top:Unroll_GeneralizedVanillaCFR");
        Unroll_Commands.DecrementDepth(completeCommandList);

    }

        private void Unroll_GeneralizedVanillaCFR_ChanceNode(
            in HistoryPoint historyPoint,
            byte playerBeingOptimized,
            int[] piValues,
            int[] avgStratPiValues,
            int[] resultArray,
            bool algorithmIsLowestDepth)
        {
            IGameState gameStateForCurrentPlayer = GetGameState(in historyPoint);
            ChanceNode chanceNode = (ChanceNode)gameStateForCurrentPlayer;

            Unroll_CloseRepeatedWindowIfBoundaryHere(chanceNode);
            Unroll_EnsureTemplates();

            if (algorithmIsLowestDepth)
            {
                // Copy OS arrays -> VS arrays using typed CopyToNew(OsIndex)
                var tmp = new int[piValues.Length];
                for (int i = 0; i < tmp.Length; i++)
                    tmp[i] = Unroll_Commands.CopyToNew(new OsIndex(piValues[i])).Value;
                piValues = tmp;

                tmp = new int[avgStratPiValues.Length];
                for (int i = 0; i < tmp.Length; i++)
                    tmp[i] = Unroll_Commands.CopyToNew(new OsIndex(avgStratPiValues[i])).Value;
                avgStratPiValues = tmp;
            }

            // Unroll_Commands.InsertComment("[DEPTH_OPEN] ChanceNode");
            // Unroll_Commands.Recorder.MarkNextDepth("ChanceNode");
            Unroll_Commands.IncrementDepth();

            if (EvolutionSettings.IncludeCommentsWhenUnrolling)
                Unroll_Commands.InsertComment($"Chance node {chanceNode.Decision.Name} (node {chanceNode.ChanceNodeNumber})");

            if (EvolutionSettings.UnrollTemplateRepeatedRanges && chanceNode.Decision.BeginRepeatedRange)
            {
                using (_repeatTpl.Open(chanceNode.Decision.Name))
                {
                    var orig = resultArray;

                    // Stage result indices into stable VS slots
                    ParameterFrame frame = new ParameterFrame(Unroll_Commands, orig.Length);
                    frame.SetFromVirtualStack(orig);

                    var staged = new int[frame.Slots.Length];
                    for (int i = 0; i < staged.Length; i++) staged[i] = frame.Slots[i].Val();

                    Unroll_Chance_EmitAllActions(in historyPoint, chanceNode, playerBeingOptimized,
                                                 piValues, avgStratPiValues, staged, algorithmIsLowestDepth);

                    // Copy results back (typed CopyToExisting)
                    for (int i = 0; i < orig.Length; i++)
                        Unroll_Commands.CopyToExisting(new VsIndex(orig[i]), new VsIndex(staged[i]));
                }
            }
            else
            {
                Unroll_Chance_EmitAllActions(in historyPoint, chanceNode, playerBeingOptimized,
                                             piValues, avgStratPiValues, resultArray, algorithmIsLowestDepth);
            }

            Unroll_Commands.InsertComment("[DEPTH_CLOSE] ChanceNode");
            Unroll_Commands.DecrementDepth();
        }

        private void Unroll_Chance_EmitAllActions(
            in HistoryPoint historyPoint,
            ChanceNode chanceNode,
            byte playerBeingOptimized,
            int[] piValues,
            int[] avgStratPiValues,
            int[] resultArray,
            bool algorithmIsLowestDepth)
        {
            Unroll_EnsureTemplates();

            byte numPossibleActions = chanceNode.Decision.NumPossibleActions;

            bool routeToOrderedBase =
                algorithmIsLowestDepth
                || (_repeatTpl != null && _repeatTpl.IsOpen)
                || (EvolutionSettings.UnrollTemplateRepeatedRanges && chanceNode.Decision.BeginRepeatedRange);

            bool useIdentical =
                EvolutionSettings.UnrollTemplateIdenticalRanges
                && chanceNode.Decision.GameStructureSameForEachAction;

            if (useIdentical)
            {
                using (_identicalTpl.BeginSet(chanceNode.Decision.Name))
                {
                    for (byte action = 1; action <= numPossibleActions; action++)
                    {
                        Unroll_Commands.Recorder.MarkNextDepth("IdenticalAction");

                        using (_identicalTpl.BeginAction($"a={action}"))
                        {
                            int[] probabilityAdjustedInnerResult = Unroll_Commands.NewZeroArray(3);

                            var nextHp = historyPoint;
                            Unroll_GeneralizedVanillaCFR_ChanceNode_NextAction(
                                in nextHp,
                                playerBeingOptimized,
                                piValues,
                                avgStratPiValues,
                                chanceNode,
                                action,
                                probabilityAdjustedInnerResult,
                                algorithmIsLowestDepth: false,
                                suppressCommentsForRepeat: true);

                            for (int k = 0; k < 3; k++)
                                Unroll_AddToResult(resultArray[k], canRouteToOriginal: routeToOrderedBase, probabilityAdjustedInnerResult[k]);
                        }
                    }
                }
            }
            else
            {
                for (byte action = 1; action <= numPossibleActions; action++)
                {
                    if (EvolutionSettings.IncludeCommentsWhenUnrolling)
                        Unroll_Commands.InsertComment($"Chance node {chanceNode.Decision.Name} (node {chanceNode.ChanceNodeNumber}) action {action}");

                    int[] probabilityAdjustedInnerResult = Unroll_Commands.NewZeroArray(3);

                    var nextHp = historyPoint;
                    Unroll_GeneralizedVanillaCFR_ChanceNode_NextAction(
                        in nextHp,
                        playerBeingOptimized,
                        piValues,
                        avgStratPiValues,
                        chanceNode,
                        action,
                        probabilityAdjustedInnerResult,
                        algorithmIsLowestDepth: false,
                        suppressCommentsForRepeat: false);

                    for (int k = 0; k < 3; k++)
                        Unroll_AddToResult(resultArray[k], canRouteToOriginal: routeToOrderedBase, probabilityAdjustedInnerResult[k]);
                }
            }
        }


        private void Unroll_GeneralizedVanillaCFR_ChanceNode_NextAction(
            in HistoryPoint historyPoint,
            byte playerBeingOptimized,
            int[] piValues,
            int[] avgStratPiValues,
            ChanceNode chanceNode,
            byte action,
            int[] resultArray,
            bool algorithmIsLowestDepth,
            bool suppressCommentsForRepeat)
        {
            if (EvolutionSettings.IncludeCommentsWhenUnrolling && !suppressCommentsForRepeat)
                Unroll_Commands.InsertComment($"Chance node {chanceNode.Decision.Name} (node {chanceNode.ChanceNodeNumber}) action {action} -> next action");

            Unroll_EnsureSlots();
            // Unroll_Commands.InsertComment("[DEPTH_OPEN] ChanceAction");
            // Unroll_Commands.Recorder.MarkNextDepth("ChanceAction");
            Unroll_Commands.IncrementDepth();

            int actionProbabilityIndex = Unroll_GetChanceNodeIndex_ProbabilityForAction(chanceNode.ChanceNodeNumber, action);
            var actionProb = Unroll_Slots.Read(new OsPort(new OsIndex(actionProbabilityIndex)));

            int[] nextPiValues = new int[NumNonChancePlayers];
            Unroll_GetNextPiValues(piValues,         playerBeingOptimized, actionProb.Index.Value, true, nextPiValues);
            int[] nextAvgStratPiValues = new int[NumNonChancePlayers];
            Unroll_GetNextPiValues(avgStratPiValues, playerBeingOptimized, actionProb.Index.Value, true, nextAvgStratPiValues);

            HistoryPoint nextHistoryPoint = historyPoint.GetBranch(Navigation, action, chanceNode.Decision, chanceNode.DecisionIndex);

            if (EvolutionSettings.TraceCFR)
            {
                int actionProbabilityCopy = Unroll_Commands.CopyToNew(actionProb.Index).Value;
                TabbedText.WriteLine(
                    $"Chance code {chanceNode.DecisionByteCode} ({chanceNode.Decision.Name}) action {action} probability ARRAY{actionProbabilityCopy} .");
                TabbedText.TabIndent();
            }

            // Zero resultArray[0..2] using typed API
            for (int i = 0; i < resultArray.Length; i++)
                Unroll_Commands.ZeroExisting(new VsIndex(resultArray[i]));

            Unroll_GeneralizedVanillaCFR(in nextHistoryPoint,
                                         playerBeingOptimized,
                                         nextPiValues,
                                         nextAvgStratPiValues,
                                         resultArray,
                                         false,
                                         playerBeingOptimized == NumNonChancePlayers - 1);

            // result[k] *= actionProb
            for (int k = 0; k < 3; k++)
                Unroll_Slots.Mul(new VsSlot(new VsIndex(resultArray[k])), actionProb);

            if (EvolutionSettings.TraceCFR)
            {
                int beforeMultipleCurrentCopy = Unroll_Commands.CopyToNew(new VsIndex(resultArray[Unroll_Result_CurrentVsCurrentIndex])).Value;
                int actionProbabilityCopy     = Unroll_Commands.CopyToNew(actionProb.Index).Value;
                int resultCurrentCopy         = Unroll_Commands.CopyToNew(new VsIndex(resultArray[Unroll_Result_CurrentVsCurrentIndex])).Value;
                TabbedText.TabUnindent();
                TabbedText.WriteLine(
                    $". action {action} value ARRAY{beforeMultipleCurrentCopy} probability ARRAY{actionProbabilityCopy} expected value contribution ARRAY{resultCurrentCopy}");
            }
            // Unroll_Commands.InsertComment("[DEPTH_CLOSE] ChanceAction");
            Unroll_Commands.DecrementDepth();
        }


        private void Unroll_GeneralizedVanillaCFR_DecisionNode(
            in HistoryPoint historyPoint,
            byte playerBeingOptimized,
            int[] piValues,
            int[] avgStratPiValues,
            int[] resultArray,
            bool algorithmIsLowestDepth)
        {
            Unroll_EnsureSlots();
            Unroll_EnsureTemplates();

            // At lowest depth, the inputs come from original sources (OS).
            // Copy OS -> VS explicitly using the typed API.
            if (algorithmIsLowestDepth)
            {
                var tmp = new int[piValues.Length];
                for (int i = 0; i < tmp.Length; i++)
                    tmp[i] = Unroll_Commands.CopyToNew(new OsIndex(piValues[i])).Value;
                piValues = tmp;

                tmp = new int[avgStratPiValues.Length];
                for (int i = 0; i < tmp.Length; i++)
                    tmp[i] = Unroll_Commands.CopyToNew(new OsIndex(avgStratPiValues[i])).Value;
                avgStratPiValues = tmp;
            }

            var informationSetOuter = (InformationSetNode)GetGameState(in historyPoint);

            // Repeated-range window: stage resultArray into stable VS slots and copy back on exit.
            if (EvolutionSettings.UnrollTemplateRepeatedRanges && informationSetOuter.Decision.BeginRepeatedRange)
            {
                // Unroll_Commands.Recorder.MarkNextDepth("RepeatWindow");
                using (_repeatTpl.Open(informationSetOuter.Decision.Name))
                {
                    var orig = resultArray;

                    // Stage into a ParameterFrame (keeps existing signature).
                    var frame = new ParameterFrame(Unroll_Commands, orig.Length);
                    frame.SetFromVirtualStack(orig);

                    var staged = new int[frame.Slots.Length];
                    for (int i = 0; i < staged.Length; i++)
                        staged[i] = frame.Slots[i].Val();

                    Unroll_Decision_EmitBody(in historyPoint, playerBeingOptimized,
                                             piValues, avgStratPiValues, staged, algorithmIsLowestDepth);

                    // Copy staged results back to caller-visible VS indices (typed).
                    for (int i = 0; i < orig.Length; i++)
                        Unroll_Commands.CopyToExisting(new VsIndex(orig[i]), new VsIndex(staged[i]));
                }
            }
            else
            {
                Unroll_Decision_EmitBody(in historyPoint, playerBeingOptimized,
                                         piValues, avgStratPiValues, resultArray, algorithmIsLowestDepth);
            }
        }

        private void Unroll_Decision_EmitBody(
            in HistoryPoint historyPoint,
            byte playerBeingOptimized,
            int[] piValues,
            int[] avgStratPiValues,
            int[] resultArray,
            bool algorithmIsLowestDepth)
        {
            Unroll_EnsureSlots();
            Unroll_EnsureTemplates();

            // Preserve original behavior: copy arrays at lowest depth (OS -> VS)
            if (algorithmIsLowestDepth)
            {
                var tmp = new int[piValues.Length];
                for (int i = 0; i < tmp.Length; i++)
                    tmp[i] = Unroll_Commands.CopyToNew(new OsIndex(piValues[i])).Value;
                piValues = tmp;

                tmp = new int[avgStratPiValues.Length];
                for (int i = 0; i < tmp.Length; i++)
                    tmp[i] = Unroll_Commands.CopyToNew(new OsIndex(avgStratPiValues[i])).Value;
                avgStratPiValues = tmp;
            }

            // Unroll_Commands.InsertComment("[DEPTH_OPEN] DecisionEVBody");
            // Unroll_Commands.Recorder.MarkNextDepth("DecisionEVBody");
            Unroll_Commands.IncrementDepth();

            int inversePi = Unroll_Commands.NewZero();
            Unroll_GetInversePiValue(piValues, playerBeingOptimized, inversePi);

            int inversePiAvgStrat = Unroll_Commands.NewZero();
            Unroll_GetInversePiValue(avgStratPiValues, playerBeingOptimized, inversePiAvgStrat);

            IGameState gameStateForCurrentPlayer = GetGameState(in historyPoint);
            var informationSet = (InformationSetNode)gameStateForCurrentPlayer;

            Unroll_CloseRepeatedWindowIfBoundaryHere(informationSet);

            byte decisionNum = informationSet.DecisionIndex;
            byte playerMakingDecision = informationSet.PlayerIndex;
            byte numPossibleActions = NumPossibleActionsAtDecision(decisionNum);
            int[] actionProbabilities;

            using (Unroll_Commands.Recorder.PushBreadcrumb(
                $"IS={informationSet.InformationSetNodeNumber}; dec={decisionNum}; player={playerMakingDecision}"))
            {
                if (EvolutionSettings.IncludeCommentsWhenUnrolling)
                    Unroll_Commands.InsertComment($"Decision  {decisionNum} InfoSet {informationSet.InformationSetNodeNumber} player {playerMakingDecision}");

                if (EvolutionSettings.CFRBR && playerMakingDecision != playerBeingOptimized)
                {
                    actionProbabilities = Unroll_Commands.NewZeroArray(informationSet.NumPossibleActions);
                    for (int i = 0; i < actionProbabilities.Length; i++)
                        Unroll_Commands.Recorder.DebugLabelSlot(actionProbabilities[i], $"actionProb[{i}]");

                    for (byte action = 1; action <= informationSet.NumPossibleActions; action++)
                    {
                        int lastBestResponseActionIndex = Unroll_Commands.CopyToNew(
                            new OsIndex(Unroll_GetInformationSetIndex_LastBestResponse(informationSet.InformationSetNodeNumber, (byte)informationSet.NumPossibleActions))
                        ).Value;

                        Unroll_Commands.InsertEqualsValue(new VsIndex(lastBestResponseActionIndex), (int)action);
                        Unroll_Commands.InsertIf();
                        int one = Unroll_Commands.CopyToNew(new OsIndex(Unroll_OneIndex)).Value;
                        Unroll_Commands.CopyToExisting(new VsIndex(actionProbabilities[action - 1]), new VsIndex(one));
                        Unroll_Commands.InsertEndIf();
                    }
                }
                else
                {
                    var src = playerMakingDecision == playerBeingOptimized
                        ? Unroll_GetInformationSetIndex_CurrentProbabilities_All(informationSet.InformationSetNodeNumber, numPossibleActions)
                        : Unroll_GetInformationSetIndex_CurrentProbabilitiesOpponent_All(informationSet.InformationSetNodeNumber, numPossibleActions);

                    actionProbabilities = new int[src.Length];
                    for (int i = 0; i < actionProbabilities.Length; i++)
                        actionProbabilities[i] = Unroll_Commands.CopyToNew(new OsIndex(src[i])).Value;

                    for (int i = 0; i < actionProbabilities.Length; i++)
                        Unroll_Commands.Recorder.DebugLabelSlot(actionProbabilities[i], $"actionProb[{i}]");
                }

                int[] expectedValueOfAction = Unroll_Commands.NewZeroArray(numPossibleActions);
                int expectedValue = Unroll_Commands.NewZero();

                int[] innerBuf = Unroll_Commands.NewZeroArray(3);
                Unroll_Commands.Recorder.DebugLabelSlot(innerBuf[0], "innerBuf[0]");
                Unroll_Commands.Recorder.DebugLabelSlot(innerBuf[1], "innerBuf[1]");
                Unroll_Commands.Recorder.DebugLabelSlot(innerBuf[2], "innerBuf[2]");

                bool pruningPossible = (EvolutionSettings.PruneOnOpponentStrategy || EvolutionSettings.CFRBR) && playerBeingOptimized != playerMakingDecision;
                int opponentPruningThresholdIndex = -1;
                if (pruningPossible)
                    opponentPruningThresholdIndex = Unroll_Commands.CopyToNew(new OsIndex(Unroll_SmallestValuePossibleIndex)).Value;

                for (byte action = 1; action <= numPossibleActions; action++)
                {
                    using (Unroll_Commands.Recorder.PushBreadcrumb($"action={action} phase=EV"))
                    {
                        if (EvolutionSettings.IncludeCommentsWhenUnrolling)
                            Unroll_Commands.InsertComment($"Decision  {decisionNum} InfoSet {informationSet.InformationSetNodeNumber} player {playerMakingDecision} Action {action}");

                        int probabilityOfAction = actionProbabilities[action - 1];
                        Unroll_Commands.Recorder.DebugLabelSlot(probabilityOfAction, $"prob[a={action}]");

                        if (pruningPossible)
                        {
                            Unroll_Commands.InsertGreaterThanOtherArrayIndex(new VsIndex(probabilityOfAction), new VsIndex(opponentPruningThresholdIndex));
                            Unroll_Commands.InsertIf();
                        }

                        int probabilityOfActionAvgStrat = Unroll_Commands.CopyToNew(
                            new OsIndex(Unroll_GetInformationSetIndex_AverageStrategy(informationSet.InformationSetNodeNumber, action))
                        ).Value;

                        int[] nextPiValues = Unroll_Commands.NewZeroArray(NumNonChancePlayers);
                        Unroll_GetNextPiValues(piValues, playerMakingDecision, probabilityOfAction, false, nextPiValues);

                        int[] nextAvgStratPiValues = Unroll_Commands.NewZeroArray(NumNonChancePlayers);
                        Unroll_GetNextPiValues(avgStratPiValues, playerMakingDecision, probabilityOfActionAvgStrat, false, nextAvgStratPiValues);

                        if (EvolutionSettings.TraceCFR)
                        {
                            int probabilityOfActionCopy = Unroll_Commands.CopyToNew(new VsIndex(probabilityOfAction)).Value;
                            TabbedText.WriteLine(
                                $"({GameDefinition.DecisionsExecutionOrder.FirstOrDefault(x => x.DecisionByteCode == informationSet.DecisionByteCode)?.Name}) code {GameDefinition.DecisionsExecutionOrder.FirstOrDefault(x => x.DecisionByteCode == informationSet.DecisionByteCode)?.DecisionByteCode} optimizing player {playerBeingOptimized}  {(playerMakingDecision == playerBeingOptimized ? "own decision" : "opp decision")} action {action} probability ARRAY{probabilityOfActionCopy} .");
                            TabbedText.TabIndent();
                        }

                        HistoryPoint nextHistoryPoint = historyPoint.GetBranch(Navigation, action, informationSet.Decision, informationSet.DecisionIndex);

                        // ZeroExisting(innerBuf)
                        for (int i = 0; i < innerBuf.Length; i++)
                            Unroll_Commands.ZeroExisting(new VsIndex(innerBuf[i]));

                        Unroll_GeneralizedVanillaCFR(in nextHistoryPoint, playerBeingOptimized, nextPiValues, nextAvgStratPiValues, innerBuf, false, playerBeingOptimized == NumNonChancePlayers - 1);

                        Unroll_Commands.CopyToExisting(new VsIndex(expectedValueOfAction[action - 1]), new VsIndex(innerBuf[Unroll_Result_CurrentVsCurrentIndex]));

                        bool toOriginal =
                            algorithmIsLowestDepth
                            || (_repeatTpl != null && _repeatTpl.IsOpen)
                            || (EvolutionSettings.UnrollTemplateRepeatedRanges && informationSet.Decision.BeginRepeatedRange);

                        if (playerMakingDecision == playerBeingOptimized)
                        {
                            int lastBestResponseActionIndex = Unroll_Commands.CopyToNew(
                                new OsIndex(Unroll_GetInformationSetIndex_LastBestResponse(informationSet.InformationSetNodeNumber, (byte)informationSet.NumPossibleActions))
                            ).Value;
                            Unroll_Commands.InsertEqualsValue(new VsIndex(lastBestResponseActionIndex), (int)action);
                            Unroll_Commands.InsertIf();
                            Unroll_AddToResult(resultArray[Unroll_Result_BestResponseIndex], toOriginal, innerBuf[Unroll_Result_BestResponseIndex]);
                            Unroll_Commands.InsertEndIf();

                            int bestResponseNumerator = Unroll_GetInformationSetIndex_BestResponseNumerator(informationSet.InformationSetNodeNumber, action);
                            int bestResponseDenominator = Unroll_GetInformationSetIndex_BestResponseDenominator(informationSet.InformationSetNodeNumber, action);

                            if (EvolutionSettings.IncludeCommentsWhenUnrolling)
                                Unroll_Commands.InsertComment($"Decision  {decisionNum} InfoSet {informationSet.InformationSetNodeNumber} player {playerMakingDecision} updating regrets");

                            Unroll_AddProductToResult(bestResponseNumerator,  true, inversePiAvgStrat, innerBuf[Unroll_Result_BestResponseIndex]);
                            Unroll_AddToResult       (bestResponseDenominator, true, inversePiAvgStrat);

                            Unroll_AddProductToResult(resultArray[Unroll_Result_CurrentVsCurrentIndex], toOriginal,
                                                      probabilityOfAction, innerBuf[Unroll_Result_CurrentVsCurrentIndex]);

                            Unroll_AddProductToResult(resultArray[Unroll_Result_AverageStrategyIndex],   toOriginal,
                                                      probabilityOfActionAvgStrat, innerBuf[Unroll_Result_AverageStrategyIndex]);
                        }
                        else
                        {
                            Unroll_AddProductToResult(resultArray[Unroll_Result_CurrentVsCurrentIndex], toOriginal,
                                                      probabilityOfAction, innerBuf[Unroll_Result_CurrentVsCurrentIndex]);

                            Unroll_AddProductToResult(resultArray[Unroll_Result_AverageStrategyIndex],   toOriginal,
                                                      probabilityOfActionAvgStrat, innerBuf[Unroll_Result_AverageStrategyIndex]);

                            Unroll_AddProductToResult(resultArray[Unroll_Result_BestResponseIndex],      toOriginal,
                                                      probabilityOfActionAvgStrat, innerBuf[Unroll_Result_BestResponseIndex]);
                        }

                        Unroll_AddProductToResult(expectedValue, canRouteToOriginal: false, probabilityOfAction, expectedValueOfAction[action - 1]);

                        if (EvolutionSettings.TraceCFR)
                        {
                            int expectedValueOfActionCopy = Unroll_Commands.CopyToNew(new VsIndex(expectedValueOfAction[action - 1])).Value;
                            int bestResponseExpectedValueCopy = Unroll_Commands.CopyToNew(new VsIndex(resultArray[Unroll_Result_BestResponseIndex])).Value;
                            int cumExpectedValueCopy = Unroll_Commands.CopyToNew(new VsIndex(expectedValue)).Value;
                            TabbedText.TabUnindent();
                            TabbedText.WriteLine(
                                $"... action {action} expected value ARRAY{expectedValueOfActionCopy} best response expected value ARRAY{bestResponseExpectedValueCopy} cum expected value ARRAY{cumExpectedValueCopy}{(action == numPossibleActions && IncludeAsteriskForBestResponseInTrace ? "*" : "")}");
                        }

                        if (pruningPossible)
                            Unroll_Commands.InsertEndIf();
                    }
                }

                if (playerMakingDecision == playerBeingOptimized)
                {
                    int smallestPossible = Unroll_Commands.CopyToNew(new OsIndex(Unroll_SmallestProbabilityRepresentedIndex)).Value;

                    for (byte action = 1; action <= numPossibleActions; action++)
                    {
                        using (Unroll_Commands.Recorder.PushBreadcrumb($"action={action} phase=regret"))
                        {
                            if (EvolutionSettings.IncludeCommentsWhenUnrolling)
                                Unroll_Commands.InsertComment($"Decision  {decisionNum} InfoSet {informationSet.InformationSetNodeNumber} player {playerMakingDecision} Action {action} incrementing regrets");

                            int pi = Unroll_Commands.CopyToNew(new VsIndex(piValues[playerBeingOptimized])).Value;
                            Unroll_Commands.Recorder.DebugLabelSlot(pi, "pi");

                            Unroll_Commands.CreateCheckpoint(new VsIndex(pi));
                            Unroll_Commands.InsertLessThanOtherArrayIndex(new VsIndex(pi), new VsIndex(smallestPossible));
                            Unroll_Commands.InsertIf();
                            Unroll_Commands.CopyToExisting(new VsIndex(pi), new VsIndex(smallestPossible));
                            Unroll_Commands.InsertEndIf();

                            int regret = Unroll_Commands.CopyToNew(new VsIndex(expectedValueOfAction[action - 1])).Value;
                            Unroll_Commands.Recorder.DebugLabelSlot(regret, "regret");

                            Unroll_Commands.Decrement(new VsIndex(regret), new VsIndex(expectedValue));

                            int lastRegretNumerator   = Unroll_GetInformationSetIndex_LastRegretNumerator(informationSet.InformationSetNodeNumber, action);
                            int lastRegretDenominator = Unroll_GetInformationSetIndex_LastRegretDenominator(informationSet.InformationSetNodeNumber, action);

                            Unroll_AddProductToResult(lastRegretNumerator,   true, regret,   inversePi);
                            Unroll_AddToResult       (lastRegretDenominator, true, inversePi);

                            // Stable alias avoids late reindexing across closed scopes
                            int probabilityOfAction = actionProbabilities[action - 1];
                            Unroll_Commands.Recorder.DebugLabelSlot(probabilityOfAction, $"prob[a={action}]");

                            int contributionToAverageStrategy = Unroll_Commands.CopyToNew(new VsIndex(pi)).Value;
                            Unroll_Commands.Recorder.DebugLabelSlot(contributionToAverageStrategy, "avgStratΔ");

                            Unroll_Commands.MultiplyBy(new VsIndex(contributionToAverageStrategy), new VsIndex(probabilityOfAction));
                            int lastCumulativeStrategyIncrement = Unroll_GetInformationSetIndex_LastCumulativeStrategyIncrement(informationSet.InformationSetNodeNumber, action);

                            Unroll_AddToResult(lastCumulativeStrategyIncrement, true, contributionToAverageStrategy);

                            if (EvolutionSettings.TraceCFR || Unroll_Commands.UseCheckpoints)
                            {
                                int piCopy              = Unroll_Commands.CopyToNew(new VsIndex(pi)).Value;
                                int piValuesZeroCopy    = Unroll_Commands.CopyToNew(new VsIndex(piValues[0])).Value;
                                int piValuesOneCopy     = Unroll_Commands.CopyToNew(new VsIndex(piValues[1])).Value;
                                int regretCopy          = Unroll_Commands.CopyToNew(new VsIndex(regret)).Value;
                                int inversePiCopy       = Unroll_Commands.CopyToNew(new VsIndex(inversePi)).Value;
                                int contributionToAverageStrategyCopy = Unroll_Commands.CopyToNew(new VsIndex(contributionToAverageStrategy)).Value;
                                int cumulativeStrategyCopy = Unroll_Commands.CopyToNew(new OsIndex(lastCumulativeStrategyIncrement)).Value;

                                if (EvolutionSettings.TraceCFR)
                                {
                                    TabbedText.WriteLine($"PiValues ARRAY{piValuesZeroCopy} ARRAY{piValuesOneCopy} pi for optimized ARRAY{piCopy}");
                                    TabbedText.WriteLine(
                                        $"Regrets ({informationSet.Decision.Name} {informationSet.InformationSetNodeNumber}): Action {action} probability ARRAY{probabilityOfAction} regret ARRAY{regretCopy} inversePi ARRAY{inversePiCopy} avg_strat_increment ARRAY{contributionToAverageStrategyCopy} cum_strategy ARRAY{cumulativeStrategyCopy}");
                                }

                                if (Unroll_Commands.UseCheckpoints)
                                {
                                    Unroll_Commands.CreateCheckpoint(new VsIndex(probabilityOfAction));
                                    Unroll_Commands.CreateCheckpoint(new VsIndex(regretCopy));
                                    Unroll_Commands.CreateCheckpoint(new VsIndex(inversePiCopy));
                                    Unroll_Commands.CreateCheckpoint(new VsIndex(piCopy));
                                }
                            }
                        }
                    }
                }
            }
            // Unroll_Commands.InsertComment("[DEPTH_CLOSE] DecisionEVBody");
            Unroll_Commands.DecrementDepth();
        }


        private void Unroll_GetNextPiValues(int[] currentPiValues, byte playerIndex, int probabilityToMultiplyBy, bool changeOtherPlayers, int[] resultArray)
        {
            Unroll_EnsureSlots();

            var prob = VS_(probabilityToMultiplyBy);

            for (byte p = 0; p < NumNonChancePlayers; p++)
            {
                var copy = Unroll_Slots.CopyToNew(VS_(currentPiValues[p]));
                resultArray[p] = copy.Index.Value;

                bool shouldScale =
                    (p == playerIndex && !changeOtherPlayers) ||
                    (p != playerIndex &&  changeOtherPlayers);

                if (shouldScale)
                    Unroll_Slots.Mul(copy, prob);
            }
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void Unroll_GetInversePiValue(int[] piValues, byte playerIndex, int inversePiValueResult)
        {
            Unroll_EnsureSlots();

            var dst = VS_(inversePiValueResult);

            if (NumNonChancePlayers == 2)
            {
                var src = VS_(piValues[(byte)1 - playerIndex]);
                Unroll_Slots.CopyTo(dst, src);
                if (Unroll_Commands.UseCheckpoints)
                    Unroll_Commands.CreateCheckpoint(new VsIndex(inversePiValueResult));
                return;
            }

            bool first = true;
            for (byte p = 0; p < NumNonChancePlayers; p++)
            {
                if (p == playerIndex)
                    continue;

                if (first)
                {
                    Unroll_Slots.CopyTo(dst, VS_(piValues[p]));
                    first = false;
                }
                else
                {
                    Unroll_Slots.Mul(dst, VS_(piValues[p]));
                }
            }

            if (Unroll_Commands.UseCheckpoints)
                Unroll_Commands.CreateCheckpoint(new VsIndex(inversePiValueResult));
        }



        // Templates (lazily created per unroll run)
        private RegionTemplateOptions _tplOpts;
        private IdenticalRangeTemplate _identicalTpl;
        private RepeatWindowTemplate _repeatTpl;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void Unroll_EnsureTemplates()
        {
            if (_tplOpts == null)
                _tplOpts = new RegionTemplateOptions
                {
                    IncludeComments = EvolutionSettings.IncludeCommentsWhenUnrolling,
                    ManageDepthScopes = true,
                    ChunkNamePrefix = null
                };

            if (_identicalTpl == null || _repeatTpl == null)
            {
                _identicalTpl = new IdenticalRangeTemplate(Unroll_Commands, _tplOpts);
                _repeatTpl    = new RepeatWindowTemplate(Unroll_Commands, _tplOpts);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void Unroll_EnsureSlots()
        {
            if (Unroll_Slots == null)
                Unroll_Slots = new ArraySlots(Unroll_Commands);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static VsSlot VS_(int ix) => new VsSlot(new VsIndex(ix));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static OsPort OS_(int originalIndex) => new OsPort(new OsIndex(originalIndex));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static OdPort OD_(int originalIndex) => new OdPort(new OdIndex(originalIndex));   
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int[] CopyOsArrayToNewVs(ArrayCommandList acl, int[] osIndices)
        {
            if (osIndices == null || osIndices.Length == 0) return Array.Empty<int>();
            var res = new int[osIndices.Length];
            for (int i = 0; i < osIndices.Length; i++)
                res[i] = acl.CopyToNew(new OsIndex(osIndices[i])).Value; // typed path
            return res;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void CopyVsArrayToExisting(ArrayCommandList acl, int[] dstVs, int[] srcVs)
        {
            if (dstVs.Length != srcVs.Length) throw new ArgumentException("dst/src length mismatch.");
            for (int i = 0; i < dstVs.Length; i++)
                acl.CopyToExisting(new VsIndex(dstVs[i]), new VsIndex(srcVs[i])); // typed path
        }

        private bool IsOriginalIndex(int index)
        {
            return index >= 0 && index < Unroll_Commands.SizeOfMainData;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void Unroll_CloseRepeatedWindowIfBoundaryHere(IGameState gameState)
        {
            if (!EvolutionSettings.UnrollTemplateRepeatedRanges || _repeatTpl == null || !_repeatTpl.IsOpen)
                return;

            bool endHere = gameState switch
            {
                InformationSetNode iset => iset.Decision.EndRepeatedRange,
                ChanceNode       cnode => cnode.Decision.EndRepeatedRange,
                _ => false
            };

            if (endHere)
                _repeatTpl.CloseAtBoundary();
        }


        /// <summary>Add a VS value into either a VS cell or an ordered destination.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void Unroll_AddToResult(int resultIndex, bool canRouteToOriginal, int valueVsIndex)
        {
            Unroll_EnsureSlots();

            if (canRouteToOriginal && IsOriginalIndex(resultIndex))
            {
                Unroll_Slots.Accumulate(OD_(resultIndex), VS_(valueVsIndex));
            }
            else
            {
                Unroll_Slots.Add(VS_(resultIndex), VS_(valueVsIndex));
            }
        }


        /// <summary>Add (lhs * rhs) into either a VS cell or an ordered destination.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void Unroll_AddProductToResult(int resultIndex, bool canRouteToOriginal, int lhsVsIndex, int rhsVsIndex)
        {
            Unroll_EnsureSlots();

            var productValue = Unroll_Slots.CopyToNew(VS_(lhsVsIndex));
            Unroll_Slots.Mul(productValue, VS_(rhsVsIndex));

            if (canRouteToOriginal && IsOriginalIndex(resultIndex))
            {
                Unroll_Slots.Accumulate(OD_(resultIndex), productValue);
            }
            else
            {
                Unroll_Slots.Add(VS_(resultIndex), productValue);
            }
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
            if (EvolutionSettings.UnrollAlgorithm && (!EvolutionSettings.UseExistingEquilibriaIfAvailable || !EquilibriaFileAlreadyExists()))
            {
                Unroll_CreateUnrolledCommandList();
            }
        }

        public override async Task<ReportCollection> RunAlgorithm(string optionSetName)
        {
            ReportCollection reportCollection = new ReportCollection();
            if (EvolutionSettings.UseExistingEquilibriaIfAvailable && EquilibriaFileAlreadyExists())
            {
                TabbedText.WriteLine($"Using preloaded equilibria file {optionSetName}");
                SaveWeightedGameProgressesAfterEachReport = true;
                double[] equilibrium = LoadEquilibriaFile().First();
                await ProcessEquilibrium(reportCollection, false, false, true, false, 1, InformationSets, 0, true, true, equilibrium);
                // NOTE: If we switch to recording multiple equilibria, we'll change the above.
            }
            else
            {
                if (EvolutionSettings.UnrollAlgorithm)
                    reportCollection = await Unroll_SolveGeneralizedVanillaCFR();
                else
                    reportCollection = await SolveGeneralizedVanillaCFR();

                double[] equilibrium = GetInformationSetValues();
                string equFile = CreateEquilibriaFile(new List<double[]> { equilibrium });
            }
            if (EvolutionSettings.CreateEFGFile)
                CreateGambitEFGFile();

            return reportCollection;
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
                if (!EvolutionSettings.TraceCFR && elapsedSeconds != lastElapsedSeconds)
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
                if (EvolutionSettings.TraceCFR)
                    TabbedText.WriteLine($"Optimizing for player {playerBeingOptimized}");
                GeneralizedVanillaCFRIteration_OptimizePlayer(iteration, results, playerBeingOptimized);
            }
            UpdateInformationSets(iteration);
            await PostIterationWorkForPrincipalComponentsAnalysis(iteration, reportCollection);
            SimulatedAnnealing(iteration);
            MiniReport(iteration, results);

            var result = await ConsiderGeneratingReports(iteration,
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
            if (EvolutionSettings.TraceCFR)
                TabbedText.WriteLine($"{GameDefinition.OptionSetName} Iteration {iteration} Player {playerBeingOptimized}");
            StrategiesDeveloperStopwatch.Start();
            HistoryPoint historyPoint = GetStartOfGameHistoryPoint();
            results[playerBeingOptimized] = GeneralizedVanillaCFR(in historyPoint, playerBeingOptimized, initialPiValues, initialAvgStratPiValues);
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
        public GeneralizedVanillaUtilities GeneralizedVanillaCFR(in HistoryPoint historyPoint, byte playerBeingOptimized, Span<double> piValues, Span<double> avgStratPiValues)
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
                return GeneralizedVanillaCFR_ChanceNode(in historyPoint, playerBeingOptimized, piValues, avgStratPiValues);
            }
            else
                return GeneralizedVanillaCFR_DecisionNode(in historyPoint, playerBeingOptimized, piValues, avgStratPiValues);
        }

        bool IncludeAsteriskForBestResponseInTrace = false;

        private GeneralizedVanillaUtilities GeneralizedVanillaCFR_DecisionNode(
            in HistoryPoint historyPoint,
            byte playerBeingOptimized,
            Span<double> piValues,
            Span<double> avgStratPiValues)
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
                double probabilityOfAction = actionProbabilities[action - 1];
                bool prune = playerBeingOptimized != playerMakingDecision && probabilityOfAction == 0;
                if (!prune)
                {
                    double probabilityOfActionAvgStrat = informationSet.GetAverageStrategy(action);
                    GetNextPiValues(piValues, playerMakingDecision, probabilityOfAction, false, nextPiValues); // reduce probability associated with player being optimized, without changing probabilities for other players
                    GetNextPiValues(avgStratPiValues, playerMakingDecision, probabilityOfActionAvgStrat, false, nextAvgStratPiValues);
                    if (EvolutionSettings.TraceCFR)
                    {
                        TabbedText.WriteLine(
                            $"({informationSet.Decision.Name}) code {informationSet.DecisionByteCode} optimizing player {playerBeingOptimized}  {(playerMakingDecision == playerBeingOptimized ? "own decision" : "opp decision")} action {action} probability {probabilityOfAction} .");
                        TabbedText.TabIndent();
                    }
                    HistoryPoint nextHistoryPoint;
                    if (Navigation.LookupApproach != InformationSetLookupApproach.PlayGameDirectly && informationSet.Decision.IsReversible)
                        nextHistoryPoint = historyPoint.SwitchToBranch(Navigation, action, informationSet.Decision, informationSet.DecisionIndex);
                    else
                        nextHistoryPoint = historyPoint.GetBranch(Navigation, action, informationSet.Decision, informationSet.DecisionIndex);
                    GeneralizedVanillaUtilities innerResult = GeneralizedVanillaCFR(in nextHistoryPoint, playerBeingOptimized, nextPiValues, nextAvgStratPiValues);
                    expectedValueOfAction[action - 1] = innerResult.CurrentVsCurrent;

                    if (playerMakingDecision == playerBeingOptimized)
                    {
                        if (informationSet.BestResponseAction == action)
                            result.BestResponseToAverageStrategy = innerResult.BestResponseToAverageStrategy;
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

                    if (EvolutionSettings.TraceCFR)
                    {
                        TabbedText.TabUnindent();
                        TabbedText.WriteLine(
                            $"... action {action}{(informationSet.BestResponseAction == action && IncludeAsteriskForBestResponseInTrace ? "*" : string.Empty)} expected value {expectedValueOfAction[action - 1]} best response expected value {result.BestResponseToAverageStrategy} cum expected value {expectedValue}{(action == numPossibleActions && IncludeAsteriskForBestResponseInTrace ? "*" : string.Empty)}");
                    }

                    if (Navigation.LookupApproach != InformationSetLookupApproach.PlayGameDirectly && informationSet.Decision.IsReversible)
                    {
                        GameDefinition.ReverseSwitchToBranchEffects(informationSet.Decision, in nextHistoryPoint);
                    }
                }
            }
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
                    if (EvolutionSettings.TraceCFR)
                    {
                        TabbedText.WriteLine($"PiValues {piValues[0]} {piValues[1]} pi for optimized {pi}");
                        //TabbedText.WriteLine($"Regrets: Action {action} regret {regret} prob-adjust {inversePi * regret} new regret {informationSet.GetCumulativeRegret(action)} strategy inc {pi * actionProbabilities[action - 1]} new cum strategy {informationSet.GetCumulativeStrategy(action)}");
                        TabbedText.WriteLine(
                            $"Regrets ({informationSet.Decision.Name} {informationSet.InformationSetNodeNumber}): Action {action} probability {actionProbabilities[action - 1]} regret {regret} inversePi {inversePi} avg_strat_increment {contributionToAverageStrategy} cum_strategy {informationSet.GetLastCumulativeStrategyIncrement(action)}");
                    }

                    //  Checkpoints
                    if (EvolutionSettings.UseCheckpointsWhenNotUnrolling)
                    {
                        RecordCheckpoint($"prob_action_{decisionNum}_{action}", actionProbabilities[action - 1]);
                        RecordCheckpoint($"regret_{decisionNum}_{action}", regret);
                        RecordCheckpoint($"inversePi_{decisionNum}", inversePi);
                        RecordCheckpoint($"pi_{decisionNum}", pi);
                    }
                    // ──────────────────────────────────────────────────────────
                }
            }
            return result;
        }

        private GeneralizedVanillaUtilities GeneralizedVanillaCFR_ChanceNode(in HistoryPoint historyPoint, byte playerBeingOptimized,
            Span<double> piValues, Span<double> avgStratPiValues)
        {
            GeneralizedVanillaUtilities result = default;
            IGameState gameStateForCurrentPlayer = GetGameState(in historyPoint);
            ChanceNode chanceNode = (ChanceNode)gameStateForCurrentPlayer;
            byte numPossibleActions = NumPossibleActionsAtDecision(chanceNode.DecisionIndex);
            byte numPossibleActionsToExplore = numPossibleActions;

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
                                avgStratPiValues2, chanceNode, action);
                        result.IncrementBasedOnProbabilityAdjusted(ref probabilityAdjustedInnerResult);

                    });
            }
            else
            {
                for (byte action = 1; action < (byte)numPossibleActionsToExplore + 1; action++)
                {
                    GeneralizedVanillaUtilities probabilityAdjustedInnerResult = GeneralizedVanillaCFR_ChanceNode_NextAction(in historyPoint, playerBeingOptimized, piValues,
                            avgStratPiValues, chanceNode, action);
                    result.IncrementBasedOnProbabilityAdjusted(ref probabilityAdjustedInnerResult);
                }
            }

            return result;
        }

        private GeneralizedVanillaUtilities GeneralizedVanillaCFR_ChanceNode_NextAction(in HistoryPoint historyPoint, byte playerBeingOptimized, Span<double> piValues, Span<double> avgStratPiValues, ChanceNode chanceNode, byte action)
        {
            Span<double> nextPiValues = stackalloc double[MaxNumMainPlayers];
            Span<double> nextAvgStratPiValues = stackalloc double[MaxNumMainPlayers];
            double actionProbability = chanceNode.GetActionProbability(action);
            GetNextPiValues(piValues, playerBeingOptimized, actionProbability, true,
                nextPiValues);
            GetNextPiValues(avgStratPiValues, playerBeingOptimized, actionProbability, true,
                nextAvgStratPiValues);
            HistoryPoint nextHistoryPoint;
            if (Navigation.LookupApproach != InformationSetLookupApproach.PlayGameDirectly && chanceNode.Decision.IsReversible)
                nextHistoryPoint = historyPoint.SwitchToBranch(Navigation, action, chanceNode.Decision, chanceNode.DecisionIndex);
            else
                nextHistoryPoint = historyPoint.GetBranch(Navigation, action, chanceNode.Decision, chanceNode.DecisionIndex);
            if (EvolutionSettings.TraceCFR)
            {
                TabbedText.WriteLine(
                    $"Chance code {chanceNode.DecisionByteCode} ({chanceNode.Decision.Name}) action {action} probability {actionProbability} ...");
                TabbedText.TabIndent();
            }
            GeneralizedVanillaUtilities result =
                GeneralizedVanillaCFR(in nextHistoryPoint, playerBeingOptimized, nextPiValues, nextAvgStratPiValues);
            if (EvolutionSettings.TraceCFR)
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