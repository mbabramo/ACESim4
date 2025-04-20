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

        public override GameOptions GetDefaultSingleGameOptions()
        {
            //return new EFGFileGameOptions() { EFGFileName = "C:\\Users\\Admin\\Documents\\GitHub\\ACESim4\\ACESimBase\\Games\\EFGFileGame\\pennies.efg" };

            return new EFGFileGameOptions() { EFGFileName = "C:\\Users\\Admin\\Documents\\GitHub\\ACESim4\\ACESimBase\\Games\\EFGFileGame\\bayes1a.efg"  };
        }

        public override List<GameOptions> GetOptionsSets()
        {
            List<GameOptions> optionSets = new List<GameOptions>() { GetDefaultSingleGameOptions().WithName("Report") };

            return optionSets;
        }

        // The following is used by the test classes
        public (EFGFileGame game, EFGFileGameProgress progress) PlayEFGGameMoves(GameOptions options, string sourceText,
            List<byte> actions)
        {
            EFGFileGameDefinition gameDefinition = new EFGFileGameDefinition();
            gameDefinition.Setup(options, sourceText);
            List<Strategy> starterStrategies = Strategy.GetStarterStrategies(gameDefinition);

            if (GameProgressLogger.LoggingOn)
                gameDefinition.PrintOutOrderingInformation();

            GamePlayer gamePlayer = new GamePlayer(starterStrategies, false, gameDefinition, true);
            var result = gamePlayer.PlayPathAndMaybeStop(actions, true);

            return ((EFGFileGame) result.game, (EFGFileGameProgress) result.gameProgress);
        }

        public EFGFileGameProgress PlayEFGFileGameOnce(GameOptions options, string sourceText,
            Func<Decision, GameProgress, byte> actionsOverride)
        {
            EFGFileGameDefinition gameDefinition = new EFGFileGameDefinition();
            gameDefinition.Setup(options, sourceText);
            List<Strategy> starterStrategies = Strategy.GetStarterStrategies(gameDefinition);

            if (GameProgressLogger.LoggingOn)
                gameDefinition.PrintOutOrderingInformation();

            GamePlayer gamePlayer = new GamePlayer(starterStrategies, false, gameDefinition, true);
            EFGFileGameProgress gameProgress = (EFGFileGameProgress)gamePlayer.PlayUsingActionOverride(actionsOverride);

            return gameProgress;
        }
    }
}
