using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ACESim
{
    [Serializable]
    public class DropInfo
    {
        public bool DroppedByPlaintiff;
        public DropOrDefaultPeriod DropOrDefaultPeriod;
        public int? DroppedAfterBargainingRound;
    }
}
