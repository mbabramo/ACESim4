﻿using System;
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

        public ConcurrentBag<GameProgress> CompletedGameProgresses;
        public GameProgress MostRecentlyCompletedGameProgress;

        public GameDefinition GameDefinition;
        public GameProgress StartingProgress;

        public List<double> AverageInputs;
        public bool DoParallel;

        // parameterless constructor for serialization
        public GamePlayer()
        {
        }

        public GamePlayer(List<Strategy> strategies, bool doParallel, GameDefinition gameDefinition)
        {
            Strategies = strategies;
            DoParallel = doParallel;
            GameDefinition = gameDefinition;
            StartingProgress = GameDefinition.GameFactory.CreateNewGameProgress(new IterationID(1));
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

        public int PlayAllPaths(Action<GameProgress> actionToTake)
        {
            Stopwatch s = new Stopwatch();
            s.Start();
            Action<Action<GameProgress>> playPathsFn = DoParallel ? (Action<Action<GameProgress>>) PlayAllPaths_Parallel : PlayAllPaths_Serial;
            int numPathsPlayed = 0;
            playPathsFn(gp => 
            {
                actionToTake(gp);
                Interlocked.Increment(ref numPathsPlayed);
            }) ;
            s.Stop();
            Debug.WriteLine("PlayAllPathsTime " + s.ElapsedMilliseconds);
            return numPathsPlayed;
        }

        public void PlayAllPaths_Serial(Action<GameProgress> processor)
        {
            // This method plays all game paths (without having any advance knowledge of what those game paths are). 
            IEnumerable<byte> path = new List<byte> { };
            while (path != null)
            {
                (GameProgress gameProgress, IEnumerable<byte> next) = PlayPath(path, true);
                processor(gameProgress);
                if (next == null)
                    path = null;
                else
                    path = next;
            }
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
        
        public void PlayAllPaths_Parallel(Action<GameProgress> processor)
        {

            // This method plays all game paths (without having any advance knowledge of what those game paths are). 
            // It seeks to play them in parallel. This is a little tricky, because we only find out the next path when
            // we actually go through and play the game. But GetNextDecisionPath tells us not just the path, but
            // what the last changed decision index is, and thus we can identify a number of additional paths.
            // For example, we start with () and the game plays (1, 1, 1). Assume three possible actions per decision.
            // If GetNextDecisionPath says that the next path is (1, 1, 2) or (1, 1, 2, 1), we can post these,
            // but we can also post (2) and (3). When we post the path, we also associate with it the last
            // index exhausted (in this case 0). Thus, if (1, 1, 2) eventually leads to (2), we don't post that,
            // because we know we already have done so.

            // So, when we get the next path, we actually can figure out
            // a number of paths, and so we post these to a buffer block right away. Only when we process the last
            // from a set of paths do we look for more paths to process.

            // The buffer block takes paths as inputs.
            int numPending = 0;
            var bufferBlock = new BufferBlock<PathInfo>(new DataflowBlockOptions() { BoundedCapacity = 10000000 });
            // It passes the paths to a worker block that plays the game and produces a GameProgress.
            var transformBlock = new TransformManyBlock<PathInfo, GameProgress>(
                thePath => EnumerateIfNotRedundant(ConvertPathInfoToGameProgressAndPlanNextPath(thePath)),
                 new ExecutionDataflowBlockOptions
                 {
                     MaxDegreeOfParallelism = Environment.ProcessorCount
                 }
                );
            var actionBlock = new ActionBlock<GameProgress>(processor,
                 new ExecutionDataflowBlockOptions
                 {
                     MaxDegreeOfParallelism = Environment.ProcessorCount
                 });
            IEnumerable<GameProgress> EnumerateIfNotRedundant(GameProgress gp)
            {
                // The algorithm will produce some redundancy. For example,
                // if (1,1,1,1,1) is the first completed game, it will
                // produce the starting paths (), (1), (1,1), ... (1,1,1,1).
                // We really only need the last of these.
                int actionsToPlayCount = gp.ActionsToPlay.Count();
                int actionsPlayedCount = gp.GameHistory.GetActionsAsList().Count();
                bool notRedundant = actionsToPlayCount == actionsPlayedCount || (actionsPlayedCount == actionsToPlayCount + 1 && gp.GameHistory.GetActionsAsList().Last() == 1);
                if (notRedundant)
                {
                    yield return gp;
                }
            }
            GameProgress ConvertPathInfoToGameProgressAndPlanNextPath(PathInfo pathToPlay)
            {
                //Debug.WriteLine($"Playing {String.Join(",", pathToPlay.Path)}");
                (GameProgress gameProgress, IEnumerable<byte> nextAsEnumerable) = PlayPath(pathToPlay.Path, true);
                List<byte> next = nextAsEnumerable?.ToList();
                if (next != null)
                {
                    int differentialIndex = pathToPlay.Path.GetIndexOfDifference(next);
                    bool alreadyPosted = differentialIndex <= pathToPlay.LastPathAlreadyExpanded;
                    if (!alreadyPosted)
                    {
                        int pathToExpand = pathToPlay.LastPathAlreadyExpanded + 1;
                        byte startingValue = next[pathToExpand];
                        byte endingValue = GameDefinition.DecisionsExecutionOrder[pathToExpand].NumPossibleActions;
                        for (byte i = startingValue; i <= endingValue; i++)
                        {
                            Interlocked.Increment(ref numPending);
                            List<byte> next2 = new List<byte>();
                            for (int j = 0; j < pathToExpand; j++)
                                next2.Add(next[j]);
                            next2.Add(i);
                            //Debug.WriteLine($"{String.Join(",", pathToPlay.Path)} => {String.Join(",", next2)}");
                            bufferBlock.Post(new PathInfo(next2, pathToExpand));
                        }
                    }
                }
                Interlocked.Decrement(ref numPending);
                if (numPending == 0)
                    bufferBlock.Complete();
                return gameProgress;
            }
            bufferBlock.LinkTo(transformBlock, new DataflowLinkOptions()
            {
                PropagateCompletion = true
            });
            transformBlock.LinkTo(actionBlock, new DataflowLinkOptions()
            {
                PropagateCompletion = true
            });

            PathInfo startingPath = new PathInfo(new List<byte> { }, -1);
            Interlocked.Increment(ref numPending);
            bufferBlock.Post(startingPath);
            Task.WaitAll(bufferBlock.Completion, transformBlock.Completion, actionBlock.Completion);
            //
            //int numYielded2 = 0;
            //bool done = false;
            //do
            //{
            //    Task<bool> t = transformBlock.OutputAvailableAsync();
            //    t.Wait();
            //    if (t.Result)
            //    {
            //        yield return transformBlock.Receive();
            //        numYielded2++;
            //    }
            //    else
            //        done = true;
            //} while (!done);
            //if (numYielded != numYielded2)
            //    throw new Exception("Internal error. Parallel processing did not yield all results!");
            //Debug.WriteLine($"Yielded: {numYielded} {numYielded2}");
        }

        public unsafe (GameProgress progress, IEnumerable<byte> next) PlayPath(IEnumerable<byte> actionsToPlay, bool getNextPath)
        {
            byte* actionsToPlay_AsPointer = stackalloc byte[GameHistory.MaxNumActions];
            int d = 0;
            foreach (byte b in actionsToPlay)
                actionsToPlay_AsPointer[d++] = b;
            actionsToPlay_AsPointer[d] = 255;
            byte* nextActionsToPlay = stackalloc byte[GameHistory.MaxNumActions];
            if (!getNextPath)
                nextActionsToPlay = null;
            GameProgress progress = PlayPathAndKeepGoing(actionsToPlay_AsPointer, ref nextActionsToPlay);
            if (nextActionsToPlay == null)
                return (progress, null);
            List<byte> nextActionsToPlayList = ListExtensions.GetPointerAsList_255Terminated(nextActionsToPlay);
            return (progress, nextActionsToPlayList.AsEnumerable());
        }

        public GameProgress PlayPathAndStop(List<byte> actionsToPlay)
        {
            Game game = GameDefinition.GameFactory.CreateNewGame();
            GameProgress gameProgress = StartingProgress.DeepCopy();
            game.PlaySetup(Strategies, gameProgress, GameDefinition, false, true);
            game.PlayPathAndStop(actionsToPlay);
            return gameProgress;
        }

        public GameProgress PlayUsingActionOverride(Func<Decision, byte> actionOverride)
        {
            Game game = GameDefinition.GameFactory.CreateNewGame();
            GameProgress gameProgress = StartingProgress.DeepCopy();
            gameProgress.ActionOverrider = actionOverride;
            game.PlaySetup(Strategies, gameProgress, GameDefinition, false, true);
            game.PlayUntilComplete();
            return gameProgress;
        }

        public unsafe GameProgress PlayPathAndKeepGoing(byte* actionsToPlay, ref byte* nextActionsToPlay)
        {
            Game game = GameDefinition.GameFactory.CreateNewGame();
            GameProgress gameProgress = StartingProgress.DeepCopy();
            game.PlaySetup(Strategies, gameProgress, GameDefinition, false, true);
            game.PlayPathAndContinueWithDefaultAction(actionsToPlay, ref nextActionsToPlay);
            return gameProgress;
        }


        private GameProgress PlayGameFromSpecifiedPoint(GameProgress currentState)
        {
            Game game = GameDefinition.GameFactory.CreateNewGame();
            game.PlaySetup(Strategies, currentState, GameDefinition, false, false);
            game.PlayUntilComplete();
            return game.Progress;
        }

        public unsafe void ContinuePathWithAction(byte actionToPlay, GameProgress currentGameState)
        {
            Game game = GameDefinition.GameFactory.CreateNewGame();
            game.PlaySetup(Strategies, currentGameState, GameDefinition, false, false);
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

        // We need to be able to play a subset of a very large number of iterations for a particular number (i.e., not using
        // a strategy).    

        ThreadLocal<int> lastDecisionNumber = new ThreadLocal<int>();
        ThreadLocal<long> lastNumIterations = new ThreadLocal<long>();
        // If we bring back the code to dispose of thread local, we must add true as a parameter to each of these constructors
        ThreadLocal<List<IterationID>> lastIterationsToGet = new ThreadLocal<List<IterationID>>();
        ThreadLocal<List<GameProgress>> lastPreplayedGames = new ThreadLocal<List<GameProgress>>();
        ThreadLocal<List<double>> lastDecisionInputs = new ThreadLocal<List<double>>();
        ThreadLocal<List<double>> lastOversamplingWeights = new ThreadLocal<List<double>>();

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
                    x => (object)PlaySpecificIterationStartToFinish(iterationsToGet[(int)x]),
                    (int)heisenbugTrackingIterNum,
                    (int)heisenbugTrackingIterNum, 
                    (x, y) => !FieldwiseObjectComparison.AreEqual((GameProgress)x, (GameProgress)y, false, false));
            }
        }

        static int MinIterationID = 0;
        static bool AlwaysPlaySameIterations = false; // DEBUG

        public IEnumerable<GameProgress> PlayMultipleIterations(
            List<GameProgress> preplayedGameProgressInfos,
            int numIterations,
            IterationID[] iterationIDArray = null)
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

            Parallelizer.Go(DoParallel, 0, numIterations, i =>
            {
                // Remove comments from the following to log specific items
                GameProgressLogger.LoggingOn = false;
                GameProgressLogger.OutputLogMessages = false;
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
                PlayHelper(i, strategiesToPlayWith, true, iterationIDArray, preplayedGameProgressInfos);

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
        void PlayHelper(int iteration, List<Strategy> strategies, bool saveCompletedGameProgressInfos, IterationID[] iterationIDArray, List<GameProgress> preplayedGameProgressInfos)
        {
            GameProgress gameProgress;
            if (preplayedGameProgressInfos != null)
            {
                gameProgress = preplayedGameProgressInfos[iteration].DeepCopy();
            }
            else
            {
                gameProgress = GameDefinition.GameFactory.CreateNewGameProgress(iterationIDArray?[iteration]);
            }

            Game game = GameDefinition.GameFactory.CreateNewGame();
            game.PlaySetup(strategies, gameProgress, GameDefinition, saveCompletedGameProgressInfos, false);
            game.PlayUntilComplete();

            if (saveCompletedGameProgressInfos)
            {
                CompletedGameProgresses.Add(game.Progress);
                MostRecentlyCompletedGameProgress = game.Progress;
            }
        }
    }
}