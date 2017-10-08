using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESim.Util
{
    [Serializable]
    public class IndividualTask
    {
        public string Name;
        public int ID;
        public int Repetition; 
        public DateTime? Started;
        public bool Complete;

        public IndividualTask(string name, int id, int repetition)
        {
            Name = name;
            ID = id;
            Repetition = repetition;
        }

        public override string ToString()
        {
            return $"{Name} {ID} {Repetition} Started:{Started} Complete:{Complete}";
        }
    }

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

    [Serializable]
    public class TaskCoordinator
    {
        public List<TaskStage> Stages;

        public TaskCoordinator(List<TaskStage> stages)
        {
            Stages = stages;
        }

        private IEnumerable<RepeatedTask> RepeatedTasks => Stages.SelectMany(x => x.RepeatedTasks);
        private IEnumerable<IndividualTask> IndividualTasks => RepeatedTasks.SelectMany(x => x.IndividualTasks);

        public void Update(IndividualTask taskCompleted, out IndividualTask taskToDo)
        {
            RepeatedTask repeatedTask = null;
            if (taskCompleted != null)
            {
                repeatedTask = RepeatedTasks.First(x => x.Name == taskCompleted.Name && x.ID == taskCompleted.ID);
                repeatedTask.IndividualTasks[taskCompleted.Repetition].Complete = true;
                taskToDo = repeatedTask.FirstIncomplete();
                if (taskToDo != null)
                {
                    taskToDo.Started = DateTime.Now;
                    return;
                }
            }
            var taskStage = Stages.FirstOrDefault(x => !x.Complete);
            if (taskStage == null)
            { // all stages complete
                taskToDo = null;
                return; 
            }
            repeatedTask = taskStage.TaskWithLowestAvailable;
            taskToDo = repeatedTask.FirstIncomplete();
            taskToDo.Started = DateTime.Now;
        }
        public override string ToString()
        {
            return String.Join("\n\n", Stages.Select(x => x.ToString()));
        }
    }
}
