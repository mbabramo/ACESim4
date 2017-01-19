using Microsoft.WindowsAzure.StorageClient;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESim
{
    public static class AzureQueueWithBlobObject
    {

        public static void Push(string queueName, Object theObject)
        {
            string guid = Guid.NewGuid().ToString();
            
        }

        //public static Object Peek(string queueName)
        //{
        //    CloudQueue q = GetCloudQueue(queueName);
        //    CloudQueueMessage message = q.PeekMessage();
        //    if (message == null)
        //        return null;
        //    return ByteArrayConversions.ByteArrayToObject(message.AsBytes);
        //}

        //public static Object Pop(string queueName)
        //{
        //    CloudQueue q = GetCloudQueue(queueName);
        //    CloudQueueMessage message = q.GetMessage();
        //    Object theObject = ByteArrayConversions.ByteArrayToObject(message.AsBytes);
        //    q.DeleteMessage(message);
        //    return theObject;
        //}

        //public static List<Object> Pop(string queueName, int numMessages)
        //{
        //    CloudQueue q = GetCloudQueue(queueName);
        //    List<CloudQueueMessage> messages = q.GetMessages(numMessages).ToList();
        //    List<Object> objects = messages.Select(x => ByteArrayConversions.ByteArrayToObject(x.AsBytes)).ToList();
        //    messages.ForEach(x => q.DeleteMessage(x));
        //    return objects;
        //}

        //public static void Clear(string queueName)
        //{
        //    CloudQueue q = GetCloudQueue(queueName);
        //    q.Clear();
        //}

        //public static List<Object> GetMessages(string queueName, int numMessages)
        //{
        //    CloudQueue q = GetCloudQueue(queueName);
        //    List<CloudQueueMessage> messages = q.GetMessages(numMessages).ToList();
        //    List<Object> objects = messages.Select(x => ByteArrayConversions.ByteArrayToObject(x.AsBytes)).ToList();
        //    messages.ForEach(x => q.DeleteMessage(x));
        //    return objects;
        //}
    }
}
