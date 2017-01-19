using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Collections.Concurrent;
using PriorityQueue;
using System.Threading;
using System.Data;
using System.Data.Entity;
using System.Data.SqlServerCe;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;

namespace ACESim
{
    public class PriorityAndValue
    {
        public IterationID Value;
        public double Priority;
    }

    public class PriorityQueueSmallRAMFootprintMonitor
    {
        public PQContext PQContext;
        public Thread MonitorThread;
        public bool CompleteWhenAllProcessed;
        public bool Complete = true;
        public int MaximumBacklog;
        private PriorityQueueSmallRAMFootprint[] ToCheck = null;
        private bool Launched;

        public bool UseOfDatabaseActivated = false;
        public object LaunchLock = new object();
        public void LaunchMonitorThread(int numberPriorityQueuesToMonitor)
        {
            if (!Launched)
            {
                lock (LaunchLock)
                {
                    if (!Launched)
                    {
                        CompleteWhenAllProcessed = false;
                        Complete = false;
                        Database.SetInitializer(new DropCreateDatabaseIfModelChanges<PQContext>());
                        ResetEntireDatabase();
                        ToCheck = new PriorityQueueSmallRAMFootprint[numberPriorityQueuesToMonitor];
                        MonitorThread = new Thread(Monitor);
                        MonitorThread.Name = "PriorityQueueMonitorThread";
                        MonitorThread.Start();
                    }
                }
            }
        }

        public void RegisterInstance(int priorityQueueNumber, PriorityQueueSmallRAMFootprint pQ)
        {
            ToCheck[priorityQueueNumber] = pQ;
        }


        public void ResetEntireDatabase()
        {
            if (!UseOfDatabaseActivated)
                return;
            bool keepGoing = true;
            while (keepGoing)
            {
                PQContext = new PQContext();
                IQueryable<PQInfo> thePQInfos = PQContext.PQInfos.Where(x => true);
                var somePQInfos = thePQInfos.Take(20).ToList();
                if (somePQInfos.Any())
                {
                    foreach (var thePQInfo in somePQInfos)
                        PQContext.PQInfos.Remove(thePQInfo);
                    PQContext.SaveChanges();
                }
                else
                    keepGoing = false;
            }
            PQContext = new PQContext();
        }

        public void Monitor()
        {
            int numComplete = 0;
            if (PQContext == null && UseOfDatabaseActivated)
                PQContext = new PQContext();
            while (true)
            {
                bool completeOnNextRound = CompleteWhenAllProcessed;
                int numCompleteStartOfRound = numComplete;
                foreach (var instance in ToCheck)
                {
                    if (instance != null && (completeOnNextRound || instance.BacklogCount >= MaximumBacklog))
                    {
                        numComplete++;
                        instance.BulkEnqueue(numComplete % 20 == 0);
                    }
                }
                if (UseOfDatabaseActivated)
                    PQContext.SaveChanges();
                if (completeOnNextRound)
                {
                    if (UseOfDatabaseActivated)
                        PQContext = new PQContext();
                    Complete = true;
                    return;
                }
                else
                {
                    if (UseOfDatabaseActivated)
                        PQContext = new PQContext();
                    if (numComplete == numCompleteStartOfRound)
                        Thread.Sleep(10);
                }
            }
        }
    }

    public class PriorityQueueSmallRAMFootprint
    {
        public int DecisionNumber;
        public int SmoothingPointNumber;
        public bool ValidationMode;
        public int MaximumToKeepInPriorityQueue;
        public double? LowestPriorityOrNullIfNotYetFull;
        public bool KeepAllInRAM;
        public int Count;
        public PriorityQueue<double, IterationID> InMemoryPriorityQueue;
        public int BacklogCount = 0;
        public PriorityQueueSmallRAMFootprintMonitor PriorityQueueSmallRAMFootprintMonitor;
        public PQContext theContext { get { return PriorityQueueSmallRAMFootprintMonitor.PQContext; } set { PriorityQueueSmallRAMFootprintMonitor.PQContext = value; } }
        private ConcurrentQueue<PriorityAndValue> WaitingToEnqueue;
        PriorityQueue<double, IterationID> newlyCreatedQueue;
        
