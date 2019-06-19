﻿using ACESim.Util;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESim
{
    [Serializable]
    public class MyGameExogenousDisputeGenerator : IMyGameDisputeGenerator
    {
        public string GetGeneratorName() => "Exog";
        /// <summary>
        /// If litigation quality is generated from truly-liable status, and truly-liable status is exogenously determined, then the probability that the correct outcome is that the defendant truly is liable.
        /// </summary>
        public double ExogenousProbabilityTrulyLiable;
        /// <summary>
        /// If generating LiabilityLevel based on true case value, this is the standard deviation of the noise used to obscure the true litigation quality value (0 or 1). Each litigation quality level will be equally likely if there is an equal chance of either true litigation quality value.
        /// </summary>
        public double StdevNoiseToProduceLiabilityLevel;

        private double[] ProbabilityOfTrulyLiabilityValues, ProbabilitiesLiabilityLevel_TrulyNotLiable, ProbabilitiesLiabilityLevel_TrulyLiable;

        public void Setup(MyGameDefinition myGameDefinition)
        {
            ProbabilityOfTrulyLiabilityValues = new double[] { 1.0 - ExogenousProbabilityTrulyLiable, ExogenousProbabilityTrulyLiable };
            // A case is assigned a "true" value of 1 (should not be liable) or 2 (should be liable).
            // Based on the litigation quality noise parameter, we then collect a distribution of possible realized values, on the assumption
            // that the true values are equally likely. We then break this distribution into evenly sized buckets to get cutoff points.
            // Given this approach, the exogenous probability has no effect on ProbabilitiesLiabilityLevel_TrulyNotLiable and ProbabilitiesLiabilityLevel_TrulyLiable; both of these are conditional on whether a case is liable or not.
            DiscreteValueLiabilitySignalParameters dsParams = new DiscreteValueLiabilitySignalParameters() { NumPointsInSourceUniformDistribution = 2, NumLiabilitySignals = myGameDefinition.Options.NumLiabilityStrengthPoints, StdevOfNormalDistribution = StdevNoiseToProduceLiabilityLevel, UseEndpoints = true };
            ProbabilitiesLiabilityLevel_TrulyNotLiable = DiscreteValueLiabilitySignal.GetProbabilitiesOfDiscreteLiabilitySignals(1, dsParams);
            ProbabilitiesLiabilityLevel_TrulyLiable = DiscreteValueLiabilitySignal.GetProbabilitiesOfDiscreteLiabilitySignals(2, dsParams);
        }

        public void GetActionsSetup(MyGameDefinition myGameDefinition, out byte prePrimaryChanceActions, out byte primaryActions, out byte postPrimaryChanceActions, out byte[] prePrimaryPlayersToInform, out byte[] primaryPlayersToInform, out byte[] postPrimaryPlayersToInform, out bool prePrimaryUnevenChance, out bool postPrimaryUnevenChance, out bool litigationQualityUnevenChance, out bool primaryActionCanTerminate, out bool postPrimaryChanceCanTerminate)
        {
            prePrimaryChanceActions = 0;
            primaryActions = 0;
            postPrimaryChanceActions = 2; // not truly liable or truly liable
            prePrimaryPlayersToInform = null; // new byte[] {(byte) MyGamePlayers.Defendant }; // if we did have a pre-primary chance, we must notify defendant
            primaryPlayersToInform = null; // new byte[] {(byte) MyGamePlayers.Resolution}; // not relevant for this dispute generator
            postPrimaryPlayersToInform = new byte[] {(byte) MyGamePlayers.QualityChance, (byte) MyGamePlayers.Resolution};
            prePrimaryUnevenChance = true; // though not used
            postPrimaryUnevenChance = true;
            litigationQualityUnevenChance = true;
            primaryActionCanTerminate = false;
            postPrimaryChanceCanTerminate = false;
        }

        public double GetLitigationIndependentSocialWelfare(MyGameDefinition myGameDefinition, MyGameDisputeGeneratorActions disputeGeneratorActions)
        {
            return 0;
        }

        double[] NoWealthEffects = new double[] { 0, 0 };
        public double[] GetLitigationIndependentWealthEffects(MyGameDefinition myGameDefinition, MyGameDisputeGeneratorActions disputeGeneratorActions)
        {
            return NoWealthEffects; 
        }

        public double[] GetLiabilityLevelProbabilities(MyGameDefinition myGameDefinition, MyGameDisputeGeneratorActions disputeGeneratorActions)
        {
            bool isTrulyLiable = IsTrulyLiable(myGameDefinition, disputeGeneratorActions, null);
            if (isTrulyLiable)
                return ProbabilitiesLiabilityLevel_TrulyLiable;
            else
                return ProbabilitiesLiabilityLevel_TrulyNotLiable;
        }

        public bool IsTrulyLiable(MyGameDefinition myGameDefinition, MyGameDisputeGeneratorActions disputeGeneratorActions, GameProgress gameProgress)
        {
            return disputeGeneratorActions.PostPrimaryChanceAction == 2;
        }

        public void ModifyOptions(ref MyGameOptions myGameOptions)
        {
            // nothing to modify
        }

        public bool PotentialDisputeArises(MyGameDefinition myGameDefinition, MyGameDisputeGeneratorActions disputeGeneratorActions)
        {
            return true;
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
            throw new NotImplementedException(); // no pre primary chance function
        }

        public double[] GetPostPrimaryChanceProbabilities(MyGameDefinition myGameDefinition, MyGameDisputeGeneratorActions disputeGeneratorActions)
        {
            return ProbabilityOfTrulyLiabilityValues;
        }

        public (bool unrollParallelize, bool unrollIdentical) GetPrePrimaryUnrollSettings()
        {
            return (true, true);
        }

        public (bool unrollParallelize, bool unrollIdentical) GetPrimaryUnrollSettings()
        {
            return (true, true);
        }

        public (bool unrollParallelize, bool unrollIdentical) GetPostPrimaryUnrollSettings()
        {
            return (true, true);
        }

        public (bool unrollParallelize, bool unrollIdentical) GetLiabilityLevelUnrollSettings()
        {
            return (true, true);
        }
        public bool PostPrimaryDoesNotAffectStrategy() => true;
    }
}
