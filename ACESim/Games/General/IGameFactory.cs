using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ACESim
{
    public interface IGameFactory
    {
        Game CreateNewGame();
        Type GetSimulationSettingsType();
        Type GetGameDefinitionType();
        GameProgress CreateNewGameProgress(IterationID iterationID);
        void InitializeStrategyDevelopment(Strategy strategy);
        void ConcludeStrategyDevelopment();
    }
}
