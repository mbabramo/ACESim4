using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ACESim
{
    [Serializable]
    public class NWayTreeStorage<T>
    {
        public static bool ParallelEnabled = true; // set this to false to disable running in parallel
        public NWayTreeStorageInternal<T> Parent;

        public T _StoredValue;
        public T StoredValue
        {
            get
            {
                if (ParallelEnabled)
                {
                    return GetStoredValueWithLock();
                }
                else
                {
                    return _StoredValue;
                }
            }
            set
            {
                if (ParallelEnabled)
                {
                    SetStoredValueWithLock(value);
                }
                else
                {
                    if (_StoredValue == null || _StoredValue.Equals(default(T)))
                        _StoredValue = value;
                }
            }
        }

        public override string ToString()
        {
            return ToString(0);
        }

        public virtual string ToString(int level)
        {
            return new string('\t', level) + StoredValue.ToString() + "\r\n";
        }

        private void SetStoredValueWithLock(T value)
        {
            lock (this)
            {
                if (_StoredValue == null || _StoredValue.Equals(default(T)))
                    _StoredValue = value;
            }
        }

        private T GetStoredValueWithLock()
        {
            lock (this)
                return _StoredValue;
        }

        public NWayTreeStorage(NWayTreeStorageInternal<T> parent)
        {
            Parent = parent;
        }

        public virtual List<byte> GetSequenceToHere(NWayTreeStorage<T> child = null)
        {
            List<byte> p = Parent?.GetSequenceToHere(this) ?? new List<byte>();
            return p;
        }

        public string SequenceToHereString => String.Join(",", GetSequenceToHere());
        
        public virtual NWayTreeStorage<T> GetNode(IEnumerable<byte> sequenceFromHere)
        {
            if (!sequenceFromHere.Any())
                return this;
            var branchByte = sequenceFromHere.First();
            var branch = GetBranch(branchByte);
            return branch.GetNode(sequenceFromHere.Skip(1));
        }

        public virtual NWayTreeStorage<T> GetBranch(byte index)
        {
            return null; // leaf node has no child; internal node overrides this
        }
        public virtual void SetBranch(byte index, NWayTreeStorage<T> tree)
        {
            throw new Exception("Cannot set branch on leaf node.");
        }

        public virtual bool IsLeaf()
        {
            return true; // this is a leaf node if not overriden
        }

        public List<(T storedValue, List<byte> sequenceToHere)> GetAllTreeNodes()
        {
            List<(T storedValue, List<byte> sequenceToHere)> l = new List<(T storedValue, List<byte> sequenceToHere)>();
            WalkTree(t =>
            {
                l.Add((t.StoredValue, t.GetSequenceToHere()));
            });
            return l;
        }

        public class ExecutionCounter
        {
            // This is a relatively simple way of limiting the total number of threads. If a tree is large, then much of the time can be spent switching between threads. Possibly, further gains could be achieved with something like the TPL Dataflow library, creating one consumer per logical processor. But it's complicated, because the consumers need to create new tasks in the form of their children, and we have to limit max concurrency.

            public int Count = 0;
            public int TotalProcessors = Environment.ProcessorCount * 3;
            public bool Throttle = true; // false = disabling this for now
            public bool ShouldAddTasks(int numTasks) => !Throttle || ProcessorsRemaining > numTasks;
            public int ProcessorsRemaining => TotalProcessors - Count; // DEBUG // a bit arbitrary but seems to work empirically

            public void Increment()
            {
                Interlocked.Increment(ref Count);
            }

            public void Decrement()
            {
                Interlocked.Decrement(ref Count);
            }
        }

        public virtual void WalkTree(Action<NWayTreeStorage<T>> beforeDescending, Action<NWayTreeStorage<T>> afterAscending, Func<NWayTreeStorage<T>, bool> parallel = null)
        {
            ExecutionCounter executionCounter = new ExecutionCounter();
            WalkTree(beforeDescending, afterAscending, executionCounter, parallel);
        }

        public virtual void WalkTree(Action<NWayTreeStorage<T>> beforeDescending, Action<NWayTreeStorage<T>> afterAscending, ExecutionCounter executionCounter, Func<NWayTreeStorage<T>, bool> parallel = null)
        {
            executionCounter.Increment();
            beforeDescending?.Invoke(this);
            afterAscending?.Invoke(this);
            executionCounter.Decrement();
        }

        public virtual void WalkTree(Action<NWayTreeStorage<T>> action, Func<NWayTreeStorage<T>, bool> parallel = null)
        {
            ExecutionCounter executionCounter = new ExecutionCounter();
            WalkTree(action, executionCounter, parallel);
        }

        public virtual void WalkTree(Action<NWayTreeStorage<T>> action, ExecutionCounter executionCounter, Func<NWayTreeStorage<T>, bool> parallel = null)
        {
            executionCounter.Increment();
            action(this);
            executionCounter.Decrement();
        }

        public string ToTreeString(string branchWord)
        {
            StringBuilder s = new StringBuilder();
            ToTreeString(s, null, 0, branchWord);
            return s.ToString();
        }

        internal virtual void ToTreeString(StringBuilder s, int? branch, int level, string branchWord)
        {
            for (int i = 0; i < level * 5; i++)
                s.Append(" ");
            if (branch == null)
                s.Append("Root");
            else
                s.Append($"{branchWord} {branch}");
            s.Append(": ");
            s.Append($"{StoredValue}");
            s.Append(Environment.NewLine);
        }
    }
}
