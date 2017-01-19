using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;


namespace ACESim
{
    [Serializable]
    public class GamePlayer
    {
        public List<Strategy> bestStrategies;
        public IGameFactory gameFactory;

        public ConcurrentBag<GameProgress> CompletedGameProgresses;
        public GameProgress MostRecentlyCompletedGameProgress;

        public GameDefinition gameDefinition;

        public List<double> averageInputs;
        public EvolutionSettings evolutionsettings;
        public bool DoParallel;

        // parameterless constructor for serialization
        public GamePlayer()
        {
        }

        public GamePlayer(List<Strategy> theCurrentBestForAllPopulations, IGameFactory theGameFactory, bool doParallel, GameDefinition gameDefinition)
        {
            bestStrategies = theCurrentBestForAllPopulations;
            gameFactory = theGameFactory;
            DoParallel = doParallel;
            this.gameDefinition = gameDefinition;
        }
        
        // In evolving, first we need to play up to the decision number for each of a very large set of iterations and retrieve the 
        // inputs to the next decision. We will do this on an iteration by iteration basis, so that we do not need to store all of this
        // very large set in memory simultaneously.

        public List<double> GetDecisionInputsForIteration(SimulationInteraction simulationInteraction, int decisionNumber, IterationID iterationID, long totalNumIterations, int? currentlyEvolvingDecision, OversamplingInfo oversamplingInfo, out bool decisionReached, out GameProgress preplayedGameProgressInfo)
        {
            List<Strategy> strategiesToPlayWith = bestStrategies;
            strategiesToPlayWith[decisionNumber].ResetScores();

            GameInputs gameInputsSet;
            List<double> decisionInputs;
            GetGameAndDecisionInputsAndPreplayedGamesForSpecificIteration(decisionNumber, totalNumIterations, iterationID, simulationInteraction, oversamplingInfo, out gameInputsSet, out preplayedGameProgressInfo, out decisionInputs, currentlyEvolvingDecision);
            decisionReached = decisionInputs != null; // note that if decisionInputs is an empty list, a decision has been reached; this is just a decision without any inputs.
            return decisionInputs;
        }

        public List<List<double>> GetDecisionInputsForSpecificIterations(SimulationInteraction simulationInteraction, int decisionNumber, List<IterationID> iterationIDs, long totalNumIterations, int? currentlyEvolvingDecision)
        {
            List<List<double>> theList = new List<List<double>>();
            foreach (IterationID iteration in iterationIDs)
            {
                bool decisionReached;
                GameProgress preplayedGameProgressInfo;
                List<double> theInputs = GetDecisionInputsForIteration(simulationInteraction, decisionNumber, iteration, totalNumIterations, currentlyEvolvingDecision, null, out decisionReached, out preplayedGameProgressInfo);
                theList.Add(theInputs);
            }
            return theList;
        }

        // For a set of game inputs, we can preplay games up to a particular decision. Note that List<GameInputs> is the
        // parameter, so we are doing this only for some small set of inputs, not an entire game inputs set (for which we currently
        // use arrays). Note that we will not be doing this repetitively, because each iteration from all the iterations to run will ordinarily belong to only one
        // group of game inputs.
        public List<GameProgress> PreplayUpToDecision(int decisionNumber, List<GameInputs> gameInputsGroup, List<IterationID> iterationID, bool recordReportInfo, int? currentlyEvolvingDecision, out List<double> decisionInputsAverage, OversamplingInfo oversamplingInfo)
        {
            GameProgress[] preplayedGameProgressArray = new GameProgress[gameInputsGroup.Count];
            StatCollectorArray collector = new StatCollectorArray();
            List<double> mostRecentRecordedInputs = null;
            Parallelizer.Go(DoParallel, 0, gameInputsGroup.Count, i =>
            // for (int i = 0; i < gameInputsGroup.Count; i++)
            {
                Game game = gameFactory.CreateNewGame();
                game.PlaySetup(bestStrategies, gameFactory.CreateNewGameProgress(iterationID[i]), gameInputsGroup[i], collector, gameDefinition, recordReportInfo, oversamplingInfo.GetWeightForObservation(i));
                game.PlayUpTo(decisionNumber, currentlyEvolvingDecision);
                preplayedGameProgressArray[i] = game.Progress;
                mostRecentRecordedInputs = game.MostRecentRecordedInputs;
            }
            );
            List<GameProgress> preplayedGameProgressInfos = preplayedGameProgressArray.ToList();
            if (iterationID.Count == 1)
                decisionInputsAverage = mostRecentRecordedInputs;
            else
            {
                if (!collector.Initialized)
                    decisionInputsAverage = null;
                else
                    decisionInputsAverage = collector.Average();
            }
            return preplayedGameProgressInfos;
        }


        static int NumSymmetryTests = -1;

        // On play mode, we need to be able to play one strategy for an entire iterations set. (This will be called repeatedly by the play routine.)
        // It's just one strategy that we are testing for each iteration, and we don't need to preplay.
        // We also need this so that  we can compare with the previous set, to determine whether we have improved things.

