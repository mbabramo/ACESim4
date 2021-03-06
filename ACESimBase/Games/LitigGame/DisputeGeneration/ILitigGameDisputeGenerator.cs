using ACESimBase.GameSolvingSupport;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESim
{
    public interface ILitigGameDisputeGenerator
    {
        LitigGameDefinition LitigGameDefinition { get; set; }
        void Setup(LitigGameDefinition litigGameDefinition);
        void GetActionsSetup(LitigGameDefinition myGameDefinition, out byte prePrimaryChanceActions, out byte primaryActions, out byte postPrimaryChanceActions, out byte[] prePrimaryPlayersToInform, out byte[] primaryPlayersToInform, out byte[] postPrimaryPlayersToInform, out bool prePrimaryUnevenChance, out bool postPrimaryUnevenChance, out bool litigationQualityUnevenChance, out bool primaryActionCanTerminate, out bool postPrimaryChanceCanTerminate);
        bool PotentialDisputeArises(LitigGameDefinition myGameDefinition, LitigGameDisputeGeneratorActions disputeGeneratorActions);
        bool MarkComplete(LitigGameDefinition myGameDefinition, byte prePrimaryAction, byte primaryAction);
        bool MarkComplete(LitigGameDefinition myGameDefinition, byte prePrimaryAction, byte primaryAction, byte postPrimaryAction);
        bool IsTrulyLiable(LitigGameDefinition myGameDefinition, LitigGameDisputeGeneratorActions disputeGeneratorActions, GameProgress gameProgress);
        double[] GetPrePrimaryChanceProbabilities(LitigGameDefinition myGameDefinition);
        double[] GetPostPrimaryChanceProbabilities(LitigGameDefinition myGameDefinition, LitigGameDisputeGeneratorActions disputeGeneratorActions);
        double[] GetDamagesStrengthProbabilities(LitigGameDefinition myGameDefinition, LitigGameDisputeGeneratorActions disputeGeneratorActions);
        double[] GetLiabilityStrengthProbabilities(LitigGameDefinition myGameDefinition, LitigGameDisputeGeneratorActions disputeGeneratorActions);
        double GetLitigationIndependentSocialWelfare(LitigGameDefinition myGameDefinition, LitigGameDisputeGeneratorActions disputeGeneratorActions);
        double[] GetLitigationIndependentWealthEffects(LitigGameDefinition myGameDefinition, LitigGameDisputeGeneratorActions disputeGeneratorActions);

        public bool SupportsSymmetry() => false;

        public (bool unrollParallelize, bool unrollIdentical, SymmetryMapInput symmetryMapInput) GetPrePrimaryUnrollSettings() =>
            (false, false, SymmetryMapInput.SameInfo);
        public (bool unrollParallelize, bool unrollIdentical, SymmetryMapInput symmetryMapInput) GetPrimaryUnrollSettings() =>
            (false, false, SymmetryMapInput.SameInfo);
        public (bool unrollParallelize, bool unrollIdentical, SymmetryMapInput symmetryMapInput) GetPostPrimaryUnrollSettings() =>
            (false, false, SymmetryMapInput.SameInfo);
        public (bool unrollParallelize, bool unrollIdentical, SymmetryMapInput symmetryMapInput) GetLiabilityStrengthUnrollSettings() =>
            (false, false, SymmetryMapInput.SameInfo);
        public (bool unrollParallelize, bool unrollIdentical, SymmetryMapInput symmetryMapInput) GetDamagesStrengthUnrollSettings() =>
            (false, false, SymmetryMapInput.SameInfo);

        bool PostPrimaryDoesNotAffectStrategy();

        string GetGeneratorName();

        (string name, string abbreviation) PrePrimaryNameAndAbbreviation { get; }
        (string name, string abbreviation) PrimaryNameAndAbbreviation { get; }
        (string name, string abbreviation) PostPrimaryNameAndAbbreviation { get; }
        string GetActionString(byte action, byte decisionByteCode);


        // The following are for dispute generators that invert calculations. Without inverted calculations, the dispute generator produces liability and damages strength
        // probabilities, often as a function of earlier probabilities (pre-primary, primary, and chance), for example representing true state of the world.
        // Without inverted calculations, the game then calculates the parties' signals based on these litigation strength values.
        // With inverted calculations, the game does not request liability and damages strength probabilities or call the dispute generator for any earlier decisions.
        // Instead, the game simply requests unconditional signals for the plaintiff. In other words, the dispute generator must calculate the probability that a plaintiff
        // would receive each signal given the dispute generation model, including considerations such as liability strength. The game then requests conditional signals
        // for the defendant given the signals for the plaintiff. Because the plaintiff and defendant both receive signals of the same underlying quantity, these are correlated,
        // and so the dispute generation module must ensure that it has the correct probability distribution for the defendant's signals given the plaintiff signal.
        // This can be handled easily by using the DiscreteProbabilityDistribution class.
        // Then, the game will request of the dispute generator the court signals, for each of liability and damages, dependent on the player signals for liability and damages.
        // Finally, the game will request that the dispute generator provide information on liability strength and damages strength, and finally on true liability.

        double[] InvertedCalculations_GetPLiabilitySignalProbabilities() => throw new NotImplementedException();
        double[] InvertedCalculations_GetDLiabilitySignalProbabilities(byte pLiabilitySignal) => throw new NotImplementedException();
        double[] InvertedCalculations_GetCLiabilitySignalProbabilities(byte pLiabilitySignal, byte dLiabilitySignal) => throw new NotImplementedException();

        double[] InvertedCalculations_GetPDamagesSignalProbabilities() => throw new NotImplementedException();
        double[] InvertedCalculations_GetDDamagesSignalProbabilities(byte pDamagesSignal) => throw new NotImplementedException();
        double[] InvertedCalculations_GetCDamagesSignalProbabilities(byte pDamagesSignal, byte dDamagesSignal) => throw new NotImplementedException();
        double[] InvertedCalculations_GetLiabilityStrengthProbabilities(byte pLiabilitySignal, byte dLiabilitySignal, byte? cLiabilitySignal) => throw new NotImplementedException();
        double[] InvertedCalculations_GetDamagesStrengthProbabilities(byte pDamagesSignal, byte dDamagesSignal, byte? cDamagesSignal) => throw new NotImplementedException();

        // Note: When we extend this to other dispute generators, we're going to need to be able to work further backwards to whether a potential dispute arises. That is, we're only going to actually optimize decisions given that a dispute arises. So, the dispute generator will need to be able to report on how often does a potential dispute arises, and then work backwards from the absence of a dispute to the conditions that could have led to the absence of a dispute. Then, we might like to report on those conditions. 
        (bool trulyLiable, byte liabilityStrength, byte damagesStrength) InvertedCalculations_WorkBackwardsFromSignals(byte pLiabilitySignal, byte dLiabilitySignal, byte? cLiabilitySignal, byte pDamagesSignal, byte dDamagesSignal, byte? cDamagesSignal, int randomSeed) => throw new NotImplementedException();

        List<(GameProgress progress, double weight)> InvertedCalculations_GenerateAllConsistentGameProgresses(byte pLiabilitySignal, byte dLiabilitySignal, byte? cLiabilitySignal, byte pDamagesSignal, byte dDamagesSignal, byte? cDamagesSignal, LitigGameProgress gameProgress) => throw new NotImplementedException();
    }
}
