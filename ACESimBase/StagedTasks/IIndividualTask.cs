using Lazinator.Attributes;
using Lazinator.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESimBase.StagedTasks
{
    [Lazinator((int)LazinatorTaskEnums.IIndividualTask)]
    public interface IIndividualTask
    {
        string TaskType { get; set; }
        int ID { get; set; }
        int Repetition { get; set; }
        int? RestrictToScenarioIndex { get; set; }
        DateTime? Started { get; set; }
        DateTime? Completed { get; set; }
    }
}
