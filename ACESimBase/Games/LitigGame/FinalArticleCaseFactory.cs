using ACESim.Util.DiscreteProbabilities;
using ACESimBase.Util.Mathematics;
using System;
using System.Globalization;
using System.IO;
using System.Linq;

namespace ACESim
{
    /// <summary>Fully specified cases, populated only from a frozen execution manifest.</summary>
    public sealed record FinalArticleCase
    {
        public required string Id { get; init; }
        public required string Family { get; init; }
        public required string Variant { get; init; }
        public required string FeeRule { get; init; }
        public required double AlphaP { get; init; }
        public required double AlphaD { get; init; }
        public required double CostMultiplier { get; init; }
        public required byte Signals { get; init; }
        public required double[] Offers { get; init; }
        public required string Distribution { get; init; }
        public required double PartySigma { get; init; }
        public required double CourtSigma { get; init; }
        public required double EntryCost { get; init; }
        public required double TrialCost { get; init; }
        public string OriginalOptionName { get; init; }
        public string CalibrationSha256 { get; init; }
        public bool IsExternalImport => OriginalOptionName != null;
    }

    public static class FinalArticleCaseFactory
    {
        public const string NamePrefix = "Final-Agreement__";
        private static string F(double x) => x.ToString("G17", CultureInfo.InvariantCulture);

