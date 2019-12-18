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
        public TimeSpan LongestDuration => IndividualTasks.Any() ? IndividualTasks.Max(x => x.Duration) : TimeSpan.FromSeconds(0);

        public void Update(IndividualTask taskCompleted, bool readyForAnotherTask, out IndividualTask taskToDo, out bool allComplete)
        {
            TimeSpan minSpanBeforeStartingAlreadyStartedJob = LongestDuration;
            RepeatedTask repeatedTask = null;
            if (taskCompleted != null)
            {
                // mark the completed task as complete
                repeatedTask = RepeatedTasks.First(x => x.Name == taskCompleted.Name && x.ID == taskCompleted.ID); // repeatedtask has same name and id as each individualtask within it
                repeatedTask.IndividualTasks[taskCompleted.Repetition].Complete = true;
                if (!readyForAnotherTask)
                {
                    taskToDo = null;
                    allComplete = false; // assume all are not complete
                    return;
                }
                taskToDo = repeatedTask.FirstIncomplete(); // look at same repeated task for a new job before considering other stages altogether.
                if (taskToDo != null)
                {
                    allComplete = false;
                    taskToDo.Started = DateTime.Now;
                    return;
                }
            }
            var taskStage = Stages.FirstOrDefault(x => !x.Complete);
            if (taskStage == null)
            { // all stages complete
                taskToDo = null;
                allComplete = true;
                return; 
            }
            repeatedTask = taskStage.IncompleteRepeatedTask;
            if (repeatedTask == null)
            {
                allComplete = false;
                taskToDo = null;
                return;
            }
            taskToDo = repeatedTask.FirstIncomplete();
            allComplete = false;
            if (taskToDo.Started != null && taskToDo.Started + minSpanBeforeStartingAlreadyStartedJob > DateTime.Now)
                taskToDo = null;
            else
                taskToDo.Started = DateTime.Now;
        }

        public override string ToString()
        {
            return String.Join("\n\n", Stages.Select(x => x.ToString()));
        }
    }
}
