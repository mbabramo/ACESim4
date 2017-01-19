using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Auth;
using Microsoft.WindowsAzure.Storage.Table;
using System.Diagnostics;

namespace ACESim
{
    public static class CloudStorageAccountV2Loader
    {
        public static CloudStorageAccount Get()
        {
            AzureSetup.SetConfigurationSettingPublisher();
            var storageAccount = AzureSetup.GetCloudStorageAccountV2("DataConnectionString");
            return storageAccount;
        }
    }



    public class AzureGenericTableEntity : TableEntity
    {
        // Notes
        // 1. 64KB limit -- could adapt Lokad FatEntity approach to get around this (i.e., add additional Data fields)
        // 2. We can't just use type "object". It will be ignored. So we must binary serialize directly.
        // 3. Must use public property to serialize properly, not a public/private field or a private property.
        public Byte[] Data { get; set; } 

        public AzureGenericTableEntity()
        {
        }

        public AzureGenericTableEntity(string partitionKey, string rowKey, object data)
        {
            PartitionKey = partitionKey;
            RowKey = rowKey;
            Data = BinarySerialization.GetByteArray(data);
        }
    }

    public static class AzureTableV2
    {
        public static CloudTable GetCloudTable(string tableName)
        {
            CloudStorageAccount storageAccount = CloudStorageAccountV2Loader.Get();

            // Create the table client.
            CloudTableClient tableClient = storageAccount.CreateCloudTableClient();
            tableClient.RetryPolicy = new Microsoft.WindowsAzure.Storage.RetryPolicies.ExponentialRetry();

            // Create the table if it doesn't exist.
            CloudTable table = tableClient.GetTableReference(tableName);
            table.CreateIfNotExists();

            return table;
        }

        public static void AddSingleEntity(TableEntity entityToAdd, CloudTable tableToAdd)
        {
            TableOperation insertOperation = TableOperation.Insert(entityToAdd);
            tableToAdd.Execute(insertOperation);
        }


        public static async Task<List<T>> ExecuteQueryAsync<T>(this CloudTable table, TableQuery<T> query) where T : TableEntity, new()
        {
            TableContinuationToken token = null;
            TableRequestOptions reqOptions = new TableRequestOptions() { };
            OperationContext ctx = new OperationContext() { ClientRequestID = "" };
            long totalEntitiesRetrieved = 0;
            List<T> list = new List<T>();
            while (true) // until break
            {
                //System.Threading.ManualResetEvent evt = new System.Threading.ManualResetEvent(false);
                var cancellable = table.BeginExecuteQuerySegmented<T>(query, token, reqOptions, ctx, null, table);
                var task = Task.Factory.FromAsync(cancellable, 
                    (o) =>
                    {
                        TableQuerySegment<T> response = (o.AsyncState as CloudTable).EndExecuteQuerySegmented<T>(o);
                        token = response.ContinuationToken;
                        list.AddRange(response);
                        int recordsRetrieved = response.Count();
                        totalEntitiesRetrieved += recordsRetrieved;
                    }
                    );
                await task.ConfigureAwait(false);
                if (token == null)
                {
                    break;
                }
            }
            return list;
        }

        

        public static int BulkTransactionsCounter = 0;

        public static List<Task<IList<TableResult>>> AddEntitiesBatched(IEnumerable<TableEntity> entities, CloudTable table)
        {
            List<TableEntity> entitiesList = entities.ToList();
            List<string> partitionKeys = entities.Select(x => x.PartitionKey).Distinct().ToList();
            const int maxBatchSize = 100;
            List<Task<IList<TableResult>>> tasks = new List<Task<IList<TableResult>>>();
            foreach (string partitionKey in partitionKeys)
            {
                List<IEnumerable<TableEntity>> batches = entities.Where(x => x.PartitionKey == partitionKey).Batch(maxBatchSize).ToList();
                foreach (var batch in batches)
                {
                    TableBatchOperation batchOperation = new TableBatchOperation();
                    foreach (TableEntity tableEntity in batch.ToList())
                    {
                        //Debug.WriteLine(tableEntity.PartitionKey + tableEntity.RowKey);
                        batchOperation.Insert(tableEntity);
                    }
                    Task<IList<TableResult>> taskToAdd = table.ExecuteBatchAsync(batchOperation);
                    tasks.Add(taskToAdd);
                    BulkTransactionsCounter++;
                }
            }
            return tasks;
        }


    }
}
