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
    }
}
