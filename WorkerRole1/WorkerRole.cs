using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Threading;
using Microsoft.WindowsAzure;
using Microsoft.WindowsAzure.Diagnostics;
using Microsoft.WindowsAzure.ServiceRuntime;
using Microsoft.WindowsAzure.Storage;
using ACESim;
using System.Threading.Tasks;
using Microsoft.WindowsAzure.Storage.Queue;
using System.Text;
using System.IO;

namespace WorkerRole1
{
    public class WorkerRole : RoleEntryPoint
    {
        public override void Run()
        {
            // This is a sample worker implementation. Replace with your logic.
            Trace.TraceInformation("WorkerRole1 entry point called", "Information");
            Microsoft.WindowsAzure.ServiceRuntime.RoleEnvironment.TraceSource.Switch.Level = SourceLevels.Information;

            //AzureSetup.SetConfigurationSettingPublisher();
            //bool somethingToDo = false;
            //while (!somethingToDo)
            //{
            //    somethingToDo = AzureQueue.Peek("mytest") != null;
            //    if (!somethingToDo)
            //        Thread.Sleep(100);
            //}
            //var result = AzureQueue.Pop("mytest");

            string lastInputObjectString = null;
            object inputObject = null;
            Dictionary<string, Strategy> alreadyDeserializedStrategies = new Dictionary<string, Strategy>();
            Tuple<string, OptimizePointsAndSmooth> lastOptimizePointsAndSmoothUsed = new Tuple<string, OptimizePointsAndSmooth>("N/A", null);
            const int minMillisecondsToWait = 100;
            const int maxMillisecondsToWait = 5000;
            const double multiplier = 1.5;
            int millisecondsToWait = minMillisecondsToWait;
            long waitingCount = 0;
            while (true)
            {
                Task heartbeat = null;
                CloudQueue toDoQueue = AzureQueue.GetCloudQueue("tasktodo");
                CloudQueueMessage message = null;
                CancellationTokenSource ts = null;
                CancellationToken ct;
                try
                {
                    message = null;
                    if (AzureQueue.Peek("tasktodo", toDoQueue) != null)
                    {
                        try
                        {
                            message = toDoQueue.GetMessage(TimeSpan.FromMinutes(1));
                        }
                        catch (Exception ex)
                        {
                            Trace.TraceError("Error getting message from queue " + ex.ExtractMessage());
                            continue;
                        }
                    }
                    AzureTaskInfo taskToDo = null;

                    if (message == null)
                    {
                        millisecondsToWait = (int)(millisecondsToWait * multiplier);
                        if (millisecondsToWait > maxMillisecondsToWait)
                            millisecondsToWait = maxMillisecondsToWait;
                    }
                    else
                    { 
                        millisecondsToWait = minMillisecondsToWait;
                        taskToDo = (AzureTaskInfo)ByteArrayConversions.ByteArrayToObject(message.AsBytes);
                    }

                    int taskSetNumRunning = ProcessInAzureWorkerRole.GetTaskSetNumRunning();
                    if (taskToDo != null && taskSetNumRunning != taskToDo.TaskSetNum)
                    {
                        Trace.TraceInformation("Disregarding old message " + taskToDo.TaskSetNum + "," + taskToDo.IndexNum);
                        toDoQueue.DeleteMessage(message);
                        taskToDo = null;
                    }
                    if (taskToDo != null)
                    {
                        if (heartbeat == null || heartbeat.IsCompleted || heartbeat.IsCanceled)
                        {
                            if (ts != null)
                                ts.Cancel();
                            ts = new CancellationTokenSource();
                            ct = ts.Token;
                            heartbeat = Task.Factory.StartNew(() =>
                            {
                                int i = 0;
                                while (!ct.IsCancellationRequested)
                                {
                                    if (message != null && i % 30 == 0)
                                    { 
                                        try
                                        { 
                                            // ensure that we don't conclude that this task failed
                                            toDoQueue.UpdateMessage(message, TimeSpan.FromMinutes(1), MessageUpdateFields.Visibility);
                                        }
                                        catch
                                        { // ignore failures to update the message
                                        }
                                    }
                                    Thread.Sleep(1000);
                                    i++;
                                }
                            }, ct);
                        }
                        bool reusingInputObject = false;
                        string inputObjectString = "input" + taskToDo.TaskSetNum.ToString();
                        if (inputObjectString == lastInputObjectString)
                            reusingInputObject = true;
                        else
                        {
                            try
                            { 
                                Stopwatch s = new Stopwatch();
                                s.Start();
                                if ((taskToDo.IPForData == null || taskToDo.ItemSizeForSocketsCommunication == null) && !AzureSetup.useBlobsForInterRoleCommunication)
                                    inputObject = null;
                                else if (AzureSetup.useBlobsForInterRoleCommunication)
                                    inputObject = AzureBlob.Download("inputblobs", inputObjectString);
                                else
                                    inputObject = AzureSockets.ClientGetItem(taskToDo.IPForData, inputObjectString, (int) taskToDo.ItemSizeForSocketsCommunication);
                                s.Stop();
                                if (inputObject != null)
                                    Trace.TraceInformation("Downloaded input object " + inputObjectString + " in " + s.ElapsedMilliseconds + " milliseconds");
                                lastInputObjectString = inputObjectString;
                            }
                            catch (Exception ex)
                            {

                                Trace.TraceError("Worker role failed to download input object because of error  " + ex.ExtractMessage());
                                Thread.Sleep(5000);
                                continue;
                            }
                        }
                        if (inputObject != null)
                        { // it could be null if this is left over from a previous run of the program
                            Stopwatch processWatch = new Stopwatch();
                            processWatch.Start();
                            Trace.TraceInformation("Begin processing on task " + taskToDo.TaskSetNum + "," + taskToDo.IndexNum + " of type " + taskToDo.TaskType);
                            CancellationTokenSource ts2 = new CancellationTokenSource();
                            Task<object> t = null;
                            if (taskToDo.TaskType == "OptimizePointsAndSmooth")
                            {
                                t = Task<object>.Run(() => { object o; OptimizePointsAndSmoothRemotely.FindAndOrOptimize(inputObject, taskToDo.TaskSetNum, taskToDo.IndexNum, ts2.Token, ref alreadyDeserializedStrategies, ref lastOptimizePointsAndSmoothUsed, out o); return o; }, ts2.Token);
                            }
                            else if (taskToDo.TaskType == "CutoffFinder")
                            {
                                t = Task<object>.Run(() =>
                                {
                                    IRemoteCutoffExecutor cutoffExecutor = (IRemoteCutoffExecutor)inputObject;
                                    if (!reusingInputObject)
                                        cutoffExecutor.RecoverState(ref alreadyDeserializedStrategies, ref lastOptimizePointsAndSmoothUsed);
                                    object o = cutoffExecutor.PlayCycleWhenExecutedRemotely(taskToDo.IndexNum, ts2.Token);
                                    return o;
                                }, ts2.Token);
                            }
                            else if (taskToDo.TaskType == "SettingsSet")
                            {
                                t = Task<object>.Run(() =>
                                {
                                    ExecutorMultiple.ExecuteSettingsSetFileName filename = (ExecutorMultiple.ExecuteSettingsSetFileName)inputObject;
                                    string basePath, settingsSubdirectory, reportsSubdirectory;
                                    //AzureCloudDrive myCloudDrive = SettingsAzure.SetUpCloudDrive("WR" + taskToDo.IndexNum + ".vhd", 20, out basePath);
                                    AzureCloudDrive myCloudDrive = new AzureCloudDrive();
                                    basePath = myCloudDrive.localStoragePath;
                                    SettingsAzure.CopySubfolders(basePath);
                                    settingsSubdirectory = Path.Combine(basePath, "Settings");
                                    reportsSubdirectory = Path.Combine(basePath, "Reports");
                                    string completePathToSettingFile = System.IO.Path.Combine(settingsSubdirectory, filename.SettingFileName);
                                    ExecutorMultiple exec = new ExecutorMultiple(basePath, completePathToSettingFile, new Interactionless(), new ProgressResumptionManager(ProgressResumptionOptions.ProceedNormallyWithoutSavingProgress, "N/A"));
                                    string reportResult = exec.RunSingleSettingsSet(taskToDo.IndexNum);
                                    AzureBlob.UploadDirectoryContentsToBlobStorage(reportsSubdirectory, AzureBlob.GetContainer("reports"), true);
                                    //myCloudDrive.Unmount();
                                    object o = new ExecutorMultiple.ExecuteSettingsSetRemotelyResult() { CompletedMetaReport = reportResult };
                                    return o;
                                }, ts2.Token);
                            }

                            else throw new Exception("Internal error: Unknown azure helper task type.");
                            bool finished = false;
                            while (!finished)
                            {
                                if (t.IsFaulted)
                                {
                                    Trace.TraceError("Task resulted in error: " + t.Exception.ExtractMessage());
                                    Thread.Sleep(5000);
                                    finished = true;
                                    message = null;
                                }
                                else if (t.IsCompleted)
                                {
                                    try
                                    { 
                                        AzureBlob.UploadSerializableObject(t.Result, "outputblobs", "output" + taskToDo.TaskSetNum.ToString() + "," + taskToDo.IndexNum.ToString());
                                        processWatch.Stop();
                                        Trace.TraceInformation("Task complete, produced " + "output" + taskToDo.TaskSetNum.ToString() + "," + taskToDo.IndexNum.ToString() + " total processing time (seconds): " + (processWatch.ElapsedMilliseconds / 1000.0));
                                        AzureQueue.Push("taskresult", taskToDo);
                                        finished = true;
                                    }
                                    catch (Exception ex)
                                    {
                                        Trace.TraceInformation("Azure upload and/or queuing of task result failed, trying again " + ex.ExtractMessage());
                                        Thread.Sleep(5000);
                                    }
                                }
                                else
                                {
                                    try
                                    {
                                        waitingCount++;
                                        if (waitingCount % 100 == 0) // only about once every five seconds -- too frequent will overwhelm the table processor
                                        { 
                                            taskSetNumRunning = ProcessInAzureWorkerRole.GetTaskSetNumRunning();
                                            if (taskSetNumRunning != taskToDo.TaskSetNum)
                                            {
                                                Trace.TraceInformation("Cancelling task " + taskToDo.TaskSetNum + "," + taskToDo.IndexNum + " because the running task set number is " + taskSetNumRunning);
                                                ts2.Cancel();
                                                finished = true;
                                            }
                                            else
                                                Thread.Sleep(50);
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        Trace.TraceInformation("Error getting task set num running or cancelling task; ignoring error: " + ex.ExtractMessage());
                                        // ignore -- we don't want a single failure with azure table to cause aborting of this process -- if the message doesn't match, the coordinator can always ignore it.
                                    }
                                }
                            }
                        }
                    }
                    if (message != null)
                    {
                        try
                        {
                            toDoQueue.DeleteMessage(message);
                        }
                        catch (Exception ex)
                        { // message may percolate again but it also may have been deleted.
                            Trace.TraceError("Error deleting queue message, so it will be reprocessed: " + ex.ExtractMessage());
                        }
                    }
                    if (ts != null)
                        ts.Cancel();
                    Thread.Sleep(Math.Max(millisecondsToWait, 1500)); // give cancellation enough time to be received
                    //Trace.TraceInformation("Working", "Information");
                }
                catch (Exception ex)
                { // ignore the error but log it
                    Trace.TraceError("Skipping queue message because of worker role error " + ex.ExtractMessage());
                    if (ts != null)
                        ts.Cancel();
                }
            }
        }

        public override bool OnStart()
        {
            // Set the maximum number of concurrent connections 
            ServicePointManager.DefaultConnectionLimit = 12;

            // Launch diagnostics listening.
            var config = DiagnosticMonitor.GetDefaultInitialConfiguration();
            config.Logs.ScheduledTransferPeriod = System.TimeSpan.FromSeconds(10.0);
            config.Logs.ScheduledTransferLogLevelFilter = Microsoft.WindowsAzure.Diagnostics.LogLevel.Information;
            DiagnosticMonitor.Start("Microsoft.WindowsAzure.Plugins.Diagnostics.ConnectionString", config);

            RoleEnvironment.Stopping += RoleEnvironmentStopping;

            // For information on handling configuration changes
            // see the MSDN topic at http://go.microsoft.com/fwlink/?LinkId=166357.

            return base.OnStart();
        }

        private void RoleEnvironmentStopping(object sender, RoleEnvironmentStoppingEventArgs e)
        {
            // Add code that is run when the role instance is being stopped
        }

        public override void OnStop()
        {
        }

    }
}
