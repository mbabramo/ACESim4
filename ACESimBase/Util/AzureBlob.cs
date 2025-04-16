using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text;
using System.Threading.Tasks;
using ACESimBase.Resources; // NOTE: If not defined, then you need to add cloudpw.cs (see email) to Resources folder in ACESimBase. This is not committed to git for security reasons
using JetBrains.Annotations;
using Microsoft.Azure.Storage;
using Microsoft.Azure.Storage.Blob;

namespace ACESim.Util
{
    public static class AzureBlob
    {
        public static void SerializeToFileOrAzure(object toSerialize, string path, string containerName, string fileName, bool useAzure)
        {
            if (useAzure)
            {
                AzureBlob.SerializeObject(containerName, fileName, false, toSerialize);
            }
            else
            {
                string fullFilename = Path.Combine(path, fileName);
                BinarySerialization.SerializeObject(fullFilename, toSerialize);
            }
        }

        public static void SaveByteArrayToFileOrAzure(byte[] byteArray, string path, string containerName, string fileName, bool useAzure)
        {
            if (useAzure)
            {
                var blockBlob = GetBlockBlob(containerName, fileName, true);
                AzureBlob.SaveByteArray(byteArray, blockBlob);
            }
            else
            {
                string fullFilename = Path.Combine(path, fileName);
                File.WriteAllBytes(fullFilename, byteArray);
            }
        }

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
                AccessCondition accessCondition = leaseID == null ? null : new AccessCondition() { LeaseId = leaseID };
                blockBlob.UploadFromStream(stream, accessCondition, options);
                if (leaseID != null)
                    blockBlob.ReleaseLease(accessCondition);
            }
        }

        public static void SaveByteArray(byte[] byteArray, CloudBlockBlob blockBlob, string leaseID = null)
        {
            var options = new BlobRequestOptions()
            {
                ServerTimeout = TimeSpan.FromMinutes(10)
            };

            using (var stream = new MemoryStream(byteArray))
            {
                stream.Seek(0, SeekOrigin.Begin);
                AccessCondition accessCondition = leaseID == null ? null : new AccessCondition() { LeaseId = leaseID };
                blockBlob.UploadFromStream(stream, accessCondition, options);
                if (leaseID != null)
                    blockBlob.ReleaseLease(accessCondition);
            }
        }

        public static byte[] GetByteArrayFromFileOrAzure(string path, string containerName, string fileName, bool useAzure)
        {
            if (useAzure)
            {
                var blockBlob = GetBlockBlob(containerName, fileName, true);
                return GetByteArray(blockBlob);
            }
            else
            {
                string fullFilename = Path.Combine(path, fileName);
                byte[] result = File.ReadAllBytes(fullFilename);
                return result;
            }
        }

        public static object GetSerializedObjectFromFileOrAzure(string path, string containerName, string fileName, bool useAzure)
        {
            if (useAzure)
            {
                return GetSerializedObject(containerName, fileName);
            }
            else
            {
                string fullFilename = Path.Combine(path, fileName);
                return BinarySerialization.GetSerializedObject(fullFilename);
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



        public static byte[] GetByteArray(CloudBlockBlob blockBlob)
        {
            var options = new BlobRequestOptions()
            {
                ServerTimeout = TimeSpan.FromMinutes(10)
            };

            using (var stream = new MemoryStream())
            {
                blockBlob.DownloadToStream(stream, null, options);
                stream.Seek(0, SeekOrigin.Begin);
                byte[] bytes = stream.ToArray();
                return bytes;
            }
        }


        public static object TransformSharedBlobOrFileObject(string path, string containerName, string fileName, Func<object, object> transformFunction, bool useAzure)
        {
            if (useAzure)
                return TransformSharedBlobObject(containerName, fileName, transformFunction);
            else
                return TransformSharedFileBlob(path, fileName, transformFunction);
        }

        public static byte[] TransformSharedBlobOrFileByteArray(string path, string containerName, string fileName, Func<byte[], byte[]> transformFunction, bool useAzure)
        {
            if (useAzure)
                return TransformSharedBlobByteArray(containerName, fileName, transformFunction);
            else
                return TransformSharedFileByteArray(path, fileName, transformFunction);
        }

        public static object TransformSharedBlobObject(string containerName, string fileName, Func<object, object> transformFunction)
        {
            var leasedBlob = GetLeasedBlockBlob(containerName, fileName, true);
            return TransformSharedBlobObject(leasedBlob.blob, leasedBlob.lease, transformFunction);
        }

        public static byte[] TransformSharedBlobByteArray(string containerName, string fileName, Func<byte[], byte[]> transformFunction)
        {
            var leasedBlob = GetLeasedBlockBlob(containerName, fileName, true);
            return TransformSharedBlobByteArray(leasedBlob.blob, leasedBlob.lease, transformFunction);
        }

        public static byte[] TransformSharedBlobByteArray(CloudBlockBlob blockBlob, string leaseID, Func<byte[], byte[]> transformFunction)
        {
            byte[] bytes = GetByteArray(blockBlob);
            var result = transformFunction(bytes);
            if (result != null)
                SaveByteArray(result, blockBlob, leaseID);
            else
                ReleaseBlobLease(blockBlob, leaseID);
            return result;
        }

        public static object TransformSharedBlobObject(CloudBlockBlob blockBlob, string leaseID, Func<object, object> transformFunction)
        {
            object result;
            var serializedObject = GetSerializedObject(blockBlob);
            Stopwatch s = new Stopwatch();
            s.Start();
            result = transformFunction(serializedObject);
            if (result != null)
                SerializeObject(result, blockBlob, leaseID);
            else
                ReleaseBlobLease(blockBlob, leaseID);
            return result;
        }

        public static byte[] TransformSharedFileByteArray(string path, string filename, Func<byte[], byte[]> transformFunction)
        {
            using (FileStream stream = GetFileStream(path, filename))
            {
                MemoryStream ms = new MemoryStream();
                stream.CopyTo(ms);
                var initialState = ms.ToArray();
                byte[] finalState = transformFunction(initialState);
                if (finalState != null)
                {
                    stream.Seek(0, SeekOrigin.Begin);
                    stream.Write(finalState, 0, finalState.Length);
                }
                stream.Close();
                return finalState;
            }
        }

        public static object TransformSharedFileBlob(string path, string filename, Func<object, object> transformFunction)
        {
            using (FileStream stream = GetFileStream(path, filename))
            {
                stream.Seek(0, SeekOrigin.Begin);
                BinaryFormatter formatter = new BinaryFormatter();
                object initialState = null;
                if (stream.Length > 0)
                    initialState = formatter.Deserialize(stream);
                object finalState = transformFunction(initialState);
                formatter = new BinaryFormatter();
                stream.Seek(0, SeekOrigin.Begin);
                if (finalState != null)
                    formatter.Serialize(stream, finalState);
                return finalState;
            }
        }

        public static string TransformSharedBlobString(string containerName, string fileName, Func<string, string> transformFunction)
        {
            var leasedBlob = GetLeasedBlockBlob(containerName, fileName, true);
            return TransformSharedBlobString(leasedBlob.blob, leasedBlob.lease, transformFunction);
        }

        public static string TransformSharedBlobString(CloudBlockBlob blockBlob, string leaseID, Func<string, string> transformFunction)
        {
            string text = blockBlob.DownloadText();
            string result = transformFunction(text);
            if (result != null)
                WriteTextToBlob(blockBlob, result, leaseID);
            else
                ReleaseBlobLease(blockBlob, leaseID);
            return result;
        }

        public static string TransformSharedFileString(string path, string filename, Func<string, string> transformFunction)
        {
            using (FileStream stream = GetFileStream(path, filename))
            {
                string initialState;
                using (StreamReader reader = new StreamReader(stream, Encoding.Default, true, 1024, true))
                {
                    stream.Seek(0, SeekOrigin.Begin);
                    initialState = reader.ReadToEnd();
                }
                string finalState = transformFunction(initialState);
                if (finalState != null)
                {
                    stream.Seek(0, SeekOrigin.Begin);
                    stream.SetLength(0);
                    using (StreamWriter writer = new StreamWriter(stream, Encoding.Default, 1024, true))
                    {
                        writer.Write(finalState);
                        writer.Flush();
                    }
                }
                stream.Close();
                return finalState;
            }
        }

        public static T TransformSharedBlobObjectJson<T>(string containerName, string fileName, Func<T, T> transformFunction)
        {
            var leasedBlob = GetLeasedBlockBlob(containerName, fileName, true);
            return TransformSharedBlobObjectJson<T>(leasedBlob.blob, leasedBlob.lease, transformFunction);
        }

        public static T TransformSharedBlobObjectJson<T>(CloudBlockBlob blockBlob, string leaseID, Func<T, T> transformFunction)
        {
            string jsonText = blockBlob.DownloadText();
            T initialState = default(T);
            if (!string.IsNullOrEmpty(jsonText))
            {
                initialState = System.Text.Json.JsonSerializer.Deserialize<T>(jsonText);
            }
            T finalState = transformFunction(initialState);
            if (finalState != null)
            {
                string outputJson = System.Text.Json.JsonSerializer.Serialize(finalState);
                WriteTextToBlob(blockBlob, outputJson, leaseID);
            }
            else
            {
                ReleaseBlobLease(blockBlob, leaseID);
            }
            return finalState;
        }

        public static T TransformSharedFileObjectJson<T>(string path, string filename, Func<T, T> transformFunction)
        {
            using (FileStream stream = GetFileStream(path, filename))
            {
                string initialJson;
                using (StreamReader reader = new StreamReader(stream, Encoding.Default, true, 1024, true))
                {
                    stream.Seek(0, SeekOrigin.Begin);
                    initialJson = reader.ReadToEnd();
                }
                T initialState = default(T);
                if (!string.IsNullOrEmpty(initialJson))
                {
                    initialState = System.Text.Json.JsonSerializer.Deserialize<T>(initialJson);
                }
                T finalState = transformFunction(initialState);
                if (finalState != null)
                {
                    string outputJson = System.Text.Json.JsonSerializer.Serialize(finalState);
                    stream.Seek(0, SeekOrigin.Begin);
                    stream.SetLength(0);
                    using (StreamWriter writer = new StreamWriter(stream, Encoding.Default, 1024, true))
                    {
                        writer.Write(outputJson);
                        writer.Flush();
                    }
                }
                stream.Close();
                return finalState;
            }
        }


        private static void WriteTextToBlob(CloudBlockBlob blockBlob, string text, string leaseID)
        {
            var options = new BlobRequestOptions()
            {
                ServerTimeout = TimeSpan.FromMinutes(10)
            };

            using (var stream = new MemoryStream(Encoding.Default.GetBytes(text), false))
            {
                AccessCondition accessCondition = leaseID == null ? null : new AccessCondition() { LeaseId = leaseID };
                blockBlob.UploadFromStream(stream, accessCondition, options);
            }
        }

        public static FileStream GetFileStream(string path, string filename)
        {
            int retryInterval = 10;
            string fullFilename = Path.Combine(path, filename);
        retry:
            try
            {
                var exists = File.Exists(fullFilename);
                FileStream fileStream;
                if (!exists)
                {
                    using (fileStream = File.Create(fullFilename))
                    {

                    }
                }
                fileStream = File.Open(fullFilename, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                return fileStream;
            }
            catch (IOException)
            { // file in use
                Task.Delay(retryInterval);
                if (retryInterval < 10_000)
                    retryInterval = (int)(retryInterval * 1.5); // use exponential backoff -- requesting lease too much causes very long delays
                goto retry;
            }
        }

        public static (string lease, CloudBlockBlob blob) GetLeasedBlockBlob(string containerName, string fileName, bool publicAccess)
        {
            int retryInterval = 10;
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
                Task.Delay(retryInterval);
                if (retryInterval < 10_000)
                    retryInterval = (int)(retryInterval * 1.5); // use exponential backoff -- requesting lease too much causes very long delays
                goto retry;
            }
        }

        public static void ReleaseBlobLease(CloudBlockBlob blockBlob, string leaseID)
        {
            AccessCondition accessCondition = new AccessCondition() { LeaseId = leaseID };
            blockBlob.ReleaseLease(accessCondition);
        }

        public static void WriteTextToFileOrAzure(string containerName, string path, string fileName, bool publicAccess, string text, bool useAzure)
        {
            if (useAzure)
            {
                WriteTextToBlob(containerName, fileName, publicAccess, text);
            }
            else
            {
                string fullFilename = Path.Combine(path, fileName);
                System.IO.File.WriteAllText(fullFilename, text);
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
            string storageConnectionString = CloudPW.GetCloudStorageAccountConnectionString(); // NOTE: If cloudpw is not defined, then you need to add cloudpw.cs (see email). This is not committed to git for security reasons // TODO: Use a technology like key vault to hide this. for now, though, we're just not putting the cloudpw file onto github, thus maintaining the secret.
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
                    new BlobContainerPermissions { PublicAccess = BlobContainerPublicAccessType.Blob });
            return container;
        }
    }
}
