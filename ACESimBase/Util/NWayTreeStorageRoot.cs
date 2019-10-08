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
        public Dictionary<NWayTreeStorageKey, NWayTreeStorage<T>> HashTableStorage;

        public static bool EnableUseDictionary = false;

        public NWayTreeStorageRoot(NWayTreeStorageInternal<T> parent, int numBranches, bool useDictionary) : base(parent, numBranches)
        {
            if (useDictionary && EnableUseDictionary)
                HashTableStorage = new Dictionary<NWayTreeStorageKey, NWayTreeStorage<T>>();
        }

        public NWayTreeStorage<T> SetValueIfNotSet(NWayTreeStorageKeyStackOnly keyStackOnly, bool historyComplete, Func<T> setter)
        {
            lock (this)
            {
                NWayTreeStorageKey key = keyStackOnly.ToThreadOnlyKey();
                NWayTreeStorage<T> node = GetNode_CreatingRestIfNecessary(keyStackOnly.PrefaceByte, key.Sequence, out bool created);
                if (created && HashTableStorage != null)
                {
                    HashTableStorage.Add(keyStackOnly.ToStorable(), node);
                    //Debug.WriteLine($"Added {key}");
                }
                if (node.StoredValue == null || node.StoredValue.Equals(default(T)))
                    node.StoredValue = setter();
                return node;
            }
        }

        public T GetValue(NWayTreeStorageKeyStackOnly keyStackOnly)
        {
            var node = GetNode(keyStackOnly);
            if (node == null)
                return default(T);
            return node.StoredValue;
        }

        private  NWayTreeStorage<T> GetNode_CreatingRestIfNecessary(byte prefaceByte, byte[] restOfSequence, out bool created)
        {
            NWayTreeStorageInternal<T> tree = (NWayTreeStorageInternal<T>)GetBranch(prefaceByte);
            if (tree == null)
            {
                AddBranch(prefaceByte, true);
                tree = (NWayTreeStorageInternal<T>)GetBranch(prefaceByte);
            }
            return tree.GetNode(restOfSequence, true, out created);
        }

        private  NWayTreeStorage<T> GetNode(NWayTreeStorageKeyStackOnly keyStackOnly)
        {
            var key = keyStackOnly.ToThreadOnlyKey();
            if (HashTableStorage != null)
            {
                bool found = HashTableStorage.TryGetValue(key, out NWayTreeStorage<T> foundNode);
                if (found)
                    return foundNode;
            }
            NWayTreeStorageInternal<T> tree = (NWayTreeStorageInternal<T>)GetBranch(keyStackOnly.PrefaceByte);
            if (tree == null)
            {
                AddBranch(keyStackOnly.PrefaceByte, true);
                tree = (NWayTreeStorageInternal<T>)GetBranch(keyStackOnly.PrefaceByte);
            }
            return tree.GetNode(key.Sequence, false, out _);
        }
    }
}