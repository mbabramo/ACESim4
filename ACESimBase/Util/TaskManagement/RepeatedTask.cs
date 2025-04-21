using System;
using System.Collections.Generic;
using System.Linq;

namespace ACESimBase.Util.TaskManagement
{
    [Serializable]
    public class RepeatedTask
    {
        public string TaskType;
        public int ID;
        public bool AvoidRedundantExecution; // use AvoidRedundantExecution for very long tasks at the end of a series of tasks, so other processes do not try to do them simultaneously when they see that it has been some time before the task was started
        public IndividualTask[] IndividualTasks;

        public RepeatedTask(string taskType, int id, int repetitions, int? scenarios)
        {
            TaskType = taskType;
            ID = id;
            int numIndividualTasks = repetitions;
            int?[] scenarioArray = new int?[] { null };
            if (scenarios != null)
            {
                scenarioArray = Enumerable.Range(0, (int)scenarios).Select(x => (int?)x).ToArray();
                numIndividualTasks *= (int)scenarios;
            }
            IndividualTasks = new IndividualTask[numIndividualTasks];
            int individualTasksIndex = 0;
            foreach (int? scenarioIndex in scenarioArray)
            {
                for (int repetition = 0; repetition < repetitions; repetition++)
                {
                    IndividualTasks[individualTasksIndex++] = new IndividualTask(taskType, ID, repetition, scenarioIndex);
                }
            }
        }

        public bool AllStarted => IndividualTasks.All(x => x.Started != null);
        public bool Complete => IndividualTasks.All(x => x.Complete);
        public int? IndexOfFirstIncomplete()
        {
            var firstIncomplete = FirstIncomplete();
            for (int i = 0; i < IndividualTasks.Count(); i++)
                if (IndividualTasks[i] == firstIncomplete)
                    return i;
            return null;
        }

        public IndividualTask FirstIncomplete() => Complete ? null :
            IndividualTasks
            .OrderBy(x => x.Complete) // incomplete first
            .ThenBy(x => x.Started != null) // not started first
            .ThenBy(x => x.Started) // oldest started first
            .FirstOrDefault();

        public List<IndividualTask> FirstIncompleteTasks(int n) => Complete ? null :
            IndividualTasks
            .OrderBy(x => x.Complete) // incomplete first
            .ThenBy(x => x.Started != null) // not started first
            .ThenBy(x => x.Started) // oldest started first
            .Take(n)
            .ToList();
        public override string ToString()
        {
            return string.Join("; ", IndividualTasks.Select(x => x.ToString()));
        }
    }
}
