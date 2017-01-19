using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ACESim
{
    [Serializable]
    class InformationGameProgressInfo : GameProgressInfo
    {
        public override GameProgressInfo DeepCopy()
        {
            InformationGameProgressInfo copy = new InformationGameProgressInfo();
            return copy;
        }
    }
}
