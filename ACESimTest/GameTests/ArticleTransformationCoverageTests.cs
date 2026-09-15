using ACESim;
using ACESimBase.Games.LitigGame.ManualReports;
using FluentAssertions;
using LitigCharts;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using static ACESim.LitigGameCorrelatedSignalsArticleLauncher;

namespace ACESimTest.GameTests;

[TestClass]
public class ArticleTransformationCoverageTests
{
    [TestMethod]
    public void EveryTransformationAndCostHasAllSixCoreCases()
    {
        var matrix = RoutineCaseMatrix();
        matrix.Should().HaveCount(276);
        matrix.GroupBy(row => (row.Transformation, row.Offers)).Should().HaveCount(10);
        foreach (var family in matrix.GroupBy(row => (row.Transformation, row.Offers)))
        {
            family.Select(row => row.Cost).Distinct().Should().BeEquivalentTo(
                family.Key.Offers == 15 ? new[] { 1.0 } : new[] { .25, .5, 1, 2, 4 });
            foreach (var cost in family.GroupBy(row => row.Cost))
            {
                cost.Should().HaveCount(6);
                cost.GroupBy(row => row.Risk).Should().HaveCount(2);
                foreach (var risk in cost.GroupBy(row => row.Risk))
                    risk.Select(row => row.FeeRule).Should().BeEquivalentTo(
                        "American", "Trial Fee-Shifting", "Complete Fee-Shifting");
            }
        }
        ValidateRoutineCaseCoverage(matrix.Select(row => row.OptionSetName));
    }

    [TestMethod]
    public void MissingCompleteHighNoiseCasesAndDuplicateCasesAreRejected()
    {
        var matrix = RoutineCaseMatrix();
        Action missing = () => ValidateRoutineCaseCoverage(matrix.Where(row =>
            !(row.Transformation == "HighNoise" && row.FeeRule == "Complete Fee-Shifting"))
            .Select(row => row.OptionSetName));
        missing.Should().Throw<System.IO.InvalidDataException>().WithMessage("*10 missing*HighNoise*");
        Action duplicate = () => ValidateRoutineCaseCoverage(matrix.Select(row => row.OptionSetName)
            .Append(matrix[0].OptionSetName));
        duplicate.Should().Throw<System.IO.InvalidDataException>().WithMessage("*duplicates: 1*");
    }

    [TestMethod]
    public void AllCasesCanBeResolvedByDiagramCodeAndRiskVariantsShareTheirFamily()
    {
        foreach (var row in RoutineCaseMatrix())
        {
            var options = ArticleWorkedPathExtraction.CreateOptions(row.OptionSetName);
            options.NumOffers.Should().Be((byte)row.Offers);
            FeeRuleLabel(options).Should().Be(row.FeeRule);
            var metadata = new Dictionary<string, string> {
                ["OptionSetName"] = row.OptionSetName, ["Number of Offers"] = row.Offers.ToString() };
            string family = WelfareOutcomeExhibits.Family(metadata);
            Action layout = () => ArticleResultsLayout.Specification(family);
            layout.Should().NotThrow();
            family.Should().NotContain("risk");
        }
    }

    [TestMethod]
    public void All124ExistingProfileIdentifiersRemainReusable()
    {
        var legacy = new[] { "Baseline", "LowNoise", "HighNoise", "ModerateRiskAversion",
            "LowNoiseModerateRiskAversion", "DirectBinaryStateSignals", "TruthConditionedLatentMerits",
            "CenterWeightedContinuousMerits", "PolarizedContinuousMerits", "AllCostsAvoidable", "AllCostsSunk" };
        var expected = new List<string>();
        foreach (string spec in legacy)
        foreach (string cost in new[] { "0.25", "0.5", "1", "2", "4" })
        foreach (string fee in new[] { "American", "British" })
            expected.Add($"CS004 Specification-{spec}__Cost-{cost}__Fee-{fee} -equ.csv");
        foreach (string spec in new[] { "Baseline", "ModerateRiskAversion" })
        {
            foreach (string fee in new[] { "American", "British" })
                expected.Add($"CS004 Specification-{spec}__Cost-1__Fee-{fee}__Offers-15 -equ.csv");
            foreach (string cost in new[] { "0.25", "0.5", "1", "2", "4" })
                expected.Add($"CS006EF Specification-{spec}__Cost-{cost}__Fee-British__ExitFees-AllUnilateralExits -equ.csv");
        }
        expected.Should().HaveCount(124);
        RoutineCaseMatrix().Select(row => row.EquilibriumFileName).Should().Contain(expected);
    }
}
