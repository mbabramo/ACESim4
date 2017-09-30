using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Azure;
using Microsoft.WindowsAzure; // Namespace for CloudConfigurationManager
using Microsoft.WindowsAzure.Storage; // Namespace for CloudStorageAccount
using Microsoft.WindowsAzure.Storage.Blob; // Namespace for Blob storage types

namespace ACESim.Util
{
    public static class AzureBlob
    {
        public static void WriteTextToBlob(string containerName, string fileName, bool publicAccess, string text)
        {
            var blockBlob = GetBlockBlob(containerName, fileName, publicAccess);

            var options = new BlobRequestOptions()
            {
                ServerTimeout = TimeSpan.FromMinutes(10)
            };

            using (var stream = new MemoryStream(Encoding.Default.GetBytes(text), false))
            {
                blockBlob.UploadFromStream(stream, null, options);
            }
        }

        public static string GetBlobText(string containerName, string fileName)
        {
            var blockBlob = GetBlockBlob(containerName, fileName, false);

            using (var stream = new MemoryStream())
            {
                string result = blockBlob.DownloadText();
                return result;
            }
        }

        private static CloudBlockBlob GetBlockBlob(string containerName, string fileName, bool publicAccess)
        {
            var container = GetContainer(containerName, publicAccess);
            
            CloudBlockBlob blockBlob = container.GetBlockBlobReference(fileName);

            return blockBlob;
        }

        private static CloudBlobContainer GetContainer(string containerName, bool publicAccess)
        {
// Retrieve storage account from connection string.
            CloudStorageAccount storageAccount = CloudStorageAccount.Parse(
                CloudConfigurationManager.GetSetting("StorageConnectionString"));

            // Create the blob client.
            CloudBlobClient blobClient = storageAccount.CreateCloudBlobClient();

            // Retrieve a reference to a container.
            CloudBlobContainer container = blobClient.GetContainerReference(containerName);

            // Create the container if it doesn't already exist.
            container.CreateIfNotExists();

            if (publicAccess)
                container.SetPermissions(
                    new BlobContainerPermissions {PublicAccess = BlobContainerPublicAccessType.Blob});
            return container;
        }
    }
}