        public IEnumerable<GameProgress> PlayStrategy(
            Strategy strategy,
            int decisionNumber,
            List<GameProgress> preplayedGameProgressInfos,
            int numIterations,
            SimulationInteraction simulationInteraction,
            GameInputs[] gameInputsArray = null,
            IterationID[] iterationIDArray = null,
            bool returnCompletedGameProgressInfos = false, 
            int? currentlyEvolvingDecision = null)
        {
            if (returnCompletedGameProgressInfos)
                CompletedGameProgresses = new ConcurrentBag<GameProgress>();

            strategy.ResetScores();

            // no oversampling during the play command
            OversamplingInfo oversamplingInfo = new OversamplingInfo() { OversamplingPlan = new OversamplingPlan(), StoreInputSeedsForImprovementOfOversamplingPlan = false, StoreWeightsForAdjustmentOfScoreAverages = false };

            if (gameInputsArray == null)
            {
                gameInputsArray = new GameInputs[numIterations];
                iterationIDArray = new IterationID[numIterations];
                for (long i = 0; i < numIterations; i++)
                {
                    iterationIDArray[i] = new IterationID(i);
                    gameInputsArray[i] = simulationInteraction.GetGameInputs(numIterations, iterationIDArray[i], oversamplingInfo);
                }
            }



            bool runSymmetryTests = false; // if this is true, then we can test whether flipping and swapping inputs leads to the opposite result
            GameProgress evenIterationGameProgress = null;

            // Copy bestStrategies to play with
            List<Strategy> strategiesToPlayWith = bestStrategies.ToList();
            strategiesToPlayWith[decisionNumber] = strategy;

            Parallelizer.Go(DoParallel && !runSymmetryTests, 0, numIterations, i =>
                {
                    NumSymmetryTests++; // if relying on this, be sure that we are ensuring consistent iterations and that randomization does not use date & time
                    // Remove comments from the following to log specific items
                    //if ((NumSymmetryTests == 5 || NumSymmetryTests == 6) && runSymmetryTests) // set this to the even iteration of a pair that failed symmetry to see why
                    //{
                    //    GameProgressLogger.LoggingOn = true;
                    //    GameProgressLogger.OutputLogMessages = true;
                    //}
                    //else
                    //{
                    //    GameProgressLogger.LoggingOn = false;
                    //    GameProgressLogger.OutputLogMessages = false;
                    //}
                    // Remove comments from the following to log a particular iteration repeatedly. We can use this to see how changing settings affects a particular iteration.
                    if ((i == 5) && runSymmetryTests) // set this to the even iteration of a pair that failed symmetry to see why
                    {
                        GameProgressLogger.LoggingOn = true;
                        GameProgressLogger.OutputLogMessages = true;
                    }
                    else
                    {
                        GameProgressLogger.LoggingOn = false;
                        GameProgressLogger.OutputLogMessages = false;
                    }
                    PlayHelper(i, strategiesToPlayWith, returnCompletedGameProgressInfos, gameInputsArray, iterationIDArray,  preplayedGameProgressInfos, currentlyEvolvingDecision,  decisionNumber == currentlyEvolvingDecision ? (int?)decisionNumber : (int?)null, oversamplingInfo);
                    if (runSymmetryTests)
                    {
                        bool evenIteration = i % 2 == 0;
                        if (evenIteration)
                            evenIterationGameProgress = MostRecentlyCompletedGameProgress;
                        else
                        {
                            bool passesSymmetry = MostRecentlyCompletedGameProgress.PassesSymmetryTest(evenIterationGameProgress);
                            if (!passesSymmetry)
                                Debug.WriteLine("Symmetry failed for iterations " + (i - 1) + " and " + i + " (symmetry tests " + (NumSymmetryTests - 1) + " and " + NumSymmetryTests + ")");
                        }
                    }
                    
                }
            );

            return CompletedGameProgresses;
        }

        private GameProgress PlayGameFromSpecifiedPoint(GameInputs inputs, GameProgress startingProgress)
        {
            Game game = gameFactory.CreateNewGame();
            game.PlaySetup(bestStrategies, startingProgress, inputs, null, gameDefinition, false, 1.0);
            game.PlayUntilComplete(null, null);
            return game.Progress;
        }

        public GameProgress PlayGameStartToFinish(IterationID iterationID, GameInputs inputs)
        {
            GameProgress startingProgress = gameFactory.CreateNewGameProgress(iterationID);
            return PlayGameFromSpecifiedPoint(inputs, startingProgress);
        }

        public GameProgress DuplicateProgressAndCompleteGame(GameProgress progress, GameInputs inputs)
        {
            var p2 = progress.DeepCopy();
            return PlayGameFromSpecifiedPoint(inputs, p2);
        }

        // We need to be able to play a subset of a very large number of iterations for a particular number (i.e., not using
        // a strategy).


