using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
// Note: This is upgraded to Azure version 2
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Auth;
using Microsoft.WindowsAzure.Storage.Queue;
using ACESim;
using System.Diagnostics;

public static class AzureQueue
{
    public static CloudQueue GetCloudQueue(string queueName)
    {
        if (queueName != queueName.ToLower())
            throw new Exception("Queue name must consist of all lowercase letters.");
        CloudStorageAccount storageAccount = AzureSetup.GetCloudStorageAccountV2("DataConnectionString");
        CloudQueueClient Qsvc = storageAccount.CreateCloudQueueClient();
        Microsoft.WindowsAzure.Storage.RetryPolicies.IRetryPolicy exponentialRetryPolicy = new Microsoft.WindowsAzure.Storage.RetryPolicies.ExponentialRetry(TimeSpan.FromSeconds(10), 100);
        Qsvc.RetryPolicy = exponentialRetryPolicy;
        CloudQueue q = Qsvc.GetQueueReference(queueName);
        CreateIfNotExistsDebuggerHidden(q); // won't throw if it doesn't exist
        return q;
    }

    [System.Diagnostics.DebuggerHidden]
    public static void CreateIfNotExistsDebuggerHidden(CloudQueue q)
    {
        try
        {
            q.CreateIfNotExists();
        }
        catch
        {
        }
    }

    public static void Push(string queueName, Object theObject, CloudQueue q = null, TimeSpan? timeToCompleteQueueMessage = null)
    {
        byte[] theMessage = ByteArrayConversions.ObjectToByteArray(theObject);
        CloudQueueMessage theNewMessage = new CloudQueueMessage(theMessage);
        if (q == null)
            q = GetCloudQueue(queueName);
        q.AddMessageAsync(theNewMessage, null, timeToCompleteQueueMessage, null, null);
    }

    public static Object Peek(string queueName, CloudQueue q = null)
    {
        if (q == null)
            q = GetCloudQueue(queueName);
        CloudQueueMessage message = q.PeekMessage();
        if (message == null)
            return null;
        return ByteArrayConversions.ByteArrayToObject(message.AsBytes);
    }

    public static Object Pop(string queueName, CloudQueue q = null)
    {
        if (q == null)
            q = GetCloudQueue(queueName);
        CloudQueueMessage message = q.GetMessage();
        if (message == null)
            return null;
        Object theObject = ByteArrayConversions.ByteArrayToObject(message.AsBytes);
        try
        {
            q.DeleteMessage(message);
        }
        catch
        { // we shouldn't end up here, because of the invisibility period we've set, but we sometimes seem to anyway
        }
        return theObject;
    }

    public static List<Object> Pop(string queueName, int numMessages, CloudQueue q = null)
    {
        if (q == null)
            q = GetCloudQueue(queueName);
        numMessages = Math.Min(numMessages, 32); // maximum number permissible
        List<CloudQueueMessage> messages = q.GetMessages(numMessages).ToList();
        List<Object> objects = messages.Select(x => ByteArrayConversions.ByteArrayToObject(x.AsBytes)).ToList();
        messages.ForEach(x => q.DeleteMessage(x));
        return objects;
    }

    public static void Clear(string queueName, CloudQueue q = null)
    {
        if (q == null)
            q = GetCloudQueue(queueName);
        ClearCloudQueueDebuggerHidden(q);
    }

    [DebuggerHidden]
    public static void ClearCloudQueueDebuggerHidden(CloudQueue q)
    {
        try { q.Clear(); }
        catch { }
    }

}

public class AzureQueueWithErrorRecovery
{
    internal List<CloudQueueMessage> Messages;
    internal int MaxAttempts = 5;
    internal Action<object> FailureAction;

    public AzureQueueWithErrorRecovery(int maxAttemptsBeforeDeleting, Action<object> failureAction)
    {
        MaxAttempts = maxAttemptsBeforeDeleting;
        FailureAction = failureAction;
    }

    public List<Object> GetMessages(string queueName, int numMessages)
    {
        CloudQueue q = AzureQueue.GetCloudQueue(queueName);
        Messages = new List<CloudQueueMessage>();
        int numMessagesToProcess = numMessages;
        while (numMessagesToProcess > 0)
        {
            int numMessagesToProcessThisTime = (numMessagesToProcess > 32) ? 32 : numMessagesToProcess; // maximum retrievable at once
            Messages.AddRange(q.GetMessages(numMessagesToProcessThisTime).ToList());
            numMessagesToProcess = numMessagesToProcess - numMessagesToProcessThisTime;
        }
        List<Object> objects = new List<object>();
        foreach (var message in Messages)
        {
            var theObject = ByteArrayConversions.ByteArrayToObject(message.AsBytes);
            if (message.DequeueCount > MaxAttempts)
            {
                q.DeleteMessage(message);
                FailureAction(theObject);
            }
            else
                objects.Add(theObject);
        }
        return objects;
    }

    public void ConfirmProperExecution(string queueName)
    {
        CloudQueue q = AzureQueue.GetCloudQueue(queueName);
        if (Messages != null)
        {
            Messages.ForEach(x => q.DeleteMessage(x));
        }
    }
}
