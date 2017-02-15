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


            

        ThreadLocal<int> lastDecisionNumber = new ThreadLocal<int>();
        ThreadLocal<long> lastNumIterations = new ThreadLocal<long>();
        // If we bring back the code to dispose of thread local, we must add true as a parameter to each of these constructors
        ThreadLocal<List<GameInputs>> lastGameInputsSet = new ThreadLocal<List<GameInputs>>();
        ThreadLocal<List<IterationID>> lastIterationsToGet = new ThreadLocal<List<IterationID>>();
        ThreadLocal<List<GameProgress>> lastPreplayedGames = new ThreadLocal<List<GameProgress>>();
        ThreadLocal<List<double>> lastDecisionInputs = new ThreadLocal<List<double>>();
        ThreadLocal<List<double>> lastOversamplingWeights = new ThreadLocal<List<double>>();

        bool _disposed;

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
