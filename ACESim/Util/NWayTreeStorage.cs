using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESim
{
    public class NWayTreeStorage<T>
    {
        public T StoredValue;

        public virtual NWayTreeStorage<T> GetChildTree(byte index)
        {
            return null; // leaf node has no child; internal node overrides this
        }
    }
}
