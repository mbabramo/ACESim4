using ACESim;
using System;
using System.Collections.Generic;
using System.Text;

namespace ACESimBase.Games.DMSReplicationGame
{
    public partial class DMSReplicationGameDefinition
    {
        private DMSReplicationGameProgress DMSProg(GameProgress gp) => gp as DMSReplicationGameProgress;

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
                new SimpleReportColumnVariable("PMinValue", (GameProgress gp) => DMSProg(gp).PMinValue),
                new SimpleReportColumnVariable("PSlope", (GameProgress gp) => DMSProg(gp).PSlope),
                new SimpleReportColumnVariable("PTruncationPortion", (GameProgress gp) => DMSProg(gp).PTruncationPortion),
                new SimpleReportColumnVariable("DMinValue", (GameProgress gp) => DMSProg(gp).DMinValue),
                new SimpleReportColumnVariable("DSlope", (GameProgress gp) => DMSProg(gp).DSlope),
                new SimpleReportColumnVariable("DTruncationPortion", (GameProgress gp) => DMSProg(gp).DTruncationPortion),
                new SimpleReportColumnVariable("settleProportion", (GameProgress gp) => DMSProg(gp).Outcomes.settleProportion),
                new SimpleReportColumnVariable("PWelfare", (GameProgress gp) => DMSProg(gp).Outcomes.p),
                new SimpleReportColumnVariable("DWelfare", (GameProgress gp) => DMSProg(gp).Outcomes.d),
                new SimpleReportColumnVariable("pPaysForDProportion", (GameProgress gp) => DMSProg(gp).Outcomes.pPaysForDProportion),
                new SimpleReportColumnVariable("dPaysForPProportion", (GameProgress gp) => DMSProg(gp).Outcomes.dPaysForPProportion),
            };
            List<SimpleReportFilter> rows;
            rows = new List<SimpleReportFilter>()
            {
                new SimpleReportFilter("All", (GameProgress gp) => true),
            };
           

            return new SimpleReportDefinition(
                "DMSReplicationGameReport",
                null,
                rows,
                colItems
            );
        }
    }
}