        public double PlaySpecificValueForSomeIterations(
            SimulationInteraction simulationInteraction,
            double? overrideValue,
            int decisionNumber,
            List<IterationID> specificIterationsToPlay,
            long totalNumIterations,
            OversamplingInfo oversamplingInfo,
            int? currentlyEvolvingDecision,
            out double[] scoresForSubsequentDecisions)
        {
            // Copy bestStrategies to play with
            List<Strategy> strategiesToPlayWith = bestStrategies;
            strategiesToPlayWith[decisionNumber].ResetScores();
            int subsequentDecisionsToRecordScoresFor = strategiesToPlayWith[decisionNumber].Decision.SubsequentDecisionsToRecordScoresFor;
            if (subsequentDecisionsToRecordScoresFor > 0)
                scoresForSubsequentDecisions = new double[subsequentDecisionsToRecordScoresFor];
            else
                scoresForSubsequentDecisions = null;
            bool doParallel = DoParallel && !strategiesToPlayWith[decisionNumber].UseThreadLocalScores; // if we are using thread local scores, then we do not want to further subdivide into more threads, because then those threads' scores will not aggregate properly

            // If this iteration is in our cache, then there are already scores for this decision and possibly subsequent decisions.
            // We add this decision to the StatCollector for this decision, and also add the scores for subsequent decisions to
            // the StatCollectors for those decisions, so the PresmoothingValuesComputedEarlier approach will work on those later
            // decisions.
            if (strategiesToPlayWith[decisionNumber].CacheFromPreviousOptimizationContainsValues)
            {
                ConcurrentBag<IterationID> replacementList = new ConcurrentBag<IterationID>();
                var cache = strategiesToPlayWith[decisionNumber].CacheFromPreviousOptimization;
                Parallelizer.Go(doParallel, 0, specificIterationsToPlay.Count(), iteration =>
                {
                    CachedInputsAndScores cachedValue = null;
                    bool cached = cache.TryGetValue(specificIterationsToPlay[iteration], out cachedValue);
                    if (cached)
                    {
                        strategiesToPlayWith[decisionNumber].Scores.StatCollectors[0].Add(cachedValue.ScoreForFirstDecision, cachedValue.Weight);
                        if (cachedValue.ScoresForSubsequentDecisions != null)
                            for (int subsequent = 1; subsequent <= cachedValue.ScoresForSubsequentDecisions.Length; subsequent++)
                                strategiesToPlayWith[decisionNumber].Scores.StatCollectors[subsequent].Add(cachedValue.ScoresForSubsequentDecisions[subsequent - 1], cachedValue.Weight);
                    }
                    else
                        replacementList.Add(specificIterationsToPlay[iteration]);
                }
                );
                specificIterationsToPlay = replacementList.ToList();
            }

            List<GameInputs> gameInputsSet = null;
            List<GameProgress> preplayedGameProgressInfos = null;
            if (specificIterationsToPlay.Any())
                GetGameAndDecisionInputsAndPreplayedGamesForSpecificIterations(decisionNumber, totalNumIterations, specificIterationsToPlay, simulationInteraction, oversamplingInfo, out gameInputsSet, out preplayedGameProgressInfos, currentlyEvolvingDecision);

            if (GameProgressLogger.LoggingOn)
                GameProgressLogger.Log("ABOUT TO PLAY UNTIL COMPLETE");
            if (gameInputsSet != null)
                Parallelizer.Go(doParallel, 0, gameInputsSet.Count, iteration =>
                {
                    if (GameProgressLogger.LoggingOn)
                        GameProgressLogger.Log("ITERATION " + specificIterationsToPlay[iteration].IterationNumber);
                    strategiesToPlayWith[decisionNumber].ThreadLocalOverrideValue.Value = overrideValue;
                    Game game = null;
                    bool triggerReplayOnException = false;
                    bool inReplayMode = false;
                    retry:
                    try
                    {
                        if (inReplayMode)
                            GetGameAndDecisionInputsAndPreplayedGamesForSpecificIterations(decisionNumber, totalNumIterations, new List<IterationID>() { specificIterationsToPlay[iteration] }, simulationInteraction, oversamplingInfo, out gameInputsSet, out preplayedGameProgressInfos, currentlyEvolvingDecision);
                        GameProgress gameProgress = preplayedGameProgressInfos[inReplayMode ? 0 : iteration].DeepCopy();
                        game = gameFactory.CreateNewGame();
                        game.PlaySetup(strategiesToPlayWith, gameProgress, gameInputsSet[inReplayMode ? 0 : iteration], null, gameDefinition, false, oversamplingInfo.GetWeightForObservation(iteration));
                        game.PlayUntilComplete(null, currentlyEvolvingDecision);
                    }
                    catch
                    {
                        if (game != null && (game.TriggerReplay || triggerReplayOnException))
                        {
                            inReplayMode = true;
                            GameProgressLogger.LoggingOn = true;
                            GameProgressLogger.OutputLogMessages = true;
                            goto retry;
                        }
                        else
                            throw;
                    }
                    if (game.TriggerReplay)
                        goto retry;
                    strategiesToPlayWith[decisionNumber].ThreadLocalOverrideValue.Value = null; // NOTE: It's critical that this be in the same thread
                }
                );


            double avg = 0;
            bool decisionNotScored = false;
            if (strategiesToPlayWith[decisionNumber].Scores.StatCollectors[0].Num() == 0)
                decisionNotScored = true;
            else
                avg = strategiesToPlayWith[decisionNumber].Scores.StatCollectors[0].Average();
            if (subsequentDecisionsToRecordScoresFor > 0)
            {
                for (int i = 0; i < subsequentDecisionsToRecordScoresFor; i++)
                {
                    scoresForSubsequentDecisions[i] = decisionNotScored ? 0 : strategiesToPlayWith[decisionNumber].Scores.StatCollectors[i + 1].Average();
                }
            }
            strategiesToPlayWith[decisionNumber].ResetScores();
            return avg;
        }


