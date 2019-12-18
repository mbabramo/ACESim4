using ACESim;
using System;
using System.Collections.Generic;
using System.Text;

namespace ACESimBase.Games.AdditiveEvidenceGame
{
    public partial class AdditiveEvidenceGameDefinition
    {
        private AdditiveEvidenceGameProgress AEGP(GameProgress gp) => gp as AdditiveEvidenceGameProgress;

        public override List<SimpleReportDefinition> GetSimpleReportDefinitions()
        {
            var reports = new List<SimpleReportDefinition>
            {
                GetOverallReport()
            };

            return reports;
        }

        private SimpleReportDefinition GetOverallReport()
        {
            var colItems = new List<SimpleReportColumnItem>()
            {
                new SimpleReportColumnFilter("All", (GameProgress gp) => true, SimpleReportColumnFilterOptions.ProportionOfAll),
                new SimpleReportColumnFilter("PQuits", (GameProgress gp) => AEGP(gp).PQuits, SimpleReportColumnFilterOptions.ProportionOfRow),
                new SimpleReportColumnFilter("DQuits", (GameProgress gp) => AEGP(gp).DQuits, SimpleReportColumnFilterOptions.ProportionOfRow),
                new SimpleReportColumnFilter("Settles", (GameProgress gp) => AEGP(gp).SettlementOccurs, SimpleReportColumnFilterOptions.ProportionOfRow),
                new SimpleReportColumnFilter("Trial", (GameProgress gp) => AEGP(gp).TrialOccurs, SimpleReportColumnFilterOptions.ProportionOfRow),
                new SimpleReportColumnFilter("Shifting", (GameProgress gp) => AEGP(gp).ShiftingOccurs, SimpleReportColumnFilterOptions.ProportionOfRow),
                new SimpleReportColumnFilter("ShiftingOccursIfTrial", (GameProgress gp) => AEGP(gp).ShiftingOccursIfTrial, SimpleReportColumnFilterOptions.ProportionOfRow),
                new SimpleReportColumnVariable("ShiftingValueIfTrial", (GameProgress gp) => AEGP(gp).ShiftingValueIfTrial),
                new SimpleReportColumnVariable("POffer", (GameProgress gp) => AEGP(gp).POfferContinuousOrNull),
                new SimpleReportColumnVariable("DOffer", (GameProgress gp) => AEGP(gp).DOfferContinuousOrNull),
                new SimpleReportColumnVariable("AccSq", (GameProgress gp) => AEGP(gp).AccuracySquared),
                new SimpleReportColumnVariable("Accuracy", (GameProgress gp) => AEGP(gp).Accuracy),
                new SimpleReportColumnVariable("Accuracy_ForPlaintiff", (GameProgress gp) => AEGP(gp).Accuracy_ForPlaintiff),
                new SimpleReportColumnVariable("Accuracy_ForDefendant", (GameProgress gp) => AEGP(gp).Accuracy_ForDefendant),
                new SimpleReportColumnVariable("SettlementOrJudgment", (GameProgress gp) => AEGP(gp).ResolutionValue),
                new SimpleReportColumnVariable("DsProportionOfCost", (GameProgress gp) => AEGP(gp).DsProportionOfCost),
                new SimpleReportColumnVariable("DsProportionOfCostIfTrial", (GameProgress gp) => AEGP(gp).DsProportionOfCostIfTrial()),
                new SimpleReportColumnVariable("TrialValuePreShiftingIfOccurs", (GameProgress gp) => AEGP(gp).TrialValuePreShiftingIfOccurs),
                new SimpleReportColumnVariable("TrialValueWithShiftingIfOccurs", (GameProgress gp) => AEGP(gp).TrialValueWithShiftingIfOccurs),
                new SimpleReportColumnVariable("PTrialEffect_IfOccurs", (GameProgress gp) => AEGP(gp).PTrialEffect_IfOccurs),
                new SimpleReportColumnVariable("DTrialEffect_IfOccurs", (GameProgress gp) => AEGP(gp).DTrialEffect_IfOccurs),
                new SimpleReportColumnVariable("ResolutionValueIncludingShiftedAmount", (GameProgress gp) => AEGP(gp).ResolutionValueIncludingShiftedAmount),
                new SimpleReportColumnVariable("SettlementValue", (GameProgress gp) => AEGP(gp).SettlementValue),
                new SimpleReportColumnVariable("PWelfare", (GameProgress gp) => AEGP(gp).PWelfare),
                new SimpleReportColumnVariable("DWelfare", (GameProgress gp) => AEGP(gp).DWelfare),
                new SimpleReportColumnVariable("PQuality", (GameProgress gp) => AEGP(gp).Chance_Plaintiff_Quality_Continuous),
                new SimpleReportColumnVariable("DQuality", (GameProgress gp) => AEGP(gp).Chance_Defendant_Quality_Continuous),
                new SimpleReportColumnVariable("NQuality", (GameProgress gp) => AEGP(gp).Chance_Neither_Quality_Continuous_IfDetermined),
                new SimpleReportColumnVariable("QualitySum", (GameProgress gp) => AEGP(gp).QualitySum),
                new SimpleReportColumnVariable("QualitySum_PInfo", (GameProgress gp) => AEGP(gp).QualitySum_PInfoOnly),
                new SimpleReportColumnVariable("QualitySum_DInfo", (GameProgress gp) => AEGP(gp).QualitySum_DInfoOnly),
                new SimpleReportColumnVariable("PBias", (GameProgress gp) => AEGP(gp).Chance_Plaintiff_Bias_Continuous),
                new SimpleReportColumnVariable("DBias", (GameProgress gp) => AEGP(gp).Chance_Defendant_Bias_Continuous),
                new SimpleReportColumnVariable("NBias", (GameProgress gp) => AEGP(gp).Chance_Neither_Bias_Continuous_IfDetermined),
                new SimpleReportColumnVariable("BiasSum", (GameProgress gp) => AEGP(gp).BiasSum),
                new SimpleReportColumnVariable("BiasSum_PInfo", (GameProgress gp) => AEGP(gp).BiasSum_PInfoOnly),
                new SimpleReportColumnVariable("BiasSum_DInfo", (GameProgress gp) => AEGP(gp).BiasSum_DInfoOnly),
                new SimpleReportColumnVariable("Quality", (GameProgress gp) => AEGP(gp).AdditiveEvidenceGameOptions.Evidence_Both_Quality),
                new SimpleReportColumnVariable("Alpha_Quality", (GameProgress gp) => AEGP(gp).AdditiveEvidenceGameOptions.Alpha_Quality),
                new SimpleReportColumnVariable("Alpha_Both_Quality", (GameProgress gp) => AEGP(gp).AdditiveEvidenceGameOptions.Alpha_Both_Quality),
                new SimpleReportColumnVariable("Alpha_Plaintiff_Quality", (GameProgress gp) => AEGP(gp).AdditiveEvidenceGameOptions.Alpha_Plaintiff_Quality),
                new SimpleReportColumnVariable("Alpha_Defendant_Quality", (GameProgress gp) => AEGP(gp).AdditiveEvidenceGameOptions.Alpha_Defendant_Quality),
                new SimpleReportColumnVariable("Alpha_Plaintiff_Bias", (GameProgress gp) => AEGP(gp).AdditiveEvidenceGameOptions.Alpha_Plaintiff_Bias),
                new SimpleReportColumnVariable("Alpha_Defendant_Bias", (GameProgress gp) => AEGP(gp).AdditiveEvidenceGameOptions.Alpha_Defendant_Bias),
                new SimpleReportColumnVariable("TrialCost", (GameProgress gp) => AEGP(gp).AdditiveEvidenceGameOptions.TrialCost),
                new SimpleReportColumnVariable("FeeShifting", (GameProgress gp) => AEGP(gp).AdditiveEvidenceGameOptions.FeeShifting ? 1.0 : 0),
                new SimpleReportColumnVariable("FSMargin", (GameProgress gp) => AEGP(gp).AdditiveEvidenceGameOptions.FeeShiftingIsBasedOnMarginOfVictory ? 1.0 : 0),
                new SimpleReportColumnVariable("FeeShiftingThreshold", (GameProgress gp) => AEGP(gp).AdditiveEvidenceGameOptions.FeeShiftingThreshold),
            };
            List<SimpleReportFilter> rows;
            if (Options.FirstRowOnly)
                rows = new List<SimpleReportFilter>()
                {
                    new SimpleReportFilter("All", (GameProgress gp) => true),
                };
            else
            {
                rows = new List<SimpleReportFilter>()
                {
                    new SimpleReportFilter("All", (GameProgress gp) => true),
                    new SimpleReportFilter("Settles", (GameProgress gp) => AEGP(gp).SettlementOccurs),
                    new SimpleReportFilter("Trial", (GameProgress gp) => AEGP(gp).TrialOccurs),
                    new SimpleReportFilter("Shifting", (GameProgress gp) => AEGP(gp).ShiftingOccurs),
                };
                foreach ((string prefix, Func<GameProgress, byte, bool> filter, int n) r in new (string prefix, Func<GameProgress, byte, bool> filter, int n)[]
                {
                    ("PQuality", (GameProgress gp, byte s) => AEGP(gp).Chance_Plaintiff_Quality == s, Options.NumQualityAndBiasLevels),
                    ("DQuality", (GameProgress gp, byte s) => AEGP(gp).Chance_Defendant_Quality == s, Options.NumQualityAndBiasLevels),
                    ("NQuality", (GameProgress gp, byte s) => AEGP(gp).Chance_Neither_Quality == s, Options.NumQualityAndBiasLevels),
                    ("PBias", (GameProgress gp, byte s) => AEGP(gp).Chance_Plaintiff_Bias == s, Options.NumQualityAndBiasLevels),
                    ("DBias", (GameProgress gp, byte s) => AEGP(gp).Chance_Defendant_Bias == s, Options.NumQualityAndBiasLevels),
                    ("NBias", (GameProgress gp, byte s) => AEGP(gp).Chance_Neither_Bias == s, Options.NumQualityAndBiasLevels),
                    ("POffer", (GameProgress gp, byte s) => AEGP(gp).POffer == s, Options.NumOffers),
                    ("DOffer", (GameProgress gp, byte s) => AEGP(gp).DOffer == s, Options.NumOffers),
                })
                {
                    for (byte signal = 1; signal <= r.n; signal++)
                    {
                        byte s = signal; // important -- avoid closure
                        rows.Add(
                            new SimpleReportFilter(r.prefix + s, p => r.filter(p, s)));
                    }
                }
            }

            return new SimpleReportDefinition(
                "AdditiveEvidenceGameReport",
                null,
                rows,
                colItems
            );
        }
    }
}
