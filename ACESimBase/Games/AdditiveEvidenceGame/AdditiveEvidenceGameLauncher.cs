using ACESim;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ACESimBase.Games.AdditiveEvidenceGame
{
    public class AdditiveEvidenceGameLauncher : Launcher
    {
        OptionSetChoice optionSetChoice = OptionSetChoice.AdditiveEvidencePaperMain;

        // We can use this to allow for multiple options sets. These can then run in parallel. But note that we can also have multiple runs with a single option set using different settings by using GameDefinition scenarios; this is useful when there is a long initialization and it makes sense to complete one set before starting the next set.
        public override string MasterReportNameForDistributedProcessing => "AE026";

        public double[] CostsLevels = new double[] { 0, 0.0625, 0.125, 0.25, 0.5 };
        public double[] QualityLevels = new double[] { 0.2, 0.35, 0.50, 0.65, 0.8 }; 
        int numFeeShiftingThresholds = 11; // DEBUG
        public double[] FeeShiftingThresholds => Enumerable.Range(0, numFeeShiftingThresholds).Select(x => (double)(x / (numFeeShiftingThresholds - 1.0))).ToArray();

        public override GameDefinition GetGameDefinition() => new AdditiveEvidenceGameDefinition();

        public override GameOptions GetDefaultSingleGameOptions() => AdditiveEvidenceGameOptionsGenerator.GetAdditiveEvidenceGameOptions();

        // See below for choice
        private enum OptionSetChoice
        {
            EarlierSets,
            Fast,
            Original,
            TwoSets,
            OtherTwoSets,
            TrialGuaranteed,
            VaryingNoiseEtc,
            Temporary,
            EvenStrength,
            Biasless,
            WinnerTakesAll,
            AdditiveEvidencePaperMain,
        }
        
        public override List<GameOptions> GetOptionsSets()
        {
            var results = GetOptionSetsFromChoice(optionSetChoice);
            return results;
        }

        public List<List<AdditiveEvidenceGameOptions>> GetSetsOfOptionsSets()
        {
            List<List<AdditiveEvidenceGameOptions>> setsOfOptionsSets = new List<List<AdditiveEvidenceGameOptions>>();
            var set1 = GetOptionSetsFromChoice(optionSetChoice).Select(x => (AdditiveEvidenceGameOptions)x).ToList();
            setsOfOptionsSets.Add(set1);
            return setsOfOptionsSets;
        }

        public Dictionary<string, string> GetAdditiveEvidenceNameMap()
        {
            Dictionary<string, string> result = new Dictionary<string, string>();
            foreach (AdditiveEvidenceGameOptions gameOptions in GetSetsOfOptionsSets().SelectMany(x => x))
                result[gameOptions.Name] = gameOptions.Name;
            return result;
        }

        private List<GameOptions> GetOptionSetsFromChoice(OptionSetChoice optionSetChoice)
        {
            List<(string optionSetName, GameOptions options)> optionSets = new List<(string optionSetName, GameOptions options)>();
            bool withOptionNotToPlay = false;
            switch (optionSetChoice)
            {
                case OptionSetChoice.AdditiveEvidencePaperMain:
                    //AddDMSGameOptionSets(optionSets, DMSVersion.Original, false);
                    //AddDMSGameOptionSets(optionSets, DMSVersion.OriginalInitialized, false);
                    AddDMSGameOptionSets(optionSets, DMSVersion.OriginalRandomized, false);
                    //AddDMSGameOptionSets(optionSets, DMSVersion.WinnerTakesAllWithQuitting, false);
                    AddDMSGameOptionSets(optionSets, DMSVersion.WinnerTakesAll, false);
                    AddDMSGameOptionSets(optionSets, DMSVersion.EvenStrength, false);
                    break;
                case OptionSetChoice.Original:
                    AddDMSGameOptionSets(optionSets, DMSVersion.Original, withOptionNotToPlay);
                    break;
                case OptionSetChoice.WinnerTakesAll:
                    AddDMSGameOptionSets(optionSets, DMSVersion.WinnerTakesAll, withOptionNotToPlay);
                    break;
                case OptionSetChoice.EvenStrength:
                    AddDMSGameOptionSets(optionSets, DMSVersion.EvenStrength, withOptionNotToPlay);
                    break;
                case OptionSetChoice.Biasless:
                    AddDMSGameOptionSets(optionSets, DMSVersion.Biasless, withOptionNotToPlay);
                    break;
                case OptionSetChoice.TwoSets:
                    AddDMSGameOptionSets(optionSets, DMSVersion.Original, withOptionNotToPlay);
                    AddDMSGameOptionSets(optionSets, DMSVersion.EvenStrengthAndBiasless, withOptionNotToPlay);
                    break;
                case OptionSetChoice.OtherTwoSets:
                    AddDMSGameOptionSets(optionSets, DMSVersion.Biasless, withOptionNotToPlay);
                    AddDMSGameOptionSets(optionSets, DMSVersion.EvenStrength, withOptionNotToPlay);
                    break;
                case OptionSetChoice.EarlierSets:
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
            WinnerTakesAll,
            OriginalInitialized,
            OriginalRandomized,
            WinnerTakesAllWithQuitting,
        }

        private void AddDMSGameOptionSets(List<(string optionSetName, GameOptions options)> optionSets, DMSVersion version, bool withOptionNotToPlay, bool feeShifting = true)
        {
            foreach (double costs in CostsLevels)
            {
                foreach (double qualityKnownToBoth in QualityLevels)
                {
                    
                    foreach (double? feeShiftingThreshold in FeeShiftingThresholds)
                    {
                        string settingsString = $";q{(decimal) (qualityKnownToBoth):0.000};c{(decimal) (costs):0.000};t{(decimal)(feeShiftingThreshold):0.0000}";
                        switch (version)
                        {
                            case DMSVersion.Original:
                                // This tracks the original model by DMS. The adjudicator's outcome depends half on some information about quality shared by both parties, and half on the sum of the parties' independent information (where this independent information does not count as part of the quality of the lawsuit).
                                optionSets.Add(GetAndTransform("orig", settingsString, () => AdditiveEvidenceGameOptionsGenerator.DariMattiacci_Saraceno_Original(qualityKnownToBoth, costs, feeShiftingThreshold != null, false, feeShiftingThreshold ?? 0, withOptionNotToPlay), x => { }));
                                break;
                            case DMSVersion.OriginalInitialized:
                                // Same as above, but initialized to focus on the DMS value
                                optionSets.Add(GetAndTransform("orinit", settingsString, () => AdditiveEvidenceGameOptionsGenerator.DariMattiacci_Saraceno_Original(qualityKnownToBoth, costs, feeShiftingThreshold != null, false, feeShiftingThreshold ?? 0, withOptionNotToPlay), x => { x.ModifyEvolutionSettings = e => { e.CustomSequenceFormInitialization = true; }; }));
                                break;
                            case DMSVersion.OriginalRandomized:
                                // Same as above, but initialized to the DMS value
                                optionSets.Add(GetAndTransform("orran", settingsString, () => AdditiveEvidenceGameOptionsGenerator.DariMattiacci_Saraceno_Original(qualityKnownToBoth, costs, feeShiftingThreshold != null, false, feeShiftingThreshold ?? 0, withOptionNotToPlay), x => { x.ModifyEvolutionSettings = e => { e.SequenceFormUseRandomSeed = true; }; }));
                                break;
                            case DMSVersion.WinnerTakesAllWithQuitting:
                                optionSets.Add(GetAndTransform("quit", settingsString, () => AdditiveEvidenceGameOptionsGenerator.DariMattiacci_Saraceno_Original(qualityKnownToBoth, costs, feeShiftingThreshold != null, false, feeShiftingThreshold ?? 0, withOptionNotToPlay), x => { x.IncludePQuitDecision = x.IncludeDQuitDecision = true; x.WinnerTakesAll = true; }));
                                break;
                            case DMSVersion.WinnerTakesAll:
                                optionSets.Add(GetAndTransform("wta", settingsString, () => AdditiveEvidenceGameOptionsGenerator.DariMattiacci_Saraceno_Original(qualityKnownToBoth, costs, feeShiftingThreshold != null, false, feeShiftingThreshold ?? 0, withOptionNotToPlay), x => { x.WinnerTakesAll = true; x.ModifyEvolutionSettings = e => { e.SequenceFormUseRandomSeed = true; /* DEBUG */ }; }));
                                break;
                            case DMSVersion.Biasless:
                                // Biasless means that all of the info that the adjudicator adds up counts in the quality measurement. That is, there is some quality known to both parties, but the remaining quality is the sum of the two parties' information. Here, we continue to follow the DMS approach of making the plaintiff's proportion of information equal to the actual shared quality value (qualityKnownToBoth).
                                optionSets.Add(GetAndTransform("bl", settingsString, () => AdditiveEvidenceGameOptionsGenerator.Biasless(qualityKnownToBoth /* the actual value of the quality that both know about */, qualityKnownToBoth, costs, feeShiftingThreshold != null, false, feeShiftingThreshold ?? 0, 0.5 /* proportion of the total quality score that is shared -- i.e., equal to qualityKnownToBoth */, withOptionNotToPlay), x => { }));
                                break;
                            case DMSVersion.EvenStrength:
                                // even strength means that each party has the same amount of information. We still have bias here -- that is, the parties' private information has nothing to do with the merits. 
                                optionSets.Add(GetAndTransform("es", settingsString, () => AdditiveEvidenceGameOptionsGenerator.SharedInfoOnQuality_EvenStrengthOnBias(qualityKnownToBoth, costs, feeShiftingThreshold != null, false, feeShiftingThreshold ?? 0, withOptionNotToPlay), x => { x.ModifyEvolutionSettings = e => { e.SequenceFormUseRandomSeed = true; /* DEBUG */ }; }));
                                break;
                            case DMSVersion.EvenStrengthAndBiasless:
                                optionSets.Add(GetAndTransform("es_bl", settingsString, () => AdditiveEvidenceGameOptionsGenerator.Biasless(qualityKnownToBoth, 0.5, costs, feeShiftingThreshold != null, false, feeShiftingThreshold ?? 0, 0.5, withOptionNotToPlay), x => { }));
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
                        //if (feeShiftingThreshold != 0)
                        //    optionSets.Last().options.InitializeToMostRecentEquilibrium = true;
                    }
                }
            }
        }

        (string optionSetName, AdditiveEvidenceGameOptions options) GetAndTransform(string groupName, string suffix, Func<AdditiveEvidenceGameOptions> baseOptionsFn, Action<AdditiveEvidenceGameOptions> transform)
        {
            AdditiveEvidenceGameOptions g = baseOptionsFn();
            g.GroupName = groupName;
            transform(g);
            string suffix2 = suffix;
            // add further transformation based on additional params here
            return (groupName + suffix2, g);
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
