using ACESimBase.Util.Randomization;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESimBase.Util.Debugging
{
    public static class HeisenbugTester
    {
        static int theNumToAddThenSubtract = 23;
        static bool addCloseCheckIns = false;

        // This illustrates the general approach to finding a Heisenbug with this method. Add CheckIns in your code. If it 
        // still doesn't find one, add more. If it finds it too rarely, figure out after which check in the Heisenbug generally
        // occurs and add more check-ins. (You can do this here by setting addCloseCheckIns to true.) Once you haev a high number
        // of failures, you've narrowed down the code and can look for a parallelism issue.
        // You can also change the probability of overlap (if you can find the bug without overlap, then it should occur consistently).

        // One thing to check, which this might not be great at, is whether there are thread-local variables carrying over from one task to the next.

        public static int GetNum(int numToGet)
        {
            // the following is the problematic code -- suppose Thread A increments, Thread B increments, Thread A adds and subtracts, Thread B adds, Thread A decrements, Thread B subtracts and decrements. Then,
            // Thread B will have added the number that has been twice incremented, while subtracting the number only once incremented. So it will not be a neutral operation.
            theNumToAddThenSubtract++;
            HeisenbugTracker.CheckIn();
            numToGet += theNumToAddThenSubtract;
            if (addCloseCheckIns)
                HeisenbugTracker.CheckIn();
            numToGet -= theNumToAddThenSubtract;
            if (addCloseCheckIns)
                HeisenbugTracker.CheckIn();
            theNumToAddThenSubtract--;
            if (addCloseCheckIns)
                HeisenbugTracker.CheckIn();

            return numToGet;
        }

        public static int SumNumbersOneThroughSeven(int callFunctionForThisNumber)
        {
            int total = 0;
            for (int n = 1; n <= 7; n++)
            {
                HeisenbugTracker.CheckIn();
                if (callFunctionForThisNumber == n)
                    total += GetNum(callFunctionForThisNumber);
                else
                    total += n;
            }
            return total;
        }

        public static void TryToFindFakeBug()
        {
            //HeisenbugTracker.KeepTryingRandomSchedulesUntilProblemIsFound_ThenRunScheduleRepeatedly(x => new object[] { (object)GetNum((int)x), (object)GetNum((int)x) }, new object[] { 2, 5 }, x => x[0] != x[1]);
            HeisenbugTracker.KeepTryingRandomSchedulesUntilProblemIsFound_ThenRunScheduleRepeatedly(x => SumNumbersOneThroughSeven((int)x), 2, 5, (x, y) => (int)x != (int)y);
        }
    }

    public static class HeisenbugTracker
    {
        static double probabilityOfOverlapWithSimpleApproach = 0;
        static bool useSimpleApproach = true;

        internal enum CheckInMode
        {
            Normal,
            Counter,
            Sync
        }

        static CheckInMode Mode = CheckInMode.Normal;

        static ScheduleTracker

            CurrentTracker = null;

        static List<object>[] InfoStoredOnEachCheckIn;

        public static bool EnableCheckInInfoStorage;

        public static int CheckIn(object infoToStoreOnCheckIn = null, bool storeOnlyIfEnabled = false)
        {
            switch (Mode)
            {
                case CheckInMode.Normal:
                    break;

                case CheckInMode.Sync:
                case CheckInMode.Counter:
                    Guid guid = Trace.CorrelationManager.ActivityId;
                    int taskNum = GuidDict[guid];
                    if (infoToStoreOnCheckIn != null && Mode == CheckInMode.Sync)
                        InfoStoredOnEachCheckIn[taskNum].Add(infoToStoreOnCheckIn);
                    if (Mode == CheckInMode.Sync)
                        return CurrentTracker.RecordCompletionAndWaitIfNecessary(taskNum);
                    else // just counting
                        return CurrentTracker.RecordCompletion(taskNum);
            }

            return 0;
        }

        static Task<object> ConvertFunctionToTaskWithActivityID(Func<object, object> theFunction, object parameterToFunction, Guid activityID)
        {
            return new Task<object>(() =>
                {
                    Trace.CorrelationManager.ActivityId = activityID;
                    return theFunction(parameterToFunction);
                }
            );
        }

        static Dictionary<Guid, int> GuidDict;
        static Task<object>[] TasksToRun;
        static int[] TotalExpectedCheckIns;

        private static void RunTasksAccordingToSchedule(Schedule theSchedule)
        {
            Mode = CheckInMode.Sync;
            CurrentTracker = new ScheduleTracker(theSchedule);

            foreach (var task in TasksToRun)
                task.Start();
            Task.WaitAll(TasksToRun);

            CurrentTracker = null;
            Mode = CheckInMode.Normal;
        }

        private static void SetUpTasks(Func<object, object>[] functions, object[] parametersToFunctions)
        {
            for (int i = 1; i <= 2; i++)
            {
                Task<object>[] theTasks = new Task<object>[functions.Count()];
                GuidDict = new Dictionary<Guid, int>();
                for (int t = 0; t < functions.Count(); t++)
                {
                    Guid newGuid = Guid.NewGuid();
                    GuidDict.Add(newGuid, t);
                    theTasks[t] = ConvertFunctionToTaskWithActivityID(functions[t], parametersToFunctions[t], newGuid);
                }
                TasksToRun = theTasks;
                if (i == 1)
                    CountStagesForTasks();
            }
            InfoStoredOnEachCheckIn = new List<object>[TasksToRun.Count()];
            for (int t = 0; t < TasksToRun.Count(); t++)
                InfoStoredOnEachCheckIn[t] = new List<object>();
        }

        private static void CountStagesForTasks()
        {
            Mode = CheckInMode.Counter;
            CurrentTracker = new ScheduleTracker(TasksToRun.Count());

            foreach (var task in TasksToRun)
                task.Start();
            Task.WaitAll(TasksToRun);
            TotalExpectedCheckIns = CurrentTracker.StagesComplete;

            CurrentTracker = null;
            Mode = CheckInMode.Normal;
        }


        private static void PrintOutInfoStoredOnCheckIns()
        {
            if (InfoStoredOnEachCheckIn.Any(x => x.Any()))
            {
                for (int t = 0; t < InfoStoredOnEachCheckIn.Count(); t++)
                {
                    foreach (var item in InfoStoredOnEachCheckIn[t])
                        Debug.WriteLine(item);
                }
            }
        }

        private static Schedule KeepTryingRandomSchedulesUntilProblemIsFound(Func<object, object>[] functions, object[] parametersToFunctions, Func<object[], bool> returnValuesIndicateProblem)
        {
            Schedule theSchedule = null;
            bool problemFound = false;
            int numTimesFailingToFindProblem = 0;
            do
            {
                SetUpTasks(functions, parametersToFunctions);
                theSchedule = new Schedule(TasksToRun.Count());
                RunTasksAccordingToSchedule(theSchedule);
                object[] returnVals = new object[TasksToRun.Count()];
                for (int t = 0; t < TasksToRun.Count(); t++)
                    returnVals[t] = TasksToRun[t].Result;
                problemFound = returnValuesIndicateProblem(returnVals);
                if (problemFound)
                    PrintOutInfoStoredOnCheckIns();
                if (!problemFound)
                    numTimesFailingToFindProblem++;
            } while (!problemFound);
            Debug.WriteLine("Found problem after " + numTimesFailingToFindProblem + " failed attempts to find it.");
            return theSchedule;
        }

        public static void KeepTryingRandomSchedulesUntilProblemIsFound_ThenRunScheduleRepeatedly(Func<object, object>[] functions, object[] parametersToFunctions, Func<object[], bool> returnValuesIndicateProblem)
        {
            Schedule theSchedule = KeepTryingRandomSchedulesUntilProblemIsFound(functions, parametersToFunctions, returnValuesIndicateProblem);
            int failedAttempts = 0;
            while (true)
            {
                SetUpTasks(functions, parametersToFunctions);
                RunTasksAccordingToSchedule(theSchedule);
                object[] returnVals = new object[TasksToRun.Count()];
                for (int t = 0; t < TasksToRun.Count(); t++)
                    returnVals[t] = TasksToRun[t].Result;
                bool problemFound = returnValuesIndicateProblem(returnVals);
                PrintOutInfoStoredOnCheckIns();
                if (problemFound)
                {
                    Debug.WriteLine("Found problem again after " + failedAttempts + " failed attempts.");
                    failedAttempts = 0;
                }
                else
                    failedAttempts++;
                Debug.WriteLine("------");
            }
        }

        public static void KeepTryingRandomSchedulesUntilProblemIsFound_ThenRunScheduleRepeatedly(Func<object, object> function, object param1, object param2, Func<object, object, bool> returnValuesIndicateProblem)
        {
            KeepTryingRandomSchedulesUntilProblemIsFound_ThenRunScheduleRepeatedly(new Func<object, object>[] { function, function }, new object[] { param1, param2 }, x => returnValuesIndicateProblem(x[0], x[1]));
        }

        internal class ScheduleEntry
        {
            public bool[] TaskExecutesThisEntry;

            public int NumTasks;

            public ScheduleEntry(int numTasks, bool[] taskExecutes = null)
            {
                NumTasks = numTasks;
                TaskExecutesThisEntry = new bool[numTasks];
                if (taskExecutes == null)
                {
                    for (int t = 0; t < NumTasks; t++)
                        TaskExecutesThisEntry[t] = RandomGenerator.NextDouble() < 0.5;
                }
                else
                    TaskExecutesThisEntry = taskExecutes;
            }

            public ScheduleEntry DeepCopy()
            {
                return new ScheduleEntry(NumTasks) { TaskExecutesThisEntry = TaskExecutesThisEntry.ToArray() };
            }

        }

        internal class Schedule
        {
            public List<ScheduleEntry> Entries = new List<ScheduleEntry>();
            public int NumTasks;
            public int[,,] CheckOutPrerequisites; // The number of stages that Task A must complete before Task B can go through Check-in C.

            double useSameAsLastProbability = 0.35;

            public Schedule(int numTasks)
            {
                NumTasks = numTasks;
                if (useSimpleApproach)
                    UseSimpleApproachToAddEntries();
                else
                    KeepAddingRandomEntriesUntilFilled();
                CalculateCheckOutPrerequisites();
            }

            private int[] CountStages(out bool exactlyRight, out bool overfilled)
            {
                int[] stages = new int[NumTasks];
                for (int t = 0; t < NumTasks; t++)
                    stages[t] = 0;
                foreach (var entry in Entries)
                    for (int t = 0; t < NumTasks; t++)
                        if (entry.TaskExecutesThisEntry[t])
                            stages[t]++;
                var stagesPlus = stages.Select((item, index) => new { Item = item, Index = index });
                exactlyRight = stagesPlus.All(x => x.Item == TotalExpectedCheckIns[x.Index]);
                overfilled = stagesPlus.Any(x => x.Item > TotalExpectedCheckIns[x.Index]);
                return stages;
            }

            private bool AddEntryIfPossible(ScheduleEntry entry)
            {
                Entries.Add(entry);
                int[] stages = CountStages(out bool exactlyRight, out bool overfilled);
                if (overfilled)
                    Entries.RemoveAt(Entries.Count - 1);
                return exactlyRight;
            }

            private ScheduleEntry GetRandomEntry()
            {
                double r1 = RandomGenerator.NextDouble();
                if (r1 < useSameAsLastProbability && Entries.Any())
                    return Entries.Last().DeepCopy();
                return new ScheduleEntry(NumTasks);
            }

            private bool TryToAddRandomEntry()
            {
                return AddEntryIfPossible(GetRandomEntry());
            }

            private void KeepAddingRandomEntriesUntilFilled()
            {
                useSameAsLastProbability = RandomGenerator.NextDouble(); // we'll vary the degree of autocorrelation
                if (useSameAsLastProbability > 0.95)
                    useSameAsLastProbability = 0.95;
                bool done = false;
                do
                {
                    done = TryToAddRandomEntry();
                } while (!done);
            }

            List<Tuple<int, int>> OrderOfTasksAndNumberToTake;
            int? IndexAtWhichLastItemShouldBeOverlap = null;

            private void UseSimpleApproachToAddEntries()
            {
                OrderOfTasksAndNumberToTake = new List<Tuple<int, int>>();
                int[] numberToTakeFirstTime = new int[NumTasks];
                int[] numberToTakeSecondTime = new int[NumTasks];
                for (int t = 0; t < NumTasks; t++)
                {
                    numberToTakeFirstTime[t] = RandomGenerator.NextIntegerExclusiveOfSecondValue(0, TotalExpectedCheckIns[t] + 1);
                    numberToTakeSecondTime[t] = TotalExpectedCheckIns[t] - numberToTakeFirstTime[t];
                    OrderOfTasksAndNumberToTake.Add(new Tuple<int, int>(t, numberToTakeFirstTime[t]));
                    OrderOfTasksAndNumberToTake.Add(new Tuple<int, int>(t, numberToTakeSecondTime[t]));
                }
                RandomSubset.Shuffle(OrderOfTasksAndNumberToTake);
                IndexAtWhichLastItemShouldBeOverlap = null;
                if (RandomGenerator.NextDouble() < probabilityOfOverlapWithSimpleApproach) // we'll overlap half of the time
                {
                    IndexAtWhichLastItemShouldBeOverlap = RandomGenerator.NextIntegerExclusiveOfSecondValue(0, OrderOfTasksAndNumberToTake.Count() - 1);
                    if (OrderOfTasksAndNumberToTake[(int)IndexAtWhichLastItemShouldBeOverlap].Item2 == 0 || OrderOfTasksAndNumberToTake[(int)IndexAtWhichLastItemShouldBeOverlap + 1].Item2 == 0
                                || OrderOfTasksAndNumberToTake[(int)IndexAtWhichLastItemShouldBeOverlap].Item1 == OrderOfTasksAndNumberToTake[(int)IndexAtWhichLastItemShouldBeOverlap + 1].Item1)
                        IndexAtWhichLastItemShouldBeOverlap = null; // can't overlap if one of these is empty, or if it's the same task
                }
                for (int index = 0; index < OrderOfTasksAndNumberToTake.Count(); index++)
                {
                    for (int entry = 0; entry < OrderOfTasksAndNumberToTake[index].Item2; entry++)
                    { // add an entry for this task
                        bool[] tasksToDoThisEntry = new bool[NumTasks];
                        for (int t = 0; t < NumTasks; t++)
                            tasksToDoThisEntry[t] = t == OrderOfTasksAndNumberToTake[index].Item1
                                || index == IndexAtWhichLastItemShouldBeOverlap && t == OrderOfTasksAndNumberToTake[index + 1].Item1 && entry == OrderOfTasksAndNumberToTake[index].Item2 - 1;
                        ScheduleEntry newEntry = new ScheduleEntry(NumTasks, tasksToDoThisEntry);
                        if (!(index == IndexAtWhichLastItemShouldBeOverlap + 1 && entry == OrderOfTasksAndNumberToTake[index].Item2 - 1)) // we need one less of the item following the overlap
                            Entries.Add(newEntry);
                    }
                }

                int[] stages = CountStages(out bool exactlyRight, out bool overfilled);
                if (!exactlyRight)
                    throw new Exception("Internal error in simple Heisenbug scheduling algorithm.");
            }

            private void CalculateCheckOutPrerequisites()
            {
                int numCheckIns = TotalExpectedCheckIns.Max();
                CheckOutPrerequisites = new int[NumTasks, NumTasks, numCheckIns];
                for (int taskA = 0; taskA < NumTasks; taskA++)
                    for (int taskB = 0; taskB < NumTasks; taskB++)
                        for (int checkInNum = 0; checkInNum < numCheckIns; checkInNum++)
                        { // The number of stages that Task A must complete before Task B can complete Stage C.
                            CheckOutPrerequisites[taskA, taskB, checkInNum] = 0;
                            if (TotalExpectedCheckIns[taskB] > checkInNum) // is this a check-in we were anticipating? keep in mind that checkInNum is zero-based, so if they are equal, this is farther than we expect to go and we have no prerequisites for continuing
                            {
                                // Go through all entries up to (not including) where taskB will reach this check-in.
                                // If taskA is performing that stage, then increment.
                                int taskBStageCount = -1;
                                int entry = -1;
                                for (entry = 0; entry < Entries.Count; entry++)
                                {
                                    if (Entries[entry].TaskExecutesThisEntry[taskB])
                                        taskBStageCount++;
                                    if (taskBStageCount == checkInNum)
                                        break;
                                }
                                for (int entry2 = 0; entry2 < entry; entry2++)
                                {
                                    if (Entries[entry2].TaskExecutesThisEntry[taskA])
                                        CheckOutPrerequisites[taskA, taskB, checkInNum]++; // another stage that Task A must perform before we get to task B. 
                                }
                            }
                        }
            }



        }

        internal class ScheduleTracker
        {
            public Schedule Schedule;

            public int[] StagesComplete;

            public ScheduleTracker(Schedule schedule)
            {
                Schedule = schedule;
                StagesComplete = new int[schedule.NumTasks];
                for (int t = 0; t < schedule.NumTasks; t++)
                    StagesComplete[t] = 0;
            }

            // Use this if no Schedule has been created, just to count the number of stages for each task.
            public ScheduleTracker(int numTasks)
            {
                StagesComplete = new int[numTasks];
                for (int t = 0; t < numTasks; t++)
                    StagesComplete[t] = 0;
            }

            public int RecordCompletion(int taskNum)
            {
                StagesComplete[taskNum]++;

                return StagesComplete[taskNum];
            }

            public bool MustWait(int taskNum)
            {
                int stageThatTaskNumWouldCompleteNext = StagesComplete[taskNum];
                if (stageThatTaskNumWouldCompleteNext >= TotalExpectedCheckIns[taskNum] - 1)
                    return false;
                bool mustWait = false;
                for (int t = 0; t < Schedule.NumTasks; t++)
                {
                    int numberStagesThatTaskTMustCompleteFirst = Schedule.CheckOutPrerequisites[t, taskNum, stageThatTaskNumWouldCompleteNext];
                    if (numberStagesThatTaskTMustCompleteFirst > StagesComplete[t])
                    {
                        mustWait = true;
                        break;
                    }
                }
                return mustWait;
            }

            public int RecordCompletionAndWaitIfNecessary(int taskNum)
            {
                StagesComplete[taskNum]++;
                while (MustWait(taskNum)) // we don't want to use Sleep because we want to go as soon as possible.
                    ;
                return StagesComplete[taskNum];
            }
        }

    }
}
