using System;
using System.Globalization;
using System.Text;
using ACESimBase.Games.LitigGame.PrecautionModel;
using ACESimBase.Util.Statistical;

namespace ACESimBase.Games.LitigGame.PrecautionModel
{
    public enum CurveSmoothingMode
    {
        CubicInterpolation, // passes through the 8 coarse anchors (PCHIP)
        FineModel           // uses a fine model grid with invariant cost-per-unit-precaution
    }

    public static class ProbitLiabilityTikzGenerator
    {
        public static string BuildStandaloneBaseline(
            int decimals = 6,
            bool includeCurves = true,
            bool includeDots = true,
            int samplesPerCurve = 100,
            CurveSmoothingMode mode = CurveSmoothingMode.CubicInterpolation,
            int finePrecautionLevels = 128,
            CourtDecisionRule decisionRule = CourtDecisionRule.ProbitThreshold,
            double probitScale = 0.25,
            double sigmaCourt = 0.2)
        {
            if (samplesPerCurve < 2) samplesPerCurve = 2;
            if (finePrecautionLevels < 2) finePrecautionLevels = 2;

            // Baseline coarse model (K=8) for anchors/dots.
            var impactCoarse = new PrecautionImpactModel(
                precautionPowerLevels: 8,
                precautionLevels: 8,
                pAccidentNoPrecaution: 1.0e-4,
                pMinLow: 8.0e-5,
                pMinHigh: 2.0e-5,
                alphaLow: 2.0,
                alphaHigh: 1.5,
                marginalPrecautionCost: 1.0e-5,
                harmCost: 1.0,
                liabilityThreshold: 1.0,
                pAccidentWrongfulAttribution: 1.0e-5
            );

            // Plot-only signal model with 8 court signals (for 8 curves); configurable sigmaCourt.
            var signalForPlot = new PrecautionSignalModel(
                numPrecautionPowerLevels: 8,
                numPlaintiffSignals: 8,
                numDefendantSignals: 8,
                numCourtSignals: 8,
                sigmaPlaintiff: 0.2,
                sigmaDefendant: 0.2,
                sigmaCourt: sigmaCourt,
                includeExtremes: true
            );

            // Optional fine model for probit + FineModel smoothing.
            PrecautionImpactModel impactFine = null;
            if (decisionRule == CourtDecisionRule.ProbitThreshold && mode == CurveSmoothingMode.FineModel)
            {
                double coarseStepCost = 1.0e-5;
                double fineStepCost = coarseStepCost * (8.0 / finePrecautionLevels); // keep cost per unit τ invariant

                impactFine = new PrecautionImpactModel(
                    precautionPowerLevels: 8,
                    precautionLevels: finePrecautionLevels,
                    pAccidentNoPrecaution: 1.0e-4,
                    pMinLow: 8.0e-5,
                    pMinHigh: 2.0e-5,
                    alphaLow: 2.0,
                    alphaHigh: 1.5,
                    marginalPrecautionCost: fineStepCost,
                    harmCost: 1.0,
                    liabilityThreshold: 1.0,
                    pAccidentWrongfulAttribution: 1.0e-5
                );
            }

            return BuildStandalone(
                impactModelCoarse: impactCoarse,
                impactModelFine: impactFine,
                signalModelForPlot: signalForPlot,
                decisionRule: decisionRule,
                probitScale: probitScale,
                decimals: decimals,
                includeCurves: includeCurves,
                includeDots: includeDots,
                samplesPerCurve: samplesPerCurve,
                mode: mode
            );
        }

