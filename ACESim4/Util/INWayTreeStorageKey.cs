using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESim
{
    public unsafe interface INWayTreeStorageKey
    {
        byte PrefaceByte { get; }
        byte Element(int i);
    }
}
