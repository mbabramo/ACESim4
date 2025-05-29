using System.Collections.Generic;
using System.Linq;
using ACESimBase.Games.LitigGame.ManualReports;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ACESimTest
{
    [TestClass]
    public class CostBreakdownReportTests
    {
        // handy alias
        private static CostBreakdownReport.Slice S(double w, double opp, double harm) =>
            new(w, opp, harm, 0, 0, 0, 0);

        // ---------------------------------------------------------------
        [TestMethod]
        public void ComputeScaling_ParityInsideOneDiagram()
        {
            // two slices, equal probability
            var slices = new List<CostBreakdownReport.Slice>
            {
                S(0.5,1,0),   // left panel (harm==0)
                S(0.5,0,2)    // right panel
            };

            var info = CostBreakdownReport.ComputeScaling(slices, rightAxisTop: 4);

            // pLeft == pRight ⇒ yMaxLeft should equal yMaxRight
            info.YMaxLeft.Should().BeApproximately(info.YMaxRight, 1e-9);

            // area/unit parity: X/Y identical on both sides
            double areaLeft = info.XScaleLeft / info.YMaxLeft;
            double areaRight = info.XScaleRight / info.YMaxRight;
            areaLeft.Should().BeApproximately(areaRight, 1e-12);
        }

        // ---------------------------------------------------------------
        [TestMethod]
        public void ComputeScalingFromReference_MatchesReferenceArea()
        {
            // reference diagram
            var refSlices = new List<CostBreakdownReport.Slice>
            {
                S(0.5,1,0),   // left
                S(0.5,0,2)    // right
            };
            var refInfo = CostBreakdownReport.ComputeScaling(refSlices, 4);

            // new diagram (costs doubled)
            var newSlices = new List<CostBreakdownReport.Slice>
            {
                S(0.5,2,0),
                S(0.5,0,4)
            };
            var newInfo = CostBreakdownReport.ComputeScalingFromReference(
                              newSlices, refSlices, 4);

            newInfo.AreaPerUnit
                   .Should().BeApproximately(refInfo.AreaPerUnit, 1e-12);

            // internal parity still holds
            (newInfo.XScaleLeft / newInfo.YMaxLeft)
                .Should().BeApproximately(
                     newInfo.XScaleRight / newInfo.YMaxRight, 1e-12);
        }

        // ---------------------------------------------------------------
        [TestMethod]
        public void FindMinimalSharedRightAxisTop_ReturnsLargestNeed()
        {
            // diagram 1 needs R ≥ 1
            var d1 = new List<CostBreakdownReport.Slice>
            {
                S(0.5,1,0),
                S(0.5,0,1)
            };

            // diagram 2 needs R ≥ 3
            var d2 = new List<CostBreakdownReport.Slice>
            {
                S(0.5,2,0),
                S(0.5,0,3)
            };

            double R = CostBreakdownReport.FindMinimalSharedRightAxisTop(
                           new List<List<CostBreakdownReport.Slice>> { d1, d2 });

            R.Should().BeApproximately(3, 1e-12);
        }

        // local alias for brevity
        private static CostBreakdownReport.Slice S(
            double w, double opp, double harm,
            double file = 0, double ans = 0, double barg = 0, double tri = 0)
            => new(w, opp, harm, file, ans, barg, tri);

        // ------------------------------------------------------------
        [TestMethod]
        public void NonSplitDiagram_WithFourSlices_RendersTikz()
        {
            var slices = new List<CostBreakdownReport.Slice>
            {
                // left-panel slices (no harm etc.)
                S(0.3,0.2,0),
                S(0.3,0.3,0),
                // right-panel slices
                S(0.15,0.1,0.6),
                S(0.25,0.05,1,1.0)
            };

            var scale = CostBreakdownReport
                .ComputeSinglePanelScaling(slices, 4.0);

            string tikz = CostBreakdownReport.TikzScaled(slices, scale,
                                            false, "four-slice demo",
                                            false /* no split */);

            tikz.Should().StartWith(@"\documentclass{standalone}");
            tikz.Should().NotContain("dashed,very thin");   // no midline
        }

        // ------------------------------------------------------------
        [TestMethod]
        public void SplitPanel_RareHarmScenario_RendersWithDivider()
        {
            var slices = new List<CostBreakdownReport.Slice>
            {
                // LEFT – precaution only (four uneven weights)
                S(0.50, 0.05, 0),      // area = 0.50 × 0.05 = 0.025
                S(0.30, 0.07, 0),      //        0.30 × 0.07 = 0.021
                S(0.15, 0.02, 0),      //        0.15 × 0.02 = 0.003
                S(0.04, 0.015,0),      //        0.04 × 0.015≈0.0006
                                       // left-panel total area ≈ 0.049 ≈ 0.05

                // RIGHT – full litigation with trial (all categories present)
                // choose total cost ≈ 5 so area = 0.01 × 5 = 0.05  (matches left)
                // w      Opp  Harm File Ans  Barg Trial   Σcost  area
                S(0.01, 0.05, 3,   0.8, 0.4, 0.2, 0.55 )  // 5.0   0.05
            };

            var scale = CostBreakdownReport
                .ComputeScaling(slices, rightAxisTop: 6.0);

            string tikz = CostBreakdownReport.TikzScaled(slices, scale,
                                            false, "rare-harm demo",
                                            true /* split */);

            tikz.Should().Contain("dashed,very thin");      // midline present
            tikz.Should().Contain(@"99\%");                // divider at x = 0.5
        }

        [TestMethod]
        public void SplitPanel_VeryRareHarmScenario_RendersWithDivider()
        {
            var slices = new List<CostBreakdownReport.Slice>
            {
                // LEFT – precaution only  (99.9999 % of cases)
                S(0.999999, 1e-6, 0),

                // RIGHT – all cost types present (0.0001 % of cases)
                // w       Opp    Harm  File  Ans  Barg  Trial   Σcost
                S(0.000001, 1e-6, 0.5, 0.2, 0.1, 0.05, 0.15)   // total ≈1.0
            };

            // right-axis top can remain 6; left axis will now be on the order of 1E-06,
            // and should display in scientific notation
            var scale = CostBreakdownReport.ComputeScaling(slices, rightAxisTop: 6.0);

            string tikz = CostBreakdownReport.TikzScaled(
                              slices, scale,
                              false, "very-rare-harm demo",
                              true /* split */);

            // sanity checks
            tikz.Should().Contain("dashed,very thin");   // divider present
            tikz.Should().Contain(@"0.0001\%");

        }

    }
}
