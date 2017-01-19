using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.WindowsAzure.StorageClient;
using Microsoft.WindowsAzure;
using Microsoft.WindowsAzure.ServiceRuntime;

namespace ACESim
{
    public class AzureCloudDrive
    {
        const string BlobContainerName = "drives"; // must be lowercase
        string DriveFileName; // specified by user -- should end in vhd

        // The following initialization of blob storage is so that we can handle
        // the unhandled exception in initial startup.
        private bool storageInitialized = false;
        private static object gate = new Object();
        private static CloudBlobClient blobStorage;
        //private static CloudQueueClient queueStorage;
        private static CloudBlobContainer container;
        private static CloudStorageAccount storageAccount;
        public string driveLetter { get; set; }
        public CloudDrive myCloudDrive;
        public string localStoragePath;

        public string InitializeStorage()
        {
            bool inDevFabric = RoleEnvironment.IsEmulated; // initialize to default
            if (RoleEnvironment.IsAvailable)
            {
                var endpoint = RoleEnvironment.CurrentRoleInstance.InstanceEndpoints.Values.FirstOrDefault();
                if (endpoint != null)
                {
                    string ip = endpoint.IPEndpoint.Address.ToString();
                    inDevFabric = ip.Contains("127.0.0.1");
                }
            }

            return InitializeStorageHelper(inDevFabric);
        }

        private string InitializeStorageHelper(bool inDevFabric)
        {
            if (storageInitialized)
            {
                return localStoragePath;
            }

            lock (gate)
            {
                if (storageInitialized)
                {
                    return localStoragePath;
                }


                // read account configuration settings

                AzureSetup.SetConfigurationSettingPublisher();
                if (inDevFabric) // in the development fabric, we can't mount a drive from real azure storage
                    storageAccount = CloudStorageAccount.DevelopmentStorageAccount;
                else
                    storageAccount = AzureSetup.GetCloudStorageAccount("DataConnectionString");

                if (RoleEnvironment.IsAvailable)
                {
                    LocalResource localCache = RoleEnvironment.GetLocalResource("AzureDriveCache");
                    localStoragePath = localCache.RootPath;
                    CloudDrive.InitializeCache(localCache.RootPath, localCache.MaximumSizeInMegabytes);
                }

                // create blob container for images
                blobStorage =
                    storageAccount.CreateCloudBlobClient();
                container = blobStorage.
                    GetContainerReference(BlobContainerName);

                container.CreateIfNotExist();

                // configure container for public access
                var permissions = container.GetPermissions();
                permissions.PublicAccess =
                     BlobContainerPublicAccessType.Container;
                container.SetPermissions(permissions);

                storageInitialized = true;

                return localStoragePath;
            }
        }

        public void Unmount()
        {
            if (myCloudDrive != null)
                myCloudDrive.Unmount();
        }

        public void Mount(int driveSizeInMB = 64)
        {
            // Create cloud drive
            myCloudDrive = storageAccount.CreateCloudDrive(
                blobStorage
                .GetContainerReference(BlobContainerName)
                .GetPageBlobReference(DriveFileName)
                .Uri.ToString()
            );

            try
            {
                myCloudDrive.CreateIfNotExist(driveSizeInMB);
                // Note: If we get an Unknown Error exception here, we probably need to clear out development storage.
            }
            catch (CloudDriveException)
            {
                // handle exception here
                // exception is also thrown if all is well but the drive already exists
            }

            driveLetter = myCloudDrive.Mount(25, DriveMountOptions.Force);
            // if this fails when not deployed, then go to Storage UI and Reset Azure Drive.
        }

        // this constructor is for local storage use.
        public AzureCloudDrive()
        {
            InitializeStorage(); 
        }

        public AzureCloudDrive(string driveFileName, int driveSizeInMB = 64)
        {
            DriveFileName = driveFileName;

            InitializeStorage();
            Mount(driveSizeInMB);
        }
    }
}
