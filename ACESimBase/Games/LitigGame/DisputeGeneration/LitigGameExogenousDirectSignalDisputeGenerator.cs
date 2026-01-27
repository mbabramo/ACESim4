using ACESim.Util;
using ACESim.Util.DiscreteProbabilities;
using ACESimBase.GameSolvingSupport.Symmetry;
using ACESimBase.Util.ArrayManipulation;
using ACESimBase.Util.DiscreteProbabilities;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ACESim
{
    [Serializable]
    public sealed class LitigGameExogenousDirectSignalDisputeGenerator : LitigGameStandardDisputeGeneratorBase
    {
        public override string GetGeneratorName() => "ExogDirSig";

        /// <summary>
        /// Exogenous probability that the defendant truly is liable (true value = 2).
        /// </summary>
        public double ExogenousProbabilityTrulyLiable;

        private double[] ProbabilityOfTrulyLiableValues;
        private double[] ProbabilityOfDamagesStrengthValues;

        private double[] ProbabilitiesLiabilityStrength_TrulyNotLiable;
        private double[] ProbabilitiesLiabilityStrength_TrulyLiable;

        private ThreePartyCorrelatedSignalsBayes LiabilitySignalsBayes;
        private ThreePartyCorrelatedSignalsBayes DamagesSignalsBayes;

        private double[] pLiabilitySignalProbabilitiesUnconditional;
        private double[] pDamagesSignalProbabilitiesUnconditional;

        private double[] dLiabilitySignalProbabilitiesUnconditional;
        private double[] dDamagesSignalProbabilitiesUnconditional;

        public override string OptionsString => $"ExogenousProbabilityTrulyLiable={ExogenousProbabilityTrulyLiable}";

        public override void Setup(LitigGameDefinition litigGameDefinition)
        {
            base.Setup(litigGameDefinition);

            if (litigGameDefinition == null)
                throw new ArgumentNullException(nameof(litigGameDefinition));

            LitigGameOptions o = litigGameDefinition.Options;

            if (o.NumLiabilityStrengthPoints != 2)
                throw new InvalidOperationException($"{nameof(LitigGameExogenousDirectSignalDisputeGenerator)} requires {nameof(o.NumLiabilityStrengthPoints)} == 2.");

            ProbabilityOfTrulyLiableValues = new double[] { 1.0 - ExogenousProbabilityTrulyLiable, ExogenousProbabilityTrulyLiable };
            ProbabilityOfTrulyLiableValues[1] = 1.0 - ProbabilityOfTrulyLiableValues[0];

            ProbabilitiesLiabilityStrength_TrulyNotLiable = new double[] { 1.0, 0.0 };
            ProbabilitiesLiabilityStrength_TrulyLiable = new double[] { 0.0, 1.0 };

            int numDamagesStrengthPoints = o.NumDamagesStrengthPoints;
            if (numDamagesStrengthPoints <= 0)
                throw new InvalidOperationException($"{nameof(o.NumDamagesStrengthPoints)} must be >= 1.");

            ProbabilityOfDamagesStrengthValues =
                Enumerable.Range(0, numDamagesStrengthPoints).Select(_ => 1.0 / (double)numDamagesStrengthPoints).ToArray();
            ProbabilityOfDamagesStrengthValues[numDamagesStrengthPoints - 1] =
                1.0 - ProbabilityOfDamagesStrengthValues.Take(numDamagesStrengthPoints - 1).Sum();

            SetupInverted(litigGameDefinition);
        }

        private static byte? NormalizeNullableSignal(byte? signal)
        {
            if (signal is byte b && b == 0)
                return null;
            return signal;
        }

        private static double[] ComputeUnconditionalSignalDistribution(double[] priorHiddenValues, double[][] probabilitiesSignalGivenHidden)
        {
            if (priorHiddenValues == null)
                throw new ArgumentNullException(nameof(priorHiddenValues));
            if (probabilitiesSignalGivenHidden == null)
                throw new ArgumentNullException(nameof(probabilitiesSignalGivenHidden));
            if (probabilitiesSignalGivenHidden.Length != priorHiddenValues.Length)
                throw new ArgumentException("Hidden dimension mismatch.");

            int signalCount = probabilitiesSignalGivenHidden[0].Length;
            double[] result = new double[signalCount];

            for (int h = 0; h < priorHiddenValues.Length; h++)
            {
                double prior = priorHiddenValues[h];
                double[] cond = probabilitiesSignalGivenHidden[h];
                if (cond.Length != signalCount)
                    throw new ArgumentException("Signal dimension mismatch.");
                for (int s = 0; s < signalCount; s++)
                    result[s] += prior * cond[s];
            }

            double sum = result.Sum();
            if (sum > 0.0 && Math.Abs(sum - 1.0) > 1e-10)
            {
                for (int s = 0; s < signalCount; s++)
                    result[s] /= sum;
            }

            return result;
        }

        private void SetupInverted(LitigGameDefinition litigGameDefinition)
        {
            LitigGameOptions o = litigGameDefinition.Options;
            if (!o.CollapseChanceDecisions)
                return;

            double[] targetPartyLiabilitySignalMarginal = o.GetTargetSignalLabelMarginalDistributionOrNull(o.NumLiabilitySignals);
            double[] targetCourtLiabilitySignalMarginal = o.GetTargetSignalLabelMarginalDistributionOrNull(o.NumCourtLiabilitySignals);

            var pLiabilitySignalProbabilitiesGivenTrueLiability = new double[][] {
                DiscreteValueSignal.GetProbabilitiesOfDiscreteSignals(1, PLiabilitySignalParameters),
                DiscreteValueSignal.GetProbabilitiesOfDiscreteSignals(2, PLiabilitySignalParameters)
            };
            var dLiabilitySignalProbabilitiesGivenTrueLiability = new double[][] {
                DiscreteValueSignal.GetProbabilitiesOfDiscreteSignals(1, DLiabilitySignalParameters),
                DiscreteValueSignal.GetProbabilitiesOfDiscreteSignals(2, DLiabilitySignalParameters)
            };
            var cLiabilitySignalProbabilitiesGivenTrueLiability = new double[][] {
                DiscreteValueSignal.GetProbabilitiesOfDiscreteSignals(1, CLiabilitySignalParameters),
                DiscreteValueSignal.GetProbabilitiesOfDiscreteSignals(2, CLiabilitySignalParameters)
            };

            if (targetPartyLiabilitySignalMarginal != null)
            {
                pLiabilitySignalProbabilitiesGivenTrueLiability = SignalMarginalDistributionTransform.TransformConditionalSignalDistributionsToMatchTargetMarginal(ProbabilityOfTrulyLiableValues, pLiabilitySignalProbabilitiesGivenTrueLiability, targetPartyLiabilitySignalMarginal);
                dLiabilitySignalProbabilitiesGivenTrueLiability = SignalMarginalDistributionTransform.TransformConditionalSignalDistributionsToMatchTargetMarginal(ProbabilityOfTrulyLiableValues, dLiabilitySignalProbabilitiesGivenTrueLiability, targetPartyLiabilitySignalMarginal);
            }
            if (targetCourtLiabilitySignalMarginal != null)
            {
                cLiabilitySignalProbabilitiesGivenTrueLiability = SignalMarginalDistributionTransform.TransformConditionalSignalDistributionsToMatchTargetMarginal(ProbabilityOfTrulyLiableValues, cLiabilitySignalProbabilitiesGivenTrueLiability, targetCourtLiabilitySignalMarginal);
            }

            _liabilitySignalsBayes = new ThreePartyCorrelatedSignalsBayes(
                ProbabilityOfTrulyLiableValues,
                pLiabilitySignalProbabilitiesGivenTrueLiability,
                dLiabilitySignalProbabilitiesGivenTrueLiability,
                cLiabilitySignalProbabilitiesGivenTrueLiability);

            pLiabilitySignalProbabilitiesUnconditional = _liabilitySignalsBayes.GetParty0SignalProbabilitiesUnconditional();
            dLiabilitySignalProbabilitiesUnconditional = ComputeUnconditionalSignalDistribution(ProbabilityOfTrulyLiableValues, dLiabilitySignalProbabilitiesGivenTrueLiability);

            if (o.NumDamagesStrengthPoints > 1)
            {
                double[] targetPartyDamagesSignalMarginal = o.GetTargetSignalLabelMarginalDistributionOrNull(o.NumDamagesSignals);
                double[] targetCourtDamagesSignalMarginal = o.GetTargetSignalLabelMarginalDistributionOrNull(o.NumDamagesSignals);

                var pDamagesSignalProbabilitiesGivenDamagesStrength = new double[o.NumDamagesStrengthPoints][];
                var dDamagesSignalProbabilitiesGivenDamagesStrength = new double[o.NumDamagesStrengthPoints][];
                var cDamagesSignalProbabilitiesGivenDamagesStrength = new double[o.NumDamagesStrengthPoints][];

                for (int i = 0; i < o.NumDamagesStrengthPoints; i++)
                {
                    int strength1Based = i + 1;
                    pDamagesSignalProbabilitiesGivenDamagesStrength[i] = DiscreteValueSignal.GetProbabilitiesOfDiscreteSignals(strength1Based, PDamagesSignalParameters);
                    dDamagesSignalProbabilitiesGivenDamagesStrength[i] = DiscreteValueSignal.GetProbabilitiesOfDiscreteSignals(strength1Based, DDamagesSignalParameters);
                    cDamagesSignalProbabilitiesGivenDamagesStrength[i] = DiscreteValueSignal.GetProbabilitiesOfDiscreteSignals(strength1Based, CDamagesSignalParameters);
                }

                if (targetPartyDamagesSignalMarginal != null)
                {
                    pDamagesSignalProbabilitiesGivenDamagesStrength = SignalMarginalDistributionTransform.TransformConditionalSignalDistributionsToMatchTargetMarginal(ProbabilityOfDamagesStrengthValues, pDamagesSignalProbabilitiesGivenDamagesStrength, targetPartyDamagesSignalMarginal);
                    dDamagesSignalProbabilitiesGivenDamagesStrength = SignalMarginalDistributionTransform.TransformConditionalSignalDistributionsToMatchTargetMarginal(ProbabilityOfDamagesStrengthValues, dDamagesSignalProbabilitiesGivenDamagesStrength, targetPartyDamagesSignalMarginal);
                }
                if (targetCourtDamagesSignalMarginal != null)
                {
                    cDamagesSignalProbabilitiesGivenDamagesStrength = SignalMarginalDistributionTransform.TransformConditionalSignalDistributionsToMatchTargetMarginal(ProbabilityOfDamagesStrengthValues, cDamagesSignalProbabilitiesGivenDamagesStrength, targetCourtDamagesSignalMarginal);
                }

                _damagesSignalsBayes = new ThreePartyCorrelatedSignalsBayes(
                    ProbabilityOfDamagesStrengthValues,
                    pDamagesSignalProbabilitiesGivenDamagesStrength,
                    dDamagesSignalProbabilitiesGivenDamagesStrength,
                    cDamagesSignalProbabilitiesGivenDamagesStrength);

                pDamagesSignalProbabilitiesUnconditional = _damagesSignalsBayes.GetParty0SignalProbabilitiesUnconditional();
                dDamagesSignalProbabilitiesUnconditional = ComputeUnconditionalSignalDistribution(ProbabilityOfDamagesStrengthValues, dDamagesSignalProbabilitiesGivenDamagesStrength);
            }
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
            postPrimaryPlayersToInform = new byte[] { (byte)LitigGamePlayers.LiabilityStrengthChance, (byte)LitigGamePlayers.Resolution };

            prePrimaryUnevenChance = false;
            postPrimaryUnevenChance = true;
            litigationQualityUnevenChance = true;

            primaryActionCanTerminate = false;
            postPrimaryChanceCanTerminate = false;
        }

        public override (string name, string abbreviation) PrePrimaryNameAndAbbreviation => ("None", "None");
        public override (string name, string abbreviation) PrimaryNameAndAbbreviation => ("None", "None");
        public override (string name, string abbreviation) PostPrimaryNameAndAbbreviation => ("Truly Liable?", "TL?");

        public override bool PotentialDisputeArises(LitigGameDefinition g, LitigGameStandardDisputeGeneratorActions a, LitigGameProgress p) => true;

        public override bool MarkComplete(LitigGameDefinition g, byte pre, byte primary) => throw new NotImplementedException();
        public override bool MarkComplete(LitigGameDefinition g, byte pre, byte primary, byte post) => throw new NotImplementedException();

        public override bool IsTrulyLiable(LitigGameDefinition g, LitigGameStandardDisputeGeneratorActions a, GameProgress p)
        {
            if (p is LitigGameProgress lgp && lgp.IsTrulyLiable is bool b)
                return b;
            return a.PostPrimaryChanceAction == 2;
        }

        public override double[] GetPrePrimaryChanceProbabilities(LitigGameDefinition g) => throw new NotImplementedException();

        public override double[] GetPostPrimaryChanceProbabilities(LitigGameDefinition g, LitigGameStandardDisputeGeneratorActions a) => ProbabilityOfTrulyLiableValues;

        public override bool PostPrimaryDoesNotAffectStrategy() => true;

        public override double[] GetLiabilityStrengthProbabilities(LitigGameDefinition g, LitigGameStandardDisputeGeneratorActions a)
            => a.PostPrimaryChanceAction == 2 ? ProbabilitiesLiabilityStrength_TrulyLiable : ProbabilitiesLiabilityStrength_TrulyNotLiable;

        public override double[] GetDamagesStrengthProbabilities(LitigGameDefinition g, LitigGameStandardDisputeGeneratorActions a)
            => ProbabilityOfDamagesStrengthValues;

        public override double GetLitigationIndependentSocialWelfare(LitigGameDefinition g, LitigGameStandardDisputeGeneratorActions a, LitigGameProgress p) => 0;

        public override double[] GetLitigationIndependentWealthEffects(LitigGameDefinition g, LitigGameStandardDisputeGeneratorActions a, LitigGameProgress p) => new double[] { 0, 0 };

        public override string GetActionString(byte action, byte decisionByteCode)
        {
            if (decisionByteCode == (byte)LitigGameDecisions.PostPrimaryActionChance)
            {
                return action == 1 ? "Truly Not Liable" : "Truly Liable";
            }
            return action.ToString();
        }

        public override bool SupportsSymmetry() => true;

        public override (bool unrollIdentical, SymmetryMapInput symmetryMapInput) GetPrePrimaryUnrollSettings()
            => (true, SymmetryMapInput.SameInfo);

        public override (bool unrollIdentical, SymmetryMapInput symmetryMapInput) GetPrimaryUnrollSettings()
            => (true, SymmetryMapInput.SameInfo);

        public override (bool unrollIdentical, SymmetryMapInput symmetryMapInput) GetPostPrimaryUnrollSettings()
            => (true, SymmetryMapInput.ReverseInfo);

        public override (bool unrollIdentical, SymmetryMapInput symmetryMapInput) GetLiabilityStrengthUnrollSettings()
            => (true, SymmetryMapInput.ReverseInfo);

        public override (bool unrollIdentical, SymmetryMapInput symmetryMapInput) GetDamagesStrengthUnrollSettings()
            => (true, SymmetryMapInput.ReverseInfo);

        public override double[] BayesianCalculations_GetPLiabilitySignalProbabilities(byte? dLiabilitySignal)
        {
            if (pLiabilitySignalProbabilitiesUnconditional == null)
                throw new InvalidOperationException("Bayesian setup not initialized. Ensure Setup() was called with CollapseChanceDecisions enabled.");
            return pLiabilitySignalProbabilitiesUnconditional;
        }

        public override double[] BayesianCalculations_GetDLiabilitySignalProbabilities(byte? pLiabilitySignal)
        {
            if (LiabilitySignalsBayes == null)
                throw new InvalidOperationException("Bayesian setup not initialized. Ensure Setup() was called with CollapseChanceDecisions enabled.");

            pLiabilitySignal = NormalizeNullableSignal(pLiabilitySignal);
            if (pLiabilitySignal is null)
                return dLiabilitySignalProbabilitiesUnconditional;

            return LiabilitySignalsBayes.GetParty1SignalProbabilitiesGivenParty0Signal(pLiabilitySignal.Value);
        }

        public override double[] BayesianCalculations_GetCLiabilitySignalProbabilities(byte pLiabilitySignal, byte dLiabilitySignal)
        {
            if (LiabilitySignalsBayes == null)
                throw new InvalidOperationException("Bayesian setup not initialized. Ensure Setup() was called with CollapseChanceDecisions enabled.");

            return LiabilitySignalsBayes.GetParty2SignalProbabilitiesGivenParty0AndParty1Signals(pLiabilitySignal, dLiabilitySignal);
        }

        public override double[] BayesianCalculations_GetPDamagesSignalProbabilities(byte? dDamagesSignal)
        {
            if (pDamagesSignalProbabilitiesUnconditional == null)
                throw new InvalidOperationException("Bayesian setup not initialized. Ensure Setup() was called with CollapseChanceDecisions enabled.");
            return pDamagesSignalProbabilitiesUnconditional;
        }

        public override double[] BayesianCalculations_GetDDamagesSignalProbabilities(byte? pDamagesSignal)
        {
            if (DamagesSignalsBayes == null)
            {
                return new double[] { 1.0 };
            }

            pDamagesSignal = NormalizeNullableSignal(pDamagesSignal);
            if (pDamagesSignal is null)
                return dDamagesSignalProbabilitiesUnconditional;

            return DamagesSignalsBayes.GetParty1SignalProbabilitiesGivenParty0Signal(pDamagesSignal.Value);
        }

        public override double[] BayesianCalculations_GetCDamagesSignalProbabilities(byte pDamagesSignal, byte dDamagesSignal)
        {
            if (DamagesSignalsBayes == null)
            {
                return new double[] { 1.0 };
            }

            return DamagesSignalsBayes.GetParty2SignalProbabilitiesGivenParty0AndParty1Signals(pDamagesSignal, dDamagesSignal);
        }

        public override double[] BayesianCalculations_GetLiabilityStrengthProbabilities(byte pLiabilitySignal, byte dLiabilitySignal, byte? cLiabilitySignal)
        {
            if (LiabilitySignalsBayes == null)
                throw new InvalidOperationException("Bayesian setup not initialized. Ensure Setup() was called with CollapseChanceDecisions enabled.");

            cLiabilitySignal = NormalizeNullableSignal(cLiabilitySignal);
            return LiabilitySignalsBayes.GetPosteriorHiddenProbabilitiesGivenSignals(pLiabilitySignal, dLiabilitySignal, cLiabilitySignal);
        }

        public override double[] BayesianCalculations_GetDamagesStrengthProbabilities(byte pDamagesSignal, byte dDamagesSignal, byte? cDamagesSignal)
        {
            if (DamagesSignalsBayes == null)
            {
                return new double[] { 1.0 };
            }

            cDamagesSignal = NormalizeNullableSignal(cDamagesSignal);
            return DamagesSignalsBayes.GetPosteriorHiddenProbabilitiesGivenSignals(pDamagesSignal, dDamagesSignal, cDamagesSignal);
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

            Random r = new Random(randomSeed);

            double[] liabilityPosterior = BayesianCalculations_GetLiabilityStrengthProbabilities(pLiabilitySignal, dLiabilitySignal, cLiabilitySignal);
            byte liabilityStrength = ArrayUtilities.ChooseIndex_OneBasedByte(liabilityPosterior, r.NextDouble());
            bool trulyLiable = liabilityStrength == 2;

            byte damagesStrength;
            if (LitigGameDefinition.Options.NumDamagesStrengthPoints <= 1)
            {
                damagesStrength = 1;
            }
            else
            {
                double[] damagesPosterior = BayesianCalculations_GetDamagesStrengthProbabilities(pDamagesSignal, dDamagesSignal, cDamagesSignal);
                damagesStrength = ArrayUtilities.ChooseIndex_OneBasedByte(damagesPosterior, r.NextDouble());
            }

            gameProgress.IsTrulyLiable = trulyLiable;
            gameProgress.LiabilityStrengthDiscrete = liabilityStrength;
            gameProgress.DamagesStrengthDiscrete = damagesStrength;

            var acts = gameProgress.DisputeGeneratorActions;
            acts.PostPrimaryChanceAction = liabilityStrength;
            gameProgress.DisputeGeneratorActions = acts;
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

            List<(GameProgress progress, double weight)> withLiability = new List<(GameProgress progress, double weight)>();

            double[] liabilityPosterior = BayesianCalculations_GetLiabilityStrengthProbabilities(pLiabilitySignal, dLiabilitySignal, cLiabilitySignal);
            for (byte liab = 1; liab <= liabilityPosterior.Length; liab++)
            {
                double weight = liabilityPosterior[liab - 1];
                if (weight <= 0.0)
                    continue;

                var copy = baseProgress.DeepCopy();
                copy.LiabilityStrengthDiscrete = liab;
                copy.IsTrulyLiable = (liab == 2);

                var acts = copy.DisputeGeneratorActions;
                acts.PostPrimaryChanceAction = liab;
                copy.DisputeGeneratorActions = acts;

                withLiability.Add((copy, weight));
            }

            if (LitigGameDefinition.Options.NumDamagesStrengthPoints <= 1)
            {
                foreach (var gp in withLiability)
                {
                    ((LitigGameProgress)gp.progress).DamagesStrengthDiscrete = 1;
                }
                return withLiability;
            }

            double[] damagesPosterior = BayesianCalculations_GetDamagesStrengthProbabilities(pDamagesSignal, dDamagesSignal, cDamagesSignal);

            List<(GameProgress progress, double weight)> results = new List<(GameProgress progress, double weight)>();
            foreach (var gp in withLiability)
            {
                for (byte dmg = 1; dmg <= damagesPosterior.Length; dmg++)
                {
                    double weight = gp.weight * damagesPosterior[dmg - 1];
                    if (weight <= 0.0)
                        continue;

                    var copy = (LitigGameProgress)gp.progress.DeepCopy();
                    copy.DamagesStrengthDiscrete = dmg;
                    results.Add((copy, weight));
                }
            }

            return results;
        }
    }
}
