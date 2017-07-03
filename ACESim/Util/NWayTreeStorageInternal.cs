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
        
        public NWayTreeStorageInternal(NWayTreeStorageInternal<T> parent, int numBranches = 0) : base(parent)
        {
            // Branches will automatically expand as necessary to fit. 
            Branches = null; // initialize to null
        }

        public override List<byte> GetActionSequence(NWayTreeStorage<T> child = null)
        {
            lock (treelock)
            {
                List<byte> p = base.GetActionSequence(child);
                if (child != null)
                {
                    for (int i = 0; i < Branches.Length; i++)
                        if (Branches[i] == child)
                            p.Add((byte)(i + 1));
                }
                return p;
            }
        }

        public override bool IsLeaf()
        {
            lock (treelock)
            {
                return Branches == null;
            }
        }

        private int AdjustedIndex(byte index) => index - (ZeroBased ? 0 : 1);

        public override NWayTreeStorage<T> GetBranch(byte index)
        {
            lock (treelock)
            {
                var adjustedIndex = AdjustedIndex(index);
                ConfirmAdjustedIndex(adjustedIndex);
                return Branches[adjustedIndex];
            }
        }

        public override void SetBranch(byte index, NWayTreeStorage<T> tree)
        {
            lock (treelock)
            {
                var adjustedIndex = AdjustedIndex(index);
                ConfirmAdjustedIndex(adjustedIndex);
                Branches[adjustedIndex] = tree;
            }
        }

        public static HashSet<string> DEBUG_HS = new HashSet<string>();
        public static bool DEBUG_BlockAdd = false;
        private void ConfirmAdjustedIndex(int adjustedIndex)
        {
            lock (treelock)
            {
                if (Branches == null || !(adjustedIndex < Branches.Length))
                {
                    string DEBUGstring = ActionSequenceString + "," + (adjustedIndex + 1).ToString();
                    bool DEBUG_done = DEBUG_HS.Contains(DEBUGstring);
                    if (DEBUG_BlockAdd)
                        throw new Exception();
                    lock (DEBUG_HS)
                        DEBUG_HS.Add(DEBUGstring);
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
            }
        }

        public unsafe NWayTreeStorage<T> GetNode(byte* restOfSequence)
        {
            lock (treelock)
            {
                NWayTreeStorage<T> tree = this;
                bool moreInSequence = *restOfSequence != 255;
                while (moreInSequence)
                {
                    var previous = ((NWayTreeStorageInternal<T>)tree);
                    tree = ((NWayTreeStorageInternal<T>)tree).GetBranch(*restOfSequence);
                    if (tree == null)
                        tree = previous.AddBranch(*restOfSequence, true);
                    restOfSequence++;
                    moreInSequence = *restOfSequence != 255;
                }
                return tree;
            }
        }

        public unsafe T GetValue(byte* restOfSequence)
        {
            lock (treelock)
                return GetNode(restOfSequence).StoredValue;
        }

        public unsafe NWayTreeStorage<T> SetValueIfNotSet(byte* restOfSequence, bool historyComplete, Func<T> setter)
        {
            lock (treelock)
            {
                NWayTreeStorage<T> node = GetNode(restOfSequence);
                if (node.StoredValue == null || node.StoredValue.Equals(default(T)))
                    node.StoredValue = setter();
                return node;
            }
        }

        public unsafe NWayTreeStorage<T> SetValue(byte* restOfSequence, bool historyComplete, T valueToAdd)
        {
            lock (treelock)
            {
                // the logic here is more complicated because we will use NWayTreeStorage<T> for leaf nodes if historyComplete is set.
                bool anyInSequence = *restOfSequence != 255;
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
        }

        private unsafe NWayTreeStorage<T> SetValueHelper(byte* restOfSequence, bool historyComplete, T valueToAdd)
        {
            lock (treelock)
            {
                byte nextInSequence = *restOfSequence;
                restOfSequence++;
                bool anotherExistsAfterNext = *restOfSequence != 255;
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
        }

        private NWayTreeStorage<T> AddBranch(byte index, bool mayBeInternal)
        {
            lock (treelock)
            {
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
        }
    }
}
