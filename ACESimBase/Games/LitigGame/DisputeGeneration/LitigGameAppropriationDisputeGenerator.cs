using ACESim.Util;
using ACESimBase.GameSolvingSupport;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static alglib;

namespace ACESim
{
    [Serializable]
    public class LitigGameAppropriationDisputeGenerator : LitigGameStandardDisputeGeneratorBase
    {
        public string GetGeneratorName() => "Appropriation";

        // Defendant must make a decision whether to appropriate something of value, at some benefit to self and some cost to the potential plaintiff.
        // (1) Defendant appropriates: Higher detectability implies a steeper geometric distribution that places most probability on the strongest plaintiff signal; lower detectability yields a flatter, more‑random distribution.
        // (2) Defendant does not appropriate: Litigation quality is drawn from a fixed geometric distribution weighted toward the weakest plaintiff signal and is independent of detectability.

        public byte NumDetectabilityLevels = 5;
        public double BenefitToDefendantOfAppropriation = 0.50;
        public double CostToPlaintiffOfAppropriation = 1.0;
        public double SocialWelfareMultiplier = 1.0;
        public bool CountBenefitToDefendantInSocialWelfare = false;

        // Controls steepness of innocent‑case geometric distribution (0 < InnocentEvidenceMultiplier < 1). Lower values mean more steeply declining distribution.
        public double InnocentEvidenceMultiplier = 0.5;

        private double[][] ProbabilityLiabilityStrengthForDetectabilityLevel_TrulyLiable;
        private double[] ProbabilityLiabilityStrength_TrulyNotLiable;

        public string OptionsString => $"NumDetectabilityLevels {NumDetectabilityLevels} BenefitToDefendant {BenefitToDefendantOfAppropriation} CostToPlaintiff {CostToPlaintiffOfAppropriation} SocialWelfareMultiplier {SocialWelfareMultiplier} CountBenefitToDefendantInSocialWelfare {CountBenefitToDefendantInSocialWelfare}";

        public (string name, string abbreviation) PrePrimaryNameAndAbbreviation => ("PrePrimaryChanceActions", "Detect");
        public (string name, string abbreviation) PrimaryNameAndAbbreviation => ("PrimaryActions", "Approp");
        public (string name, string abbreviation) PostPrimaryNameAndAbbreviation => ("PostPrimaryChanceActions", "Post Primary");

        public string GetActionString(byte action, byte decisionByteCode) => action.ToString();

        public LitigGameDefinition LitigGameDefinition { get; set; }
        public void Setup(LitigGameDefinition myGameDefinition)
        {
            LitigGameDefinition = myGameDefinition;
            int numLiabilityStrengthPoints = myGameDefinition.Options.NumLiabilityStrengthPoints;
            double seqSum, startVal;
            ProbabilityLiabilityStrengthForDetectabilityLevel_TrulyLiable = new double[NumDetectabilityLevels][];
            for (byte n = 1; n <= NumDetectabilityLevels; n++)
            {
                double multiplier = 1.0 - (double)(n - 0.5) / NumDetectabilityLevels; // higher detectability ⇒ smaller multiplier ⇒ steeper distribution
                seqSum = 0;
                for (byte k = 1; k <= numLiabilityStrengthPoints; k++)
                    seqSum += Math.Pow(multiplier, k - 1);
                startVal = 1.0 / seqSum;

                var decliningSequence = Enumerable.Range(0, numLiabilityStrengthPoints)
                    .Select(i => startVal * Math.Pow(multiplier, i))   // higher detectability → smaller multiplier → faster decline
                    .ToArray();

                var increasingSequence = decliningSequence.Reverse()   // reverse so mass moves from weakest to strongest signal
                    .ToArray();

                ProbabilityLiabilityStrengthForDetectabilityLevel_TrulyLiable[n - 1] = increasingSequence;

            }

            // Build detectability-independent distribution for the innocent (non-appropriation) case.
            // Probability mass is highest at the weakest plaintiff signal and declines geometrically.
            // InnocentEvidenceMultiplier (< 1) controls how steeply the probabilities fall.
            seqSum = 0;
            for (int i = 0; i < numLiabilityStrengthPoints; i++)
                seqSum += Math.Pow(InnocentEvidenceMultiplier, i);
            startVal = 1.0 / seqSum;
            ProbabilityLiabilityStrength_TrulyNotLiable = Enumerable
                .Range(0, numLiabilityStrengthPoints)
                .Select(i => startVal * Math.Pow(InnocentEvidenceMultiplier, i))
                .ToArray();

        }

