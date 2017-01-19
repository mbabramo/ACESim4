using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.WindowsAzure.ServiceRuntime;
using Microsoft.WindowsAzure.StorageClient;
using Microsoft.WindowsAzure;

namespace ACESim
{
    public class AzureStartupDiagnostic
    {
        const string StartupBlobContainerName = "startupdiagnostic"; // MUST be all lowercase

        public AzureStartupDiagnostic()
        {
            if (!RoleEnvironment.IsAvailable)
                return;

            // Initialize the storage variables.
            InitializeStorage();
            // Get Reference to error container.
            var container = blobStorage.
                       GetContainerReference(StartupBlobContainerName);


            if (container != null)
            {
                // Will create a new entry in the container
                // and upload the text representing the 
                // exception.
                container.GetBlobReference(
                   String.Format(
                      "Success {0}-{1}.txt",
                      RoleEnvironment.CurrentRoleInstance.Id,
                      TestableDateTime.Now.ToString())
                   ).UploadText("Successfully started");
            }

            AppDomain appDomain = AppDomain.CurrentDomain;
            appDomain.UnhandledException += new UnhandledExceptionEventHandler(appDomain_UnhandledException);

        }


        // The following initialization of blob storage is so that we can handle
        // the unhandled exception in initial startup.
        private static bool storageInitialized = false;
        private static object gate = new Object();
        private static CloudBlobClient blobStorage;
        //private static CloudQueueClient queueStorage;

        private void InitializeStorage()
        {

            if (!RoleEnvironment.IsAvailable)
                return;

            if (storageInitialized)
            {
                return;
            }

            lock (gate)
            {
                if (storageInitialized)
                {
                    return;
                }


                // read account configuration settings

                AzureSetup.SetConfigurationSettingPublisher();
                var storageAccount =
                  AzureSetup.GetCloudStorageAccount("DataConnectionString");

                // create blob container for images
                blobStorage =
                    storageAccount.CreateCloudBlobClient();
                CloudBlobContainer container = blobStorage.
                    GetContainerReference(StartupBlobContainerName);

                container.CreateIfNotExist();

                // configure container for public access
                var permissions = container.GetPermissions();
                permissions.PublicAccess =
                     BlobContainerPublicAccessType.Container;
                container.SetPermissions(permissions);

                storageInitialized = true;
            }
        }

        void appDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {

            if (!RoleEnvironment.IsAvailable)
                return;

            // Initialize the storage variables.
            InitializeStorage();

            // Get Reference to error container.
            var container = blobStorage.
                       GetContainerReference(StartupBlobContainerName);


            if (container != null)
            {
                // Retrieve last exception.
                Exception ex = e.ExceptionObject as Exception;

                if (ex != null)
                {
                    // Will create a new entry in the container
                    // and upload the text representing the 
                    // exception.
                    container.GetBlobReference(
                       String.Format(
                          "Error {0}-{1}.txt",
                          RoleEnvironment.CurrentRoleInstance.Id,
                          TestableDateTime.Now.ToString())
                       ).UploadText(ex.ToString());
                }
            }

        }

    }
}
