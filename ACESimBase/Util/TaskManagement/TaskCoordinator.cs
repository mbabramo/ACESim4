using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using static ACESimBase.Util.ArrayManipulation.ByteArrayCompression;

namespace ACESimBase.Util.TaskManagement
{
    [Serializable]
    public class TaskCoordinator
    {
        private const int StatusMagic = 0x41434553; // "ACES"
        private const int StatusVersion = 2;

        public List<TaskStage> Stages;

        public TaskCoordinator(List<TaskStage> stages)
        {
            Stages = stages ?? throw new ArgumentNullException(nameof(stages));
            ValidateUniqueTaskIdentities();
        }

        private IEnumerable<RepeatedTask> RepeatedTasks =>
            Stages.SelectMany(x => x.RepeatedTasks);

        private IEnumerable<IndividualTask> IndividualTasks =>
            RepeatedTasks.SelectMany(x => x.IndividualTasks);

        public IReadOnlyList<IndividualTask> Tasks => IndividualTasks.ToList();

        public int NumIndividualTasks => IndividualTasks.Count();
        public int NumTasksComplete => IndividualTasks.Count(x => x.Complete);
        public int NumTasksStarted => IndividualTasks.Count(x => x.Started != null);
        public int NumTasksPending => IndividualTasks.Count(x => x.Started != null && !x.Complete && !x.Failed);
        public int NumTasksFailed => IndividualTasks.Count(x => x.Failed);
        public int NumTasksUnstarted => IndividualTasks.Count(x => x.Started == null && !x.Complete && !x.Failed);
        public bool HasFailures => NumTasksFailed > 0;
        public bool AllComplete => IndividualTasks.All(x => x.Complete);
        public bool AllTerminal => IndividualTasks.All(x => x.Complete || x.Failed);
        public double ProportionComplete => NumIndividualTasks == 0
            ? 1.0
            : NumTasksComplete / (double)NumIndividualTasks;
        public string PlanFingerprint => Convert.ToHexString(CalculatePlanFingerprint());

        public void StatusFromByteArray(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0)
                throw new InvalidDataException("Task coordinator status is empty.");

            using var memoryStream = new MemoryStream(Decompress(bytes));
            using var reader = new BinaryReader(memoryStream, Encoding.UTF8, leaveOpen: false);

            int magic = reader.ReadInt32();
            int version = reader.ReadInt32();
            int taskCount = reader.ReadInt32();
            byte[] storedFingerprint = reader.ReadBytes(32);

            if (magic != StatusMagic)
                throw new InvalidDataException("Task coordinator status has an unknown format.");
            if (version != StatusVersion)
                throw new InvalidDataException(
                    $"Task coordinator version {version} does not match expected version {StatusVersion}.");
            if (taskCount != NumIndividualTasks)
                throw new InvalidDataException(
                    $"Task coordinator contains {taskCount} tasks but this launcher defines {NumIndividualTasks}.");
            if (!storedFingerprint.SequenceEqual(CalculatePlanFingerprint()))
                throw new InvalidDataException(
                    "Task coordinator plan fingerprint does not match the current production matrix. " +
                    "Do not combine outputs from different launcher configurations.");

            foreach (IndividualTask task in IndividualTasks)
            {
                byte state = reader.ReadByte();
                task.Started = null;
                task.Complete = false;
                task.Failed = false;

                switch (state)
                {
                    case 0: // unstarted
                        break;
                    case 1: // pending
                        task.Started = DateTime.FromBinary(reader.ReadInt64());
                        break;
                    case 2: // complete
                        task.Started = DateTime.UnixEpoch;
                        task.Complete = true;
                        break;
                    case 3: // failed
                        task.Started = DateTime.UnixEpoch;
                        task.Failed = true;
                        break;
                    default:
                        throw new InvalidDataException($"Unknown task state {state} for {task.Identity}.");
                }
            }

            if (memoryStream.Position != memoryStream.Length)
                throw new InvalidDataException("Task coordinator status contains unexpected trailing data.");
        }

        public byte[] StatusAsByteArray()
        {
            using var memoryStream = new MemoryStream();
            using (var writer = new BinaryWriter(memoryStream, Encoding.UTF8, leaveOpen: true))
            {
                writer.Write(StatusMagic);
                writer.Write(StatusVersion);
                writer.Write(NumIndividualTasks);
                writer.Write(CalculatePlanFingerprint());

                foreach (IndividualTask task in IndividualTasks)
                {
                    byte state = task.Complete ? (byte)2
                        : task.Failed ? (byte)3
                        : task.Started != null ? (byte)1
                        : (byte)0;
                    writer.Write(state);
                    if (state == 1)
                        writer.Write(task.Started.Value.ToBinary());
                }
            }

            return Compress(memoryStream.ToArray());
        }

