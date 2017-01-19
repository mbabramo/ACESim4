using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.ComponentModel.Composition;

namespace ACESim
{
    [Export(typeof(IGameFactory))]
    [ExportMetadata("GameName", "InformationGame")] // put the name of the game class here: ExportMetadata("GameName", "XXX")
    public class InformationGameFactory  : IGameFactory, IGameName
    {
        /// <summary>
        /// Creates a new instance of the Game type
        /// </summary>
        /// <returns></returns>
        public Game CreateNewGame()
        {
            return new InformationGame();
        }

        /// <summary>
        /// Returns a new GameProgressInfo. Note that subclasses that keep track of settings over the course 
        /// of the game will have subclassed GameProgressInfo and thus must override this method. 
        /// </summary>
        /// <returns></returns>
        public GameProgressInfo CreateNewGameProgress()
        {
            return new InformationGameProgressInfo();
        }

        /// <summary>
        /// Returns the type of SimulationSettings subclass corresponding to the type of game returned by GetNewGame()
        /// </summary>
        /// <returns></returns>
        public Type GetSimulationSettingsType()
        {
            return typeof(InformationGameInputs);
        }

        /// <summary>
        /// Returns the type of GameDefinition subclass corresponding to the type of game returned by GetNewGame()
        /// </summary>
        /// <returns></returns>
        public Type GetGameDefinitionType()
        {
            return typeof(InformationGameDefinition);
        }

        public string GameName
        {
            get
            {
                return "InformationGame";
            }
        }
    }
}
