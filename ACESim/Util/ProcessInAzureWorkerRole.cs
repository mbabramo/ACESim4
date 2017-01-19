using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Auth;
using Microsoft.WindowsAzure.Storage.Queue;
using System.Net;

namespace ACESim
{
    [Serializable]
    public class AzureTaskInfo
    {
        public int TaskSetNum;
        public int IndexNum;
        public string TaskType;
        public IPEndPoint IPForData;
        public int? ItemSizeForSocketsCommunication;
    }

    public class ProcessInAzureWorkerRole
    {
        
        static int taskSetNum = -1;
        static bool outputTraceMessages = false;

        public ProcessInAzureWorkerRole()
        {
            if (taskSetNum == -1)
            {   
                Random r = new Random(unchecked((int)DateTime.Now.Ticks)); // use random numbers that are independent of RandomGenerator. This reduces risk of conflicts from a previous execution of the program.
                taskSetNum = r.Next(Int32.MaxValue);
            }
        }
        
        private void SetTaskSetNumRunning(int taskSetNumRunning)
        {
            TableServiceContextAccess<DataTableServiceEntity<int>> context = null;
            DataTableServiceEntity<int> theTableEntity = AzureTable<int>.LoadFirstOrDefaultByPartitionKey("NoPartitionKey", ref context, "taskSetNumRunning");
            if (theTableEntity == null)
                AzureTable<int>.Add(taskSetNumRunning, "NoPartitionKey", "taskSetNumRunning");
            else
            {
                theTableEntity.SetData(taskSetNumRunning);
                AzureTable<int>.Update(theTableEntity, context, "taskSetNumRunning");
            }
        }

        public static int GetTaskSetNumRunning()
        {
            TableServiceContextAccess<DataTableServiceEntity<int>> context = null;
            DataTableServiceEntity<int> theTableEntity = AzureTable<int>.LoadFirstOrDefaultByPartitionKey("NoPartitionKey", ref context, "taskSetNumRunning");
            if (theTableEntity == null)
                return -1;
            else
                return theTableEntity.GetData();
        }