        public PriorityQueueSmallRAMFootprint()
        {
        }


        static bool initialized;
        static object initializationLock = new object();
        public PriorityQueueSmallRAMFootprint(int decisionNumber, int smoothingPointNumber, bool validationMode, int maximumToKeepInPriorityQueue, bool keepAllInRAM, PriorityQueueSmallRAMFootprintMonitor priorityQueueSmallRAMFootprintMonitor)
        {
            DecisionNumber = decisionNumber;
            SmoothingPointNumber = smoothingPointNumber;
            ValidationMode = validationMode;
            PriorityQueueSmallRAMFootprintMonitor = priorityQueueSmallRAMFootprintMonitor;
            MaximumToKeepInPriorityQueue = maximumToKeepInPriorityQueue;
            KeepAllInRAM = keepAllInRAM;
            if (!KeepAllInRAM && PriorityQueueSmallRAMFootprintMonitor != null)
                PriorityQueueSmallRAMFootprintMonitor.UseOfDatabaseActivated = true;
            ResetQueue();
            if (PriorityQueueSmallRAMFootprintMonitor != null)
                PriorityQueueSmallRAMFootprintMonitor.RegisterInstance(smoothingPointNumber, this);
        }

        public PriorityQueueSmallRAMFootprint DeepCopy()
        {
            return new PriorityQueueSmallRAMFootprint() { DecisionNumber = DecisionNumber, SmoothingPointNumber = SmoothingPointNumber, MaximumToKeepInPriorityQueue = MaximumToKeepInPriorityQueue, LowestPriorityOrNullIfNotYetFull = LowestPriorityOrNullIfNotYetFull, KeepAllInRAM = KeepAllInRAM, Count = Count, InMemoryPriorityQueue = InMemoryPriorityQueue, WaitingToEnqueue = new ConcurrentQueue<PriorityAndValue>(), PriorityQueueSmallRAMFootprintMonitor = PriorityQueueSmallRAMFootprintMonitor };
        }

        public void PrepareToEnqueue(double priority, IterationID value)
        {
            if (LowestPriorityOrNullIfNotYetFull == null || priority < LowestPriorityOrNullIfNotYetFull) // lower items are of higher priority
            {
                WaitingToEnqueue.Enqueue(new PriorityAndValue() { Priority = priority, Value = value });
                BacklogCount++; // once this exceeds maximum backlog, existing entries will be saved to disk
            }
            else
            { // forget about this iteration
            }
        }

        public List<IterationID> ToList()
        {
            if (!PriorityQueueSmallRAMFootprintMonitor.Complete)
                throw new Exception("Internal error: Can only call ToList once the PriorityQueues are complete and processing is done.");
            return LoadFromDisk().Select(x => x.Value).ToList();
        }

        public double LowestPriorityWhetherOrNotFull()
        {
            if (!PriorityQueueSmallRAMFootprintMonitor.Complete)
                throw new Exception("Internal error: Can only call ToList once the PriorityQueues are complete and processing is done.");
            var farthestFirst = LoadFromDisk().Select(x => x.Key).OrderByDescending(x => x);
            return farthestFirst.First();
        }


        public void BulkEnqueue(bool saveChanges)
        {
            PriorityQueue<double, IterationID> theQueue = null;
            if (WaitingToEnqueue.Any())
            {
                PQInfo thePQInfo;
                theQueue = LoadFromDisk(out thePQInfo);
                while (WaitingToEnqueue.Any())
                {
                    PriorityAndValue toEnqueue;
                    WaitingToEnqueue.TryDequeue(out toEnqueue);
                    if (toEnqueue != null)
                    {
                        bool full;
                        LowestPriorityOrNullIfNotYetFull = theQueue.Enqueue(toEnqueue.Priority, toEnqueue.Value, out full);
                        if (!full)
                            LowestPriorityOrNullIfNotYetFull = null;
                        Count = theQueue.Count;
                    }
                }
                BacklogCount = 0;
                SaveToDisk(theQueue, saveChanges, thePQInfo);
            }
        }

