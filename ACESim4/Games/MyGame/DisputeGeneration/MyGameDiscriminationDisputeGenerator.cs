using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESim
{
    class MyGameDiscriminationDisputeGenerator : IMyGameDisputeGenerator
    {
        // Defendant businessperson must decide whether to fire employee. Defendant may or may not have a taste for racism. Plaintiff may or may not be a good worker. We assume that there is a social cost when a good employee is fired, and a social cost when a bad employee is not fired. Our goal is thus to make the employer with a taste for discrination act as much as possible as an employer without such a taste would act in the absence of a legal regime.
        // The plaintiff's case will be strongest when the employee is good and the employer is bad. Let's make this a 7 on average. For each factor that changes in the opposite direction, subtract 2 points. So we have a litigation quality of 3 on average when the employee is bad and the employer is good. Then, we'll evenly distribute plus or minus 2 points from there.

        // Pre primary action chance: Determines whether the employee is a good worker or a bad worker and whether the employer is good or bad.
        // Primary action: Fire (yes = 1, no = 2).
        // Post primary action: None.

        public double ProbabilityBadEmployee = 0.5;
        public double ProbabilityGoodEmployee => 1.0 - ProbabilityBadEmployee;
        public double ProbabilityBadEmployer = 0.5;
        public double ProbabilityGoodEmployer => 1.0 - ProbabilityBadEmployer;
        public double CostToEmployeeOfBeingFired = 100_000;
        public double SocialCostOfFiringGoodEmployee = 100_000; // social harms extend beyond harmed employee
        public double CostOfLeavingBadEmployee = 100_000;
        public double PrivateBenefitToBadEmployerFromFiring = 85000;

        private double[] ProbabilityLitigationQuality_StrongPlaintiffCase, ProbabilityLitigationQuality_MediumPlaintiffCase, ProbabilityLitigationQuality_PoorPlaintiffCase;

        public void Setup(MyGameDefinition myGameDefinition)
        {
            myGameDefinition.Options.NumLitigationQualityPoints = 9; // always
            ProbabilityLitigationQuality_StrongPlaintiffCase = new double[] {0.0, 0.0, 0.0, 0.0, 0.2, 0.2, 0.2, 0.2, 0.2,};
            ProbabilityLitigationQuality_MediumPlaintiffCase = new double[] { 0.0, 0.0, 0.2, 0.2, 0.2, 0.2, 0.2, 0.0, 0.0 };
            ProbabilityLitigationQuality_PoorPlaintiffCase = new double[] { 0.2, 0.2, 0.2, 0.2, 0.2, 0.0, 0.0, 0.0, 0.0 };
        }

        public (bool badEmployee, bool badEmployer) ConvertPrePrimaryChance(byte prePrimaryChance)
        {
            switch (prePrimaryChance)
            {
                case 1:
                    return (false, false);
                case 2:
                    return (false, true);
                case 3:
                    return (true, false);
                case 4:
                    return (true, true);
                default:
                    throw new NotImplementedException();
            }
        }

        public void GetActionsSetup(MyGameDefinition myGameDefinition, out byte prePrimaryChanceActions, out byte primaryActions, out byte postPrimaryChanceActions, out byte[] prePrimaryPlayersToInform, out byte[] primaryPlayersToInform, out byte[] postPrimaryPlayersToInform, out bool prePrimaryUnevenChance, out bool postPrimaryUnevenChance, out bool litigationQualityUnevenChance, out bool primaryActionCanTerminate, out bool postPrimaryChanceCanTerminate)
        {
            prePrimaryChanceActions = 4;
            primaryActions = 2;
            postPrimaryChanceActions = 0;
            prePrimaryPlayersToInform = new byte[] { (byte)MyGamePlayers.Resolution, (byte)MyGamePlayers.Defendant, (byte)MyGamePlayers.QualityChance };
            primaryPlayersToInform = new byte[] { (byte)MyGamePlayers.Resolution, (byte)MyGamePlayers.QualityChance };
            postPrimaryPlayersToInform = null;
            prePrimaryUnevenChance = true;
            postPrimaryUnevenChance = true; // irrelevant
            litigationQualityUnevenChance = true;
            primaryActionCanTerminate = true;
            postPrimaryChanceCanTerminate = false;
        }

        readonly double[] WealthEffects_NoAppropriation = new double[] { 0, 0 };
        public double[] GetLitigationIndependentWealthEffects(MyGameDefinition myGameDefinition, MyGameDisputeGeneratorActions disputeGeneratorActions)
        {
            var peopleStatus = ConvertPrePrimaryChance(disputeGeneratorActions.PrePrimaryChanceAction);
            if (disputeGeneratorActions.PrimaryAction == 1) 
            { // fired
                if (peopleStatus.badEmployee)
                    return new double[] { 0 - CostToEmployeeOfBeingFired, peopleStatus.badEmployer ? PrivateBenefitToBadEmployerFromFiring : 0};
                else
                    return new double[] { 0 - CostToEmployeeOfBeingFired, peopleStatus.badEmployer ? PrivateBenefitToBadEmployerFromFiring : 0 };
            }
            else
            { // not fired
                if (peopleStatus.badEmployee)
                    return new double[] { 0, 0 - CostOfLeavingBadEmployee };
                else
                    return new double[] { 0, 0 }; // a good employer gets no benefit or cost from firing/not firing a good employee, but shouldn't want to do so as a result of litigation costs
            }
        }

        public double GetLitigationIndependentSocialWelfare(MyGameDefinition myGameDefinition, MyGameDisputeGeneratorActions disputeGeneratorActions)
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

        public bool PotentialDisputeArises(MyGameDefinition myGameDefinition, MyGameDisputeGeneratorActions disputeGeneratorActions)
        {
            return disputeGeneratorActions.PrimaryAction == 1; // only a dispute if employee is fired
        }

        public double[] GetLitigationQualityProbabilities(MyGameDefinition myGameDefinition, MyGameDisputeGeneratorActions disputeGeneratorActions)
        {
            var peopleStatus = ConvertPrePrimaryChance(disputeGeneratorActions.PrePrimaryChanceAction);
            if (peopleStatus.badEmployee == false && peopleStatus.badEmployer == true)
                return ProbabilityLitigationQuality_StrongPlaintiffCase;
            else if (peopleStatus.badEmployee == true && peopleStatus.badEmployer == false)
                return ProbabilityLitigationQuality_PoorPlaintiffCase; // no reason that this should arise -- since employer gets nothing out of firing, it can avoid this scenario altogether
            else 
                return ProbabilityLitigationQuality_MediumPlaintiffCase;
        }

        public bool IsTrulyLiable(MyGameDefinition myGameDefinition, MyGameDisputeGeneratorActions disputeGeneratorActions, GameProgress gameProgress)
        {
            var peopleStatus = ConvertPrePrimaryChance(disputeGeneratorActions.PrePrimaryChanceAction);
            return disputeGeneratorActions.PrimaryAction == 1 /* fired */ && peopleStatus.badEmployee == false;
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
            return new double[] { ProbabilityGoodEmployee * ProbabilityGoodEmployer, ProbabilityGoodEmployee * ProbabilityBadEmployer, ProbabilityBadEmployee * ProbabilityGoodEmployer, ProbabilityBadEmployee * ProbabilityBadEmployer };
        }

        public double[] GetPostPrimaryChanceProbabilities(MyGameDefinition myGameDefinition, MyGameDisputeGeneratorActions disputeGeneratorActions)
        {
            throw new NotImplementedException(); // no post primary chance function
        }
    }
}
