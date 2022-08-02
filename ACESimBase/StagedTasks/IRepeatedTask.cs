using ACESim.Util;
using Lazinator.Attributes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESimBase.StagedTasks
{
    [Lazinator((int)LazinatorTaskEnums.IRepeatedTask)]
    public interface IRepeatedTask
    {
        string TaskType { get; set; }
        int ID { get; set; }
        bool AvoidRedundantExecution { get; set; } // use AvoidRedundantExecution for very long tasks at the end of a series of tasks, so other processes do not try to do them simultaneously when they see that it has been some time before the task was started
        IndividualTask[] IndividualTasks { get; set; }
    }
}
