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
        public override string MasterReportNameForDistributedProcessing => "AE002"; // Note: Overridden in subclass.

        public override GameDefinition GetGameDefinition() => new AdditiveEvidenceGameDefinition();

        public override GameOptions GetDefaultSingleGameOptions() => AdditiveEvidenceGameOptionsGenerator.GetAdditiveEvidenceGameOptions();

        private enum OptionSetChoice
        {
            All,
            Fast,
            Original,
            OriginalPiecewise,
            TwoSets,
            OtherTwoSets,
            TrialGuaranteed,
            VaryingNoiseEtc,
            Temporary,
        }

        public override List<GameOptions> GetOptionsSets()
        {
            List<(string optionSetName, GameOptions options)> optionSets = new List<(string optionSetName, GameOptions options)>();
            OptionSetChoice optionSetChoice = OptionSetChoice.Original;
            bool withOptionNotToPlay = false;
            switch (optionSetChoice)
            {
                case OptionSetChoice.Original:
                    AddDMSGameOptionSets(optionSets, DMSVersion.Original, withOptionNotToPlay);
                    break;
                case OptionSetChoice.OriginalPiecewise:
                    AddDMSGameOptionSets(optionSets, DMSVersion.OriginalPiecewise, withOptionNotToPlay);
                    break;
                case OptionSetChoice.TwoSets:
                    AddDMSGameOptionSets(optionSets, DMSVersion.Original, withOptionNotToPlay);
                    AddDMSGameOptionSets(optionSets, DMSVersion.EvenStrengthAndBiasless, withOptionNotToPlay);
                    break;
                case OptionSetChoice.OtherTwoSets:
                    AddDMSGameOptionSets(optionSets, DMSVersion.Biasless, withOptionNotToPlay);
                    AddDMSGameOptionSets(optionSets, DMSVersion.EvenStrength, withOptionNotToPlay);
                    break;
                case OptionSetChoice.All:
                    AddDMSGameOptionSets(optionSets, DMSVersion.Original, withOptionNotToPlay);
                    AddDMSGameOptionSets(optionSets, DMSVersion.Biasless, withOptionNotToPlay);
                    AddDMSGameOptionSets(optionSets, DMSVersion.EvenStrength, withOptionNotToPlay);
                    AddDMSGameOptionSets(optionSets, DMSVersion.EvenStrengthAndBiasless, withOptionNotToPlay);
                    break;
                case OptionSetChoice.Fast:
                    AddDMSGameOptionSets(optionSets, DMSVersion.Original, false, false);
                    break;
                case OptionSetChoice.TrialGuaranteed:
                    AddDMSGameOptionSets(optionSets, DMSVersion.Baseline_WithTrialGuaranteed, false, true);
                    break;
                case OptionSetChoice.VaryingNoiseEtc:
                    AddDMSGameOptionSets(optionSets, DMSVersion.VaryNoise_00, false, true);
                    AddDMSGameOptionSets(optionSets, DMSVersion.VaryNoise_25, false, true);
                    AddDMSGameOptionSets(optionSets, DMSVersion.VaryNoise_50, false, true);
                    AddDMSGameOptionSets(optionSets, DMSVersion.VaryShared_00, false, true);
                    AddDMSGameOptionSets(optionSets, DMSVersion.VaryShared_25, false, true);
                    AddDMSGameOptionSets(optionSets, DMSVersion.VaryShared_50, false, true);
                    AddDMSGameOptionSets(optionSets, DMSVersion.VaryShared_75, false, true);
                    AddDMSGameOptionSets(optionSets, DMSVersion.VaryShared_100, false, true);
                    AddDMSGameOptionSets(optionSets, DMSVersion.VaryPStrength_00, false, true);
                    AddDMSGameOptionSets(optionSets, DMSVersion.VaryPStrength_25, false, true);
                    AddDMSGameOptionSets(optionSets, DMSVersion.VaryPStrength_50, false, true);
                    AddDMSGameOptionSets(optionSets, DMSVersion.VaryPStrength_75, false, true);
                    AddDMSGameOptionSets(optionSets, DMSVersion.VaryPStrength_100, false, true);
                    break;
                case OptionSetChoice.Temporary:
                    AddDMSGameOptionSets(optionSets, DMSVersion.VaryShared_100, false, true);
                    break;
            }

            optionSets = optionSets.OrderBy(x => x.optionSetName).ToList();

            bool simplify = false; // Enable for debugging purposes to speed up execution without going all the way to "fast" option
            if (simplify)
                foreach (var optionSet in optionSets)
                    optionSet.options.Simplify();

            return optionSets.Select(x => x.options.WithName(x.optionSetName)).ToList();
        }

        enum DMSVersion
        {
            Original,
            OriginalPiecewise,
            Biasless,
            EvenStrength,
            EvenStrengthAndBiasless,
            EvenStrengthAndBiasless_MoreInfoShared,
            Baseline_WithTrialGuaranteed, // same as VaryNoise_25 with trial guaranteed
            VaryNoise_00, // no noise; 50% of judgment depends on shared evaluation of quality; p, d private estimates evenly determine rest of judgment
            VaryNoise_25, // 25% noise; 50% of rest of judgment depends on shared evaluation of quality; p, d private estimates evenly determine rest of judgment
            VaryNoise_50, // 50% noise; 50% of rest of judgment depends on shared evaluation of quality; p, d private estimates evenly determine rest of judgment
            VaryShared_00, // 25% noise; no shared info on case quality; p, d private estimates evenly determine rest of judgment
            VaryShared_25, // 25% noise; 25% of rest of judgment depends on shared evaluation of quality; p, d private estimates evenly determine rest of judgment
            VaryShared_50, // 25% noise; 50% of rest of judgment depends on shared evaluation of quality; p, d private estimates evenly determine rest of judgment
            VaryShared_75, // 25% noise; 75% of rest of judgment depends on shared evaluation of quality; p, d private estimates evenly determine rest of judgment
            VaryShared_100, // 25% noise; 100% of rest of judgment depends on shared evaluation of quality; p, d private estimates are actually irrelevant
            VaryPStrength_00, // 25% noise; 50% of rest of judgment depends on shared evaluation of quality; p private estimate determines 0% of rest of judgment
            VaryPStrength_25, // 25% noise; 50% of rest of judgment depends on shared evaluation of quality; p private estimate determines 25% of rest of judgment
            VaryPStrength_50, // 25% noise; 50% of rest of judgment depends on shared evaluation of quality; p private estimate determines 50% of rest of judgment
            VaryPStrength_75, // 25% noise; 50% of rest of judgment depends on shared evaluation of quality; p private estimate determines 75% of rest of judgment
            VaryPStrength_100, // 25% noise; 50% of rest of judgment depends on shared evaluation of quality; p private estimate determines 100% of rest of judgment
        }

        private void AddDMSGameOptionSets(List<(string optionSetName, GameOptions options)> optionSets, DMSVersion version, bool withOptionNotToPlay, bool feeShifting = true)
        {
            // now, liability and damages only
                foreach (double? feeShiftingThreshold in feeShifting ? new double?[] { 0, 0.25, 0.50, 0.75, 1.0 } : new double?[] { 0 })
                    foreach (double costs in new double[] { 0, 0.0625, 0.125, 0.25, 0.50, 1.0 })
                    foreach (double qualityKnownToBoth in new double[] { 0.35, 0.50, 0.65 })
                    {
                        string settingsString = $"q{(int) (qualityKnownToBoth*100)}c{(int) (costs*100)}t{(int)(feeShiftingThreshold*100)}";
                        switch (version)
                        {
                            case DMSVersion.Original:
                                // this tracks the original model by DMS. The adjudicator's outcome depends half on some information about quality shared by both parties, and half on the sum of the parties' independent information (where this independent information does not count as part of the quality of the lawsuit).
                                optionSets.Add(GetAndTransform("orig", settingsString, () => AdditiveEvidenceGameOptionsGenerator.DariMattiacci_Saraceno_Original(qualityKnownToBoth, costs, feeShiftingThreshold != null, false, feeShiftingThreshold ?? 0, withOptionNotToPlay), x => { }));
                                break;
                            case DMSVersion.OriginalPiecewise:
                                // this tracks the original model by DMS. The adjudicator's outcome depends half on some information about quality shared by both parties, and half on the sum of the parties' independent information (where this independent information does not count as part of the quality of the lawsuit).
                                optionSets.Add(GetAndTransform("orig", settingsString, () => AdditiveEvidenceGameOptionsGenerator.DariMattiacci_Saraceno_Original(qualityKnownToBoth, costs, feeShiftingThreshold != null, false, feeShiftingThreshold ?? 0, withOptionNotToPlay), x => { x.PiecewiseLinearBids = true; }));
                                break;
                            case DMSVersion.Biasless:
                                // biasless means that all of the info that the adjudicator adds up counts in the quality measurement. Here, we continue to follow the DMS approach of making the plaintiff's proportion of information equal to the actual shared quality value (qualityKnownToBoth).
                                optionSets.Add(GetAndTransform("bl", settingsString, () => AdditiveEvidenceGameOptionsGenerator.Biasless(qualityKnownToBoth /* the actual value of the quality that both know about */, qualityKnownToBoth, costs, feeShiftingThreshold != null, false, feeShiftingThreshold ?? 0, 0.5 /* proportion of the total quality score that is shared -- i.e., equal to qualityKnownToBoth */, withOptionNotToPlay), x => { }));
                                break;
                            case DMSVersion.EvenStrength:
                                // even strength means that each party has the same amount of information. We still have bias here -- that is, the parties' private information has nothing to do with the merits. 
                                optionSets.Add(GetAndTransform("es", settingsString, () => AdditiveEvidenceGameOptionsGenerator.SharedInfoOnQuality_EvenStrengthOnBias(qualityKnownToBoth, costs, feeShiftingThreshold != null, false, feeShiftingThreshold ?? 0, withOptionNotToPlay), x => { }));
                                break;
                            case DMSVersion.EvenStrengthAndBiasless:
                                optionSets.Add(GetAndTransform("bl_es", settingsString, () => AdditiveEvidenceGameOptionsGenerator.Biasless(qualityKnownToBoth, 0.5, costs, feeShiftingThreshold != null, false, feeShiftingThreshold ?? 0, 0.5, withOptionNotToPlay), x => { }));
                                break;
                            case DMSVersion.EvenStrengthAndBiasless_MoreInfoShared:
                                optionSets.Add(GetAndTransform("mis", settingsString, () => AdditiveEvidenceGameOptionsGenerator.Biasless(qualityKnownToBoth, 0.5, costs, feeShiftingThreshold != null, false, feeShiftingThreshold ?? 0, 0.75, withOptionNotToPlay), x => { }));
                                break;
                            case DMSVersion.Baseline_WithTrialGuaranteed:
                                optionSets.Add(GetAndTransform("trialg", settingsString, () => AdditiveEvidenceGameOptionsGenerator.SomeNoise(0.25, 0.5, 0.5, qualityKnownToBoth, costs, feeShiftingThreshold != null, false, feeShiftingThreshold ?? 0, withOptionNotToPlay), x => { x.TrialGuaranteed = true; }));
                                break;
                            case DMSVersion.VaryNoise_00:
                                optionSets.Add(GetAndTransform("noise00", settingsString, () => AdditiveEvidenceGameOptionsGenerator.SomeNoise(0, 0.5, 0.5, qualityKnownToBoth, costs, feeShiftingThreshold != null, false, feeShiftingThreshold ?? 0, withOptionNotToPlay), x => { }));
                                break;
                            case DMSVersion.VaryNoise_25:
                                optionSets.Add(GetAndTransform("noise25", settingsString, () => AdditiveEvidenceGameOptionsGenerator.SomeNoise(0.25, 0.5, 0.5, qualityKnownToBoth, costs, feeShiftingThreshold != null, false, feeShiftingThreshold ?? 0, withOptionNotToPlay), x => { })); // NOTE: This is the baseline
                                break;
                            case DMSVersion.VaryNoise_50:
                                optionSets.Add(GetAndTransform("noise50", settingsString, () => AdditiveEvidenceGameOptionsGenerator.SomeNoise(0.50, 0.5, 0.5, qualityKnownToBoth, costs, feeShiftingThreshold != null, false, feeShiftingThreshold ?? 0, withOptionNotToPlay), x => { }));
                                break;
                            case DMSVersion.VaryShared_00:
                                optionSets.Add(GetAndTransform("shared00", settingsString, () => AdditiveEvidenceGameOptionsGenerator.SomeNoise(0.25, 0.0, 0.5, qualityKnownToBoth, costs, feeShiftingThreshold != null, false, feeShiftingThreshold ?? 0, withOptionNotToPlay), x => { }));
                                break;
                            case DMSVersion.VaryShared_25:
                                optionSets.Add(GetAndTransform("shared25", settingsString, () => AdditiveEvidenceGameOptionsGenerator.SomeNoise(0.25, 0.25, 0.5, qualityKnownToBoth, costs, feeShiftingThreshold != null, false, feeShiftingThreshold ?? 0, withOptionNotToPlay), x => { }));
                                break;
                            case DMSVersion.VaryShared_50:
                                optionSets.Add(GetAndTransform("shared50", settingsString, () => AdditiveEvidenceGameOptionsGenerator.SomeNoise(0.25, 0.50, 0.5, qualityKnownToBoth, costs, feeShiftingThreshold != null, false, feeShiftingThreshold ?? 0, withOptionNotToPlay), x => { }));
                                break;
                            case DMSVersion.VaryShared_75:
                                optionSets.Add(GetAndTransform("shared75", settingsString, () => AdditiveEvidenceGameOptionsGenerator.SomeNoise(0.25, 0.75, 0.5, qualityKnownToBoth, costs, feeShiftingThreshold != null, false, feeShiftingThreshold ?? 0, withOptionNotToPlay), x => { }));
                                break;
                            case DMSVersion.VaryShared_100:
                                optionSets.Add(GetAndTransform("shared100", settingsString, () => AdditiveEvidenceGameOptionsGenerator.SomeNoise(0.25, 1.0, 0.5, qualityKnownToBoth, costs, feeShiftingThreshold != null, false, feeShiftingThreshold ?? 0, withOptionNotToPlay), x => { }));
                                break;
                            case DMSVersion.VaryPStrength_00:
                                optionSets.Add(GetAndTransform("pstrength00", settingsString, () => AdditiveEvidenceGameOptionsGenerator.SomeNoise(0.25, 0.5, 0, qualityKnownToBoth, costs, feeShiftingThreshold != null, false, feeShiftingThreshold ?? 0, withOptionNotToPlay), x => { }));
                                break;
                            case DMSVersion.VaryPStrength_25:
                                optionSets.Add(GetAndTransform("pstrength25", settingsString, () => AdditiveEvidenceGameOptionsGenerator.SomeNoise(0.25, 0.5, 0.25, qualityKnownToBoth, costs, feeShiftingThreshold != null, false, feeShiftingThreshold ?? 0, withOptionNotToPlay), x => { }));
                                break;
                            case DMSVersion.VaryPStrength_50:
                                optionSets.Add(GetAndTransform("pstrength50", settingsString, () => AdditiveEvidenceGameOptionsGenerator.SomeNoise(0.25, 0.5, 0.5, qualityKnownToBoth, costs, feeShiftingThreshold != null, false, feeShiftingThreshold ?? 0, withOptionNotToPlay), x => { }));
                                break;
                            case DMSVersion.VaryPStrength_75:
                                optionSets.Add(GetAndTransform("pstrength75", settingsString, () => AdditiveEvidenceGameOptionsGenerator.SomeNoise(0.25, 0.5, 0.75, qualityKnownToBoth, costs, feeShiftingThreshold != null, false, feeShiftingThreshold ?? 0, withOptionNotToPlay), x => { }));
                                break;
                            case DMSVersion.VaryPStrength_100:
                                optionSets.Add(GetAndTransform("pstrength100", settingsString, () => AdditiveEvidenceGameOptionsGenerator.SomeNoise(0.25, 0.5, 1.0, qualityKnownToBoth, costs, feeShiftingThreshold != null, false, feeShiftingThreshold ?? 0, withOptionNotToPlay), x => { }));
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

            GamePlayer gamePlayer = new GamePlayer(starterStrategies, true, gameDefinition, true);
            AdditiveEvidenceGameProgress gameProgress = (AdditiveEvidenceGameProgress)gamePlayer.PlayUsingActionOverride(actionsOverride);

            return gameProgress;
        }
    }
}
