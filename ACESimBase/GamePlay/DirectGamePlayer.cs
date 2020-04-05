using ACESim;
using System;
using System.Collections.Generic;
using System.Text;

namespace ACESimBase
{
    public class DirectGamePlayer
    {
        GameDefinition GameDefinition;
        GameProgress GameProgress;
        Game Game;

        public DirectGamePlayer(GameDefinition gameDefinition, GameProgress startingProgress, Game game)
        {
            GameDefinition = gameDefinition;
            GameProgress = startingProgress.DeepCopy();
            if (game == null)
            {
                Game = GameDefinition.GameFactory.CreateNewGame();
                Game.PlaySetup(null, GameProgress, GameDefinition, false, true);
            }
            else
                Game = game;
        }

        public DirectGamePlayer DeepCopy()
        {
            return new DirectGamePlayer(GameDefinition, 
        }

        public void PlayAction(byte actionToPlay) => Game.ContinuePathWithAction(actionToPlay);

        public bool GameComplete => GameProgress.GameComplete;

        public Decision CurrentDecision => Game.CurrentDecision;
    }
}
