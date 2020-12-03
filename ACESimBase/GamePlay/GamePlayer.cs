using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using ACESim.Util;
using System.Threading.Tasks.Dataflow;

namespace ACESim
{
    [Serializable]
    public class GamePlayer
    {
        public List<Strategy> Strategies;

        [NonSerialized]
        public ConcurrentBag<GameProgress> CompletedGameProgresses;
        public GameProgress MostRecentlyCompletedGameProgress;

        public GameDefinition GameDefinition;
        public GameProgress StartingProgress;

        public List<double> AverageInputs;

        public bool DoParallelIfNotDisabled;

        // parameterless constructor for serialization
        public GamePlayer()
        {
        }

        public GamePlayer(List<Strategy> strategies, bool doParallel, GameDefinition gameDefinition, bool fullHistoryRequired)
        {
            Strategies = strategies;
            DoParallelIfNotDisabled = doParallel;
            GameDefinition = gameDefinition;
            StartingProgress = GameDefinition.GameFactory.CreateNewGameProgress(fullHistoryRequired, new IterationID(1));
        }


        public void PlaySinglePathAndKeepGoing(string path)
        {
            IEnumerable<byte> path2 = path.Split(',').Select(x => Convert.ToByte(x)).ToList();
            PlaySinglePathAndKeepGoing(path2);
        }

        public void PlaySinglePathAndKeepGoing(IEnumerable<byte> path)
        {
            (GameProgress gameProgress, IEnumerable<byte> next) = PlayPath(path, true);
        }


        private class PathInfo
        {
            public readonly List<byte> Path;
            public readonly int LastPathAlreadyExpanded;

            public PathInfo(List<byte> path, int? lastPathAlreadyExpanded = null)
            {
                Path = path;
                LastPathAlreadyExpanded = lastPathAlreadyExpanded ?? -1;
            }
        }

        public (GameProgress progress, IEnumerable<byte> next) PlayPath(IEnumerable<byte> actionsToPlay, bool getNextPath)
        {
            Span<byte> actionsToPlay_AsPointer = stackalloc byte[GameHistory.MaxNumActions];
            int d = 0;
            foreach (byte b in actionsToPlay)
                actionsToPlay_AsPointer[d++] = b;
            actionsToPlay_AsPointer[d] = 255;
            Span<byte> nextActionsToPlay = stackalloc byte[GameHistory.MaxNumActions];
            if (!getNextPath)
                nextActionsToPlay = null;
            GameProgress progress = PlayPathAndKeepGoing(actionsToPlay_AsPointer, ref nextActionsToPlay);
            if (nextActionsToPlay == null)
                return (progress, null);
            List<byte> nextActionsToPlayList = ListExtensions.GetSpan255TerminatedAsList(nextActionsToPlay);
            return (progress, nextActionsToPlayList.AsEnumerable());
        }

        public (Game game, GameProgress gameProgress) PlayPathAndStop(List<byte> actionsToPlay)
        {
            GameProgress gameProgress = StartingProgress.DeepCopy();
            Game game = GameDefinition.GameFactory.CreateNewGame(Strategies, gameProgress, GameDefinition, false, true, true);
            game.PlayPathAndStop(actionsToPlay);
            if (!gameProgress.GameComplete)
                game.AdvanceToOrCompleteNextStep();
            while (!game.DecisionNeeded && !gameProgress.GameComplete)
                game.AdvanceToOrCompleteNextStep();
            return (game, gameProgress);
        }

        public (Game game, GameProgress gameProgress) GetGameStarted()
        {
            GameProgress gameProgress = StartingProgress.DeepCopy();
            Game game = GameDefinition.GameFactory.CreateNewGame(Strategies, gameProgress, GameDefinition, false, true, true);
            game.AdvanceToOrCompleteNextStep();
            return (game, gameProgress);
        }

        public GameProgress PlayUsingActionOverride(Func<Decision, GameProgress, byte> actionOverride)
        {
            GameProgress gameProgress = StartingProgress.DeepCopy();
            gameProgress.ActionOverrider = actionOverride;
            Game game = GameDefinition.GameFactory.CreateNewGame(Strategies, gameProgress, GameDefinition, false, true, true);
            game.PlayUntilComplete();
            return gameProgress;
        }

        public GameProgress PlayPathAndKeepGoing(Span<byte> actionsToPlay, ref Span<byte> nextActionsToPlay)
        {
            GameProgress gameProgress = StartingProgress.DeepCopy();
            Game game = GameDefinition.GameFactory.CreateNewGame(Strategies, gameProgress, GameDefinition, false, true, true);
            game.PlayPathAndContinueWithDefaultAction(actionsToPlay, ref nextActionsToPlay);
            return gameProgress;
        }


        private GameProgress PlayGameFromSpecifiedPoint(GameProgress currentState)
        {
            Game game = GameDefinition.GameFactory.CreateNewGame(Strategies, currentState, GameDefinition, false, false, true);
            game.PlayUntilComplete();
            return game.Progress;
        }

        public void ContinuePathWithAction(byte actionToPlay, GameProgress currentGameState)
        {
            Game game = GameDefinition.GameFactory.CreateNewGame(Strategies, currentGameState, GameDefinition, false, false, true);
            game.ContinuePathWithAction(actionToPlay);
        }

