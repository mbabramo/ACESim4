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
        private T _StoredValue;
        public T StoredValue
        {
            get
            {
                return _StoredValue;
            }
            set
            {
                lock (this)
                    _StoredValue = value;
            }
        }

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