        public void ProcessScoresForSpecifiedValuesWithManyIterations(
            SimulationInteraction simulationInteraction,
            List<double> overrideValues,
            Func<GameProgress, bool> processThisOne,
            Action<GameProgress, List<double>, double> scoreProcessor, // parameters are the game progress so far, the list of scores, and the oversampling weight 
            int decisionNumber,
            int numIterationsToPlay,
            int startAtIteration,
            Strategy overallStrategy,
            long totalNumIterations,
            int? currentlyEvolvingDecision = null)
        {
            // Copy bestStrategies to play with
            List<Strategy> strategiesToPlayWith = bestStrategies;
            bool doParallel = DoParallel && !strategiesToPlayWith[decisionNumber].UseThreadLocalScores; // if we are using thread local scores, then we do not want to further subdivide into more threads, because then those threads' scores will not aggregate properly
            bool useThreadLocalScoresOriginal = overallStrategy.UseThreadLocalScores;
            overallStrategy.UseThreadLocalScores = true;

            if (GameProgressLogger.LoggingOn)
                GameProgressLogger.Log("ABOUT TO PLAY UNTIL COMPLETE");
            Parallelizer.Go(doParallel, startAtIteration, startAtIteration + numIterationsToPlay, iteration =>
            {
                ProcessScoresForSpecifiedValues_OneIteration(simulationInteraction, overrideValues, processThisOne, scoreProcessor, decisionNumber, overallStrategy, totalNumIterations, currentlyEvolvingDecision, strategiesToPlayWith, iteration);
            }
            );

            strategiesToPlayWith[decisionNumber].ResetScores();
            overallStrategy.UseThreadLocalScores = useThreadLocalScoresOriginal;
        }

        public void ProcessScoresForSpecifiedValues_OneIteration(SimulationInteraction simulationInteraction, List<double> overrideValues, Func<GameProgress, bool> processThisOne, Action<GameProgress, List<double>, double> scoreProcessor, int decisionNumber, Strategy overallStrategy, long maxIterationNum, int? currentlyEvolvingDecision, List<Strategy> strategiesToPlayWith, int iteration)
        {
            GameInputs gameInputs;
            GameProgress preplayedGameProgressInfo;
            List<double> decisionInputs;
            IterationID iterID = overallStrategy.GenerateIterationID((long)iteration);
            OversamplingInfo oversamplingInfo = new OversamplingInfo() { OversamplingPlan = overallStrategy.OversamplingPlanDuringOptimization, StoreWeightsForAdjustmentOfScoreAverages = true, ReturnedWeightsToApplyToObservation = new List<double>(), StoreInputSeedsForImprovementOfOversamplingPlan = false };
            GetGameAndDecisionInputsAndPreplayedGamesForSpecificIteration(decisionNumber, maxIterationNum, iterID, simulationInteraction, oversamplingInfo, out gameInputs, out preplayedGameProgressInfo, out decisionInputs, currentlyEvolvingDecision);
            if (decisionInputs != null && processThisOne(preplayedGameProgressInfo))
            { // we've reached the decision
                List<double> scores = new List<double>();
                foreach (double overrideValue in overrideValues)
                {
                    if (GameProgressLogger.LoggingOn)
                        GameProgressLogger.Log("ITERATION " + iterID.IterationNumber);
                    strategiesToPlayWith[decisionNumber].ResetScores();
                    strategiesToPlayWith[decisionNumber].ThreadLocalOverrideValue.Value = overrideValue;
                    GameProgress gameProgress = preplayedGameProgressInfo.DeepCopy();
                    Game game = gameFactory.CreateNewGame();
                    game.PlaySetup(strategiesToPlayWith, gameProgress, gameInputs, null, gameDefinition, false, oversamplingInfo.ReturnedWeightsToApplyToObservation[0] /* this should not matter since it is just one iteration -- we use the weight again below */);
                    game.PlayUntilComplete(null, currentlyEvolvingDecision);

                    double avg = strategiesToPlayWith[decisionNumber].Scores.StatCollectors[0].Average();
                    scores.Add(avg);
                    if (game.TriggerReplay)
                    {
                        gameProgress = preplayedGameProgressInfo.DeepCopy();
                        game = gameFactory.CreateNewGame();
                        game.PlaySetup(strategiesToPlayWith, gameProgress, gameInputs, null, gameDefinition, false, oversamplingInfo.GetWeightForObservation(iteration));
                        game.PlayUntilComplete(null, currentlyEvolvingDecision);
                    }
                    strategiesToPlayWith[decisionNumber].ThreadLocalOverrideValue.Value = null; // NOTE: It's critical that this be in the same thread
                }
                scoreProcessor(preplayedGameProgressInfo, scores, oversamplingInfo.ReturnedWeightsToApplyToObservation[0]);
            }
        }

