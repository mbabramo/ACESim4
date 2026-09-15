using System;
using System.Linq;
using ACESim;
using ACESimBase.Games.LitigGame.ManualReports;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using LitigCharts;

namespace ACESimTest.GameTests;

[TestClass]
public class CoreDirectedInterventionTests
{
    [TestMethod]
    public void EveryCoreDirectedFeeAndRiskChangeIsAdmissibleButJointChangesAreRejected()
    {
        var cases=new[] { LitigGameCorrelatedSignalsArticleLauncher.ProductionRunPlan.FocusedContinuousMerits,
            LitigGameCorrelatedSignalsArticleLauncher.ProductionRunPlan.ExitFeeShifting }
            .SelectMany(p=>new LitigGameCorrelatedSignalsArticleLauncher(p).GetOptionsSets().Cast<LitigGameOptions>())
            .Where(o=>o.CostsMultiplier==1 && o.NumOffers==10 &&
                (o.Name.StartsWith("Specification-Baseline__") || o.Name.StartsWith("Specification-ModerateRiskAversion__"))).ToArray();
        cases.Should().HaveCount(6);
        int accepted=0;
        foreach(var a in cases)
        foreach(var b in cases.Where(b=>b.Name!=a.Name))
        {
            bool fee=LitigGameCorrelatedSignalsArticleLauncher.FeeRuleLabel(a)!=LitigGameCorrelatedSignalsArticleLauncher.FeeRuleLabel(b);
            bool risk=a.VariableSettings["CARA Alpha"].ToString()!=b.VariableSettings["CARA Alpha"].ToString();
            Action check=()=>ArticlePressureAnalysis.ValidateMatchedOptions(a,b);
            if(fee!=risk)
            {
                check.Should().NotThrow();accepted++;
                var heading=EquilibriumChangeTables.Heading(a.Name,b.Name);
                heading.Original.Should().Contain(LitigGameCorrelatedSignalsArticleLauncher.FeeRuleLabel(a));
                heading.Target.Should().Contain(LitigGameCorrelatedSignalsArticleLauncher.FeeRuleLabel(b));
            }
            else check.Should().Throw<System.IO.InvalidDataException>();
        }
        accepted.Should().Be(18);
    }
}
