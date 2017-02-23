﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESim
{
    public class NWayTreeStorageInternal<T> : NWayTreeStorage<T>
    {
        public bool ZeroBased;
        public NWayTreeStorage<T>[] Branches;

        public NWayTreeStorageInternal(bool zeroBased, int numBranches)
        {
            ZeroBased = zeroBased;
            Branches = new NWayTreeStorageInternal<T>[numBranches]; // initialize to null
        }

        public NWayTreeStorage<T> GetChildTree(byte index)
        {
            return Branches[index - (ZeroBased ? 0 : 1)];
        }

        public T GetValue(IEnumerator<byte> restOfSequence)
        {
            NWayTreeStorage<T> tree = this;
            bool moreInSequence = restOfSequence.MoveNext();
            while (moreInSequence)
            {
                moreInSequence = restOfSequence.MoveNext();
                tree = ((NWayTreeStorageInternal<T>)tree).GetChildTree(restOfSequence.Current);
            }
            return tree.StoredValue;
        }

        public void AddValue(IEnumerator<byte> restOfSequence, IEnumerator<byte> numberBranchesSequence, bool historyComplete, T valueToAdd)
        {
            bool anyInSequence = restOfSequence.MoveNext();
            bool anyInBranchSequence = numberBranchesSequence.MoveNext();
            if (!anyInSequence)
                StoredValue = valueToAdd;
            else
            {
                if (numberBranchesSequence.Current != Branches.Length)
                    throw new Exception("Inconsistent number of branches.");
                AddValueHelper(restOfSequence, numberBranchesSequence, historyComplete, valueToAdd);
            }
        }

        private void AddValueHelper(IEnumerator<byte> restOfSequence, IEnumerator<byte> numberBranchesSequence, bool historyComplete, T valueToAdd)
        {
            byte nextInSequence = restOfSequence.Current;
            bool anotherExistsAfterNext = restOfSequence.MoveNext();
            bool anotherBranchesExistsAfterNext = numberBranchesSequence.MoveNext();
            if (anotherExistsAfterNext != anotherBranchesExistsAfterNext)
                throw new Exception("Inconsistent sequence lengths.");
            NWayTreeStorage<T> nextTree = GetChildTree(nextInSequence);
            if (nextTree == null)
            {
                if (anotherExistsAfterNext || !historyComplete)
                    nextTree = new NWayTreeStorageInternal<T>(ZeroBased, numberBranchesSequence.Current);
                else
                {
                    nextTree = new NWayTreeStorage<T>(); // leaf node for last item in history
                    nextTree.StoredValue = valueToAdd;
                    return;
                }
                Branches[nextInSequence - (ZeroBased ? 0 : 1)] = nextTree;
            }
            if (anotherExistsAfterNext)
                ((NWayTreeStorageInternal<T>)nextTree).AddValueHelper(restOfSequence, numberBranchesSequence, historyComplete, valueToAdd);
            else
                StoredValue = valueToAdd;
        }
    }
}
