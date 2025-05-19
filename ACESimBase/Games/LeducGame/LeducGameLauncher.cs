using ACESim.Util;
using ACESimBase.GameSolvingSupport.Settings;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ACESim
{
    public class LeducGameLauncher : Launcher
    {
        public bool OneBetSizeOnly = false;

        public override GameDefinition GetGameDefinition() => new LeducGameDefinition();

        public override GameOptions GetDefaultSingleGameOptions()
        {
            return new LeducGameOptions() { OneBetSizeOnly = true };
        }

        public override List<GameOptions> GetOptionsSets()
        {
            List<GameOptions> optionSets = new List<GameOptions>() { GetDefaultSingleGameOptions().WithName("Report") };

            
            return optionSets;
        }

        private List<(string optionSetName, GameOptions options)> GetOptionsVariations(string description, Func<GameOptions> initialOptionsFunc)
        {
            var list = new List<(string optionSetName, GameOptions options)>();
            GameOptions options;

            options = initialOptionsFunc();
            list.Add((description + " American", options));

            return list;
        }

        // The following is used by the test classes
        public LeducGameProgress PlayLeducGameOnce(GameOptions options,
            Func<Decision, GameProgress, byte> actionsOverride)
        {
            LeducGameDefinition gameDefinition = new LeducGameDefinition();
            gameDefinition.Setup(options);
            List<Strategy> starterStrategies = Strategy.GetStarterStrategies(gameDefinition);

            if (GameProgressLogger.LoggingOn)
                gameDefinition.PrintOutOrderingInformation();

            GamePlayer gamePlayer = new GamePlayer(starterStrategies, false, gameDefinition, true);
            LeducGameProgress gameProgress = (LeducGameProgress)gamePlayer.PlayUsingActionOverride(actionsOverride);

            return gameProgress;
        }
    }
}
