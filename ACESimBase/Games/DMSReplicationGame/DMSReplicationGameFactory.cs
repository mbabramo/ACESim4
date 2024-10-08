﻿using ACESim;
using System;
using System.Collections.Generic;
using System.Text;

namespace ACESimBase.Games.DMSReplicationGame
{
    public class DMSReplicationGameFactory : IGameFactory
    {
        /// <summary>
        /// Perform any general initialization. One might create information that can then be passed on to the game class.
        /// </summary>
        public void InitializeStrategyDevelopment(Strategy strategy)
        {
        }


        public void ConcludeStrategyDevelopment()
        {
        }

        /// <summary>
        /// Creates a new instance of the Game type
        /// </summary>
        /// <returns></returns>
        public Game CreateNewGame(List<Strategy> strategies,
            GameProgress progress,
            GameDefinition gameDefinition,
            bool recordReportInfo,
            bool restartFromBeginningOfGame,
            bool fullHistoryRequired)
        {
            return new DMSReplicationGame(strategies, progress, gameDefinition, recordReportInfo, restartFromBeginningOfGame, fullHistoryRequired);
        }

        /// <summary>
        /// Returns a new GameProgressInfo. Note that subclasses that keep track of settings over the course 
        /// of the game will have subclassed GameProgressInfo and thus must override this method. 
        /// </summary>
        /// <returns></returns>
        public GameProgress CreateNewGameProgress(bool fullHistoryRequired, IterationID iterationID)
        {
            GameProgress gameProgress = new DMSReplicationGameProgress(fullHistoryRequired) { IterationID = iterationID };
            return gameProgress;
        }

        /// <summary>
        /// Returns the type of GameDefinition subclass corresponding to the type of game returned by GetNewGame()
        /// </summary>
        /// <returns></returns>
        public Type GetGameDefinitionType()
        {
            return typeof(DMSReplicationGameDefinition);
        }
    }
}
