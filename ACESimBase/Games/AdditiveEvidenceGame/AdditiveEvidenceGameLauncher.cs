﻿using ACESim;
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
            All
        }

        public override List<(string optionSetName, GameOptions options)> GetOptionsSets()
        {
            List<(string optionSetName, GameOptions options)> optionSets = new List<(string optionSetName, GameOptions options)>();
            OptionSetChoice optionSetChoice = OptionSetChoice.All;
            bool withQuits = false;
            switch (optionSetChoice)
            {
                case OptionSetChoice.All:
                    AddDariMattiacci_Saraceno_Tests(optionSets, DMSVersion.Original, withQuits);
                    AddDariMattiacci_Saraceno_Tests(optionSets, DMSVersion.Biasless, withQuits);
                    AddDariMattiacci_Saraceno_Tests(optionSets, DMSVersion.EvenStrength, withQuits);
                    AddDariMattiacci_Saraceno_Tests(optionSets, DMSVersion.EvenStrengthAndBiasless, withQuits);
                    break;
            }

            optionSets = optionSets.OrderBy(x => x.optionSetName).ToList();

            bool simplify = false; // Enable for debugging purposes to speed up execution without going all the way to "fast" option
            if (simplify)
                foreach (var optionSet in optionSets)
                    optionSet.options.Simplify();

            return optionSets;
        }

        enum DMSVersion
        {
            Original,
            Biasless,
            EvenStrength,
            EvenStrengthAndBiasless
        }

        private void AddDariMattiacci_Saraceno_Tests(List<(string optionSetName, GameOptions options)> optionSets, DMSVersion version, bool withQuits)
        {
            // now, liability and damages only
            foreach (double quality in new double[] { 0, 0.20, 0.40, 0.60, 0.80, 1.0 })
                foreach (double costs in new double[] { 0, 0.15, 0.30, 0.45, 0.60 })
                    foreach (double? feeShiftingThreshold in new double?[] { 0, 0.25, 0.50, 0.75, 1.0 })
                {
                        string settingsString = $"q{(int) (quality*100)}c{(int) (costs*100)}t{(int)(feeShiftingThreshold*100)}";
                        switch (version)
                        {
                            case DMSVersion.Original:
                                optionSets.Add(GetAndTransform("orig", settingsString, () => AdditiveEvidenceGameOptionsGenerator.DariMattiacci_Saraceno(quality, costs, feeShiftingThreshold != null, false, feeShiftingThreshold ?? 0, withQuits), x => { }));
                                break;
                            case DMSVersion.Biasless:
                                optionSets.Add(GetAndTransform("bl", settingsString, () => AdditiveEvidenceGameOptionsGenerator.Biasless(quality, quality, costs, feeShiftingThreshold != null, false, feeShiftingThreshold ?? 0, withQuits), x => { }));
                                break;
                            case DMSVersion.EvenStrength:
                                optionSets.Add(GetAndTransform("es", settingsString, () => AdditiveEvidenceGameOptionsGenerator.EvenStrength(quality, costs, feeShiftingThreshold != null, false, feeShiftingThreshold ?? 0, withQuits), x => { }));
                                break;
                            case DMSVersion.EvenStrengthAndBiasless:
                                optionSets.Add(GetAndTransform("bl_es", settingsString, () => AdditiveEvidenceGameOptionsGenerator.Biasless(quality, 0.5, costs, feeShiftingThreshold != null, false, feeShiftingThreshold ?? 0, withQuits), x => { }));
                                break;
                        }
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
