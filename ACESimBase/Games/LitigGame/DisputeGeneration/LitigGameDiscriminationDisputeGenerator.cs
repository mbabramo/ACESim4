using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESim
{
    [Serializable]
    public class LitigGameDiscriminationDisputeGenerator : LitigGameStandardDisputeGeneratorBase
    {
        public string GetGeneratorName() => "Discrimination";

        public string OptionsString => $"ProbabilityGoodEmployee {ProbabilityGoodEmployee} ProbabilityGoodEmployer {ProbabilityGoodEmployer} ProbabilityLowTasteDiscrimination {ProbabilityLowTasteDiscrimination} ProbabilityMediumTasteDiscrimination {ProbabilityMediumTasteDiscrimination} ProbabilityHighTasteDiscrimination {ProbabilityHighTasteDiscrimination} CostToEmployeeOfBeingFired {CostToEmployeeOfBeingFired} SocialCostOfFiringGoodEmployee {SocialCostOfFiringGoodEmployee} CostOfLeavingBadEmployee {CostOfLeavingBadEmployee} PrivateBenefitToBadEmployerFromFiring_LowTaste {PrivateBenefitToBadEmployerFromFiring_LowTaste} PrivateBenefitToBadEmployerFromFiring_MediumTaste {PrivateBenefitToBadEmployerFromFiring_MediumTaste} PrivateBenefitToBadEmployerFromFiring_HighTaste {PrivateBenefitToBadEmployerFromFiring_HighTaste}";
        public (string name, string abbreviation) PrePrimaryNameAndAbbreviation => ("Good or Bad Employer and Employee", "PeopleQuality");
        public (string name, string abbreviation) PrimaryNameAndAbbreviation => ("Fire", "Primary");
        public (string name, string abbreviation) PostPrimaryNameAndAbbreviation => ("PostPrimaryChanceActions", "Post Primary");
        public string GetActionString(byte action, byte decisionByteCode)
        {
            return action.ToString();
        }

        // Defendant businessperson must decide whether to fire employee. Defendant may or may not have a taste for racism. Plaintiff may or may not be a good worker. We assume that there is a social cost when a good employee is fired, and a social cost when a bad employee is not fired. Our goal is thus to make the employer with a taste for discrimination act as much as possible as an employer without such a taste would act in the absence of a legal regime.
        // The plaintiff's case will be strongest when the employee is good and the employer is bad. Let's make this a 7 on average. For each factor that changes in the opposite direction, subtract 2 points. So we have a litigation quality of 3 on average when the employee is bad and the employer is good. Then, we'll evenly distribute plus or minus 2 points from there.

        // Pre primary action chance: Determines whether the employee is a good worker or a bad worker and whether the employer is good or bad.
        // Primary action: Fire (yes = 1, no = 2).
        // Post primary action: None.

        public double ProbabilityBadEmployee = 0.25;
        public double ProbabilityGoodEmployee => 1.0 - ProbabilityBadEmployee;
        public double ProbabilityGoodEmployer = 0.75;
        public double ProbabilityBadEmployer => 1.0 - ProbabilityGoodEmployer;
        public double ProbabilityLowTasteDiscrimination => ProbabilityBadEmployer / 3.0;
        public double ProbabilityMediumTasteDiscrimination => ProbabilityBadEmployer / 3.0;
        public double ProbabilityHighTasteDiscrimination => ProbabilityBadEmployer / 3.0;
        public double CostToEmployeeOfBeingFired = 1.0;
        public double SocialCostOfFiringGoodEmployee = 1.0; // if > CostToEmployeeOfBeingFired, social harms extend beyond harmed employee
        public double CostOfLeavingBadEmployee = 1.0;
        public double PrivateBenefitToBadEmployerFromFiring_LowTaste = 0.25;
        public double PrivateBenefitToBadEmployerFromFiring_MediumTaste = 0.5;
        public double PrivateBenefitToBadEmployerFromFiring_HighTaste = 0.75;

        private double[] ProbabilityLiabilityStrength_StrongPlaintiffCase, ProbabilityLiabilityStrength_MediumPlaintiffCase, ProbabilityLiabilityStrength_PoorPlaintiffCase;

        public LitigGameDefinition LitigGameDefinition { get; set; }
        public void Setup(LitigGameDefinition myGameDefinition)
        {
            LitigGameDefinition = myGameDefinition;
            myGameDefinition.Options.NumLiabilityStrengthPoints = 9; // always
            myGameDefinition.Options.NumDamagesStrengthPoints = 1;
            ProbabilityLiabilityStrength_StrongPlaintiffCase = new double[] {0.0, 0.0, 0.0, 0.0, 0.2, 0.2, 0.2, 0.2, 0.2,};
            ProbabilityLiabilityStrength_MediumPlaintiffCase = new double[] { 0.0, 0.0, 0.2, 0.2, 0.2, 0.2, 0.2, 0.0, 0.0 };
            ProbabilityLiabilityStrength_PoorPlaintiffCase = new double[] { 0.2, 0.2, 0.2, 0.2, 0.2, 0.0, 0.0, 0.0, 0.0 };
        }

        public (bool badEmployee, double employerTasteForDiscrimination) ConvertPrePrimaryChance(byte prePrimaryChance)
        {
            switch (prePrimaryChance)
            {
                case 1:
                    return (false, 0);
                case 2:
                    return (false, PrivateBenefitToBadEmployerFromFiring_LowTaste);
                case 3:
                    return (false, PrivateBenefitToBadEmployerFromFiring_MediumTaste);
                case 4:
                    return (false, PrivateBenefitToBadEmployerFromFiring_HighTaste);
                case 5:
                    return (true, 0);
                case 6:
                    return (true, PrivateBenefitToBadEmployerFromFiring_LowTaste);
                case 7:
                    return (true, PrivateBenefitToBadEmployerFromFiring_MediumTaste);
                case 8:
                    return (true, PrivateBenefitToBadEmployerFromFiring_HighTaste);
                default:
                    throw new NotImplementedException();
            }
        }

        public void GetActionsSetup(LitigGameDefinition myGameDefinition, out byte prePrimaryChanceActions, out byte primaryActions, out byte postPrimaryChanceActions, out byte[] prePrimaryPlayersToInform, out byte[] primaryPlayersToInform, out byte[] postPrimaryPlayersToInform, out bool prePrimaryUnevenChance, out bool postPrimaryUnevenChance, out bool litigationQualityUnevenChance, out bool primaryActionCanTerminate, out bool postPrimaryChanceCanTerminate)
        {
            prePrimaryChanceActions = 8;
            primaryActions = 2;
            postPrimaryChanceActions = 0;
            prePrimaryPlayersToInform = new byte[] { (byte)LitigGamePlayers.Resolution, (byte)LitigGamePlayers.Defendant, (byte)LitigGamePlayers.LiabilityStrengthChance };
            primaryPlayersToInform = new byte[] { (byte)LitigGamePlayers.Resolution, (byte)LitigGamePlayers.LiabilityStrengthChance };
            postPrimaryPlayersToInform = null;
            prePrimaryUnevenChance = true;
            postPrimaryUnevenChance = true; // irrelevant
            litigationQualityUnevenChance = true;
            primaryActionCanTerminate = true;
            postPrimaryChanceCanTerminate = false;
        }

        readonly double[] WealthEffects_NoAppropriation = new double[] { 0, 0 };
        public double[] GetLitigationIndependentWealthEffects(LitigGameDefinition myGameDefinition, LitigGameDisputeGeneratorActions disputeGeneratorActions)
        {
            var peopleStatus = ConvertPrePrimaryChance(disputeGeneratorActions.PrePrimaryChanceAction);
            if (disputeGeneratorActions.PrimaryAction == 1) 
            { // fired
                if (peopleStatus.badEmployee)
                    return new double[] { 0 - CostToEmployeeOfBeingFired, peopleStatus.employerTasteForDiscrimination};
                else
                    return new double[] { 0 - CostToEmployeeOfBeingFired, peopleStatus.employerTasteForDiscrimination };
            }
            else
            { // not fired
                if (peopleStatus.badEmployee)
                    return new double[] { 0, 0 - CostOfLeavingBadEmployee };
                else
                    return new double[] { 0, 0 }; // a good employer gets no benefit or cost from firing/not firing a good employee, but shouldn't want to do so as a result of litigation costs
            }
        }

        public double GetLitigationIndependentSocialWelfare(LitigGameDefinition myGameDefinition, LitigGameDisputeGeneratorActions disputeGeneratorActions)
        {
            var peopleStatus = ConvertPrePrimaryChance(disputeGeneratorActions.PrePrimaryChanceAction);
            if (disputeGeneratorActions.PrimaryAction == 1)
            { // fired
                if (peopleStatus.badEmployee)
                    return 0;
                else
                    return 0 - SocialCostOfFiringGoodEmployee;
            }
            else
            { // not fired
                if (peopleStatus.badEmployee)
                    return 0 - CostOfLeavingBadEmployee;
                else
                    return 0;
            }
        }

        public (double opportunityCost, double harmCost) GetOpportunityAndHarmCosts(LitigGameDefinition gameDef,
                               LitigGameDisputeGeneratorActions acts)
        {
            var status = ConvertPrePrimaryChance(acts.PrePrimaryChanceAction);
            bool fired = acts.PrimaryAction == 1;
            double opportunity = 0.0;
            double harm = 0.0;
            if (fired)
            {
                harm += CostToEmployeeOfBeingFired;
                if (!status.badEmployee)
                    harm = SocialCostOfFiringGoodEmployee;
            }
            else
            {
                if (status.badEmployee)
                    opportunity = CostOfLeavingBadEmployee;
            }
            return (opportunity, harm);
        }

        public bool PotentialDisputeArises(LitigGameDefinition myGameDefinition, LitigGameDisputeGeneratorActions disputeGeneratorActions)
        {
            return disputeGeneratorActions.PrimaryAction == 1; // only a dispute if employee is fired
        }

        public double[] GetLiabilityStrengthProbabilities(LitigGameDefinition myGameDefinition, LitigGameDisputeGeneratorActions disputeGeneratorActions)
        {
            var peopleStatus = ConvertPrePrimaryChance(disputeGeneratorActions.PrePrimaryChanceAction);
            if (peopleStatus.badEmployee == false && peopleStatus.employerTasteForDiscrimination > 0)
                return ProbabilityLiabilityStrength_StrongPlaintiffCase;
            else if (peopleStatus.badEmployee == true && peopleStatus.employerTasteForDiscrimination == 0)
                return ProbabilityLiabilityStrength_PoorPlaintiffCase; // no reason that this should arise -- since employer gets nothing out of firing, it can avoid this scenario altogether
            else 
                return ProbabilityLiabilityStrength_MediumPlaintiffCase;
        }

        public double[] GetDamagesStrengthProbabilities(LitigGameDefinition myGameDefinition, LitigGameDisputeGeneratorActions disputeGeneratorActions) => new double[] { 1.0 };

        public bool IsTrulyLiable(LitigGameDefinition myGameDefinition, LitigGameDisputeGeneratorActions disputeGeneratorActions, GameProgress gameProgress)
        {
            var peopleStatus = ConvertPrePrimaryChance(disputeGeneratorActions.PrePrimaryChanceAction);
            return disputeGeneratorActions.PrimaryAction == 1 /* fired */ && peopleStatus.badEmployee == false;
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
            return new double[] { ProbabilityGoodEmployee * ProbabilityGoodEmployer, ProbabilityGoodEmployee * ProbabilityLowTasteDiscrimination, ProbabilityGoodEmployee * ProbabilityMediumTasteDiscrimination, ProbabilityGoodEmployee * ProbabilityHighTasteDiscrimination, ProbabilityBadEmployee * ProbabilityGoodEmployer, ProbabilityBadEmployee * ProbabilityLowTasteDiscrimination, ProbabilityBadEmployee * ProbabilityMediumTasteDiscrimination, ProbabilityBadEmployee * ProbabilityHighTasteDiscrimination };
        }

        public double[] GetPostPrimaryChanceProbabilities(LitigGameDefinition myGameDefinition, LitigGameDisputeGeneratorActions disputeGeneratorActions)
        {
            throw new NotImplementedException(); // no post primary chance function
        }
        public bool PostPrimaryDoesNotAffectStrategy() => false;
    }
}
