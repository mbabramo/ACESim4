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
                new SimpleReportColumnFilter("Settles", (GameProgress gp) => AEGP(gp).SettlementOccurs, SimpleReportColumnFilterOptions.ProportionOfRow),
                new SimpleReportColumnFilter("Trial", (GameProgress gp) => AEGP(gp).TrialOccurs, SimpleReportColumnFilterOptions.ProportionOfRow),
                new SimpleReportColumnFilter("Shifting", (GameProgress gp) => AEGP(gp).ShiftingOccurs, SimpleReportColumnFilterOptions.ProportionOfRow),
                new SimpleReportColumnVariable("POffer", (GameProgress gp) => AEGP(gp).POfferContinuous),
                new SimpleReportColumnVariable("DOffer", (GameProgress gp) => AEGP(gp).DOfferContinuous),
                new SimpleReportColumnVariable("AccSq", (GameProgress gp) => AEGP(gp).AccuracyIgnoringCostsSquared),
                new SimpleReportColumnVariable("AccuracyIgnoringCosts", (GameProgress gp) => AEGP(gp).AccuracyIgnoringCosts),
                new SimpleReportColumnVariable("Accuracy_ForPlaintiff", (GameProgress gp) => AEGP(gp).Accuracy_ForPlaintiff),
                new SimpleReportColumnVariable("Accuracy_ForDefendant", (GameProgress gp) => AEGP(gp).Accuracy_ForDefendant),
                new SimpleReportColumnVariable("SettlementOrJudgment", (GameProgress gp) => AEGP(gp).SettlementOrJudgment),
                new SimpleReportColumnVariable("DsProportionOfCost", (GameProgress gp) => AEGP(gp).DsProportionOfCost),
                new SimpleReportColumnVariable("TrialValueIfOccurs", (GameProgress gp) => AEGP(gp).TrialValueIfOccurs),
                new SimpleReportColumnVariable("PTrialEffect_IfOccurs", (GameProgress gp) => AEGP(gp).PTrialEffect_IfOccurs),
                new SimpleReportColumnVariable("DTrialEffect_IfOccurs", (GameProgress gp) => AEGP(gp).DTrialEffect_IfOccurs),
                new SimpleReportColumnVariable("PQuality", (GameProgress gp) => AEGP(gp).Chance_Plaintiff_Quality_Continuous),
                new SimpleReportColumnVariable("DQuality", (GameProgress gp) => AEGP(gp).Chance_Defendant_Quality_Continuous),
                new SimpleReportColumnVariable("NQuality", (GameProgress gp) => AEGP(gp).Chance_Neither_Quality_Continuous),
                new SimpleReportColumnVariable("QualitySum", (GameProgress gp) => AEGP(gp).QualitySum),
                new SimpleReportColumnVariable("QualitySum_PInfo", (GameProgress gp) => AEGP(gp).QualitySum_PInfoOnly),
                new SimpleReportColumnVariable("QualitySum_DInfo", (GameProgress gp) => AEGP(gp).QualitySum_DInfoOnly),
                new SimpleReportColumnVariable("PBias", (GameProgress gp) => AEGP(gp).Chance_Plaintiff_Bias_Continuous),
                new SimpleReportColumnVariable("DBias", (GameProgress gp) => AEGP(gp).Chance_Defendant_Bias_Continuous),
                new SimpleReportColumnVariable("NBias", (GameProgress gp) => AEGP(gp).Chance_Neither_Bias_Continuous),
                new SimpleReportColumnVariable("BiasSum", (GameProgress gp) => AEGP(gp).BiasSum),
                new SimpleReportColumnVariable("BiasSum_PInfo", (GameProgress gp) => AEGP(gp).BiasSum_PInfoOnly),
                new SimpleReportColumnVariable("BiasSum_DInfo", (GameProgress gp) => AEGP(gp).BiasSum_DInfoOnly),
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
                    new SimpleReportFilter("Settles", (GameProgress gp) => !AEGP(gp).SettlementOccurs),
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
