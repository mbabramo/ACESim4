using ACESim.Util;
using ACESimBase.Util.Mathematics;
using ACESimBase.Util.Statistical;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESim
{
    [Serializable]
    public class LitigGameEqualQualityProbabilitiesDisputeGenerator : LitigGameStandardDisputeGeneratorBase
    {

        public override string GetGeneratorName() => "EqualQual";

        public override string OptionsString => $"ProbabilityTrulyLiable_LiabilityStrength75 {ProbabilityTrulyLiable_LiabilityStrength75} ProbabilityTrulyLiable_LiabilityStrength90 {ProbabilityTrulyLiable_LiabilityStrength90} NumPointsToDetermineTrulyLiable {NumPointsToDetermineTrulyLiable}";
        public override (string name, string abbreviation) PrePrimaryNameAndAbbreviation => ("PrePrimaryChanceActions", "Pre Primary");
        public override (string name, string abbreviation) PrimaryNameAndAbbreviation => ("PrimaryChanceActions", "Primary");
        public override (string name, string abbreviation) PostPrimaryNameAndAbbreviation => ("PostPrimaryChanceActions", "Post Primary");
        public override string GetActionString(byte action, byte decisionByteCode)
        {
            return action.ToString();
        }
        /// <summary>
        /// If each litigation quality value is equally likely, then the probability that the defendant is truly liable when the uniform litigation quality is 0.75. A value higehr than 0.75 reflects the proposition that majorities are likely generally to be correct.
        /// </summary>
        public double ProbabilityTrulyLiable_LiabilityStrength75;
        /// If each litigation quality value is equally likely, then the probability that the defendant is truly liable when the uniform litigation quality is 0.90. A value higehr than 0.90 reflects the proposition that majorities are likely generally to be correct.
        public double ProbabilityTrulyLiable_LiabilityStrength90;
        /// <summary>
        /// The number of points to determine whether the defendant is truly liable in a particular case. 
        /// </summary>
        public byte NumPointsToDetermineTrulyLiable;

        /// <summary>
        /// The curvature of the curve determining the probability of truly liable based on litigation quality.
        /// </summary>
        private double Curvature;

        public override void Setup(LitigGameDefinition myGameDefinition)
        {
            LitigGameDefinition = myGameDefinition;
            Curvature = MonotonicCurve.CalculateCurvatureForThreePoints(0.5, 0.5, 0.75, ProbabilityTrulyLiable_LiabilityStrength75, 0.9, ProbabilityTrulyLiable_LiabilityStrength90);
        }

        public override void GetActionsSetup(LitigGameDefinition myGameDefinition, out byte prePrimaryChanceActions, out byte primaryActions, out byte postPrimaryChanceActions, out byte[] prePrimaryPlayersToInform, out byte[] primaryPlayersToInform, out byte[] postPrimaryPlayersToInform, out bool prePrimaryUnevenChance, out bool postPrimaryUnevenChance, out bool litigationQualityUnevenChance, out bool primaryActionCanTerminate, out bool postPrimaryChanceCanTerminate)
        {
            prePrimaryChanceActions = 0;
            primaryActions = 0;
            postPrimaryChanceActions = NumPointsToDetermineTrulyLiable; 
            prePrimaryPlayersToInform = null; // new byte[] {(byte) LitigGamePlayers.Defendant }; // if we did have a pre-primary chance, we must notify defendant
            primaryPlayersToInform = null; // new byte[] {(byte) LitigGamePlayers.Resolution}; // not relevant for this dispute generator
            postPrimaryPlayersToInform = new byte[] { (byte)LitigGamePlayers.LiabilityStrengthChance }; // NOTE: The post-primary chance decision affects only our measurement of "is truly liable"; thus, it doesn't affect resolution probabilities and doesn't need to be included here.
            prePrimaryUnevenChance = true; // though not used
            postPrimaryUnevenChance = false; // we use even chance probabilities on number of poitns to determine truly liable
            litigationQualityUnevenChance = false; // we use even chance probabilities on litigation quality
            primaryActionCanTerminate = false;
            postPrimaryChanceCanTerminate = false;
        }

        public override double GetLitigationIndependentSocialWelfare(LitigGameDefinition myGameDefinition, LitigGameDisputeGeneratorActions disputeGeneratorActions)
        {
            return 0;
        }

        double[] NoWealthEffects = new double[] { 0, 0 };
        public override double[] GetLitigationIndependentWealthEffects(LitigGameDefinition myGameDefinition, LitigGameDisputeGeneratorActions disputeGeneratorActions)
        {
            return NoWealthEffects;
        }

        public override (double opportunityCost, double harmCost) GetOpportunityAndHarmCosts(LitigGameDefinition gameDef, LitigGameDisputeGeneratorActions acts)
        {
            return (0.0, 0.0);  // inapplicable
        }

        public override double[] GetLiabilityStrengthProbabilities(LitigGameDefinition myGameDefinition, LitigGameDisputeGeneratorActions disputeGeneratorActions)
        {
            throw new NotImplementedException(); // we use even chance probabilities
        }

        public override double[] GetDamagesStrengthProbabilities(LitigGameDefinition myGameDefinition, LitigGameDisputeGeneratorActions disputeGeneratorActions) => new double[] { 1.0 };

        public override bool IsTrulyLiable(LitigGameDefinition myGameDefinition, LitigGameDisputeGeneratorActions disputeGeneratorActions, GameProgress gameProgress)
        {
            LitigGameProgress myGameProgress = (LitigGameProgress) gameProgress;
            double liabilityStrengthUniform = EquallySpaced.GetLocationOfEquallySpacedPoint(myGameProgress.LiabilityStrengthDiscrete - 1 /* make it zero-based */, myGameDefinition.Options.NumLiabilityStrengthPoints, false); 
            double probabilityTrulyLiable = MonotonicCurve.CalculateYValueForX(0, 1.0, Curvature, (double)liabilityStrengthUniform);
            double randomValue = EquallySpaced.GetLocationOfEquallySpacedPoint(disputeGeneratorActions.PostPrimaryChanceAction - 1, NumPointsToDetermineTrulyLiable, false);
            return probabilityTrulyLiable >= randomValue;
        }

        public void ModifyOptions(ref LitigGameOptions myGameOptions)
        {
            // nothing to modify
        }

        public override bool PotentialDisputeArises(LitigGameDefinition myGameDefinition, LitigGameDisputeGeneratorActions disputeGeneratorActions)
        {
            return true;
        }

        public override bool MarkComplete(LitigGameDefinition myGameDefinition, byte prePrimaryAction, byte primaryAction)
        {
            throw new NotImplementedException();
        }

        public override bool MarkComplete(LitigGameDefinition myGameDefinition, byte prePrimaryAction, byte primaryAction, byte postPrimaryAction)
        {
            throw new NotImplementedException();
        }

        public override double[] GetPrePrimaryChanceProbabilities(LitigGameDefinition myGameDefinition)
        {
            throw new NotImplementedException(); // no pre primary chance function
        }

        public override double[] GetPostPrimaryChanceProbabilities(LitigGameDefinition myGameDefinition, LitigGameDisputeGeneratorActions disputeGeneratorActions)
        {
            throw new NotImplementedException(); // we use even chance probabilities
        }

        public override bool SupportsSymmetry() => false; // we might be able to change this by changing the curvature so that we have symmetry around probability of 0.5

        public override bool PostPrimaryDoesNotAffectStrategy() => false;
    }
}




