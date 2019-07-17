using System;
using System.Linq;

namespace ACESim.Util
{
    [Serializable]
    public class RepeatedTask
    {
        public string Name;
        public int ID;
        public IndividualTask[] IndividualTasks;

        public RepeatedTask(string name, int id, int repetitions)
        {
            Name = name;
            ID = id;
            IndividualTasks = new IndividualTask[repetitions];
            for (int repetition = 0; repetition < repetitions; repetition++)
            {
                IndividualTasks[repetition] = new IndividualTask(Name, ID, repetition);
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
