using ACESim;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESimBase.Games.EFGFileGame
{

    public class EFGFileGameLauncher : Launcher
    {
        public bool OneBetSizeOnly = false;

        public override GameDefinition GetGameDefinition() => new EFGFileGameDefinition();

        public override GameOptions GetSingleGameOptions()
        {
            return new EFGFileGameOptions() {  };
        }

        public override List<GameOptions> GetOptionsSets()
        {
            List<GameOptions> optionSets = new List<GameOptions>() { GetSingleGameOptions().WithName("Report") };

            return optionSets;
        }

        // The following is used by the test classes
        public EFGFileGameProgress PlayEFGFileGameOnce(GameOptions options,
            Func<Decision, GameProgress, byte> actionsOverride)
        {
            EFGFileGameDefinition gameDefinition = new EFGFileGameDefinition();
            gameDefinition.Setup(options);
            List<Strategy> starterStrategies = Strategy.GetStarterStrategies(gameDefinition);

            if (GameProgressLogger.LoggingOn)
                gameDefinition.PrintOutOrderingInformation();

            GamePlayer gamePlayer = new GamePlayer(starterStrategies, false, gameDefinition, true);
            EFGFileGameProgress gameProgress = (EFGFileGameProgress)gamePlayer.PlayUsingActionOverride(actionsOverride);

            return gameProgress;
        }
    }
}
