using System;
using System.Collections;
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

        DateTime referenceTime = new DateTime(2022, 1, 1);
        
        public void StatusFromByteArray(byte[] bytes)
        {
            // 1 bit for each individual task indicating whether it has started;
            // 1 bit for each started task indicating whether it is complete;
            // 4 bytes = 32 bits for each pending task indicating when it was started, measured in minutes from some reference point 
            BitArray bits = new BitArray(bytes);
            BitArray scratch = new BitArray(32);
            int i = 0;
            foreach (var individualTask in IndividualTasks)
            {
                bool started;
                bool complete = false;
                int minutesFromReferenceTimeToStartIfPending = 0;
                started = bits[i++];
                if (started)
                {
                    complete = bits[i++];
                    if (!complete)
                    { // time is included only if task is pending
                        for (int j = 0; j < 32; j++)
                            scratch[j] = bits[i++];
                        byte[] fourBytes = ConvertToByteArray(scratch);
                        minutesFromReferenceTimeToStartIfPending = BitConverter.ToInt32(fourBytes, 0);
                    }
                }
                individualTask.Started = (started, complete) switch
                {
                    (true, true) => referenceTime, // it doesn't matter
                    (true, false) => referenceTime.AddMinutes((double)minutesFromReferenceTimeToStartIfPending),
                    (false, _) => null,
                };
                individualTask.Complete = complete;
            }
        }

        public byte[] StatusAsByteArray()
        {
            int numBits = NumIndividualTasks + NumTasksStarted + 32 * NumTasksPending;
            BitArray bits = new BitArray(numBits);
            int i = 0;
            foreach (var individualTask in IndividualTasks)
            {
                bool hasStarted = individualTask.Started != null;
                bits[i++] = hasStarted;
                if (hasStarted)
                {
                    bits[i++] = individualTask.Complete;
                    if (individualTask.Complete == false)
                    {
                        int minutesFromReferenceTimeToStartIfPending = (int)(individualTask.Started.Value - referenceTime).TotalMinutes;
                        byte[] bytes = BitConverter.GetBytes(minutesFromReferenceTimeToStartIfPending);
                        BitArray scratch = new BitArray(bytes);
                        for (int j = 0; j < 32; j++)
                            bits[i++] = scratch[j];
                    }
                }
            }
            return ConvertToByteArray(bits);
        }

        static byte[] ConvertToByteArray(BitArray bits)
        {
            // Make sure we have enough space allocated even when number of bits is not a multiple of 8
            var bytes = new byte[(bits.Length - 1) / 8 + 1];
            bits.CopyTo(bytes, 0);
            return bytes;
        }

        private IEnumerable<RepeatedTask> RepeatedTasks => Stages.SelectMany(x => x.RepeatedTasks);
        private IEnumerable<IndividualTask> IndividualTasks => RepeatedTasks.SelectMany(x => x.IndividualTasks);
        public int NumIndividualTasks => IndividualTasks.Count();
        public int NumTasksStarted => IndividualTasks.Where(x => x.Started != null).Count();
        public int NumTasksPending => IndividualTasks.Where(x => x.Started != null && !x.Complete).Count();
        public bool AllComplete => IndividualTasks.All(x => x.Complete);
        public double ProportionComplete => (double) IndividualTasks.Count(x => x.Complete) / (double) IndividualTasks.Count();

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
