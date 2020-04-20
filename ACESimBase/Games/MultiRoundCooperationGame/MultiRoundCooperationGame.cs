using ACESim.Util;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ACESim
{
    /// <summary>
    /// A dummy Game subclass to show a basic Game implementation.
    /// </summary>
    public class MultiRoundCooperationGame : Game
    {
        public MultiRoundCooperationGameDefinition MyDefinition => (MultiRoundCooperationGameDefinition)GameDefinition;
        public MultiRoundCooperationGameProgress MyProgress => (MultiRoundCooperationGameProgress)Progress;

        public MultiRoundCooperationGame(List<Strategy> strategies,
            GameProgress progress,
            GameDefinition gameDefinition,
            bool recordReportInfo,
            bool restartFromBeginningOfGame
            ) : base(strategies, progress, gameDefinition, recordReportInfo, restartFromBeginningOfGame)
        {

        }

        public override void UpdateGameProgressFollowingAction(byte currentDecisionByteCode, byte action)
        {
            if (currentDecisionByteCode == (byte)MultiRoundCooperationGameDecisions.P1Decision)
                MyProgress.P1Decisions.Add(action);
            else if (currentDecisionByteCode == (byte) MultiRoundCooperationGameDecisions.P2Decision)
            {
                MyProgress.P2Decisions.Add(action);
                if (MyProgress.P2Decisions.Count == MyDefinition.TotalRounds)
                    MyProgress.GameComplete = true;
            }
            //else if (currentDecisionByteCode == (byte)MultiRoundCooperationGameDecisions.Chance)
            //    MyProgress.ChanceDecision = action;
        }


        public override void FinalProcessing()
        {
            base.FinalProcessing();
        }
    }
}