        public void ExecuteTask(object serializableInputObject, string taskType, int minimumRepetitions, bool allowMoreRepetitionsThanMinimum, Func<object, int, bool> completedTaskProcessor)
        {
            taskSetNum++;
            SetTaskSetNumRunning(taskSetNum);

            // Delete the contents of the input queue and the output queue.
            AzureQueue.Clear("tasktodo");
            AzureQueue.Clear("taskresult");
            // AzureBlob.DeleteItems("inputblobs"); we don't do this because it might still be needed by a worker role that has started
            AzureBlob.DeleteItems("outputblobs");

            if (serializableInputObject is ISerializationPrep)
                ((ISerializationPrep)serializableInputObject).PreSerialize();

            // Make the object available to the worker roles.
            int? itemSizeForSocketsCommunication = null;
            if (AzureSetup.useBlobsForInterRoleCommunication)
                AzureBlob.UploadSerializableObject(serializableInputObject, "inputblobs", "input" + taskSetNum.ToString());
            else
            {
                AzureSockets.ClearHostedItems();
                byte[] item = BinarySerialization.GetByteArray(serializableInputObject);
                itemSizeForSocketsCommunication = item.Length;
                AzureSockets.HostedItems.AddOrUpdate("input" + taskSetNum.ToString(), item, (s, b) => null );
                AzureSockets.StartServer("InputDataEndPoint");
            }

            if (serializableInputObject is ISerializationPrep)
                ((ISerializationPrep)serializableInputObject).UndoPreSerialize();

            CloudQueue taskToDoQueue = AzureQueue.GetCloudQueue("tasktodo");
            CloudQueue taskResultQueue = AzureQueue.GetCloudQueue("taskresult");
            List<int> tasksAlreadyDownloaded = new List<int>();
            Microsoft.WindowsAzure.StorageClient.CloudBlobContainer outputBlobsContainer = AzureBlob.GetContainer("outputblobs");
            // Place in the input queue minimum number of tasks to be completed.
            int tasksEnqueued = 0;
            for (int i = 0; i < minimumRepetitions; i++)
            {
                if (outputTraceMessages)
                    Trace.TraceInformation("Pushing task " + tasksEnqueued + ": " + DateTime.Now.ToString("MM/dd/yyyy hh:mm:ss.fff tt"));
                AzureQueue.Push("tasktodo", new AzureTaskInfo() { TaskSetNum = taskSetNum, IndexNum = tasksEnqueued, TaskType = taskType, IPForData = AzureSetup.useBlobsForInterRoleCommunication ? null : AzureSockets.ServerAddress, ItemSizeForSocketsCommunication = itemSizeForSocketsCommunication }, taskToDoQueue);
                tasksEnqueued++;
            }

            // Poll the output queue to see if there is a task completed. 
            List<Tuple<Task<object>,int>> downloadTasks = new List<Tuple<Task<object>,int>>();
            const int minMillisecondsToWait = 100;
            const int maxMillisecondsToWait = 5000;
            const double multiplier = 1.5;
            int millisecondsToWait = minMillisecondsToWait;
            bool done = false;
            while (!done)
            {
                bool pause = true;
                // first, process the queued messages by initiating blob downloads
                List<object> queuedMessages = null;
                try
                { 
                    queuedMessages = AzureQueue.Pop("taskresult", 32, taskResultQueue);
                }
                catch
                {
                    Thread.Sleep(5000);
                    continue;
                }
                if (!queuedMessages.Any())
                {
                    millisecondsToWait = (int)(millisecondsToWait * multiplier);
                    if (millisecondsToWait > maxMillisecondsToWait)
                        millisecondsToWait = maxMillisecondsToWait;
                }
                else
                    millisecondsToWait = minMillisecondsToWait;
                foreach (object message in queuedMessages)
                {
                    AzureTaskInfo resultTask = (AzureTaskInfo)message;
                    if (outputTraceMessages)
                        Trace.TraceInformation("Received message " + resultTask.TaskSetNum + "," + resultTask.IndexNum);
                    if (resultTask != null && resultTask.TaskSetNum == taskSetNum && !tasksAlreadyDownloaded.Contains(resultTask.IndexNum)) // we shouldn't get a message twice, but this is an extra precaution
                    {
                        if (outputTraceMessages)
                            Trace.TraceInformation("Downloading message " + resultTask.TaskSetNum + "," + resultTask.IndexNum);
                        pause = false; 
                        string outputBlobName = "output" + taskSetNum.ToString() + "," + resultTask.IndexNum.ToString();
                        Task<object> downloadTask = AzureBlob.DownloadAsync(outputBlobsContainer, outputBlobName, delete:true);
                        downloadTasks.Add(new Tuple<Task<object>,int>(downloadTask, resultTask.IndexNum));
                        tasksAlreadyDownloaded.Add(resultTask.IndexNum);
                    }
                }
                // second, scan for completed blob downloads (may be from previous dequeuing)
                List<Tuple<Task<object>,int>> tasksToRemove = new List<Tuple<Task<object>,int>>();
                foreach (Tuple<Task<object>,int> t in downloadTasks)
                {
                    if (t.Item1.IsCompleted)
                    {
                        pause = false;
                        tasksToRemove.Add(t);
                        try
                        {
                            done = completedTaskProcessor(t.Item1.Result, t.Item2); // we can be done more than once; as long as we have the data completed, we might as well use it.
                            if (outputTraceMessages)
                                Trace.TraceInformation("Processing downloaded message " + t.Item2 + " complete? " + done);
                        }
                        catch
                        {
                        }
                    }
                }
                if (!done && allowMoreRepetitionsThanMinimum)
                {
                    taskToDoQueue.FetchAttributes();
                    int messageCount = taskToDoQueue.ApproximateMessageCount.Value;
                    int minimumMessageCount = 4;
                    if (messageCount < minimumMessageCount )
                    {
                        for (int message = 0; message < minimumMessageCount - messageCount; message++)
                        {
                            if (outputTraceMessages)
                                Trace.TraceInformation("Pushing supplemental task " + tasksEnqueued + ": " + DateTime.Now.ToString("MM/dd/yyyy hh:mm:ss.fff tt"));
                            AzureQueue.Push("tasktodo", new AzureTaskInfo() { TaskSetNum = taskSetNum, IndexNum = tasksEnqueued, TaskType = taskType, IPForData = AzureSetup.useBlobsForInterRoleCommunication ? null : AzureSockets.ServerAddress, ItemSizeForSocketsCommunication = itemSizeForSocketsCommunication }, taskToDoQueue);
                            tasksEnqueued++;
                        }
                    }       
                }
               
                foreach (Tuple<Task<object>,int> t in tasksToRemove)
                    downloadTasks.Remove(t);
                if (pause)
                    Thread.Sleep(millisecondsToWait);
            }
            // Delete the contents of the input queue and the output queue.
            AzureQueue.Clear("tasktodo");
            AzureQueue.Clear("taskresult");
            Task.WaitAll(downloadTasks.Select(x => x.Item1).ToArray()); // must wait for the downloads to complete before deleting the output blobs, b/c we might still be downloading them
            AzureBlob.DeleteItems("outputblobs");

            SetTaskSetNumRunning(-1);
        }


    }
}
