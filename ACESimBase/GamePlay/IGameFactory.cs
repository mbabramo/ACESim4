using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ACESim
{
    public interface IGameFactory
    {
        Game CreateNewGame(List<Strategy> strategies,
            GameProgress progress,
            GameDefinition gameDefinition,
            bool recordReportInfo,
            bool restartFromBeginningOfGame,
            bool fullHistoryRequired);
        Type GetGameDefinitionType();
        GameProgress CreateNewGameProgress(bool fullHistoryRequired, IterationID iterationID);
        void InitializeStrategyDevelopment(Strategy strategy);
        void ConcludeStrategyDevelopment();
    }
}
