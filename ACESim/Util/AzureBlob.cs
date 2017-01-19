using Microsoft.WindowsAzure;
//using Microsoft.WindowsAzure.Storage.Blob;

using Microsoft.WindowsAzure.StorageClient;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text;
using System.Threading.Tasks;

namespace ACESim
{
    public static class AzureBlob
    {
        [DebuggerStepThrough] // ignore the exceptions, which are necessarily part of the logic
        public static bool Exists(this CloudBlob blob)
        {
            try
            {
                blob.FetchAttributes();
                return true;
            }
            catch (StorageClientException e)
            {
                if (e.ErrorCode == StorageErrorCode.ResourceNotFound)
                {
                    return false;
                }
                else
                {
                    throw;
                }
            }
        }

        public static CloudBlobContainer GetContainer(string containerName)
        {
            if (containerName.ToLower() != containerName)
                throw new Exception("Container name may not contain capital letters.");

            // Retrieve storage account from connection string.
            CloudStorageAccount storageAccount = AzureSetup.GetCloudStorageAccount("BlobConnectionString"); // CloudStorageAccount.Parse(CloudConfigurationManager.GetSetting("BlobConnectionString"));

            // Create the blob client.
            CloudBlobClient blobClient = storageAccount.CreateCloudBlobClient();
            blobClient.RetryPolicy = RetryPolicies.RetryExponential(99, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(90), TimeSpan.FromSeconds(5));

            // Retrieve a reference to a container. 
            CloudBlobContainer container = blobClient.GetContainerReference(containerName);

           //  Create the container if it doesn't already exist.
           container.CreateIfNotExist();

           return container;
        }

        public static void DeleteItems(string containerName)
        {
            CloudBlobContainer container = GetContainer(containerName);
            DeleteItems(container);
        }

        public static void DeleteItems(CloudBlobContainer container, string except = null)
        {
            IEnumerable<IListBlobItem> items = container.ListBlobs();
            foreach (var item in items)
            {
                if (except != null && item.Uri.ToString().Contains(except))
                    continue;
                Trace.TraceInformation("Deleting blob " + item.Uri);
                ((CloudBlob)item).DeleteIfExists();
            }
        }

        public static void UploadSerializableObject(object serializableObject, string containerName, string blobName, bool replace = true)
        {
            CloudBlobContainer container = GetContainer(containerName);
            UploadSerializableObject(serializableObject, container, blobName, replace);
        }

        public static void UploadSerializableObject(object serializableObject, CloudBlobContainer container, string blobName, bool replace = true)
        {
            CloudBlockBlob blockBlob = container.GetBlockBlobReference(blobName);
            if (replace || !blockBlob.Exists())
            {
                byte[] byteArray = BinarySerialization.GetByteArray(serializableObject);
                MemoryStream ms = new MemoryStream(byteArray);
                blockBlob.UploadFromStream(ms);
            }
        }

        // Note that this ignores folders.
        public static void UploadDirectoryContentsToBlobStorage(string path, CloudBlobContainer container, bool replace = true)
        {
            // Check if the target directory exists, if not, create it.
            if (!Directory.Exists(path))
                throw new Exception("Directory not found.");
            DirectoryInfo source = new DirectoryInfo(path);

            foreach (FileInfo fi in source.GetFiles())
                UploadFileToBlob(path, fi.Name, container, replace);
        }

        public static void UploadFileToBlob(string path, string file, CloudBlobContainer container, bool replace = true)
        {
            CloudBlockBlob blockBlob = container.GetBlockBlobReference(file);
            if (replace || !blockBlob.Exists())
            {
                FileStream fs = new FileStream(Path.Combine(path, file), FileMode.Open);
                blockBlob.UploadFromStream(fs);
            }
        }

        public static object Download(string containerName, string blobName)
        {
            CloudBlobContainer container = GetContainer(containerName);
            return Download(container, blobName);
        }

        public static object Download(CloudBlobContainer container, string blobName)
        {
            object theObject = null;
            CloudBlockBlob blockBlob = container.GetBlockBlobReference(blobName);
            MemoryStream ms = new MemoryStream();
            blockBlob.DownloadToStream(ms);
            ms.Seek(0, SeekOrigin.Begin);
            IFormatter formatter = new BinaryFormatter();
            theObject = formatter.Deserialize(ms);
            return theObject;
        }

        public static Task<object> DownloadAsync(string containerName, string blobName, bool delete = false)
        {
            CloudBlobContainer container = GetContainer(containerName);
            return DownloadAsync(container, blobName, delete);
        }

        public static Task<object> DownloadAsync(CloudBlobContainer container, string blobName, bool delete = false)
        {
            CloudBlockBlob blockBlob = container.GetBlockBlobReference(blobName);
            MemoryStream ms = new MemoryStream();
            Task<object> theTask = Task.Factory.FromAsync<object>(
                blockBlob.BeginDownloadToStream(ms, null, null), 
                ar =>
                {
                    object theObject = null;
                    blockBlob.EndDownloadToStream(ar);
                    ms.Seek(0, SeekOrigin.Begin);
                    IFormatter formatter = new BinaryFormatter();
                    theObject = formatter.Deserialize(ms);
                    if (delete)
                    {
                        var ar2 = blockBlob.BeginDeleteIfExists(null, null);
                        var task = Task.Factory.FromAsync<Boolean>(ar2, blockBlob.EndDeleteIfExists);
                    }
                    return theObject;
                });
            return theTask;
        }

        public static void Delete(string containerName, string blobName)
        {
            CloudBlobContainer container = GetContainer(containerName);
            Delete(container, blobName);
        }

        public static void Delete(CloudBlobContainer container, string blobName)
        {
            CloudBlockBlob blockBlob = container.GetBlockBlobReference(blobName);
            Trace.TraceInformation("Deleting item " + blobName);
            var ar = blockBlob.BeginDeleteIfExists(null, null);
            var task = Task.Factory.FromAsync<Boolean>(ar, blockBlob.EndDeleteIfExists);
        }
    }
}