        public static string BuildStandalone(
            PrecautionImpactModel impactModelCoarse,
            PrecautionImpactModel impactModelFine,
            PrecautionSignalModel signalModelForPlot,
            CourtDecisionRule decisionRule = CourtDecisionRule.ProbitThreshold,
            double probitScale = 0.25,
            int decimals = 6,
            bool includeCurves = true,
            bool includeDots = true,
            int samplesPerCurve = 100,
            CurveSmoothingMode mode = CurveSmoothingMode.CubicInterpolation)
        {
            if (impactModelCoarse == null) throw new ArgumentNullException(nameof(impactModelCoarse));
            if (signalModelForPlot == null) throw new ArgumentNullException(nameof(signalModelForPlot));
            if (!includeCurves && !includeDots)
                throw new ArgumentException("At least one of includeCurves and includeDots must be true.");
            if (samplesPerCurve < 2) samplesPerCurve = 2;

            if (decisionRule == CourtDecisionRule.DeterministicThreshold)
                impactModelFine = null; // steps ignore fine grid

            string addplots = BuildAddplots(
                impactModelCoarse,
                impactModelFine,
                signalModelForPlot,
                decisionRule,
                probitScale,
                decimals,
                includeCurves,
                includeDots,
                samplesPerCurve,
                mode);

            var sb = new StringBuilder();
            sb.AppendLine("\\documentclass[tikz,border=3mm]{standalone}");
            sb.AppendLine("\\usepackage{pgfplots}");
            sb.AppendLine("\\pgfplotsset{compat=1.9}"); // match precaution diagram compat
            sb.AppendLine();
            sb.AppendLine("\\begin{document}");
            sb.AppendLine("\\begin{tikzpicture}");
            sb.AppendLine("  \\begin{axis}[");
            sb.AppendLine("    width=15cm, height=9cm,");
            sb.AppendLine("    xmin=0, xmax=1,");
            sb.AppendLine("    ymin=0, ymax=1,");
            sb.AppendLine("    xlabel={Relative Precaution Level},");
            sb.AppendLine("    ylabel={Liability Probability},");
            sb.AppendLine("    xmajorgrids, ymajorgrids,");
            sb.AppendLine("    major grid style={gray!55, dotted, line width=0.25pt},");
            sb.AppendLine("    xtick={0,0.125,0.25,0.375,0.5,0.625,0.75,0.875,1},");
            sb.AppendLine("    xticklabels={$0$,$\\frac{1}{8}$,$\\frac{1}{4}$,$\\frac{3}{8}$,$\\frac{1}{2}$,$\\frac{5}{8}$,$\\frac{3}{4}$,$\\frac{7}{8}$,$1$}");
            sb.AppendLine("  ]");
            sb.Append(addplots);
            sb.AppendLine("  \\end{axis}");
            sb.AppendLine("\\end{tikzpicture}");
            sb.AppendLine("\\end{document}");
            return sb.ToString();
        }

        private static string BuildAddplots(
            PrecautionImpactModel impactModelCoarse,
            PrecautionImpactModel impactModelFine,
            PrecautionSignalModel signalModelForPlot,
            CourtDecisionRule decisionRule,
            double probitScale,
            int decimals,
            bool includeCurves,
            bool includeDots,
            int samplesPerCurve,
            CurveSmoothingMode mode)
        {
            var courtCoarse = new PrecautionCourtDecisionModel(
                impactModelCoarse,
                signalModelForPlot,
                decisionRule,
                probitScale);

            PrecautionCourtDecisionModel courtFine = null;
            int Kf = 0;
            if (decisionRule == CourtDecisionRule.ProbitThreshold && impactModelFine != null)
            {
                courtFine = new PrecautionCourtDecisionModel(
                    impactModelFine,
                    signalModelForPlot,
                    decisionRule,
                    probitScale);
                Kf = impactModelFine.PrecautionLevels;
            }

            int C = signalModelForPlot.NumCSignals;       // 8 curves
            int Kc = impactModelCoarse.PrecautionLevels;  // 8 anchors (0..7/8)

            var fmt = CultureInfo.InvariantCulture;
            string fx(double v) => v.ToString("F" + decimals, fmt);

            // Anchors (and dots) from coarse model
            double[][] yCoarse = new double[C][];
            for (int c = 0; c < C; c++)
            {
                yCoarse[c] = new double[Kc];
                for (int k = 0; k < Kc; k++)
                    yCoarse[c][k] = LiabilityProbabilityAt(courtCoarse, impactModelCoarse, decisionRule, probitScale, c, k);
            }

            // Fine grid values (probit + FineModel)
            double[][] yFine = null;
            if (decisionRule == CourtDecisionRule.ProbitThreshold && courtFine != null)
            {
                yFine = new double[C][];
                for (int c = 0; c < C; c++)
                {
                    yFine[c] = new double[Kf];
                    for (int k = 0; k < Kf; k++)
                        yFine[c][k] = LiabilityProbabilityAt(courtFine, impactModelFine, decisionRule, probitScale, c, k);
                }
            }

            var sb = new StringBuilder();

            for (int c = 0; c < C; c++)
            {
                if (includeCurves)
                {
                    // Style from precaution diagram: blue!60!black, thick, solid
                    sb.AppendLine("\\addplot[blue!60!black, thick] coordinates {");

                    if (decisionRule == CourtDecisionRule.DeterministicThreshold)
                    {
                        // Step function at 0/1, flat tail to 1
                        for (int j = 0; j < samplesPerCurve; j++)
                        {
                            double x = j == samplesPerCurve - 1 ? 1.0 : (double)j / (samplesPerCurve - 1);
                            double y;
                            double xBreak = (Kc - 1) / (double)Kc;
                            if (x >= xBreak)
                            {
                                y = yCoarse[c][Kc - 1];
                            }
                            else
                            {
                                int i0 = (int)Math.Floor(x * Kc);
                                if (i0 >= Kc) i0 = Kc - 1;
                                y = yCoarse[c][i0];
                            }
                            sb.AppendLine("  (" + fx(x) + "," + fx(y) + ")");
                        }
                    }
                    else
                    {
                        if (yFine == null)
                        {
                            // Probit + cubic interpolation (PCHIP) through coarse anchors (passes through dots)
                            for (int j = 0; j < samplesPerCurve; j++)
                            {
                                double x = j == samplesPerCurve - 1 ? 1.0 : (double)j / (samplesPerCurve - 1);
                                double y = EvaluatePchipAtCoarse(x, yCoarse[c], Kc);
                                sb.AppendLine("  (" + fx(x) + "," + fx(y) + ")");
                            }
                        }
                        else
                        {
                            // Probit + fine model (linear over fine grid, flat tail)
                            for (int j = 0; j < samplesPerCurve; j++)
                            {
                                double x = j == samplesPerCurve - 1 ? 1.0 : (double)j / (samplesPerCurve - 1);
                                double y;
                                double xBreak = (Kf - 1) / (double)Kf;
                                if (x >= xBreak)
                                {
                                    y = yFine[c][Kf - 1];
                                }
                                else
                                {
                                    double pos = x * Kf;
                                    int i0 = (int)Math.Floor(pos);
                                    double frac = pos - i0;
                                    double y0 = yFine[c][i0];
                                    double y1 = yFine[c][i0 + 1];
                                    y = y0 + frac * (y1 - y0);
                                }
                                sb.AppendLine("  (" + fx(x) + "," + fx(y) + ")");
                            }
                        }
                    }

                    sb.AppendLine("};");
                }

                if (includeDots)
                {
                    // Black filled markers, size ~1.6pt (from reference diagram)
                    sb.AppendLine("\\addplot[only marks, mark=*, mark size=1.6pt, draw=black, fill=black] coordinates {");
                    for (int k = 0; k < Kc; k++)
                    {
                        double xRel = k / (double)Kc;
                        sb.AppendLine("  (" + fx(xRel) + "," + fx(yCoarse[c][k]) + ")");
                    }
                    sb.AppendLine("};");
                }
            }

            return sb.ToString();
        }

