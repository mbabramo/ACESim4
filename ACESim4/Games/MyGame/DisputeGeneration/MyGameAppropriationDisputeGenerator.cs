using ACESim.Util;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESim
{
    public class MyGameAppropriationDisputeGenerator : IMyGameDisputeGenerator
    {
        // Defendant receives information on the noise to be added to the information about whether the defendant is truly liable. With the lowest level of noise, correct identification of the defendant's choice whether to appropriate is certain. With the highest level of noise, there is a substantial chance that one who is not liable will be thought to be liable and vice versa. Assuming that the court does not take selection effects into account (i.e., the court assumes that one is equally liable to be not liable or liable), the court will find liability if the litigation quality appears to be > 0.5.

        // Pre primary action chance: Determines the level of noise (from 0 to MaxNoise).
        // Primary action: Appropriate (yes = 1, no = 2).
        // Post primary action: None.

        public byte NumNoiseLevels = 11;
        public double MaxNoise = 0.002;
        public double BenefitToDefendantOfAppropriation = 30000;
        public double CostToPlaintiffOfAppropriation = 30000;
        public double SocialWelfareMultiplier = 1.0;

        private double[][] ProbabilityLitigationQualityForNoiseLevel_TrulyLiable, ProbabilityLitigationQualityForNoiseLevel_TrulyNotLiable;

        private double GetNoiseLevel(byte noiseLevelDiscrete)
        {
            return EquallySpaced.GetLocationOfEquallySpacedPoint(noiseLevelDiscrete - 1, NumNoiseLevels, true) * MaxNoise;
        }

        public void Setup(MyGameDefinition myGameDefinition)
        {
            // We need to determine the probability of different litigation qualities 
            ProbabilityLitigationQualityForNoiseLevel_TrulyLiable = new double[NumNoiseLevels][];
            ProbabilityLitigationQualityForNoiseLevel_TrulyNotLiable = new double[NumNoiseLevels][];
            for (byte n = 1; n <= NumNoiseLevels; n++)
            {
                double noiseLevel = GetNoiseLevel(n);
                DiscreteValueSignalParameters dsParams = new DiscreteValueSignalParameters() { NumPointsInSourceUniformDistribution = 2 /* not truly liable and truly liable */, NumSignals = myGameDefinition.Options.NumLitigationQualityPoints, StdevOfNormalDistribution = noiseLevel, UseEndpoints = true };
                if (n == 1) // no noise -- algorithm isn't perfect on this (ideally, to be fixed but this works for now)
                {
                    ProbabilityLitigationQualityForNoiseLevel_TrulyNotLiable[0] = Enumerable.Range(1, NumNoiseLevels).Select(x => x == 1 ? 1.0 : 0).ToArray();
                    ProbabilityLitigationQualityForNoiseLevel_TrulyLiable[0] = Enumerable.Range(1, NumNoiseLevels).Select(x => x == NumNoiseLevels ? 1.0 : 0).ToArray();
                }
                else
                {
                    ProbabilityLitigationQualityForNoiseLevel_TrulyNotLiable[n - 1] = DiscreteValueSignal.GetProbabilitiesOfDiscreteSignals(1, dsParams);
                    ProbabilityLitigationQualityForNoiseLevel_TrulyLiable[n - 1] = DiscreteValueSignal.GetProbabilitiesOfDiscreteSignals(2, dsParams);
                }
            }
            myGameDefinition.Options.CourtNoiseStdev = 0.001; // DEBUG
            debug // overall, this doesn't work. With signals of litigation quality, we're always putting it into 10 equal buckets, so if there is a true value, then with a low noise, we have five signals. Instead, maybe we should just say that the defendant previews what litigation quality would be. 
        }

        public void GetActionsSetup(MyGameDefinition myGameDefinition, out byte prePrimaryChanceActions, out byte primaryActions, out byte postPrimaryChanceActions, out byte[] prePrimaryPlayersToInform, out byte[] primaryPlayersToInform, out byte[] postPrimaryPlayersToInform, out bool prePrimaryUnevenChance, out bool postPrimaryUnevenChance, out bool litigationQualityUnevenChance, out bool primaryActionCanTerminate, out bool postPrimaryChanceCanTerminate)
        {
            prePrimaryChanceActions = NumNoiseLevels;
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
