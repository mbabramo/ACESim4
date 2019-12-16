using ACESim;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ACESimBase.Games.AdditiveEvidenceGame
{
    public class AdditiveEvidenceGameLauncher : Launcher
    {

        // We can use this to allow for multiple options sets. These can then run in parallel. But note that we can also have multiple runs with a single option set using different settings by using GameDefinition scenarios; this is useful when there is a long initialization and it makes sense to complete one set before starting the next set.


        public override GameDefinition GetGameDefinition() => new AdditiveEvidenceGameDefinition();

        public override GameOptions GetSingleGameOptions() => AdditiveEvidenceGameOptionsGenerator.GetAdditiveEvidenceGameOptions();

        private enum OptionSetChoice
        {
            Basic
        }

        public override List<(string optionSetName, GameOptions options)> GetOptionsSets()
        {
            List<(string optionSetName, GameOptions options)> optionSets = new List<(string optionSetName, GameOptions options)>();
            OptionSetChoice optionSetChoice = OptionSetChoice.Basic;
            switch (optionSetChoice)
            {
                case OptionSetChoice.Basic:
                    AddBasic(optionSets);
                    break;
            }

            optionSets = optionSets.OrderBy(x => x.optionSetName).ToList();

            bool simplify = false; // Enable for debugging purposes to speed up execution without going all the way to "fast" option
            if (simplify)
                foreach (var optionSet in optionSets)
                    optionSet.options.Simplify();

            return optionSets;
        }


        private void AddBasic(List<(string optionSetName, GameOptions options)> optionSets)
        {

            // now, liability and damages only
            foreach (double quality in new double[] { 0.35, 0.40, 0.45, 0.50, 0.55, 0.60, 0.65 })
            {
                optionSets.Add(GetAndTransform("quality", quality.ToString(), () => AdditiveEvidenceGameOptionsGenerator.Usual(quality), x => { }));
            }
        }

        (string optionSetName, AdditiveEvidenceGameOptions options) GetAndTransform(string baseName, string suffix, Func<AdditiveEvidenceGameOptions> baseOptionsFn, Action<AdditiveEvidenceGameOptions> transform)
        {
            AdditiveEvidenceGameOptions g = baseOptionsFn();
            transform(g);
            string suffix2 = suffix;
            // add further transformation based on additional params here
            return (baseName + suffix2, g);
        }

        // The following is used by the test classes
        public static AdditiveEvidenceGameProgress PlayAdditiveEvidenceGameOnce(AdditiveEvidenceGameOptions options,
            Func<Decision, GameProgress, byte> actionsOverride)
        {
            AdditiveEvidenceGameDefinition gameDefinition = new AdditiveEvidenceGameDefinition();
            gameDefinition.Setup(options);
            List<Strategy> starterStrategies = Strategy.GetStarterStrategies(gameDefinition);

            if (GameProgressLogger.LoggingOn)
                gameDefinition.PrintOutOrderingInformation();

            GamePlayer gamePlayer = new GamePlayer(starterStrategies, false, gameDefinition);
            AdditiveEvidenceGameProgress gameProgress = (AdditiveEvidenceGameProgress)gamePlayer.PlayUsingActionOverride(actionsOverride);

            return gameProgress;
        }
    }
}
