using Lazinator.Attributes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESimBase.StagedTasks
{
    [Lazinator((int)LazinatorTaskEnums.ITaskStage)]
    public interface ITaskStage
    {
        List<RepeatedTask> RepeatedTasks { get; set; }
    }
}