        public double PlaySpecificValueForLargeNumberOfIterations(
            SimulationInteraction simulationInteraction,
            double? overrideValue,
            int decisionNumber,
            int numIterationsToPlay,
            Strategy overallStrategy,
            long totalNumIterations,
            int? currentlyEvolvingDecision = null)
        {
            // Copy bestStrategies to play with
            List<Strategy> strategiesToPlayWith = bestStrategies;
            strategiesToPlayWith[decisionNumber].ResetScores();
            bool doParallel = DoParallel && !strategiesToPlayWith[decisionNumber].UseThreadLocalScores; // if we are using thread local scores, then we do not want to further subdivide into more threads, because then those threads' scores will not aggregate properly

            if (GameProgressLogger.LoggingOn)
                GameProgressLogger.Log("ABOUT TO PLAY UNTIL COMPLETE");
            Parallelizer.Go(doParallel, 0, numIterationsToPlay, iteration =>
            {
                GameInputs gameInputs;
                GameProgress preplayedGameProgressInfo;
                List<double> decisionInputs;
                IterationID iterID = overallStrategy.GenerateIterationID((long)iteration);
                OversamplingInfo oversamplingInfo = new OversamplingInfo() { OversamplingPlan = overallStrategy.OversamplingPlanDuringOptimization, StoreWeightsForAdjustmentOfScoreAverages = true, ReturnedWeightsToApplyToObservation = new List<double>(), StoreInputSeedsForImprovementOfOversamplingPlan = false };
                GetGameAndDecisionInputsAndPreplayedGamesForSpecificIteration(decisionNumber, totalNumIterations, iterID, simulationInteraction, oversamplingInfo, out gameInputs, out preplayedGameProgressInfo, out decisionInputs, currentlyEvolvingDecision);
                if (GameProgressLogger.LoggingOn)
                    GameProgressLogger.Log("ITERATION " + iterID.IterationNumber);
                strategiesToPlayWith[decisionNumber].ThreadLocalOverrideValue.Value = overrideValue;
                GameProgress gameProgress = preplayedGameProgressInfo.DeepCopy(); 
                Game game = gameFactory.CreateNewGame();
                game.PlaySetup(strategiesToPlayWith, gameProgress, gameInputs, null, gameDefinition, false, oversamplingInfo.ReturnedWeightsToApplyToObservation[0]);
                game.PlayUntilComplete(null, currentlyEvolvingDecision);
                if (game.TriggerReplay)
                {
                    gameProgress = preplayedGameProgressInfo.DeepCopy();
                    game = gameFactory.CreateNewGame();
                    game.PlaySetup(strategiesToPlayWith, gameProgress, gameInputs, null, gameDefinition, false, oversamplingInfo.GetWeightForObservation(iteration));
                    game.PlayUntilComplete(null, currentlyEvolvingDecision);
                }
                strategiesToPlayWith[decisionNumber].ThreadLocalOverrideValue.Value = null; // NOTE: It's critical that this be in the same thread
            }
            );



            if (strategiesToPlayWith[decisionNumber].Scores.StatCollectors[0].Num() == 0)
                throw new Exception("Decision was not scored.");

            double avg = strategiesToPlayWith[decisionNumber].Scores.StatCollectors[0].Average();
            strategiesToPlayWith[decisionNumber].ResetScores();
            return avg;
        }

