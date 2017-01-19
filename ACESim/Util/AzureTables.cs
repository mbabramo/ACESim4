using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.WindowsAzure.StorageClient;
using Microsoft.WindowsAzure;
using Microsoft.WindowsAzure.ServiceRuntime;
using ACESim;

public static class CloudStorageAccountLoader
{
    public static CloudStorageAccount Get()
    {
        AzureSetup.SetConfigurationSettingPublisher();
        var storageAccount = AzureSetup.GetCloudStorageAccount("DataConnectionString");
        return storageAccount;
    }
}

public class TableServiceContextAccess<U> : TableServiceContext where U : TableServiceEntity
{
    private CloudStorageAccount _storageAccount;
    private string _entitySetName;

    public TableServiceContextAccess(CloudStorageAccount storageAccount, string tableNameOverride = "")
        : base(storageAccount.TableEndpoint.AbsoluteUri, storageAccount.Credentials)
    {
        _storageAccount = storageAccount;
        Type theType = typeof(U);
        if (tableNameOverride == "")
            _entitySetName = theType.Name;
        else
            _entitySetName = tableNameOverride;
        var tableStorage = new CloudTableClient(_storageAccount.TableEndpoint.AbsoluteUri, _storageAccount.Credentials);
        tableStorage.RetryPolicy = RetryPolicies.RetryExponential(20, TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(3));
        tableStorage.CreateTableIfNotExist(_entitySetName);
    }

    public void Add(U entityToAdd)
    {
        AddObject(_entitySetName, entityToAdd);
        SaveChanges();
    }

    public void Update(U entityToUpdate)
    {
        UpdateObject(entityToUpdate);
        SaveChanges();
    }

    public void Delete(U entityToDelete)
    {
        DeleteObject(entityToDelete);
        SaveChanges();
    }

    public IQueryable<U> Load()
    {
        return CreateQuery<U>(_entitySetName);
    }

}

public static class AzureTableContext<T> where T : TableServiceEntity
{
    internal static TableServiceContextAccess<T> Context(string tableNameOverride = "")
    {
        TableServiceContextAccess<T> context = new TableServiceContextAccess<T>(CloudStorageAccountLoader.Get(), tableNameOverride);
        return context;
    }

    public static void Add(T entityToAdd, string tableNameOverride = "")
    {
        TableServiceContextAccess<T> context = Context(tableNameOverride);
        context.Add(entityToAdd);
    }

    public static void Update(T entityToUpdate, TableServiceContextAccess<T> context, string tableNameOverride = "")
    {
        context.Update(entityToUpdate);
    }

    public static void Delete(T entityToDelete, TableServiceContextAccess<T> context, string tableNameOverride = "")
    {
        context.Delete(entityToDelete);
    }

    public static IQueryable<T> Load(ref TableServiceContextAccess<T> context, string tableNameOverride = "")
    {
        context = Context(tableNameOverride);
        return context.Load();
    }
}

public class DataTableServiceEntity<T> : TableServiceEntity
{
    public byte[] Data { get; set; } // convert serializable object to this type
    

    public DataTableServiceEntity() : base()
    { 
    }

    public DataTableServiceEntity(T theObject, string partitionKey, string rowKey) : base(partitionKey, rowKey)
    {
        Data = ByteArrayConversions.ObjectToByteArray(theObject);
    }

    public T GetData()
    {
        return (T) ByteArrayConversions.ByteArrayToObject(Data);
    }

    public void SetData(T theObject)
    {
        Data = ByteArrayConversions.ObjectToByteArray(theObject);
    }
}

public static class AzureTable<T>
{
    internal static void AddWithRowKey(T entityToAdd, string partitionKey, string rowKey, string tableNameOverride = "")
    {

        DataTableServiceEntity<T> theEntity = new DataTableServiceEntity<T>(entityToAdd, partitionKey, rowKey);
        AzureTableContext<DataTableServiceEntity<T>>.Add(theEntity, tableNameOverride);
    }

