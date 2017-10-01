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
        public Dictionary<INWayTreeStorageKey, NWayTreeStorage<T>> HashTableStorage;

        public static bool EnableUseDictionary = false;

        public NWayTreeStorageRoot(NWayTreeStorageInternal<T> parent, int numBranches, bool useDictionary) : base(parent, numBranches)
        {
            if (useDictionary && EnableUseDictionary)
                HashTableStorage = new Dictionary<INWayTreeStorageKey, NWayTreeStorage<T>>();
        }

        public unsafe NWayTreeStorage<T> SetValueIfNotSet(NWayTreeStorageKeyUnsafeStackOnly keyUnsafeStackOnly, bool historyComplete, Func<T> setter)
        {
            lock (this)
            {
                NWayTreeStorage<T> node = GetNode_CreatingRestIfNecessary(keyUnsafeStackOnly.PrefaceByte, keyUnsafeStackOnly.Sequence, out bool created);
                if (created && HashTableStorage != null)
                {
                    HashTableStorage.Add(keyUnsafeStackOnly.ToSafe(), node);
                    //Debug.WriteLine($"Added {key}");
                }
                if (node.StoredValue == null || node.StoredValue.Equals(default(T)))
                    node.StoredValue = setter();
                return node;
            }
        }

        public unsafe T GetValue(NWayTreeStorageKeyUnsafeStackOnly keyUnsafeStackOnly)
        {
            var node = GetNode(keyUnsafeStackOnly);
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

        private unsafe NWayTreeStorage<T> GetNode(NWayTreeStorageKeyUnsafeStackOnly keyUnsafeStackOnly)
        {
            if (HashTableStorage != null)
            {
                bool found = HashTableStorage.TryGetValue(keyUnsafeStackOnly, out NWayTreeStorage<T> foundNode);
                if (found)
                    return foundNode;
            }
            NWayTreeStorageInternal<T> tree = (NWayTreeStorageInternal<T>)GetBranch(keyUnsafeStackOnly.PrefaceByte);
            if (tree == null)
            {
                AddBranch(keyUnsafeStackOnly.PrefaceByte, true);
                tree = (NWayTreeStorageInternal<T>)GetBranch(keyUnsafeStackOnly.PrefaceByte);
            }
            return tree.GetNode(keyUnsafeStackOnly.Sequence, false, out _);
        }
    }
}