        public double PlaySpecificValuesForSomeIterations(
            SimulationInteraction simulationInteraction,
            List<double> overrideValue,
            int decisionNumber,
            List<IterationID> specificIterationsToPlay,
            long totalNumIterations,
            List<GameInputs> gameInputsSet,
            List<GameProgress> preplayedGameProgressInfos,
            OversamplingInfo oversamplingInfo,
            bool recordInputs = false,
            int? currentlyEvolvingDecision = null)
        {
            // Copy bestStrategies to play with
            List<Strategy> strategiesToPlayWith = bestStrategies.ToList();
            strategiesToPlayWith[decisionNumber].ResetScores();
            bool doParallel = DoParallel && !strategiesToPlayWith[decisionNumber].UseThreadLocalScores; // if we are using thread local scores, then we do not want to further subdivide into more threads, because then those threads' scores will not aggregate properly

            if (gameInputsSet == null || preplayedGameProgressInfos == null)
                GetGameAndDecisionInputsAndPreplayedGamesForSpecificIterations(decisionNumber, totalNumIterations, specificIterationsToPlay, simulationInteraction, oversamplingInfo, out gameInputsSet, out preplayedGameProgressInfos, currentlyEvolvingDecision);

            Parallelizer.Go(doParallel, 0, gameInputsSet.Count, iteration =>
            //for (int iteration = 0; iteration < gameInputsSet.Count; iteration++)
            {
                strategiesToPlayWith[decisionNumber].ThreadLocalOverrideValue.Value = overrideValue[iteration];
                GameProgress gameProgress = preplayedGameProgressInfos[iteration].DeepCopy();
                Game game = gameFactory.CreateNewGame();
                game.PlaySetup(strategiesToPlayWith, gameProgress, gameInputsSet[iteration], null, gameDefinition, false, oversamplingInfo.GetWeightForObservation(iteration));
                game.PlayUntilComplete(recordInputs ? (int?)decisionNumber : (int?)null, currentlyEvolvingDecision);
                strategiesToPlayWith[decisionNumber].ThreadLocalOverrideValue.Value = null; // Note: It's critical that this be in same thread
            }
            );


            double avg = strategiesToPlayWith[decisionNumber].Scores.StatCollectors[0].Average();
            strategiesToPlayWith[decisionNumber].ResetScores();
            return avg;
        }

        public double PlayForSomeIterations(
            SimulationInteraction simulationInteraction,
            int decisionNumber,
            List<IterationID> specificIterationsToPlay,
            long totalNumIterations,
            OversamplingInfo oversamplingInfo,
            int? currentlyEvolvingDecision = null)
        {
            List<GameInputs> gameInputsSet;
            List<GameProgress> preplayedGameProgressInfos;
            GetGameAndDecisionInputsAndPreplayedGamesForSpecificIterations(decisionNumber, totalNumIterations, specificIterationsToPlay, simulationInteraction, oversamplingInfo, out gameInputsSet, out preplayedGameProgressInfos, currentlyEvolvingDecision);

            return PlayPreplayedGamesForSomeIterations(decisionNumber, currentlyEvolvingDecision, gameInputsSet, preplayedGameProgressInfos, oversamplingInfo);
        }

        public double PlayPreplayedGamesForSomeIterations(int decisionNumber, int? currentlyEvolvingDecision, List<GameInputs> gameInputsSet, List<GameProgress> preplayedGameProgressInfos, OversamplingInfo oversamplingInfo)
        {
            bestStrategies[decisionNumber].Scores.Reset();

            for (int iteration = 0; iteration < gameInputsSet.Count; iteration++)
            {
                GameProgress gameProgress = preplayedGameProgressInfos[iteration].DeepCopy();
                Game game = gameFactory.CreateNewGame();
                game.PlaySetup(bestStrategies, gameProgress, gameInputsSet[iteration], null, gameDefinition, false, oversamplingInfo.GetWeightForObservation(iteration));
                game.PlayUntilComplete(null, currentlyEvolvingDecision);
            }

            double avg = bestStrategies[decisionNumber].Scores.StatCollectors[0].Average();
            bestStrategies[decisionNumber].ResetScores();
            return avg;
        }

        ThreadLocal<int> lastDecisionNumber = new ThreadLocal<int>();
        ThreadLocal<long> lastNumIterations = new ThreadLocal<long>();
        // If we bring back the code to dispose of thread local, we must add true as a parameter to each of these constructors
        ThreadLocal<List<GameInputs>> lastGameInputsSet = new ThreadLocal<List<GameInputs>>();
        ThreadLocal<List<IterationID>> lastIterationsToGet = new ThreadLocal<List<IterationID>>();
        ThreadLocal<List<GameProgress>> lastPreplayedGames = new ThreadLocal<List<GameProgress>>();
        ThreadLocal<List<double>> lastDecisionInputs = new ThreadLocal<List<double>>();
        ThreadLocal<List<double>> lastOversamplingWeights = new ThreadLocal<List<double>>();


        bool _disposed;


        public void CheckProgressIntegrityForParticularIteration(int decisionNumber, long totalNumIterations, IterationID iterationToGet, SimulationInteraction simulationInteraction, OversamplingInfo oversamplingInfo)
        {
            GameInputs gameInputs;
            GameProgress preplayedGame1, preplayedGame2;
            List<double> decisionInputs;
            GetGameAndDecisionInputsAndPreplayedGamesForSpecificIteration(decisionNumber, totalNumIterations, iterationToGet, simulationInteraction, oversamplingInfo, out gameInputs, out preplayedGame1, out decisionInputs, null);
            // first, check whether DeepCopy is producing an identical object (note that this doesn't prove that it's duplicating all non-immutable objects)
            var copy = preplayedGame1.DeepCopy();
            FieldwiseObjectComparison.AreEqual(preplayedGame1, copy, true, true);
            // now check whether we get same progress if we redo things up to here
            GetGameAndDecisionInputsAndPreplayedGamesForSpecificIteration(decisionNumber, totalNumIterations, iterationToGet, simulationInteraction, oversamplingInfo, out gameInputs, out preplayedGame2, out decisionInputs, null);
            FieldwiseObjectComparison.AreEqual(preplayedGame1, preplayedGame2, true, true);
            // now, play the games through and see if we continue to have the same result
            GameProgress playedGame1 = PlayGameFromSpecifiedPoint(gameInputs, preplayedGame1);
            preplayedGame2 = preplayedGame1.DeepCopy();
            GameProgress playedGame2 = PlayGameFromSpecifiedPoint(gameInputs, preplayedGame2);
            FieldwiseObjectComparison.AreEqual(playedGame1, playedGame2, true, true);
        }

