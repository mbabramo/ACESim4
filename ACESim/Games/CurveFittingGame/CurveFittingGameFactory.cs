using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.ComponentModel.Composition;

namespace ACESim
{
    [Serializable]
    [Export(typeof(IGameFactory))]
    [ExportMetadata("GameName", "CurveFittingGame")] // put the name of the game class here: ExportMetadata("GameName", "XXX")
    public class CurveFittingGameFactory : IGameFactory
    {
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
        public Game CreateNewGame()
        {
            return new CurveFittingGame();
        }

        /// <summary>
        /// Returns a new GameProgressInfo. Note that subclasses that keep track of settings over the course 
        /// of the game will have subclassed GameProgressInfo and thus must override this method. 
        /// </summary>
        /// <returns></returns>
        public GameProgress CreateNewGameProgress(IterationID iterationID)
        {
            return new CurveFittingGameProgressInfo()  { IterationID = iterationID };
        }

        /// <summary>
        /// Returns the type of SimulationSettings subclass corresponding to the type of game returned by GetNewGame()
        /// </summary>
        /// <returns></returns>
        public Type GetSimulationSettingsType()
        {
            return typeof(CurveFittingGameInputs);
        }

        /// <summary>
        /// Returns the type of GameDefinition subclass corresponding to the type of game returned by GetNewGame()
        /// </summary>
        /// <returns></returns>
        public Type GetGameDefinitionType()
        {
            return typeof(CurveFittingGameDefinition);
        }

    }
}
