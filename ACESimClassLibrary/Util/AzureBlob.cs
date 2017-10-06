using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Formatters.Binary;
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
        public static void SerializeObject(string containerName, string fileName, bool publicAccess, object theObject)
        {
            var blockBlob = GetBlockBlob(containerName, fileName, publicAccess);

            var options = new BlobRequestOptions()
            {
                ServerTimeout = TimeSpan.FromMinutes(10)
            };

            using (var stream = new MemoryStream())
            {
                BinaryFormatter formatter = new BinaryFormatter();
                formatter.Serialize(stream, theObject);
                blockBlob.UploadFromStream(stream, null, options);
            }
        }

        public static object GetSerializedObject(string containerName, string fileName)
        {
            var blockBlob = GetBlockBlob(containerName, fileName, true);

            var options = new BlobRequestOptions()
            {
                ServerTimeout = TimeSpan.FromMinutes(10)
            };

            using (var stream = new MemoryStream())
            {
                blockBlob.DownloadToStream(stream, null, options);
                BinaryFormatter formatter = new BinaryFormatter();
                object theObject = formatter.Deserialize(stream);
                return theObject;
            }
        }

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

            if (!blockBlob.Exists())
                return null;

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
            string storageConnectionString = "DefaultEndpointsProtocol=https;AccountName=acesim;AccountKey=hUGjg6lQjXYORQxzyUbDl9joTgJ4xlXQ6kb2d3Lm2DSx8YsgWm29UDoezSOj6IIAG6oZL7oJvenO80KD2GLl+g=="; // TODO: Use a technology like key vault to hide this and ideally put it in config file in the class library
            CloudStorageAccount storageAccount = CloudStorageAccount.Parse(storageConnectionString
                );

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
