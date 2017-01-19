using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ACESim
{
    [Serializable]
    public enum Operation
    {
        equals, 
        doesNotEqual, 
        greaterThan, 
        lessThan, 
        greaterThanOrEqualTo, 
        lessThanOrEqualTo,
        or,
        and
    }
}
