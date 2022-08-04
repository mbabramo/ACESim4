using ACESim.Util;
using Lazinator.Collections;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESimBase.StagedTasks
{

    public partial class TaskCoordinator
    {

        public LazinatorList<TaskStage> Stages;

        public TaskCoordinator(IEnumerable<TaskStage> stages)
        {
            Stages = new LazinatorList<TaskStage>(stages);
        }
        
        public TaskCoordinator(string path, string containerName, string fileName, bool useAzure)
        {
            byte[] bytes = AzureBlob.GetByteArrayFromFileOrAzure(path, containerName, fileName, useAzure);
            Stages = new LazinatorList<TaskStage>(bytes);
        }

        public void SaveToFileOrAzure(string path, string containerName, string fileName, bool useAzure)
        {
            byte[] bytes = SerializeToBytes();
            AzureBlob.SaveByteArrayToFileOrAzure(bytes, path, containerName, fileName, useAzure);
        }

        public byte[] SerializeToBytes()
        {
            Stages.SerializeLazinator();
            byte[] bytes = Stages.LazinatorMemoryStorage.EnumerateBytes().ToArray();
            return bytes;
        }

        public static TaskCoordinator TransformShared(string path, string containerName, string fileName, bool useAzure, Func<TaskCoordinator, TaskCoordinator> transformFunc)
        {
            TaskCoordinator tc = AzureBlob.BlobOrFileExists(path, containerName, fileName, useAzure) ? new TaskCoordinator(path, containerName, fileName, useAzure) : null;
            tc = transformFunc(tc);
            if (tc != null)
                tc.SaveToFileOrAzure(path, containerName, fileName, useAzure);
            return tc;
        }

        private IEnumerable<RepeatedTask> RepeatedTasks => Stages.SelectMany(x => x.RepeatedTasks);
        private IEnumerable<IndividualTask> IndividualTasks => RepeatedTasks.SelectMany(x => x.IndividualTasks);
        public int IndividualTaskCount => IndividualTasks.Count();
        public bool Complete => IndividualTasks.All(x => x.Complete);
        public double ProportionComplete => IndividualTasks.Count(x => x.Complete) / (double)IndividualTasks.Count();
        public TimeSpan LongestDuration => IndividualTasks.Any() ? IndividualTasks.Max(x => x.DurationOfLongestComplete) : TimeSpan.FromSeconds(0);

        public void Update(IndividualTask taskCompleted, bool readyForAnotherTask, out IndividualTask taskToDo, out bool allComplete)
        {
            TimeSpan minSpanBeforeStartingAlreadyStartedJob = TimeSpan.FromSeconds(0); // ALTERNATIVE: LongestDuration;
            RepeatedTask repeatedTask = null;
            if (taskCompleted != null)
            {
                // mark the completed task as complete
                repeatedTask = RepeatedTasks.First(x => x.TaskType == taskCompleted.TaskType && x.ID == taskCompleted.ID); // repeatedtask has same name and id as each individualtask within it
                var individualTask = repeatedTask.IndividualTasks.First(x => x.Repetition == taskCompleted.Repetition && x.RestrictToScenarioIndex == taskCompleted.RestrictToScenarioIndex);
                individualTask.Complete = true;
                individualTask.Completed = DateTime.Now;
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
            if (taskToDo.Started != null && (taskToDo.Started + minSpanBeforeStartingAlreadyStartedJob > DateTime.Now || repeatedTask.AvoidRedundantExecution))
                taskToDo = null;
            else
                taskToDo.Started = DateTime.Now;
        }

        public override string ToString()
        {
            return string.Join("\n\n", Stages.Select(x => x.ToString()));
        }
    }
}
