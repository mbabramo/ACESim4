using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESim
{
    [Serializable]
    public class ProgressResumptionManagerInfo
    {
        public int ExecutorMultipleSetNumber;
        public bool IsComplete;
        internal int EvolveDecisionPointIndex;
        internal int ExecuteSingleSetCommand;
        internal int ExecuteSingleSetCommandSet;
        internal int ExecuteSingleSetMultipartCommand;
        internal int NumStrategyStatesSerialized;
        internal int SimulationCoordinatorPhase;
    }
}
