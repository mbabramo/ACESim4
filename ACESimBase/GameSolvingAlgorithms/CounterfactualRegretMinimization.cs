using ACESim.Util;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using System.Threading;
using ACESimBase.Util;
using ACESimBase.GameSolvingSupport;

namespace ACESim
{
    [Serializable]
    public abstract partial class CounterfactualRegretMinimization : StrategiesDeveloperBase
    {

        #region Options and variables

        public bool TraceCFR = false;

        #endregion

        #region Construction

        public CounterfactualRegretMinimization()
        {
            Navigation = Navigation.WithGameStateFunction(GetGameState);
        }

        public CounterfactualRegretMinimization(List<Strategy> existingStrategyState, EvolutionSettings evolutionSettings, GameDefinition gameDefinition)
        {
            Strategies = existingStrategyState;
            EvolutionSettings = evolutionSettings;
            GameDefinition = gameDefinition;
            GameFactory = GameDefinition.GameFactory;
            NumNonChancePlayers = (byte) GameDefinition.Players.Count(x => !x.PlayerIsChance);
            NumChancePlayers = (byte) GameDefinition.Players.Count(x => x.PlayerIsChance);
        }

        public unsafe byte SampleAction(double* actionProbabilities, byte numPossibleActions, double randomNumber)
        {

            double cumulative = 0;
            byte action = 1;
            do
            {
                if (action == numPossibleActions)
                    return action;
                cumulative += actionProbabilities[action - 1];
                if (cumulative >= randomNumber)
                    return action;
                else
                    action++;
            } while (true);
        }

        #endregion
    }
}
