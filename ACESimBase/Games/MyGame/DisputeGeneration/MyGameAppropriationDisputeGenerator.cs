using ACESim.Util;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESim
{
    [Serializable]
    public class MyGameAppropriationDisputeGenerator : IMyGameDisputeGenerator
    {
        public string GetGeneratorName() => "Appropriation";

        // Defendant receives information on the extent to which actions will be reflected in litigation quality (call this systemic randomness). Defendant must determine whether to appropriate value. With the lowest systemic randomness, appropriation is highly likely to make the litigation quality the strongest possible value for the plaintiff and nonappropriation is highly likely to make the litigation quality the weakest possible value for the plaintiff. With the highest systemic randomness, each litigation quality is equally likely, regardless of whether the defendant appropriates. Regardless of the level of systemic randomness, the probability of various litigation qualities is a geometric sequence, with the "correct" value being most likely.

        // Pre primary action chance: Determines the level of systemic randomness
        // Primary action: Appropriate (yes = 1, no = 2).
        // Post primary action: None.

        public byte NumSystemicRandomnessLevels = 10;
        public double BenefitToDefendantOfAppropriation = 25000;
        public double CostToPlaintiffOfAppropriation = 50000;
        public double SocialWelfareMultiplier = 1.0;

        private double[][] ProbabilityLitigationQualityForNoiseLevel_TrulyLiable, ProbabilityLitigationQualityForNoiseLevel_TrulyNotLiable;

        public void Setup(MyGameDefinition myGameDefinition)
        {
            // We need to determine the probability of different litigation qualities 
            ProbabilityLitigationQualityForNoiseLevel_TrulyLiable = new double[NumSystemicRandomnessLevels][];
            ProbabilityLitigationQualityForNoiseLevel_TrulyNotLiable = new double[NumSystemicRandomnessLevels][];
            for (byte n = 1; n <= NumSystemicRandomnessLevels; n++)
            {
                double multiplier = (double) n / (double) NumSystemicRandomnessLevels;
                double sequenceSum = 0;
                for (byte n2 = 1; n2 <= NumSystemicRandomnessLevels; n2++)
                    sequenceSum += Math.Pow(multiplier, (n2 - 1));
                double startingValue = 1.0/sequenceSum;

                ProbabilityLitigationQualityForNoiseLevel_TrulyNotLiable[n - 1] = Enumerable.Range(0, NumSystemicRandomnessLevels).Select(y => startingValue * Math.Pow(multiplier, y)).ToArray();
                ProbabilityLitigationQualityForNoiseLevel_TrulyLiable[n - 1] = ProbabilityLitigationQualityForNoiseLevel_TrulyNotLiable[n - 1].Reverse().ToArray();
            }
        }

        public void GetActionsSetup(MyGameDefinition myGameDefinition, out byte prePrimaryChanceActions, out byte primaryActions, out byte postPrimaryChanceActions, out byte[] prePrimaryPlayersToInform, out byte[] primaryPlayersToInform, out byte[] postPrimaryPlayersToInform, out bool prePrimaryUnevenChance, out bool postPrimaryUnevenChance, out bool litigationQualityUnevenChance, out bool primaryActionCanTerminate, out bool postPrimaryChanceCanTerminate)
        {
            prePrimaryChanceActions = NumSystemicRandomnessLevels;
            primaryActions = 2;
            postPrimaryChanceActions = 0;
            prePrimaryPlayersToInform = new byte[] { (byte)MyGamePlayers.Defendant, (byte)MyGamePlayers.QualityChance };
            primaryPlayersToInform = new byte[] { (byte)MyGamePlayers.Resolution, (byte)MyGamePlayers.QualityChance };
            postPrimaryPlayersToInform = null;
            prePrimaryUnevenChance = false;
            postPrimaryUnevenChance = true; // irrelevant
            litigationQualityUnevenChance = true;
            primaryActionCanTerminate = false;
            postPrimaryChanceCanTerminate = false;
        }

        readonly double[] WealthEffects_NoAppropriation = new double[] { 0, 0 };
        public double[] GetLitigationIndependentWealthEffects(MyGameDefinition myGameDefinition, MyGameDisputeGeneratorActions disputeGeneratorActions)
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

        public double GetLitigationIndependentSocialWelfare(MyGameDefinition myGameDefinition, MyGameDisputeGeneratorActions disputeGeneratorActions)
        {
            if (disputeGeneratorActions.PrimaryAction == 2)
                return 0;
            return -CostToPlaintiffOfAppropriation * SocialWelfareMultiplier;
        }

        public double[] GetLitigationQualityProbabilities(MyGameDefinition myGameDefinition, MyGameDisputeGeneratorActions disputeGeneratorActions)
        {
            if (disputeGeneratorActions.PrimaryAction == 2)
                return ProbabilityLitigationQualityForNoiseLevel_TrulyNotLiable[disputeGeneratorActions.PrePrimaryChanceAction - 1];
            else
                return ProbabilityLitigationQualityForNoiseLevel_TrulyLiable[disputeGeneratorActions.PrePrimaryChanceAction - 1];
        }

        public bool IsTrulyLiable(MyGameDefinition myGameDefinition, MyGameDisputeGeneratorActions disputeGeneratorActions, GameProgress gameProgress)
        {
            return disputeGeneratorActions.PrimaryAction == 1;
        }

        public bool PotentialDisputeArises(MyGameDefinition myGameDefinition, MyGameDisputeGeneratorActions disputeGeneratorActions)
        {
            return true; // in this game, one can falsely be found liable
        }

        public bool MarkComplete(MyGameDefinition myGameDefinition, byte prePrimaryAction, byte primaryAction)
        {
            throw new NotImplementedException();
        }

        public bool MarkComplete(MyGameDefinition myGameDefinition, byte prePrimaryAction, byte primaryAction, byte postPrimaryAction)
        {
            throw new NotImplementedException();
        }

        public double[] GetPrePrimaryChanceProbabilities(MyGameDefinition myGameDefinition)
        {
            throw new NotImplementedException(); // even probabiltiies
        }

        public double[] GetPostPrimaryChanceProbabilities(MyGameDefinition myGameDefinition, MyGameDisputeGeneratorActions disputeGeneratorActions)
        {
            throw new NotImplementedException(); // no post primary chance function
        }
    }
}
