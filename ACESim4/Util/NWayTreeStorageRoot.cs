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

        public NWayTreeStorageRoot(NWayTreeStorageInternal<T> parent, int numBranches, bool useDictionary) : base(parent, numBranches)
        {
            if (useDictionary)
                HashTableStorage = new Dictionary<NWayTreeStorageKey, NWayTreeStorage<T>>();
        }

        public unsafe NWayTreeStorage<T> SetValueIfNotSet(NWayTreeStorageKey key, bool historyComplete, Func<T> setter)
        {
            lock (this)
            {
                NWayTreeStorage<T> node = GetNode_CreatingRestIfNecessary(key.PrefaceByte, key.Sequence, out bool created);
                if (created && HashTableStorage != null)
                {
                    HashTableStorage.Add(key, node);
                    Debug.WriteLine($"Added {key}");

                    bool found = HashTableStorage.TryGetValue(key, out NWayTreeStorage<T> foundNode); // DEBUG
                    if (!found)
                        throw new Exception();
                }
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
            if (HashTableStorage != null)
            {
                bool found = HashTableStorage.TryGetValue(key, out NWayTreeStorage<T> foundNode);
                if (found)
                    return foundNode;
                byte* DEBUGsequence = stackalloc byte[15];
                DEBUGsequence[0] = 7;
                DEBUGsequence[1] = 1;
                DEBUGsequence[2] = 2;
                DEBUGsequence[3] = 255;
                NWayTreeStorageKey test = new NWayTreeStorageKey(11, DEBUGsequence);
                if (key.PrefaceByte == 11)
                {
                    foreach (var key2 in HashTableStorage.Keys)
                        Debug.WriteLine("Existing " + key2);
                }
                Debug.WriteLine($"Failed to find {key} {key.Equals(test)}");
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