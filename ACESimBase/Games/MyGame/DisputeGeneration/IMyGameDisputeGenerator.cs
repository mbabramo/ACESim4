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
        double[] GetLitigationQualityProbabilities(MyGameDefinition myGameDefinition, MyGameDisputeGeneratorActions disputeGeneratorActions);
        double GetLitigationIndependentSocialWelfare(MyGameDefinition myGameDefinition, MyGameDisputeGeneratorActions disputeGeneratorActions);
        double[] GetLitigationIndependentWealthEffects(MyGameDefinition myGameDefinition, MyGameDisputeGeneratorActions disputeGeneratorActions);

        (bool unrollParallelize, bool unrollIdentical) GetPrePrimaryUnrollSettings();
        (bool unrollParallelize, bool unrollIdentical) GetPrimaryUnrollSettings();
        (bool unrollParallelize, bool unrollIdentical) GetPostPrimaryUnrollSettings();
        (bool unrollParallelize, bool unrollIdentical) GetLitigationQualityUnrollSettings();

        string GetGeneratorName();
    }
}
