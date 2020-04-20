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
    public class LeducGame : Game
    {
        public LeducGameDefinition MyDefinition => (LeducGameDefinition)GameDefinition;
        public LeducGameProgress MyProgress => (LeducGameProgress)Progress;
        public LeducGameState MyState => MyProgress.GameState;

        public LeducGame(List<Strategy> strategies,
            GameProgress progress,
            GameDefinition gameDefinition,
            bool recordReportInfo,
            bool restartFromBeginningOfGame
            ) : base(strategies, progress, gameDefinition, recordReportInfo, restartFromBeginningOfGame)
        {

        }

        public override void Initialize()
        {
            MyProgress.GameState = new LeducGameState(MyDefinition.Options.OneBetSizeOnly);
        }

        public override void UpdateGameProgressFollowingAction(byte currentDecisionByteCode, byte action)
        {
            switch (currentDecisionByteCode)
            {
                case (byte)LeducGameDecisions.P1Chance:
                    MyState.P1Card = action;
                    break;
                case (byte)LeducGameDecisions.P2Chance:
                    MyState.P2Card = action;
                    break;
                case (byte)LeducGameDecisions.FlopChance:
                    MyState.FlopCard = action;
                    break;
                case (byte)LeducGameDecisions.P1Decision: // fold is always excluded
                case (byte)LeducGameDecisions.P2DecisionFoldExcluded:
                    MyState.AddChoice((LeducPlayerChoice)action + 1 /* i.e., skip fold */);
                    break;
                case (byte)LeducGameDecisions.P2Decision:
                case (byte)LeducGameDecisions.P1Response:
                case (byte)LeducGameDecisions.P1ResponseBetsExcluded:
                case (byte)LeducGameDecisions.P2Response:
                    MyState.AddChoice((LeducPlayerChoice)action);
                    break;
                default:
                    throw new Exception();
            }
            if (GameProgressLogger.LoggingOn)
                GameProgressLogger.Log($"State: {MyState} NextTurn: {MyState.GetTurn()} Choices: {String.Join(",", MyState.GetAvailableChoices())}");
        }


        public override void FinalProcessing()
        {
            base.FinalProcessing();
        }
    }
}
