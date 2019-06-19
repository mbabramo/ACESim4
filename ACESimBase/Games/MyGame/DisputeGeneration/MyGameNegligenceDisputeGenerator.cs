using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESim
{
    [Serializable]
    public class MyGameNegligenceDisputeGenerator : IMyGameDisputeGenerator
    {
        public string GetGeneratorName() => "Negligence";
        // Defendant receives information that signals how quickly the marginal benefit of a precaution decreases with increased precaution level. 

        // Pre primary action chance: This determines how quickly the marginal benefit of precaution decreases. Let m = 1 - ppc / 20.0.
        // Primary action: The defendant must choose a precaution level p. Let r(p) represent the reduction in the probability of risk at that precaution level, over and above the risk reduction from lower levels of precaution. We define r(1) = 0, r(2) = 0.05, and for p > 2, r(p) = r(p-1) * m. 
        // Post primary action chance: Does an injury occur? yes = 1, no = 2

        // Cost-benefit. We calculate the cost-benefit ratio of the first precaution level NOT taken. CostBenefitRatio = IncrementalPrecautionCost / (r(p+1) * CostOfInjury). We then truncate this ratio so that it represents a number from 0.25 to 4.0, and we invert to take the benefit-cost ratio.
        // Litigation quality calculation. Litigation quality increases proportionately from the lowest benefit-cost to the highest. A low benefit-cost ratio means that the defendant declined a precaution that would have produced little benefit, so that is low litigation quality. 

        public byte NumMarginalBenefitSchedules = 10;
        public byte NumPrecautionLevels = 11;
        public double CostOfInjury = 100_000.00, ProbabilityOfInjuryNoPrecaution = 0.5, IncrementalPrecautionCost = 2_000, RiskReductionFromFirstPrecautionTaken = 0.05;

        private bool[][] ShouldBeLiable;
        private double[][] CumulativeRiskReduction;
        public double GetRiskOfInjury(byte marginalBenefitSchedule, byte precautionLevelChosen) => ProbabilityOfInjuryNoPrecaution - CumulativeRiskReduction[marginalBenefitSchedule - 1][precautionLevelChosen - 1];
        private double[][][] LiabilityStrength;

        public void Setup(MyGameDefinition myGameDefinition)
        {
            myGameDefinition.Options.DamagesMax = myGameDefinition.Options.DamagesMin = CostOfInjury;
            myGameDefinition.Options.NumDamagesStrengthPoints = 1;
            ShouldBeLiable = ArrayFormConversionExtension.CreateJaggedArray<bool[][]>(new int[] { NumMarginalBenefitSchedules, NumPrecautionLevels });
            CumulativeRiskReduction = ArrayFormConversionExtension.CreateJaggedArray<double[][]>(new int[] { NumMarginalBenefitSchedules, NumPrecautionLevels });
            LiabilityStrength = ArrayFormConversionExtension.CreateJaggedArray<double[][][]>(new int[] {NumMarginalBenefitSchedules, NumPrecautionLevels, myGameDefinition.Options.NumLiabilityStrengthPoints});
            for (int marginalBenefitSchedule = 1; marginalBenefitSchedule <= NumMarginalBenefitSchedules; marginalBenefitSchedule++)
            {
                for (int precautionLevelChosen = 1; precautionLevelChosen <= NumPrecautionLevels; precautionLevelChosen++)
                {
                    double m = 1.0 - marginalBenefitSchedule / 20.0;
                    double cumulativeRiskReduction = 0;
                    double riskRemovalAtLevel = 0;
                    for (int precautionsTakenByDefendant = 2; precautionsTakenByDefendant <= precautionLevelChosen; precautionsTakenByDefendant++)
                    {
                        if (precautionsTakenByDefendant == 2)
                            riskRemovalAtLevel = RiskReductionFromFirstPrecautionTaken;
                        else
                            riskRemovalAtLevel *= m;
                        cumulativeRiskReduction += riskRemovalAtLevel;
                    }
                    CumulativeRiskReduction[marginalBenefitSchedule - 1][precautionLevelChosen - 1] = cumulativeRiskReduction;
                    double riskRemovalForegone = cumulativeRiskReduction == 0 ? RiskReductionFromFirstPrecautionTaken : riskRemovalAtLevel * m;
                    double riskRemovalForegoneDollars = riskRemovalForegone * CostOfInjury;
                    //double costBenefitRatio = IncrementalPrecautionCost / riskRemovalForegoneDollars;
                    double benefitCostRatio = riskRemovalForegoneDollars / IncrementalPrecautionCost;
                    if (benefitCostRatio < 0.25)
                        benefitCostRatio = 0.25;
                    else if (benefitCostRatio > 4.0)
                        benefitCostRatio = 4.0;
                    ShouldBeLiable[marginalBenefitSchedule - 1][precautionLevelChosen - 1] = benefitCostRatio > 1.0;
                    // (LiabilityStrength - 1) / (NumLiabilityStrengthLevels - 1) = (benefitCostRatio - 0.25) / (4 - 0.25)
                    double litigationQuality = (benefitCostRatio - 0.25) * (myGameDefinition.Options.NumLiabilityStrengthPoints - 1.0) / (4 - 0.25) + 1.0;
                    byte litigationQualityDiscrete = (byte) Math.Round(litigationQuality);
                    for (int litigationQualityLevel = 1; litigationQualityLevel <= myGameDefinition.Options.NumLiabilityStrengthPoints; litigationQualityLevel++)
                    {
                        double probabilityOfLiabilityStrength;
                        if (litigationQualityDiscrete == litigationQualityLevel)
                            probabilityOfLiabilityStrength = 1.0;
                        else
                            probabilityOfLiabilityStrength = 0.0;
                        LiabilityStrength[marginalBenefitSchedule - 1][precautionLevelChosen - 1][litigationQualityLevel - 1] = probabilityOfLiabilityStrength;
                    }
                }
            }
        }

        public void GetActionsSetup(MyGameDefinition myGameDefinition, out byte prePrimaryChanceActions, out byte primaryActions, out byte postPrimaryChanceActions, out byte[] prePrimaryPlayersToInform, out byte[] primaryPlayersToInform, out byte[] postPrimaryPlayersToInform, out bool prePrimaryUnevenChance, out bool postPrimaryUnevenChance, out bool litigationQualityUnevenChance, out bool primaryActionCanTerminate, out bool postPrimaryChanceCanTerminate)
        {
            prePrimaryChanceActions = NumMarginalBenefitSchedules;
            primaryActions = NumPrecautionLevels;
            postPrimaryChanceActions = 2; // 1 = injury occurs; 2 = no injury
            prePrimaryPlayersToInform = new byte[] { (byte)MyGamePlayers.Resolution, (byte)MyGamePlayers.Defendant, (byte)MyGamePlayers.PostPrimaryChance, (byte)MyGamePlayers.LiabilityStrengthChance };
            primaryPlayersToInform = new byte[] { (byte)MyGamePlayers.Resolution, (byte)MyGamePlayers.PostPrimaryChance, (byte)MyGamePlayers.LiabilityStrengthChance };
            postPrimaryPlayersToInform = new byte[] { (byte)MyGamePlayers.Resolution };
            prePrimaryUnevenChance = false;
            postPrimaryUnevenChance = true; // i.e., chance of injury
            litigationQualityUnevenChance = true;
            primaryActionCanTerminate = false;
            postPrimaryChanceCanTerminate = true;
        }
        
        public double[] GetLitigationIndependentWealthEffects(MyGameDefinition myGameDefinition, MyGameDisputeGeneratorActions disputeGeneratorActions)
        {
            double precautionSpent = (disputeGeneratorActions.PrimaryAction - 1) * IncrementalPrecautionCost;
            bool injuryOccurs = disputeGeneratorActions.PostPrimaryChanceAction == 2;
            if (injuryOccurs)
                return new double[] { 0 - CostOfInjury, 0 - precautionSpent };
            else
                return new double[] { 0, 0 - precautionSpent };
        }

        public double GetLitigationIndependentSocialWelfare(MyGameDefinition myGameDefinition, MyGameDisputeGeneratorActions disputeGeneratorActions)
        {
            double precautionSpent = (disputeGeneratorActions.PrimaryAction - 1) * IncrementalPrecautionCost;
            bool injuryOccurs = disputeGeneratorActions.PostPrimaryChanceAction == 1;
            double socialWelfare = 0;
            if (injuryOccurs)
                socialWelfare = 0 - CostOfInjury - precautionSpent;
            else
                socialWelfare = 0 - precautionSpent;
            return socialWelfare;
        }

        public bool PotentialDisputeArises(MyGameDefinition myGameDefinition, MyGameDisputeGeneratorActions disputeGeneratorActions)
        {
            return disputeGeneratorActions.PostPrimaryChanceAction == 1; // only a dispute if an injury occurs
        }

        public double[] GetLiabilityStrengthProbabilities(MyGameDefinition myGameDefinition, MyGameDisputeGeneratorActions disputeGeneratorActions)
        {
            byte marginalBenefitSchedule = disputeGeneratorActions.PrePrimaryChanceAction;
            byte precautionLevelChosen = disputeGeneratorActions.PrimaryAction;
            return LiabilityStrength[marginalBenefitSchedule - 1][precautionLevelChosen - 1];
        }

        public double[] GetDamagesStrengthProbabilities(MyGameDefinition myGameDefinition, MyGameDisputeGeneratorActions disputeGeneratorActions) => new double[] { 1.0 };

        public bool IsTrulyLiable(MyGameDefinition myGameDefinition, MyGameDisputeGeneratorActions disputeGeneratorActions, GameProgress gameProgress)
        {
            byte marginalBenefitSchedule = disputeGeneratorActions.PrePrimaryChanceAction;
            byte precautionLevelChosen = disputeGeneratorActions.PrimaryAction;
            if (precautionLevelChosen == NumPrecautionLevels)
                return false; // we assume that no higher precaution level is possible, so no liability should attach
            bool returnVal = ShouldBeLiable[marginalBenefitSchedule - 1][precautionLevelChosen - 1];
            return returnVal;
        }

        public bool MarkComplete(MyGameDefinition myGameDefinition, byte prePrimaryAction, byte primaryAction)
        {
            throw new NotImplementedException();
        }

        public bool MarkComplete(MyGameDefinition myGameDefinition, byte prePrimaryAction, byte primaryAction, byte postPrimaryAction)
        {
            return postPrimaryAction == 2; // no injury occurs
        }

        public double[] GetPrePrimaryChanceProbabilities(MyGameDefinition myGameDefinition)
        {
            throw new NotImplementedException(); // no pre primary chance function
        }

        public double[] GetPostPrimaryChanceProbabilities(MyGameDefinition myGameDefinition, MyGameDisputeGeneratorActions disputeGeneratorActions)
        {
            byte marginalBenefitSchedule = disputeGeneratorActions.PrePrimaryChanceAction;
            byte precautionLevelChosen = disputeGeneratorActions.PrimaryAction;
            double riskOfInjury = GetRiskOfInjury(marginalBenefitSchedule, precautionLevelChosen);
            if (marginalBenefitSchedule == 1)
            {
                Debug.WriteLine($"Precaution level {precautionLevelChosen} Risk of injury {riskOfInjury}");
            }
            return new double[] {riskOfInjury, 1.0 - riskOfInjury};
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

        public (bool unrollParallelize, bool unrollIdentical) GetLiabilityStrengthUnrollSettings()
        {
            return (false, false);
        }

        public (bool unrollParallelize, bool unrollIdentical) GetDamagesStrengthUnrollSettings()
        {
            return (false, false);
        }

        public bool PostPrimaryDoesNotAffectStrategy() => false;
    }
}
