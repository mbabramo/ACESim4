using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESim
{
    public interface ILitigGamePretrialDecisionGenerator
    {
        void Setup(LitigGameDefinition myGameDefinition);
        void GetActionsSetup(LitigGameDefinition myGameDefinition, out byte pActions, out byte dActions, out byte[] playersToInformOfPAction, out byte[] playersToInformOfDAction);
        void GetEffectOnPlayerWelfare(LitigGameDefinition myGameDefinition, bool trialOccurs, bool pWinsAtTrial, double damagesAllegedTimesMultiplier, LitigGamePretrialActions pretrialActions, out double effectOnP, out double effectOnD);
        void ProcessAction(LitigGameDefinition myGameDefinition, LitigGameProgress myGameProgress, bool pAction, byte action);
    }
}