        public void CheckConsistencyForSetOfIterations(long totalNumIterations, List<IterationID> iterationsToGet, SimulationInteraction simulationInteraction, OversamplingInfo oversamplingInfo)
        {
            List<GameInputs> gameInputsSet = new List<GameInputs>();

            int repetitions = 1; // Should set to 1 (to do all iterations to get once) unless iterationToPlayOverride is non-null, in which case a large number may be desirable
            bool runAllIterationsReached = true; // Should set to iterationsToGet.Count unless you want to try over smaller number
            int numberToRunInParallelEachRepetition = runAllIterationsReached ? iterationsToGet.Count : 2; // if not running all in parallel, we'll run the same iteration twice
            int? iterationToPlayOverride = null;  // In general this should be null, but if one has found a problematic observation, one can speed up the identification of problems by selecting it here.
            bool atLeastOneFound = false;
            int? heisenbugTrackingIterNum = null;
            for (int r = 0; r < repetitions; r++)
            {
                for (int i = 0; i < iterationsToGet.Count; i++)
                    gameInputsSet.Add(simulationInteraction.GetGameInputs(totalNumIterations, iterationsToGet[i], oversamplingInfo));
                GameProgress[,] gameProgressResults = new GameProgress[numberToRunInParallelEachRepetition, 2];
                bool useParallel = true; // In general, this should be true, in part because parallelism problems could cause some problems. But, one can then true to set this false to see if problems still occur, since it will be easier to trace problems if parallelism is not used.
                for (int p = 0; p <= 1; p++)
                    Parallelizer.Go(useParallel, 0, numberToRunInParallelEachRepetition, iterNum =>
                        {
                            // We'll go in opposite order the second time through with parallelism, to maximize the chance that we get different results with parallelism
                            int iterationToUse = p == 0 || !useParallel || numberToRunInParallelEachRepetition != iterationsToGet.Count ? iterNum : iterationsToGet.Count - iterNum - 1;
                            gameProgressResults[iterationToUse, p] = PlayGameStartToFinish(iterationsToGet[iterationToPlayOverride ?? iterationToUse], gameInputsSet[iterationToPlayOverride ?? iterationToUse]);
                        }
                    );
                object lockObj = new object();
                Parallelizer.Go(useParallel, 0, numberToRunInParallelEachRepetition, iterNum =>
                    {
                        if (!atLeastOneFound)
                        { // once we find a problem, we know we have a problem, and can move on to trying to solve it
                            bool result = FieldwiseObjectComparison.AreEqual(gameProgressResults[iterNum, 0], gameProgressResults[iterNum, 1], false, false);
                            if (!result)
                            {
                                lock (lockObj)
                                {
                                    atLeastOneFound = true;
                                    heisenbugTrackingIterNum = iterNum;
                                    Debug.WriteLine("CONSISTENCY ERROR (same observation producing different results based on parallel execution order) at iteration " + iterNum);
                                    FieldwiseObjectComparison.AreEqual(gameProgressResults[iterNum, 0], gameProgressResults[iterNum, 1], false, true);
                                }
                            }
                        }
                    }
                );
                if (atLeastOneFound)
                    break;
            }
            bool doHeisenbugTracking = atLeastOneFound; 
            if (doHeisenbugTracking)
            {
                HeisenbugTracker.KeepTryingRandomSchedulesUntilProblemIsFound_ThenRunScheduleRepeatedly(
                    x => (object)PlayGameStartToFinish(iterationsToGet[(int)x], gameInputsSet[(int)x]),
                    (int)heisenbugTrackingIterNum,
                    (int)heisenbugTrackingIterNum, 
                    (x, y) => !FieldwiseObjectComparison.AreEqual((GameProgress)x, (GameProgress)y, false, false));
            }
        }

        public void GetGameAndDecisionInputsAndPreplayedGamesForSpecificIterations(int decisionNumber, long totalNumIterations, List<IterationID> iterationsToGet, SimulationInteraction simulationInteraction, OversamplingInfo oversamplingInfo, out List<GameInputs> gameInputsSet, out List<GameProgress> preplayedGames, int? currentlyEvolvingDecision)
        {
            List<double> decisionInputs;
            GetGameAndDecisionInputsAndPreplayedGamesForSpecificIterations(decisionNumber, totalNumIterations, iterationsToGet, simulationInteraction, oversamplingInfo, out gameInputsSet, out preplayedGames, out decisionInputs, currentlyEvolvingDecision);
        }

