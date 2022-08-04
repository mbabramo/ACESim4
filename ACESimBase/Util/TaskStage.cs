using System;
using System.Collections.Generic;
using System.Linq;

namespace ACESim.Util
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

        public override string ToString()
        {
            return String.Join("\n", RepeatedTasks.Select(x => x.ToString()));
        }
    }
}