    public static void Add(T entityToAdd, string partitionKey, string tableNameOverride = "")
    {
        // use a separate metadatatable to find the rowKey. The partitionKey in that metadatatable
        // will be the table name for this table.
        string tableNameForThisTable = tableNameOverride;
        if (tableNameOverride == "")
        {
            DataTableServiceEntity<T> obj = new DataTableServiceEntity<T>(entityToAdd, "", "");
            tableNameForThisTable = obj.GetType().Name;
        }
        TableServiceContextAccess<DataTableServiceEntity<string>> context = null;
        DataTableServiceEntity<string> lastRowKeyInfo = AzureTable<string>.LoadFirstOrDefaultByPartitionKey(tableNameForThisTable, ref context, "RowKeyMetaData");
        string theLastRowKey;
        if (lastRowKeyInfo == null)
            theLastRowKey = "-1";
        else
        {
            theLastRowKey = lastRowKeyInfo.GetData();
            if (theLastRowKey == "")
                theLastRowKey = "-1";
        }
        int theLastRowKeyInt = Convert.ToInt32(theLastRowKey);
        // increment key
        theLastRowKeyInt++;
        theLastRowKey = theLastRowKeyInt.ToString();
        // now add to this table
        AddWithRowKey(entityToAdd, partitionKey, theLastRowKey, tableNameOverride);
        // update metadata

        if (lastRowKeyInfo == null)
            AzureTable<string>.AddWithRowKey(theLastRowKey, tableNameForThisTable, "0", "RowKeyMetaData");
        else
        {
            lastRowKeyInfo.SetData(theLastRowKey);
            AzureTable<string>.Update(lastRowKeyInfo, context, "RowKeyMetaData");
        }
    }

    public static void Update(DataTableServiceEntity<T> entityToUpdate, TableServiceContextAccess<DataTableServiceEntity<T>> context, string tableNameOverride = "")
    {
        AzureTableContext<DataTableServiceEntity<T>>.Update(entityToUpdate, context, tableNameOverride);
    }

    public static void Delete(DataTableServiceEntity<T> entityToDelete, TableServiceContextAccess<DataTableServiceEntity<T>> context, string tableNameOverride = "")
    {
        AzureTableContext<DataTableServiceEntity<T>>.Delete(entityToDelete, context, tableNameOverride);
    }

    public static IQueryable<DataTableServiceEntity<T>> Load(ref TableServiceContextAccess<DataTableServiceEntity<T>> context, string tableNameOverride = "")
    {
        return AzureTableContext<DataTableServiceEntity<T>>.Load(ref context, tableNameOverride);
    }

    public static IQueryable<DataTableServiceEntity<T>> LoadDataTableServiceEntityByPartitionKey(string partitionKey, ref TableServiceContextAccess<DataTableServiceEntity<T>> context, string tableNameOverride = "")
    {
        return Load(ref context, tableNameOverride).Where(x => x.PartitionKey == partitionKey);
    }

    public static DataTableServiceEntity<T> LoadFirstOrDefaultByPartitionKey(string partitionKey, ref TableServiceContextAccess<DataTableServiceEntity<T>> context, string tableNameOverride = "")
    {
        var data = Load(ref context, tableNameOverride).Where(x => x.PartitionKey == partitionKey);
        DataTableServiceEntity<T> e = null;
        using (IEnumerator<DataTableServiceEntity<T>> enumer = data.GetEnumerator())
        {
            if (enumer.MoveNext()) e = enumer.Current;
        }
        return e;
    }

    public static DataTableServiceEntity<T> LoadDataTableServiceEntityByRowKey(string rowKey, ref TableServiceContextAccess<DataTableServiceEntity<T>> context, string tableNameOverride = "")
    {
        return Load(ref context, tableNameOverride).SingleOrDefault(x => x.RowKey == rowKey);
    }

    public static List<T> LoadDataOnlyByPartitionKey(string partitionKey, ref TableServiceContextAccess<DataTableServiceEntity<T>>  context, string tableNameOverride = "")
    {
        var entitiesByPartitionKey = LoadDataTableServiceEntityByPartitionKey(partitionKey, ref context, tableNameOverride).ToList();
        return entitiesByPartitionKey.Select(x => x.GetData()).ToList();
    }

    public static T LoadDataOnlyByRowKey(string rowKey, ref TableServiceContextAccess<DataTableServiceEntity<T>> context, string tableNameOverride = "")
    {
        return LoadDataTableServiceEntityByRowKey(rowKey, ref context, tableNameOverride).GetData();
    }
}