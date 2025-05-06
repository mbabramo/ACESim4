using ACESimBase.Util.Debugging;
using ACESimBase.Util.Parallelization;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ACESimBase.Util.NWayTreeStorage
{
    [Serializable]
    public class NWayTreeStorageInternal<T> : NWayTreeStorage<T>
    {
        public const bool ZeroBased = false; // make this a constant to save space; we could alternatively store this with the tree or pass it to the methods
        public NWayTreeStorage<T>[] Branches;

        public NWayTreeStorageInternal(NWayTreeStorageInternal<T> parent, int numBranches = 0) : base(parent)
        {
            // Branches will automatically expand as necessary to fit. 
            Branches = null; // initialize to null
        }

        public override IEnumerable<NWayTreeStorage<T>> EnumerateNodes(Func<NWayTreeStorage<T>, bool> enumerateThis, Func<NWayTreeStorageInternal<T>, IEnumerable<bool>> enumerateBranches, Action beforeAction = null, Action afterAction = null)
        {
            if (beforeAction != null)
                beforeAction();
            if (enumerateThis(this))
                yield return this;
            int branchIndex = 0;
            if (Branches != null)
                foreach (bool enumerateBranch in enumerateBranches(this))
                {
                    if (enumerateBranch)
                    {
                        foreach (NWayTreeStorage<T> node in Branches[branchIndex].EnumerateNodes(enumerateThis, enumerateBranches))
                            yield return node;
                    }
                    branchIndex++;
                }
            if (afterAction != null)
                afterAction();
        }
        public override void ExecuteActions(Action<T> downTreeAction, Action<T> upTreeAction)
        {
            downTreeAction(StoredValue);
            if (Branches != null)
                foreach (var branch in Branches.Where(x => x != null))
                {
                    //var topOfBranchString = branch.ToString().Split(Environment.NewLine).First();
                    branch.ExecuteActions(downTreeAction, upTreeAction);
                }
            upTreeAction(StoredValue);
        }
        public override void ExecuteActionsOnTree(Action<NWayTreeStorage<T>> downTreeAction, Action<NWayTreeStorage<T>> upTreeAction)
        {
            downTreeAction(this);
            if (Branches != null)
                foreach (var branch in Branches.Where(x => x != null))
                {
                    //var topOfBranchString = branch.ToString().Split(Environment.NewLine).First();
                    branch.ExecuteActionsOnTree(downTreeAction, upTreeAction);
                }
            upTreeAction(this);
        }



        private T GetItem(IEnumerable<byte> branchSequence) => GetNodeAtPath(branchSequence).StoredValue;

        private NWayTreeStorageInternal<T> GetNodeAtPath(IEnumerable<byte> branchSequence)
        {
            var current = this;
            foreach (byte branch in branchSequence)
            {
                if (current.Branches == null || branch >= current.Branches.Length)
                {
                    lock (current)
                    {
                        if (current.Branches == null || branch >= current.Branches.Length)
                        {
                            if (current.Branches == null)
                                current.Branches = new NWayTreeStorage<T>[branch + 1];
                            if (branch >= current.Branches.Length)
                                Array.Resize(ref current.Branches, branch + 1);
                        }
                    }
                }
                var node = current.Branches[branch];
                if (node == null)
                    node = current.Branches[branch] = new NWayTreeStorageInternal<T>(current);
                if (node is NWayTreeStorageInternal<T> internalNode)
                    current = internalNode;
                else
                    throw new NotImplementedException("Cannot use GetNodeAtPath to get item at leaf.");
            }
            return current;
        }

        public T GetOrSetValueAtPath(IEnumerable<byte> branchSequence, Func<T> valueFunc)
        {
            var node = GetNodeAtPath(branchSequence);
            if (node.StoredValue == null)
            {
                lock (node)
                {
                    if (node.StoredValue == null)
                        node.StoredValue = valueFunc();
                }
            }
            return node.StoredValue;
        }

        public override string ToString(int level)
        {
            StringBuilder b = new StringBuilder();
            b.Append(base.ToString(level));
            if (Branches != null)
                foreach (var branch in Branches)
                {
                    if (branch == null)
                        b.AppendLine(new string('\t', level + 1) + "N/A");
                    else
                        b.Append(branch.ToString(level + 1));
                }
            return b.ToString();
        }

        public override List<byte> GetSequenceToHere(NWayTreeStorage<T> child = null)
        {
            List<byte> p = base.GetSequenceToHere(child);
            if (child != null)
            {
                for (int i = 0; i < (Branches?.Length ?? 0); i++)
                    if (Branches[i] == child)
                        p.Add((byte)(i + 1));
            }
            return p;
        }

        public override bool IsLeaf()
        {
            return Branches == null;
        }

        private int AdjustedIndex(byte index) => index - (ZeroBased ? 0 : 1);

        public override NWayTreeStorage<T> GetBranch(byte index)
        {
            var adjustedIndex = AdjustedIndex(index);
            if (ParallelEnabled)
                ConfirmAdjustedIndexWithLock(adjustedIndex);
            else
                ConfirmAdjustedIndex(adjustedIndex);
            if (adjustedIndex == -1)
                throw new NotImplementedException(); // ACESim note: might you have defined a decision where 0 is considered a valid action? don't do that -- always start numbering at 1
            return Branches[adjustedIndex];
        }

        public override void SetBranch(byte index, NWayTreeStorage<T> tree)
        {
#if OUTPUT_HOISTING_INFO
            // quick trace so we can see what happens during the failing test
            TabbedText.WriteLine(
                $"[SET] branch={index}  beforeLen={(Branches?.Length ?? 0)}");
#endif

            if (ParallelEnabled)
            {
                CompleteSetBranchWithLock(index, tree);
            }
            else
            {
                CompleteSetBranch(index, tree);
            }

#if OUTPUT_HOISTING_INFO
            TabbedText.WriteLine(
                $"[SET] branch={index}  afterLen={(Branches?.Length ?? 0)}");
#endif
        }

        private void CompleteSetBranchWithLock(byte index, NWayTreeStorage<T> tree)
        {
            lock (this)
            {
                var adjustedIndex = AdjustedIndex(index);
                ConfirmAdjustedIndex(adjustedIndex);
                Branches[adjustedIndex] = tree;
            }
        }

        private void CompleteSetBranch(byte index, NWayTreeStorage<T> tree)
        {
            var adjustedIndex = AdjustedIndex(index);
            ConfirmAdjustedIndex(adjustedIndex);
            Branches[adjustedIndex] = tree;
        }

        private void ConfirmAdjustedIndexWithLock(int adjustedIndex)
        {
            if (Branches == null)
            {
                lock (this)
                    Branches = new NWayTreeStorage<T>[adjustedIndex + 1];
                return;
            }
            int branchesLength = Branches.Length;
            if (!(adjustedIndex < branchesLength))
            {
                lock (this)
                {
                    branchesLength = Branches.Length; // may have been set while waiting for lock
                    if (branchesLength >= adjustedIndex + 1)
                        return;
                    else if (!(adjustedIndex < branchesLength))
                    {
                        NWayTreeStorage<T>[] branchesReplacement = new NWayTreeStorage<T>[adjustedIndex + 1];
                        for (int i = 0; i < branchesLength; i++)
                            branchesReplacement[i] = Branches[i];
                        Branches = branchesReplacement;
                    }
                }
            }
        }

        private void ConfirmAdjustedIndex(int adjustedIndex)
        {
            if (Branches == null)
            {
                Branches = new NWayTreeStorage<T>[adjustedIndex + 1];
                return;
            }
            int branchesLength = Branches.Length;
            if (!(adjustedIndex < branchesLength))
            {
                NWayTreeStorage<T>[] branchesReplacement = new NWayTreeStorage<T>[adjustedIndex + 1];
                for (int i = 0; i < branchesLength; i++)
                    branchesReplacement[i] = Branches[i];
                Branches = branchesReplacement;
            }
        }

        internal NWayTreeStorage<T> GetNode(byte[] restOfSequence, bool createIfNecessary, out bool created)
        {
            created = false;
            NWayTreeStorageInternal<T> tree = this;
            int i = 0;
            bool moreInSequence = restOfSequence[i] != 255;
            while (moreInSequence)
            {
                var previous = tree;
                tree = (NWayTreeStorageInternal<T>)tree.GetBranch(restOfSequence[i]);
                if (tree == null)
                {
                    if (createIfNecessary)
                    {
                        tree = (NWayTreeStorageInternal<T>)previous.AddBranch(restOfSequence[i], true);
                        created = true;
                    }
                    else
                        return null;
                }
                i++;
                moreInSequence = restOfSequence[i] != 255;
            }
            return tree;
        }

        public NWayTreeStorage<T> AddBranch(byte index, bool mayBeInternal)
        {
            if (ParallelEnabled)
            {
                return CompleteAddBranchWithLock(index, mayBeInternal);
            }
            else
            {
                return CompleteAddBranch(index, mayBeInternal);
            }
        }

        private NWayTreeStorage<T> CompleteAddBranchWithLock(byte index, bool mayBeInternal)
        {
            lock (this)
                return CompleteAddBranch(index, mayBeInternal);
        }

        private NWayTreeStorage<T> CompleteAddBranch(byte index, bool mayBeInternal)
        {
            //Debug.WriteLine(System.Environment.StackTrace);
            //Debug.WriteLine("");
            var existing = GetBranch(index);
            if (existing != null)
                return existing; // this may have been set during the lock
            NWayTreeStorage<T> nextTree;
            if (mayBeInternal)
                nextTree = new NWayTreeStorageInternal<T>(this);
            else
            {
                nextTree = new NWayTreeStorage<T>(this); // leaf node for last item in history; having a separate type saves space on Branches.
            }
            SetBranch(index, nextTree);
            return nextTree;
        }

        public override void WalkTree(Action<NWayTreeStorage<T>> beforeDescending, Action<NWayTreeStorage<T>> afterAscending, ExecutionCounter executionCounter, Func<NWayTreeStorage<T>, bool> parallel = null)
        {
            executionCounter.Increment();
            beforeDescending?.Invoke(this);
            if (Branches != null && Branches.Where(x => x != null).Any())
            {
                if (parallel == null || parallel(this) == false || !executionCounter.ShouldAddTasks(Branches.Count()))
                {
                    WalkTreeSerial(beforeDescending, afterAscending, executionCounter, parallel);
                }
                else
                {
                    WalkTreeParallel(beforeDescending, afterAscending, executionCounter, parallel);
                }
            }
            afterAscending?.Invoke(this);
            executionCounter.Decrement();
        }

        public void BranchSerially<U>(U parameter, Func<NWayTreeStorageInternal<T>, U, (byte branchID, T childBranch, bool isLeaf)[]> subbrancher)
        {
            var branches = subbrancher(this, parameter);
            foreach (var branchInfo in branches)
            {
                if (branchInfo.childBranch != null)
                {
                    var branch = GetBranch(branchInfo.branchID) ?? AddBranch(branchInfo.branchID, !branchInfo.isLeaf);
                    branch.StoredValue = branchInfo.childBranch; // note that this may already be set
                }
            }
            if (Branches != null)
                foreach (var b in Branches.Where(x => x is NWayTreeStorageInternal<T>))
                    ((NWayTreeStorageInternal<T>)b).BranchSerially(parameter, subbrancher);
        }

        public async Task BranchInParallel<U>(bool doParallel, U parameter, Func<NWayTreeStorageInternal<T>, U, (byte branchID, T childBranch, bool isLeaf)[]> subbranchesCreator)
        {
            if (!doParallel)
            {
                BranchSerially(parameter, subbranchesCreator);
                return;
            }
            var newBranches = subbranchesCreator(this, parameter);
            foreach (var branchInfo in newBranches)
            {
                var branch = GetBranch(branchInfo.branchID) ?? AddBranch(branchInfo.branchID, !branchInfo.isLeaf);
                branch.StoredValue = branchInfo.childBranch; // note that this may already be set
            }
            if (Branches != null)
            {
                await Branches.Where(x => x is NWayTreeStorageInternal<T>).ForEachAsync(async b =>
                {
                    await ((NWayTreeStorageInternal<T>)b).BranchInParallel(true, parameter, subbranchesCreator);
                });
            }
        }

        //public async Task CreateBranchesParallel(Func<NWayTreeStorageInternal<T>, (byte, NWayTreeStorage<T>)[]> subbranchesCreator)
        //{
        //    var newBranches = subbranchesCreator(this);
        //    var expandableBranches = newBranches.Where(x => x.Item2 != null && x.Item2 is NWayTreeStorageInternal<T>).ToList();
        //    await Parallelizer.ForEachAsync(expandableBranches, async b =>
        //    {
        //        await ((NWayTreeStorageInternal<T>)b.Item2).CreateBranchesParallel(subbranchesCreator);
        //    });
        //}

        private void WalkTreeParallel(Action<NWayTreeStorage<T>> beforeDescending, Action<NWayTreeStorage<T>> afterAscending, ExecutionCounter executionCounter, Func<NWayTreeStorage<T>, bool> parallel)
        {
            List<int> branches = Enumerable.Range(1, Branches.Length).ToList();
            //The commented out code randomizes order without executing code simultaneously. You can also comment out just the first to run in order instead of in parallel.
            //RandomSubset.Shuffle(branches, 5); 
            //foreach (int branch in branches)
            if (Branches.Count() < 10)
            {
                var tasks = Branches.Skip(1).Where(x => x != null)
                    .Select(x => Task.Factory.StartNew(() => x.WalkTree(beforeDescending, afterAscending, executionCounter, parallel))).ToArray();
                Branches.First().WalkTree(beforeDescending, afterAscending, executionCounter, parallel); // this is a simple way of doing some work while waiting for the remaining tasks -- better would be to make everything async.
                Task.WaitAll(tasks);
            }
            else
            {
                Parallel.ForEach(branches, branchIndexPlusOne =>
                {
                    NWayTreeStorage<T> branch = Branches[branchIndexPlusOne - 1];
                    if (branch != null && !branch.Equals(default(T)))
                        branch.WalkTree(beforeDescending, afterAscending, executionCounter, parallel);
                });
            }
        }

        private void WalkTreeSerial(Action<NWayTreeStorage<T>> beforeDescending, Action<NWayTreeStorage<T>> afterAscending, ExecutionCounter executionCounter, Func<NWayTreeStorage<T>, bool> parallel)
        {
            for (int branchIndexPlusOne = 1; branchIndexPlusOne <= Branches.Length; branchIndexPlusOne++)
            {
                NWayTreeStorage<T> branch = Branches[branchIndexPlusOne - 1];
                if (branch != null && !branch.Equals(default(T)))
                    branch.WalkTree(beforeDescending, afterAscending, executionCounter, parallel);
            }
        }

        public override void WalkTree(Action<NWayTreeStorage<T>> action, ExecutionCounter executionCounter, Func<NWayTreeStorage<T>, bool> parallel = null) => WalkTree(action, null, executionCounter, parallel);

        internal override void ToTreeString(StringBuilder s, int? branch, int level, Func<T, string> branchWordFunc)
        {
            base.ToTreeString(s, branch, level, branchWordFunc);
            if (Branches != null)
                for (int branch2 = 1; branch2 <= Branches.Length; branch2++)
                {
                    if (Branches[branch2 - 1] != null && !Branches[branch2 - 1].Equals(default(T)))
                        Branches[branch2 - 1].ToTreeString(s, branch2, level + 1, branchWordFunc);
                }
        }

        public override void WalkTreeWithPredicate(Func<NWayTreeStorage<T>, bool> shouldRecurse, Action<NWayTreeStorage<T>> processNode)
        {
            processNode(this);
            if (shouldRecurse(this) && Branches != null)
            {
                foreach (var branch in Branches)
                {
                    if (branch != null)
                        branch.WalkTreeWithPredicate(shouldRecurse, processNode);
                }
            }
        }


    }
}