        // Shape-preserving monotone cubic (PCHIP) through coarse points at x_i = i/Kc
        private static double EvaluatePchipAtCoarse(double x, double[] y, int Kc)
        {
            if (x <= 0) return y[0];
            if (x >= 1) return y[Kc - 1];

            double pos = x * Kc;
            int i = (int)Math.Floor(pos);
            if (i >= Kc - 1) i = Kc - 2;
            double t = pos - i; // [0,1)
            double h = 1.0 / Kc;

            int n = Kc;
            double[] d = new double[n - 1];
            for (int j = 0; j < n - 1; j++)
                d[j] = (y[j + 1] - y[j]) / h;

            double[] m = new double[n];
            m[0] = d[0];
            m[n - 1] = d[n - 2];

            for (int j = 1; j <= n - 2; j++)
            {
                if (d[j - 1] == 0.0 || d[j] == 0.0 || (d[j - 1] > 0 && d[j] < 0) || (d[j - 1] < 0 && d[j] > 0))
                {
                    m[j] = 0.0;
                }
                else
                {
                    // Fritsch–Carlson limiter on a uniform grid
                    double cand = 3.0 / (2.0 / d[j - 1] + 1.0 / d[j]); // harmonic mean with equal weights
                    double sign = Math.Sign(cand);
                    double minAbs = Math.Min(Math.Abs(d[j - 1]), Math.Abs(d[j]));
                    double limit = 3.0 * minAbs;
                    m[j] = sign * Math.Min(Math.Abs(cand), limit);
                }
            }

            // Hermite cubic on [i, i+1]
            double y0 = y[i];
            double y1 = y[i + 1];
            double m0 = m[i];
            double m1 = m[i + 1];

            double t2 = t * t;
            double t3 = t2 * t;
            double h00 = (2 * t3 - 3 * t2 + 1);
            double h10 = (t3 - 2 * t2 + t) * h;
            double h01 = (-2 * t3 + 3 * t2);
            double h11 = (t3 - t2) * h;

            return h00 * y0 + h10 * m0 + h01 * y1 + h11 * m1;
        }

        private static double LiabilityProbabilityAt(
            PrecautionCourtDecisionModel courtModel,
            PrecautionImpactModel impactModel,
            CourtDecisionRule decisionRule,
            double probitScale,
            int courtSignal,
            int precautionLevel)
        {
            double ratio = courtModel.GetBenefitCostRatio(courtSignal, precautionLevel);

            if (decisionRule == CourtDecisionRule.DeterministicThreshold)
                return ratio > impactModel.LiabilityThreshold ? 1.0 : 0.0;

            double z = (ratio - impactModel.LiabilityThreshold) / probitScale;
            return NormalDistributionCalculation.CumulativeNormalDistribution(z);
        }
    }
}
