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
        public NWayTreeStorageInternal<T> Parent;

        private T _StoredValue;
        public T StoredValue
        {
            get
            {
                lock (this)
                    return _StoredValue;
            }
            set
            {
                lock (this)
                {
                    if (_StoredValue == null || _StoredValue.Equals(default(T)))
                        _StoredValue = value;
                }
            }
        }

        public NWayTreeStorage(NWayTreeStorageInternal<T> parent)
        {
            Parent = parent;
        }

        public virtual List<byte> GetSequenceToHere(NWayTreeStorage<T> child = null)
        {
            List<byte> p = Parent?.GetSequenceToHere(this) ?? new List<byte>();
            return p;
        }

        public string SequenceToHereString => String.Join(",", GetSequenceToHere());

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
