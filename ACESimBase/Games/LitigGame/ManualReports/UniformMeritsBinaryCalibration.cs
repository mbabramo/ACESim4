using ACESim;
using ACESim.Util.DiscreteProbabilities;
using ACESimBase.Util.DiscreteProbabilities;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ACESimBase.Games.LitigGame.ManualReports
{
    /// <summary>
    /// The established KL calibration procedure, with uniform continuous merits as
    /// the target. This never initializes a strategy or solves an equilibrium.
    /// </summary>
    public static class UniformMeritsBinaryCalibration
    {
        public const double MinimumSigma = 0.01, MaximumSigma = 1.50, CoarseStep = 0.001;
        public const int RefinementIterations = 100;
        public sealed record Evaluation(string Stage, double Sigma, double? Divergence, bool Finite);
        public sealed record Fit(double Sigma, double Divergence, Evaluation[] Evaluations);
        public sealed record Result(string Schema, string Target, int Signals, int CourtOutcomes, int QuadratureOrder,
            double TargetPartySigma, double TargetCourtSigma, double TruthPrior,
            double CoarseMinimum, double CoarseMaximum, double CoarseStepSize, int GoldenSectionIterations,
            Fit PartyFit, Fit CourtFit, double PartyTotalVariation, double ThreeSignalTotalVariation,
            double[] TargetPartyJoint, double[] CandidatePartyJoint, double[] TargetThreeSignalJoint, double[] CandidateThreeSignalJoint);

        public static Result Run()
        {
            var option = new LitigGameCorrelatedSignalsArticleLauncher(
                LitigGameCorrelatedSignalsArticleLauncher.ProductionRunPlan.AgreementToBargain)
                .GetOptionsSets().Cast<LitigGameOptions>().Single(o =>
                    o.Name == "Agreement-Enabled__Specification-Baseline__Cost-1__Fee-American");
            var definition = new LitigGameDefinition(); definition.Setup(option);
            var target = (LitigGameUniformQualityDisputeGenerator)option.LitigGameDisputeGenerator;
            double[] p = target.BayesianCalculations_GetPLiabilitySignalProbabilities(null);
            var parties = new List<double>(); var three = new List<double>();
            for (byte ps = 1; ps <= option.NumLiabilitySignals; ps++)
            {
                double[] d = target.BayesianCalculations_GetDLiabilitySignalProbabilities(ps);
                for (byte ds = 1; ds <= option.NumLiabilitySignals; ds++)
                {
                    double joint = p[ps - 1] * d[ds - 1];
                    parties.Add(joint);
                    foreach (double c in target.BayesianCalculations_GetCLiabilitySignalProbabilities(ps, ds))
                        three.Add(joint * c);
                }
            }
            var targetParty = parties.ToArray(); var targetThree = three.ToArray();
            var partyFit = Minimize(sigma => KL(targetParty, BinaryJoint(option.NumLiabilitySignals, sigma, sigma, false)));
            var courtFit = Minimize(sigma => KL(targetThree, BinaryJoint(option.NumLiabilitySignals, partyFit.Sigma, sigma, true)));
            var candidateParty = BinaryJoint(option.NumLiabilitySignals, partyFit.Sigma, courtFit.Sigma, false);
            var candidateThree = BinaryJoint(option.NumLiabilitySignals, partyFit.Sigma, courtFit.Sigma, true);
            return new("1", "Uniform(0,1); T|Q=Bernoulli(Q); unrounded signal kernels; p,d,c index order",
                option.NumLiabilitySignals, option.NumCourtLiabilitySignals, target.QuadratureOrder,
                option.PLiabilityNoiseStdev, option.CourtLiabilityNoiseStdev, 0.5,
                MinimumSigma, MaximumSigma, CoarseStep, RefinementIterations,
                partyFit, courtFit, TotalVariation(targetParty, candidateParty), TotalVariation(targetThree, candidateThree),
                targetParty, candidateParty, targetThree, candidateThree);
        }

        public static double[] BinaryJoint(int signals, double partySigma, double courtSigma, bool includeCourt)
        {
            var model = SignalChannelBuilder.BuildFromNoise(new[] { 0.5, 0.5 },
                signals, partySigma, signals, partySigma, 2, courtSigma,
                sourcePointsIncludeExtremes: true,
                signalShapeParameters: new SignalShapeParameters { Mode = SignalShapeMode.Identity });
            int courts = includeCourt ? 2 : 1;
            var joint = new double[signals * signals * courts];
            for (int h = 0; h < 2; h++)
            for (int p = 0; p < signals; p++)
            for (int d = 0; d < signals; d++)
            for (int c = 0; c < courts; c++)
                joint[(p * signals + d) * courts + c] += model.PriorHiddenValues[h] *
                    model.PlaintiffSignalProbabilitiesGivenHidden[h][p] * model.DefendantSignalProbabilitiesGivenHidden[h][d] *
                    (includeCourt ? model.CourtSignalProbabilitiesGivenHidden[h][c] : 1);
            return joint;
        }

        public static Fit Minimize(Func<double, double> objective)
        {
            var evaluations = new List<Evaluation>();
            double Evaluate(double sigma, string stage)
            {
                double value = objective(sigma);
                evaluations.Add(new(stage, sigma, double.IsFinite(value) ? value : null, double.IsFinite(value)));
                return value;
            }
            int count = (int)Math.Floor((MaximumSigma - MinimumSigma) / CoarseStep) + 1;
            int best = 0; double bestValue = double.PositiveInfinity;
            for (int i = 0; i < count; i++)
            {
                double value = Evaluate(MinimumSigma + i * CoarseStep, "coarse");
                if (value < bestValue) { best = i; bestValue = value; }
            }
            if (!double.IsFinite(bestValue)) throw new InvalidOperationException("No finite calibration objective.");
            double left = Math.Max(MinimumSigma, MinimumSigma + (best - 1) * CoarseStep);
            double right = Math.Min(MaximumSigma, MinimumSigma + (best + 1) * CoarseStep);
            const double ratio = 0.6180339887498948482;
            double c = right - ratio * (right - left), d = left + ratio * (right - left);
            double fc = Evaluate(c, "refine"), fd = Evaluate(d, "refine");
            for (int iteration = 0; iteration < RefinementIterations; iteration++)
            {
                if (fc < fd)
                {
                    right = d; d = c; fd = fc;
                    c = right - ratio * (right - left); fc = Evaluate(c, "refine");
                }
                else
                {
                    left = c; c = d; fc = fd;
                    d = left + ratio * (right - left); fd = Evaluate(d, "refine");
                }
            }
            double sigma = (left + right) / 2;
            return new(sigma, Evaluate(sigma, "selected"), evaluations.ToArray());
        }

        public static double KL(double[] target, double[] candidate)
        {
            if (target.Length != candidate.Length || target.Length == 0)
                throw new ArgumentException("Calibration distribution dimensions differ.");
            double value = 0;
            for (int i = 0; i < target.Length; i++)
            {
                if (target[i] <= 0) continue;
                if (candidate[i] <= 0) return double.PositiveInfinity;
                value += target[i] * Math.Log(target[i] / candidate[i]);
            }
            return value;
        }
        private static double TotalVariation(double[] a, double[] b) => 0.5 * a.Zip(b, (x,y) => Math.Abs(x-y)).Sum();
    }
}
