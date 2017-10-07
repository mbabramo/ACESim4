using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESim.Util
{
    public class IndividualTask
    {
        public int Repetition; 
        public DateTime? Started;
        public bool Complete;
    }

    public class RepeatedTask
    {
        public string ID;
        public IndividualTask[] IndividualTasks;

        public RepeatedTask(string id, int repetitions)
        {
            ID = id;
            IndividualTasks = new IndividualTask[repetitions];
            for (int i = 1; i <= repetitions; i++)
                IndividualTasks[i - 1].Repetition = i;
        }

        public bool Complete => IndividualTasks.All(x => x.Complete);
        public int? LowestAvailable => Complete ? (int?) null : IndividualTasks.OrderBy(x => x.Complete).ThenBy(x => x.Started != null).ThenBy(x => x.Started).First().Repetition;
    }

    public class TaskStage
    {
        public List<RepeatedTask> Tasks = null;
        public bool Complete => Tasks.All(x => x.Complete);
        public RepeatedTask TaskWithLowestAvailable => Tasks.Where(x => !x.Complete).OrderBy(x => x.LowestAvailable).First();
    }

    public class TaskCoordinator
    {
        public List<TaskStage> Stages;
        public List<RepeatedTask> RepeatedTasks => Stages.SelectMany(x => x.Tasks).ToList();
        public void Update(string idOfTaskCompleted, int? repetitionOfTaskCompleted, out string idOfNewTask, out int? repetitionOfNewTask)
        {
            RepeatedTask repeatedTask = null;
            if (idOfTaskCompleted != null && repetitionOfTaskCompleted != null)
            {
                repeatedTask = RepeatedTasks.First(x => x.ID == idOfTaskCompleted);
                repeatedTask.IndividualTasks[(int) (repetitionOfTaskCompleted - 1)].Complete = true;
                repetitionOfNewTask = repeatedTask.LowestAvailable;
                if (repetitionOfNewTask != null)
                {
                    repeatedTask.IndividualTasks[(int) (repetitionOfNewTask - 1)].Started = DateTime.Now;
                    idOfNewTask = repeatedTask.ID;
                    return;
                }
            }
            var taskStage = Stages.FirstOrDefault(x => !x.Complete);
            if (taskStage == null)
            { // all stages complete
                idOfNewTask = null;
                repetitionOfNewTask = null;
                return; 
            }
            repeatedTask = taskStage.TaskWithLowestAvailable;
            idOfNewTask = repeatedTask.ID;
            repetitionOfNewTask = repeatedTask.LowestAvailable;
        }
    }
}
