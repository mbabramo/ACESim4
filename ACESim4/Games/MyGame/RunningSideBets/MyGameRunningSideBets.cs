using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESim
{
    public class MyGameRunningSideBets
    {
        /// <summary>
        /// The value of a single chip, as a percentage of damages.
        /// </summary>
        public double ValueOfChip;
        /// <summary>
        /// The maximum number of chips per round. Each player will simultaneously bet a suggested number of chips up to this number, and the higher will control. The players must then decide whether to give up based on this maximum.
        /// </summary>
        public byte MaxChipsPerRound;


        public void Setup(MyGameDefinition myGameDefinition)
        {
            // We need the agreement to bargain decisions, since we have a settlement, chips are returned to the players. We could change that, but that would require identifying a "winner" of the settlement.
            myGameDefinition.Options.IncludeAgreementToBargainDecisions = true;
            // We need the chance to withdraw rather than accept a bet, as well.
            myGameDefinition.Options.AllowAbandonAndDefaults = true;
        }

        public void SaveRunningSideBets(MyGameDefinition myGameDefinition, MyGameProgress myGameProgress, byte dAction)
        {
            // We will be storing the plaintiff's action in the cache. So we can now modify game progress.
            byte pAction = myGameProgress.GameHistory.GetCacheItemAtIndex(myGameDefinition.GameHistoryCacheIndex_PChipsAction);
            if (myGameProgress.RunningSideBetsActions.ActionsEachBargainingRound == null)
                myGameProgress.RunningSideBetsActions.ActionsEachBargainingRound = new List<(byte PAction, byte DAction)>();
            myGameProgress.RunningSideBetsActions.ActionsEachBargainingRound.Add((pAction, dAction));
        }

        public void GetEffectOnPlayerWelfare(MyGameDefinition myGameDefinition, byte? roundOfAbandonment, bool pAbandons, bool dDefaults, bool trialOccurs, bool pWinsAtTrial, MyGameRunningSideBetsActions runningSideBetsActions, out double effectOnP, out double effectOnD)
        {
            byte numChipsBet = runningSideBetsActions.GetTotalChipsThatCount(roundOfAbandonment, pAbandons);
            if (numChipsBet > 0 && (pAbandons || dDefaults || trialOccurs))
            {
                double transferToP = numChipsBet * ValueOfChip;
                bool pWins = dDefaults || (trialOccurs && pWinsAtTrial);
                if (!pWins)
                    transferToP = -transferToP;
                effectOnP = transferToP;
                effectOnD = 0 - transferToP;
            }
            else
            {
                effectOnP = effectOnD = 0;
            }
        }
    }
}
