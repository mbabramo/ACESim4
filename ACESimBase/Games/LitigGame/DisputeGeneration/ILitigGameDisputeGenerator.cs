using System;
using System.Collections.Generic;

namespace ACESim
{
    public interface ILitigGameDisputeGenerator
    {
        LitigGameDefinition LitigGameDefinition { get; set; }

        void Setup(LitigGameDefinition litigGameDefinition);

        List<Decision> GenerateDisputeDecisions(LitigGameDefinition gameDefinition);

        bool PotentialDisputeArises(
            LitigGameDefinition gameDefinition,
            LitigGameStandardDisputeGeneratorActions disputeGeneratorActions,
            LitigGameProgress gameProgress);

        //bool MarkComplete(
        //    LitigGameDefinition gameDefinition,
        //    GameProgress gameProgress,
        //    Decision decisionJustTaken,
        //    byte actionChosen); // DEBUG

        bool HandleUpdatingGameProgress(LitigGameProgress gameProgress, byte currentDecisionByteCode, byte action);

        bool IsTrulyLiable(
            LitigGameDefinition gameDefinition,
            LitigGameStandardDisputeGeneratorActions disputeGeneratorActions,
            GameProgress gameProgress);

        double[] GetLiabilityStrengthProbabilities(
            LitigGameDefinition gameDefinition,
            LitigGameStandardDisputeGeneratorActions disputeGeneratorActions);

        double[] GetDamagesStrengthProbabilities(
            LitigGameDefinition gameDefinition,
            LitigGameStandardDisputeGeneratorActions disputeGeneratorActions);

        double GetLitigationIndependentSocialWelfare(
            LitigGameDefinition gameDefinition,
            LitigGameStandardDisputeGeneratorActions disputeGeneratorActions,
            LitigGameProgress gameProgress);

        double[] GetLitigationIndependentWealthEffects(
            LitigGameDefinition gameDefinition,
            LitigGameStandardDisputeGeneratorActions disputeGeneratorActions, 
            LitigGameProgress gameProgress);

        (double opportunityCost, double harmCost) GetOpportunityAndHarmCosts(
            LitigGameDefinition gameDefinition,
            LitigGameStandardDisputeGeneratorActions disputeGeneratorActions, 
            LitigGameProgress gameProgress);

        bool SupportsSymmetry();

        string GetGeneratorName();

        string OptionsString { get; }

        string GetActionString(byte action, byte decisionByteCode);


        /* ─────── Inversion support (mirrors Exogenous generator) ─────── */

        double[] BayesianCalculations_GetPLiabilitySignalProbabilities(byte? dLiabilitySignal);
        double[] BayesianCalculations_GetDLiabilitySignalProbabilities(byte? pLiabilitySignal);
        double[] BayesianCalculations_GetCLiabilitySignalProbabilities(byte pLiabilitySignal, byte dLiabilitySignal);

        double[] BayesianCalculations_GetPDamagesSignalProbabilities(byte? dDamagesSignal);
        double[] BayesianCalculations_GetDDamagesSignalProbabilities(byte? pDamagesSignal);
        double[] BayesianCalculations_GetCDamagesSignalProbabilities(byte pDamagesSignal, byte dDamagesSignal);

        void
            BayesianCalculations_WorkBackwardsFromSignals(
                LitigGameProgress gameProgress,
                byte pLiabilitySignal,
                byte dLiabilitySignal,
                byte? cLiabilitySignal,
                byte pDamagesSignal,
                byte dDamagesSignal,
                byte? cDamagesSignal,
                int randomSeed);

        bool GenerateConsistentGameProgressesWhenNotCollapsing => false;

        List<(GameProgress progress, double weight)>
            BayesianCalculations_GenerateAllConsistentGameProgresses(
                byte pLiabilitySignal,
                byte dLiabilitySignal,
                byte? cLiabilitySignal,
                byte pDamagesSignal,
                byte dDamagesSignal,
                byte? cDamagesSignal,
                LitigGameProgress baseProgress);
    }
}




