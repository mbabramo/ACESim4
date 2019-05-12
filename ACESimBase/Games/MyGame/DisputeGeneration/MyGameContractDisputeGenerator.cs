using ACESim.Util;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESim
{
    [Serializable]
    public class MyGameContractDisputeGenerator : IMyGameDisputeGenerator
    {
        public string GetGeneratorName() => "Contract";
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
        public double StdevNoiseToProduceLitigationQuality = 0.2; 

        private double[][] ProbabilityLitigationQuality;

        private double BenefitOfActionToDefendant_Proportion(byte benefitLevel)
        {
            return EquallySpaced.GetLocationOfEquallySpacedPoint(benefitLevel - 1, NumBenefitLevels, true);
        }

        private double BenefitOfActionToDefendant_Level(byte benefitLevel)
        {
            return MinBenefitOfActionToDefendant + (MaxBenefitOfActionToDefendant - MinBenefitOfActionToDefendant) * BenefitOfActionToDefendant_Proportion(benefitLevel);
        }

        public void Setup(MyGameDefinition myGameDefinition)
        {
            // We need to determine the probability of different litigation qualities 
            ProbabilityLitigationQuality = new double[NumBenefitLevels][];
            DiscreteValueSignalParameters dsParams = new DiscreteValueSignalParameters() { NumPointsInSourceUniformDistribution = NumBenefitLevels, NumSignals = myGameDefinition.Options.NumLitigationQualityPoints, StdevOfNormalDistribution = StdevNoiseToProduceLitigationQuality, UseEndpoints = true };
            for (byte a = 1; a <= NumBenefitLevels; a++)
            {
                // When the benefit to the defendant is low, then the plaintiff has a good claim -- defendant is only allowed to take the benefit when it is high.
                // When the benefit to the defendant is high, then the plaintiff has a bad claim.
                byte strengthOfClaimIfNoRandomness = (byte) (NumBenefitLevels - a + 1);
                ProbabilityLitigationQuality[a - 1] = DiscreteValueSignal.GetProbabilitiesOfDiscreteSignals(strengthOfClaimIfNoRandomness, dsParams);
            }
        }

        public void GetActionsSetup(MyGameDefinition myGameDefinition, out byte prePrimaryChanceActions, out byte primaryActions, out byte postPrimaryChanceActions, out byte[] prePrimaryPlayersToInform, out byte[] primaryPlayersToInform, out byte[] postPrimaryPlayersToInform, out bool prePrimaryUnevenChance, out bool postPrimaryUnevenChance, out bool litigationQualityUnevenChance, out bool primaryActionCanTerminate, out bool postPrimaryChanceCanTerminate)
        {
            prePrimaryChanceActions = NumBenefitLevels;
            primaryActions = 2;
            postPrimaryChanceActions = 0; 
            prePrimaryPlayersToInform = new byte[] {(byte) MyGamePlayers.Defendant, (byte)MyGamePlayers.QualityChance }; 
            primaryPlayersToInform = new byte[] {(byte) MyGamePlayers.Resolution, (byte)MyGamePlayers.QualityChance }; 
            postPrimaryPlayersToInform = null;
            prePrimaryUnevenChance = false; 
            postPrimaryUnevenChance = true; // irrelevant
            litigationQualityUnevenChance = true;
            primaryActionCanTerminate = true;
            postPrimaryChanceCanTerminate = false;
        }

        readonly double[] WealthEffects_NoBenefitTaken = new double[] { 0, 0 };
        public double[] GetLitigationIndependentWealthEffects(MyGameDefinition myGameDefinition, MyGameDisputeGeneratorActions disputeGeneratorActions)
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

        public double GetLitigationIndependentSocialWelfare(MyGameDefinition myGameDefinition, MyGameDisputeGeneratorActions disputeGeneratorActions)
        {
            if (disputeGeneratorActions.PrimaryAction == 2)
                return 0;
            return BenefitOfActionToDefendant_Level(disputeGeneratorActions.PrePrimaryChanceAction) - CostOfActionOnPlaintiff;
        }

        public double[] GetLitigationQualityProbabilities(MyGameDefinition myGameDefinition, MyGameDisputeGeneratorActions disputeGeneratorActions)
        {
            return ProbabilityLitigationQuality[disputeGeneratorActions.PrePrimaryChanceAction - 1];
        }

        public bool IsTrulyLiable(MyGameDefinition myGameDefinition, MyGameDisputeGeneratorActions disputeGeneratorActions, GameProgress gameProgress)
        {
            return GetLitigationIndependentSocialWelfare(myGameDefinition, disputeGeneratorActions) < 0;
        }

        public bool PotentialDisputeArises(MyGameDefinition myGameDefinition, MyGameDisputeGeneratorActions disputeGeneratorActions)
        {
            return disputeGeneratorActions.PrimaryAction == 1; // defendant has taken benefit, so there is something to sue over
        }

        public bool MarkComplete(MyGameDefinition myGameDefinition, byte prePrimaryAction, byte primaryAction)
        {
            return primaryAction == 2;
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
        public (bool unrollParallelize, bool unrollIdentical) GetPrePrimaryUnrollSettings()
        {
            return (false, false);
        }

        public (bool unrollParallelize, bool unrollIdentical) GetPrimaryUnrollSettings()
        {
            return (false, false);
        }

        public (bool unrollParallelize, bool unrollIdentical) GetPostPrimaryUnrollSettings()
        {
            return (false, false);
        }

        public (bool unrollParallelize, bool unrollIdentical) GetLitigationQualityUnrollSettings()
        {
            return (false, false);
        }
        public bool PostPrimaryDoesNotAffectStrategy() => false;
    }
}
