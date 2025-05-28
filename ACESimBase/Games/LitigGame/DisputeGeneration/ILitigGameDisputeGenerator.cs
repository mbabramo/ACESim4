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
            LitigGameDisputeGeneratorActions disputeGeneratorActions);

        bool MarkComplete(
            LitigGameDefinition gameDefinition,
            GameProgress gameProgress,
            Decision decisionJustTaken,
            byte actionChosen);

        bool IsTrulyLiable(
            LitigGameDefinition gameDefinition,
            LitigGameDisputeGeneratorActions disputeGeneratorActions,
            GameProgress gameProgress);

        double[] GetLiabilityStrengthProbabilities(
            LitigGameDefinition gameDefinition,
            LitigGameDisputeGeneratorActions disputeGeneratorActions);

        double[] GetDamagesStrengthProbabilities(
            LitigGameDefinition gameDefinition,
            LitigGameDisputeGeneratorActions disputeGeneratorActions);

        double GetLitigationIndependentSocialWelfare(
            LitigGameDefinition gameDefinition,
            LitigGameDisputeGeneratorActions disputeGeneratorActions);

        double[] GetLitigationIndependentWealthEffects(
            LitigGameDefinition gameDefinition,
            LitigGameDisputeGeneratorActions disputeGeneratorActions);

        (double opportunityCost, double harmCost) GetOpportunityAndHarmCosts(
            LitigGameDefinition gameDefinition,
            LitigGameDisputeGeneratorActions disputeGeneratorActions);

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

        (bool trulyLiable, byte liabilityStrength, byte damagesStrength)
            InvertedCalculations_WorkBackwardsFromSignals(
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
                LitigGameProgress baseProgress) => throw new NotSupportedException();
    }
}




