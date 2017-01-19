using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.ComponentModel.Composition;

namespace ACESim
{
    [Serializable]
    public class SimplisticBargainingInputs : BargainingInputs
    {
        public double Spread;
    }
}
