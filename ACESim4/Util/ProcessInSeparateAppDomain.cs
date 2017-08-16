using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SeparateAppDomain
{
    public static class ProcessInSeparateAppDomainSetupExample
    {
        static int numTasks = 200;

        public static void DoIt()
        {
            // we will use an array for input data, so that each task can get its own data
            Random r = new Random(0);
            object[] inputs = new object[numTasks];
            for (int i = 0; i < numTasks; i++)
                inputs[i] = (object) r.NextDouble(); 

            ProcessInSeparateAppDomain p = new ProcessInSeparateAppDomain();
            object[] outputs = p.ExecuteTask(inputs, "SeparateAppDomain.ProcessInSeparateAppDomainActionExample", numTasks);
            Debug.WriteLine("Final: ");
            foreach (var o in outputs)
                Debug.WriteLine((long)o);
            Debug.WriteLine(outputs.Sum(x => (long)x));
        }
    }


    public class ProcessInSeparateAppDomainActionExample : ProcessInSeparateAppDomainActionBase
    {

        public override void DoProcessing(object input, int index, CancellationToken ct, out object output)
        {
            Random r = new Random(index);
            double startValue = (double)(((object[])input)[index]);
            long count = 0;
            for (long i = 0; i < 10000000; i++)
            {
                double newValue = 0;
                do
                {
                    count++;
                    newValue = r.NextDouble();
                }
                while (newValue > startValue);

            }
            // Return the total number of times we ran this.
            Debug.WriteLine("Index " + index + " count " + count);
            output = (object)count;
        }
    }

    public abstract class ProcessInSeparateAppDomainActionBase
    {
        public ProcessInSeparateAppDomainActionBase()
        {
            // The Processor data can be used to set processor affinity for the entire process.
            object processor = AppDomain.CurrentDomain.GetData("Processor");
            if (processor != null)
            {
                int processorNum = 1;
                for (int p = 0; p < (int?)processor; p++)
                    processorNum *= 2;
                System.Diagnostics.Process.GetCurrentProcess().Threads[0].ProcessorAffinity = (System.IntPtr)processorNum;
            }
            Task.Run(() => { WaitForTasksToProcess(); }); // do this asynchronously so that we can return 
        }

        public void WaitForTasksToProcess()
        {
            object input = AppDomain.CurrentDomain.GetData("Input"); // note that we load the input data only once -- then we just need an index to do more processing
            bool exitCommandReceived = false;
            while (!exitCommandReceived)
            {
                int? index = (int?)AppDomain.CurrentDomain.GetData("Index");
                if (index != null)
                {
                    AppDomain.CurrentDomain.SetData("Index", null);
                    AppDomain.CurrentDomain.SetData("Input", null);
                    AppDomain.CurrentDomain.SetData("Output", null);
                    CancellationToken ct = new CancellationToken();
                    DoProcessing(input, (int)index, ct, out object output);
                    AppDomain.CurrentDomain.SetData("Output", output);
                }
                else
                {
                    string exitData = (string) AppDomain.CurrentDomain.GetData("Exit");
                    exitCommandReceived = (exitData == "Sent");
                    if (exitCommandReceived)
                        AppDomain.CurrentDomain.SetData("Exit", "Received");
                    else
                        Thread.Sleep(10); // wait for something to do
                }
            }
        }

        public abstract void DoProcessing(object input, int index, CancellationToken ct, out object output);

    }

    public class ProcessInSeparateAppDomain
    {
        private AppDomain SetUpAppDomain(object serializableInputObject, int processorAffinity)
        {
            // Create a new application domain.
            AppDomain domain = AppDomain.CreateDomain("Test");

            // Place the list in the data cache of the new application domain.
            domain.SetData("Input", serializableInputObject);
            domain.SetData("Processor", processorAffinity);

            return domain;
        }

        private void SetIndexToProcess(AppDomain d, int? indexToProcess)
        {
            d.SetData("Output", null); // must do this before we start, so we don't incorrectly conclude that this is complete
            d.SetData("Index", (object)indexToProcess);
        }

        private object GetOutputDataIfComplete(AppDomain d)
        {
            object outputData = d.GetData("Output");
            return outputData;
        }

        public object[] ExecuteTask(object serializableInputObject, string fullyQualifiedTaskName, int exactNumberOfTimesToExecute)
        {
            object[] returnValues = new object[exactNumberOfTimesToExecute];
            object lockObj = new object();
            int numComplete = 0;
            ExecuteTask(serializableInputObject, fullyQualifiedTaskName, exactNumberOfTimesToExecute, exactNumberOfTimesToExecute, (output, index) =>
                {
                    lock(lockObj)
                    {
                        // Debug.WriteLine("Reported as completed: " + index + " total complete: " + (numComplete + 1));
                        returnValues[index] = output;
                        numComplete++;
                        return numComplete == exactNumberOfTimesToExecute;
                    }
                }
            );
            return returnValues;
        }

        /// <summary>
        /// This function executes the specified task on the available processors, at least minimumRepetitions times, until the task is found complete.
        /// </summary>
        /// <param name="serializableInputObject">A binary serialization of the object to be passed to the task via the AppDomain's data in the Input object</param>
        /// <param name="fullyQualifiedTaskName">The name of the task to perform</param>
        /// <param name="minimumRepetitions">The minimum number of times that are anticipated to be necessary to complete the task; note that the completedTaskProcessor may still find the task to be complete on a lower number of repetitions</param>
        /// <param name="completedTaskProcessor">This accepts the output data and task index and returns whether the task is complete</param>
        public void ExecuteTask(object serializableInputObject, string fullyQualifiedTaskName, int minimumRepetitions, int maxRepetitions, Func<object, int, bool> completedTaskProcessor, bool ensureProcessorIsCalledInOrder = false)
        {
            // set up of task scheduler and AppDomains
            int maxNumberSimultaneously = System.Environment.ProcessorCount;
            int numAppDomains = Math.Min(maxRepetitions, maxNumberSimultaneously);
            AppDomain[] domains = new AppDomain[numAppDomains];
            for (int d = 0; d < numAppDomains; d++)
            {
                domains[d] = SetUpAppDomain(serializableInputObject, d);
                domains[d].CreateInstance(Assembly.GetExecutingAssembly().FullName, fullyQualifiedTaskName);
            }

            // set up initial list of tasks
            List<Task<object>> tasks = new List<Task<object>>();
            List<int> idsOfTasksToProcess = new List<int>();
            List<DateTime> launchTimeForTasks = new List<DateTime>();
            for (int t = 0; t < numAppDomains; t++)
            {
                // since we are just starting, t will be the value for both indexToProcess and domainNumber. But later,
                // we may use any domain to process any later indices.
                int indexToProcess = t;
                idsOfTasksToProcess.Add(indexToProcess);
                int domainNumber = t;
                AppDomain applicableDomain = domains[domainNumber];
                tasks.Add(LaunchTaskThatTriggersProcessingAndWaitsForCompletion(indexToProcess, applicableDomain));
                launchTimeForTasks.Add(DateTime.Now);
            }

            // wait for a task to complete. figure out if we're all done when it completes. If so, unload appdomains. Otherwise, trigger another process.
            int lastTaskIndexLaunched = idsOfTasksToProcess.Max();
            bool complete = false;
            Dictionary<int, object> outputsNotProcessedYet = new Dictionary<int, object>();
            int lastTaskIndexProcessed = -1;
            while (!complete)
            {
                Task<object>[] tasksArray = tasks.Where(x => x != null).ToArray();
                int taskArrayIndex = Task.WaitAny(tasksArray);
                int domainIndex = tasks.IndexOf(tasksArray[taskArrayIndex]);
                int taskIndex = idsOfTasksToProcess[domainIndex];
                object outputData = tasksArray[taskArrayIndex].Result;
                if (ensureProcessorIsCalledInOrder)
                    outputsNotProcessedYet.Add(taskIndex, outputData);
                //Debug.WriteLine("ExecuteTask Complete: " + taskIndex + " in domain " + domainIndex + " with id " + tasksArray[taskArrayIndex].Id + " elapsed time: " + (DateTime.Now - launchTimeForTasks[taskIndex]).TotalMilliseconds);
                bool keepLookingForOutputToProcess = true;
                while (keepLookingForOutputToProcess && !complete)
                {
                    if (!ensureProcessorIsCalledInOrder)
                    {
                        keepLookingForOutputToProcess = false;
                        lastTaskIndexProcessed = taskIndex;
                    }
                    else
                    {
                        outputData = outputsNotProcessedYet.ContainsKey(lastTaskIndexProcessed + 1) ? outputsNotProcessedYet[lastTaskIndexProcessed + 1] : null;
                        if (outputData == null)
                            keepLookingForOutputToProcess = false;
                        else
                        {
                            lastTaskIndexProcessed++;
                            outputsNotProcessedYet.Remove(lastTaskIndexProcessed);
                        }
                    }
                    if (outputData != null)
                    {
                        complete = completedTaskProcessor(outputData, lastTaskIndexProcessed);
                        if (complete)
                            SendExitMessageAndUnloadAppDomains(domains);
                    }
                }
                if (!complete)
                {
                    lastTaskIndexLaunched = SpinUpNewTaskInSlot(maxRepetitions, domains, tasks, idsOfTasksToProcess, lastTaskIndexLaunched, domainIndex);
                    launchTimeForTasks.Add(DateTime.Now);
                }
            }
        }

        private int SpinUpNewTaskInSlot(int maxRepetitions, AppDomain[] domains, List<Task<object>> tasks, List<int> idsOfTasksToProcess, int lastTaskIndexLaunched, int domainIndex)
        {
            // spin up a new task in this slot
            if (lastTaskIndexLaunched < maxRepetitions - 1)
            {
                lastTaskIndexLaunched++;
                AppDomain applicableDomain = domains[domainIndex];
                idsOfTasksToProcess[domainIndex] = lastTaskIndexLaunched;
                tasks[domainIndex] = LaunchTaskThatTriggersProcessingAndWaitsForCompletion(lastTaskIndexLaunched, applicableDomain);
                //Debug.WriteLine("Assigning new task " + lastTaskIndexLaunched + " to domain " + domainIndex + " with id " + tasks[domainIndex].Id);
            }
            else
            {
                tasks[domainIndex] = null;
                if (tasks.All(x => x == null))
                    throw new Exception("All subtasks completed but task is still incomplete, and maximum repetitions have been hit.");
            }
            return lastTaskIndexLaunched;
        }

        private static void SendExitMessageAndUnloadAppDomains(AppDomain[] domains)
        {

            foreach (AppDomain d in domains)
            {
                d.SetData("Exit", "Sent"); // send exit command
            }

            // give a little time for a graceful exit
            Stopwatch s = new Stopwatch();
            const int maxMillisecondsToWait = 200000;
            while (s.ElapsedMilliseconds < maxMillisecondsToWait && domains.Any(x => ((string)x.GetData("Exit")) != "Received"))
                ;

            if (s.ElapsedMilliseconds >= maxMillisecondsToWait)
                Debug.WriteLine("Possible problem with AppDomain that has not received Exit message.");

            foreach (AppDomain d in domains)
            {
                AppDomain.Unload(d); // since it is a separate appdomain, we must do this manually
            }
        }

        private Task<object> LaunchTaskThatTriggersProcessingAndWaitsForCompletion(int indexToProcess, AppDomain applicableDomain)
        {
            TaskFactory factory = new TaskFactory();
            Task<object> executingTask = factory.StartNew(() =>
            {
                SetIndexToProcess(applicableDomain, indexToProcess);
                bool thisTaskComplete = false;
                bool abort = false;
                object outputData = null;
                while (!thisTaskComplete && !abort)
                {
                    Thread.Sleep(10); // must be considerably smaller than amount of time for application to exit gracefully
                    abort = ((string)applicableDomain.GetData("Exit")) != null; // check if the domain is about to be unloaded, we will exit this and return null before the unloading occurs, otherwise we will be querying an unloaded appdomain
                    if (!abort)
                    {
                        outputData = GetOutputDataIfComplete(applicableDomain);
                        thisTaskComplete = outputData != null;
                    }
                }
                return outputData;
            }
            );
            return executingTask;
        }
    }

}