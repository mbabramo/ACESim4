using ACESim;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESimBase.Games.EFGFileGame
{
    /// <summary>
    /// A game that is loaded in from an .efg file, representing an extended form game. There are some limitations and assumptions about the format of that file.
    /// </summary>
    public class EFGFileGame : Game
    {
        public EFGFileGameProgress MyProgress => (EFGFileGameProgress)Progress;

        public EFGFileGame(List<Strategy> strategies,
            GameProgress progress,
            GameDefinition gameDefinition,
            bool recordReportInfo,
            bool restartFromBeginningOfGame,
            bool fullHistoryRequired
            ) : base(strategies, progress, gameDefinition, recordReportInfo, restartFromBeginningOfGame, fullHistoryRequired)
        {

        }

        public override void Initialize()
        {
        }

        public override void UpdateGameProgressFollowingAction(byte currentDecisionByteCode, byte action)
        {
            MyProgress.AddEFGFileGameMove(action);
            if (GameProgressLogger.LoggingOn)
                GameProgressLogger.Log($"");
            if (MyProgress.MovesIndicateCompleteGame())
                Progress.GameComplete = true;
        }


        public override void FinalProcessing()
        {
            base.FinalProcessing();
        }
    }
}
