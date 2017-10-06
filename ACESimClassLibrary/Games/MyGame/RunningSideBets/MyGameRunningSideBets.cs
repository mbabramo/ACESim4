using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESim
{
    [Serializable]
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
        /// <summary>
        /// If true, then a player who gives up after a round must pay the number of chips the opponent bet in that round, even if that is larger than the number of chips paid by the player.
        /// </summary>
        public bool CountAllChipsInAbandoningRound;
        /// <summary>
        /// As stakes go to infinity, trial costs go to this value
        /// </summary>
        public double TrialCostsMultiplierAsymptote = 3.0;
        /// <summary>
        /// The trial costs multiplier with double stakes
        /// </summary>
        public double TrialCostsMultiplierWithDoubleStakes = 1.3;


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

        public void GetEffectOnPlayerWelfare(MyGameDefinition myGameDefinition, byte? roundOfAbandonment, bool pAbandons, bool dDefaults, bool trialOccurs, bool pWinsAtTrial, MyGameRunningSideBetsActions runningSideBetsActions, out double effectOnP, out double effectOnD, out byte totalChipsThatCount)
        {
            totalChipsThatCount = runningSideBetsActions.GetTotalChipsThatCount(roundOfAbandonment, pAbandons, myGameDefinition.Options.MyGameRunningSideBets.CountAllChipsInAbandoningRound);
            if (totalChipsThatCount > 0 && (pAbandons || dDefaults || trialOccurs))
            {
                double transferToP = totalChipsThatCount * ValueOfChip;
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

        public double GetTrialCostsMultiplier(MyGameDefinition gameDefinition, double totalChipsThatCount)
        {
            if (TrialCostsMultiplierAsymptote == 1.0)
                return 1.0;
            double stakesRatio = (gameDefinition.Options.DamagesToAllege + totalChipsThatCount * ValueOfChip) / gameDefinition.Options.DamagesToAllege;
            // y = (asymptote - 1)*(x - 1)/(x - k) + 1, where x is the stakes ratio and y is the spending ratio. Thus, when x = 1, y = 1. 
            double k = (TrialCostsMultiplierAsymptote - 1.0)*(2.0 - 1.0) / (TrialCostsMultiplierWithDoubleStakes - 1.0) - 2.0;
            double trialCostsMultiplier = (TrialCostsMultiplierAsymptote - 1.0) * (stakesRatio - 1) / (stakesRatio + k) + 1.0;
            return trialCostsMultiplier;
        }
    }
}
