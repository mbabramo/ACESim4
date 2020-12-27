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
        void Setup(LitigGameDefinition myGameDefinition);
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
        // Finally, the game will request that the dispute 

        double[] InvertedCalculations_GetPLiabilitySignalProbabilities() => throw new NotImplementedException();
        double[] InvertedCalculations_GetDLiabilitySignalProbabilities(int pLiabilitySignal) => throw new NotImplementedException();
        double[] InvertedCalculations_GetCLiabilitySignalProbabilities(int pLiabilitySignal, int dLiabilitySignal) => throw new NotImplementedException();
        double[] InvertedCalculations_GetLiabilityStrengthProbabilities(int pLiabilitySignal, int dLiabilitySignal, int cLiabilitySignal) => throw new NotImplementedException();
        double[] InvertedCalculations_GetLiabilityTrueValueProbabilities(int pLiabilitySignal, int dLiabilitySignal, int cLiabilitySignal, int liabilityStrength) => throw new NotImplementedException();

        double[] InvertedCalculations_GetPDamagesSignalProbabilities() => throw new NotImplementedException();
        double[] InvertedCalculations_GetDDamagesSignalProbabilities(int pDamagesSignal) => throw new NotImplementedException();
        double[] InvertedCalculations_GetCDamagesSignalProbabilities(int pDamagesSignal, int dDamagesSignal) => throw new NotImplementedException();
        double[] InvertedCalculations_GetDamagesStrengthProbabilities(int pDamagesSignal, int dDamagesSignal, int cDamagesSignal) => throw new NotImplementedException();



    }
}
