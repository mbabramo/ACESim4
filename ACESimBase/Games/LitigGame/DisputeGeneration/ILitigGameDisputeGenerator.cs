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

        double[] InvertedCalculations_GetPLiabilitySignalProbabilities(byte? dLiabilitySignal);
        double[] InvertedCalculations_GetDLiabilitySignalProbabilities(byte? pLiabilitySignal);
        double[] InvertedCalculations_GetCLiabilitySignalProbabilities(byte pLiabilitySignal, byte dLiabilitySignal);

        double[] InvertedCalculations_GetPDamagesSignalProbabilities(byte? dDamagesSignal);
        double[] InvertedCalculations_GetDDamagesSignalProbabilities(byte? pDamagesSignal);
        double[] InvertedCalculations_GetCDamagesSignalProbabilities(byte pDamagesSignal, byte dDamagesSignal);

        double[] InvertedCalculations_GetLiabilityStrengthProbabilities(
            byte pLiabilitySignal,
            byte dLiabilitySignal,
            byte? cLiabilitySignal);

        double[] InvertedCalculations_GetDamagesStrengthProbabilities(
            byte pDamagesSignal,
            byte dDamagesSignal,
            byte? cDamagesSignal);

        void
            InvertedCalculations_WorkBackwardsFromSignals(
                LitigGameProgress gameProgress,
                byte pLiabilitySignal,
                byte dLiabilitySignal,
                byte? cLiabilitySignal,
                byte pDamagesSignal,
                byte dDamagesSignal,
                byte? cDamagesSignal,
                int randomSeed);

        List<(GameProgress progress, double weight)>
            InvertedCalculations_GenerateAllConsistentGameProgresses(
                byte pLiabilitySignal,
                byte dLiabilitySignal,
                byte? cLiabilitySignal,
                byte pDamagesSignal,
                byte dDamagesSignal,
                byte? cDamagesSignal,
                LitigGameProgress baseProgress);
    }
}




