using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESim
{
    [Serializable]
    public class MyGameSideBet : IMyGamePretrialDecisionGenerator
    {
        /// <summary>
        /// The multiplier of damages that a party who is challenged to a side bet must pay if the challenged party loses. This will generally be less than or equal to the damages multiple that the challenger must pay. If a party is a challenger and is challenged, then both parties will be treated as challengers.
        /// </summary>
        public double DamagesMultipleForChallengedToPay;
        /// <summary>
        /// The multiplier of damages that a party that challenges the other to a side bet must pay if the party loses. 
        /// </summary>
        public double DamagesMultipleForChallengerToPay;

        public void Setup(MyGameDefinition myGameDefinition)
        {
        }

        public void GetActionsSetup(MyGameDefinition myGameDefinition, out byte pActions, out byte dActions, out byte[] playersToInformOfPAction, out byte[] playersToInformOfDAction)
        {
            pActions = 2; // 1 = yes, challenge D to a sidebet; 2 = no
            dActions = 2; // 1 = yes, challenge P to a sidebet; 2 = no
            playersToInformOfPAction = new byte[] {(byte) MyGamePlayers.Resolution};
            playersToInformOfDAction = new byte[] {(byte) MyGamePlayers.Resolution};
        }

        public void ProcessAction(MyGameDefinition myGameDefinition, MyGameProgress myGameProgress, bool pAction, byte action)
        {
            if (pAction)
                myGameProgress.PretrialActions.PAction = action;
            else
                myGameProgress.PretrialActions.DAction = action;
        }

        public void GetEffectOnPlayerWelfare(MyGameDefinition myGameDefinition, bool trialOccurs, bool pWinsAtTrial, double damagesAlleged, MyGamePretrialActions pretrialActions, out double effectOnP, out double effectOnD)
        {
            if (trialOccurs && (pretrialActions.PAction == 1 || pretrialActions.DAction == 1))
            { // we have a side-bet challenge to process
                if (pWinsAtTrial)
                {
                    bool loserIsChallenger = pretrialActions.DAction == 1; // regardless of whether other party challenged
                    double damagesMultiplier = loserIsChallenger ? DamagesMultipleForChallengerToPay : DamagesMultipleForChallengedToPay;
                    double amountToWinner = (double) damagesAlleged * damagesMultiplier;
                    effectOnP = amountToWinner;
                    effectOnD = 0 - amountToWinner;
                }
                else
                { // defendant wins
                    bool loserIsChallenger = pretrialActions.PAction == 1; // regardless of whether other party challenged
                    double damagesMultiplier = loserIsChallenger ? DamagesMultipleForChallengerToPay : DamagesMultipleForChallengedToPay;
                    double amountToWinner = (double)damagesAlleged * damagesMultiplier;
                    effectOnP = 0 - amountToWinner;
                    effectOnD = amountToWinner;
                }
            }
            else
            {
                effectOnP = effectOnD = 0;
            }
        }
    }
}
