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
    public static class AzureTableGrouping
    {

        // This allows grouping of data of one type with two goals: (1) We want to use several partitions to create good throughput overall. There is some cost to this, though, as we'll need to do multiple downloads.
        // (2) We want to batch our data so that we can minimize the total number of storage transactions.
        // Azure allows up to 100 items to be batched, but we can do better than this by making each of the items in an Azure batch a group of items that we are storing.
        // This approach works best when one is being stored is lists of data (which themselves individually could be lists).

        public static List<Task<IList<TableResult>>> AddEntitiesGroupedAndBatched(CloudTable table, IEnumerable<object> theItems, int maxUploads, string partitionKeyBase, string rowKeySuffix)
        {
            const int maxGroupsPerUpload = 100; // Azure limit
            List<object> itemsList = theItems.ToList();
            int numSeparateItems = theItems.Count();
            List<AzureGenericTableEntity> tableEntityList = new List<AzureGenericTableEntity>();
            IEnumerable<int> iAll = Enumerable.Range(0, numSeparateItems);
            List<IEnumerable<int>> partitions = iAll.Batch<int>(numSeparateItems / maxUploads + 1).ToList();
            for (int p = 0; p < partitions.Count(); p++)
            {
                List<int> partition = partitions[p].ToList();
                List<IEnumerable<int>> itemGroups = partition.Batch<int>(partition.Count() / maxGroupsPerUpload + 1).ToList();
                for (int ig = 0; ig < itemGroups.Count; ig++)
                {
                    List<int> theIndices = itemGroups[ig].ToList();
                    AzureGenericTableEntity agte = new AzureGenericTableEntity(
                        partitionKeyBase + p.ToString("D5"),
                        "IG" + p.ToString("D5") + "_" + ig.ToString("D5") + "_" + rowKeySuffix,
                        theIndices.Select(x => itemsList[x]).ToList()
                        );
                    tableEntityList.Add(agte);
                    //Debug.WriteLine("A: " + "IG" + p.ToString("D5") + "_" + ig.ToString("D5") + "_" + rowKeySuffix + theIndices.Min() + "," + theIndices.Max());
                }
            }
            return AzureTableV2.AddEntitiesBatched(tableEntityList, table);
        }

        public async static Task<List<T>> DownloadItemsAndMerge<T>(List<EntityGroupInfo> entityGroupInfos, int firstItemIndex, int lastItemIndex, CloudTable table, Func<List<T>, T> mergeFn)
        {
            List<EntityGroupInfo> matches = entityGroupInfos.Where(
                x =>
                    (x.FirstItemIndex <= firstItemIndex && x.LastItemIndex >= firstItemIndex)
                    ||
                    (x.FirstItemIndex <= lastItemIndex && x.LastItemIndex >= lastItemIndex)
                    ||
                    (x.FirstItemIndex >= firstItemIndex && x.LastItemIndex <= lastItemIndex)
                    )
                    .ToList();
            if (!matches.Any())
                return null;
            List<Task<List<T>>> tasks = new List<Task<List<T>>>();
            foreach (var match in matches)
            {
                EntityGroupInfo theMatch = match;
                Task<List<T>> theTask = theMatch.DownloadItemsAndMerge<T>(table, mergeFn);
                tasks.Add(theTask);
            }
            await Task.WhenAll(tasks);
            T[] tArray = new T[lastItemIndex - firstItemIndex + 1];
            List<T> listToReturn = new List<T>();
            for (int i = firstItemIndex; i <= lastItemIndex; i++)
                listToReturn.Add(default(T));
            for (int m = 0; m < matches.Count(); m++)
            {
                var match = matches[m];
                List<T> r = tasks[m].Result;
                for (int i = match.FirstItemIndex; i <= match.LastItemIndex; i++)
                    if (i >= firstItemIndex && i <= lastItemIndex)
                    {
                        if (object.Equals(listToReturn[i - firstItemIndex], default(T)))
                            listToReturn[i - firstItemIndex] = r[i - match.FirstItemIndex];
                        else
                            listToReturn[i - firstItemIndex] = mergeFn(new List<T>() { listToReturn[i - firstItemIndex], r[i - match.FirstItemIndex] });
                    }
            }
            return listToReturn;
        }

        public class EntityGroupInfo
        {
            public int FirstItemIndex;
            public int LastItemIndex;
            public string PartitionKey;
            public string RowKeyWithoutSuffix;

            public Tuple<int,int> GetItemRange()
            {
                return new Tuple<int, int>(FirstItemIndex, LastItemIndex);
            }

            // The mergeFn MUST combine Lists of something into a bigger version of that same thing.
            // For example, if the something is itself a list of ints, we take a list of lists of ints and get a list of ints.
            public async Task<List<T>> DownloadItemsAndMerge<T>(CloudTable table, Func<List<T>, T> mergeFn)
            {
                List<List<T>> dataByRowKeyAndItemIndex = await DownloadItems<T>(table);

                var overallList = dataByRowKeyAndItemIndex.SelectMany((item, index) =>
                    item.Select((item2, index2) => new { Item = item2, Index = FirstItemIndex + index2 })).OrderBy(x => x.Index).ToList();

                List<T> tList = new List<T>();
                for (int i = FirstItemIndex; i <= LastItemIndex; i++)
                {
                    List<T> itemsForIndex = overallList.Where(x => x.Index == i).Select(x => x.Item).ToList();
                    tList.Add(mergeFn(itemsForIndex));
                }
                return tList;
            }

            public async Task<List<List<T>>> DownloadItems<T>(CloudTable table)
            {
                string lowerBoundRowKey = RowKeyWithoutSuffix;
                string upperBoundRowKey = RowKeyWithoutSuffix + "_" + "ZZZZZZZZZZZZZZZZZZ";

                TableQuery<AzureGenericTableEntity> rangeQuery = new TableQuery<AzureGenericTableEntity>().Where(
                    TableQuery.CombineFilters(
                        TableQuery.GenerateFilterCondition("PartitionKey", QueryComparisons.Equal, PartitionKey),
                        TableOperators.And,
                        TableQuery.CombineFilters(
                            TableQuery.GenerateFilterCondition("RowKey", QueryComparisons.GreaterThanOrEqual, lowerBoundRowKey),
                            TableOperators.And,
                            TableQuery.GenerateFilterCondition("RowKey", QueryComparisons.LessThanOrEqual, upperBoundRowKey)
                            )
                        )
                    );
                List<AzureGenericTableEntity> entities = await table.ExecuteQueryAsync<AzureGenericTableEntity>(rangeQuery);
                // note that every AzureGenericTableEntity is a List<object>, and that object may itself be a List. So we could have three levels of lists here.
                // The outer layer enumerates different row keys within the partition. 
                // The next layer enumerates each of the item indices.
                // The innermost layer is application-specific (for ACESim, it's a list of ints)
                List<List<T>> dataByRowKeyAndItemIndex = entities
                    .Select(x =>
                        (List<object>)BinarySerialization.GetObjectFromByteArray(x.Data))
                    .Select(x => x.Select(y => (T)y).ToList()) // inner List<T>
                    .ToList();
                return dataByRowKeyAndItemIndex;
            }
        }

        public static List<Tuple<int,int>> GetItemRangeList(int numSeparateItems, int numPartitionsToSplitOver, List<EntityGroupInfo> egis = null)
        {
            if (egis == null)
                egis = GetEntityGroupingInfos(numSeparateItems, numPartitionsToSplitOver, "");
            return egis.Select(x => x.GetItemRange()).Distinct().ToList();
        }

        public static List<EntityGroupInfo> GetEntityGroupingInfos(int numSeparateItems, int numPartitionsToSplitOver, string partitionKeyBase)
        {
            const int maxGroupsPerUpload = 100; // Azure limit
            List<EntityGroupInfo> egiList = new List<EntityGroupInfo>();
            IEnumerable<int> iAll = Enumerable.Range(0, numSeparateItems);
            List<IEnumerable<int>> partitions = iAll.Batch<int>(numSeparateItems / numPartitionsToSplitOver + 1).ToList();
            for (int p = 0; p < partitions.Count(); p++)
            {
                List<int> partition = partitions[p].ToList();
                List<IEnumerable<int>> itemGroups = partition.Batch<int>(partition.Count() / maxGroupsPerUpload + 1).ToList();
                for (int ig = 0; ig < itemGroups.Count; ig++)
                {
                    List<int> theIndices = itemGroups[ig].ToList();
                    EntityGroupInfo egi = new EntityGroupInfo()
                    {
                        FirstItemIndex = theIndices.Min(),
                        LastItemIndex = theIndices.Max(),
                        PartitionKey = partitionKeyBase + p.ToString("D5"),
                        RowKeyWithoutSuffix = "IG" + p.ToString("D5") + "_" + ig.ToString("D5")
                    };
                    egiList.Add(egi);

                    //Debug.WriteLine("B: " + "IG" + p.ToString("D5") + "_" + ig.ToString("D5") + " " + theIndices.Min() + " " + theIndices.Max());
                }
            }
            return egiList;
        }

        private static async void Example_SimulateProcessReturningData(int processID, string tableName, int numSeparateItemsThatEachProcessWouldReturn, int numIntsToUseForEachItem, int numPartitionsToSpreadOver)
        {
            CloudTable table = AzureTableV2.GetCloudTable(tableName);
            //AzureGenericTableEntity test = new AzureGenericTableEntity("P", "R", 34);
            //AzureTableV2.AddSingleEntity(test, table);
            // Start by creating our data
            List<List<int>> theItems = new List<List<int>>(); // each item for our application is a list
            for (int i = 0; i < numSeparateItemsThatEachProcessWouldReturn; i++)
                theItems.Add(Enumerable.Range(processID * 100000 + i * 100, numIntsToUseForEachItem).ToList());
            List<Task<IList<TableResult>>> tasks = AzureTableGrouping.AddEntitiesGroupedAndBatched(table, theItems, numPartitionsToSpreadOver, "SPI", processID.ToString());
            await Task.WhenAll(tasks.ToArray()); // could Task.WaitAll without async, but that would stop the calling thread. This will allow that to continue.
        }

        public async static void Example_SimpleUploadDownload()
        {
            AzureSetup.useDevelopmentAccount = false;

            string tableName = "Example" + DateTime.Now.ToString("yyyyMMddTHHmmss"); // no non alpha numeric characters

            // 'books' is the name of the table

            int numSeparateItemsThatEachProcessWouldReturn = 1000; // eg., in acesim smoothing points
            int numIntsToUseForEachItem = 15; // eg., in acesim iterations
            int numPartitionsToSpreadOver = 10; // to increase throughput
            int numProcessesSimulated = 3;
            bool doUpload = true;
            if (doUpload)
            {
                Stopwatch s = new Stopwatch();
                s.Start();
                List<Task> tasks2 = new List<Task>();
                for (int i = 0; i < numProcessesSimulated; i++)
                {
                    int j = i; // must copy variable; otherwise we will get max value for each task.
                    tasks2.Add(Task.Factory.StartNew(() => Example_SimulateProcessReturningData(j, tableName, numSeparateItemsThatEachProcessWouldReturn, numIntsToUseForEachItem, numPartitionsToSpreadOver)));
                }
                await Task.WhenAll(tasks2.ToArray()); // do NOT use Task.WaitAll(), which can cause deadlock.
                s.Stop();
            }
            List<AzureTableGrouping.EntityGroupInfo> egis = AzureTableGrouping.GetEntityGroupingInfos(numSeparateItemsThatEachProcessWouldReturn, numPartitionsToSpreadOver, "SPI");
            AzureTableGrouping.EntityGroupInfo egi = egis.First();
            CloudTable table = AzureTableV2.GetCloudTable(tableName);

            List<List<int>> allLists = await DownloadItemsAndMerge<List<int>>(egis, 0, numSeparateItemsThatEachProcessWouldReturn - 1, table, MergeListOfLists<int>);
            if ((allLists.Count() != numSeparateItemsThatEachProcessWouldReturn)
                || !(allLists.All(x => x.Count() == numIntsToUseForEachItem * numProcessesSimulated)))
                throw new Exception("Internal error.");
        }

        public static List<T> MergeListOfLists<T>(List<List<T>> listOfLists)
        {
            return listOfLists.SelectMany(x => x).ToList();
        }
    }
}
