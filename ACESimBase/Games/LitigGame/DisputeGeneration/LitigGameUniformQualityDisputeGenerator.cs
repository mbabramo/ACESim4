using ACESim.Util.DiscreteProbabilities;
using ACESimBase.GameSolvingSupport.Symmetry;
using ACESimBase.Util.ArrayManipulation;
using ACESimBase.Util.DiscreteProbabilities;
using MathNet.Numerics.Integration;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ACESim
{
    public enum ContinuousQualityDistribution
    {
        Uniform,
        BetaTwoTwo,
        BetaHalfHalf,
    }

    /// <summary>
    /// Correlated evidentiary signals generated from continuous case quality Q ~ Uniform(0, 1),
    /// with true liability T | Q=q ~ Bernoulli(q). Integration over Q is performed during setup;
    /// Q is not introduced as a game-tree state.
    /// </summary>
    [Serializable]
    public sealed class LitigGameUniformQualityDisputeGenerator : LitigGameStandardDisputeGeneratorBase
    {
        public const int DefaultQuadratureOrder = 64;

        public int QuadratureOrder { get; set; } = DefaultQuadratureOrder;
        public ContinuousQualityDistribution QualityDistribution { get; set; } =
            ContinuousQualityDistribution.Uniform;

        private double[] qualityNodes;
        private double[] qualityWeights;
        private double[] probabilityOfTrulyLiableValues;
        private ThreePartyCorrelatedSignalsBayes liabilitySignalsBayes;
        private double[] pLiabilitySignalProbabilitiesUnconditional;
        private double[] dLiabilitySignalProbabilitiesUnconditional;

        public override string GetGeneratorName() => QualityDistribution switch
        {
            ContinuousQualityDistribution.Uniform => "UniformQuality",
            ContinuousQualityDistribution.BetaTwoTwo => "BetaTwoTwoQuality",
            ContinuousQualityDistribution.BetaHalfHalf => "BetaHalfHalfQuality",
            _ => throw new NotSupportedException(),
        };

        public override string OptionsString =>
            $"QualityDistribution={GetDistributionLabel(QualityDistribution)};" +
            $"TruthGivenQuality=Bernoulli(q);GaussLegendreOrder={QuadratureOrder}";

        public override void Setup(LitigGameDefinition litigGameDefinition)
        {
            base.Setup(litigGameDefinition);
            if (litigGameDefinition == null)
                throw new ArgumentNullException(nameof(litigGameDefinition));

            LitigGameOptions options = litigGameDefinition.Options;
            if (!options.CollapseChanceDecisions)
                throw new InvalidOperationException(
                    $"{nameof(LitigGameUniformQualityDisputeGenerator)} requires collapsed chance decisions so continuous quality does not become a game-tree state.");
            if (options.NumLiabilityStrengthPoints != 2)
                throw new InvalidOperationException(
                    $"{nameof(LitigGameUniformQualityDisputeGenerator)} uses two reported liability-strength states (true and not true liability).");
            if (options.NumDamagesStrengthPoints != 1)
                throw new InvalidOperationException(
                    $"{nameof(LitigGameUniformQualityDisputeGenerator)} currently supports the article's one-point damages specification only.");
            if (options.LiabilitySignalShapeParameters.Mode != SignalShapeMode.Identity)
                throw new InvalidOperationException(
                    $"{nameof(LitigGameUniformQualityDisputeGenerator)} requires identity liability-signal shaping.");
            if (QuadratureOrder < 2)
                throw new InvalidOperationException($"{nameof(QuadratureOrder)} must be at least 2.");

            (qualityNodes, qualityWeights) = BuildQualityQuadrature(
                QualityDistribution,
                QuadratureOrder);

            DiscreteValueSignalParameters pParameters = options.PLiabilitySignalParameters;
            DiscreteValueSignalParameters dParameters = options.DLiabilitySignalParameters;
            var cParameters = new DiscreteValueSignalParameters
            {
                NumPointsInSourceUniformDistribution = QuadratureOrder,
                NumSignals = options.NumCourtLiabilitySignals,
                StdevOfNormalDistribution = options.CourtLiabilityNoiseStdev,
                SourcePointsIncludeExtremes = false,
                SignalBoundaryMode = pParameters.SignalBoundaryMode,
            };

            double[][] pGivenQuality = BuildConditionalTable(qualityNodes, pParameters);
            double[][] dGivenQuality = BuildConditionalTable(qualityNodes, dParameters);
            double[][] cGivenQuality = BuildConditionalTable(qualityNodes, cParameters);

            liabilitySignalsBayes = new ThreePartyCorrelatedSignalsBayes(
                qualityWeights,
                pGivenQuality,
                dGivenQuality,
                cGivenQuality);
            pLiabilitySignalProbabilitiesUnconditional =
                liabilitySignalsBayes.GetParty0SignalProbabilitiesUnconditional();
            dLiabilitySignalProbabilitiesUnconditional =
                liabilitySignalsBayes.GetParty1SignalProbabilitiesUnconditional();

            double probabilityTrulyLiable = 0.0;
            for (int i = 0; i < qualityNodes.Length; i++)
                probabilityTrulyLiable += qualityWeights[i] * qualityNodes[i];
            probabilityTrulyLiable = Math.Clamp(probabilityTrulyLiable, 0.0, 1.0);
            probabilityOfTrulyLiableValues = new[] { 1.0 - probabilityTrulyLiable, probabilityTrulyLiable };
        }

        private static double[][] BuildConditionalTable(
            IEnumerable<double> nodes,
            DiscreteValueSignalParameters parameters) =>
            nodes.Select(q => DiscreteValueSignal.GetProbabilitiesOfDiscreteSignals(q, parameters)).ToArray();

        private static (double[] nodes, double[] weights) BuildQualityQuadrature(
            ContinuousQualityDistribution distribution,
            int order)
        {
            GaussLegendreRule quadrature;
            double[] nodes;
            double[] weights;
            switch (distribution)
            {
                case ContinuousQualityDistribution.Uniform:
                    quadrature = new GaussLegendreRule(0.0, 1.0, order);
                    nodes = quadrature.Abscissas;
                    weights = quadrature.Weights;
                    break;

                case ContinuousQualityDistribution.BetaTwoTwo:
                    quadrature = new GaussLegendreRule(0.0, 1.0, order);
                    nodes = quadrature.Abscissas;
                    weights = quadrature.Weights
                        .Select((weight, index) =>
                            weight * 6.0 * nodes[index] * (1.0 - nodes[index]))
                        .ToArray();
                    break;

                case ContinuousQualityDistribution.BetaHalfHalf:
                    // If Q ~ Beta(1/2, 1/2), then Q = sin(theta)^2 for
                    // theta ~ Uniform(0, pi/2). Integrating in theta avoids direct
                    // evaluation of the beta density, which is singular at both endpoints.
                    quadrature = new GaussLegendreRule(0.0, Math.PI / 2.0, order);
                    nodes = quadrature.Abscissas
                        .Select(theta => Math.Pow(Math.Sin(theta), 2.0))
                        .ToArray();
                    weights = quadrature.Weights;
                    break;

                default:
                    throw new NotSupportedException(
                        $"Unsupported continuous-quality distribution '{distribution}'.");
            }

            NormalizeInPlace(weights);
            return (nodes, weights);
        }

        public static string GetDistributionLabel(ContinuousQualityDistribution distribution) =>
            distribution switch
            {
                ContinuousQualityDistribution.Uniform => "Uniform(0,1)",
                ContinuousQualityDistribution.BetaTwoTwo => "Beta(2,2)",
                ContinuousQualityDistribution.BetaHalfHalf => "Beta(0.5,0.5)",
                _ => throw new NotSupportedException(),
            };

        private static void NormalizeInPlace(double[] values)
        {
            double sum = values.Sum();
            if (!(sum > 0.0) || double.IsNaN(sum) || double.IsInfinity(sum))
                throw new InvalidOperationException("Gauss-Legendre weights could not be normalized.");
            for (int i = 0; i < values.Length; i++)
                values[i] /= sum;
        }

        private static byte? NormalizeNullableSignal(byte? signal) => signal == 0 ? null : signal;

        private void RequireBayesianSetup()
        {
            if (liabilitySignalsBayes == null)
                throw new InvalidOperationException("Bayesian setup has not been initialized.");
        }

        public override void GetActionsSetup(
            LitigGameDefinition gameDefinition,
            out byte prePrimaryChanceActions,
            out byte primaryActions,
            out byte postPrimaryChanceActions,
            out byte[] prePrimaryPlayersToInform,
            out byte[] primaryPlayersToInform,
            out byte[] postPrimaryPlayersToInform,
            out bool prePrimaryUnevenChance,
            out bool postPrimaryUnevenChance,
            out bool litigationQualityUnevenChance,
            out bool primaryActionCanTerminate,
            out bool postPrimaryChanceCanTerminate)
        {
            prePrimaryChanceActions = 0;
            primaryActions = 0;
            postPrimaryChanceActions = 2;
            prePrimaryPlayersToInform = null;
            primaryPlayersToInform = null;
            postPrimaryPlayersToInform = new[]
            {
                (byte)LitigGamePlayers.LiabilityStrengthChance,
                (byte)LitigGamePlayers.Resolution,
            };
            prePrimaryUnevenChance = false;
            postPrimaryUnevenChance = true;
            litigationQualityUnevenChance = true;
            primaryActionCanTerminate = false;
            postPrimaryChanceCanTerminate = false;
        }

        public override (string name, string abbreviation) PrePrimaryNameAndAbbreviation => ("None", "None");
        public override (string name, string abbreviation) PrimaryNameAndAbbreviation => ("None", "None");
        public override (string name, string abbreviation) PostPrimaryNameAndAbbreviation => ("Truly Liable?", "TL?");

        public override bool PotentialDisputeArises(
            LitigGameDefinition g,
            LitigGameStandardDisputeGeneratorActions a,
            LitigGameProgress p) => true;

        public override bool MarkComplete(LitigGameDefinition g, byte pre, byte primary) => throw new NotImplementedException();
        public override bool MarkComplete(LitigGameDefinition g, byte pre, byte primary, byte post) => throw new NotImplementedException();

        public override bool IsTrulyLiable(
            LitigGameDefinition g,
            LitigGameStandardDisputeGeneratorActions a,
            GameProgress p) =>
            p is LitigGameProgress litigProgress && litigProgress.IsTrulyLiable is bool value
                ? value
                : a.PostPrimaryChanceAction == 2;

        public override double[] GetPrePrimaryChanceProbabilities(LitigGameDefinition g) => throw new NotImplementedException();

        public override double[] GetPostPrimaryChanceProbabilities(
            LitigGameDefinition g,
            LitigGameStandardDisputeGeneratorActions a) => probabilityOfTrulyLiableValues;

        public override bool PostPrimaryDoesNotAffectStrategy() => true;

        public override double[] GetLiabilityStrengthProbabilities(
            LitigGameDefinition g,
            LitigGameStandardDisputeGeneratorActions a) =>
            a.PostPrimaryChanceAction == 2 ? new[] { 0.0, 1.0 } : new[] { 1.0, 0.0 };

        public override double[] GetDamagesStrengthProbabilities(
            LitigGameDefinition g,
            LitigGameStandardDisputeGeneratorActions a) => new[] { 1.0 };

        public override double GetLitigationIndependentSocialWelfare(
            LitigGameDefinition g,
            LitigGameStandardDisputeGeneratorActions a,
            LitigGameProgress p) => 0.0;

        public override double[] GetLitigationIndependentWealthEffects(
            LitigGameDefinition g,
            LitigGameStandardDisputeGeneratorActions a,
            LitigGameProgress p) => new[] { 0.0, 0.0 };

        public override string GetActionString(byte action, byte decisionByteCode) =>
            decisionByteCode == (byte)LitigGameDecisions.PostPrimaryActionChance
                ? action == 1 ? "Truly Not Liable" : "Truly Liable"
                : action.ToString();

        public override bool SupportsSymmetry() => true;
        public override (bool unrollIdentical, SymmetryMapInput symmetryMapInput) GetPrePrimaryUnrollSettings() =>
            (true, SymmetryMapInput.SameInfo);
        public override (bool unrollIdentical, SymmetryMapInput symmetryMapInput) GetPrimaryUnrollSettings() =>
            (true, SymmetryMapInput.SameInfo);
        public override (bool unrollIdentical, SymmetryMapInput symmetryMapInput) GetPostPrimaryUnrollSettings() =>
            (true, SymmetryMapInput.ReverseInfo);
        public override (bool unrollIdentical, SymmetryMapInput symmetryMapInput) GetLiabilityStrengthUnrollSettings() =>
            (true, SymmetryMapInput.ReverseInfo);
        public override (bool unrollIdentical, SymmetryMapInput symmetryMapInput) GetDamagesStrengthUnrollSettings() =>
            (true, SymmetryMapInput.ReverseInfo);

        public override double[] BayesianCalculations_GetPLiabilitySignalProbabilities(byte? dLiabilitySignal)
        {
            RequireBayesianSetup();
            return pLiabilitySignalProbabilitiesUnconditional;
        }

        public override double[] BayesianCalculations_GetDLiabilitySignalProbabilities(byte? pLiabilitySignal)
        {
            RequireBayesianSetup();
            pLiabilitySignal = NormalizeNullableSignal(pLiabilitySignal);
            return pLiabilitySignal is byte pSignal
                ? liabilitySignalsBayes.GetParty1SignalProbabilitiesGivenParty0Signal(pSignal)
                : dLiabilitySignalProbabilitiesUnconditional;
        }

        public override double[] BayesianCalculations_GetCLiabilitySignalProbabilities(
            byte pLiabilitySignal,
            byte dLiabilitySignal)
        {
            RequireBayesianSetup();
            return liabilitySignalsBayes.GetParty2SignalProbabilitiesGivenParty0AndParty1Signals(
                pLiabilitySignal,
                dLiabilitySignal);
        }

        public override double[] BayesianCalculations_GetPDamagesSignalProbabilities(byte? dDamagesSignal) => new[] { 1.0 };
        public override double[] BayesianCalculations_GetDDamagesSignalProbabilities(byte? pDamagesSignal) => new[] { 1.0 };
        public override double[] BayesianCalculations_GetCDamagesSignalProbabilities(byte pDamagesSignal, byte dDamagesSignal) => new[] { 1.0 };
        public override double[] BayesianCalculations_GetDamagesStrengthProbabilities(
            byte pDamagesSignal,
            byte dDamagesSignal,
            byte? cDamagesSignal) => new[] { 1.0 };

        public double GetPosteriorMeanQuality(
            byte pLiabilitySignal,
            byte dLiabilitySignal,
            byte? cLiabilitySignal)
        {
            RequireBayesianSetup();
            cLiabilitySignal = NormalizeNullableSignal(cLiabilitySignal);
            double[] posteriorQualityWeights = liabilitySignalsBayes
                .GetPosteriorHiddenProbabilitiesGivenSignals(
                    pLiabilitySignal,
                    dLiabilitySignal,
                    cLiabilitySignal);
            double mean = 0.0;
            for (int i = 0; i < qualityNodes.Length; i++)
                mean += posteriorQualityWeights[i] * qualityNodes[i];
            return Math.Clamp(mean, 0.0, 1.0);
        }

        public override double[] BayesianCalculations_GetLiabilityStrengthProbabilities(
            byte pLiabilitySignal,
            byte dLiabilitySignal,
            byte? cLiabilitySignal)
        {
            double pTrue = GetPosteriorMeanQuality(
                pLiabilitySignal,
                dLiabilitySignal,
                cLiabilitySignal);
            return new[] { 1.0 - pTrue, pTrue };
        }

        public override void BayesianCalculations_WorkBackwardsFromSignals(
            LitigGameProgress gameProgress,
            byte pLiabilitySignal,
            byte dLiabilitySignal,
            byte? cLiabilitySignal,
            byte pDamagesSignal,
            byte dDamagesSignal,
            byte? cDamagesSignal,
            int randomSeed)
        {
            if (gameProgress == null)
                throw new ArgumentNullException(nameof(gameProgress));

            var random = new Random(randomSeed);
            double[] truthProbabilities = BayesianCalculations_GetLiabilityStrengthProbabilities(
                pLiabilitySignal,
                dLiabilitySignal,
                cLiabilitySignal);
            byte truth = ArrayUtilities.ChooseIndex_OneBasedByte(truthProbabilities, random.NextDouble());
            SetReportedTruth(gameProgress, truth);
        }

        public override List<(GameProgress progress, double weight)> BayesianCalculations_GenerateAllConsistentGameProgresses(
            byte pLiabilitySignal,
            byte dLiabilitySignal,
            byte? cLiabilitySignal,
            byte pDamagesSignal,
            byte dDamagesSignal,
            byte? cDamagesSignal,
            LitigGameProgress baseProgress)
        {
            if (baseProgress == null)
                throw new ArgumentNullException(nameof(baseProgress));

            double[] truthProbabilities = BayesianCalculations_GetLiabilityStrengthProbabilities(
                pLiabilitySignal,
                dLiabilitySignal,
                cLiabilitySignal);
            var results = new List<(GameProgress progress, double weight)>();
            for (byte truth = 1; truth <= 2; truth++)
            {
                double weight = truthProbabilities[truth - 1];
                if (weight <= 0.0)
                    continue;
                LitigGameProgress copy = baseProgress.DeepCopy();
                SetReportedTruth(copy, truth);
                results.Add((copy, weight));
            }
            return results;
        }

        private static void SetReportedTruth(LitigGameProgress progress, byte truth)
        {
            progress.IsTrulyLiable = truth == 2;
            progress.LiabilityStrengthDiscrete = truth;
            progress.DamagesStrengthDiscrete = 1;
            LitigGameStandardDisputeGeneratorActions actions = progress.DisputeGeneratorActions;
            actions.PostPrimaryChanceAction = truth;
            progress.DisputeGeneratorActions = actions;
        }
    }
}
