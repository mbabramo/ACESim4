using ACESimBase.Util.ArrayManipulation;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESim
{
    [Serializable]
    public class LitigGameNegligencePrecautionLevelDisputeGenerator : LitigGameStandardDisputeGeneratorBase
    {
        public string GetGeneratorName() => "NegligencePrecaution";
        public string OptionsString => $"NumMarginalBenefitSchedules {NumMarginalBenefitSchedules} NumPrecautionLevels {NumPrecautionLevels} CostOfInjury {CostOfInjury} ProbabilityOfInjuryNoPrecaution {ProbabilityOfInjuryNoPrecaution} IncrementalPrecautionCost {IncrementalPrecautionCost} RiskReductionFromFirstPrecautionTaken {RiskReductionFromFirstPrecautionTaken}";
        public (string name, string abbreviation) PrePrimaryNameAndAbbreviation => ("Precaution Benefit Curve", "PrecautionBenefit");
        public (string name, string abbreviation) PrimaryNameAndAbbreviation => ("Precaution Level", "Precaution");
        public (string name, string abbreviation) PostPrimaryNameAndAbbreviation => ("Injury", "Injury");
        public string GetActionString(byte action, byte decisionByteCode)
        {
            return action.ToString();
        }

        // Defendant receives information that signals how quickly the marginal benefit of a precaution decreases with increased precaution level. 

        // Pre primary action chance: This determines how quickly the marginal benefit of precaution decreases. Let m = 1 - ppc / 20.0.
        // Primary action: The defendant must choose a precaution level p. Let r(p) represent the reduction in the probability of risk at that precaution level, over and above the risk reduction from lower levels of precaution. We define r(1) = 0 (there is no risk reduction at lowest level of precaution), r(2) = 0.05, and for p > 2, r(p) = r(p-1) * m (so total risk reduction = r(p-1)(1 + m)). 
        // Post primary action chance: Does an injury occur? yes = 1, no = 2

        // Cost-benefit. We calculate the cost-benefit ratio of the first precaution level NOT taken. CostBenefitRatio = IncrementalPrecautionCost / (r(p+1) * CostOfInjury). We then truncate this ratio so that it represents a number from 0.25 to 4.0, and we invert to take the benefit-cost ratio.
        // Litigation quality calculation. Litigation quality increases proportionately from the lowest benefit-cost to the highest. A low benefit-cost ratio means that the defendant declined a precaution that would have produced little benefit, so that is low litigation quality. 

        public byte NumMarginalBenefitSchedules = 10;
        public byte NumPrecautionLevels = 11;
        public double CostOfInjury = 1.0, ProbabilityOfInjuryNoPrecaution = 0.5, IncrementalPrecautionCost = 0.05;
        public double RiskReductionFromFirstPrecautionTaken = 0.05; // this is the risk reduction from the second possible level of precaution, since the first level corresponds to no precaution at all

        private bool[][] ShouldBeLiable;
        private double[][] CumulativeRiskReduction;
        public double GetRiskOfInjury(byte marginalBenefitSchedule, byte precautionLevelChosen) => ProbabilityOfInjuryNoPrecaution - CumulativeRiskReduction[marginalBenefitSchedule - 1][precautionLevelChosen - 1];
        private double[][][] LiabilityStrength;

        public LitigGameDefinition LitigGameDefinition { get; set; }
        public void Setup(LitigGameDefinition myGameDefinition)
        {
            LitigGameDefinition = myGameDefinition;
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
                    const double minBoundedBenefitCostRatio = 0.25, maxBoundedBenefitCostRatio = 4.0;
                    double boundedBenefitCostRatio = riskRemovalForegoneDollars / IncrementalPrecautionCost;
                    if (boundedBenefitCostRatio < minBoundedBenefitCostRatio)
                        boundedBenefitCostRatio = minBoundedBenefitCostRatio;
                    else if (boundedBenefitCostRatio > maxBoundedBenefitCostRatio)
                        boundedBenefitCostRatio = maxBoundedBenefitCostRatio;
                    ShouldBeLiable[marginalBenefitSchedule - 1][precautionLevelChosen - 1] = boundedBenefitCostRatio > 1.0;
                    // We determine liability strength based on the proportion of the range from the minimum to the maximum bounded benefit-cost ratio
                    // (LiabilityStrength - 1) / (NumLiabilityStrengthLevels - 1) = (benefitCostRatio - minBoundedBenefitCostRatio) / (maxBoundedBenefitCostRatio - minBoundedBenefitCostRatio)
                    double litigationQuality = (boundedBenefitCostRatio - minBoundedBenefitCostRatio) * (myGameDefinition.Options.NumLiabilityStrengthPoints - 1.0) / (maxBoundedBenefitCostRatio - minBoundedBenefitCostRatio) + 1.0;
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

        public void GetActionsSetup(LitigGameDefinition myGameDefinition, out byte prePrimaryChanceActions, out byte primaryActions, out byte postPrimaryChanceActions, out byte[] prePrimaryPlayersToInform, out byte[] primaryPlayersToInform, out byte[] postPrimaryPlayersToInform, out bool prePrimaryUnevenChance, out bool postPrimaryUnevenChance, out bool litigationQualityUnevenChance, out bool primaryActionCanTerminate, out bool postPrimaryChanceCanTerminate)
        {
            prePrimaryChanceActions = NumMarginalBenefitSchedules;
            primaryActions = NumPrecautionLevels;
            postPrimaryChanceActions = 2; // 1 = injury occurs; 2 = no injury
            prePrimaryPlayersToInform = new byte[] { (byte)LitigGamePlayers.Resolution, (byte)LitigGamePlayers.Defendant, (byte)LitigGamePlayers.PostPrimaryChance, (byte)LitigGamePlayers.LiabilityStrengthChance };
            primaryPlayersToInform = new byte[] { (byte)LitigGamePlayers.Resolution, (byte)LitigGamePlayers.PostPrimaryChance, (byte)LitigGamePlayers.LiabilityStrengthChance };
            postPrimaryPlayersToInform = new byte[] { (byte)LitigGamePlayers.Resolution };
            prePrimaryUnevenChance = false;
            postPrimaryUnevenChance = true; // i.e., chance of injury
            litigationQualityUnevenChance = true;
            primaryActionCanTerminate = false;
            postPrimaryChanceCanTerminate = true;
        }
        
        public double[] GetLitigationIndependentWealthEffects(LitigGameDefinition myGameDefinition, LitigGameDisputeGeneratorActions disputeGeneratorActions)
        {
            double precautionSpent = (disputeGeneratorActions.PrimaryAction - 1) * IncrementalPrecautionCost;
            bool injuryOccurs = disputeGeneratorActions.PostPrimaryChanceAction == 1;
            if (injuryOccurs)
                return new double[] { 0 - CostOfInjury, 0 - precautionSpent };
            else
                return new double[] { 0, 0 - precautionSpent };
        }

        public double GetLitigationIndependentSocialWelfare(LitigGameDefinition myGameDefinition, LitigGameDisputeGeneratorActions disputeGeneratorActions)
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

        public (double opportunityCost, double harmCost) GetOpportunityAndHarmCosts(LitigGameDefinition gameDef, LitigGameDisputeGeneratorActions acts)
        {
            double opportunity = (acts.PrimaryAction - 1) * IncrementalPrecautionCost;
            double harm = acts.PostPrimaryChanceAction == 1 ? CostOfInjury : 0.0;
            return (opportunity, harm);
        }

        public bool PotentialDisputeArises(LitigGameDefinition myGameDefinition, LitigGameDisputeGeneratorActions disputeGeneratorActions)
        {
            return disputeGeneratorActions.PostPrimaryChanceAction == 1; // only a dispute if an injury occurs
        }

        public double[] GetLiabilityStrengthProbabilities(LitigGameDefinition myGameDefinition, LitigGameDisputeGeneratorActions disputeGeneratorActions)
        {
            byte marginalBenefitSchedule = disputeGeneratorActions.PrePrimaryChanceAction;
            byte precautionLevelChosen = disputeGeneratorActions.PrimaryAction;
            return LiabilityStrength[marginalBenefitSchedule - 1][precautionLevelChosen - 1];
        }

        public double[] GetDamagesStrengthProbabilities(LitigGameDefinition myGameDefinition, LitigGameDisputeGeneratorActions disputeGeneratorActions) => new double[] { 1.0 };

        public bool IsTrulyLiable(LitigGameDefinition myGameDefinition, LitigGameDisputeGeneratorActions disputeGeneratorActions, GameProgress gameProgress)
        {
            byte marginalBenefitSchedule = disputeGeneratorActions.PrePrimaryChanceAction;
            byte precautionLevelChosen = disputeGeneratorActions.PrimaryAction;
            if (precautionLevelChosen == NumPrecautionLevels)
                return false; // we assume that no higher precaution level is possible, so no liability should attach
            bool returnVal = ShouldBeLiable[marginalBenefitSchedule - 1][precautionLevelChosen - 1];
            return returnVal;
        }

        public bool MarkComplete(LitigGameDefinition myGameDefinition, byte prePrimaryAction, byte primaryAction)
        {
            throw new NotSupportedException();
        }

        public bool MarkComplete(LitigGameDefinition myGameDefinition, byte prePrimaryAction, byte primaryAction, byte postPrimaryAction)
        {
            return postPrimaryAction == 2; // no injury occurs
        }

        public double[] GetPrePrimaryChanceProbabilities(LitigGameDefinition myGameDefinition)
        {
            throw new NotSupportedException(); // no pre primary chance function
        }

        public double[] GetPostPrimaryChanceProbabilities(LitigGameDefinition myGameDefinition, LitigGameDisputeGeneratorActions disputeGeneratorActions)
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

        public bool PostPrimaryDoesNotAffectStrategy() => false;
    }
}
