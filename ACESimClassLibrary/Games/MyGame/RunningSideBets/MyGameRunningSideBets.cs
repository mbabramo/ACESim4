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
        /// <summary>
        /// If true, then a player who gives up after a round must pay the number of chips the opponent bet in that round, even if that is larger than the number of chips paid by the player.
        /// </summary>
        public bool CountAllChipsInAbandoningRound;
        /// <summary>
        /// The trial expenses will be multiplied by this if the side bets lead to doubling of litigation expenses.
        /// </summary>
        public double EffectOfDoublingStakesOnLitigationExpenses = 1.5;
        /// <summary>
        /// The trial expenses will be multiplied by this if the side bets lead to a 50% increase in litigation expenses
        /// </summary>
        public double EffectOf150PercentStakesOnLitigationExpenses = 1.3;
        /// <summary>
        /// The curvature of the effect of litigation expenses on curvature. This is automatically calculated.
        /// </summary>
        public double LitigationExpensesCurvature;


        public void Setup(MyGameDefinition myGameDefinition)
        {
            // We need the agreement to bargain decisions, since we have a settlement, chips are returned to the players. We could change that, but that would require identifying a "winner" of the settlement.
            myGameDefinition.Options.IncludeAgreementToBargainDecisions = true;
            // We need the chance to withdraw rather than accept a bet, as well.
            myGameDefinition.Options.AllowAbandonAndDefaults = true;

            double ratioOfOriginalToRevisedStakes0 = 1.0 / 2.0;
            double incrementToTrialExpensesMultiplier0 = EffectOfDoublingStakesOnLitigationExpenses - 1.0;
            double ratioOfOriginalToRevisedStakes1 = 2.0 / 3.0;
            double incrementToTrialExpensesMultiplier1 = EffectOf150PercentStakesOnLitigationExpenses - 1.0;
            double ratioOfOriginalToRevisedStakes2 = 1.0;
            double incrementToTrialExpensesMultiplier2 = 0;
            LitigationExpensesCurvature = MonotonicCurve.CalculateCurvatureForThreePoints(ratioOfOriginalToRevisedStakes0, incrementToTrialExpensesMultiplier0, ratioOfOriginalToRevisedStakes1, incrementToTrialExpensesMultiplier1, ratioOfOriginalToRevisedStakes2, incrementToTrialExpensesMultiplier2);
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
    }
}
