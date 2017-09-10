using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESim
{
    [Serializable]
    public class NWayTreeStorageRoot<T> : NWayTreeStorageInternal<T>
    {
        // We allow for fast lookup at the root by using a very simple hash table. If there is a collision, we ignore the hash table and just use the tree
        public Dictionary<NWayTreeStorageKey, NWayTreeStorage<T>> Dictionary;

        public NWayTreeStorageRoot(NWayTreeStorageInternal<T> parent, int numBranches, bool useDictionary) : base(parent, numBranches)
        {
            if (useDictionary)
                Dictionary = new Dictionary<NWayTreeStorageKey, NWayTreeStorage<T>>();
        }

        public unsafe NWayTreeStorage<T> SetValueIfNotSet(NWayTreeStorageKey key, bool historyComplete, Func<T> setter)
        {
            lock (this)
            {
                NWayTreeStorage<T> node = GetNode_CreatingRestIfNecessary(key.PrefaceByte, key.Sequence, out bool created);
                if (created)
                    Dictionary?.Add(key, node);
                if (node.StoredValue == null || node.StoredValue.Equals(default(T)))
                    node.StoredValue = setter();
                return node;
            }
        }

        public unsafe T GetValue(NWayTreeStorageKey key)
        {
            var node = GetNode(key);
            if (node == null)
                return default(T);
            return node.StoredValue;
        }

        private unsafe NWayTreeStorage<T> GetNode_CreatingRestIfNecessary(byte prefaceByte, byte* restOfSequence, out bool created)
        {
            NWayTreeStorageInternal<T> tree = (NWayTreeStorageInternal<T>)GetBranch(prefaceByte);
            if (tree == null)
            {
                AddBranch(prefaceByte, true);
                tree = (NWayTreeStorageInternal<T>)GetBranch(prefaceByte);
            }
            return tree.GetNode(restOfSequence, true, out created);
        }

        private unsafe NWayTreeStorage<T> GetNode(NWayTreeStorageKey key)
        {
            if (Dictionary != null)
            {
                bool found = Dictionary.TryGetValue(key, out NWayTreeStorage<T> foundNode);
                if (found)
                    return foundNode;
            }
            NWayTreeStorageInternal<T> tree = (NWayTreeStorageInternal<T>)GetBranch(key.PrefaceByte);
            if (tree == null)
            {
                AddBranch(key.PrefaceByte, true);
                tree = (NWayTreeStorageInternal<T>)GetBranch(key.PrefaceByte);
            }
            return tree.GetNode(key.Sequence, false, out _);
        }
    }
}