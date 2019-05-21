using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Azure.Storage;
using Microsoft.Azure.Storage.Blob;

namespace ACESim.Util
{
    public static class AzureBlob
    {
        public static void SerializeObject(string containerName, string fileName, bool publicAccess, object theObject)
        {
            var blockBlob = GetBlockBlob(containerName, fileName, publicAccess);

            SerializeObject(theObject, blockBlob);
        }

        public static void SerializeObject(object theObject, CloudBlockBlob blockBlob, string leaseID = null)
        {
            var options = new BlobRequestOptions()
            {
                ServerTimeout = TimeSpan.FromMinutes(10)
            };

            using (var stream = new MemoryStream())
            {
                BinaryFormatter formatter = new BinaryFormatter();
                formatter.Serialize(stream, theObject);
                stream.Seek(0, SeekOrigin.Begin);
                AccessCondition accessCondition = leaseID == null ? null : new AccessCondition() {LeaseId = leaseID};
                blockBlob.UploadFromStream(stream, accessCondition, options);
                if (leaseID != null)
                    blockBlob.ReleaseLease(accessCondition);
            }
        }

        public static object GetSerializedObject(string containerName, string fileName)
        {
            var blockBlob = GetBlockBlob(containerName, fileName, true);

            return GetSerializedObject(blockBlob);
        }

        public static object GetSerializedObject(CloudBlockBlob blockBlob)
        {
            var options = new BlobRequestOptions()
            {
                ServerTimeout = TimeSpan.FromMinutes(10)
            };

            using (var stream = new MemoryStream())
            {
                blockBlob.DownloadToStream(stream, null, options);
                stream.Seek(0, SeekOrigin.Begin);
                BinaryFormatter formatter = new BinaryFormatter();
                if (stream.Length == 0)
                    return null;
                object theObject = formatter.Deserialize(stream);
                return theObject;
            }
        }

        public static object TransformSharedBlobObject(string containerName, string fileName, Func<object, object> transformFunction)
        {
            var leasedBlob = GetLeasedBlockBlob(containerName, fileName, true);
            return TransformSharedBlobObject(leasedBlob.blob, leasedBlob.lease, transformFunction);
        }

        public static object TransformSharedBlobObject(CloudBlockBlob blockBlob, string leaseID, Func<object, object> transformFunction)
        {
            object result;
            var serializedObject = GetSerializedObject(blockBlob);
            result = transformFunction(serializedObject);
            if (result != null)
                SerializeObject(result, blockBlob, leaseID);
            else
                ReleaseBlobLease(blockBlob, leaseID);
            return result;
        }

        public static (string lease, CloudBlockBlob blob) GetLeasedBlockBlob(string containerName, string fileName, bool publicAccess)
        {
            retry:
            try
            {
                var blockBlob = GetBlockBlob(containerName, fileName, publicAccess);
                if (!blockBlob.Exists())
                {
                    using (MemoryStream ms = new MemoryStream())
                    {
                        blockBlob.UploadFromStream(ms); //Empty memory stream. Will create an empty blob.
                    }
                }
                string lease = blockBlob.AcquireLease(TimeSpan.FromSeconds(59), null);
                return (lease, blockBlob);
            }
            catch (Microsoft.Azure.Storage.StorageException)
            { // failed to acquire lease
                goto retry;
            }
        }

        public static void ReleaseBlobLease(CloudBlockBlob blockBlob, string leaseID)
        {
            AccessCondition accessCondition = new AccessCondition() { LeaseId = leaseID };
            blockBlob.ReleaseLease(accessCondition);
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
