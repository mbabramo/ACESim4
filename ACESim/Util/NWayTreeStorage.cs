using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESim
{
    [Serializable]
    public class NWayTreeStorage<T>
    {

        public static object treelock = new object(); // DEBUG

        public NWayTreeStorageInternal<T> Parent;

        private T _StoredValue;
        public T StoredValue
        {
            get
            {
                lock (treelock)
                    return _StoredValue;
            }
            set
            {
                lock (treelock)
                    _StoredValue = value;
            }
        }

        public static int DEBUGCount;

        public NWayTreeStorage(NWayTreeStorageInternal<T> parent)
        {
            lock (treelock)
            {
                Parent = parent;
                System.Threading.Interlocked.Increment(ref DEBUGCount);
            }
        }

        public virtual List<byte> GetActionSequence(NWayTreeStorage<T> child = null)
        {
            lock (treelock)
            {
                List<byte> p = Parent?.GetActionSequence(this) ?? new List<byte>();
                return p;
            }
        }

        public string ActionSequenceString => String.Join(",", GetActionSequence());

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
    }
}
