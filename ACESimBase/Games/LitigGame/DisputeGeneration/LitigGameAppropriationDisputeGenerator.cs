using ACESim.Util;
using ACESimBase.GameSolvingSupport;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESim
{
    [Serializable]
    public class LitigGameAppropriationDisputeGenerator : ILitigGameDisputeGenerator
    {
        public string GetGeneratorName() => "Appropriation";

        // Defendant receives information on the extent to which actions will be reflected in litigation quality (call this systemic randomness). Defendant must determine whether to appropriate value. With the lowest systemic randomness, appropriation is highly likely to make the litigation quality the strongest possible value for the plaintiff and nonappropriation is highly likely to make the litigation quality the weakest possible value for the plaintiff. With the highest systemic randomness, each litigation quality is equally likely, regardless of whether the defendant appropriates. Regardless of the level of systemic randomness, the probability of various litigation qualities is a geometric sequence, with the "correct" value being most likely.

        // Pre primary action chance: Determines the level of systemic randomness
        // Primary action: Appropriate (yes = 1, no = 2).
        // Post primary action: None.

        public byte NumSystemicRandomnessLevels = 5;
        public double BenefitToDefendantOfAppropriation = 0.50;
        public double CostToPlaintiffOfAppropriation = 1.0;
        public double SocialWelfareMultiplier = 1.0;
        public bool CountBenefitToDefendantInSocialWelfare = false;

        private double[][] ProbabilityLiabilityStrengthForNoiseLevel_TrulyLiable, ProbabilityLiabilityStrengthForNoiseLevel_TrulyNotLiable;

        public string OptionsString => $"NumSystemicRandomnessLevels {NumSystemicRandomnessLevels} BenefitToDefendant {BenefitToDefendantOfAppropriation} CostToPlaintiff {CostToPlaintiffOfAppropriation} SocialWelfareMultiplier {SocialWelfareMultiplier} CountBenefitToDefendantInSocialWelfare {CountBenefitToDefendantInSocialWelfare}";

        public (string name, string abbreviation) PrePrimaryNameAndAbbreviation => ("PrePrimaryChanceActions", "SystRand");
        public (string name, string abbreviation) PrimaryNameAndAbbreviation => ("PrimaryActions", "Approp");
        public (string name, string abbreviation) PostPrimaryNameAndAbbreviation => ("PostPrimaryChanceActions", "Post Primary");

        public string GetActionString(byte action, byte decisionByteCode)
        {
            return action.ToString();
        }

        public LitigGameDefinition LitigGameDefinition { get; set; }
        public void Setup(LitigGameDefinition myGameDefinition)
        {
            LitigGameDefinition = myGameDefinition;
            // We need to determine the probability of different litigation qualities 
            ProbabilityLiabilityStrengthForNoiseLevel_TrulyLiable = new double[NumSystemicRandomnessLevels][];
            ProbabilityLiabilityStrengthForNoiseLevel_TrulyNotLiable = new double[NumSystemicRandomnessLevels][];
            for (byte n = 1; n <= NumSystemicRandomnessLevels; n++)
            {
                double multiplier = (double) (n - 0.5) / (double) NumSystemicRandomnessLevels;
                double sequenceSum = 0;
                for (byte n2 = 1; n2 <= myGameDefinition.Options.NumLiabilityStrengthPoints; n2++)
                    sequenceSum += Math.Pow(multiplier, (n2 - 1));
                double startingValue = 1.0/sequenceSum;

                ProbabilityLiabilityStrengthForNoiseLevel_TrulyNotLiable[n - 1] = Enumerable.Range(0, myGameDefinition.Options.NumLiabilityStrengthPoints).Select(y => startingValue * Math.Pow(multiplier, y)).ToArray();
                ProbabilityLiabilityStrengthForNoiseLevel_TrulyLiable[n - 1] = ProbabilityLiabilityStrengthForNoiseLevel_TrulyNotLiable[n - 1].Reverse().ToArray();
            }
        }

        public void GetActionsSetup(LitigGameDefinition myGameDefinition, out byte prePrimaryChanceActions, out byte primaryActions, out byte postPrimaryChanceActions, out byte[] prePrimaryPlayersToInform, out byte[] primaryPlayersToInform, out byte[] postPrimaryPlayersToInform, out bool prePrimaryUnevenChance, out bool postPrimaryUnevenChance, out bool litigationQualityUnevenChance, out bool primaryActionCanTerminate, out bool postPrimaryChanceCanTerminate)
        {
            prePrimaryChanceActions = NumSystemicRandomnessLevels;
            primaryActions = 2;
            postPrimaryChanceActions = 0;
            prePrimaryPlayersToInform = new byte[] { (byte)LitigGamePlayers.Defendant, (byte)LitigGamePlayers.LiabilityStrengthChance };
            primaryPlayersToInform = new byte[] { (byte)LitigGamePlayers.Defendant, (byte)LitigGamePlayers.Resolution, (byte)LitigGamePlayers.LiabilityStrengthChance };
            postPrimaryPlayersToInform = null;
            prePrimaryUnevenChance = false;
            postPrimaryUnevenChance = true; // irrelevant
            litigationQualityUnevenChance = true;
            primaryActionCanTerminate = false;
            postPrimaryChanceCanTerminate = false;
        }

        readonly double[] WealthEffects_NoAppropriation = new double[] { 0, 0 };
        public double[] GetLitigationIndependentWealthEffects(LitigGameDefinition myGameDefinition, LitigGameDisputeGeneratorActions disputeGeneratorActions)
        {
            if (disputeGeneratorActions.PrimaryAction == 2)
            {
                return WealthEffects_NoAppropriation; // no benefit taken; no effect on P or D
            }
            else
            {
                var wealthEffect = new double[] { -CostToPlaintiffOfAppropriation, BenefitToDefendantOfAppropriation };
                return wealthEffect;
            }
        }

        public double GetLitigationIndependentSocialWelfare(LitigGameDefinition myGameDefinition, LitigGameDisputeGeneratorActions disputeGeneratorActions)
        {
            if (disputeGeneratorActions.PrimaryAction == 2)
                return 0;
            return -CostToPlaintiffOfAppropriation * SocialWelfareMultiplier + (CountBenefitToDefendantInSocialWelfare ? BenefitToDefendantOfAppropriation * SocialWelfareMultiplier : 0);
        }

        public double[] GetLiabilityStrengthProbabilities(LitigGameDefinition myGameDefinition, LitigGameDisputeGeneratorActions disputeGeneratorActions)
        {
            if (disputeGeneratorActions.PrimaryAction == 2)
                return ProbabilityLiabilityStrengthForNoiseLevel_TrulyNotLiable[disputeGeneratorActions.PrePrimaryChanceAction - 1];
            else
                return ProbabilityLiabilityStrengthForNoiseLevel_TrulyLiable[disputeGeneratorActions.PrePrimaryChanceAction - 1];
        }

        public double[] GetDamagesStrengthProbabilities(LitigGameDefinition myGameDefinition, LitigGameDisputeGeneratorActions disputeGeneratorActions) => new double[] { 1.0 };

        public bool IsTrulyLiable(LitigGameDefinition myGameDefinition, LitigGameDisputeGeneratorActions disputeGeneratorActions, GameProgress gameProgress)
        {
            return disputeGeneratorActions.PrimaryAction == 1;
        }

        public bool PotentialDisputeArises(LitigGameDefinition myGameDefinition, LitigGameDisputeGeneratorActions disputeGeneratorActions)
        {
            return true; // in this game, one can falsely be found liable
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
            throw new NotImplementedException(); // even probabiltiies
        }

        public double[] GetPostPrimaryChanceProbabilities(LitigGameDefinition myGameDefinition, LitigGameDisputeGeneratorActions disputeGeneratorActions)
        {
            throw new NotImplementedException(); // no post primary chance function
        }

        public bool PostPrimaryDoesNotAffectStrategy() => false;
    }
}
