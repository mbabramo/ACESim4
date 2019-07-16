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
        public RepeatedTask TaskWithLowestAvailable => RepeatedTasks.Where(x => !x.Complete).OrderBy(x => x.IndexOfFirstIncomplete).First();
        public override string ToString()
        {
            return String.Join("\n", RepeatedTasks.Select(x => x.ToString()));
        }
    }
}
