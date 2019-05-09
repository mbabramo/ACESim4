using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESim
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

        public override string ToString(int level)
        {
            StringBuilder b = new StringBuilder();
            b.Append(base.ToString(level));
            if (Branches != null)
                foreach (var branch in Branches)
                    b.Append(branch.ToString(level + 1));
            return b.ToString();
        }

        public override List<byte> GetSequenceToHere(NWayTreeStorage<T> child = null)
        {
            List<byte> p = base.GetSequenceToHere(child);
            if (child != null)
            {
                for (int i = 0; i < Branches.Length; i++)
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
            if (ParallelEnabled)
            {
                CompleteSetBranchWithLock(index, tree);
            }
            else
            {
                CompleteSetBranch(index, tree);
            }
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

        internal unsafe NWayTreeStorage<T> GetNode(byte* restOfSequence, bool createIfNecessary, out bool created)
        {
            created = false;
            NWayTreeStorageInternal<T> tree = this;
            bool moreInSequence = *restOfSequence != 255;
            while (moreInSequence)
            {
                var previous = tree;
                tree = (NWayTreeStorageInternal<T>) tree.GetBranch(*restOfSequence);
                if (tree == null)
                {
                    if (createIfNecessary)
                    {
                        tree = (NWayTreeStorageInternal<T>)previous.AddBranch(*restOfSequence, true);
                        created = true;
                    }
                    else
                        return null;
                }
                restOfSequence++;
                moreInSequence = *restOfSequence != 255;
            }
            return tree;
        }

        //public unsafe NWayTreeStorage<T> SetValue(byte* restOfSequence, bool historyComplete, T valueToAdd)
        //{
        //    // the logic here is more complicated because we will use NWayTreeStorage<T> for leaf nodes if historyComplete is set.
        //    bool anyInSequence = *restOfSequence != 255;
        //    if (!anyInSequence)
        //    {
        //        StoredValue = valueToAdd;
        //        return this;
        //    }
        //    else
        //    {
        //        return SetValueHelper(restOfSequence, historyComplete, valueToAdd);
        //    }
        //}

        //private unsafe NWayTreeStorage<T> SetValueHelper(byte* restOfSequence, bool historyComplete, T valueToAdd)
        //{
        //    lock (this)
        //    {
        //        byte nextInSequence = *restOfSequence;
        //        restOfSequence++;
        //        bool anotherExistsAfterNext = *restOfSequence != 255;
        //        NWayTreeStorage<T> nextTree = GetBranch(nextInSequence);
        //        if (nextTree == null)
        //        {
        //            lock (this)
        //            {
        //                nextTree = GetBranch(nextInSequence); // check again, now that we're in the lock
        //                if (nextTree == null)
        //                {
        //                    bool mayBeInternal = anotherExistsAfterNext || !historyComplete;
        //                    nextTree = AddBranch(nextInSequence, mayBeInternal);
        //                }
        //            }
        //        }
        //        if (anotherExistsAfterNext)
        //            return ((NWayTreeStorageInternal<T>)nextTree).SetValueHelper(restOfSequence, historyComplete, valueToAdd);
        //        else
        //        {
        //            nextTree.StoredValue = valueToAdd;
        //            return nextTree;
        //        }
        //    }
        //}

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

        public override void WalkTree(Action<NWayTreeStorage<T>> action, Func<NWayTreeStorage<T>, bool> parallel = null)
        {
            action(this);
            if (Branches != null)
            {
                if (parallel == null || parallel(this) == false)
                {
                    for (int branch = 1; branch <= Branches.Length; branch++)
                    {
                        if (Branches[branch - 1] != null && !Branches[branch - 1].Equals(default(T)))
                            Branches[branch - 1].WalkTree(action, parallel);
                    }
                }
                else
                {
                    List<int> branches = Enumerable.Range(1, Branches.Length).ToList();
                    RandomSubset.Shuffle(branches, 5); // DEBUG
                    foreach (int branch in branches) // DEBUG
                    // DEBUGParallel.ForEach(branches, branch =>
                    {
                        if (Branches[branch - 1] != null && !Branches[branch - 1].Equals(default(T)))
                            Branches[branch - 1].WalkTree(action, parallel);
                    };
                }
            }
        }

        internal override void ToTreeString(StringBuilder s, int? branch, int level, string branchWord)
        {
            base.ToTreeString(s, branch, level, branchWord);
            if (Branches != null)
                for (int branch2 = 1; branch2 <= Branches.Length; branch2++)
                {
                    if (Branches[branch2 - 1] != null && !Branches[branch2 - 1].Equals(default(T)))
                        Branches[branch2 - 1].ToTreeString(s, branch2, level + 1, branchWord);
                }

        }
    }
}
