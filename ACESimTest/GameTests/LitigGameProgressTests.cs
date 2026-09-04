using System.Collections.Generic;
using ACESim;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ACESimTest.GameTests
{
    [TestClass]
    public class LitigGameProgressTests
    {
        [TestMethod]
        public void WeightAlternativeEndings_AggregatesBothPartiesOutcomes()
        {
            var firstEnding = new LitigGameProgress(false)
            {
                PChangeWealth = 2,
                DChangeWealth = -3,
                PFinalWealth = 12,
                DFinalWealth = 7,
                PWelfare = 4,
                DWelfare = 5
            };
            var secondEnding = new LitigGameProgress(false)
            {
                PChangeWealth = -2,
                DChangeWealth = 1,
                PFinalWealth = 8,
                DFinalWealth = 11,
                PWelfare = 2,
                DWelfare = 9
            };
            var collapsedProgress = new LitigGameProgress(false)
            {
                DFinalWealth = -999,
                AlternativeEndings = new List<(LitigGameProgress completedGame, double weight)>
                {
                    (firstEnding, 0.25),
                    (secondEnding, 0.75)
                }
            };

            collapsedProgress.WeightAlternativeEndings();

            collapsedProgress.PChangeWealth.Should().Be(-1);
            collapsedProgress.DChangeWealth.Should().Be(0);
            collapsedProgress.PFinalWealth.Should().Be(9);
            collapsedProgress.DFinalWealth.Should().Be(10);
            collapsedProgress.PWelfare.Should().Be(2.5);
            collapsedProgress.DWelfare.Should().Be(8);
            collapsedProgress.TrialOccurs.Should().BeFalse(
                "abandonment/default alternative endings do not constitute a trial");
        }

        [TestMethod]
        public void WeightAlternativeEndings_PreservesTrialStatusForCollapsedCourtOutcomes()
        {
            var collapsedProgress = new LitigGameProgress(false)
            {
                AlternativeEndings = new List<(LitigGameProgress completedGame, double weight)>
                {
                    (new LitigGameProgress(false) { TrialOccurs = true }, 0.4),
                    (new LitigGameProgress(false) { TrialOccurs = true }, 0.6)
                }
            };

            collapsedProgress.WeightAlternativeEndings();

            collapsedProgress.TrialOccurs.Should().BeTrue();
        }
    }
}
