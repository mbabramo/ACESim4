using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESim
{
    public interface IMyGamePretrialDecisionGenerator
    {
        void Setup(MyGameDefinition myGameDefinition);
        void GetActionsSetup(MyGameDefinition myGameDefinition, out byte pActions, out byte dActions, out byte[] playersToInformOfPAction, out byte[] playersToInformOfDAction);
        void GetEffectOnPlayerWelfare(MyGameDefinition myGameDefinition, bool trialOccurs, bool pWinsAtTrial, double damagesAlleged, MyGamePretrialActions pretrialActions, out double effectOnP, out double effectOnD);
        void ProcessAction(MyGameDefinition myGameDefinition, MyGameProgress myGameProgress, bool pAction, byte action);
    }
}