        public static LitigGameOptions Create(FinalArticleCase spec)
        {
            if (spec == null || string.IsNullOrWhiteSpace(spec.Id) ||
                spec.Id.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-' && c != '_'))
                throw new ArgumentException("A safe, nonempty case identity is required.");
            if (spec.AlphaP is not (0 or 2) || spec.AlphaD is not (0 or 2) ||
                !double.IsFinite(spec.CostMultiplier) || spec.CostMultiplier <= 0 ||
                !double.IsFinite(spec.PartySigma) || spec.PartySigma <= 0 ||
                !double.IsFinite(spec.CourtSigma) || spec.CourtSigma <= 0 ||
                !double.IsFinite(spec.EntryCost) || spec.EntryCost <= 0 ||
                !double.IsFinite(spec.TrialCost) || spec.TrialCost <= 0 ||
                spec.Signals < 2 || spec.Offers == null || spec.Offers.Length < 2 || spec.Offers.Length > byte.MaxValue)
                throw new ArgumentException("Incomplete or unsupported final article parameters.");
            if (Math.Abs(spec.EntryCost + spec.TrialCost - 0.3) > 1e-15)
                throw new ArgumentException("The declared timing variants retain total cost 0.3 per party before the multiplier.");
            if (spec.Offers[0] != 0.05 || spec.Offers[^1] != 0.95)
                throw new ArgumentException("Article grids retain fixed support 0.05 through 0.95.");
            string fee = spec.FeeRule switch { "american" => "American", "trial-only" or "complete" => "British", _ => throw new ArgumentException("Unknown fee rule.") };
            bool symmetricRA = spec.AlphaP == 2 && spec.AlphaD == 2;
            string originalName = "Agreement-Enabled__Specification-" + (symmetricRA ? "ModerateRiskAversion" : "Baseline") +
                "__Cost-" + spec.CostMultiplier.ToString("G", CultureInfo.InvariantCulture) + "__Fee-" + fee +
                (spec.FeeRule == "complete" ? "__ExitFees-AllUnilateralExits" : "");
            var options = new LitigGameCorrelatedSignalsArticleLauncher(
                LitigGameCorrelatedSignalsArticleLauncher.ProductionRunPlan.AgreementToBargain)
                .CreateAgreementCase(new LitigGameCorrelatedSignalsArticleLauncher.CoreCase(symmetricRA,
                    spec.FeeRule switch { "american" => LitigGameCorrelatedSignalsArticleLauncher.CoreFeeRule.American,
                        "trial-only" => LitigGameCorrelatedSignalsArticleLauncher.CoreFeeRule.Trial,
                        "complete" => LitigGameCorrelatedSignalsArticleLauncher.CoreFeeRule.Complete,
                        _ => throw new ArgumentException("Unknown fee rule.") }), spec.CostMultiplier);
            if (spec.IsExternalImport)
            {
                if (spec.OriginalOptionName != originalName || spec.AlphaP != spec.AlphaD ||
                    spec.Distribution != "uniform" || spec.Signals != options.NumLiabilitySignals ||
                    !spec.Offers.SequenceEqual(options.GetOfferValues()) ||
                    spec.PartySigma != options.PLiabilityNoiseStdev || spec.CourtSigma != options.CourtLiabilityNoiseStdev ||
                    spec.EntryCost != options.PFilingCost || spec.TrialCost != options.PTrialCosts)
                    throw new InvalidDataException("An original study reservation cannot be repurposed for a different strategic game.");
                // Preserve all original metadata, actions, numerical settings and names.
                return options;
            }
            if (spec.CostMultiplier != 1 || spec.FeeRule == "trial-only")
                throw new ArgumentException("Additional variants use ordinary cost and the declared American/complete comparison only.");
            options.Name = NamePrefix + spec.Id;
            options.NumLiabilitySignals = spec.Signals;
            options.NumOffers = (byte)spec.Offers.Length;
            options.ExplicitOfferValues = spec.Offers;
            options.ValidateOfferValues();
            options.PLiabilityNoiseStdev = options.DLiabilityNoiseStdev = spec.PartySigma;
            options.CourtLiabilityNoiseStdev = spec.CourtSigma;
            options.PFilingCost = options.DAnswerCost = spec.EntryCost;
            options.PTrialCosts = options.DTrialCosts = spec.TrialCost;
            if (spec.AlphaP != spec.AlphaD)
            {
                if (spec.AlphaP == 2) options.PUtilityCalculator = new CARARiskAverseUtilityCalculator { InitialWealth = options.PInitialWealth, Alpha = 2, LinearTransformation = true };
                if (spec.AlphaD == 2) options.DUtilityCalculator = new CARARiskAverseUtilityCalculator { InitialWealth = options.DInitialWealth, Alpha = 2, LinearTransformation = true };
            }
            if (spec.Distribution == "direct-binary")
            {
                if (spec.CalibrationSha256 == null || spec.CalibrationSha256.Length != 64 || !spec.CalibrationSha256.All(Uri.IsHexDigit))
                    throw new InvalidDataException("Direct binary requires an identified uniform-merits calibration artifact.");
                options.LitigGameDisputeGenerator = new LitigGameExogenousDirectSignalDisputeGenerator { ExogenousProbabilityTrulyLiable = 0.5 };
            }
            else
            {
                var continuous = (LitigGameUniformQualityDisputeGenerator)options.LitigGameDisputeGenerator;
                continuous.QualityDistribution = spec.Distribution switch
                {
                    "uniform" => ContinuousQualityDistribution.Uniform,
                    "beta-2-2" => ContinuousQualityDistribution.BetaTwoTwo,
                    "beta-half-half" => ContinuousQualityDistribution.BetaHalfHalf,
                    _ => throw new ArgumentException("Unknown merits distribution.")
                };
            }
            var v = options.VariableSettings;
            v["Final Case ID"] = spec.Id; v["Variation Family"] = spec.Family; v["Variation"] = spec.Variant;
            v["Specification"] = spec.Family + " / " + spec.Variant;
            v["Number of Signals"] = spec.Signals.ToString(CultureInfo.InvariantCulture);
            v["Number of Offers"] = options.NumOffers.ToString(CultureInfo.InvariantCulture);
            v["Explicit Offer Values"] = string.Join(";", spec.Offers.Select(F));
            v["Party Signal Sigma"] = F(spec.PartySigma); v["Court Signal Sigma"] = F(spec.CourtSigma);
            v["CARA Alpha P"] = F(spec.AlphaP); v["CARA Alpha D"] = F(spec.AlphaD);
            v["CARA Alpha"] = spec.AlphaP == spec.AlphaD ? F(spec.AlphaP) : "Asymmetric";
            v["Risk Aversion"] = spec.AlphaP == spec.AlphaD ? symmetricRA ? "Moderately Risk Averse" : "Risk Neutral" : spec.AlphaP == 2 ? "Plaintiff Only Risk Averse" : "Defendant Only Risk Averse";
            v["Proportion of Costs at Beginning"] = F(spec.EntryCost / (spec.EntryCost + spec.TrialCost));
            v["Quality Distribution"] = spec.Distribution;
            v["Quality-Truth Link"] = spec.Distribution == "direct-binary" ? "Signals generated directly by T" : "T | Q ~ Bernoulli(Q)";
            v["Signal Structure"] = spec.Distribution == "direct-binary" ? LitigGameCorrelatedSignalsArticleLauncher.BinaryTruthLabel : LitigGameCorrelatedSignalsArticleLauncher.ContinuousMeritsLabel;
            v["Integration Method"] = spec.Distribution == "direct-binary" ? "Finite sum" : spec.Distribution == "beta-half-half" ? "Gauss-Legendre after arcsine transform" : "Gauss-Legendre";
            v["Quadrature Order"] = spec.Distribution == "direct-binary" ? "N/A" : "64";
            if (spec.CalibrationSha256 != null) v["Calibration SHA256"] = spec.CalibrationSha256;
            return options;
        }
    }
}
