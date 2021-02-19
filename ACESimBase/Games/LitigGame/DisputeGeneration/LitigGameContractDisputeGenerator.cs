using ACESim.Util;
using ACESim.Util.DiscreteProbabilities;
using ACESimBase.Util;
using ACESimBase.Util.DiscreteProbabilities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESim
{
    [Serializable]
    public class LitigGameContractDisputeGenerator : ILitigGameDisputeGenerator
    {
        public string GetGeneratorName() => "Contract";


        public (string name, string abbreviation) PrePrimaryNameAndAbbreviation => ("Benefit to D", "BenefitD");
        public (string name, string abbreviation) PrimaryNameAndAbbreviation => ("Take Benefit", "TakeBenefit");
        public (string name, string abbreviation) PostPrimaryNameAndAbbreviation => ("PostPrimaryChanceActions", "Post Primary");
        public string GetActionString(byte action, byte decisionByteCode)
        {
            return action.ToString();
        }

        // Explanation: Potential defendant has a binary choice to take a certain action that will produce some benefit for itself and a standard cost for its opponent. The postulated contractual rule is that the defendant may take the action, without paying compensation, if the benefit is at least as great as the cost; that is, the contract is written to allow actions that increase joint welfare. The litigation quality should on average be in the middle of the range when benefit is equal to cost and more favorable to the plaintiff when there is a lower benefit. However, the actual litigation quality may vary somewhat from this level, as a noise term will be added. We assume for simplicity that at the time it makes its decision, the defendant has no separate estimate of litigation quality other than the benefit that it knows that it will receive.
        // Pre-primary decision: Benefit to defendant.
        // Primary decision: Take the benefit? (1 = yes, 2 = no)
        // Post-primary decision: None.
        // Litigation quality decision: A noise drawn from a distribution is added to the litigation quality to produce a litigation quality signal. Thus, there is a distribution of litigation quality signals based on all possible combinations of potential benefit (whether or not defendant actually takes it) and the noise. Thus, given the expected litigation quality, we must determine the probability of each litigation quality level. 

        // Another possibility: We could implement strict liability. E.g., the cost to the plaintiff could vary. Then, there may be disagreement about damages. But we can't do that until we implement varying damages. 

        public byte NumBenefitLevels = 5; // since we are using endpoints, an even number means we won't have a benefit level at the exact midpoint
        public double MinBenefitOfActionToDefendant = 0;
        public double MaxBenefitOfActionToDefendant = 200000;
        public double CostOfActionOnPlaintiff = 100_000;
        public double StdevNoiseToProduceLiabilityStrength = 0.2; 

        private double[][] ProbabilityLiabilityStrength;

        private double BenefitOfActionToDefendant_Proportion(byte benefitLevel)
        {
            return EquallySpaced.GetLocationOfEquallySpacedPoint(benefitLevel - 1, NumBenefitLevels, true);
        }

        private double BenefitOfActionToDefendant_Level(byte benefitLevel)
        {
            return MinBenefitOfActionToDefendant + (MaxBenefitOfActionToDefendant - MinBenefitOfActionToDefendant) * BenefitOfActionToDefendant_Proportion(benefitLevel);
        }

        public LitigGameDefinition LitigGameDefinition { get; set; }
        public void Setup(LitigGameDefinition myGameDefinition)
        {
            LitigGameDefinition = myGameDefinition;
            // We need to determine the probability of different liability strengths 
            ProbabilityLiabilityStrength = new double[NumBenefitLevels][];
            DiscreteValueSignalParameters dsParams = new DiscreteValueSignalParameters() { NumPointsInSourceUniformDistribution = NumBenefitLevels, NumSignals = myGameDefinition.Options.NumLiabilityStrengthPoints, StdevOfNormalDistribution = StdevNoiseToProduceLiabilityStrength, SourcePointsIncludeExtremes = true };
            for (byte a = 1; a <= NumBenefitLevels; a++)
            {
                // When the benefit to the defendant is low, then the plaintiff has a good claim -- defendant is only allowed to take the benefit when it is high.
                // When the benefit to the defendant is high, then the plaintiff has a bad claim.
                byte strengthOfClaimIfNoRandomness = (byte) (NumBenefitLevels - a + 1);
                ProbabilityLiabilityStrength[a - 1] = DiscreteValueSignal.GetProbabilitiesOfDiscreteSignals(strengthOfClaimIfNoRandomness, dsParams);
            }
        }

        public void GetActionsSetup(LitigGameDefinition myGameDefinition, out byte prePrimaryChanceActions, out byte primaryActions, out byte postPrimaryChanceActions, out byte[] prePrimaryPlayersToInform, out byte[] primaryPlayersToInform, out byte[] postPrimaryPlayersToInform, out bool prePrimaryUnevenChance, out bool postPrimaryUnevenChance, out bool litigationQualityUnevenChance, out bool primaryActionCanTerminate, out bool postPrimaryChanceCanTerminate)
        {
            prePrimaryChanceActions = NumBenefitLevels;
            primaryActions = 2;
            postPrimaryChanceActions = 0; 
            prePrimaryPlayersToInform = new byte[] {(byte) LitigGamePlayers.Defendant, (byte)LitigGamePlayers.LiabilityStrengthChance }; 
            primaryPlayersToInform = new byte[] {(byte) LitigGamePlayers.Resolution, (byte)LitigGamePlayers.LiabilityStrengthChance }; 
            postPrimaryPlayersToInform = null;
            prePrimaryUnevenChance = false; 
            postPrimaryUnevenChance = true; // irrelevant
            litigationQualityUnevenChance = true;
            primaryActionCanTerminate = true;
            postPrimaryChanceCanTerminate = false;
        }

        readonly double[] WealthEffects_NoBenefitTaken = new double[] { 0, 0 };
        public double[] GetLitigationIndependentWealthEffects(LitigGameDefinition myGameDefinition, LitigGameDisputeGeneratorActions disputeGeneratorActions)
        {
            if (disputeGeneratorActions.PrimaryAction == 2)
            {
                return WealthEffects_NoBenefitTaken; // no benefit taken; no effect on P or D
            }
            else
            {
                var wealthEffect = new double[] { -CostOfActionOnPlaintiff, BenefitOfActionToDefendant_Level(disputeGeneratorActions.PrePrimaryChanceAction) };
                return wealthEffect;
            }
        }

        public double GetLitigationIndependentSocialWelfare(LitigGameDefinition myGameDefinition, LitigGameDisputeGeneratorActions disputeGeneratorActions)
        {
            if (disputeGeneratorActions.PrimaryAction == 2)
                return 0;
            return BenefitOfActionToDefendant_Level(disputeGeneratorActions.PrePrimaryChanceAction) - CostOfActionOnPlaintiff;
        }

        public double[] GetLiabilityStrengthProbabilities(LitigGameDefinition myGameDefinition, LitigGameDisputeGeneratorActions disputeGeneratorActions)
        {
            return ProbabilityLiabilityStrength[disputeGeneratorActions.PrePrimaryChanceAction - 1];
        }

        public double[] GetDamagesStrengthProbabilities(LitigGameDefinition myGameDefinition, LitigGameDisputeGeneratorActions disputeGeneratorActions) => new double[] { 1.0 };

        public bool IsTrulyLiable(LitigGameDefinition myGameDefinition, LitigGameDisputeGeneratorActions disputeGeneratorActions, GameProgress gameProgress)
        {
            return GetLitigationIndependentSocialWelfare(myGameDefinition, disputeGeneratorActions) < 0;
        }

        public bool PotentialDisputeArises(LitigGameDefinition myGameDefinition, LitigGameDisputeGeneratorActions disputeGeneratorActions)
        {
            return disputeGeneratorActions.PrimaryAction == 1; // defendant has taken benefit, so there is something to sue over
        }

        public bool MarkComplete(LitigGameDefinition myGameDefinition, byte prePrimaryAction, byte primaryAction)
        {
            return primaryAction == 2;
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
