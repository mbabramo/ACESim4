using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESim.Util
{

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
        public int IndividualTaskCount => IndividualTasks.Count();
        public double ProportionComplete => (double) IndividualTasks.Count(x => x.Complete) / (double) IndividualTasks.Count();

        public void Update(IndividualTask taskCompleted, bool readyForAnotherTask, out IndividualTask taskToDo)
        {
            RepeatedTask repeatedTask = null;
            if (taskCompleted != null)
            {
                repeatedTask = RepeatedTasks.First(x => x.Name == taskCompleted.Name && x.ID == taskCompleted.ID); // repeatedtask has same name and id as each individualtask within it
                repeatedTask.IndividualTasks[taskCompleted.Repetition].Complete = true;
                if (!readyForAnotherTask)
                {
                    taskToDo = null;
                    return;
                }
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