        public void GetActionsSetup(LitigGameDefinition myGameDefinition, out byte prePrimaryChanceActions, out byte primaryActions, out byte postPrimaryChanceActions, out byte[] prePrimaryPlayersToInform, out byte[] primaryPlayersToInform, out byte[] postPrimaryPlayersToInform, out bool prePrimaryUnevenChance, out bool postPrimaryUnevenChance, out bool litigationQualityUnevenChance, out bool primaryActionCanTerminate, out bool postPrimaryChanceCanTerminate)
        {
            prePrimaryChanceActions = NumDetectabilityLevels;
            primaryActions = 2;
            postPrimaryChanceActions = 0;
            prePrimaryPlayersToInform = new byte[] { (byte)LitigGamePlayers.Defendant, (byte)LitigGamePlayers.LiabilityStrengthChance };
            primaryPlayersToInform = new byte[] { (byte)LitigGamePlayers.Defendant, (byte)LitigGamePlayers.Resolution, (byte)LitigGamePlayers.LiabilityStrengthChance };
            postPrimaryPlayersToInform = null;
            prePrimaryUnevenChance = false;
            postPrimaryUnevenChance = true;
            litigationQualityUnevenChance = true;
            primaryActionCanTerminate = false;
            postPrimaryChanceCanTerminate = false;
        }

        readonly double[] WealthEffects_NoAppropriation = new double[] { 0, 0 };
        public double[] GetLitigationIndependentWealthEffects(LitigGameDefinition myGameDefinition, LitigGameDisputeGeneratorActions disputeGeneratorActions)
        {
            if (disputeGeneratorActions.PrimaryAction == 2)
                return WealthEffects_NoAppropriation;
            return new double[] { -CostToPlaintiffOfAppropriation, BenefitToDefendantOfAppropriation };
        }

        public double GetLitigationIndependentSocialWelfare(LitigGameDefinition myGameDefinition, LitigGameDisputeGeneratorActions disputeGeneratorActions)
        {
            if (disputeGeneratorActions.PrimaryAction == 2)
                return 0;
            return -CostToPlaintiffOfAppropriation * SocialWelfareMultiplier + (CountBenefitToDefendantInSocialWelfare ? BenefitToDefendantOfAppropriation * SocialWelfareMultiplier : 0);
        }
        public (double opportunityCost, double harmCost) GetOpportunityAndHarmCosts(LitigGameDefinition gameDef, LitigGameDisputeGeneratorActions acts)
        {
            // Not appropriating (primaryAction == 2) means giving up the benefit.
            double precaution = acts.PrimaryAction == 2
                ? BenefitToDefendantOfAppropriation
                : 0.0;

            // Appropriation (primaryAction == 1) harms the plaintiff.
            double injury = acts.PrimaryAction == 1
                ? CostToPlaintiffOfAppropriation
                : 0.0;

            return (precaution, injury);
        }


        public double[] GetLiabilityStrengthProbabilities(LitigGameDefinition myGameDefinition, LitigGameDisputeGeneratorActions disputeGeneratorActions)
        {
            if (disputeGeneratorActions.PrimaryAction == 2)
                return ProbabilityLiabilityStrength_TrulyNotLiable;
            return ProbabilityLiabilityStrengthForDetectabilityLevel_TrulyLiable[disputeGeneratorActions.PrePrimaryChanceAction - 1];
        }

        public double[] GetDamagesStrengthProbabilities(LitigGameDefinition myGameDefinition, LitigGameDisputeGeneratorActions disputeGeneratorActions) => new double[] { 1.0 };

        public bool IsTrulyLiable(LitigGameDefinition myGameDefinition, LitigGameDisputeGeneratorActions disputeGeneratorActions, GameProgress gameProgress) => disputeGeneratorActions.PrimaryAction == 1;

        public bool PotentialDisputeArises(LitigGameDefinition myGameDefinition, LitigGameDisputeGeneratorActions disputeGeneratorActions) => true;

        public bool MarkComplete(LitigGameDefinition myGameDefinition, byte prePrimaryAction, byte primaryAction) => throw new NotImplementedException();
        public bool MarkComplete(LitigGameDefinition myGameDefinition, byte prePrimaryAction, byte primaryAction, byte postPrimaryAction) => throw new NotImplementedException();
        public double[] GetPrePrimaryChanceProbabilities(LitigGameDefinition myGameDefinition) => throw new NotImplementedException(); // even probabilities are selected, so we don't need to specify
        public double[] GetPostPrimaryChanceProbabilities(LitigGameDefinition myGameDefinition, LitigGameDisputeGeneratorActions disputeGeneratorActions) => throw new NotImplementedException();
        public bool PostPrimaryDoesNotAffectStrategy() => false;
    }
}