        public GameProgress PlaySpecificIterationStartToFinish(IterationID iterationID)
        {
            GameProgress initialGameState = StartingProgress.DeepCopy();
            return PlayGameFromSpecifiedPoint(initialGameState);
        }

        public GameProgress DuplicateProgressAndCompleteGame(GameProgress progress)
        {
            var p2 = progress.DeepCopy();
            return PlayGameFromSpecifiedPoint(p2);
        }


        public void CheckConsistencyForSetOfIterations(long totalNumIterations, List<IterationID> iterationsToGet)
        {

            int repetitions = 1; // Should set to 1 (to do all iterations to get once) unless iterationToPlayOverride is non-null, in which case a large number may be desirable
            bool runAllIterationsReached = true; // Should set to iterationsToGet.Count unless you want to try over smaller number
            int numberToRunInParallelEachRepetition = runAllIterationsReached ? iterationsToGet.Count : 2; // if not running all in parallel, we'll run the same iteration twice
            int? iterationToPlayOverride = null;  // In general this should be null, but if one has found a problematic observation, one can speed up the identification of problems by selecting it here.
            bool atLeastOneFound = false;
            int? heisenbugTrackingIterNum = null;
            for (int r = 0; r < repetitions; r++)
            {
                GameProgress[,] gameProgressResults = new GameProgress[numberToRunInParallelEachRepetition, 2];
                bool useParallel = true; // In general, this should be true, in part because parallelism problems could cause some problems. But, one can then true to set this false to see if problems still occur, since it will be easier to trace problems if parallelism is not used.
                for (int p = 0; p <= 1; p++)
                    Parallelizer.Go(useParallel, 0, numberToRunInParallelEachRepetition, iterNum =>
                        {
                            // We'll go in opposite order the second time through with parallelism, to maximize the chance that we get different results with parallelism
                            int iterationToUse = p == 0 || !useParallel || numberToRunInParallelEachRepetition != iterationsToGet.Count ? iterNum : iterationsToGet.Count - iterNum - 1;
                            gameProgressResults[iterationToUse, p] = PlaySpecificIterationStartToFinish(iterationsToGet[iterationToPlayOverride ?? iterationToUse]);
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
                                    TabbedText.WriteLine("CONSISTENCY ERROR (same observation producing different results based on parallel execution order) at iteration " + iterNum);
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
                    x => (object)PlaySpecificIterationStartToFinish(iterationsToGet[(int)x]),
                    (int)heisenbugTrackingIterNum,
                    (int)heisenbugTrackingIterNum,
                    (x, y) => !FieldwiseObjectComparison.AreEqual((GameProgress)x, (GameProgress)y, false, false));
            }
        }



        public static int MinIterationID = 0;
        public static bool AlwaysPlaySameIterations = false;

        public IEnumerable<GameProgress> PlayMultipleIterations(
            List<GameProgress> preplayedGameProgressInfos,
            int numIterations,
            IterationID[] iterationIDArray,
            Func<Decision, GameProgress, byte> actionOverride)
        {
            CompletedGameProgresses = new ConcurrentBag<GameProgress>();

            if (iterationIDArray == null)
            {
                iterationIDArray = new IterationID[numIterations];
                for (long i = 0; i < numIterations; i++)
                {
                    iterationIDArray[i] = new IterationID(i + MinIterationID);
                }
                if (!AlwaysPlaySameIterations)
                    MinIterationID += numIterations;
            }

            // Copy bestStrategies to play with
            List<Strategy> strategiesToPlayWith = Strategies.ToList();

            // Note: we're now doing this serially, instead of producing all games -- note that bottleneck is processing gameprogress, not producing it 
            Parallelizer.Go(DoParallelIfNotDisabled, 0, numIterations, i =>
            {
                // Remove comments from the following to log specific items
                GameProgressLogger.LoggingOn = false;
                GameProgressLogger.OutputLogMessages = false;
                PlayHelper(i, strategiesToPlayWith, true, iterationIDArray, preplayedGameProgressInfos, actionOverride);

            }
           );

            return CompletedGameProgresses;
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
        public GameProgress PlayHelper(int iteration, List<Strategy> strategies, bool saveCompletedGameProgressInfos, IterationID[] iterationIDArray, List<GameProgress> preplayedGameProgressInfos, Func<Decision, GameProgress, byte> actionOverride)
        {
            GameProgress gameProgress;
            if (preplayedGameProgressInfos != null)
            {
                gameProgress = preplayedGameProgressInfos[iteration].DeepCopy();
            }
            else
            {
                gameProgress = GameDefinition.GameFactory.CreateNewGameProgress(false, iterationIDArray?[iteration]);
                gameProgress.ReportingMode = ReportingMode;
                gameProgress.ActionOverrider = actionOverride;
            }

            Game game = GameDefinition.GameFactory.CreateNewGame(strategies, gameProgress, GameDefinition, saveCompletedGameProgressInfos, false, true);
            game.PlayUntilComplete();

            if (saveCompletedGameProgressInfos)
            {
                if (CompletedGameProgresses != null)
                    CompletedGameProgresses.Add(game.Progress);
                MostRecentlyCompletedGameProgress = game.Progress;
            }
            return game.Progress;
        }

        internal bool ReportingMode;
    }
}
