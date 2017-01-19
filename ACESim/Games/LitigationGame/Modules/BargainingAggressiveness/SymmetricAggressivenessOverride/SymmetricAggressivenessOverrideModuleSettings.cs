using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESim
{
    [Serializable]
    public class SymmetricAggressivenessOverrideModuleSettings
    {
        public int AggressivenessModuleNumber;
        public bool DivideBargainingIntoMinirounds;
        public int NumBargainingMinirounds;
    }
}
