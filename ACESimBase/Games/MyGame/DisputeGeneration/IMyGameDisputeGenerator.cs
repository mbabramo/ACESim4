using ACESimBase.GameSolvingSupport;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESim
{
    public interface IMyGameDisputeGenerator
    {
        void Setup(MyGameDefinition myGameDefinition);
        void GetActionsSetup(MyGameDefinition myGameDefinition, out byte prePrimaryChanceActions, out byte primaryActions, out byte postPrimaryChanceActions, out byte[] prePrimaryPlayersToInform, out byte[] primaryPlayersToInform, out byte[] postPrimaryPlayersToInform, out bool prePrimaryUnevenChance, out bool postPrimaryUnevenChance, out bool litigationQualityUnevenChance, out bool primaryActionCanTerminate, out bool postPrimaryChanceCanTerminate);
        bool PotentialDisputeArises(MyGameDefinition myGameDefinition, MyGameDisputeGeneratorActions disputeGeneratorActions);
        bool MarkComplete(MyGameDefinition myGameDefinition, byte prePrimaryAction, byte primaryAction); 
        bool MarkComplete(MyGameDefinition myGameDefinition, byte prePrimaryAction, byte primaryAction, byte postPrimaryAction);
        bool IsTrulyLiable(MyGameDefinition myGameDefinition, MyGameDisputeGeneratorActions disputeGeneratorActions, GameProgress gameProgress);
        double[] GetPrePrimaryChanceProbabilities(MyGameDefinition myGameDefinition);
        double[] GetPostPrimaryChanceProbabilities(MyGameDefinition myGameDefinition, MyGameDisputeGeneratorActions disputeGeneratorActions);
        double[] GetDamagesStrengthProbabilities(MyGameDefinition myGameDefinition, MyGameDisputeGeneratorActions disputeGeneratorActions);
        double[] GetLiabilityStrengthProbabilities(MyGameDefinition myGameDefinition, MyGameDisputeGeneratorActions disputeGeneratorActions);
        double GetLitigationIndependentSocialWelfare(MyGameDefinition myGameDefinition, MyGameDisputeGeneratorActions disputeGeneratorActions);
        double[] GetLitigationIndependentWealthEffects(MyGameDefinition myGameDefinition, MyGameDisputeGeneratorActions disputeGeneratorActions);

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