        public void Update(
            IReadOnlyCollection<IndividualTask> tasksCompleted,
            IReadOnlyCollection<IndividualTask> tasksFailed,
            bool readyForAnotherTask,
            int numTasksToRequest,
            out List<IndividualTask> tasksToDo,
            out bool allComplete)
        {
            if (numTasksToRequest <= 0)
                throw new ArgumentOutOfRangeException(nameof(numTasksToRequest));

            ApplyTaskResults(tasksCompleted, completed: true);
            ApplyTaskResults(tasksFailed, completed: false);

            allComplete = AllComplete;
            tasksToDo = null;
            // A terminal failure stops the stage immediately. Recovery must explicitly reset
            // failed tasks before any worker can receive more work, so a bad result set cannot
            // continue growing after its first detected failure.
            if (allComplete || HasFailures || !readyForAnotherTask)
                return;

            TaskStage firstIncompleteStage = Stages.FirstOrDefault(x => !x.Complete);
            if (firstIncompleteStage == null)
            {
                allComplete = true;
                return;
            }

            // Never reassign a pending task automatically. This makes output ownership unique.
            // A crashed worker's pending task is reset only through an explicit recovery action.
            tasksToDo = firstIncompleteStage.RepeatedTasks
                .SelectMany(x => x.IndividualTasks)
                .Where(x => x.Started == null && !x.Complete && !x.Failed)
                .OrderBy(x => x.ID)
                .ThenBy(x => x.Repetition)
                .ThenBy(x => x.RestrictToScenarioIndex)
                .Take(numTasksToRequest)
                .ToList();

            foreach (IndividualTask task in tasksToDo)
                task.Started = DateTime.UtcNow;

            if (tasksToDo.Count == 0)
                tasksToDo = null;
        }

        public int ResetFailedTasks()
        {
            var failed = IndividualTasks.Where(x => x.Failed).ToList();
            foreach (IndividualTask task in failed)
                ResetTask(task);
            return failed.Count;
        }

        public int ResetPendingTasks()
        {
            var pending = IndividualTasks
                .Where(x => x.Started != null && !x.Complete && !x.Failed)
                .ToList();
            foreach (IndividualTask task in pending)
                ResetTask(task);
            return pending.Count;
        }

        public override string ToString() =>
            $"Tasks: {NumIndividualTasks}; complete: {NumTasksComplete}; pending: {NumTasksPending}; " +
            $"failed: {NumTasksFailed}; unstarted: {NumTasksUnstarted}; plan: {PlanFingerprint}";

        private void ApplyTaskResults(
            IReadOnlyCollection<IndividualTask> reportedTasks,
            bool completed)
        {
            if (reportedTasks == null)
                return;

            var duplicateReports = reportedTasks
                .GroupBy(x => x.Identity, StringComparer.Ordinal)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToList();
            if (duplicateReports.Count > 0)
                throw new InvalidOperationException(
                    "Duplicate task result reports: " + string.Join(", ", duplicateReports));

            foreach (IndividualTask reported in reportedTasks)
            {
                IndividualTask actual = FindTask(reported);
                if (actual.Started == null)
                    throw new InvalidOperationException($"Task was reported before it was claimed: {actual.Identity}.");
                if (actual.Complete || actual.Failed)
                    throw new InvalidOperationException($"Duplicate terminal result for task: {actual.Identity}.");

                actual.Complete = completed;
                actual.Failed = !completed;
            }
        }

        private IndividualTask FindTask(IndividualTask task)
        {
            var matches = IndividualTasks.Where(x =>
                x.TaskType == task.TaskType &&
                x.ID == task.ID &&
                x.Repetition == task.Repetition &&
                x.RestrictToScenarioIndex == task.RestrictToScenarioIndex).ToList();
            if (matches.Count != 1)
                throw new InvalidOperationException(
                    $"Task result identity '{task.Identity}' matched {matches.Count} coordinator tasks.");
            return matches[0];
        }

        private static void ResetTask(IndividualTask task)
        {
            task.Started = null;
            task.Complete = false;
            task.Failed = false;
        }

        private byte[] CalculatePlanFingerprint()
        {
            var builder = new StringBuilder();
            for (int stageIndex = 0; stageIndex < Stages.Count; stageIndex++)
            {
                foreach (RepeatedTask repeated in Stages[stageIndex].RepeatedTasks.OrderBy(x => x.TaskType).ThenBy(x => x.ID))
                {
                    foreach (IndividualTask task in repeated.IndividualTasks
                        .OrderBy(x => x.Repetition)
                        .ThenBy(x => x.RestrictToScenarioIndex))
                    {
                        builder.Append(stageIndex.ToString(CultureInfo.InvariantCulture)).Append('|')
                            .Append(task.TaskType).Append('|')
                            .Append(task.ID.ToString(CultureInfo.InvariantCulture)).Append('|')
                            .Append(task.Repetition.ToString(CultureInfo.InvariantCulture)).Append('|')
                            .Append(task.RestrictToScenarioIndex?.ToString(CultureInfo.InvariantCulture) ?? "-").Append('|')
                            .Append(task.PlanLabel).Append('\n');
                    }
                }
            }

            return SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()));
        }

        private void ValidateUniqueTaskIdentities()
        {
            var duplicates = IndividualTasks
                .GroupBy(x => x.Identity, StringComparer.Ordinal)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToList();
            if (duplicates.Count > 0)
                throw new InvalidOperationException(
                    "Task plan contains duplicate identities: " + string.Join(", ", duplicates));
        }
    }
}
