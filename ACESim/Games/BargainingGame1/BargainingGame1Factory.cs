using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.ComponentModel.Composition;

namespace ACESim
{
    [Export(typeof(IGameFactory))]
    [ExportMetadata("GameName", "BargainingGame1")] // put the name of the game class here: ExportMetadata("GameName", "XXX")
    public class BargainingGame1Factory : IGameFactory
    {
        Strategy previousVersionOfStrategy = null;

        public void InitializeStrategyDevelopment(Strategy strategy)
        {
            previousVersionOfStrategy = strategy.DeepCopy();
        }

        public void ConcludeStrategyDevelopment()
        {
            previousVersionOfStrategy = null;
        }

        /// <summary>
        /// Creates a new instance of the Game type
        /// </summary>
        /// <returns></returns>
        public Game CreateNewGame()
        {
            return new BargainingGame1(previousVersionOfStrategy);
        }

        /// <summary>
        /// Returns a new GameProgressInfo. Note that subclasses that keep track of settings over the course 
        /// of the game will have subclassed GameProgressInfo and thus must override this method. 
        /// </summary>
        /// <returns></returns>
        public GameProgressInfo CreateNewGameProgress()
        {
            return new BargainingGame1ProgressInfo();
        }

        /// <summary>
        /// Returns the type of SimulationSettings subclass corresponding to the type of game returned by GetNewGame()
        /// </summary>
        /// <returns></returns>
        public Type GetSimulationSettingsType()
        {
            return typeof(BargainingGame1Inputs);
        }

        /// <summary>
        /// Returns the type of GameDefinition subclass corresponding to the type of game returned by GetNewGame()
        /// </summary>
        /// <returns></returns>
        public Type GetGameDefinitionType()
        {
            return typeof(BargainingGame1Definition);
        }
    }
}
