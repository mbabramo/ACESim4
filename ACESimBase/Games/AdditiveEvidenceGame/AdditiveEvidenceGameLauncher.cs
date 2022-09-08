using ACESim;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace ACESimBase.Games.AdditiveEvidenceGame
{
    public class AdditiveEvidenceGameLauncher : Launcher
    {
        OptionSetChoice optionSetChoice = OptionSetChoice.Main;

        // We can use this to allow for multiple options sets. These can then run in parallel. But note that we can also have multiple runs with a single option set using different settings by using GameDefinition scenarios; this is useful when there is a long initialization and it makes sense to complete one set before starting the next set.
        public override string MasterReportNameForDistributedProcessing => "AE042";

        public static bool UseSpecificOnly = false;
        public static bool LimitToNonTrivialDMS = false; 

        public double[] CostsLevels = UseSpecificOnly ? new double[] { 0.25 } : new double[] { 0, 0.0625, 0.125, 0.25, 0.5 };
        public double[] QualityLevels => UseSpecificOnly ? QualityLevels_Specific : new double[] { 0.2, 0.35, 0.50, 0.65, 0.8 };
        public double[] QualityLevels_Specific = new double[] { 0.35, 0.5 };
        int numFeeShiftingThresholds = 101;
        public bool specificThresholdsDefined = false;
        public double[] FeeShiftingThresholds => UseSpecificOnly && specificThresholdsDefined ? SpecificThresholdLevels : Enumerable.Range(0, numFeeShiftingThresholds).Select(x => (double)(x / (numFeeShiftingThresholds - 1.0))).ToArray();

        public override GameDefinition GetGameDefinition() => new AdditiveEvidenceGameDefinition();

        public override GameOptions GetDefaultSingleGameOptions() => AdditiveEvidenceGameOptionsGenerator.GetAdditiveEvidenceGameOptions();

        // See top of this file to select a choice
        private enum OptionSetChoice
        {
            Main,
            EvenStrength,
            Original,
            WinnerTakesAll,
            WinnerTakeAllWithQuitting,
            Fast,
            TrialGuaranteed,
            VaryingNoiseEtc,
            Temporary,
            Biasless,
            RevisitSpecific,
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
            switch (optionSetChoice)
            {
                case OptionSetChoice.Main:
                    AddDMSGameOptionSets(optionSets, DMSVersion.Original, false);
                    AddDMSGameOptionSets(optionSets, DMSVersion.EvenStrength, false);
                    AddDMSGameOptionSets(optionSets, DMSVersion.WinnerTakesAll, false);
                    AddDMSGameOptionSets(optionSets, DMSVersion.WinnerTakesAll, true);
                    break;
                case OptionSetChoice.Original:
                    AddDMSGameOptionSets(optionSets, DMSVersion.OriginalEvenPrior, false);
                    break;
                case OptionSetChoice.EvenStrength:
                    AddDMSGameOptionSets(optionSets, DMSVersion.EvenStrength, false);
                    break;
                case OptionSetChoice.WinnerTakeAllWithQuitting:
                    AddDMSGameOptionSets(optionSets, DMSVersion.WinnerTakesAll, true);
                    break;
                case OptionSetChoice.RevisitSpecific:
                    AddDMSGameOptionSets(optionSets, DMSVersion.RevisitSpecific, false);
                    break;
                case OptionSetChoice.WinnerTakesAll:
                    AddDMSGameOptionSets(optionSets, DMSVersion.WinnerTakesAll, false);
                    break;
                case OptionSetChoice.Biasless:
                    AddDMSGameOptionSets(optionSets, DMSVersion.Biasless, false);
                    break;
                case OptionSetChoice.Fast:
                    AddDMSGameOptionSets(optionSets, DMSVersion.OriginalEvenPrior, false, false);
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
            OriginalInitializedToDMS,
            OriginalEvenPrior,
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
            RevisitSpecific,
        }

        private void AddDMSGameOptionSets(List<(string optionSetName, GameOptions options)> optionSets, DMSVersion version, bool withOptionNotToPlay, bool feeShifting = true)
        {
            foreach (double costs in CostsLevels)
            {
                foreach (double qualityKnownToBoth in QualityLevels)
                {
                    
                    foreach (double? feeShiftingThreshold in FeeShiftingThresholds)
                    {
                        if (LimitToNonTrivialDMS)
                        {
                            DMSCalc dmsCalc = new DMSCalc((double) feeShiftingThreshold, costs, qualityKnownToBoth);
                            if (dmsCalc.GetCorrectStrategiesPair(true).Nontrivial == false)
                                continue;
                        }
                        
                        string settingsString = $";q{(decimal) (qualityKnownToBoth):0.000};c{(decimal) (costs):0.000};t{(decimal)(feeShiftingThreshold):0.0000}";
                        switch (version)
                        {
                            case DMSVersion.Original:
                                // Same as above, but using random seeds
                                optionSets.Add(GetAndTransform("orig", settingsString, () => AdditiveEvidenceGameOptionsGenerator.DariMattiacci_Saraceno_Original(qualityKnownToBoth, costs, feeShiftingThreshold != null, false, feeShiftingThreshold ?? 0, withOptionNotToPlay), x => { x.ModifyEvolutionSettings = e => { e.SequenceFormUseRandomSeed = true; }; }));
                                break;
                            case DMSVersion.OriginalEvenPrior:
                                // This tracks the original model by DMS. The adjudicator's outcome depends half on some information about quality shared by both parties, and half on the sum of the parties' independent information (where this independent information does not count as part of the quality of the lawsuit).
                                optionSets.Add(GetAndTransform("oreven", settingsString, () => AdditiveEvidenceGameOptionsGenerator.DariMattiacci_Saraceno_Original(qualityKnownToBoth, costs, feeShiftingThreshold != null, false, feeShiftingThreshold ?? 0, withOptionNotToPlay), x => { }));
                                break;
                            case DMSVersion.OriginalInitializedToDMS:
                                // Same as above, but initialized to focus on the DMS value
                                optionSets.Add(GetAndTransform("orinit", settingsString, () => AdditiveEvidenceGameOptionsGenerator.DariMattiacci_Saraceno_Original(qualityKnownToBoth, costs, feeShiftingThreshold != null, false, feeShiftingThreshold ?? 0, withOptionNotToPlay), x => { x.ModifyEvolutionSettings = e => { e.CustomSequenceFormInitialization = true; }; }));
                                break;
                            case DMSVersion.WinnerTakesAll:
                                optionSets.Add(GetAndTransform("wta" + (withOptionNotToPlay ? "q" : ""), settingsString, () => AdditiveEvidenceGameOptionsGenerator.DariMattiacci_Saraceno_Original(qualityKnownToBoth, costs, feeShiftingThreshold != null, false, feeShiftingThreshold ?? 0, withOptionNotToPlay, winnerTakesAll: true), x => { x.ModifyEvolutionSettings = e => { e.SequenceFormUseRandomSeed = true; }; }));
                                break;
                            case DMSVersion.RevisitSpecific:
                                // Same as above, but initialized to the DMS value
                                optionSets.Add(GetAndTransform("spec", settingsString, () => AdditiveEvidenceGameOptionsGenerator.DariMattiacci_Saraceno_Original(qualityKnownToBoth, costs, feeShiftingThreshold != null, false, feeShiftingThreshold ?? 0, withOptionNotToPlay), x => { x.ModifyEvolutionSettings = e => { e.SequenceFormUseRandomSeed = true; }; }));
                                break;
                            case DMSVersion.Biasless:
                                // Biasless means that all of the info that the adjudicator adds up counts in the quality measurement. That is, there is some quality known to both parties, but the remaining quality is the sum of the two parties' information. Here, we continue to follow the DMS approach of making the plaintiff's proportion of information equal to the actual shared quality value (qualityKnownToBoth).
                                optionSets.Add(GetAndTransform("bl", settingsString, () => AdditiveEvidenceGameOptionsGenerator.Biasless(qualityKnownToBoth /* the actual value of the quality that both know about */, qualityKnownToBoth, costs, feeShiftingThreshold != null, false, feeShiftingThreshold ?? 0, 0.5 /* proportion of the total quality score that is shared -- i.e., equal to qualityKnownToBoth */, withOptionNotToPlay), x => { }));
                                break;
                            case DMSVersion.EvenStrength:
                                // even strength means that each party has the same amount of information. We still have bias here -- that is, the parties' private information has nothing to do with the merits. 
                                optionSets.Add(GetAndTransform("es", settingsString, () => AdditiveEvidenceGameOptionsGenerator.SharedInfoOnQuality_EvenStrengthOnBias(qualityKnownToBoth, costs, feeShiftingThreshold != null, false, feeShiftingThreshold ?? 0, withOptionNotToPlay), x => { x.ModifyEvolutionSettings = e => { e.SequenceFormUseRandomSeed = true; }; }));
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

        public double[] SpecificThresholdLevels = new double[] { 0, 0.001, 0.004, 0.005, 0.006, 0.007, 0.008, 0.009, 0.01, 0.011, 0.012, 0.013, 0.014, 0.016, 0.017, 0.018, 0.019, 0.02, 0.021, 0.022, 0.023, 0.024, 0.025, 0.026, 0.027, 0.028, 0.029, 0.03, 0.031, 0.032, 0.033, 0.034, 0.035, 0.036, 0.037, 0.038, 0.039, 0.04, 0.041, 0.042, 0.043, 0.044, 0.046, 0.047, 0.048, 0.049, 0.05, 0.051, 0.052, 0.053, 0.054, 0.055, 0.056, 0.057, 0.059, 0.061, 0.062, 0.063, 0.064, 0.066, 0.067, 0.068, 0.069, 0.07, 0.071, 0.072, 0.073, 0.074, 0.075, 0.076, 0.077, 0.078, 0.079, 0.08, 0.081, 0.082, 0.083, 0.084, 0.085, 0.086, 0.087, 0.088, 0.089, 0.09, 0.091, 0.092, 0.093, 0.094, 0.095, 0.096, 0.097, 0.098, 0.099, 0.1, 0.101, 0.102, 0.103, 0.104, 0.105, 0.106, 0.107, 0.108, 0.109, 0.11, 0.111, 0.112, 0.113, 0.114, 0.115, 0.116, 0.117, 0.118, 0.119, 0.12, 0.121, 0.122, 0.123, 0.124, 0.125, 0.126, 0.127, 0.128, 0.129, 0.13, 0.131, 0.133, 0.134, 0.135, 0.136, 0.137, 0.139, 0.14, 0.141, 0.142, 0.143, 0.144, 0.145, 0.146, 0.147, 0.148, 0.149, 0.15, 0.151, 0.152, 0.153, 0.154, 0.155, 0.156, 0.157, 0.158, 0.16, 0.161, 0.162, 0.163, 0.164, 0.165, 0.166, 0.167, 0.168, 0.169, 0.17, 0.172, 0.173, 0.174, 0.175, 0.176, 0.177, 0.178, 0.179, 0.18, 0.182, 0.183, 0.184, 0.185, 0.186, 0.187, 0.188, 0.19, 0.192, 0.193, 0.194, 0.195, 0.196, 0.197, 0.198, 0.199, 0.201, 0.202, 0.203, 0.204, 0.205, 0.207, 0.208, 0.209, 0.21, 0.211, 0.212, 0.213, 0.214, 0.215, 0.217, 0.218, 0.219, 0.22, 0.221, 0.222, 0.223, 0.224, 0.225, 0.226, 0.227, 0.228, 0.229, 0.23, 0.231, 0.232, 0.233, 0.234, 0.235, 0.236, 0.237, 0.238, 0.239, 0.241, 0.242, 0.243, 0.244, 0.245, 0.246, 0.247, 0.248, 0.249, 0.251, 0.252, 0.253, 0.254, 0.255, 0.256, 0.257, 0.259, 0.26, 0.261, 0.262, 0.263, 0.264, 0.265, 0.266, 0.267, 0.268, 0.269, 0.27, 0.271, 0.272, 0.273, 0.274, 0.275, 0.276, 0.277, 0.278, 0.279, 0.28, 0.281, 0.282, 0.283, 0.284, 0.285, 0.286, 0.287, 0.288, 0.289, 0.29, 0.291, 0.292, 0.293, 0.294, 0.295, 0.296, 0.297, 0.3, 0.301, 0.302, 0.304, 0.305, 0.306, 0.307, 0.308, 0.309, 0.31, 0.311, 0.312, 0.313, 0.314, 0.315, 0.316, 0.317, 0.318, 0.319, 0.32, 0.321, 0.322, 0.323, 0.324, 0.325, 0.326, 0.327, 0.328, 0.33, 0.331, 0.332, 0.334, 0.335, 0.336, 0.337, 0.338, 0.34, 0.341, 0.342, 0.343, 0.344, 0.346, 0.347, 0.348, 0.349, 0.35, 0.351, 0.352, 0.353, 0.354, 0.355, 0.356, 0.357, 0.358, 0.359, 0.36, 0.361, 0.362, 0.363, 0.364, 0.365, 0.366, 0.367, 0.368, 0.369, 0.37, 0.371, 0.372, 0.373, 0.374, 0.375, 0.376, 0.377, 0.379, 0.38, 0.381, 0.382, 0.383, 0.384, 0.385, 0.386, 0.387, 0.388, 0.389, 0.39, 0.391, 0.392, 0.393, 0.394, 0.395, 0.396, 0.397, 0.398, 0.399, 0.4, 0.401, 0.402, 0.403, 0.404, 0.405, 0.406, 0.407, 0.408, 0.409, 0.41, 0.411, 0.412, 0.413, 0.414, 0.415, 0.416, 0.417, 0.418, 0.419, 0.42, 0.421, 0.422, 0.423, 0.424, 0.425, 0.426, 0.427, 0.428, 0.429, 0.43, 0.431, 0.432, 0.433, 0.434, 0.435, 0.436, 0.437, 0.438, 0.439, 0.44, 0.441, 0.442, 0.443, 0.444, 0.445, 0.446, 0.447, 0.448, 0.449, 0.45, 0.451, 0.452, 0.453, 0.454, 0.455, 0.456, 0.457, 0.458, 0.459, 0.46, 0.461, 0.462, 0.463, 0.464, 0.465, 0.466, 0.467, 0.468, 0.469, 0.47, 0.471, 0.472, 0.473, 0.474, 0.475, 0.476, 0.477, 0.478, 0.479, 0.48, 0.481, 0.482, 0.483, 0.484, 0.485, 0.486, 0.488, 0.489, 0.49, 0.491, 0.492, 0.493, 0.495, 0.496, 0.497, 0.498, 0.499, 0.5, 0.501, 0.502, 0.504, 0.505, 0.506, 0.507, 0.508, 0.509, 0.51, 0.511, 0.512, 0.513, 0.515, 0.517, 0.518, 0.519, 0.52, 0.521, 0.522, 0.523, 0.524, 0.525 };

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
