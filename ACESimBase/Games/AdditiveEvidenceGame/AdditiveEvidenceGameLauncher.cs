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
            All,
            Fast,
            Original,
            TwoSets,
            OtherTwoSets,
            VaryingNoise,
        }

        public override List<(string optionSetName, GameOptions options)> GetOptionsSets()
        {
            List<(string optionSetName, GameOptions options)> optionSets = new List<(string optionSetName, GameOptions options)>();
            OptionSetChoice optionSetChoice = OptionSetChoice.Fast;
            bool withOptionNotToPlay = false;
            switch (optionSetChoice)
            {
                case OptionSetChoice.Original:
                    AddDariMattiacci_Saraceno_Tests(optionSets, DMSVersion.Original, withOptionNotToPlay);
                    break;
                case OptionSetChoice.TwoSets:
                    AddDariMattiacci_Saraceno_Tests(optionSets, DMSVersion.Original, withOptionNotToPlay);
                    AddDariMattiacci_Saraceno_Tests(optionSets, DMSVersion.EvenStrengthAndBiasless, withOptionNotToPlay);
                    break;
                case OptionSetChoice.OtherTwoSets:
                    AddDariMattiacci_Saraceno_Tests(optionSets, DMSVersion.Biasless, withOptionNotToPlay);
                    AddDariMattiacci_Saraceno_Tests(optionSets, DMSVersion.EvenStrength, withOptionNotToPlay);
                    break;
                case OptionSetChoice.All:
                    AddDariMattiacci_Saraceno_Tests(optionSets, DMSVersion.Original, withOptionNotToPlay);
                    AddDariMattiacci_Saraceno_Tests(optionSets, DMSVersion.Biasless, withOptionNotToPlay);
                    AddDariMattiacci_Saraceno_Tests(optionSets, DMSVersion.EvenStrength, withOptionNotToPlay);
                    AddDariMattiacci_Saraceno_Tests(optionSets, DMSVersion.EvenStrengthAndBiasless, withOptionNotToPlay);
                    break;
                case OptionSetChoice.Fast:
                    AddDariMattiacci_Saraceno_Tests(optionSets, DMSVersion.Original, false, false);
                    break;
                case OptionSetChoice.VaryingNoise:
                    AddDariMattiacci_Saraceno_Tests(optionSets, DMSVersion.VaryNoise_00, false, true);
                    AddDariMattiacci_Saraceno_Tests(optionSets, DMSVersion.VaryNoise_25, false, true);
                    AddDariMattiacci_Saraceno_Tests(optionSets, DMSVersion.VaryNoise_50, false, true);
                    AddDariMattiacci_Saraceno_Tests(optionSets, DMSVersion.VaryShared_00, false, true);
                    AddDariMattiacci_Saraceno_Tests(optionSets, DMSVersion.VaryShared_25, false, true);
                    AddDariMattiacci_Saraceno_Tests(optionSets, DMSVersion.VaryShared_50, false, true);
                    AddDariMattiacci_Saraceno_Tests(optionSets, DMSVersion.VaryShared_75, false, true);
                    AddDariMattiacci_Saraceno_Tests(optionSets, DMSVersion.VaryShared_100, false, true);
                    AddDariMattiacci_Saraceno_Tests(optionSets, DMSVersion.VaryPStrength_00, false, true);
                    AddDariMattiacci_Saraceno_Tests(optionSets, DMSVersion.VaryPStrength_25, false, true);
                    AddDariMattiacci_Saraceno_Tests(optionSets, DMSVersion.VaryPStrength_50, false, true);
                    AddDariMattiacci_Saraceno_Tests(optionSets, DMSVersion.VaryPStrength_75, false, true);
                    AddDariMattiacci_Saraceno_Tests(optionSets, DMSVersion.VaryPStrength_100, false, true);
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
            EvenStrengthAndBiasless,
            EvenStrengthAndBiasless_MoreInfoShared,
            VaryNoise_00, // no noise; 50% of judgment depends on shared evaluation of quality; p, d private estimates evenly determine rest of judgment
            VaryNoise_25, // 25% noise; 50% of rest of judgment depends on shared evaluation of quality; p, d private estimates evenly determine rest of judgment
            VaryNoise_50, // 50% noise; 50% of rest of judgment depends on shared evaluation of quality; p, d private estimates evenly determine rest of judgment
            VaryShared_00, // 25% noise; no shared info on case quality; p, d private estimates evenly determine rest of judgment
            VaryShared_25, // 25% noise; 25% of rest of judgment depends on shared evaluation of quality; p, d private estimates evenly determine rest of judgment
            VaryShared_50, // 25% noise; 50% of rest of judgment depends on shared evaluation of quality; p, d private estimates evenly determine rest of judgment
            VaryShared_75, // 25% noise; 75% of rest of judgment depends on shared evaluation of quality; p, d private estimates evenly determine rest of judgment
            VaryShared_100, // 25% noise; 100% of judgment depends on shared evaluation of quality; p, d private estimates are actually irrelevant
            VaryPStrength_00, // 25% noise; 50% of rest of judgment depends on shared evaluation of quality; p private estimate determines 0% of rest of judgment
            VaryPStrength_25, // 25% noise; 50% of rest of judgment depends on shared evaluation of quality; p private estimate determines 25% of rest of judgment
            VaryPStrength_50, // 25% noise; 50% of rest of judgment depends on shared evaluation of quality; p private estimate determines 50% of rest of judgment
            VaryPStrength_75, // 25% noise; 50% of rest of judgment depends on shared evaluation of quality; p private estimate determines 75% of rest of judgment
            VaryPStrength_100, // 25% noise; 50% of rest of judgment depends on shared evaluation of quality; p private estimate determines 100% of rest of judgment
        }

        private void AddDariMattiacci_Saraceno_Tests(List<(string optionSetName, GameOptions options)> optionSets, DMSVersion version, bool withOptionNotToPlay, bool feeShifting = true)
        {
            // now, liability and damages only
                foreach (double? feeShiftingThreshold in feeShifting ? new double?[] { 0, 0.25, 0.50, 0.75, 1.0 } : new double?[] { 0 })
                    foreach (double costs in new double[] { 0, 0.15, 0.30, 0.45, 0.60 })
                    foreach (double quality in new double[] { 0, 0.20, 0.40, 0.60, 0.80, 1.0 })
                    {
                        string settingsString = $"q{(int) (quality*100)}c{(int) (costs*100)}t{(int)(feeShiftingThreshold*100)}";
                        switch (version)
                        {
                            case DMSVersion.Original:
                                optionSets.Add(GetAndTransform("orig", settingsString, () => AdditiveEvidenceGameOptionsGenerator.DariMattiacci_Saraceno(quality, costs, feeShiftingThreshold != null, false, feeShiftingThreshold ?? 0, withOptionNotToPlay), x => { }));
                                break;
                            case DMSVersion.Biasless:
                                optionSets.Add(GetAndTransform("bl", settingsString, () => AdditiveEvidenceGameOptionsGenerator.Biasless(quality, quality, costs, feeShiftingThreshold != null, false, feeShiftingThreshold ?? 0, 0.5, withOptionNotToPlay), x => { }));
                                break;
                            case DMSVersion.EvenStrength:
                                optionSets.Add(GetAndTransform("es", settingsString, () => AdditiveEvidenceGameOptionsGenerator.EvenStrength(quality, costs, feeShiftingThreshold != null, false, feeShiftingThreshold ?? 0, withOptionNotToPlay), x => { }));
                                break;
                            case DMSVersion.EvenStrengthAndBiasless:
                                optionSets.Add(GetAndTransform("bl_es", settingsString, () => AdditiveEvidenceGameOptionsGenerator.Biasless(quality, 0.5, costs, feeShiftingThreshold != null, false, feeShiftingThreshold ?? 0, 0.5, withOptionNotToPlay), x => { }));
                                break;
                            case DMSVersion.EvenStrengthAndBiasless_MoreInfoShared:
                                optionSets.Add(GetAndTransform("mis", settingsString, () => AdditiveEvidenceGameOptionsGenerator.Biasless(quality, 0.5, costs, feeShiftingThreshold != null, false, feeShiftingThreshold ?? 0, 0.75, withOptionNotToPlay), x => { }));
                                break;

                            case DMSVersion.VaryNoise_00:
                                optionSets.Add(GetAndTransform("noise00", settingsString, () => AdditiveEvidenceGameOptionsGenerator.SomeNoise(0, 0.5, 0.5, quality, costs, feeShiftingThreshold != null, false, feeShiftingThreshold ?? 0, withOptionNotToPlay), x => { }));
                                break;
                            case DMSVersion.VaryNoise_25:
                                optionSets.Add(GetAndTransform("noise25", settingsString, () => AdditiveEvidenceGameOptionsGenerator.SomeNoise(0.25, 0.5, 0.5, quality, costs, feeShiftingThreshold != null, false, feeShiftingThreshold ?? 0, withOptionNotToPlay), x => { }));
                                break;
                            case DMSVersion.VaryNoise_50:
                                optionSets.Add(GetAndTransform("noise50", settingsString, () => AdditiveEvidenceGameOptionsGenerator.SomeNoise(0.50, 0.5, 0.5, quality, costs, feeShiftingThreshold != null, false, feeShiftingThreshold ?? 0, withOptionNotToPlay), x => { }));
                                break;
                            case DMSVersion.VaryShared_00:
                                optionSets.Add(GetAndTransform("shared00", settingsString, () => AdditiveEvidenceGameOptionsGenerator.SomeNoise(0.25, 0.0, 0.5, quality, costs, feeShiftingThreshold != null, false, feeShiftingThreshold ?? 0, withOptionNotToPlay), x => { }));
                                break;
                            case DMSVersion.VaryShared_25:
                                optionSets.Add(GetAndTransform("shared25", settingsString, () => AdditiveEvidenceGameOptionsGenerator.SomeNoise(0.25, 0.25, 0.5, quality, costs, feeShiftingThreshold != null, false, feeShiftingThreshold ?? 0, withOptionNotToPlay), x => { }));
                                break;
                            case DMSVersion.VaryShared_50:
                                optionSets.Add(GetAndTransform("shared50", settingsString, () => AdditiveEvidenceGameOptionsGenerator.SomeNoise(0.25, 0.50, 0.5, quality, costs, feeShiftingThreshold != null, false, feeShiftingThreshold ?? 0, withOptionNotToPlay), x => { }));
                                break;
                            case DMSVersion.VaryShared_75:
                                optionSets.Add(GetAndTransform("shared75", settingsString, () => AdditiveEvidenceGameOptionsGenerator.SomeNoise(0.25, 0.75, 0.5, quality, costs, feeShiftingThreshold != null, false, feeShiftingThreshold ?? 0, withOptionNotToPlay), x => { }));
                                break;
                            case DMSVersion.VaryShared_100:
                                optionSets.Add(GetAndTransform("shared100", settingsString, () => AdditiveEvidenceGameOptionsGenerator.SomeNoise(0.25, 1.0, 0.5, quality, costs, feeShiftingThreshold != null, false, feeShiftingThreshold ?? 0, withOptionNotToPlay), x => { }));
                                break;
                            case DMSVersion.VaryPStrength_00:
                                optionSets.Add(GetAndTransform("pstrength00", settingsString, () => AdditiveEvidenceGameOptionsGenerator.SomeNoise(0.25, 0.5, 0, quality, costs, feeShiftingThreshold != null, false, feeShiftingThreshold ?? 0, withOptionNotToPlay), x => { }));
                                break;
                            case DMSVersion.VaryPStrength_25:
                                optionSets.Add(GetAndTransform("pstrength25", settingsString, () => AdditiveEvidenceGameOptionsGenerator.SomeNoise(0.25, 0.5, 0.25, quality, costs, feeShiftingThreshold != null, false, feeShiftingThreshold ?? 0, withOptionNotToPlay), x => { }));
                                break;
                            case DMSVersion.VaryPStrength_50:
                                optionSets.Add(GetAndTransform("pstrength50", settingsString, () => AdditiveEvidenceGameOptionsGenerator.SomeNoise(0.25, 0.5, 0.5, quality, costs, feeShiftingThreshold != null, false, feeShiftingThreshold ?? 0, withOptionNotToPlay), x => { }));
                                break;
                            case DMSVersion.VaryPStrength_75:
                                optionSets.Add(GetAndTransform("pstrength75", settingsString, () => AdditiveEvidenceGameOptionsGenerator.SomeNoise(0.25, 0.5, 0.75, quality, costs, feeShiftingThreshold != null, false, feeShiftingThreshold ?? 0, withOptionNotToPlay), x => { }));
                                break;
                            case DMSVersion.VaryPStrength_100:
                                optionSets.Add(GetAndTransform("pstrength100", settingsString, () => AdditiveEvidenceGameOptionsGenerator.SomeNoise(0.25, 0.5, 1.0, quality, costs, feeShiftingThreshold != null, false, feeShiftingThreshold ?? 0, withOptionNotToPlay), x => { }));
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