        public void GetGameAndDecisionInputsAndPreplayedGamesForSpecificIteration(int decisionNumber, long totalNumIterations, IterationID iterationToGet, SimulationInteraction simulationInteraction, OversamplingInfo oversamplingInfo, out GameInputs gameInputs, out GameProgress preplayedGame, out List<double> decisionInputs, int? currentlyEvolvingDecision)
        {
            List<GameInputs> gameInputsSet;
            gameInputsSet = new List<GameInputs>();
            gameInputs = simulationInteraction.GetGameInputs(totalNumIterations, iterationToGet, oversamplingInfo);
            gameInputsSet.Add(gameInputs);
            decisionInputs = null;
            List<GameProgress> gameProgressSet;
            gameProgressSet = PreplayUpToDecision(decisionNumber, gameInputsSet, new List<IterationID>() { iterationToGet }, false, currentlyEvolvingDecision, out decisionInputs, oversamplingInfo);
            preplayedGame = gameProgressSet[0];
        }

        public void GetGameAndDecisionInputsAndPreplayedGamesForSpecificIterations(int decisionNumber, long totalNumIterations, List<IterationID> iterationsToGet, SimulationInteraction simulationInteraction, OversamplingInfo oversamplingInfo, out List<GameInputs> gameInputsSet, out List<GameProgress> preplayedGames, out List<double> decisionInputsAverage, int? currentlyEvolvingDecision)
        {
            if (lastGameInputsSet != null && decisionNumber == lastDecisionNumber.Value && totalNumIterations == lastNumIterations.Value && lastIterationsToGet.Value == iterationsToGet)
            { // Our fastest caching method is when we are trying to find the 
                if (GameProgressLogger.LoggingOn)
                    GameProgressLogger.Log("Using previous game inputs and preplayed games");
                gameInputsSet = lastGameInputsSet.Value;
                preplayedGames = lastPreplayedGames.Value;
                decisionInputsAverage = lastDecisionInputs.Value;
                oversamplingInfo.ReturnedWeightsToApplyToObservation = lastOversamplingWeights.Value;
                return;
            }
            

            gameInputsSet = new List<GameInputs>();
            foreach (IterationID iteration in iterationsToGet)
            {
                GameInputs theGameInputs = simulationInteraction.GetGameInputs(totalNumIterations, iteration, oversamplingInfo);
                gameInputsSet.Add(theGameInputs);
            }
            lastGameInputsSet.Value = gameInputsSet;
            lastDecisionNumber.Value = decisionNumber;
            lastNumIterations.Value = totalNumIterations;
            lastIterationsToGet.Value = iterationsToGet;
            preplayedGames = PreplayUpToDecision(decisionNumber, gameInputsSet, iterationsToGet, false, currentlyEvolvingDecision, out decisionInputsAverage, oversamplingInfo);
            lastPreplayedGames.Value = preplayedGames.Select(x => x.DeepCopy()).ToList();
            lastOversamplingWeights.Value = oversamplingInfo.ReturnedWeightsToApplyToObservation;
        }

        /// <summary>
        /// For each of the iterations referenced in playChunkInfo, call PlaySetup and then PlayUntilComplete 
        /// method. Note that in calling PlaySetup, the simulationSettings should be set from the 
        /// simulationSettings array corresponding to the current iteration number; theStrategies should be set 
        /// to strategiesToPlayWith; and the gameProgressInfo should be set either to null or to a clone of the 
        /// gameProgressInfo object from preplayedGames. If saveCompletedGames, then the gameProgressInfo 
        /// resulting for each iteration should be added to completedGames; that way, after evolution is complete, 
        /// the GameProgressInfo objects can be called to generate reports.
        /// </summary>
        void PlayHelper(int iteration, List<Strategy> strategies, bool saveCompletedGameProgressInfos, GameInputs[] gameInputsArray, IterationID[] iterationIDArray, List<GameProgress> preplayedGameProgressInfos, int? currentlyEvolvingDecision, int? recordInputsForDecision, OversamplingInfo oversamplingInfo)
        {
            GameProgress gameProgress;
            if (preplayedGameProgressInfos != null)
            {
                gameProgress = preplayedGameProgressInfos[iteration].DeepCopy();
            }
            else
            {
                gameProgress = gameFactory.CreateNewGameProgress(iterationIDArray == null ? null : iterationIDArray[iteration]);
            }

            Game game = gameFactory.CreateNewGame();
            game.PlaySetup(strategies, gameProgress, gameInputsArray[iteration], null, gameDefinition, saveCompletedGameProgressInfos, oversamplingInfo.GetWeightForObservation(iteration));
            game.PlayUntilComplete(recordInputsForDecision, currentlyEvolvingDecision, false);

            if (saveCompletedGameProgressInfos)
            {
                CompletedGameProgresses.Add(game.Progress);
                MostRecentlyCompletedGameProgress = game.Progress;
            }
        }
    }
}
