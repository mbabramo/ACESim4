using System;
using System.Collections.Generic;
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

        public NWayTreeStorageInternal(int numBranches = 0)
        {
            // Branches will automatically expand as necessary to fit. 
            Branches = null; // initialize to null
        }

        public override bool IsLeaf()
        {
            return Branches == null;
        }

        private int AdjustedIndex(byte index) => index - (ZeroBased ? 0 : 1);

        public override NWayTreeStorage<T> GetBranch(byte index)
        {
            var adjustedIndex = AdjustedIndex(index);
            ConfirmAdjustedIndex(adjustedIndex);
            return Branches[adjustedIndex];
        }

        public override void SetBranch(byte index, NWayTreeStorage<T> tree)
        {
            var adjustedIndex = AdjustedIndex(index);
            ConfirmAdjustedIndex(adjustedIndex);
            lock (this)
            {
                Branches[adjustedIndex] = tree;
            }
        }

        private void ConfirmAdjustedIndex(int adjustedIndex)
        {
            if (Branches == null)
                Branches = new NWayTreeStorage<T>[adjustedIndex + 1];
            else if (!(adjustedIndex < Branches.Length))
            {
                NWayTreeStorage<T>[] branchesReplacement = new NWayTreeStorage<T>[adjustedIndex + 1];
                for (int i = 0; i < Branches.Length; i++)
                    branchesReplacement[i] = Branches[i];
                Branches = branchesReplacement;
            }
        }

        public NWayTreeStorage<T> GetNode(IEnumerator<byte> restOfSequence)
        {
            NWayTreeStorage<T> tree = this;
            bool moreInSequence = restOfSequence.MoveNext();
            while (moreInSequence)
            {
                var previous = ((NWayTreeStorageInternal<T>)tree);
                tree = ((NWayTreeStorageInternal<T>)tree).GetBranch(restOfSequence.Current);
                if (tree == null)
                    tree = previous.AddBranch(restOfSequence.Current, true);
                moreInSequence = restOfSequence.MoveNext();
            }
            return tree;
        }

        public T GetValue(IEnumerator<byte> restOfSequence)
        {
            return GetNode(restOfSequence).StoredValue;
        }

        public NWayTreeStorage<T> SetValueIfNotSet(IEnumerator<byte> restOfSequence, bool historyComplete, Func<T> setter)
        {
            NWayTreeStorage<T> node = GetNode(restOfSequence);
            if (node.StoredValue == null || node.StoredValue.Equals(default(T)))
                node.StoredValue = setter();
            return node;
        }

        public NWayTreeStorage<T> SetValue(IEnumerator<byte> restOfSequence, bool historyComplete, T valueToAdd)
        {
            // the logic here is more complicated because we will use NWayTreeStorage<T> for leaf nodes if historyComplete is set.
            bool anyInSequence = restOfSequence.MoveNext();
            if (!anyInSequence)
            {
                StoredValue = valueToAdd;
                return this;
            }
            else
            {
                return SetValueHelper(restOfSequence, historyComplete, valueToAdd);
            }
        }

        private NWayTreeStorage<T> SetValueHelper(IEnumerator<byte> restOfSequence, bool historyComplete, T valueToAdd)
        {
            byte nextInSequence = restOfSequence.Current;
            bool anotherExistsAfterNext = restOfSequence.MoveNext();
            NWayTreeStorage<T> nextTree = GetBranch(nextInSequence);
            if (nextTree == null)
            {
                bool mayBeInternal = anotherExistsAfterNext || !historyComplete;
                nextTree = AddBranch(nextInSequence, mayBeInternal);
            }
            if (anotherExistsAfterNext)
                return ((NWayTreeStorageInternal<T>)nextTree).SetValueHelper(restOfSequence, historyComplete, valueToAdd);
            else
            {
                nextTree.StoredValue = valueToAdd;
                return nextTree;
            }
        }

        private NWayTreeStorage<T> AddBranch(byte index, bool mayBeInternal)
        {
            NWayTreeStorage<T> nextTree;
            if (mayBeInternal)
                nextTree = new NWayTreeStorageInternal<T>();
            else
            {
                nextTree = new NWayTreeStorage<T>(); // leaf node for last item in history; having a separate type saves space on Branches.
            }
            SetBranch(index, nextTree);
            return nextTree;
        }
    }
}
