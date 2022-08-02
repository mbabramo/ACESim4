using Lazinator.Attributes;
using Lazinator.Collections;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESimBase.StagedTasks
{
    [Lazinator((int)LazinatorTaskEnums.ITaskCoordinator)]
    public interface ITaskCoordinator
    {
        LazinatorList<TaskStage> Stages { get; set; }
    }
}
