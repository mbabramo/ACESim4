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
using System.IO;

namespace WorkerCoordinator
{
    public class WorkerRole : RoleEntryPoint
    {
        public override void Run()
        {
            try
            {
                // This is a sample worker implementation. Replace with your logic.
                Trace.TraceInformation("WorkerCoordinator entry point called", "Information");

                string mainSettingsFileName = AzureSetup.workerCoordinatorSettingsFile;
               
                // The outputs will be in a vhd in a blob. You can copy and paste this blob to see the contents.
                string cloudDriveName = "driveoutput.vhd";
                const int cloudDriveSize = 2000;
                string basePath;
                string settingsSubdirectory;
                SettingsAzure.SetUpCloudDrive(cloudDriveName, cloudDriveSize, out basePath);
                settingsSubdirectory = Path.Combine(basePath, "Settings");

                string fullSettingsFilePath = Path.Combine(settingsSubdirectory, mainSettingsFileName);
                Thread theThread = StartRunning.Go(basePath, fullSettingsFilePath, new AzureInteraction(), ProgressResumptionOptions.SkipToPreviousPositionIfResumptionInfoIsRecent);
                while (theThread.IsAlive)
                    Thread.Sleep(1000);

                Trace.TraceInformation("Complete.");
            }
            catch (Exception ex)
            {
                Trace.TraceError("Exception: " + ex.Message);
            }

            Thread.Sleep(Timeout.Infinite);
            // TODO: -- it would be good to terminate the deployment here instead (whether successfully completed or not).
            // It would also be good to make it so that if this role were to recycle the deployment would be destroyed on reboot.
            // AzureFluentManagement would be a good project to do this but might take some time.
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