        public void ResetQueue()
        {
            WaitingToEnqueue = new ConcurrentQueue<PriorityAndValue>();
            newlyCreatedQueue = new PriorityQueue<double, IterationID>(0, MaximumToKeepInPriorityQueue, new ReverseComparer<double>());
            Count = 0;
        }

        static object diskAccessLock = new object();

        private PriorityQueue<double, IterationID> LoadFromDisk()
        {
            PQInfo thePQInfo;
            return LoadFromDisk(out thePQInfo);
        }

        private PriorityQueue<double, IterationID> LoadFromDisk(out PQInfo thePQInfo)
        {
            lock (diskAccessLock)
            {
                if (newlyCreatedQueue != null)
                {
                    thePQInfo = null;
                    return newlyCreatedQueue;
                }

                if (KeepAllInRAM)
                {
                    thePQInfo = null;
                    return InMemoryPriorityQueue;
                }
                else
                {
                    thePQInfo = theContext.PQInfos.SingleOrDefault(x => x.DecisionNumber == DecisionNumber && x.SmoothingPointNumber == SmoothingPointNumber && x.ValidationMode == ValidationMode);
                    if (thePQInfo == null)
                        throw new Exception("Internal error: Failed to load data from disk correctly.");
                    MemoryStream ms = new MemoryStream(thePQInfo.PQData);
                    BinaryFormatter bf = new BinaryFormatter();
                    PriorityQueue<double, IterationID> queue = (PriorityQueue<double, IterationID>)bf.Deserialize(ms);
                    return queue;
                }
            }
        }

        public void SaveToDisk(PriorityQueue<double, IterationID> queue, bool saveChanges, PQInfo existingPQInfo)
        {
            lock (diskAccessLock)
            {
                if (KeepAllInRAM)
                    InMemoryPriorityQueue = queue;
                else
                {
                    MemoryStream ms = new MemoryStream();
                    BinaryFormatter bf = new BinaryFormatter();
                    bf.Serialize(ms, queue);
                    byte[] serializedData = ms.ToArray();

                    if (existingPQInfo == null)
                        existingPQInfo = theContext.PQInfos.SingleOrDefault(x => x.DecisionNumber == DecisionNumber && x.SmoothingPointNumber == SmoothingPointNumber && x.ValidationMode == ValidationMode);
                    if (existingPQInfo == null)
                    {
                        PQInfo thePQInfo = new PQInfo() { DecisionNumber = DecisionNumber, SmoothingPointNumber = SmoothingPointNumber, ValidationMode = ValidationMode, PQData = serializedData };
                        theContext.PQInfos.Add(thePQInfo);
                    }
                    else
                    {
                        existingPQInfo.PQData = serializedData;
                    }
                    if (saveChanges)
                    {
                        theContext.SaveChanges();
                        newlyCreatedQueue = null;
                        theContext = new PQContext();
                    }
                }
            }
        }

    }

    public class PQInfo
    {
        [Key]
        public int ID { get; set; }
        public int DecisionNumber { get; set; }
        public int SmoothingPointNumber { get; set; }
        public bool ValidationMode { get; set; }

        [Column(TypeName = "image")]
        public byte[] PQData { get; set; }
    }

    public class PQContext : DbContext
    {
        protected override bool ShouldValidateEntity(System.Data.Entity.Infrastructure.DbEntityEntry entityEntry)
        {
            // Required to prevent bug - http://stackoverflow.com/questions/5737733
            if (entityEntry.Entity is PQInfo)
            {
                return false;
            }
            return base.ShouldValidateEntity(entityEntry);
        }

        public DbSet<PQInfo> PQInfos { get; set; }
    }
}
