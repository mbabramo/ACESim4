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

        public bool Complete => IndividualTasks.All(x => x.Complete);
        public int? IndexOfFirstIncomplete => Complete ? (int?) null : IndividualTasks.OrderBy(x => x.Complete).ThenBy(x => x.Started != null).ThenBy(x => x.Started).First().Repetition;

        public IndividualTask FirstIncomplete()
        {
            int? lowestAvailable = IndexOfFirstIncomplete;
            if (lowestAvailable == null)
                return null;
            return IndividualTasks[(int) lowestAvailable];
        }

        public override string ToString()
        {
            return String.Join("; ", IndividualTasks.Select(x => x.ToString()));
        }
    }
}
