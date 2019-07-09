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

        public CounterfactualRegretMinimization(List<Strategy> existingStrategyState, EvolutionSettings evolutionSettings, GameDefinition gameDefinition) : base(existingStrategyState, evolutionSettings, gameDefinition)
        {

        }

        #region Options and variables

        public bool TraceCFR = true;  // DEBUG

        #endregion

        #region Misc

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
