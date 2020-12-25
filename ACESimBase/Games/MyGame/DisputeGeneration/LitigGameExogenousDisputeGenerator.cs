using ACESim.Util;
using ACESimBase.GameSolvingSupport;
using ACESimBase.Util;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESim
{
    [Serializable]
    public class LitigGameExogenousDisputeGenerator : ILitigGameDisputeGenerator
    {
        public string GetGeneratorName() => "Exog";
        /// <summary>
        /// If litigation quality is generated from truly-liable status, and truly-liable status is exogenously determined, then the probability that the correct outcome is that the defendant truly is liable.
        /// </summary>
        public double ExogenousProbabilityTrulyLiable;
        /// <summary>
        /// If generating LiabilityStrength based on true case value, this is the standard deviation of the noise used to obscure the true litigation quality value (0 or 1). Each litigation quality level will be equally likely if there is an equal chance of either true litigation quality value.
        /// </summary>
        public double StdevNoiseToProduceLiabilityStrength;

        private double[] ProbabilityOfTrulyLiableValues, ProbabilitiesLiabilityStrength_TrulyNotLiable, ProbabilitiesLiabilityStrength_TrulyLiable;
        private double[] ProbabilityOfDamagesStrengthValues;

        public void Setup(LitigGameDefinition myGameDefinition)
        {
            ProbabilityOfTrulyLiableValues = new double[] { 1.0 - ExogenousProbabilityTrulyLiable, ExogenousProbabilityTrulyLiable };
            // A case is assigned a "true" value of 1 (should not be liable) or 2 (should be liable).
            // Based on the litigation quality noise parameter, we then collect a distribution of possible realized values, on the assumption
            // that the true values are equally likely. We then break this distribution into evenly sized buckets (based on a separate standard
            // deviation value) to get cutoff points.
            // Given this approach, the exogenous probability has no effect on ProbabilitiesLiabilityStrength_TrulyNotLiable and ProbabilitiesLiabilityStrength_TrulyLiable; both of these are conditional on whether a case is liable or not.
           DiscreteValueSignalParameters liabilityParams = new DiscreteValueSignalParameters() { NumPointsInSourceUniformDistribution = 2, NumSignals = myGameDefinition.Options.NumLiabilityStrengthPoints, StdevOfNormalDistribution = StdevNoiseToProduceLiabilityStrength, StdevOfNormalDistributionForCutoffPoints = 0.5, SourcePointsIncludeExtremes = true };
            ProbabilitiesLiabilityStrength_TrulyNotLiable = DiscreteValueSignal.GetProbabilitiesOfDiscreteSignals(1, liabilityParams);
            ProbabilitiesLiabilityStrength_TrulyLiable = DiscreteValueSignal.GetProbabilitiesOfDiscreteSignals(2, liabilityParams);

            // damages is simpler -- each damages level is equally likely
            ProbabilityOfDamagesStrengthValues = new double[myGameDefinition.Options.NumDamagesStrengthPoints];
            for (int i = 0; i < myGameDefinition.Options.NumDamagesStrengthPoints; i++)
                ProbabilityOfDamagesStrengthValues[i] = 1.0 / (double)myGameDefinition.Options.NumDamagesStrengthPoints;
        }

        public void GetActionsSetup(LitigGameDefinition myGameDefinition, out byte prePrimaryChanceActions, out byte primaryActions, out byte postPrimaryChanceActions, out byte[] prePrimaryPlayersToInform, out byte[] primaryPlayersToInform, out byte[] postPrimaryPlayersToInform, out bool prePrimaryUnevenChance, out bool postPrimaryUnevenChance, out bool litigationQualityUnevenChance, out bool primaryActionCanTerminate, out bool postPrimaryChanceCanTerminate)
        {
            prePrimaryChanceActions = 0;
            primaryActions = 0;
            postPrimaryChanceActions = (byte) 2; // not truly liable or truly liable
            prePrimaryPlayersToInform = null; // new byte[] {(byte) LitigGamePlayers.Defendant }; // if we did have a pre-primary chance, we must notify defendant
            primaryPlayersToInform = null; // new byte[] {(byte) LitigGamePlayers.Resolution}; // not relevant for this dispute generator
            postPrimaryPlayersToInform = new byte[] {(byte) LitigGamePlayers.LiabilityStrengthChance, (byte) LitigGamePlayers.Resolution};
            prePrimaryUnevenChance = true; // though not used
            postPrimaryUnevenChance = true;
            litigationQualityUnevenChance = true;
            primaryActionCanTerminate = false;
            postPrimaryChanceCanTerminate = false;
        }

        public double GetLitigationIndependentSocialWelfare(LitigGameDefinition myGameDefinition, LitigGameDisputeGeneratorActions disputeGeneratorActions)
        {
            return 0;
        }

        double[] NoWealthEffects = new double[] { 0, 0 };
        public double[] GetLitigationIndependentWealthEffects(LitigGameDefinition myGameDefinition, LitigGameDisputeGeneratorActions disputeGeneratorActions)
        {
            return NoWealthEffects; 
        }

        public double[] GetLiabilityStrengthProbabilities(LitigGameDefinition myGameDefinition, LitigGameDisputeGeneratorActions disputeGeneratorActions)
        {
            bool isTrulyLiable = IsTrulyLiable(myGameDefinition, disputeGeneratorActions, null);
            if (isTrulyLiable)
                return ProbabilitiesLiabilityStrength_TrulyLiable;
            else
                return ProbabilitiesLiabilityStrength_TrulyNotLiable;
        }

        public double[] GetDamagesStrengthProbabilities(LitigGameDefinition myGameDefinition, LitigGameDisputeGeneratorActions disputeGeneratorActions) => ProbabilityOfDamagesStrengthValues;

        public bool IsTrulyLiable(LitigGameDefinition myGameDefinition, LitigGameDisputeGeneratorActions disputeGeneratorActions, GameProgress gameProgress)
        {
            return disputeGeneratorActions.PostPrimaryChanceAction == 2;
        }

        public void ModifyOptions(ref LitigGameOptions myGameOptions)
        {
            // nothing to modify
        }

        public bool PotentialDisputeArises(LitigGameDefinition myGameDefinition, LitigGameDisputeGeneratorActions disputeGeneratorActions)
        {
            return true;
        }

        public bool MarkComplete(LitigGameDefinition myGameDefinition, byte prePrimaryAction, byte primaryAction)
        {
            throw new NotImplementedException();
        }

        public bool MarkComplete(LitigGameDefinition myGameDefinition, byte prePrimaryAction, byte primaryAction, byte postPrimaryAction)
        {
            throw new NotImplementedException();
        }

        public double[] GetPrePrimaryChanceProbabilities(LitigGameDefinition myGameDefinition)
        {
            throw new NotImplementedException(); // no pre primary chance function
        }

        public double[] GetPostPrimaryChanceProbabilities(LitigGameDefinition myGameDefinition, LitigGameDisputeGeneratorActions disputeGeneratorActions)
        {
            return ProbabilityOfTrulyLiableValues;
        }

        public bool PostPrimaryDoesNotAffectStrategy() => true;

        public bool SupportsSymmetry() => true;

        public (bool unrollParallelize, bool unrollIdentical, SymmetryMapInput symmetryMapInput) GetPrePrimaryUnrollSettings() =>
            (true, true, SymmetryMapInput.SameInfo);
        public (bool unrollParallelize, bool unrollIdentical, SymmetryMapInput symmetryMapInput) GetPrimaryUnrollSettings() =>
            (true, true, SymmetryMapInput.SameInfo);
        public (bool unrollParallelize, bool unrollIdentical, SymmetryMapInput symmetryMapInput) GetPostPrimaryUnrollSettings() =>
            (true, true, SymmetryMapInput.ReverseInfo);
        public (bool unrollParallelize, bool unrollIdentical, SymmetryMapInput symmetryMapInput) GetLiabilityStrengthUnrollSettings() =>
            (true, true, SymmetryMapInput.ReverseInfo);
        public (bool unrollParallelize, bool unrollIdentical, SymmetryMapInput symmetryMapInput) GetDamagesStrengthUnrollSettings() =>
            (true, true, SymmetryMapInput.ReverseInfo);

    }
}