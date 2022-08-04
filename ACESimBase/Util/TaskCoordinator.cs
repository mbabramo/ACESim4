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
        public bool Complete => IndividualTasks.All(x => x.Complete);
        public double ProportionComplete => (double) IndividualTasks.Count(x => x.Complete) / (double) IndividualTasks.Count();
        public TimeSpan LongestDuration => IndividualTasks.Any() ? IndividualTasks.Max(x => x.DurationOfLongestComplete) : TimeSpan.FromSeconds(0);

        public void Update(List<IndividualTask> tasksCompleted, bool readyForAnotherTask, int numTasksToRequest, out List<IndividualTask> tasksToDo, out bool allComplete)
        {
            TimeSpan minSpanBeforeStartingAlreadyStartedJob = TimeSpan.FromSeconds(0); // ALTERNATIVE: LongestDuration;
            RepeatedTask repeatedTask = null;
            if (tasksCompleted != null)
            {
                foreach (var taskCompleted in tasksCompleted)
                {
                    // mark the completed task as complete
                    repeatedTask = RepeatedTasks.First(x => x.TaskType == taskCompleted.TaskType && x.ID == taskCompleted.ID); // repeatedtask has same name and id as each individualtask within it
                    var individualTask = repeatedTask.IndividualTasks.First(x => x.Repetition == taskCompleted.Repetition && x.RestrictToScenarioIndex == taskCompleted.RestrictToScenarioIndex);
                    individualTask.Complete = true;
                    individualTask.Completed = DateTime.Now;
                }
                if (!readyForAnotherTask)
                {
                    tasksToDo = null;
                    allComplete = false; // assume all are not complete
                    return;
                }
                tasksToDo = repeatedTask.FirstIncompleteTasks(numTasksToRequest); // look at same repeated task for a new job before considering other stages altogether.
                if (tasksToDo != null)
                {
                    allComplete = false;
                    foreach (var taskToDo in tasksToDo)
                        taskToDo.Started = DateTime.Now;
                    return;
                }
            }
            var taskStage = Stages.FirstOrDefault(x => !x.Complete);
            if (taskStage == null)
            { // all stages complete
                tasksToDo = null;
                allComplete = true;
                return; 
            }
            repeatedTask = taskStage.IncompleteRepeatedTask;
            if (repeatedTask == null)
            {
                allComplete = false;
                tasksToDo = null;
                return;
            }
            tasksToDo = repeatedTask.FirstIncompleteTasks(numTasksToRequest);
            allComplete = false;
            foreach (var taskToDo in tasksToDo.ToList())
            {
                if (taskToDo.Started != null && (taskToDo.Started + minSpanBeforeStartingAlreadyStartedJob > DateTime.Now || repeatedTask.AvoidRedundantExecution))
                    tasksToDo.Remove(taskToDo);
                else
                    taskToDo.Started = DateTime.Now;
            }
            if (!tasksToDo.Any())
                tasksToDo = null;
        }

        public override string ToString()
        {
            return String.Join("\n\n", Stages.Select(x => x.ToString()));
        }
    }
}
