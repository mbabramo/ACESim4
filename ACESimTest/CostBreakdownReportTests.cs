using ACESimBase.Games.LitigGame.ManualReports;
using ACESimBase.Util.Tikz;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ACESimTest
{
    [TestClass]
    public class CostBreakdownReportTests
    {
        // handy alias
        private static CostBreakdownReport.Slice S(double w, double opp, double harm) =>
            new(w, opp, harm / 2.0, harm / 2.0, 0, 0, 0, 0);

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
            => new(w, opp, harm / 2.0, harm / 2.0, file, ans, barg, tri);

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
        public void ThreeSplitPanels_WithFourLeftAndFourRightSlices_Render()
        {
            // shorthand S(width, opportunity, harm, filing, answer, bargaining, trial) already in test framework

            var p1 = new List<CostBreakdownReport.Slice>
            {
                S(0.40, 0.02, 0),  S(0.30, 0.015, 0), S(0.20, 0.010, 0), S(0.09, 0.005, 0),
                S(0.0025, 0.002, 0.08, 0.04, 0.03, 0.02, 0.03),
                S(0.0025, 0.002, 0.07, 0.035,0.03, 0.02, 0.03),
                S(0.0025, 0.002, 0.09, 0.045,0.04, 0.03, 0.04),
                S(0.0025, 0.002, 0.10, 0.050,0.04, 0.03, 0.04)
            };

            var p2 = new List<CostBreakdownReport.Slice>
            {
                S(0.492, 0.015, 0), S(0.25, 0.010, 0), S(0.16, 0.008, 0), S(0.093, 0.004, 0),
                S(0.00125, 0.002, 0.05, 0.025,0.02,0.015,0.02),
                S(0.00125, 0.002, 0.06, 0.030,0.02,0.015,0.02),
                S(0.00125, 0.002, 0.04, 0.020,0.02,0.015,0.02),
                S(0.00125, 0.002, 0.03, 0.015,0.02,0.015,0.02)
            };

            var p3 = new List<CostBreakdownReport.Slice>
            {
                S(0.45, 0.025, 0),  S(0.30, 0.018, 0), S(0.15, 0.010, 0), S(0.094, 0.005, 0),
                S(0.0015, 0.003, 0.12, 0.06, 0.05, 0.04, 0.05),
                S(0.0015, 0.003, 0.11, 0.055,0.05, 0.04, 0.05),
                S(0.0015, 0.003, 0.13, 0.065,0.05, 0.04, 0.05),
                S(0.0015, 0.003, 0.14, 0.070,0.05, 0.04, 0.05)
            };

            var allSlices = new List<List<CostBreakdownReport.Slice>>()
            {
                p1, p2, p3
            };

            var scalingInfo = CostBreakdownReport.ComputeScaling(allSlices, 0.8);

            var fig = new StringBuilder();

            fig.Append(CostBreakdownReport.TikzScaled(
                p1, scalingInfo[0], pres: false, "Panel 1", true,
                standalone: false, includeLegend: false,
                xOffset: 0, yOffset: 0));

            fig.Append(CostBreakdownReport.TikzScaled(
                p2, scalingInfo[1], pres: false, "Panel 2", true,
                standalone: false, includeLegend: false,
                targetWidth: 0.60 * 15, targetHeight: 0.60 * 16,
                xOffset: 17, yOffset: 0));

            fig.Append(CostBreakdownReport.TikzScaled(
                p3, scalingInfo[2], pres: true, "Panel 3", true,
                standalone: false, includeLegend: false,
                targetWidth: 0.40 * 26.6666, targetHeight: 0.40 * 15,
                xOffset: 0, yOffset: 18));

            string latexDoc = TikzHelper.GetStandaloneDocument(
                fig.ToString(),
                additionalHeaderInfo: "\\usepackage[sfdefault]{ClearSans}");

            Assert.IsTrue(latexDoc.Contains("dashed,very thin"));
        }

        [TestMethod]
        public void SharedAreaPerUnitAndPeakProportionAreHonoured()
        {
            const double peak = 0.90;
            const double epsilon = 1e-10;

            // ----- diagram 1 -------------------------------------------------
            var diagramOne = new List<CostBreakdownReport.Slice>
            {
                // left-panel slice
                new(0.60, 10, 0, 0, 0, 0, 0, 0),
                // right-panel slices
                new(0.30,  5, 1.5, 1.5, 2, 0, 1, 0),
                new(0.10,  2, 0, 0, 1, 1, 0.5, 0)
            };

            // ----- diagram 2 -------------------------------------------------
            var diagramTwo = new List<CostBreakdownReport.Slice>
            {
                new(0.40, 20, 0, 0, 0, 0, 0, 0),
                new(0.60,  8, 3, 3, 1, 0, 0, 2)
            };

            var diagrams = new List<List<CostBreakdownReport.Slice>>
            {
                diagramOne,
                diagramTwo
            };

            // act
            var infos = CostBreakdownReport.ComputeScaling(diagrams, peak);

            // assert: area-per-unit identical
            infos.Select(i => i.AreaPerUnit).Distinct().Count()
                 .Should().Be(1, "all diagrams must share a common area-per-unit-cost");

            double sharedAreaPerUnit = infos.First().AreaPerUnit;

            for (int d = 0; d < diagrams.Count; d++)
            {
                var slices = diagrams[d];
                var info = infos[d];

                bool isSplit = slices.Any(s => s.IsLeft) &&
                               slices.Any(s => !s.IsLeft);

                // tallest stacks
                double tallestLeft = 0, tallestRight = 0;
                foreach (var s in slices)
                {
                    double total = s.Total;
                    bool left = s.IsLeft;
                    if (left)  tallestLeft  = Math.Max(tallestLeft, total);
                    else       tallestRight = Math.Max(tallestRight, total);
                }

                if (isSplit)
                {
                    (tallestLeft     / info.YMaxLeft ).Should()
                        .BeLessOrEqualTo(peak + epsilon);
                    (tallestRight    / info.YMaxRight).Should()
                        .BeLessOrEqualTo(peak + epsilon);
                }
                else
                {
                    double tallest = slices.Max(s => s.Total);
                    (tallest / info.YMaxLeft).Should()
                        .BeLessOrEqualTo(peak + epsilon);
                }

                // area-per-unit matches shared value
                info.AreaPerUnit.Should().BeApproximately(sharedAreaPerUnit, epsilon);
            }
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

        [TestMethod]
        [DataRow(2,2)]
        [DataRow(4,4)]
        [DataRow(4,1)]
        [DataRow(1,4)]
        public void RepeatedCostBreakdownReport_RendersTikz(int rows, int cols)
        {
            var rng = new Random(98765);

            // ---------------------------------------------------------------------
            // Build a 4×3 grid where every panel has at least one left-only slice
            // (opportunity costs) and at least one right-side slice (includes harm)
            // ---------------------------------------------------------------------
            var sliceGrid = new List<List<List<CostBreakdownReport.Slice>>>();
            for (int r = 0; r < rows; r++)
            {
                var row = new List<List<CostBreakdownReport.Slice>>();
                for (int c = 0; c < cols; c++)
                {
                    var slices = RandomSliceSet(rng);
                    // Sanity: each panel must split into left & right
                    slices.Any(IsLeftSlice).Should().BeTrue();
                    slices.Any(s => !IsLeftSlice(s)).Should().BeTrue();
                    row.Add(slices);
                }
                sliceGrid.Add(row);
            }

            var majorX = Enumerable.Range(1, cols).Select(i => $"Col {i}").ToList();
            var majorY = Enumerable.Range(1, rows).Select(i => $"Row {i}").ToList();

            string tikz = RepeatedCostBreakdownReport.GenerateRepeatedReport(
                             sliceGrid,
                             majorX,
                             majorY,
                             "Cases",
                             "Scenarios",
                             peakProportion: 0.8,
                             keepAxisLabels: false,
                             keepAxisTicks: true);

            tikz.Should().StartWith(@"\documentclass{standalone}");
        }

        private static List<CostBreakdownReport.Slice> RandomSliceSet(Random rng)
        {
            // ------------------------------------------------------------------
            // 1.  Probability split:  98 %–99.999 % left  vs.  0.01 %–2 % right
            // ------------------------------------------------------------------
            double pRare   = Math.Pow(10, -(2.0 + rng.NextDouble() * 3.0));   // 1 e-2 … 1 e-5
            double pCommon = 1.0 - pRare;

            // 2–4 slices per side (≥1 each)
            int leftCount  = rng.Next(1, 4);
            int rightCount = rng.Next(1, 4);

            // Dirichlet helper – returns *absolute* widths that sum to ‘total’
            static double[] Dirichlet(int k, double total, Random r)
            {
                double[] x = new double[k];
                double    s = 0;
                for (int i = 0; i < k; i++) { x[i] = -Math.Log(r.NextDouble()); s += x[i]; }
                for (int i = 0; i < k; i++) x[i] = x[i] / s * total;
                return x;
            }

            double[] wLeft  = Dirichlet(leftCount,  pCommon, rng);
            double[] wRight = Dirichlet(rightCount, pRare,   rng);

            // ------------------------------------------------------------------
            // 2.  Low-opportunity left panel
            // ------------------------------------------------------------------
            var slices    = new List<CostBreakdownReport.Slice>();
            double areaL  = 0.0;                       // Σ width·cost on the left
            static double R2(double v) => Math.Round(v, 2, MidpointRounding.AwayFromZero);

            for (int i = 0; i < leftCount; i++)
            {
                double opp = 0.005 + rng.NextDouble() * 0.045;     // 0.005 – 0.05
                areaL += wLeft[i] * opp;

                slices.Add(new CostBreakdownReport.Slice(
                    width:       wLeft[i],
                    opportunity: R2(opp),
                    trulyLiableHarm: 0, trulyNotLiableHarm: 0, filing: 0, answer: 0, bargaining: 0, trial: 0));
            }

            // ------------------------------------------------------------------
            // 3.  High-harm right panel — scaled so that 0.1 ≤ areaR/areaL ≤ 10
            // ------------------------------------------------------------------
            double ratioLR     = Math.Pow(10, rng.NextDouble() * 2.0 - 1.0);  // 0.1 … 10
            double targetAreaR = areaL * ratioLR;
            double baseCost    = targetAreaR / pRare;                         // equal across right slices

            foreach (double w in wRight)
            {
                // preserve a small opportunity cost even on the right
                double opp = 0.005 + rng.NextDouble() * 0.045;
                double remaining = baseCost - opp;

                // harm takes 60–90 % of what remains
                double harm    = remaining * (0.60 + 0.30 * rng.NextDouble());
                double balance = remaining - harm;

                // split balance over four litigation phases
                double[] phase = Dirichlet(4, balance, rng);

                slices.Add(new CostBreakdownReport.Slice(
                    width:       w,
                    opportunity: R2(opp),
                    trulyLiableHarm:        R2(harm / 2.0),
                    trulyNotLiableHarm:        R2(harm / 2.0),
                    filing:      R2(phase[0]),
                    answer:      R2(phase[1]),
                    bargaining:  R2(phase[2]),
                    trial:       R2(phase[3])));
            }

            return slices;
        }



        private static bool IsLeftSlice(CostBreakdownReport.Slice s) =>
            s.IsLeft;

    }
}
