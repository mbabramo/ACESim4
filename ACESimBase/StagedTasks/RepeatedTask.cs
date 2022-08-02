using ACESimBase.StagedTasks;
using System;
using System.Linq;

namespace ACESimBase.StagedTasks
{
    [Serializable]
    public partial class RepeatedTask : IRepeatedTask
    {
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

        public IndividualTask FirstIncomplete() => Complete ? (IndividualTask)null :
            IndividualTasks
            .OrderBy(x => x.Complete) // incomplete first
            .ThenBy(x => x.Started != null) // not started first
            .ThenBy(x => x.Started) // oldest started first
            .FirstOrDefault();
        public override string ToString()
        {
            return String.Join("; ", IndividualTasks.Select(x => x.ToString()));
        }
    }
}
