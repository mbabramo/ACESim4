using System;
using System.Collections.Generic;
using System.Linq;

namespace ACESimBase.Util.TaskManagement
{
    [Serializable]
    public class TaskStage
    {
        public TaskStage(List<RepeatedTask> repeatedTasks)
        {
            RepeatedTasks = repeatedTasks;
        }
        public List<RepeatedTask> RepeatedTasks = null;
        public bool Complete => RepeatedTasks.All(x => x.Complete);
        public RepeatedTask IncompleteRepeatedTask => RepeatedTasks
            .Where(x => !x.Complete)
            .OrderBy(x => x.AllStarted) // put repeated tasks where not all have been started first
            .ThenBy(x => x.IndividualTasks.OrderBy(y => y.Started).FirstOrDefault()?.Started ?? DateTime.Now)
            .ThenBy(x => x.ID)
            .FirstOrDefault();

        public List<IndividualTask> FirstIncompleteTasks(int n) => Complete ? null :
            RepeatedTasks.SelectMany(x => x.IndividualTasks)
            .OrderBy(x => x.Complete) // incomplete first
            .ThenBy(x => x.Started != null) // not started first
            .ThenBy(x => x.Started) // oldest started first
            .Take(n)
            .ToList();

        public override string ToString()
        {
            return string.Join("\n", RepeatedTasks.Select(x => x.ToString()));
        }
    }
}
